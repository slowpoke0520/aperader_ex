using ApeRadar.ViewModels;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    internal sealed class RosterBadgeBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            RosterBadgeSeverity severity = value is RosterBadgeSeverity badgeSeverity ? badgeSeverity : RosterBadgeSeverity.Neutral;
            bool background = string.Equals(parameter as string, "Background", StringComparison.OrdinalIgnoreCase);
            string color = (severity, background) switch
            {
                (RosterBadgeSeverity.Info, true) => "#E8F2FF",
                (RosterBadgeSeverity.Info, false) => "#245B95",
                (RosterBadgeSeverity.Positive, true) => "#E7F6EE",
                (RosterBadgeSeverity.Positive, false) => "#19714C",
                (RosterBadgeSeverity.Warning, true) => "#FFF4D6",
                (RosterBadgeSeverity.Warning, false) => "#805200",
                (RosterBadgeSeverity.Critical, true) => "#FDECEC",
                (RosterBadgeSeverity.Critical, false) => "#A72828",
                (_, true) => "#EEF2F6",
                _ => "#455468"
            };
            SolidColorBrush brush = new((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
