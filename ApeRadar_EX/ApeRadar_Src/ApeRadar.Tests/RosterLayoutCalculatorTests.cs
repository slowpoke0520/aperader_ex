using ApeRadar.Utils;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterLayoutCalculatorTests
{
    [Fact]
    public void StandardRoster_FitsTwelveRowsAtTargetFonts()
    {
        RosterFitMetrics result = Fit(940, 800, 12);

        Assert.Equal(17, result.Layout.PlayerFontSize);
        Assert.Equal(14, result.Layout.StatisticsFontSize);
        Assert.False(result.Layout.RequiresHorizontalScroll);
        Assert.False(result.Layout.RequiresVerticalScroll);
        Assert.Equal(940, result.Columns.Total, 1);
        Assert.InRange(result.Layout.RowHeight, 61, 66);
    }

    [Fact]
    public void VisibleMetricCount_DeterminesRequiredRowHeight()
    {
        RosterFitMetrics twoLines = Fit(940, 900, 12, shipLines: 2);
        RosterFitMetrics fourLines = Fit(940, 900, 12, shipLines: 4);

        Assert.True(fourLines.Layout.RowHeight > twoLines.Layout.RowHeight);
        Assert.True(fourLines.NaturalHeight > twoLines.NaturalHeight);
    }

    [Fact]
    public void BothTeams_ReceiveDeterministicIdenticalMetrics()
    {
        RosterFitMetrics allies = Fit(760, 760, 12, showTier: true);
        RosterFitMetrics enemies = Fit(760, 760, 12, showTier: true);

        Assert.Equal(allies, enemies);
        Assert.Equal(allies.Columns, enemies.Columns);
        Assert.Equal(allies.Layout.RowHeight, enemies.Layout.RowHeight);
    }

    [Fact]
    public void ExtraWidth_IsDistributedAcrossSemanticColumns()
    {
        RosterFitMetrics natural = Fit(660, 800, 12);
        RosterFitMetrics wide = Fit(860, 800, 12);

        Assert.True(wide.Columns.Player > natural.Columns.Player);
        Assert.True(wide.Columns.Account > natural.Columns.Account);
        Assert.True(wide.Columns.Ship > natural.Columns.Ship);
        Assert.True(wide.Columns.PersonalRating > natural.Columns.PersonalRating);
        Assert.Equal(natural.Columns.Performance, wide.Columns.Performance);
    }

    [Fact]
    public void OptionalColumns_RecomputeNaturalWidthWithoutLeavingHoles()
    {
        RosterFitMetrics reduced = RosterLayoutCalculator.CalculateFit(new RosterFitInput(
            800, 800, 12, 18, 16, RosterDisplayDensity.Standard,
            ShowAccount: false, ShowWeighted: false, ShowShip: true, ShowPersonalRating: false,
            ShowTier: false, ShowPerformance: false,
            AccountMetricLines: 0, WeightedMetricLines: 0, ShipMetricLines: 3,
            PersonalRatingMetricLines: 0, TierMetricLines: 0));

        Assert.Equal(0, reduced.Columns.Account);
        Assert.Equal(0, reduced.Columns.Weighted);
        Assert.Equal(0, reduced.Columns.PersonalRating);
        Assert.Equal(0, reduced.Columns.Tier);
        Assert.Equal(0, reduced.Columns.Performance);
        Assert.Equal(800, reduced.Columns.Total, 1);
    }

    [Fact]
    public void VerySmallViewport_UsesFontFloorsAndScrollbars()
    {
        RosterFitMetrics result = Fit(360, 420, 12, showTier: true);

        Assert.Equal(14, result.Layout.PlayerFontSize);
        Assert.Equal(12, result.Layout.StatisticsFontSize);
        Assert.True(result.Layout.RequiresHorizontalScroll);
        Assert.True(result.Layout.RequiresVerticalScroll);
    }

    [Fact]
    public void MoreThanTwelvePlayers_AlwaysScrollsWithoutShrinkingBelowFloor()
    {
        RosterFitMetrics result = Fit(800, 900, 15);

        Assert.True(result.Layout.RequiresVerticalScroll);
        Assert.True(result.Layout.PlayerFontSize >= 14);
        Assert.True(result.Layout.StatisticsFontSize >= 12);
    }

    [Theory]
    [InlineData("Compact", 15, 12)]
    [InlineData("Standard", 17, 14)]
    [InlineData("Comfortable", 18, 16)]
    public void Density_ControlsTargetFonts(string setting, double playerFont, double statisticsFont)
    {
        RosterDisplayDensity density = RosterDisplayDensityExtensions.Parse(setting);
        RosterFitMetrics result = Fit(940, 1_000, 12, density: density);

        Assert.Equal(playerFont, result.Layout.PlayerFontSize);
        Assert.Equal(statisticsFont, result.Layout.StatisticsFontSize);
    }

    [Fact]
    public void UnknownSettings_FallBackToStableDefaults()
    {
        Assert.Equal(RosterDisplayDensity.Standard, RosterDisplayDensityExtensions.Parse("future-value"));
        Assert.Equal(RosterPerformanceMetric.PR, RosterPerformanceMetricExtensions.Parse("future-value"));
    }

    [Fact]
    public void LegibilityMigration_DisablesExperienceOnceAndRestoresWeightedColumn()
    {
        bool oldMigration = ApeRadar.Properties.Settings.Default.RosterLegibilityMigrationDone;
        int oldAccountExperience = ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility;
        int oldShipExperience = ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility;
        int oldWeighted = ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility;
        bool oldAnalysis = ApeRadar.Properties.Settings.Default.AnalysisPanelExpanded;
        try
        {
            ApeRadar.Properties.Settings.Default.RosterLegibilityMigrationDone = false;
            ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility = 0;
            ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility = 0;
            ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility = 2;

            MainWindow.ApplyRosterLegibilitySettingsMigration(persist: false);

            Assert.True(ApeRadar.Properties.Settings.Default.RosterLegibilityMigrationDone);
            Assert.Equal(2, ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility);
            Assert.Equal(2, ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility);
            Assert.Equal(0, ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility);

            ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility = 0;
            ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility = 0;
            ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility = 2;
            MainWindow.ApplyRosterLegibilitySettingsMigration(persist: false);

            Assert.Equal(0, ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility);
            Assert.Equal(0, ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility);
            Assert.Equal(2, ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.RosterLegibilityMigrationDone = oldMigration;
            ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility = oldAccountExperience;
            ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility = oldShipExperience;
            ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility = oldWeighted;
            ApeRadar.Properties.Settings.Default.AnalysisPanelExpanded = oldAnalysis;
        }
    }

    private static RosterFitMetrics Fit(
        double width,
        double height,
        int players,
        bool showTier = false,
        int shipLines = 3,
        RosterDisplayDensity density = RosterDisplayDensity.Standard) =>
        RosterLayoutCalculator.CalculateFit(new RosterFitInput(
            width, height, players, 18, 16, density,
            ShowAccount: true, ShowWeighted: true, ShowShip: true, ShowPersonalRating: true,
            ShowTier: showTier, ShowPerformance: true,
            AccountMetricLines: 2, WeightedMetricLines: 1, ShipMetricLines: shipLines,
            PersonalRatingMetricLines: 2, TierMetricLines: 3));
}
