using ApeRadar.History;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ApeRadar.Utils.Converters
{
    internal sealed class BattleResultColorConverter : IValueConverter
    {
        private static readonly Brush WinBrush = new SolidColorBrush(Color.FromRgb(42, 134, 76));
        private static readonly Brush LossBrush = new SolidColorBrush(Color.FromRgb(196, 52, 52));
        private static readonly Brush DrawBrush = new SolidColorBrush(Color.FromRgb(160, 112, 0));
        private static readonly Brush UnknownBrush = new SolidColorBrush(Color.FromRgb(96, 96, 96));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            BattleResult.Win => WinBrush,
            BattleResult.Loss => LossBrush,
            BattleResult.Draw or BattleResult.UnknownNonWin => DrawBrush,
            _ => UnknownBrush
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
