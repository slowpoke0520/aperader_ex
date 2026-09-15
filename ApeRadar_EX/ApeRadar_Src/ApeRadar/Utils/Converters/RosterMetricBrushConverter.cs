using ApeRadar.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    public sealed class RosterMetricBrushConverter : IValueConverter
    {
        private static readonly Brush Neutral = Brush("#162235");
        private static readonly Brush Error = Brush("#B42318");
        private static readonly Brush BelowAverage = Brush("#B54708");
        private static readonly Brush Average = Brush("#7A5B00");
        private static readonly Brush Good = Brush("#237B3A");
        private static readonly Brush VeryGood = Brush("#146C2E");
        private static readonly Brush Great = Brush("#007C70");
        private static readonly Brush Unicum = Brush("#7E22CE");
        private static readonly Brush SuperUnicum = Brush("#6B21A8");
        private static readonly Brush Warning = Brush("#946200");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not MetricItemViewModel metric || !metric.IsAvailable)
                return Neutral;

            return metric.Kind switch
            {
                RosterMetricKind.Winrate => Winrate(metric.ColorScore),
                RosterMetricKind.PersonalRating or RosterMetricKind.DamageRating => Rating(metric.ColorScore),
                RosterMetricKind.Warning => Warning,
                _ => Neutral
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static Brush Winrate(double? value)
        {
            if (value is not double winrate || winrate < 0 || Properties.Settings.Default.ColorStyle == 0) return Neutral;
            if (Properties.Settings.Default.ColorStyle == 1)
            {
                return winrate switch
                {
                    > 0.60 => SuperUnicum,
                    > 0.52 => VeryGood,
                    > 0.47 => Average,
                    _ => Error
                };
            }
            return winrate switch
            {
                > 0.65 => SuperUnicum,
                > 0.60 => Unicum,
                > 0.56 => Great,
                > 0.54 => VeryGood,
                > 0.52 => Good,
                > 0.49 => Average,
                > 0.47 => BelowAverage,
                _ => Error
            };
        }

        private static Brush Rating(double? value)
        {
            if (value is not double rating || rating < 0) return Neutral;
            return rating switch
            {
                < 750 => Error,
                < 1100 => BelowAverage,
                < 1350 => Average,
                < 1550 => Good,
                < 1750 => VeryGood,
                < 2100 => Great,
                < 2450 => Unicum,
                _ => SuperUnicum
            };
        }

        private static Brush Brush(string value)
        {
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
    }
}
