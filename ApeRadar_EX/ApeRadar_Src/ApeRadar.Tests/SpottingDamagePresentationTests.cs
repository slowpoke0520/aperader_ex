using ApeRadar.History;
using ApeRadar.ViewModels;
using LiveChartsCore.SkiaSharpView;
using Xunit;

namespace ApeRadar.Tests;

public sealed class SpottingDamagePresentationTests
{
    [Fact]
    public void SpottingSummaryAndTrend_KeepZeroAndExcludeUnknownValues()
    {
        BattleRecord[] battles = Enumerable.Range(1, 5).Select(HistoryPagingTests.Record).ToArray();
        Dictionary<long, BattleAdvancedMetrics> metrics = new()
        {
            [1] = Spotting(1, 0),
            [2] = Spotting(2, 30_000),
            [3] = Spotting(3, 90_000, MetricAvailability.Unavailable),
            [4] = Spotting(4, null),
            [5] = Spotting(5, 60_000, MetricAvailability.Experimental)
        };
        SessionAnalysisService analysis = new(new HistoryAnalysisService());

        HistorySummary normal = analysis.CalculateSession(new BattleSession(), battles, metrics).Metrics;
        Assert.Equal(2, normal.SpottingDamageSampleCount);
        Assert.Equal(15_000, normal.AverageSpottingDamage);
        Assert.Null(normal.AverageDamageTaken);
        Assert.Null(normal.AverageTradeRatio);

        IReadOnlyList<HistoryTrendPoint> trend = analysis.CalculateAdvancedTrend(battles, metrics, "SpottingDamage", 0, false);
        Assert.Equal(new long[] { 1, 2 }, trend.Select(x => x.BattleId));
        Assert.Equal(new[] { 0d, 15_000d }, trend.Select(x => x.Value));

        HistorySummary experimental = analysis.CalculateSession(new BattleSession(), battles, metrics, true).Metrics;
        Assert.Equal(3, experimental.SpottingDamageSampleCount);
        Assert.Equal(30_000, experimental.AverageSpottingDamage);
        Assert.Null(experimental.AverageTradeRatio);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpottingNeverSubstitutesForMissingOrUnavailableDamageTaken(bool experimental)
    {
        BattleRecord battle = HistoryPagingTests.Record(1);
        battle.Damage = 100_000;
        BattleAdvancedMetrics metrics = Spotting(1, 25_000);
        metrics.DamageTaken = 50_000;
        metrics.DamageTakenAvailability = MetricAvailability.Unavailable;

        Assert.Equal(25_000, SessionAnalysisService.MetricValue(battle, metrics, "SpottingDamage", experimental));
        Assert.Null(SessionAnalysisService.MetricValue(battle, metrics, "DamageTaken", experimental));
        Assert.Null(SessionAnalysisService.MetricValue(battle, metrics, "TradeRatio", experimental));
        HistoryRowViewModel row = new(battle, null, null, null, metrics, experimental);
        Assert.Equal("25000", row.SpottingDamage);
        Assert.Equal("-", row.DamageTaken);

        // A future verified received-damage source remains independent of spotting damage.
        metrics.DamageTakenAvailability = MetricAvailability.Stable;
        Assert.Equal(2, SessionAnalysisService.MetricValue(battle, metrics, "TradeRatio", experimental));
        metrics.DamageTaken = null;
        Assert.Null(SessionAnalysisService.MetricValue(battle, metrics, "TradeRatio", experimental));
    }

    [Fact]
    public async Task SpottingTrend_LoadsAdvancedMetricsWithoutExperimentalOptIn()
    {
        bool previous = ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = false;
            HistoryPagingTests.ReadRepository repository = new(4);
            repository.AdvancedMetrics.Add(1, Spotting(1, 0));
            repository.AdvancedMetrics.Add(2, Spotting(2, 10_000));
            repository.AdvancedMetrics.Add(3, Spotting(3, null));
            repository.AdvancedMetrics.Add(4, Spotting(4, 30_000));
            using HistoryViewModel vm = CreateViewModel(repository);
            vm.SelectedMetric = vm.MetricOptions.Single(x => x.Value == "SpottingDamage");
            vm.SelectedRollingWindow = vm.RollingWindowOptions.Single(x => x.Value == "1");

            await vm.ReloadAsync();

            LineSeries<HistoryTrendPoint> series = Assert.IsType<LineSeries<HistoryTrendPoint>>(Assert.Single(vm.ChartSeries));
            Assert.Equal(new[] { 0d, 10_000d, 30_000d }, series.Values!.Select(x => x.Value));
            Assert.Equal("0", vm.Rows.Single(x => x.Battle.Id == 1).SpottingDamage);
            Assert.Equal("-", vm.Rows.Single(x => x.Battle.Id == 3).SpottingDamage);
        }
        finally { ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = previous; }
    }

