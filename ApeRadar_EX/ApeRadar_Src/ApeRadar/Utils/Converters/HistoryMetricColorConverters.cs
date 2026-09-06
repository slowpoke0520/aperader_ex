using ApeRadar.History;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    /// <summary>
    /// Accessible colour tokens used by the history window.  The foregrounds are
    /// deliberately darker than the traditional PR palette so they stay legible
    /// on the light backgrounds used by the standard WPF theme.
    /// </summary>
    internal static class HistoryColorPalette
    {
        internal static readonly Brush WinForeground = CreateBrush("#1E7A3A");
        internal static readonly Brush WinBackground = CreateBrush("#EAF6EC");
        internal static readonly Brush LossForeground = CreateBrush("#B42318");
        internal static readonly Brush LossBackground = CreateBrush("#FDECEA");
        internal static readonly Brush DrawForeground = CreateBrush("#7A5B00");
        internal static readonly Brush DrawBackground = CreateBrush("#FFF8D7");
        internal static readonly Brush UnknownForeground = CreateBrush("#5B636B");
        internal static readonly Brush UnknownBackground = CreateBrush("#F1F3F5");
        private static readonly Brush PrBadForeground = CreateBrush("#B42318");
        private static readonly Brush PrBelowAverageForeground = CreateBrush("#B54708");
        private static readonly Brush PrAverageForeground = CreateBrush("#7A5B00");
        private static readonly Brush PrGoodForeground = CreateBrush("#237B3A");
        private static readonly Brush PrVeryGoodForeground = CreateBrush("#146C2E");
        private static readonly Brush PrGreatForeground = CreateBrush("#007C70");
        private static readonly Brush PrUnicumForeground = CreateBrush("#7E22CE");
        private static readonly Brush PrSuperUnicumForeground = CreateBrush("#6B21A8");
        private static readonly Brush PrBadBackground = CreateBrush("#FDECEA");
        private static readonly Brush PrBelowAverageBackground = CreateBrush("#FFF0E0");
        private static readonly Brush PrAverageBackground = CreateBrush("#FFF8D7");
        private static readonly Brush PrGoodBackground = CreateBrush("#EAF6EC");
        private static readonly Brush PrVeryGoodBackground = CreateBrush("#E1F1E4");
        private static readonly Brush PrGreatBackground = CreateBrush("#E0F5F2");
        private static readonly Brush PrUnicumBackground = CreateBrush("#F3E8FF");
        private static readonly Brush PrSuperUnicumBackground = CreateBrush("#EEE4F7");

        internal static Brush GetResultBackground(object? value) => value switch
        {
            BattleResult.Win => WinBackground,
            BattleResult.Loss => LossBackground,
            BattleResult.Draw or BattleResult.UnknownNonWin => DrawBackground,
            _ => UnknownBackground
        };

        internal static Brush GetPrForeground(object? value) => TryGetPr(value, out var pr) ? pr switch
        {
            < 750 => PrBadForeground,
            < 1100 => PrBelowAverageForeground,
            < 1350 => PrAverageForeground,
            < 1550 => PrGoodForeground,
            < 1750 => PrVeryGoodForeground,
            < 2100 => PrGreatForeground,
            < 2450 => PrUnicumForeground,
            _ => PrSuperUnicumForeground
        } : UnknownForeground;

        internal static Brush GetPrBackground(object? value) => TryGetPr(value, out var pr) ? pr switch
        {
            < 750 => PrBadBackground,
            < 1100 => PrBelowAverageBackground,
            < 1350 => PrAverageBackground,
            < 1550 => PrGoodBackground,
            < 1750 => PrVeryGoodBackground,
            < 2100 => PrGreatBackground,
            < 2450 => PrUnicumBackground,
            _ => PrSuperUnicumBackground
        } : Brushes.Transparent;

        private static bool TryGetPr(object? value, out double pr)
        {
            switch (value)
            {
                case double number when number >= 0:
                    pr = number;
                    return true;
                case float number when number >= 0:
                    pr = number;
                    return true;
                case decimal number when number >= 0:
                    pr = (double)number;
                    return true;
                case int number when number >= 0:
                    pr = number;
                    return true;
                default:
                    pr = 0;
                    return false;
            }
        }

        private static Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }

    internal sealed class BattleResultBackgroundColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            HistoryColorPalette.GetResultBackground(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    internal sealed class HistoryPrColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            HistoryColorPalette.GetPrForeground(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    internal sealed class HistoryPrBackgroundColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            HistoryColorPalette.GetPrBackground(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
