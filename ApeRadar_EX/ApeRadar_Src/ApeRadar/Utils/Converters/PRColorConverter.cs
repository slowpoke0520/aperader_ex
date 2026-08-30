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
            if (value is not double pr || pr < 0)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
            }
            string color = pr switch
            {
                < 750 => "#FE0E00",
                < 1100 => "#FE7903",
                < 1350 => "#FFC71F",
                < 1550 => "#44B300",
                < 1750 => "#318000",
                < 2100 => "#02C9B3",
                < 2450 => "#D042F3",
                _ => "#A00DC5",
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
