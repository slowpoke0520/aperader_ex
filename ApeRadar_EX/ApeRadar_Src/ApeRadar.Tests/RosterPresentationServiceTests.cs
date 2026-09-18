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

        Assert.Single(row.AccountMetrics.Lines);
        Assert.Single(row.ShipMetrics.Lines);
        Assert.Single(row.TierMetrics.Lines);
        Assert.Equal(new[] { RosterMetricKind.Neutral, RosterMetricKind.Winrate, RosterMetricKind.PersonalRating, RosterMetricKind.Neutral, RosterMetricKind.Winrate }, row.AccountMetrics.Items.Select(item => item.Kind));
        Assert.Equal(RosterMetricKind.DamageRating, row.ShipMetrics.Items[3].Kind);
        Assert.Empty(row.StatusBadges);
        Assert.Equal(string.Empty, row.ContextPreview);
        Assert.Equal(PlayerSkillBand.VeryGood, row.SkillBand);
    }

    [Fact]
    public void CompactDensity_KeepsTheSameSemanticColumnOrder()
    {
        PlayerRosterRowViewModel row = CreateRow(CreatePlayer("Compact", 8_000),
            new RosterPresentationOptions(0, 0, 0, 0, 0, 0, 0, true, RosterDisplayDensity.Compact));

        Assert.Single(row.AccountMetrics.Lines);
        Assert.Single(row.ShipMetrics.Lines);
        Assert.Equal("Games", row.AccountMetrics.Items[0].Label);
        Assert.Equal("Games", row.ShipMetrics.Items[0].Label);
    }

    [Fact]
    public void SmallTierSample_WarnsOnceBesideTierBattleCount()
    {
        Player player = CreatePlayer("Small sample", 8_000);
        player.IsTierSampleSmall = true;

        PlayerRosterRowViewModel row = CreateRow(player, new(0, 0, 0, 0, 0, 0, 0, true));
        MetricItemViewModel tierBattles = row.TierMetrics.Items[0];

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
    public void LegacyPerformanceIcon_IsOffByDefaultAndOptionalInsideColorCell()
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

            Assert.Empty(disabled.Performance.Icon);
            Assert.Equal(ApeRadar.Properties.Settings.Default.ApeIcon, enabled.Performance.Icon);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.ApeWinrateThreshold = oldThreshold;
            ApeRadar.Properties.Settings.Default.ApeBattleCountThreshold = oldBattles;
        }
    }

    [Fact]
    public void PerformanceCell_CanUseAccountPrOrSelectedWinrate()
    {
        Player player = CreatePlayer("Performance", 8_000);
        int oldWinrateType = ApeRadar.Properties.Settings.Default.WinrateTypeUsed;
        int oldColorStyle = ApeRadar.Properties.Settings.Default.ColorStyle;
        try
        {
            ApeRadar.Properties.Settings.Default.ColorStyle = 2;
            ApeRadar.Properties.Settings.Default.WinrateTypeUsed = 0;
            PlayerRosterRowViewModel pr = CreateRow(player,
                new(0, 0, 0, 0, 0, 0, 0, true, PerformanceMetric: RosterPerformanceMetric.PR));
            PlayerRosterRowViewModel winrate = CreateRow(player,
                new(0, 0, 0, 0, 0, 0, 0, true, PerformanceMetric: RosterPerformanceMetric.Winrate));

            Assert.Equal(1_650, pr.Performance.RawValue);
            Assert.Equal(player.AccountWinrate, winrate.Performance.RawValue);
            Assert.Equal(RosterPerformanceMetric.PR, pr.Performance.Metric);
            Assert.Equal(RosterPerformanceMetric.Winrate, winrate.Performance.Metric);
            Assert.NotEqual(pr.Performance.ToolTip, winrate.Performance.ToolTip);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.WinrateTypeUsed = oldWinrateType;
            ApeRadar.Properties.Settings.Default.ColorStyle = oldColorStyle;
        }
    }

    [Fact]
    public void PerformanceCell_UsesWeightedWinrateWhenConfigured()
    {
        Player player = CreatePlayer("Weighted", 8_000);
        int oldWinrateType = ApeRadar.Properties.Settings.Default.WinrateTypeUsed;
        try
        {
            ApeRadar.Properties.Settings.Default.WinrateTypeUsed = 1;
            PlayerRosterRowViewModel row = CreateRow(player,
                new(0, 0, 0, 0, 0, 0, 0, true, PerformanceMetric: RosterPerformanceMetric.Winrate));

            Assert.Equal(player.WeightedWinrate, row.Performance.RawValue);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.WinrateTypeUsed = oldWinrateType;
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
