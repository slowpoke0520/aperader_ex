using ApeRadar.Utils;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterLayoutCalculatorTests
{
    [Fact]
    public void StandardRoster_FitsTwelveRowsWithoutScrollingAtDesktopSize()
    {
        RosterFitMetrics result = RosterLayoutCalculator.CalculateFit(
            740, 760, 12, 18, 16, RosterDisplayDensity.Standard,
            showAccount: true, showShip: true, showTier: false, showPerformance: true);

        Assert.Equal(1, result.Layout.Scale);
        Assert.InRange(result.Layout.RowHeight, 54, 68);
        Assert.False(result.Layout.RequiresHorizontalScroll);
        Assert.False(result.Layout.RequiresVerticalScroll);
        Assert.Equal(740, result.Columns.Total, 1);
        Assert.True(result.Columns.Player > RosterLayoutCalculator.PlayerWidth);
    }

    [Fact]
    public void BothTeams_ReceiveDeterministicIdenticalMetrics()
    {
        RosterFitMetrics allies = Fit(610, 650, 12, showTier: true);
        RosterFitMetrics enemies = Fit(610, 650, 12, showTier: true);

        Assert.Equal(allies, enemies);
        Assert.Equal(allies.Columns.Player, enemies.Columns.Player);
        Assert.Equal(allies.Layout.RowHeight, enemies.Layout.RowHeight);
    }

    [Fact]
    public void ExtraWidth_IsGivenOnlyToPlayerColumn()
    {
        RosterFitMetrics natural = Fit(516, 760, 12, showTier: false);
        RosterFitMetrics wide = Fit(716, 760, 12, showTier: false);

        Assert.Equal(natural.Columns.Account, wide.Columns.Account);
        Assert.Equal(natural.Columns.Ship, wide.Columns.Ship);
        Assert.Equal(natural.Columns.Performance, wide.Columns.Performance);
        Assert.Equal(200, wide.Columns.Player - natural.Columns.Player, 1);
    }

    [Fact]
    public void OptionalColumns_RecomputeNaturalWidthWithoutLeavingHoles()
    {
        RosterFitMetrics all = Fit(800, 760, 12, showTier: true);
        RosterFitMetrics reduced = RosterLayoutCalculator.CalculateFit(
            800, 760, 12, 18, 16, RosterDisplayDensity.Standard,
            showAccount: false, showShip: true, showTier: false, showPerformance: false);

        Assert.Equal(0, reduced.Columns.Account);
        Assert.Equal(0, reduced.Columns.Tier);
        Assert.Equal(0, reduced.Columns.Performance);
        Assert.True(reduced.Columns.Player > all.Columns.Player);
        Assert.Equal(800, reduced.Columns.Total, 1);
    }

    [Fact]
    public void VerySmallViewport_ClampsScaleAndUsesScrollbars()
    {
        RosterFitMetrics result = Fit(360, 420, 12, showTier: true);

        Assert.Equal(RosterLayoutCalculator.MinimumScale, result.Layout.Scale, 3);
        Assert.True(result.Layout.RequiresHorizontalScroll);
        Assert.True(result.Layout.RequiresVerticalScroll);
        Assert.True(result.Layout.RowHeight >= 37.4);
    }

    [Fact]
    public void MoreThanTwelvePlayers_AlwaysKeepsReadableRowsAndScrolls()
    {
        RosterFitMetrics result = Fit(700, 900, 15, showTier: false);

        Assert.True(result.Layout.RequiresVerticalScroll);
        Assert.True(result.Layout.RowHeight >= 42);
    }

    [Theory]
    [InlineData("Compact", 48)]
    [InlineData("Standard", 54)]
    [InlineData("Comfortable", 60)]
    public void Density_ControlsNaturalRowHeight(string setting, double expected)
    {
        RosterDisplayDensity density = RosterDisplayDensityExtensions.Parse(setting);
        RosterFitMetrics result = RosterLayoutCalculator.CalculateFit(
            900, RosterLayoutCalculator.HeaderHeight + expected * 12, 12, 18, 16, density, true, true, false, true);

        Assert.Equal(expected, result.Layout.RowHeight);
    }

    [Fact]
    public void UnknownSettings_FallBackToStableDefaults()
    {
        Assert.Equal(RosterDisplayDensity.Standard, RosterDisplayDensityExtensions.Parse("future-value"));
        Assert.Equal(RosterPerformanceMetric.PR, RosterPerformanceMetricExtensions.Parse("future-value"));
    }

    private static RosterFitMetrics Fit(double width, double height, int players, bool showTier) =>
        RosterLayoutCalculator.CalculateFit(width, height, players, 18, 16,
            RosterDisplayDensity.Standard, true, true, showTier, true);
}
