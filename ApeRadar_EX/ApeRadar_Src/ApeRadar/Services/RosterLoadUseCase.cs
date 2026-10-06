using ApeRadar.Models;
using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.Services
{
    internal sealed record RosterLoadCommand(
        string Filename,
        string GamePath,
        string PrimaryServerName,
        string SecondaryServerName,
        bool SecondaryServerEnabled,
        string ApiTypeName,
        int MaximumRetryAttempts,
        bool ForceRefresh,
        string? ForceRefreshPlayerId,
        Server? ForceRefreshPlayerServer,
        string WatchListFilename)
    {
        public PlayerStatisticsOptions? StatisticsOptions { get; init; }
    }

    internal sealed record RosterLoadMetadata(
        string BattleType,
        DateTimeOffset BattleStartTime,
        string RawMapName,
        Server Server,
        APIType Provider,
        IReadOnlyList<Player> Players);

    internal sealed record RosterHistoryCaptureRequest(
        string BattleId,
        string BattleType,
        DateTimeOffset BattleStartTime,
        string MapName,
        Server Server,
        IReadOnlyList<Player> Players);

    internal enum RosterBackgroundRefreshState
    {
        Refreshed,
        Failed,
        NoChanges,
        Error
    }

    internal sealed record RosterBackgroundRefreshResult(
        RosterBackgroundRefreshState State,
        IReadOnlyList<Player> OriginalPlayers,
        IReadOnlyList<Player>? RefreshedPlayers,
        BattleRosterLoadResult? RosterResult,
        Exception? Error);

    internal sealed class RosterBackgroundRefreshOperation
    {
        private readonly Action start;
        private int started;

        internal RosterBackgroundRefreshOperation(Task<RosterBackgroundRefreshResult?> completion, Action start)
        {
            Completion = completion;
            this.start = start;
        }

        public Task<RosterBackgroundRefreshResult?> Completion { get; }

        public Task<RosterBackgroundRefreshResult?> Start()
        {
            if (Interlocked.Exchange(ref started, 1) == 0) start();
            return Completion;
        }
    }

    internal sealed record RosterLoadExecution(
        string Filename,
        string BattleId,
        string BattleType,
        DateTimeOffset BattleStartTime,
        string RawMapName,
        Server Server,
        APIType Provider,
        IReadOnlyList<Player> Players,
        BattleRosterLoadResult RosterResult,
        RosterHistoryCaptureRequest? HistoryCapture,
        RosterBackgroundRefreshOperation? BackgroundRefresh);

    internal interface IRosterLoadUseCase : IDisposable
    {
        Task<RosterLoadExecution?> LoadAsync(
            RosterLoadCommand command,
            Action? attemptStarted = null,
            Action<RosterLoadMetadata>? metadataReady = null,
            Action? statisticsLoading = null,
            Action<Exception>? attemptFailed = null,
            Action<int>? retrying = null,
            CancellationToken cancellationToken = default);
        void Cancel();
    }

    internal sealed class RosterLoadUseCase : IRosterLoadUseCase
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private readonly IBattleRosterCoordinator coordinator;
        private readonly Func<string, JObject> arenaReader;
        private readonly Func<string, JObject> watchListReader;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly Action<IEnumerable<Player>, string, DateTimeOffset> applyEncounterMarkers;
        private CancellationTokenSource? activeLoad;
        private long generation;

        public RosterLoadUseCase(IBattleRosterCoordinator coordinator) : this(
            coordinator,
            FileUtils.ReadTempArenaInfoFile,
            WatchListUtils.ReadWatchList,
            Task.Delay,
            EncounterHistoryUtils.ApplyRecentEncounterMarkers)
        {
        }

        internal RosterLoadUseCase(
            IBattleRosterCoordinator coordinator,
            Func<string, JObject> arenaReader,
            Func<string, JObject> watchListReader,
            Func<TimeSpan, CancellationToken, Task> delay,
            Action<IEnumerable<Player>, string, DateTimeOffset> applyEncounterMarkers)
        {
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.arenaReader = arenaReader ?? throw new ArgumentNullException(nameof(arenaReader));
            this.watchListReader = watchListReader ?? throw new ArgumentNullException(nameof(watchListReader));
            this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
            this.applyEncounterMarkers = applyEncounterMarkers ?? throw new ArgumentNullException(nameof(applyEncounterMarkers));
        }

        public async Task<RosterLoadExecution?> LoadAsync(
            RosterLoadCommand command,
            Action? attemptStarted = null,
            Action<RosterLoadMetadata>? metadataReady = null,
            Action? statisticsLoading = null,
            Action<Exception>? attemptFailed = null,
            Action<int>? retrying = null,
            CancellationToken cancellationToken = default)
        {
            long currentGeneration = Interlocked.Increment(ref generation);
            CancellationTokenSource currentLoad = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationTokenSource? previousLoad = Interlocked.Exchange(ref activeLoad, currentLoad);
            previousLoad?.Cancel();
            CancellationToken loadToken = currentLoad.Token;
            bool backgroundRefreshOwnsLifetime = false;

            try
            {
                for (int attempt = 0; attempt <= command.MaximumRetryAttempts; attempt++)
                {
                    try
                    {
                        loadToken.ThrowIfCancellationRequested();
                        if (!IsCurrent(currentGeneration)) return null;
                        attemptStarted?.Invoke();

                        PreparedRosterLoad prepared = Prepare(command);
                        if (command.ForceRefreshPlayerId == null && IsCurrent(currentGeneration))
                        {
                            metadataReady?.Invoke(new RosterLoadMetadata(
                                prepared.BattleType,
                                prepared.BattleStartTime,
                                prepared.RawMapName,
                                prepared.Server,
                                prepared.Request.ApiType,
                                coordinator.CreateMetadataRoster(prepared.Request)));
                        }
                        statisticsLoading?.Invoke();

                        BattleRosterLoadResult rosterResult = await coordinator.LoadAsync(prepared.Request, loadToken);
                        loadToken.ThrowIfCancellationRequested();
                        if (!IsCurrent(currentGeneration)) return null;

                        List<Player> players = rosterResult.Players.ToList();
                        ApplyWatchList(players, prepared.WatchList);
                        string battleId = CreateBattleId(prepared.Arena, prepared.Server, prepared.BattleStartTime);
                        applyEncounterMarkers(players, battleId, prepared.BattleStartTime);
                        RosterHistoryCaptureRequest? historyCapture = IsRandomBattle(prepared.BattleType)
                            ? new RosterHistoryCaptureRequest(
                                battleId,
                                prepared.BattleType,
                                prepared.BattleStartTime,
                                prepared.Arena["mapName"]?.Value<string>() ?? prepared.Arena["mapDisplayName"]?.Value<string>() ?? "",
                                prepared.Server,
                                players)
                            : null;

                        RosterBackgroundRefreshOperation? backgroundRefresh = null;
                        if (players.Any(player => player.IsDataStale))
                        {
                            BattleRosterRequest refreshRequest = prepared.Request with
                            {
                                ApiType = rosterResult.Provider,
                                ForceRefresh = true,
                                ForceRefreshPlayerId = null,
                                ForceRefreshPlayerServer = null
                            };
                            backgroundRefreshOwnsLifetime = true;
                            TaskCompletionSource startRefresh = new();
                            Task<RosterBackgroundRefreshResult?> completion = RefreshStalePlayersAsync(
                                refreshRequest,
                                players,
                                currentGeneration,
                                currentLoad,
                                startRefresh.Task);
                            backgroundRefresh = new RosterBackgroundRefreshOperation(completion, () => startRefresh.TrySetResult());
                        }

                        return new RosterLoadExecution(
                            command.Filename,
                            battleId,
                            prepared.BattleType,
                            prepared.BattleStartTime,
                            prepared.RawMapName,
                            prepared.Server,
                            rosterResult.Provider,
                            players,
                            rosterResult,
                            historyCapture,
                            backgroundRefresh);
                    }
                    catch (OperationCanceledException) when (loadToken.IsCancellationRequested)
                    {
                        return null;
                    }
                    catch (Exception ex)
                    {
                        if (!IsCurrent(currentGeneration)) return null;
                        attemptFailed?.Invoke(ex);
                        if (IsNonRetryable(ex) || attempt >= command.MaximumRetryAttempts) throw;
                        await delay(RetryDelay, loadToken);
                        if (IsCurrent(currentGeneration)) retrying?.Invoke(attempt + 1);
                    }
                }

                return null;
            }
            finally
            {
                if (!backgroundRefreshOwnsLifetime) Complete(currentGeneration, currentLoad);
            }
        }

        public void Cancel()
        {
            Interlocked.Increment(ref generation);
            CancellationTokenSource? load = Interlocked.Exchange(ref activeLoad, null);
            if (load == null) return;
            try { load.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose() => Cancel();

        internal static string CreateBattleId(JObject arena, Server server, DateTimeOffset battleStartTime)
        {
            string arenaId = arena["arenaUniqueId"]?.Value<string>()
                ?? arena["arenaUniqueID"]?.Value<string>()
                ?? arena["arenaId"]?.Value<string>()
                ?? "";
            if (!string.IsNullOrWhiteSpace(arenaId)) return $"arena:{arenaId}";

            string shipComposition = string.Join(",", arena["vehicles"]!
                .Select(vehicle => vehicle["shipId"]?.Value<string>() ?? "")
                .OrderBy(shipId => shipId, StringComparer.Ordinal));
            return $"{ServerExt.GetNameByServer(server)}|{battleStartTime:O}|{arena["mapName"]?.Value<string>()}|{arena["playerName"]?.Value<string>()}|{shipComposition}";
        }

        private PreparedRosterLoad Prepare(RosterLoadCommand command)
        {
            Server server = ServerExt.GetServerByName(command.PrimaryServerName);
            LogUtils.WriteInfo($"server={ServerExt.GetNameByServer(server)}");
            if (server == Server.AUTO)
            {
                server = ServerExt.AutoDetectServer($@"{command.GamePath}\profile\clientrunner.log");
                LogUtils.WriteInfo($"detectedServer={ServerExt.GetNameByServer(server)}");
            }
            Server secondaryServer = ServerExt.GetServerByName(command.SecondaryServerName);
            LogUtils.WriteInfo($"secondaryServer={ServerExt.GetNameByServer(secondaryServer)}");
            JObject watchList = watchListReader(command.WatchListFilename);
            JObject arena = arenaReader(command.Filename);
            string battleType = arena["matchGroup"]!.Value<string>()!;
            DateTimeOffset battleStartTime = DateTimeOffset.ParseExact(
                arena["dateTime"]!.Value<string>()!,
                "dd.MM.yyyy HH:mm:ss",
                CultureInfo.CurrentCulture);
            string rawMapName = arena["mapDisplayName"]?.Value<string>() ?? arena["mapName"]?.Value<string>() ?? "";
            int playerCount = arena["vehicles"]!.Count();
            LogUtils.WriteInfo($"playerCount={playerCount}");
            APIType apiType = APITypeExt.GetAPITypeByName(command.ApiTypeName);
            BattleRosterRequest request = new(
                arena,
                playerCount,
                server,
                secondaryServer,
                command.SecondaryServerEnabled,
                apiType,
                command.ForceRefresh,
                command.ForceRefreshPlayerId,
                command.ForceRefreshPlayerServer) { StatisticsOptions = command.StatisticsOptions };
            return new PreparedRosterLoad(arena, watchList, battleType, battleStartTime, rawMapName, server, request);
        }

        private async Task<RosterBackgroundRefreshResult?> RefreshStalePlayersAsync(
            BattleRosterRequest request,
            IReadOnlyList<Player> originalPlayers,
            long currentGeneration,
            CancellationTokenSource currentLoad,
            Task startSignal)
        {
            try
            {
                await startSignal.WaitAsync(currentLoad.Token);
                BattleRosterLoadResult refreshResult = await coordinator.LoadAsync(request, currentLoad.Token);
                if (!IsCurrent(currentGeneration)) return null;
                if (refreshResult.IsFailed)
                    return new RosterBackgroundRefreshResult(RosterBackgroundRefreshState.Failed, originalPlayers, null, refreshResult, null);

                int updatedCount = 0;
                foreach (Player refreshed in refreshResult.Players)
                {
                    Player? existing = originalPlayers.FirstOrDefault(player => player.Identity.MatchesAccount(refreshed.Identity));
                    if (existing == null) continue;
                    existing.CopyFrom(refreshed);
                    updatedCount++;
                }
                if (!IsCurrent(currentGeneration)) return null;
                if (updatedCount == 0)
                    return new RosterBackgroundRefreshResult(RosterBackgroundRefreshState.NoChanges, originalPlayers, null, refreshResult, null);

                return new RosterBackgroundRefreshResult(RosterBackgroundRefreshState.Refreshed, originalPlayers, originalPlayers.ToList(), refreshResult, null);
            }
            catch (OperationCanceledException) when (currentLoad.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                return IsCurrent(currentGeneration)
                    ? new RosterBackgroundRefreshResult(RosterBackgroundRefreshState.Error, originalPlayers, null, null, ex)
                    : null;
            }
            finally
            {
                Complete(currentGeneration, currentLoad);
            }
        }

        private bool IsCurrent(long currentGeneration) => currentGeneration == Interlocked.Read(ref generation);

        private void Complete(long currentGeneration, CancellationTokenSource currentLoad)
        {
            if (IsCurrent(currentGeneration)) Interlocked.CompareExchange(ref activeLoad, null, currentLoad);
            currentLoad.Dispose();
        }

        private static bool IsNonRetryable(Exception exception) =>
            exception.Message is "FileFormatIncorrect" or "ServerAutoDetectionFailed";

        private static bool IsRandomBattle(string mode) =>
            mode.Equals("pvp", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("random", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("RandomBattle", StringComparison.OrdinalIgnoreCase);

        private static void ApplyWatchList(IEnumerable<Player> players, JObject watchList)
        {
            foreach (Player player in players)
            {
                if (!player.Identity.HasAccountId || watchList[ServerExt.GetNameByServer(player.Server)]!.SelectToken(player.ID) == null) continue;
                player.WatchStatus = WatchStatusExt.GetStatusByName(
                    watchList[ServerExt.GetNameByServer(player.Server)]![player.ID]!["status"]!.Value<string>()!);
                player.Note = WatchListUtils.GetPlayerNote(watchList, player.Server, player.ID);
                player.IsCustomMarked = WatchListUtils.GetPlayerCustomMarker(watchList, player.Server, player.ID);
            }
        }

        private sealed record PreparedRosterLoad(
            JObject Arena,
            JObject WatchList,
            string BattleType,
            DateTimeOffset BattleStartTime,
            string RawMapName,
            Server Server,
            BattleRosterRequest Request);
    }
}
