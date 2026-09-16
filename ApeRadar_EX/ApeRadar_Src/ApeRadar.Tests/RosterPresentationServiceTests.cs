using System.Collections;
using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterPresentationServiceTests
{
    [Fact]
    public void CreateRows_GroupsStatisticsInStableReadableOrder()
    {
        Player player = CreatePlayer("Complete", 8_000);
        RosterPresentationOptions options = new(0, 0, 0, 0, 0, 0, 0, true);

        PlayerRosterRowViewModel row = CreateRow(player, options);

        Assert.Equal(3, row.AccountMetrics.Lines.Count);
        Assert.Equal(3, row.ShipMetrics.Lines.Count);
        Assert.Equal(2, row.TierMetrics.Lines.Count);
        Assert.Equal(new[] { RosterMetricKind.Winrate, RosterMetricKind.PersonalRating }, row.AccountMetrics.Lines[0].Items.Select(item => item.Kind));
        Assert.All(row.AccountMetrics.Lines[0].Items, item => Assert.Equal(RosterMetricEmphasis.Primary, item.Emphasis));
        Assert.Equal(RosterMetricKind.DamageRating, row.ShipMetrics.Lines[1].Items[1].Kind);
        Assert.Empty(row.StatusBadges);
        Assert.Equal(string.Empty, row.ContextPreview);
    }

    [Fact]
    public void CompactDensity_KeepsOnlyTwoCoreLines()
    {
        PlayerRosterRowViewModel row = CreateRow(CreatePlayer("Compact", 8_000),
            new RosterPresentationOptions(0, 0, 0, 0, 0, 0, 0, true, RosterDisplayDensity.Compact));

        Assert.Equal(2, row.AccountMetrics.Lines.Count);
        Assert.Equal(2, row.ShipMetrics.Lines.Count);
        Assert.DoesNotContain(row.AccountMetrics.Lines.SelectMany(line => line.Items), item => item.Emphasis == RosterMetricEmphasis.Tertiary);
        Assert.DoesNotContain(row.ShipMetrics.Lines.SelectMany(line => line.Items), item => item.Emphasis == RosterMetricEmphasis.Tertiary);
    }

    [Fact]
    public void SmallTierSample_WarnsOnceBesideTierBattleCount()
    {
        Player player = CreatePlayer("Small sample", 8_000);
        player.IsTierSampleSmall = true;

        PlayerRosterRowViewModel row = CreateRow(player, new(0, 0, 0, 0, 0, 0, 0, true));
        MetricItemViewModel tierBattles = Assert.Single(row.TierMetrics.Lines[1].Items);

        Assert.Equal("⚠", tierBattles.WarningGlyph);
        Assert.NotEmpty(tierBattles.ToolTip);
        Assert.DoesNotContain(row.StatusBadges, badge => badge.Kind == RosterBadgeKind.LowTierBias);
        Assert.DoesNotContain("sample", row.ContextPreview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StatusBadges_UsePriorityAndCollapseAfterThree()
    {
        Player player = CreatePlayer("Badges", 8_000);
        player.WatchStatus = WatchStatus.CHEATER;
        player.IsCustomMarked = true;
        player.RecentEncounterCount = 2;
        player.IsHidden = true;
        player.IsDataStale = true;
        player.IsLowTierBiased = true;

        PlayerRosterRowViewModel row = CreateRow(player, new(0, 0, 0, 0, 0, 0, 0, true));

        Assert.Equal(new[] { RosterBadgeKind.Watch, RosterBadgeKind.CustomMark, RosterBadgeKind.RecentEncounter }, row.VisibleStatusBadges.Select(badge => badge.Kind));
        Assert.Equal(3, row.OverflowBadgeCount);
        Assert.Equal("+3", row.OverflowBadgeText);
        Assert.Contains(Environment.NewLine, row.AllStatusToolTip);
    }

    [Fact]
    public void ContextPreview_PrefersNoteAndFixedTeammateReplacesRecentBadge()
    {
        Player player = CreatePlayer("Context", 8_000);
        player.Note = "Reliable caller";
        player.RecentEncounterCount = 4;
        player.IsFixedTeammate = true;

        PlayerRosterRowViewModel row = CreateRow(player, new(0, 0, 0, 0, 0, 0, 0, true));

        Assert.Contains("Reliable caller", row.ContextPreview);
        Assert.Contains(row.StatusBadges, badge => badge.Kind == RosterBadgeKind.FixedTeammate);
        Assert.DoesNotContain(row.StatusBadges, badge => badge.Kind == RosterBadgeKind.RecentEncounter);
    }

    [Fact]
    public void LegacyPerformanceBadge_IsOffByDefaultAndOptional()
    {
        Player player = CreatePlayer("Legacy", 8_000);
        double oldThreshold = ApeRadar.Properties.Settings.Default.ApeWinrateThreshold;
        int oldBattles = ApeRadar.Properties.Settings.Default.ApeBattleCountThreshold;
        try
        {
            ApeRadar.Properties.Settings.Default.ApeWinrateThreshold = 45;
            ApeRadar.Properties.Settings.Default.ApeBattleCountThreshold = 100;
            player.AccountWinrate = 0.35;

            PlayerRosterRowViewModel disabled = CreateRow(player, new(0, 0, 0, 0, 0, 0, 0, true));
            PlayerRosterRowViewModel enabled = CreateRow(player, new(0, 0, 0, 0, 0, 0, 0, true, ShowLegacyPerformanceTag: true, LegacyTagVisibility: 0));

            Assert.DoesNotContain(disabled.StatusBadges, badge => badge.Kind == RosterBadgeKind.LegacySkill);
            Assert.Contains(enabled.StatusBadges, badge => badge.Kind == RosterBadgeKind.LegacySkill);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.ApeWinrateThreshold = oldThreshold;
            ApeRadar.Properties.Settings.Default.ApeBattleCountThreshold = oldBattles;
        }
    }

    [Fact]
    public void CreateRows_RemovesHiddenGroupsWithoutLeavingEmptyLines()
    {
        PlayerRosterRowViewModel row = CreateRow(CreatePlayer("Hidden columns", 8_000), new(2, 2, 2, 2, 2, 2, 2, false));

        Assert.False(row.AccountMetrics.IsVisible);
        Assert.False(row.ShipMetrics.IsVisible);
        Assert.False(row.TierMetrics.IsVisible);
    }

    [Fact]
    public void PlayerRosterRowComparer_SortsUsingUnderlyingPlayerValues()
    {
        Player first = CreatePlayer("First", 2_000);
        Player second = CreatePlayer("Second", 9_000);
        IReadOnlyList<PlayerRosterRowViewModel> rows = new RosterPresentationService().CreateRows(new[] { first, second }, new(0, 0, 0, 0, 0, 0, 0, true));
        PlayerRosterRowComparer comparer = new(new BattlesDescendingComparer());

        Assert.True(comparer.Compare(rows[0], rows[1]) > 0);
        Assert.True(comparer.Compare(rows[1], rows[0]) < 0);
    }

    private static PlayerRosterRowViewModel CreateRow(Player player, RosterPresentationOptions options) =>
        Assert.Single(new RosterPresentationService().CreateRows(new[] { player }, options));

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
        TierPR = 1_710
    };

    private sealed class BattlesDescendingComparer : IComparer
    {
        public int Compare(object? x, object? y) =>
            -Comparer<double>.Default.Compare(((Player)x!).Battles, ((Player)y!).Battles);
    }
}
