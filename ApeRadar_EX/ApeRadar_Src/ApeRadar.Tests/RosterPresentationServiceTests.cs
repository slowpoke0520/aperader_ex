using System.Collections;
using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterPresentationServiceTests
{
    [Fact]
    public void CreateRows_GroupsCompleteStatisticsIntoThreeReadableLines()
    {
        Player player = CreatePlayer("Complete", 8_000);
        RosterPresentationOptions options = new(0, 0, 0, 0, 0, 0, 0, true);

        PlayerRosterRowViewModel row = Assert.Single(new RosterPresentationService().CreateRows(new[] { player }, options));

        Assert.Equal(3, row.AccountMetrics.Lines.Count);
        Assert.Equal(3, row.ShipMetrics.Lines.Count);
        Assert.Equal(3, row.TierMetrics.Lines.Count);
        Assert.Contains(row.AccountMetrics.Lines.SelectMany(line => line.Items), item => item.Kind == RosterMetricKind.PersonalRating);
        Assert.Contains(row.ShipMetrics.Lines.SelectMany(line => line.Items), item => item.Kind == RosterMetricKind.DamageRating);
        Assert.Contains("Small", row.StatusSummary, StringComparison.OrdinalIgnoreCase);
        Assert.True(row.HasAttention);
    }

    [Fact]
    public void CreateRows_RemovesHiddenGroupsWithoutLeavingEmptyLines()
    {
        Player player = CreatePlayer("Hidden columns", 8_000);
        RosterPresentationOptions options = new(2, 2, 2, 2, 2, 2, 2, false);

        PlayerRosterRowViewModel row = Assert.Single(new RosterPresentationService().CreateRows(new[] { player }, options));

        Assert.False(row.AccountMetrics.IsVisible);
        Assert.False(row.ShipMetrics.IsVisible);
        Assert.False(row.TierMetrics.IsVisible);
    }

    [Fact]
    public void PlayerRosterRowComparer_SortsUsingUnderlyingPlayerValues()
    {
        Player first = CreatePlayer("First", 2_000);
        Player second = CreatePlayer("Second", 9_000);
        RosterPresentationOptions options = new(0, 0, 0, 0, 0, 0, 0, true);
        IReadOnlyList<PlayerRosterRowViewModel> rows = new RosterPresentationService().CreateRows(new[] { first, second }, options);
        PlayerRosterRowComparer comparer = new(new BattlesDescendingComparer());

        Assert.True(comparer.Compare(rows[0], rows[1]) > 0);
        Assert.True(comparer.Compare(rows[1], rows[0]) < 0);
    }

    private static Player CreatePlayer(string name, double battles) => new(name, "1", Server.ASIA, WatchStatus.NONE)
    {
        Relation = "1",
        ShipID = "3760142160",
        ShipName = "Yamato",
        ShipType = "Battleship",
        ShipTier = 10,
        Battles = battles,
        AccountWinrate = 0.58,
        AvgExpPerBattle = 2_100,
        WeightedWinrate = 0.55,
        PR = 1_650,
        ShipBattles = 420,
        ShipWinrate = 0.56,
        ShipAvgDmgPerBattle = 106_000,
        ShipAvgExpPerBattle = 2_200,
        ShipPR = 1_520,
        TierBattles = 34,
        TierWinrate = 0.59,
        TierPR = 1_710,
        IsTierSampleSmall = true,
        IsLowTierBiased = true
    };

    private sealed class BattlesDescendingComparer : IComparer
    {
        public int Compare(object? x, object? y) =>
            -Comparer<double>.Default.Compare(((Player)x!).Battles, ((Player)y!).Battles);
    }
}
