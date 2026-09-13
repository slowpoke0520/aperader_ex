using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    internal sealed class WinrateColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double winrate || winrate < 0 || Properties.Settings.Default.ColorStyle == 0)
            {
                return Brushes.Black;
            }

            string color = Properties.Settings.Default.ColorStyle == 1
                ? winrate switch
                {
                    > 0.60 => "#A00DC5",
                    > 0.52 => "#318000",
                    > 0.47 => "#B8860B",
                    _ => "#D41111",
                }
                : winrate switch
                {
                    > 0.65 => "#A00DC5",
                    > 0.60 => "#D042F3",
                    > 0.56 => "#008F82",
                    > 0.54 => "#318000",
                    > 0.52 => "#44B300",
                    > 0.49 => "#9B7A00",
                    > 0.47 => "#D85F00",
                    _ => "#D41111",
                };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
