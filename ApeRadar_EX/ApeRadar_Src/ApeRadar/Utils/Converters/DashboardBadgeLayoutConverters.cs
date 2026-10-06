using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ApeRadar.Utils.Converters
{
    public sealed class DashboardBadgeVisibilityConverter : IValueConverter
    {
        internal const double MinimumWidth = 250;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double width && width >= MinimumWidth ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public sealed class DashboardBadgeOverflowConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length is not (2 or 3) || values[0] is not int count || values[1] is not double width) return "";
            int visibleCount = values.Length > 2 && values[2] is int displayed ? displayed : 1;
            int remaining = Math.Max(0, count - (width >= DashboardBadgeVisibilityConverter.MinimumWidth ? visibleCount : 0));
            return remaining > 0 ? $"+{remaining}" : "";
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
