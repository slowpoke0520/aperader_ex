using ApeRadar.Services;
using ApeRadar.Utils.Converters;
using ApeRadar.ViewModels;
using System.Globalization;
using System.Windows.Media;
using Xunit;

namespace ApeRadar.Tests;

public sealed class StatisticsOptionsTests
{
    [Fact]
    public void WeightedWinrate_UsesExplicitWeightsAndCapsShipContribution()
    {
        PlayerStatisticsOptions options = new("id", 1, 0.5, 0.25, 40, 100);
        double account = 95d / 175;
        Assert.Equal(account * 0.8 + 0.7 * 0.2,
            WeightedWinrateCalculator.Calculate(options, 0.6, 100, 0.5, 100, 0.4, 100, 0.7, 50), 10);
        Assert.Equal(account * 0.6 + 0.7 * 0.4,
            WeightedWinrateCalculator.Calculate(options, 0.6, 100, 0.5, 100, 0.4, 100, 0.7, 500), 10);
        Assert.Equal(-1, WeightedWinrateCalculator.Calculate(options, 0, 0, 0, 0, 0, 0, 0, 0));
        Assert.Equal(-1, WeightedWinrateCalculator.Calculate(options, double.NaN, 100, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void PrTextPalette_IsSharedAndReadableOnActualLightSurfaces()
    {
        PRColorConverter detail = new();
        foreach (double rating in new[] { 0d, 749, 750, 1100, 1350, 1550, 1750, 2100, 2450 })
        {
            SolidColorBrush expected = Assert.IsType<SolidColorBrush>(RosterMetricBrushConverter.ForValue(rating, RosterMetricKind.PersonalRating));
            SolidColorBrush actual = Assert.IsType<SolidColorBrush>(detail.Convert(rating, typeof(Brush), null!, CultureInfo.InvariantCulture));
            Assert.Equal(expected.Color, actual.Color);
            foreach (string background in new[] { "#FFFFFF", "#F6F8FC", "#FAFBFD" })
                Assert.True(Contrast(actual.Color, (Color)ColorConverter.ConvertFromString(background)) >= 4.5, $"{actual.Color} on {background}");
        }
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
