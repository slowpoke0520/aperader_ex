using ApeRadar.History;
using ApeRadar.Utils;
using Xunit;

namespace ApeRadar.Tests;

public sealed class HistoryAnalysisTests
{
    private readonly HistoryAnalysisService service = new();

    [Fact]
    public void Summary_WeightsMergedIntervalsByBattleCount()
    {
        BattleRecord[] battles =
        {
            Complete(1, 1, 100_000, 2, 1),
            Complete(2, 3, 240_000, 3, 2, BattleMetricSource.ApiMerged)
        };

        HistorySummary summary = service.CalculateSummary(battles);

        Assert.Equal(4, summary.RecordedBattles);
        Assert.Equal(0.75, summary.Winrate);
        Assert.Equal(85_000, summary.AverageDamage);
        Assert.Equal(1.25, summary.AverageFrags);
        Assert.Equal(1, summary.CompletenessRate);
    }

    [Fact]
    public void RollingWinrate_UsesSelectedBattleWindow()
    {
        BattleRecord[] battles =
        {
            Complete(1, 1, 1, 0, 1),
            Complete(2, 1, 1, 0, 0),
            Complete(3, 1, 1, 0, 1)
        };

        IReadOnlyList<HistoryTrendPoint> trend = service.CalculateTrend(battles, "Winrate", 2);

        Assert.Equal(new[] { 1d, .5d, .5d }, trend.Select(x => x.Value).ToArray());
    }

    [Fact]
    public void MetadataOnlyRows_DoNotCountAsComplete()
    {
        BattleRecord metadata = Complete(1, 1, 0, 0, 0);
        metadata.Source = BattleMetricSource.MetadataOnly;
        metadata.Completeness = BattleCompleteness.Pending;

        HistorySummary summary = service.CalculateSummary(new[] { metadata });

        Assert.Equal(1, summary.RecordedBattles);
        Assert.Equal(0, summary.EffectiveBattles);
        Assert.Equal(0, summary.CompletenessRate);
    }

    [Theory]
    [InlineData(400, 1000, 0.4, 0)]
    [InlineData(1000, 1000, 0.4, 1150)]
    [InlineData(1600, 1000, 0.4, 2300)]
    [InlineData(100, 1000, 0.1, 0)]
    [InlineData(1000, 1000, 0.1, 1150)]
    public void MetricRating_MapsExpectedValueToAveragePrBand(double actual, double expected, double floor, double rating)
    {
        Assert.Equal(rating, PRUtils.CalculateMetricRating(actual, expected, floor), 6);
    }

    [Theory]
    [InlineData("37_Ridge", "山脉锁链")]
    [InlineData("spaces/01_solomon_islands", "狂鲨怒湾")]
    [InlineData("IDS_MAP_20_NE_TWO_BROTHERS", "双峰海峡")]
    [InlineData("Mountain Range", "山脉锁链")]
    public void MapNames_UseAsiaOfficialChineseNames(string internalName, string expected)
    {
        Assert.Equal(expected, HistoryMapNameLocalizer.GetDisplayName(internalName, true));
    }

    [Fact]
    public void UnknownMapName_IsPreserved()
    {
        Assert.Equal("future_map", HistoryMapNameLocalizer.GetDisplayName("future_map", true));
    }

    [Fact]
    public void SessionSummary_IgnoresUnavailableAdvancedMetrics()
    {
        BattleRecord first = Complete(1, 1, 120_000, 2, 1);
        BattleRecord second = Complete(2, 1, 60_000, 0, 0);
        Dictionary<long, BattleAdvancedMetrics> advanced = new()
        {
            [1] = new() { BattleId = 1, Survived = true, SurvivalSeconds = 900, BattleDurationSeconds = 900, PotentialDamage = 1_000_000, SurvivalAvailability = MetricAvailability.Stable, PotentialDamageAvailability = MetricAvailability.Stable },
            [2] = new() { BattleId = 2, Survived = null, PotentialDamage = 0, SurvivalAvailability = MetricAvailability.Unavailable, PotentialDamageAvailability = MetricAvailability.Unavailable }
        };
        SessionAnalysisService sessionAnalysis = new(service);

        SessionSummary summary = sessionAnalysis.CalculateSession(new BattleSession(), new[] { first, second }, advanced);

        Assert.Equal(1, summary.Metrics.SurvivalSampleCount);
        Assert.Equal(1, summary.Metrics.SurvivalRate);
        Assert.Equal(1, summary.Metrics.PotentialDamageSampleCount);
        Assert.Equal(1_000_000, summary.Metrics.AveragePotentialDamage);
        Assert.Equal(8_000, summary.Metrics.AverageDamagePerMinute);
    }

    [Fact]
    public void ExperimentalMetrics_AreHiddenUnlessEnabled()
    {
        BattleRecord battle = Complete(1, 1, 100_000, 1, 1);
        BattleAdvancedMetrics metric = new() { BattleId = 1, DamageTaken = 50_000, DamageTakenAvailability = MetricAvailability.Experimental };

        Assert.Null(SessionAnalysisService.MetricValue(battle, metric, "TradeRatio", false));
        Assert.Equal(2, SessionAnalysisService.MetricValue(battle, metric, "TradeRatio", true));

        SessionAnalysisService sessionAnalysis = new(service);
        Dictionary<long, BattleAdvancedMetrics> advanced = new() { [battle.Id] = metric };
        Assert.Null(sessionAnalysis.CalculateSession(new BattleSession(), new[] { battle }, advanced).Metrics.AverageTradeRatio);
        Assert.Equal(2, sessionAnalysis.CalculateSession(new BattleSession(), new[] { battle }, advanced, true).Metrics.AverageTradeRatio);
    }

    [Fact]
    public void Insights_RequireFiveCurrentAndTwentyHistoricalBattlesOfSameShip()
    {
        ImprovementInsightService insights = new(service);
        BattleRecord[] current = Enumerable.Range(1, 5).Select(i => Complete(i, 1, 110_000, 1, 1)).ToArray();
        BattleRecord[] baseline = Enumerable.Range(100, 20).Select(i => Complete(i, 1, 100_000, 1, 1)).ToArray();

        IReadOnlyList<ImprovementInsight> result = insights.CreateInsights(current, new Dictionary<long, BattleAdvancedMetrics>(), baseline, new Dictionary<long, BattleAdvancedMetrics>(), false);

        Assert.Contains(result, x => x.Metric.EndsWith("|Damage") && x.Kind == ImprovementInsightKind.Positive);
        Assert.DoesNotContain(result, x => x.Metric == "CollectingData");
    }

    private static BattleRecord Complete(long id, int count, long damage, double frags, double wins, BattleMetricSource source = BattleMetricSource.ApiExact) => new()
    {
        Id = id, BattleKey = $"battle-{id}", StartedAt = DateTimeOffset.UtcNow.AddMinutes(id), Server = "ASIA", Mode = "random",
        AccountId = "1", AccountName = "Tester", ShipId = "101", ShipName = "Yamato", Damage = damage, Frags = frags,
        WinCount = wins, BattleCount = count, Source = source, Completeness = source == BattleMetricSource.ApiMerged ? BattleCompleteness.Partial : BattleCompleteness.Complete
    };
}
