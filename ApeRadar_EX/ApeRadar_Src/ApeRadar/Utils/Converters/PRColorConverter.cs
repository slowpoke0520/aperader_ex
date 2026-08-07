using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    internal class PRColorConverter : IValueConverter
    {
        //color bands follow WoWS Numbers rating scale
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double pr || pr < 0)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#000000"));
            }
            string color = pr switch
            {
                < 750 => "#607D8B",
                < 1100 => "#FE0E00",
                < 1350 => "#FE7903",
                < 1550 => "#F5C84C",
                < 1750 => "#67AF34",
                < 2100 => "#4A7D23",
                < 2450 => "#60C6B3",
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
