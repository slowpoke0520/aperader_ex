using ApeRadar.History;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    internal sealed class BattleResultColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            BattleResult.Win => HistoryColorPalette.WinForeground,
            BattleResult.Loss => HistoryColorPalette.LossForeground,
            BattleResult.Draw or BattleResult.UnknownNonWin => HistoryColorPalette.DrawForeground,
            _ => HistoryColorPalette.UnknownForeground
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
