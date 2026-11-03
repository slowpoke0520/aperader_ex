using ApeRadar.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodsoft.WowsReplaysUnpack;
using Nodsoft.WowsReplaysUnpack.Core.Exceptions;
using Nodsoft.WowsReplaysUnpack.Core.Entities;
using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using Nodsoft.WowsReplaysUnpack.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed class NodsoftReplayParserAdapter : IReplayParser, IDisposable
    {
        internal const string AdvancedSchemaVersion = "7";
        private readonly ServiceProvider services;
        private readonly IReplayUnpackerFactory factory;

        public NodsoftReplayParserAdapter()
        {
            ServiceCollection collection = new();
            collection.AddLogging();
            collection.AddWowsReplayUnpacker(builder =>
                builder.AddReplayController<BattleHistoryReplayController, BattleHistoryReplay>());
            collection.AddTransient<IReplayDataParser, BattleResultsReplayDataParser>();
            collection.AddTransient<IDefinitionStore, ReplayDefinitionStore>();
            services = collection.BuildServiceProvider();
            factory = services.GetRequiredService<IReplayUnpackerFactory>();
        }

        public string ParserVersion => $"{typeof(IReplayUnpackerFactory).Assembly.GetName().Version?.ToString() ?? "unknown"}/aperadar-{AdvancedSchemaVersion}";

        public async Task<ReplayParseResult> ParseAsync(string path, CancellationToken cancellationToken = default)
        {
            string hash = await CalculateHashAsync(path, cancellationToken);
            ReplayHeader header = await ReadHeaderAsync(path, cancellationToken);
            if (!IsRandomMode(header.Mode))
            {
                return Create(header, hash, ReplayParseStatus.Invalid, "NotRandomBattle", "Only Random Battles are tracked.");
            }

            try
            {
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, true);
                BattleHistoryReplay replay = await Task.Run(
                    () => factory.GetUnpacker<BattleHistoryReplay>().Unpack(stream, new ReplayUnpackerOptions()),
                    cancellationToken);
                header = header.Merge(replay.ArenaInfo, replay.MapName, replay.ClientVersion);

                ReplayBattleResults? results = FindValidatedBattleResults(replay, header.AccountName, header.ShipId, out string resultsError);
                if (results != null) header = header with { BattleKey = $"arena:{results.ArenaId}" };
                ReplayMetrics metrics = FindResultMetrics(replay, results);
                BattleAdvancedMetrics advanced = FindAdvancedMetrics(replay, results);
                IReadOnlyList<BattleDamageBreakdown> breakdowns = FindDamageBreakdowns(replay);
                bool exitedAfterDeath = results == null && !replay.HasBattleEnded && advanced.Survived == false;
                bool complete = results != null;
                string errorCode = complete
                    ? results!.DamageTakenError ?? (replay.EntityParsingError.Length > 0 ? "ReplayEventDetailsUnavailable" : "")
                    : resultsError.Length > 0 ? resultsError
                    : replay.EntityParsingError.Length > 0 ? "ReplayEventDetailsUnavailable"
                    : exitedAfterDeath ? "BattleExitedAfterDeath"
                    : !replay.HasBattleEnded ? "BattleNotFinished"
                    : !string.IsNullOrWhiteSpace(GetMetricError(replay)) ? "ReplayMetricsInvalid"
                    : "BattleResultsMissing";
                string errorMessage = complete
                    ? results!.DamageTakenError != null ? "Battle results were imported; damage taken could not be verified and remains unavailable."
                        : replay.EntityParsingError.Length > 0 ? "Battle results were imported; event-derived details could not be decoded and remain unavailable." : ""
                    : resultsError.Length > 0 ? "Battle results could not be verified. Available event statistics were retained as partial data."
                    : replay.EntityParsingError.Length > 0 ? "Event details could not be decoded and no verified battle result is available."
                    : exitedAfterDeath
                        ? "The player ship was destroyed before the recording ended. Damage and frags at exit were retained; the final result will be checked after the battle can finish."
                    : !replay.HasBattleEnded
                        ? "Replay metadata was imported, but the recording ended before the battle result was received."
                        : !string.IsNullOrWhiteSpace(GetMetricError(replay))
                            ? $"Replay metadata was imported, but battle metrics could not be decoded: {GetMetricError(replay)}"
                            : "Replay metadata was imported; complete battle metrics were not available.";
                return new ReplayParseResult
                {
                    Status = complete ? ReplayParseStatus.Parsed : ReplayParseStatus.Partial,
                    FileHash = hash,
                    GameVersion = header.GameVersion,
                    ParserVersion = ParserVersion,
                    BattleKey = header.BattleKey,
                    StartedAt = header.StartedAt,
                    Mode = header.Mode,
                    MapName = header.MapName,
                    AccountName = header.AccountName,
                    ShipId = header.ShipId,
                    RosterSignature = header.RosterSignature,
                    Result = metrics.Result,
                    Damage = metrics.Damage,
                    Frags = metrics.Frags,
                    Source = metrics.Source,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage,
                    ExitedAfterDeath = exitedAfterDeath,
                    AdvancedMetrics = advanced,
                    DamageBreakdowns = breakdowns
                };
            }
            catch (VersionNotSupportedException ex)
            {
                return Create(header, hash, ReplayParseStatus.Unsupported, "ReplayVersionUnsupported", ex.Message);
            }
            catch (InvalidReplayException ex)
            {
                return Create(header, hash, ReplayParseStatus.Invalid, "ReplayInvalid", ex.Message);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                LogUtils.WriteError($"Replay parsing failed: {path}", ex);
                return Create(header, hash, ReplayParseStatus.Invalid, "ReplayReadFailed", ex.Message);
            }
        }

        private ReplayParseResult Create(ReplayHeader header, string hash, ReplayParseStatus status, string errorCode, string errorMessage) => new()
        {
            Status = status, FileHash = hash, GameVersion = header.GameVersion, ParserVersion = ParserVersion,
            BattleKey = header.BattleKey, StartedAt = header.StartedAt, Mode = header.Mode, MapName = header.MapName,
            AccountName = header.AccountName, ShipId = header.ShipId, RosterSignature = header.RosterSignature,
            Source = BattleMetricSource.MetadataOnly,
            ErrorCode = errorCode, ErrorMessage = errorMessage
        };

        private static bool IsRandomMode(string mode) =>
            mode.Equals("pvp", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("random", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("RandomBattle", StringComparison.OrdinalIgnoreCase);

        internal static ReplayBattleResults? FindValidatedBattleResults(BattleHistoryReplay replay, string accountName, string shipId, out string error)
        {
            error = replay.BattleResultsError;
            if (error == "BattleResultsTooManyPackets") return null;
            ReplayBattleResults? selected = null;
            foreach (JsonElement root in replay.BattleResults.Concat(replay.ExtraJsonData.Where(x =>
                         x.ValueKind == JsonValueKind.Object && x.TryGetProperty("playersPublicInfo", out _))))
            {
                if (!ReplayBattleResultsDecoder.TryDecode(root, replay.ClientVersion, accountName, shipId, out ReplayBattleResults? candidate, out string candidateError))
                {
                    error = candidateError;
                    continue;
                }
                if (selected != null && selected != candidate)
                {
                    error = "BattleResultsConflict";
                    return null;
                }
                selected = candidate;
            }
            if (selected != null) error = "";
            return selected;
        }

        internal static ReplayMetrics FindResultMetrics(BattleHistoryReplay replay, ReplayBattleResults? results = null)
        {
            if (results != null) return new(results.Damage, results.Frags, results.Result, BattleMetricSource.ReplayExact);
            if (replay.EntityParsingError.Length > 0)
                return new(null, null, BattleResult.Unknown, BattleMetricSource.MetadataOnly);

            uint? ownShipId = FindOwnShipId(replay);
            if (!replay.HasBattleEnded)
            {
                if (!TryFindEarlyExitMetrics(replay, ownShipId, out long? exitDamage, out double? exitFrags))
                    return new ReplayMetrics(null, null, BattleResult.Unknown, BattleMetricSource.MetadataOnly);
                return new ReplayMetrics(exitDamage, exitFrags, BattleResult.Unknown, BattleMetricSource.ReplayDerived);
            }

            BattleResult derivedResult = FindBattleResult(replay, out ownShipId);
            long? derivedDamage = FindDamage(replay);
            double? derivedFrags = ownShipId.HasValue && string.IsNullOrWhiteSpace(replay.VehicleDeathsError)
                ? replay.VehicleDeaths.LongCount(x => x.KillerId == ownShipId.Value)
                : null;
            BattleMetricSource source = derivedResult != BattleResult.Unknown || derivedDamage.HasValue || derivedFrags.HasValue
                ? BattleMetricSource.ReplayDerived
                : BattleMetricSource.MetadataOnly;
            return new ReplayMetrics(derivedDamage, derivedFrags, derivedResult, source);
        }

        internal static long? FindDamage(BattleHistoryReplay replay)
        {
            if (!replay.DamageStatsSeen || !string.IsNullOrWhiteSpace(replay.DamageStatsError) || replay.EntityParsingError.Length > 0) return null;
            double damage = replay.EnemyDamageByType.Values.Sum();
            if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0 || damage > long.MaxValue) return null;
            return checked((long)Math.Round(damage, MidpointRounding.AwayFromZero));
        }

        internal static bool TryFindEarlyExitMetrics(BattleHistoryReplay replay, uint? ownShipId, out long? damage, out double? frags)
        {
            damage = null;
            frags = null;
            if (!ownShipId.HasValue || !replay.VehicleDeaths.Any(x => x.VictimId == ownShipId.Value)) return false;

            damage = FindDamage(replay);
            if (string.IsNullOrWhiteSpace(replay.VehicleDeathsError))
                frags = replay.VehicleDeaths.LongCount(x => x.KillerId == ownShipId.Value);
            return true;
        }

        internal static BattleAdvancedMetrics FindAdvancedMetrics(BattleHistoryReplay replay, ReplayBattleResults? results = null)
        {
            bool eventsValid = replay.EntityParsingError.Length == 0;
            uint? ownShipId = eventsValid ? FindOwnShipId(replay) : null;
            VehicleDeathEvent? ownDeath = ownShipId.HasValue && string.IsNullOrEmpty(replay.VehicleDeathsError)
                ? replay.VehicleDeaths.Where(x => x.VictimId == ownShipId.Value).OrderBy(x => x.PacketTime).Cast<VehicleDeathEvent?>().FirstOrDefault()
                : null;

            bool? survived = results != null ? results.Survived : ownDeath.HasValue ? false
                : eventsValid && replay.BattleEnded && replay.BattleClock.HasEnded &&
                    string.IsNullOrEmpty(replay.VehicleDeathsError) && OwnShipIsAlive(replay, ownShipId) ? true : null;
            double? duration = results != null ? results.BattleDurationSeconds : eventsValid ? replay.BattleClock.DurationSeconds : null;
            double? survivalSeconds = results != null ? results.SurvivalSeconds : ownDeath.HasValue
                ? replay.BattleClock.SurvivalUntil(ownDeath.Value.PacketTime) : survived == true ? duration : null;
            bool damageStatsValid = eventsValid && string.IsNullOrWhiteSpace(replay.DamageStatsError);
            long? potential = damageStatsValid && replay.DamageStatisticTypesSeen.Contains(3)
                ? SumDamage(replay.PotentialDamageByType)
                : null;
            long? spotting = results != null ? results.SpottingDamage : damageStatsValid && replay.DamageStatisticTypesSeen.Contains(2)
                ? SumDamage(replay.SpottingDamageByType)
                : null;

            return new BattleAdvancedMetrics
            {
                BattleDurationSeconds = duration,
                Survived = survived,
                SurvivalSeconds = survivalSeconds,
                BattleDurationSource = duration.HasValue ? results != null ? ReplayTimingSource.BattleResults : ReplayTimingSource.BattleClock : ReplayTimingSource.Unknown,
                SurvivalTimeSource = survivalSeconds.HasValue ? results != null ? ReplayTimingSource.BattleResults : ReplayTimingSource.BattleClock : ReplayTimingSource.Unknown,
                PotentialDamage = potential,
                SpottingDamage = spotting,
                DamageTaken = results?.DamageTaken,
                SurvivalAvailability = survived.HasValue ? MetricAvailability.Stable : MetricAvailability.Unavailable,
                PotentialDamageAvailability = potential.HasValue ? MetricAvailability.Stable : MetricAvailability.Unavailable,
                SpottingDamageAvailability = spotting.HasValue ? MetricAvailability.Stable : MetricAvailability.Unavailable,
                DamageTakenAvailability = results?.DamageTaken.HasValue == true ? MetricAvailability.Experimental : MetricAvailability.Unavailable,
                ParserSchemaVersion = AdvancedSchemaVersion
            };
        }

        private static bool OwnShipIsAlive(BattleHistoryReplay replay, uint? ownShipId)
        {
            if (!ownShipId.HasValue || !replay.Entities.TryGetValue(ownShipId.Value, out Entity? own)) return false;
            bool alive = own.ClientProperties.GetValueOrDefault("isAlive") is true or (byte)1;
            object? health = own.ClientProperties.GetValueOrDefault("health");
            return alive && health is float hp && float.IsFinite(hp) && hp > 0;
        }

        internal static IReadOnlyList<BattleDamageBreakdown> FindDamageBreakdowns(BattleHistoryReplay replay)
        {
            if (!string.IsNullOrWhiteSpace(replay.DamageStatsError) || replay.EntityParsingError.Length > 0) return Array.Empty<BattleDamageBreakdown>();
            List<BattleDamageBreakdown> result = new();
            AddBreakdowns(result, replay.EnemyDamageByType, DamageDirection.Dealt);
            AddBreakdowns(result, replay.SpottingDamageByType, DamageDirection.Spotting);
            return result;
        }

        private static void AddBreakdowns(List<BattleDamageBreakdown> target, IReadOnlyDictionary<int, double> source, DamageDirection direction)
        {
            foreach ((int rawType, double value) in source)
            {
                long? damage = ToDamage(value);
                if (!damage.HasValue) continue;
                target.Add(new BattleDamageBreakdown
                {
                    Direction = direction,
                    RawTypeCode = rawType,
                    Category = DamageCategory.Unknown,
                    Damage = damage.Value,
                    Availability = MetricAvailability.Experimental
                });
            }
        }

        private static long? SumDamage(IReadOnlyDictionary<int, double> values)
        {
            double total = values.Values.Sum();
            return ToDamage(total);
        }

        private static long? ToDamage(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > long.MaxValue) return null;
            return checked((long)Math.Round(value, MidpointRounding.AwayFromZero));
        }

        private static string GetMetricError(BattleHistoryReplay replay) =>
            !string.IsNullOrWhiteSpace(replay.DamageStatsError)
                ? replay.DamageStatsError
                : replay.VehicleDeathsError;

        private static BattleResult FindBattleResult(BattleHistoryReplay replay, out uint? ownShipId)
        {
            ownShipId = FindOwnShipId(replay);
            Entity? avatar = replay.PlayerEntityId.HasValue && replay.Entities.TryGetValue(replay.PlayerEntityId.Value, out Entity? player)
                ? player
                : replay.Entities.Values.FirstOrDefault(x => x.Name.Equals("Avatar", StringComparison.Ordinal));
            if (avatar == null ||
                !ownShipId.HasValue ||
                !TryGetInt32(avatar.ClientProperties, "teamId", out int playerTeamId))
                return BattleResult.Unknown;
            Entity? battleLogic = replay.Entities.Values.FirstOrDefault(x => x.Name.Equals("BattleLogic", StringComparison.Ordinal));
            if (battleLogic == null ||
                !TryGetMember(battleLogic.ClientProperties, "battleResult", out object? battleResult) ||
                !TryGetInt32(battleResult, "winnerTeamId", out int winnerTeamId))
                return BattleResult.Unknown;

            return InterpretBattleResult(playerTeamId, winnerTeamId);
        }

        private static uint? FindOwnShipId(BattleHistoryReplay replay)
        {
            Entity? avatar = replay.PlayerEntityId.HasValue && replay.Entities.TryGetValue(replay.PlayerEntityId.Value, out Entity? player)
                ? player
                : replay.Entities.Values.FirstOrDefault(x => x.Name.Equals("Avatar", StringComparison.Ordinal));
            return avatar != null && TryGetUInt32(avatar.ClientProperties, "ownShipId", out uint shipId) && shipId != 0 ? shipId : null;
        }

        internal static BattleResult InterpretBattleResult(int playerTeamId, int winnerTeamId) =>
            winnerTeamId == 255
                ? BattleResult.Draw
                : winnerTeamId == playerTeamId ? BattleResult.Win : BattleResult.Loss;

        private static bool TryGetUInt32(object? container, string name, out uint value)
        {
            value = 0;
            if (!TryGetMember(container, name, out object? raw) || raw == null) return false;
            try
            {
                value = Convert.ToUInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return false; }
        }

        private static bool TryGetInt32(object? container, string name, out int value)
        {
            value = 0;
            if (!TryGetMember(container, name, out object? raw) || raw == null) return false;
            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return false; }
        }

        private static bool TryGetMember(object? container, string name, out object? value)
        {
            if (container is IDictionary<string, object?> generic && generic.TryGetValue(name, out value)) return true;
            if (container is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is string key && key.Equals(name, StringComparison.Ordinal))
                    {
                        value = entry.Value;
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        private static async Task<string> CalculateHashAsync(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, true);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }

        private static async Task<ReplayHeader> ReadHeaderAsync(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, true);
            byte[] prefix = new byte[12];
            await stream.ReadExactlyAsync(prefix, cancellationToken);
            int length = BitConverter.ToInt32(prefix, 8);
            if (length <= 1 || length > 4 * 1024 * 1024) throw new InvalidDataException("Replay JSON header length is invalid.");
            byte[] json = new byte[length];
            await stream.ReadExactlyAsync(json, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string mode = GetString(root, "matchGroup", "gameType");
            string account = GetString(root, "playerName");
            string shipId = "";
            List<string> rosterEntries = new();
            if (root.TryGetProperty("vehicles", out JsonElement vehicles) && vehicles.ValueKind == JsonValueKind.Array)
            {
                JsonElement self = vehicles.EnumerateArray().FirstOrDefault(x => GetString(x, "name").Equals(account, StringComparison.OrdinalIgnoreCase));
                shipId = GetString(self, "shipId");
                rosterEntries.AddRange(vehicles.EnumerateArray().Select(x => CreateRosterEntry(GetString(x, "name"), GetString(x, "shipId"))));
            }
            DateTimeOffset? started = null;
            string date = GetString(root, "dateTime");
            if (DateTimeOffset.TryParseExact(date, "dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset parsed)) started = parsed;
            string map = GetString(root, "mapDisplayName", "mapName", "name");
            string version = GetString(root, "clientVersionFromExe", "clientVersionFromXml");
            string arenaId = GetString(root, "arenaUniqueId", "arenaUniqueID", "arenaId");
            string rosterSignature = string.Join("|", rosterEntries.Where(x => x.Length > 1).OrderBy(x => x, StringComparer.Ordinal));
            string keySeed = string.IsNullOrWhiteSpace(rosterSignature)
                ? $"{started:O}|{map}|{account}|{shipId}"
                : $"{mode}|{map}|{account}|{shipId}|{rosterSignature}";
            string key = !string.IsNullOrWhiteSpace(arenaId)
                ? $"arena:{arenaId}"
                : $"replay:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySeed)))}";
            return new ReplayHeader(mode, map, account, shipId, version, started, key, rosterSignature);
        }

        private static string CreateRosterEntry(string accountName, string shipId) =>
            string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(shipId)
                ? ""
                : $"{accountName.Trim().ToLowerInvariant()}:{shipId.Trim()}";

        private static string GetString(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object) return "";
            foreach (string name in names)
                if (element.TryGetProperty(name, out JsonElement property)) return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
            return "";
        }

        public void Dispose() => services.Dispose();

        internal readonly record struct ReplayMetrics(long? Damage, double? Frags, BattleResult Result, BattleMetricSource Source);

        private sealed record ReplayHeader(string Mode, string MapName, string AccountName, string ShipId, string GameVersion, DateTimeOffset? StartedAt, string BattleKey, string RosterSignature)
        {
            public ReplayHeader Merge(ArenaInfo? arena, string? mapName, Version? version)
            {
                if (arena == null) return this;
                string account = string.IsNullOrWhiteSpace(AccountName) ? arena.PlayerName : AccountName;
                string ship = ShipId;
                if (string.IsNullOrWhiteSpace(ship)) ship = arena.Vehicles?.FirstOrDefault(x => x.Name.Equals(account, StringComparison.OrdinalIgnoreCase))?.ShipId.ToString() ?? "";
                return this with
                {
                    Mode = string.IsNullOrWhiteSpace(Mode) ? arena.MatchGroup : Mode,
                    MapName = string.IsNullOrWhiteSpace(MapName) ? mapName ?? arena.Name : MapName,
                    AccountName = account,
                    ShipId = ship,
                    GameVersion = string.IsNullOrWhiteSpace(GameVersion) ? version?.ToString() ?? arena.ClientVersion.ToString() : GameVersion,
                    StartedAt = StartedAt ?? new DateTimeOffset(arena.DateTime)
                };
            }
        }
    }
}
