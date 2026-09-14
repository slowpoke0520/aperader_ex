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
                if (child is ShipTypeIconBadge badge)
                {
                    badge.Refresh();
                }
                else if (child is Image image)
                {
                    image.GetBindingExpression(Image.SourceProperty)?.UpdateTarget();
                    image.GetBindingExpression(UIElement.VisibilityProperty)?.UpdateTarget();
                }
                RefreshVisualTree(child);
            }
        }
    }

    public sealed class ShipTypeIconBadge : Border
    {
        private static readonly SolidColorBrush BadgeBackground = CreateFrozenBrush(Color.FromRgb(37, 55, 78));
        private static readonly SolidColorBrush BadgeBorder = CreateFrozenBrush(Color.FromRgb(88, 111, 139));
        private readonly Image icon = new()
        {
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };

        public static readonly DependencyProperty ShipTypeProperty = DependencyProperty.Register(
            nameof(ShipType),
            typeof(string),
            typeof(ShipTypeIconBadge),
            new FrameworkPropertyMetadata("", (dependencyObject, _) => ((ShipTypeIconBadge)dependencyObject).Refresh()));

        public string ShipType
        {
            get => (string)GetValue(ShipTypeProperty);
            set => SetValue(ShipTypeProperty, value);
        }

        public ShipTypeIconBadge()
        {
            Width = 22;
            Height = 22;
            Padding = new Thickness(2);
            CornerRadius = new CornerRadius(4);
            BorderThickness = new Thickness(1);
            Background = BadgeBackground;
            BorderBrush = BadgeBorder;
            VerticalAlignment = VerticalAlignment.Center;
            SnapsToDevicePixels = true;
            Child = icon;
            Refresh();
        }

        internal void Refresh()
        {
            ImageSource? source = Properties.Settings.Default.ShowShipTypeIcon
                ? ShipTypePresentation.GetIcon(ShipType)
                : null;
            icon.Source = source;
            ToolTip = source == null ? null : ShipTypePresentation.GetDisplayName(ShipType);
            Visibility = source == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
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
