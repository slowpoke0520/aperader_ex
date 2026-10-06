using ApeRadar.History;
using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterLoadUseCaseTests
{
    [Fact]
    public async Task MetadataIsPublishedBeforeStatisticsAndFinalResultKeepsAnnotations()
    {
        TaskCompletionSource<BattleRosterLoadResult> statistics = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubRosterCoordinator coordinator = new(statistics.Task);
        bool encounterMarkersApplied = false;
        using RosterLoadUseCase useCase = CreateUseCase(
            coordinator,
            _ => CreateArena("battle-1"),
            players =>
            {
                encounterMarkersApplied = true;
                Assert.Equal("arena:battle-1", players.BattleId);
            });
        RosterLoadMetadata? metadata = null;

        Task<RosterLoadExecution?> load = useCase.LoadAsync(CreateCommand("first"), metadataReady: value => metadata = value);

        Assert.NotNull(metadata);
        Assert.Equal("Self", Assert.Single(metadata.Players).Name);
        Assert.False(load.IsCompleted);

        Player loaded = CreateLoadedPlayer("Self", "100", isStale: false);
        statistics.SetResult(Result(loaded));
        RosterLoadExecution execution = Assert.IsType<RosterLoadExecution>(await load);

        Assert.True(encounterMarkersApplied);
        Assert.Equal("arena:battle-1", execution.BattleId);
        Player finalPlayer = Assert.Single(execution.Players);
        Assert.Equal(WatchStatus.POSITIVE, finalPlayer.WatchStatus);
        Assert.Equal("tracked", finalPlayer.Note);
        Assert.True(finalPlayer.IsCustomMarked);
        Assert.NotNull(execution.HistoryCapture);
    }

    [Fact]
    public async Task NewBattleSuppressesLateInitialResult()
    {
        TaskCompletionSource<BattleRosterLoadResult> firstInitial = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubRosterCoordinator coordinator = new(
            firstInitial.Task,
            Task.FromResult(Result(CreateLoadedPlayer("Second", "200", isStale: false))));
        using RosterLoadUseCase useCase = CreateUseCase(
            coordinator,
            filename => CreateArena(filename == "second" ? "battle-2" : "battle-1"));

        Task<RosterLoadExecution?> firstLoad = useCase.LoadAsync(CreateCommand("first"));
        Task<RosterLoadExecution?> secondLoad = useCase.LoadAsync(CreateCommand("second"));
        RosterLoadExecution second = Assert.IsType<RosterLoadExecution>(await secondLoad);
        firstInitial.SetResult(Result(CreateLoadedPlayer("First", "100", isStale: false)));

        Assert.Null(await firstLoad);
        Assert.Equal("arena:battle-2", second.BattleId);
    }

    [Fact]
    public async Task NewBattleSuppressesLateBackgroundRefresh()
    {
        TaskCompletionSource<BattleRosterLoadResult> firstRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubRosterCoordinator coordinator = new(
            Task.FromResult(Result(CreateLoadedPlayer("First", "100", isStale: true))),
            firstRefresh.Task,
            Task.FromResult(Result(CreateLoadedPlayer("Second", "200", isStale: false))));
        using RosterLoadUseCase useCase = CreateUseCase(
            coordinator,
            filename => CreateArena(filename == "second" ? "battle-2" : "battle-1"));

        Task<RosterLoadExecution?> staleLoad = useCase.LoadAsync(CreateCommand("first"));
        RosterLoadExecution stale = Assert.IsType<RosterLoadExecution>(await staleLoad);
        Assert.NotNull(stale.BackgroundRefresh);
        Task<RosterBackgroundRefreshResult?> staleRefresh = stale.BackgroundRefresh.Start();

        RosterLoadExecution current = Assert.IsType<RosterLoadExecution>(await useCase.LoadAsync(CreateCommand("second")));
        Assert.Equal("arena:battle-2", current.BattleId);
        firstRefresh.SetResult(Result(CreateLoadedPlayer("First", "100", isStale: false)));
        Assert.Null(await staleRefresh);
    }

    [Fact]
    public async Task BackgroundRefreshUsesResolvedProviderAndPreservesLocalAnnotations()
    {
        Player stalePlayer = CreateLoadedPlayer("Self", "100", isStale: true);
        Player refreshedPlayer = CreateLoadedPlayer("Self", "100", isStale: false);
        refreshedPlayer.Battles = 321;
        StubRosterCoordinator coordinator = new(
            Task.FromResult(ResultWithProvider(APIType.WG_PUBLIC, stalePlayer)),
            Task.FromResult(Result(refreshedPlayer)));
        using RosterLoadUseCase useCase = CreateUseCase(coordinator, _ => CreateArena("battle-1"));

        RosterLoadExecution execution = Assert.IsType<RosterLoadExecution>(await useCase.LoadAsync(CreateCommand("first")));
        Assert.Single(coordinator.Requests);
        Assert.True(Assert.Single(execution.Players).IsDataStale);
        RosterBackgroundRefreshResult refresh = Assert.IsType<RosterBackgroundRefreshResult>(await execution.BackgroundRefresh!.Start());

        Assert.Equal(RosterBackgroundRefreshState.Refreshed, refresh.State);
        Player finalPlayer = Assert.Single(refresh.RefreshedPlayers!);
        Assert.Equal(321, finalPlayer.Battles);
        Assert.Equal("tracked", finalPlayer.Note);
        Assert.Equal(WatchStatus.POSITIVE, finalPlayer.WatchStatus);
        Assert.True(finalPlayer.IsCustomMarked);
        Assert.Equal(2, coordinator.Requests.Count);
        Assert.Equal(APIType.WG_PUBLIC, coordinator.Requests[1].ApiType);
        Assert.True(coordinator.Requests[1].ForceRefresh);
        Assert.Null(coordinator.Requests[1].ForceRefreshPlayerId);
        Assert.Null(coordinator.Requests[1].ForceRefreshPlayerServer);
    }

    [Fact]
    public async Task TransientPreparationFailureRetriesButStableFileErrorDoesNot()
    {
        int reads = 0;
        int retries = 0;
        StubRosterCoordinator coordinator = new(Task.FromResult(Result(CreateLoadedPlayer("Self", "100", isStale: false))));
        using RosterLoadUseCase useCase = CreateUseCase(coordinator, _ =>
        {
            if (++reads == 1) throw new IOException("transient");
            return CreateArena("battle-1");
        });

        RosterLoadExecution retried = Assert.IsType<RosterLoadExecution>(await useCase.LoadAsync(
            CreateCommand("first", maximumRetryAttempts: 1),
            retrying: _ => retries++));

        Assert.Equal("arena:battle-1", retried.BattleId);
        Assert.Equal(2, reads);
        Assert.Equal(1, retries);

        int stableReads = 0;
        using RosterLoadUseCase stableFailure = CreateUseCase(coordinator, _ =>
        {
            stableReads++;
            throw new Exception("FileFormatIncorrect");
        });
        Exception error = await Assert.ThrowsAsync<Exception>(() => stableFailure.LoadAsync(CreateCommand("invalid", maximumRetryAttempts: 3)));

        Assert.Equal("FileFormatIncorrect", error.Message);
        Assert.Equal(1, stableReads);
    }

    [Fact]
    public async Task HistoryCaptureBuildsTheExistingPendingBattleShape()
    {
        CapturingHistoryCoordinator coordinator = new();
        RosterHistoryCaptureService service = new(() => coordinator);
        Player self = CreateLoadedPlayer("Self", "100", isStale: false);
        self.Relation = "0";
        self.ShipName = "Yamato";
        self.ShipType = "Battleship";
        DateTimeOffset started = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        RosterHistoryCaptureRequest request = new("arena:123", "pvp", started, "ocean", Server.ASIA, new[] { self });

        await service.CaptureAsync(request);

        BattleRecord battle = Assert.IsType<BattleRecord>(coordinator.Battle);
        Assert.Equal("arena:123", battle.BattleKey);
        Assert.Equal(BattleCompleteness.Pending, battle.Completeness);
        Assert.Equal(BattleMetricSource.MetadataOnly, battle.Source);
        Assert.Equal("WaitingForReplay", battle.StatusMessage);
        Assert.Equal("Yamato", battle.ShipName);
        Assert.Single(coordinator.Players!);
    }

    private static RosterLoadUseCase CreateUseCase(
        StubRosterCoordinator coordinator,
        Func<string, JObject> arenaReader,
        Action<(IEnumerable<Player> Players, string BattleId, DateTimeOffset StartedAt)>? encounter = null) =>
        new(
            coordinator,
            arenaReader,
            _ => JObject.Parse("""
                {"RU":{},"EU":{},"NA":{},"ASIA":{"100":{"name":"Self","status":"Positive","note":"tracked","customMarker":true}},"CN":{}}
                """),
            (_, _) => Task.CompletedTask,
            (players, battleId, startedAt) => encounter?.Invoke((players, battleId, startedAt)));

    private static RosterLoadCommand CreateCommand(string filename, int maximumRetryAttempts = 0) => new(
        filename,
        "C:\\Games\\WorldOfWarships",
        "ASIA",
        "RU",
        false,
        "VORTEX",
        maximumRetryAttempts,
        false,
        null,
        null,
        "WatchList.json");

    private static JObject CreateArena(string arenaId) => JObject.Parse($$"""
        {
          "arenaUniqueId": "{{arenaId}}",
          "matchGroup": "pvp",
          "dateTime": "29.09.2026 12:00:00",
          "mapName": "ocean",
          "playerName": "Self",
          "vehicles": [
            { "id": 101, "name": "Self", "relation": 0, "shipId": "4179601392" }
          ]
        }
        """);

    private static Player CreateLoadedPlayer(string name, string id, bool isStale) => new(name, Server.ASIA, "0", "4179601392")
    {
        ID = id,
        Battles = 100,
        IsDataStale = isStale
    };

    private static BattleRosterLoadResult Result(params Player[] players) => new(players, APIType.VORTEX, players.Count(player => player.IsDataStale), 1, Array.Empty<ApiFailureKind>());

    private static BattleRosterLoadResult ResultWithProvider(APIType provider, params Player[] players) =>
        new(players, provider, players.Count(player => player.IsDataStale), 1, Array.Empty<ApiFailureKind>());

    private sealed class StubRosterCoordinator : IBattleRosterCoordinator
    {
        private readonly Queue<Task<BattleRosterLoadResult>> results;
        public List<BattleRosterRequest> Requests { get; } = new();

        public StubRosterCoordinator(params Task<BattleRosterLoadResult>[] results) => this.results = new Queue<Task<BattleRosterLoadResult>>(results);

        public IReadOnlyList<Player> CreateMetadataRoster(BattleRosterRequest request) => request.Arena["vehicles"]!
            .Select(vehicle => new Player(
                vehicle["name"]!.Value<string>()!,
                request.PrimaryServer,
                vehicle["relation"]!.Value<string>()!,
                vehicle["shipId"]!.Value<string>()!))
            .ToList();

        public Task<BattleRosterLoadResult> LoadAsync(BattleRosterRequest request, CancellationToken cancellationToken)
        {
            lock (results)
            {
                Requests.Add(request);
                return results.Dequeue();
            }
        }
    }

    private sealed class CapturingHistoryCoordinator : IBattleTrackingCoordinator
    {
        public IReplayMonitor ReplayMonitor { get; } = new NullReplayMonitor();
        public BattleRecord? Battle { get; private set; }
        public IReadOnlyCollection<BattlePlayerRecord>? Players { get; private set; }

        public Task InitializeAsync(string gamePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CapturePreBattleAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default)
        {
            Battle = battle;
            Players = players;
            return Task.CompletedTask;
        }

        public Task RetryPendingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullReplayMonitor : IReplayMonitor
    {
        public event EventHandler<ReplayImportProgress>? ImportProgressChanged { add { } remove { } }
        public Task StartAsync(string gamePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RescanAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RetryFailedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void CancelImport() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
