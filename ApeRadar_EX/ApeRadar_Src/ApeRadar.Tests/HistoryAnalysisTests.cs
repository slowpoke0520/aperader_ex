using ApeRadar.History;
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

    private static BattleRecord Complete(long id, int count, long damage, double frags, double wins, BattleMetricSource source = BattleMetricSource.ApiExact) => new()
    {
        Id = id, BattleKey = $"battle-{id}", StartedAt = DateTimeOffset.UtcNow.AddMinutes(id), Server = "ASIA", Mode = "random",
        AccountId = "1", AccountName = "Tester", ShipId = "101", ShipName = "Yamato", Damage = damage, Frags = frags,
        WinCount = wins, BattleCount = count, Source = source, Completeness = source == BattleMetricSource.ApiMerged ? BattleCompleteness.Partial : BattleCompleteness.Complete
    };
}
