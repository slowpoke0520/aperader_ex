using ApeRadar.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ApeRadar.Utils.Converters
{
    internal class ContextMenuItemEditNoteIsEnabledConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null)
            {
                return DependencyProperty.UnsetValue;
            }
            Player p = (value as Player)!;
            return p.Name[..1] != ":" && p.ID != "-1";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
