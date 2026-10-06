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
        Assert.Equal("5000", account.ContextBattlesText);
        Assert.Equal("1500", account.ContextPrMetric.DisplayValue);
        Assert.Equal("1500", account.AccountPrMetric.DisplayValue);
        Assert.Equal("1620", account.ShipPrMetric.DisplayValue);

        dashboard.SetContext(DashboardRosterContext.Tier);

        DashboardPlayerRowViewModel tier = Assert.Single(dashboard.Allies);
        Assert.Equal("80", tier.ContextBattlesText);
        Assert.Equal("1400", tier.ContextPrMetric.DisplayValue);
        Assert.Equal("1500", tier.AccountPrMetric.DisplayValue);
        Assert.Equal("1620", tier.ShipPrMetric.DisplayValue);
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
    public void KnownPlayerWithFailedAccountEndpoint_IsMarkedAsFetchFailure()
    {
        Player failedPlayer = new("Known", Server.ASIA, "1", "3760142160")
        {
            ID = "123456",
            IsDataFetchFailed = true
        };

        DashboardPlayerRowViewModel failed = CreateRow(failedPlayer, true);

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
        Assert.Contains(dashboard.Summary.Carrier.Name, dashboard.Summary.Carrier.AllySummaryText);
        Assert.Contains(dashboard.Summary.Carrier.Name, dashboard.Summary.Carrier.EnemySummaryText);
        Assert.Contains(dashboard.Summary.Destroyer.Name, dashboard.Summary.Destroyer.AllySummaryText);
        Assert.Contains(dashboard.Summary.Destroyer.Name, dashboard.Summary.Destroyer.EnemySummaryText);
    }

    [Fact]
    public void Karma_UsesOriginalSuperscriptValueIncludingZero()
    {
        Player zero = CreatePlayer("Zero", "1", 1_000, 0.50, 1_000, 50, 0.50, 1_000);
        zero.Karma = 0;
        Player positive = CreatePlayer("Positive", "1", 1_000, 0.50, 1_000, 50, 0.50, 1_000);
        positive.Karma = 2;

        DashboardPlayerRowViewModel zeroRow = CreateRow(zero, true);
        DashboardPlayerRowViewModel positiveRow = CreateRow(positive, true);

        Assert.True(zeroRow.HasKarma);
        Assert.Equal("0", zeroRow.KarmaText);
        Assert.True(positiveRow.HasKarma);
        Assert.Equal("2", positiveRow.KarmaText);
    }

    [Theory]
    [InlineData(null, "Dashboard")]
    [InlineData("unknown", "Dashboard")]
    [InlineData("Dashboard", "Dashboard")]
    [InlineData("Legacy", "Legacy")]
    public void InterfaceStyle_NormalizesUnknownValues(string? value, string expected) =>
        Assert.Equal(expected, ConfigWindow.NormalizeMainInterfaceStyle(value));

    [Fact]
    public void MissingPr_KeepsWinratesDamageBattlesAndIndependentCoverage()
    {
        Player partial = CreatePlayer("Partial", "1", 780, 0.483, -1, 12, 0.492, -1);
        partial.TierPR = -1;
        Player complete = CreatePlayer("Complete", "1", 2000, 0.60, 1800, 300, 0.60, 1800);
        BattleDashboardViewModel dashboard = CreateDashboard(partial, complete);
        DashboardPlayerRowViewModel row = dashboard.Allies.Single(row => row.Player == partial);
        Assert.Equal("780", row.ContextBattlesText);
        Assert.True(row.ContextWinrateMetric.IsAvailable);
        Assert.False(row.ContextPrMetric.IsAvailable);
        Assert.Equal("12", row.ShipBattlesText);
        Assert.Equal("90000", row.ShipDamageText);
        Assert.True(row.IsShipLowSample);
        Assert.Contains(row.StatusBadges, badge => badge.Kind == RosterBadgeKind.PartialData);
        Assert.Equal(2, dashboard.Summary.ContextWinrate.AllyValidCount);
        Assert.Equal(1, dashboard.Summary.ContextPr.AllyValidCount);
        Assert.Equal(2, dashboard.Summary.ShipWinrate.AllyValidCount);
        Assert.Equal(1, dashboard.Summary.ShipPr.AllyValidCount);
        Assert.Equal(0.5415, dashboard.Summary.Ally.ContextWinrate!.Value, 5);
        Assert.Equal(1800, dashboard.Summary.Ally.ContextPr);
        dashboard.SetContext(DashboardRosterContext.Tier);
        Assert.True(dashboard.Allies.Single(row => row.Player == partial).ContextWinrateMetric.IsAvailable);
        Assert.Equal(1, dashboard.Summary.ContextPr.AllyValidCount);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.01)]
    [InlineData(-1)]
    public void InvalidWinrate_IsExcludedWithoutHidingOtherMetrics(double winrate)
    {
        Player player = CreatePlayer("Invalid", "1", 100, winrate, 1200, 50, 0.5, 1000);
        BattleDashboardViewModel dashboard = CreateDashboard(player);
        DashboardPlayerRowViewModel row = Assert.Single(dashboard.Allies);
        Assert.Equal("—", row.ContextWinrateMetric.DisplayValue);
        Assert.Equal("100", row.ContextBattlesText);
        Assert.Equal("1200", row.ContextPrMetric.DisplayValue);
        Assert.Null(dashboard.Summary.Ally.ContextWinrate);
        Assert.Equal(1, dashboard.Summary.Ally.ContextPrValidCount);
    }

    [Fact]
    public void HiddenStatistics_AreExcludedFromBothInterfacesEvenWhenCachedValuesRemain()
    {
        Player player = CreatePlayer("HiddenCached", "1", 2000, 0.6, 1800, 50, 0.5, 1200);
        player.IsHidden = true;
        DashboardPlayerRowViewModel row = CreateRow(player, true);
        Assert.Equal("—", row.ContextBattlesText);
        Assert.Equal("—", row.ShipDamageText);
        Assert.All(row.BaseRow.AccountMetrics.Items, metric => Assert.False(metric.IsAvailable));
        Assert.All(row.BaseRow.ShipMetrics.Items, metric => Assert.False(metric.IsAvailable));
    }

    [Fact]
    public void KnownZeroCountsAndWinrates_AreNotTreatedAsMissing()
    {
        DashboardPlayerRowViewModel row = CreateRow(CreatePlayer("Zero", "1", 0, 0, -1, 0, 0, -1), true);
        Assert.Equal("0", row.ContextBattlesText);
        Assert.True(row.ContextWinrateMetric.IsAvailable);
        Assert.Equal("0", row.ShipBattlesText);
        Assert.True(row.IsShipLowSample);
        Assert.False(row.ShipPrMetric.IsAvailable);
    }

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
