using ApeRadar.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodsoft.WowsReplaysUnpack;
using Nodsoft.WowsReplaysUnpack.Core.Exceptions;
using Nodsoft.WowsReplaysUnpack.Core.Entities;
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
        private readonly ServiceProvider services;
        private readonly IReplayUnpackerFactory factory;

        public NodsoftReplayParserAdapter()
        {
            ServiceCollection collection = new();
            collection.AddLogging();
            collection.AddWowsReplayUnpacker(builder =>
                builder.AddReplayController<BattleHistoryReplayController, BattleHistoryReplay>());
            services = collection.BuildServiceProvider();
            factory = services.GetRequiredService<IReplayUnpackerFactory>();
        }

        public string ParserVersion => typeof(IReplayUnpackerFactory).Assembly.GetName().Version?.ToString() ?? "unknown";

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

                ReplayMetrics metrics = FindResultMetrics(replay, header.AccountName);
                bool complete = metrics.Damage.HasValue && metrics.Frags.HasValue && metrics.Result != BattleResult.Unknown;
                string errorCode = complete
                    ? ""
                    : !replay.BattleEnded ? "BattleNotFinished"
                    : !string.IsNullOrWhiteSpace(GetMetricError(replay)) ? "ReplayMetricsInvalid"
                    : "BattleResultsMissing";
                string errorMessage = complete
                    ? ""
                    : !replay.BattleEnded
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
                    Result = metrics.Result,
                    Damage = metrics.Damage,
                    Frags = metrics.Frags,
                    Source = metrics.Source,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage
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
            AccountName = header.AccountName, ShipId = header.ShipId, Source = BattleMetricSource.MetadataOnly,
            ErrorCode = errorCode, ErrorMessage = errorMessage
        };

        private static bool IsRandomMode(string mode) =>
            mode.Equals("pvp", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("random", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("RandomBattle", StringComparison.OrdinalIgnoreCase);

        private static ReplayMetrics FindResultMetrics(BattleHistoryReplay replay, string accountName)
        {
            foreach (JsonElement root in replay.ExtraJsonData)
            {
                foreach (JsonElement candidate in FindPersonalResultObjects(root, accountName))
                {
                    long? damage = FindNumber(candidate, "damage_dealt", "damageDealt", "totalDamage");
                    double? frags = FindNumber(candidate, "frags", "kills", "shipsDestroyed");
                    BattleResult result = FindResult(candidate);
                    if (damage.HasValue && frags.HasValue && result != BattleResult.Unknown)
                        return new ReplayMetrics(damage, frags, result, BattleMetricSource.ReplayExact);
                }
            }

            if (!replay.BattleEnded)
                return new ReplayMetrics(null, null, BattleResult.Unknown, BattleMetricSource.MetadataOnly);

            BattleResult derivedResult = FindBattleResult(replay, out uint? ownShipId);
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
            if (!string.IsNullOrWhiteSpace(replay.DamageStatsError)) return null;
            double damage = replay.EnemyDamageByType.Values.Sum();
            if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0 || damage > long.MaxValue) return null;
            return checked((long)Math.Round(damage, MidpointRounding.AwayFromZero));
        }

        private static string GetMetricError(BattleHistoryReplay replay) =>
            !string.IsNullOrWhiteSpace(replay.DamageStatsError)
                ? replay.DamageStatsError
                : replay.VehicleDeathsError;

        private static BattleResult FindBattleResult(BattleHistoryReplay replay, out uint? ownShipId)
        {
            ownShipId = null;
            Entity? avatar = replay.PlayerEntityId.HasValue && replay.Entities.TryGetValue(replay.PlayerEntityId.Value, out Entity? player)
                ? player
                : replay.Entities.Values.FirstOrDefault(x => x.Name.Equals("Avatar", StringComparison.Ordinal));
            if (avatar == null ||
                !TryGetUInt32(avatar.ClientProperties, "ownShipId", out uint shipId) || shipId == 0 ||
                !TryGetInt32(avatar.ClientProperties, "teamId", out int playerTeamId))
                return BattleResult.Unknown;

            ownShipId = shipId;
            Entity? battleLogic = replay.Entities.Values.FirstOrDefault(x => x.Name.Equals("BattleLogic", StringComparison.Ordinal));
            if (battleLogic == null ||
                !TryGetMember(battleLogic.ClientProperties, "battleResult", out object? battleResult) ||
                !TryGetInt32(battleResult, "winnerTeamId", out int winnerTeamId))
                return BattleResult.Unknown;

            return InterpretBattleResult(playerTeamId, winnerTeamId);
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

        private static System.Collections.Generic.IEnumerable<JsonElement> FindPersonalResultObjects(JsonElement element, string accountName)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                bool identityMatches = element.EnumerateObject().Any(property =>
                    (property.Name is "name" or "playerName" or "nickname" or "accountName") &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString()?.Equals(accountName, StringComparison.OrdinalIgnoreCase) == true);
                if (identityMatches) yield return element;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
                        yield return property.Value;
                    foreach (JsonElement nested in FindPersonalResultObjects(property.Value, accountName)) yield return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in element.EnumerateArray())
                    foreach (JsonElement nested in FindPersonalResultObjects(child, accountName)) yield return nested;
        }

        private static long? FindNumber(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (names.Any(x => property.Name.Equals(x, StringComparison.OrdinalIgnoreCase)) && property.Value.TryGetInt64(out long value))
                        return value;
                    long? nested = FindNumber(property.Value, names);
                    if (nested.HasValue) return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    long? nested = FindNumber(child, names);
                    if (nested.HasValue) return nested;
                }
            }
            return null;
        }

        private static BattleResult FindResult(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals("isWinner", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        return property.Value.GetBoolean() ? BattleResult.Win : BattleResult.Loss;
                    if (property.Name.Equals("result", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        string value = property.Value.GetString() ?? "";
                        if (value.Contains("win", StringComparison.OrdinalIgnoreCase)) return BattleResult.Win;
                        if (value.Contains("loss", StringComparison.OrdinalIgnoreCase) || value.Contains("defeat", StringComparison.OrdinalIgnoreCase)) return BattleResult.Loss;
                        if (value.Contains("draw", StringComparison.OrdinalIgnoreCase)) return BattleResult.Draw;
                    }
                    BattleResult nested = FindResult(property.Value);
                    if (nested != BattleResult.Unknown) return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement child in element.EnumerateArray())
                {
                    BattleResult nested = FindResult(child);
                    if (nested != BattleResult.Unknown) return nested;
                }
            return BattleResult.Unknown;
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
            if (root.TryGetProperty("vehicles", out JsonElement vehicles) && vehicles.ValueKind == JsonValueKind.Array)
            {
                JsonElement self = vehicles.EnumerateArray().FirstOrDefault(x => GetString(x, "name").Equals(account, StringComparison.OrdinalIgnoreCase));
                shipId = GetString(self, "shipId");
            }
            DateTimeOffset? started = null;
            string date = GetString(root, "dateTime");
            if (DateTimeOffset.TryParseExact(date, "dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset parsed)) started = parsed;
            string map = GetString(root, "mapDisplayName", "mapName", "name");
            string version = GetString(root, "clientVersionFromExe", "clientVersionFromXml");
            string arenaId = GetString(root, "arenaUniqueId", "arenaUniqueID", "arenaId");
            string keySeed = $"{started:O}|{map}|{account}|{shipId}";
            string key = !string.IsNullOrWhiteSpace(arenaId)
                ? $"arena:{arenaId}"
                : $"replay:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySeed)))}";
            return new ReplayHeader(mode, map, account, shipId, version, started, key);
        }

        private static string GetString(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object) return "";
            foreach (string name in names)
                if (element.TryGetProperty(name, out JsonElement property)) return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
            return "";
        }

        public void Dispose() => services.Dispose();

        private readonly record struct ReplayMetrics(long? Damage, double? Frags, BattleResult Result, BattleMetricSource Source);

        private sealed record ReplayHeader(string Mode, string MapName, string AccountName, string ShipId, string GameVersion, DateTimeOffset? StartedAt, string BattleKey)
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
