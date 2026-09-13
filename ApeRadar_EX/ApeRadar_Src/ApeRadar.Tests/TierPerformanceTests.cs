using ApeRadar.Utils;
using Xunit;

namespace ApeRadar.Tests;

public sealed class TierPerformanceTests
{
    [Fact]
    public void Calculate_UsesExactCurrentTierAndDetectsLowTierBias()
    {
        TierPerformanceSummary summary = TierPerformanceUtils.Calculate(10, new[]
        {
            Ship("tier-5-a", 5, 3_000, 1_800),
            Ship("tier-5-b", 5, 1_000, 600),
            Ship("tier-10-a", 10, 60, 30),
            Ship("tier-10-b", 10, 40, 18),
        });

        Assert.Equal(100, summary.Battles);
        Assert.Equal(48, summary.Wins);
        Assert.Equal(0.48, summary.Winrate, 6);
        Assert.Equal(5, summary.MostPlayedTier);
        Assert.Equal(4_000, summary.MostPlayedBattles);
        Assert.Equal(4_000d / 4_100d, summary.MostPlayedShare, 6);
        Assert.True(summary.IsLowTierBiased);
    }

    [Fact]
    public void Calculate_TierElevenKeepsExactDataAndAddsTenToElevenReference()
    {
        TierPerformanceSummary summary = TierPerformanceUtils.Calculate(11, new[]
        {
            Ship("tier-10", 10, 195, 105),
            Ship("tier-11", 11, 5, 4),
        });

        Assert.Equal(5, summary.Battles);
        Assert.Equal(0.8, summary.Winrate, 6);
        Assert.True(summary.IsSmallSample);
        Assert.True(summary.HasReference);
        Assert.Equal(10, summary.ReferenceMinTier);
        Assert.Equal(11, summary.ReferenceMaxTier);
        Assert.Equal(200, summary.ReferenceBattles);
        Assert.Equal(109d / 200d, summary.ReferenceWinrate, 6);
        Assert.True(summary.ReferencePr >= -1);
    }

    [Fact]
    public void Calculate_CrossTierBattleStillUsesEachPlayersOwnShipTier()
    {
        TierPerformanceSummary summary = TierPerformanceUtils.Calculate(8, new[]
        {
            Ship("tier-8", 8, 120, 66),
            Ship("tier-9", 9, 900, 540),
            Ship("tier-10", 10, 500, 300),
        });

        Assert.Equal(120, summary.Battles);
        Assert.Equal(0.55, summary.Winrate, 6);
        Assert.False(summary.IsSmallSample);
        Assert.False(summary.HasReference);
    }

    [Fact]
    public void Calculate_ExactSampleAtThresholdDoesNotShowReference()
    {
        TierPerformanceSummary summary = TierPerformanceUtils.Calculate(7, new[]
        {
            Ship("tier-6", 6, 500, 260),
            Ship("tier-7", 7, TierPerformanceUtils.MinimumReliableBattles, 25),
            Ship("tier-8", 8, 500, 270),
        });

        Assert.False(summary.IsSmallSample);
        Assert.False(summary.HasReference);
    }

    [Fact]
    public void Calculate_UnknownCurrentTierDoesNotBorrowAnotherTier()
    {
        TierPerformanceSummary summary = TierPerformanceUtils.Calculate(0, new[]
        {
            Ship("tier-1", 1, 100, 60),
            Ship("invalid", 12, 100, 100),
            Ship("empty", 10, 0, 0),
        });

        Assert.Equal(-1, summary.Battles);
        Assert.Equal(-1, summary.Winrate);
        Assert.False(summary.IsSmallSample);
        Assert.False(summary.HasReference);
        Assert.Equal(0, summary.MostPlayedTier);
    }

    private static TierShipStatistics Ship(string id, int tier, double battles, double wins) =>
        new(id, tier, battles, wins, battles * 50_000, battles);
}
