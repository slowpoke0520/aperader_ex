using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    internal class PRColorConverter : IValueConverter
    {
        //color bands follow the WoWS Numbers 8-color scale (same palette as the winrate tag colors)
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
                < 1550 => "#FFC71F",
                < 1750 => "#44B300",
                < 2100 => "#318000",
                < 2450 => "#02C9B3",
                < 2800 => "#D042F3",
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
