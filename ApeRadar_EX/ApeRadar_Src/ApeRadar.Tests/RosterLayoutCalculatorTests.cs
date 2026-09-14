using ApeRadar.Utils;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RosterLayoutCalculatorTests
{
    [Theory]
    [InlineData(631, 12, 50)]
    [InlineData(559, 12, 44)]
    [InlineData(400, 12, 42)]
    public void RowHeight_UsesAvailableViewportAndStaysWithinReadableBounds(double gridHeight, int players, double expected)
    {
        RosterLayoutMetrics result = RosterLayoutCalculator.Calculate(gridHeight, players, 18, 16);

        Assert.Equal(expected, result.RowHeight);
        Assert.InRange(result.PlayerFontSize, 10, 18);
        Assert.InRange(result.StatisticsFontSize, 10, 16);
    }

    [Fact]
    public void LargeConfiguredFonts_AreTreatedAsUpperBounds()
    {
        RosterLayoutMetrics result = RosterLayoutCalculator.Calculate(400, 12, 22, 22);

        Assert.Equal(RosterLayoutCalculator.MinimumRowHeight, result.RowHeight);
        Assert.True(result.PlayerFontSize < 22);
        Assert.True(result.StatisticsFontSize < 22);
    }
}