    [Fact]
    public async Task BattleDetails_ShowSpottingSeparatelyAndDoNotExposeInvalidTakenMetrics()
    {
        bool previous = ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = true;
            HistoryPagingTests.ReadRepository repository = new(2);
            BattleAdvancedMetrics first = Spotting(1, 0);
            first.DamageTaken = 777_777;
            first.DamageTakenAvailability = MetricAvailability.Unavailable;
            repository.AdvancedMetrics.Add(1, first);
            repository.AdvancedMetrics.Add(2, Spotting(2, 888_888, MetricAvailability.Unavailable));
            repository.DamageDetails.Add(new BattleDamageBreakdown
            {
                BattleId = 1, Direction = DamageDirection.Spotting, RawTypeCode = 17,
                Category = DamageCategory.Unknown, Damage = 0, Availability = MetricAvailability.Experimental
            });
            repository.DamageDetails.Add(new BattleDamageBreakdown
            {
                BattleId = 1, Direction = DamageDirection.Received, RawTypeCode = 2,
                Damage = 777_777, Availability = MetricAvailability.Unavailable
            });
            using HistoryViewModel vm = CreateViewModel(repository);
            vm.SelectedBattle = new HistoryRowViewModel(repository.Records[0], null, null, null);
            await vm.BattleDetailLoadTask;

            Assert.Equal("0", vm.DetailSpottingDamage);
            Assert.Contains("0", vm.BattleDetailAdvancedSummary);
            Assert.DoesNotContain("777777", vm.BattleDetailMetrics);
            Assert.DoesNotContain("777777", vm.BattleDetailAdvancedSummary);
            DamageBreakdownRowViewModel breakdown = Assert.Single(vm.DamageBreakdowns);
            Assert.Equal("Spotting", breakdown.Direction);
            Assert.Equal("Type #17", breakdown.Type);
            Assert.Equal("0", breakdown.Damage);

            vm.SelectedBattle = new HistoryRowViewModel(repository.Records[1], null, null, null);
            await vm.BattleDetailLoadTask;
            Assert.Equal("—", vm.DetailSpottingDamage);
            Assert.DoesNotContain("888888", vm.BattleDetailMetrics);
            Assert.DoesNotContain("888888", vm.BattleDetailAdvancedSummary);
        }
        finally { ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = previous; }
    }

    private static BattleAdvancedMetrics Spotting(long id, long? damage, MetricAvailability availability = MetricAvailability.Stable) => new()
    {
        BattleId = id, SpottingDamage = damage, SpottingDamageAvailability = availability, ParserSchemaVersion = "4"
    };

    private static HistoryViewModel CreateViewModel(IHistoryRepository repository)
    {
        HistoryAnalysisService analysis = new();
        return new(repository, analysis, new SessionAnalysisService(analysis), new ImprovementInsightService(analysis),
            new HistoryPagingTests.IdleTrackingCoordinator(), "Microsoft YaHei");
    }
}
