using ApeRadar.History;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class TimingAnalysisTests
{
    [Theory]
    [InlineData(true, 6_000)]
    [InlineData(false, 12_000)]
    public void DamagePerMinute_UsesOnlyTheCalibratedTimeForTheKnownOutcome(bool survived, double expected)
    {
        BattleRecord battle = Battle();
        BattleAdvancedMetrics metric = Timing(survived);
        if (survived)
        {
            metric.SurvivalSeconds = null;
            metric.SurvivalTimeSource = ReplayTimingSource.Unknown;
        }
        else
        {
            metric.BattleDurationSeconds = null;
            metric.BattleDurationSource = ReplayTimingSource.Unknown;
        }
        Assert.Equal(expected, SessionAnalysisService.MetricValue(battle, metric, "DamagePerMinute", false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DamagePerMinute_RejectsUncalibratedSelectedTimeEvenWhenSurvivalStateIsKnown(bool survived)
    {
        BattleAdvancedMetrics metric = Timing(survived);
        if (survived) metric.BattleDurationSource = ReplayTimingSource.Unknown;
        else metric.SurvivalTimeSource = ReplayTimingSource.Unknown;
        Assert.Null(SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", false));
        Assert.Null(SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", true));
    }

    [Fact]
    public void DamagePerMinute_DoesNotTreatUnknownSurvivalAsSurvived()
    {
        BattleAdvancedMetrics metric = Timing(true);
        metric.Survived = null;
        Assert.Null(SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", true));
    }

    [Fact]
    public void CompleteApiSingleBattle_CanUseCalibratedReplayLifeTime()
    {
        BattleRecord battle = Battle();
        battle.Source = BattleMetricSource.ApiExact;
        Assert.Equal(12_000, SessionAnalysisService.MetricValue(battle, Timing(false), "DamagePerMinute", false));
    }

    [Fact]
    public void KnownZeroLifetime_RemainsZeroButCannotBeADivisor()
    {
        BattleAdvancedMetrics metric = Timing(false);
        metric.SurvivalSeconds = 0;
        Assert.Equal(0, SessionAnalysisService.MetricValue(Battle(), metric, "SurvivalTime", false));
        Assert.Null(SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", false));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    public void DamagePerMinute_NeverDividesByInvalidOrZeroSeconds(double seconds)
    {
        foreach (bool survived in new[] { false, true })
        {
            BattleAdvancedMetrics metric = Timing(survived);
            metric.BattleDurationSeconds = seconds;
            metric.SurvivalSeconds = seconds;
            Assert.Null(SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", false));
        }
    }

    [Theory]
    [InlineData((int)BattleCompleteness.Partial)]
    [InlineData((int)BattleCompleteness.Pending)]
    [InlineData((int)BattleCompleteness.Unsupported)]
    [InlineData((int)BattleCompleteness.Failed)]
    public void DamagePerMinute_DoesNotCombinePartialDamageWithACompleteTimeWindow(int completeness)
    {
        BattleRecord battle = Battle();
        battle.Completeness = (BattleCompleteness)completeness;
        Assert.Null(SessionAnalysisService.MetricValue(battle, Timing(false), "DamagePerMinute", true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DamagePerMinute_RequiresExactlyOneBattle(int count)
    {
        BattleRecord battle = Battle();
        battle.BattleCount = count;
        Assert.Null(SessionAnalysisService.MetricValue(battle, Timing(true), "DamagePerMinute", false));
    }

    [Fact]
    public void DamagePerMinute_PreservesKnownZeroButRejectsMissingAndNegativeDamage()
    {
        BattleRecord battle = Battle();
        BattleAdvancedMetrics metric = Timing(true);
        battle.Damage = 0;
        Assert.Equal(0, SessionAnalysisService.MetricValue(battle, metric, "DamagePerMinute", false));
        battle.Damage = null;
        Assert.Null(SessionAnalysisService.MetricValue(battle, metric, "DamagePerMinute", false));
        battle.Damage = -1;
        Assert.Null(SessionAnalysisService.MetricValue(battle, metric, "DamagePerMinute", false));
    }

    [Fact]
    public void TimingAvailability_RespectsTheExperimentalSwitch()
    {
        BattleAdvancedMetrics metric = Timing(false);
        metric.SurvivalAvailability = MetricAvailability.Experimental;
        Assert.Null(SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", false));
        Assert.Equal(12_000, SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", true));
        metric.SurvivalAvailability = MetricAvailability.Unavailable;
        Assert.Null(SessionAnalysisService.MetricValue(Battle(), metric, "DamagePerMinute", true));
    }

    [Fact]
    public void SurvivalSummary_KeepsKnownOutcomeButExcludesUncalibratedLifetime()
    {
        BattleRecord[] battles = { Battle(1), Battle(2) };
        BattleAdvancedMetrics unknownTime = Timing(false);
        unknownTime.SurvivalSeconds = 123;
        unknownTime.SurvivalTimeSource = ReplayTimingSource.Unknown;
        BattleAdvancedMetrics knownTime = Timing(true);
        knownTime.SurvivalSeconds = 600;
        var metrics = new Dictionary<long, BattleAdvancedMetrics> { [1] = unknownTime, [2] = knownTime };
        SessionSummary summary = new SessionAnalysisService(new HistoryAnalysisService()).CalculateSession(new(), battles, metrics);
        Assert.Equal(2, summary.Metrics.SurvivalSampleCount);
        Assert.Equal(0.5, summary.Metrics.SurvivalRate);
        Assert.Equal(600, summary.Metrics.AverageSurvivalSeconds);
        Assert.Null(SessionAnalysisService.MetricValue(battles[0], unknownTime, "SurvivalTime", false));
    }

    [Fact]
    public void DamagePerMinuteTrend_ExcludesUnknownTimeAndPartialDamageButKeepsKnownZero()
    {
        BattleRecord[] battles = Enumerable.Range(1, 4).Select(Battle).ToArray();
        battles[0].Damage = 0;
        battles[2].Completeness = BattleCompleteness.Partial;
        var metrics = battles.ToDictionary(b => b.Id, _ => Timing(true));
        metrics[4].BattleDurationSource = ReplayTimingSource.Unknown;
        SessionAnalysisService service = new(new HistoryAnalysisService());
        IReadOnlyList<HistoryTrendPoint> trend = service.CalculateAdvancedTrend(battles, metrics, "DamagePerMinute", 1, false);
        Assert.Equal(new long[] { 1, 2 }, trend.Select(p => p.BattleId));
        Assert.Equal(new[] { 0d, 6_000d }, trend.Select(p => p.Value));
        Assert.Equal(3_000, service.CalculateSession(new(), battles, metrics).Metrics.AverageDamagePerMinute);
    }

    [Theory]
    [InlineData("unknown-source")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("negative")]
    public async Task DetailText_DoesNotExposeOrThrowOnUnusableSurvivalTime(string invalid)
    {
        bool previous = ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = false;
            HistoryPagingTests.ReadRepository repository = new(1);
            BattleAdvancedMetrics metric = Timing(false);
            metric.SurvivalSeconds = invalid switch
            {
                "nan" => double.NaN,
                "infinity" => double.PositiveInfinity,
                "negative" => -1,
                _ => 123
            };
            if (invalid == "unknown-source") metric.SurvivalTimeSource = ReplayTimingSource.Unknown;
            repository.AdvancedMetrics[1] = metric;
            HistoryAnalysisService analysis = new();
            using HistoryViewModel vm = new(repository, analysis, new SessionAnalysisService(analysis),
                new ImprovementInsightService(analysis), new HistoryPagingTests.IdleTrackingCoordinator(), "Microsoft YaHei");
            vm.SelectedBattle = new HistoryRowViewModel(repository.Records[0], null, null, null);
            await vm.BattleDetailLoadTask;
            Assert.Contains("Survival: Sunk", vm.BattleDetailMetrics);
            Assert.Contains("Survival time: -", vm.BattleDetailMetrics);
            Assert.DoesNotContain("02:03", vm.BattleDetailAdvancedSummary);
            Assert.DoesNotContain("NaN", vm.BattleDetailMetrics);
            Assert.DoesNotContain("Infinity", vm.BattleDetailMetrics);
            Assert.Equal("—", vm.DetailSurvivalTime);
            Assert.Equal("Sunk", vm.DetailSurvival);
        }
        finally { ApeRadar.Properties.Settings.Default.ShowExperimentalReplayMetrics = previous; }
    }

    private static BattleRecord Battle(int id = 1) => new()
    {
        Id = id, StartedAt = DateTimeOffset.UnixEpoch.AddMinutes(id), Damage = 60_000, BattleCount = 1,
        Source = BattleMetricSource.ReplayExact, Completeness = BattleCompleteness.Complete, Result = BattleResult.Win
    };

    private static BattleAdvancedMetrics Timing(bool survived) => new()
    {
        Survived = survived, SurvivalAvailability = MetricAvailability.Stable,
        BattleDurationSeconds = 600, SurvivalSeconds = survived ? 600 : 300,
        BattleDurationSource = ReplayTimingSource.BattleResults, SurvivalTimeSource = ReplayTimingSource.BattleClock,
        ParserSchemaVersion = "7"
    };
}
