using ApeRadar.History;
using System.Reflection;
using Xunit;

namespace ApeRadar.Tests;

public sealed class CompositionRootTests
{
    [Fact]
    public void AppAndWindowsUseAnInstanceScopedHistoryServiceGraph()
    {
        PropertyInfo appServices = Assert.IsAssignableFrom<PropertyInfo>(typeof(App).GetProperty(
            nameof(App.HistoryServices), BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.False(appServices.GetMethod!.IsStatic);

        Assert.NotNull(typeof(MainWindow).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(HistoryServices), typeof(bool) },
            modifiers: null));
        Assert.NotNull(typeof(HistoryWindow).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(HistoryServices), typeof(bool) },
            modifiers: null));
    }

    [Fact]
    public async Task HistoryServiceScopesHaveIndependentStateAndIdempotentDisposal()
    {
        HistoryServices first = new();
        HistoryServices second = new();

        Assert.NotSame(first.Repository, second.Repository);
        Assert.NotSame(first.Coordinator, second.Coordinator);

        await first.DisposeAsync();
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    [Fact]
    public async Task ChangingGamePathReplacesOnlyTheCoordinatorInTheCurrentScope()
    {
        HistoryAnalysisService analysis = new();
        List<RecordingCoordinator> coordinators = new();
        HistoryServices services = new(
            new SqliteHistoryRepository(),
            analysis,
            new SessionAnalysisService(analysis),
            new ImprovementInsightService(analysis),
            () =>
            {
                RecordingCoordinator coordinator = new();
                coordinators.Add(coordinator);
                return coordinator;
            });

        await services.InitializeAsync("C:\\Games\\WorldOfWarships");
        await services.InitializeAsync("c:\\games\\worldofwarships");

        RecordingCoordinator first = Assert.Single(coordinators);
        Assert.Equal(1, first.InitializeCount);
        Assert.False(first.IsDisposed);

        await services.InitializeAsync("D:\\Games\\WorldOfWarships");

        Assert.True(first.IsDisposed);
        Assert.Equal(2, coordinators.Count);
        Assert.Equal(1, coordinators[1].InitializeCount);

        await services.DisposeAsync();
        Assert.True(coordinators[1].IsDisposed);
    }

    private sealed class RecordingCoordinator : IBattleTrackingCoordinator
    {
        public IReplayMonitor ReplayMonitor { get; } = new NullReplayMonitor();
        public int InitializeCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task InitializeAsync(string gamePath, CancellationToken cancellationToken = default)
        {
            InitializeCount++;
            return Task.CompletedTask;
        }

        public Task CapturePreBattleAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RetryPendingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
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
