using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class DashboardPresentationTests
{
    [Fact]
    public void ContextSwitch_ChangesOnlyContextStatisticsAndKeepsBothPrValues()
    {
        Player ally = CreatePlayer("Ally", "1", 5_000, 0.56, 1_500, 120, 0.54, 1_620);
        Player enemy = CreatePlayer("Enemy", "2", 4_000, 0.51, 1_100, 80, 0.49, 980);
        BattleDashboardViewModel dashboard = CreateDashboard(ally, enemy);

        DashboardPlayerRowViewModel account = Assert.Single(dashboard.Allies);
        Assert.Equal("5,000", account.ContextBattlesText);
        Assert.Equal("1,500", account.AccountPrMetric.DisplayValue);
        Assert.Equal("1,620", account.ShipPrMetric.DisplayValue);

        dashboard.SetContext(DashboardRosterContext.Tier);

        DashboardPlayerRowViewModel tier = Assert.Single(dashboard.Allies);
        Assert.Equal("80", tier.ContextBattlesText);
        Assert.Equal("1,500", tier.AccountPrMetric.DisplayValue);
        Assert.Equal("1,620", tier.ShipPrMetric.DisplayValue);
    }

    [Theory]
    [InlineData(19, true)]
    [InlineData(20, false)]
    public void ShipLowSample_UsesTwentyBattleBoundary(double battles, bool expected)
    {
        DashboardPlayerRowViewModel row = CreateRow(CreatePlayer("Sample", "1", 2_000, 0.52, 1_200, battles, 0.51, 1_100), true);

        Assert.Equal(expected, row.IsShipLowSample);
        Assert.Equal(expected, row.StatusBadges.Any(badge => badge.Kind == RosterBadgeKind.LowSample));
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    public void TierLowSample_UsesFiftyBattleBoundary(double battles, bool expected)
    {
        Player player = CreatePlayer("Tier", "1", 2_000, 0.52, 1_200, 80, 0.51, 1_100);
        player.TierBattles = battles;
        DashboardPlayerRowViewModel row = CreateRow(player, true, DashboardRosterContext.Tier);

        Assert.Equal(expected, row.IsTierLowSample);
        Assert.Equal(expected ? "⚠" : "", row.ContextWarning);
    }

    [Fact]
    public void LoadingAndFinalFailure_AreDistinguished()
    {
        Player unresolved = new("Unresolved", Server.ASIA, "1", "3760142160");

        DashboardPlayerRowViewModel loading = CreateRow(unresolved, false);
        DashboardPlayerRowViewModel failed = CreateRow(unresolved, true);

        Assert.True(loading.IsLoading);
        Assert.False(loading.IsAnomaly);
        Assert.Contains(loading.StatusBadges, badge => badge.Kind == RosterBadgeKind.Loading);
        Assert.True(failed.IsFetchFailed);
        Assert.True(failed.IsAnomaly);
        Assert.Contains(failed.StatusBadges, badge => badge.Kind == RosterBadgeKind.FetchFailed);
    }

    [Fact]
    public void Summary_IncludesLowSamplesButExcludesHiddenPlayers()
    {
        Player low = CreatePlayer("Low", "1", 100, 0.50, 900, 5, 0.40, 600);
        Player regular = CreatePlayer("Regular", "1", 4_000, 0.60, 1_800, 300, 0.60, 1_800);
        Player hidden = CreatePlayer("Hidden", "1", 9_000, 0.90, 3_000, 500, 0.90, 3_000);
        hidden.IsHidden = true;
        Player enemy = CreatePlayer("Enemy", "2", 2_000, 0.50, 1_000, 100, 0.50, 1_000);
        BattleDashboardViewModel dashboard = CreateDashboard(low, regular, hidden, enemy);

        Assert.Equal(2, dashboard.Summary.Ally.ContextValidCount);
        Assert.Equal(2, dashboard.Summary.Ally.ShipValidCount);
        Assert.Equal(1, dashboard.Summary.Ally.ShipLowSampleCount);
        Assert.Equal(0.55, dashboard.Summary.Ally.ContextWinrate!.Value, 5);
        Assert.Equal(0.50, dashboard.Summary.Ally.ShipWinrate!.Value, 5);
        Assert.False(dashboard.Summary.CoverageInsufficient);
    }

    [Fact]
    public void ComparisonDirection_UsesDisplayedPrecision()
    {
        DashboardComparisonMetric equal = DashboardComparisonMetric.Percentage(0.50041, 0.50044, "WR");
        DashboardComparisonMetric ally = DashboardComparisonMetric.Integer(1_200.6, 1_200.4, "PR");
        DashboardComparisonMetric enemy = DashboardComparisonMetric.Integer(1_200.4, 1_200.6, "PR");
        DashboardComparisonMetric missing = DashboardComparisonMetric.Integer(null, 1_200, "PR");

        Assert.Equal("=", equal.Direction);
        Assert.Equal("←", ally.Direction);
        Assert.Equal("→", enemy.Direction);
        Assert.Equal("", missing.Direction);
    }

    [Fact]
    public void Filter_PreservesOriginalOrderAssignedAfterGlobalSort()
    {
        Player marked = CreatePlayer("Marked", "1", 1_000, 0.45, 800, 10, 0.42, 700);
        marked.IsCustomMarked = true;
        Player strong = CreatePlayer("Strong", "1", 9_000, 0.61, 1_900, 300, 0.60, 1_850);
        Player enemy = CreatePlayer("Enemy", "2", 2_000, 0.50, 1_000, 100, 0.50, 1_000);
        BattleDashboardViewModel dashboard = CreateDashboard(marked, strong, enemy);
        dashboard.SetSortMode(6);
        int markedOrder = dashboard.Allies.Single(row => row.Player == marked).OriginalOrder;

        dashboard.SetFilter(true, DashboardRosterFilter.Marked);

        DashboardPlayerRowViewModel filtered = Assert.Single(dashboard.Allies);
        Assert.Same(marked, filtered.Player);
        Assert.Equal(markedOrder, filtered.OriginalOrder);
    }

    [Fact]
    public void ShipClassSummary_IsOnlyVisibleWhenClassExists()
    {
        Player carrier = CreatePlayer("Carrier", "1", 3_000, 0.55, 1_400, 100, 0.54, 1_350);
        carrier.ShipType = "AirCarrier";
        Player destroyer = CreatePlayer("Destroyer", "2", 3_000, 0.51, 1_100, 90, 0.50, 1_050);
        destroyer.ShipType = "Destroyer";
        BattleDashboardViewModel dashboard = CreateDashboard(carrier, destroyer);

        Assert.True(dashboard.Summary.Carrier.IsVisible);
        Assert.Equal(1, dashboard.Summary.Carrier.AllyCount);
        Assert.Equal(0, dashboard.Summary.Carrier.EnemyCount);
        Assert.True(dashboard.Summary.Destroyer.IsVisible);
        Assert.Equal(0, dashboard.Summary.Destroyer.AllyCount);
        Assert.Equal(1, dashboard.Summary.Destroyer.EnemyCount);
    }

    [Theory]
    [InlineData(null, "Dashboard")]
    [InlineData("unknown", "Dashboard")]
    [InlineData("Dashboard", "Dashboard")]
    [InlineData("Legacy", "Legacy")]
    public void InterfaceStyle_NormalizesUnknownValues(string? value, string expected) =>
        Assert.Equal(expected, ConfigWindow.NormalizeMainInterfaceStyle(value));

    private static BattleDashboardViewModel CreateDashboard(params Player[] players)
    {
        Battlefield battlefield = (Battlefield)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Battlefield));
        battlefield.BattleType = "RandomBattle";
        battlefield.BattleStartTime = DateTimeOffset.Now;
        battlefield.Allies = players.Where(player => player.Relation is "0" or "1").ToList();
        battlefield.Enemies = players.Where(player => player.Relation is not ("0" or "1")).ToList();
        BattleDashboardViewModel dashboard = new(new DashboardPresentationService());
        dashboard.Update(battlefield, true, DashboardBattleMetadata.Empty);
        return dashboard;
    }

    private static DashboardPlayerRowViewModel CreateRow(Player player, bool completed, DashboardRosterContext context = DashboardRosterContext.Account)
    {
        PlayerRosterRowViewModel baseRow = Assert.Single(new RosterPresentationService().CreateRows(
            new[] { player }, RosterPresentationOptions.FromCurrentSettings()));
        return new DashboardPlayerRowViewModel(1, baseRow, context, completed);
    }

    private static Player CreatePlayer(
        string name,
        string relation,
        double battles,
        double accountWinrate,
        double accountPr,
        double shipBattles,
        double shipWinrate,
        double shipPr) => new(name, name.GetHashCode().ToString(), Server.ASIA, WatchStatus.NONE)
        {
            Relation = relation,
            ShipID = "3760142160",
            ShipName = "Yamato",
            ShipType = "Battleship",
            ShipTier = 10,
            Battles = battles,
            AccountWinrate = accountWinrate,
            PR = accountPr,
            ShipBattles = shipBattles,
            ShipWinrate = shipWinrate,
            ShipAvgDmgPerBattle = 90_000,
            ShipPR = shipPr,
            TierBattles = 80,
            TierWinrate = accountWinrate - 0.01,
            TierPR = accountPr - 100,
            WeightedWinrate = accountWinrate
        };
}
