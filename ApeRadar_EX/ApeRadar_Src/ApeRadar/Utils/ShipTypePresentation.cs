using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApeRadar.Utils
{
    internal static class ShipTypePresentation
    {
        private static readonly IReadOnlyDictionary<string, string> IconFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AirCarrier"] = "aircarrier",
                ["Battleship"] = "battleship",
                ["Cruiser"] = "cruiser",
                ["Destroyer"] = "destroyer",
                ["Submarine"] = "submarine"
            };

        private static readonly Dictionary<string, ImageSource> IconCache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource? GetIcon(string? shipType)
        {
            if (string.IsNullOrWhiteSpace(shipType) || !IconFiles.TryGetValue(shipType, out string? file)) return null;
            lock (IconCache)
            {
                if (IconCache.TryGetValue(shipType, out ImageSource? cached)) return cached;
                BitmapImage image = new();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri($"pack://application:,,,/ApeRadar;component/Resources/Image/ShipTypes/{file}.png", UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                IconCache[shipType] = image;
                return image;
            }
        }

        public static string GetDisplayName(string? shipType)
        {
            if (string.IsNullOrWhiteSpace(shipType)) return "";
            string resourceKey = $"ShipType{shipType}";
            return Application.Current?.TryFindResource(resourceKey) as string ?? shipType;
        }

        public static bool ShouldShow(string? shipType) =>
            Properties.Settings.Default.ShowShipTypeIcon && GetIcon(shipType) != null;

        public static void RefreshOpenWindows()
        {
            if (Application.Current == null) return;
            foreach (Window window in Application.Current.Windows)
                RefreshVisualTree(window);
        }

        private static void RefreshVisualTree(DependencyObject parent)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is Image image)
                {
                    image.GetBindingExpression(Image.SourceProperty)?.UpdateTarget();
                    image.GetBindingExpression(UIElement.VisibilityProperty)?.UpdateTarget();
                }
                RefreshVisualTree(child);
            }
        }
    }

    internal sealed class ShipTypeIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Properties.Settings.Default.ShowShipTypeIcon ? ShipTypePresentation.GetIcon(value as string) : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    internal sealed class ShipTypeIconVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            ShipTypePresentation.ShouldShow(value as string) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    internal sealed class ShipTypeDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            ShipTypePresentation.GetDisplayName(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
