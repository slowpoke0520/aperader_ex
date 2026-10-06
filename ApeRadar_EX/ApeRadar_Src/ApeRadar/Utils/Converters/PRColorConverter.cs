using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    internal class PRColorConverter : IValueConverter
    {
        // WoWS Numbers PR scale: Bad, Below Average, Average, Good,
        // Very Good, Great, Unicum, Super Unicum.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return RosterMetricBrushConverter.ForValue(value is double pr ? pr : null, ViewModels.RosterMetricKind.PersonalRating);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
