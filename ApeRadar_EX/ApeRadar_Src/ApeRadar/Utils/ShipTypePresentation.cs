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
        private static readonly SolidColorBrush NeutralTint = CreateFrozenBrush(Color.FromRgb(76, 96, 120));
        private readonly Border iconShape = new()
        {
            Background = NeutralTint,
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

        public static readonly DependencyProperty TintBrushProperty = DependencyProperty.Register(
            nameof(TintBrush),
            typeof(Brush),
            typeof(ShipTypeIconBadge),
            new FrameworkPropertyMetadata(NeutralTint, (dependencyObject, _) => ((ShipTypeIconBadge)dependencyObject).Refresh()));

        public Brush TintBrush
        {
            get => (Brush)GetValue(TintBrushProperty);
            set => SetValue(TintBrushProperty, value);
        }

        internal ImageSource? IconSource { get; private set; }

        public ShipTypeIconBadge()
        {
            Width = 20;
            Height = 20;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(0);
            Background = Brushes.Transparent;
            BorderBrush = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            SnapsToDevicePixels = true;
            Child = iconShape;
            Refresh();
        }

        internal void Refresh()
        {
            ImageSource? source = Properties.Settings.Default.ShowShipTypeIcon
                ? ShipTypePresentation.GetIcon(ShipType)
                : null;
            IconSource = source;
            iconShape.Background = TintBrush ?? NeutralTint;
            iconShape.OpacityMask = source == null
                ? null
                : new ImageBrush(source)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
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

    public sealed class ShipRelationBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush Ally = CreateFrozenBrush(Color.FromRgb(35, 126, 78));
        private static readonly SolidColorBrush Enemy = CreateFrozenBrush(Color.FromRgb(184, 50, 50));
        private static readonly SolidColorBrush Neutral = CreateFrozenBrush(Color.FromRgb(76, 96, 120));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
        {
            "0" or "1" => Ally,
            "2" => Enemy,
            _ => Neutral
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }
    }

    public sealed class WidthReductionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double width = value is double number && double.IsFinite(number) ? number : 0;
            double reduction = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : 0;
            return Math.Max(0, width - reduction);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
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
