using ApeRadar.History;
using ApeRadar.ViewModels;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace ApeRadar.Tests;

public sealed class HistoryPagingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Paging_ReadsOnlyThePageAndKeepsSummaryAndTrend()
    {
        ReadRepository repository = new();
        using HistoryViewModel vm = CreateViewModel(repository);
        await vm.ReloadAsync();
        Assert.Equal(100, vm.Rows.Count);
        Assert.Equal(3, vm.TotalPages);
        var originalSeries = vm.ChartSeries;
        string summary = vm.RecordedBattlesText;
        await vm.NextPageAsync();
        await vm.NextPageAsync();
        await vm.PreviousPageAsync();
        Assert.Equal(1, repository.AllReads);
        Assert.Equal(1, repository.CountReads);
        Assert.Equal(4, repository.PageReads);
        Assert.Same(originalSeries, vm.ChartSeries);
        Assert.Equal(summary, vm.RecordedBattlesText);
        Assert.Equal("2 / 3", vm.PageText);
        Assert.Equal(140, vm.Rows[0].Battle.Id);
        Assert.False(vm.IsBusy);

        // Explicit refresh must see an import into an otherwise identical query.
        repository.Records.Add(Record(241));
        await vm.ReloadAsync();
        Assert.Equal(2, repository.AllReads);
        Assert.Equal(2, repository.CountReads);
        Assert.Equal("241", vm.RecordedBattlesText);
    }

    [Fact]
    public async Task FilterAndMetricChanges_CannotReuseAPreviousQuerySnapshot()
    {
        ReadRepository repository = new();
        using HistoryViewModel vm = CreateViewModel(repository);
        await vm.ReloadAsync();
        vm.SelectedServer = new HistoryFilterOption { Value = "EU", Display = "EU" };
        // Even if a navigation event follows a selection, key matching prevents reuse.
        await vm.NextPageAsync();
        Assert.Empty(vm.Rows);
        Assert.Equal(1, vm.TotalPages);
        Assert.Equal("0", vm.RecordedBattlesText);
        Assert.Equal(2, repository.AllReads);
        Assert.Empty(vm.ChartSeries);

        vm.SelectedServer = null;
        vm.SelectedMetric = vm.MetricOptions.Single(x => x.Value == "Damage");
        await vm.ReloadAsync();
        Assert.Equal(3, repository.AllReads);
        Assert.Equal("240", vm.RecordedBattlesText);
        Assert.Single(vm.ChartSeries);
    }

    [Fact]
    public async Task LateCancelledAnalysis_DoesNotOverwriteNewFilterOrBusyState()
    {
        ReadRepository repository = new();
        using HistoryViewModel vm = CreateViewModel(repository);
        repository.HoldAllReads = true;
        Task oldLoad = vm.ReloadAsync();
        await repository.AllReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.SelectedServer = new HistoryFilterOption { Value = "EU", Display = "EU" };
        repository.HoldAllReads = false;
        await vm.ReloadAsync();
        Assert.False(vm.IsBusy);
        string newestStatus = vm.StatusText;
        repository.ReleaseAllRead.SetResult(); // simulates a provider ignoring cancellation
        await oldLoad;
        Assert.Equal("0", vm.RecordedBattlesText);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.ChartSeries);
        Assert.Equal(newestStatus, vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task TenThousandRecords_AnalysisRunsOnceAcrossTenPages()
    {
        ReadRepository repository = new(10_000);
        using HistoryViewModel vm = CreateViewModel(repository);
        Stopwatch timer = Stopwatch.StartNew();
        await vm.ReloadAsync();
        long initialMs = timer.ElapsedMilliseconds;
        timer.Restart();
        for (int i = 0; i < 10; i++) await vm.NextPageAsync();
        output.WriteLine($"10,000 history records: initial query/analysis {initialMs} ms; 10 pages {timer.ElapsedMilliseconds} ms; all reads {repository.AllReads}; page reads {repository.PageReads}.");
        Assert.Equal(1, repository.AllReads);
        Assert.Equal(1, repository.CountReads);
        Assert.Equal(11, repository.PageReads);
        Assert.Equal(100, vm.Rows.Count);
        Assert.Equal(100, vm.TotalPages);
    }

    [Fact]
    public async Task AnalysisCancellation_StopsTheCumulativeCurve()
    {
        var records = Enumerable.Range(1, 10_000).Select(Record).ToArray();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new HistoryAnalysisService().CalculateTrend(records, "Damage", 0, cancellation.Token));
        SessionAnalysisService advanced = new(new HistoryAnalysisService());
        Assert.Throws<OperationCanceledException>(() => advanced.CalculateAdvancedTrend(records,
            new Dictionary<long, BattleAdvancedMetrics>(), "PotentialDamage", 0, false, cancellation.Token));
        await Task.CompletedTask;
    }

    private static HistoryViewModel CreateViewModel(ReadRepository repository)
    {
        HistoryAnalysisService analysis = new();
        return new(repository, analysis, new SessionAnalysisService(analysis),
            new ImprovementInsightService(analysis), new IdleTrackingCoordinator(), "Microsoft YaHei");
    }

    internal static BattleRecord Record(int id) => new()
    {
        Id = id, BattleKey = $"battle-{id}", Server = "ASIA", AccountId = "1", AccountName = "Tester",
        ShipId = "101", ShipName = "A long ship name", ShipType = "Battleship",
        StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(id),
        BattleCount = 1, WinCount = id % 2, Damage = 90_000 + id, Frags = 1,
        Source = BattleMetricSource.ApiExact, Completeness = BattleCompleteness.Complete
    };

    internal sealed class ReadRepository : HistoryReadTestRepository
    {
        public List<BattleRecord> Records { get; }
        public int AllReads, PageReads, CountReads;
        public bool HoldAllReads;
        public TaskCompletionSource AllReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseAllRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReadRepository(int count = 240) => Records = Enumerable.Range(1, count).Select(Record).ToList();
        private IEnumerable<BattleRecord> Query(HistoryQuery query) => Records.Where(x =>
            (string.IsNullOrEmpty(query.Server) || x.Server == query.Server) &&
            (string.IsNullOrEmpty(query.ShipId) || x.ShipId == query.ShipId));
        public override Task<int> CountBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default)
        {
            CountReads++;
            return Task.FromResult(Query(query).Count());
        }
        public override async Task<IReadOnlyList<BattleRecord>> GetBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default)
        {
            var result = (query.Descending ? Query(query).OrderByDescending(x => x.StartedAt) : Query(query).OrderBy(x => x.StartedAt)).ToArray();
            if (query.Limit is int limit)
            {
                PageReads++;
                return result.Skip(query.Offset).Take(limit).ToArray();
            }
            AllReads++;
            if (HoldAllReads) { AllReadStarted.TrySetResult(); await ReleaseAllRead.Task; }
            return result;
        }
    }

    internal sealed class IdleTrackingCoordinator : IBattleTrackingCoordinator, IReplayMonitor
    {
        public IReplayMonitor ReplayMonitor => this;
        public event EventHandler<ReplayImportProgress>? ImportProgressChanged { add { } remove { } }
        public Task InitializeAsync(string gamePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CapturePreBattleAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RetryPendingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(string gamePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RescanAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RetryFailedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void CancelImport() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
