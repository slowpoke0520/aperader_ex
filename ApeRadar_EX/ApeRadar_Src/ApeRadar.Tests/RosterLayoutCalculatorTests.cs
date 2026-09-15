using ApeRadar.Utils;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterLayoutCalculatorTests
{
    [Theory]
    [InlineData(639, true)]
    [InlineData(640, false)]
    [InlineData(900, false)]
    public void HorizontalScrolling_IsSelectedOnlyBelowTheSemanticMinimum(double rosterGridWidth, bool expected)
    {
        Assert.Equal(expected, RosterLayoutCalculator.RequiresHorizontalScroll(rosterGridWidth));
    }

    [Theory]
    [InlineData(849, 12, 68)]
    [InlineData(753, 12, 60)]
    [InlineData(600, 12, 58)]
    public void RowHeight_UsesAvailableViewportAndStaysWithinReadableBounds(double gridHeight, int players, double expected)
    {
        RosterLayoutMetrics result = RosterLayoutCalculator.Calculate(gridHeight, players, 18, 16);

        Assert.Equal(expected, result.RowHeight);
        Assert.InRange(result.PlayerFontSize, 11, 15.5);
        Assert.InRange(result.StatisticsFontSize, 10.5, 13.5);
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
