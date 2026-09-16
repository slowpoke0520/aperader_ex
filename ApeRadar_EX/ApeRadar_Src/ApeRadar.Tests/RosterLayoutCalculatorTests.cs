using ApeRadar.Utils;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterLayoutCalculatorTests
{
    [Theory]
    [InlineData(729, true)]
    [InlineData(730, false)]
    [InlineData(900, false)]
    public void HorizontalScrolling_IsSelectedOnlyBelowTheSemanticMinimum(double rosterGridWidth, bool expected)
    {
        Assert.Equal(expected, RosterLayoutCalculator.RequiresHorizontalScroll(rosterGridWidth));
    }

    [Theory]
    [InlineData(849, 12, 68)]
    [InlineData(753, 12, 60)]
    [InlineData(600, 12, 58)]
    public void StandardRowHeight_UsesAvailableViewportAndStaysReadable(double gridHeight, int players, double expected)
    {
        RosterLayoutMetrics result = RosterLayoutCalculator.Calculate(gridHeight, players, 18, 16);

        Assert.Equal(expected, result.RowHeight);
        Assert.InRange(result.PlayerFontSize, 11, 15.5);
        Assert.InRange(result.StatisticsFontSize, 10.5, 13.5);
        Assert.Equal(92, result.StatusColumnWidth);
    }

    [Theory]
    [InlineData("Compact", 50, 56, 68)]
    [InlineData("Standard", 58, 68, 92)]
    [InlineData("Comfortable", 68, 78, 100)]
    public void Density_UsesExpectedRowAndStatusBounds(string densitySetting, double minimum, double maximum, double status)
    {
        RosterDisplayDensity density = RosterDisplayDensityExtensions.Parse(densitySetting);
        RosterLayoutMetrics small = RosterLayoutCalculator.Calculate(500, 12, 18, 16, density);
        RosterLayoutMetrics large = RosterLayoutCalculator.Calculate(2_000, 12, 18, 16, density);

        Assert.Equal(minimum, small.RowHeight);
        Assert.Equal(maximum, large.RowHeight);
        Assert.Equal(status, small.StatusColumnWidth);
    }

    [Fact]
    public void ColumnWidths_AreDeterministicAndSharedBetweenTeams()
    {
        RosterColumnWidths allies = RosterLayoutCalculator.CalculateColumns(900, RosterDisplayDensity.Standard, true, true, true);
        RosterColumnWidths enemies = RosterLayoutCalculator.CalculateColumns(900, RosterDisplayDensity.Standard, true, true, true);

        Assert.Equal(allies, enemies);
        Assert.True(allies.Player >= 190);
        Assert.True(allies.Account >= 136);
        Assert.True(allies.Ship >= 158);
        Assert.True(allies.Tier >= 132);
        Assert.False(allies.RequiresHorizontalScroll);
    }

    [Fact]
    public void HiddenMetricColumns_GiveRemainingSpaceToVisibleColumns()
    {
        RosterColumnWidths all = RosterLayoutCalculator.CalculateColumns(900, RosterDisplayDensity.Standard, true, true, true);
        RosterColumnWidths reduced = RosterLayoutCalculator.CalculateColumns(900, RosterDisplayDensity.Standard, false, true, false);

        Assert.Equal(0, reduced.Account);
        Assert.Equal(0, reduced.Tier);
        Assert.True(reduced.Player > all.Player);
        Assert.True(reduced.Ship > all.Ship);
    }

    [Fact]
    public void UnknownDensitySetting_FallsBackToStandard()
    {
        Assert.Equal(RosterDisplayDensity.Standard, RosterDisplayDensityExtensions.Parse("future-value"));
        Assert.Equal("Standard", RosterDisplayDensity.Standard.ToSettingValue());
    }

    [Fact]
    public void LargeConfiguredFonts_AreTreatedAsUpperBounds()
    {
        RosterLayoutMetrics result = RosterLayoutCalculator.Calculate(600, 12, 22, 22);

        Assert.Equal(RosterLayoutCalculator.MinimumRowHeight, result.RowHeight);
        Assert.True(result.PlayerFontSize < 22);
        Assert.True(result.StatisticsFontSize < 22);
    }
}
