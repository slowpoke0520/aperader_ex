using ApeRadar.History;
using ApeRadar.Models;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ApeRadar.Utils
{
    internal sealed class ShipAwareChartTooltip : IChartTooltip<SkiaSharpDrawingContext>
    {
        private readonly Popup popup = new()
        {
            AllowsTransparency = true,
            Placement = PlacementMode.MousePoint,
            HorizontalOffset = 12,
            VerticalOffset = 12,
            StaysOpen = false
        };

        public void Show(IEnumerable<ChartPoint> points, Chart<SkiaSharpDrawingContext> chart)
        {
            ChartPoint[] visiblePoints = points.Where(point => !point.IsEmpty).ToArray();
            if (visiblePoints.Length == 0)
            {
                Hide();
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                StackPanel rows = new() { Margin = new Thickness(2) };
                foreach (ChartPoint point in visiblePoints) rows.Children.Add(BuildPoint(point));
                popup.Child = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(204, 214, 226)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 7, 9, 7),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, Opacity = 0.18, ShadowDepth = 2 },
                    Child = rows
                };
                popup.IsOpen = true;
            });
        }

        public void Hide()
        {
            if (Application.Current == null) return;
            Application.Current.Dispatcher.Invoke(() => popup.IsOpen = false);
        }

        private static FrameworkElement BuildPoint(ChartPoint point)
        {
            object? model = point.Context.DataSource;
            string shipName = "";
            string shipType = "";
            string heading = point.Context.Series.Name ?? "";
            string value = point.PrimaryValue.ToString("N2", CultureInfo.CurrentCulture);

            if (model is Player player)
            {
                heading = $"{player.ClanTag} {player.Name}".Trim();
                shipName = $"{new Converters.ShipTierConverter().Convert(player.ShipTier, null!, null!, CultureInfo.CurrentCulture)} {player.ShipName}".Trim();
                shipType = player.ShipType;
                value = point.PrimaryValue.ToString("P2", CultureInfo.CurrentCulture);
            }
            else if (model is HistoryTrendPoint history)
            {
                heading = history.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
                shipName = history.ShipName;
                shipType = history.ShipType;
                value = $"{point.Context.Series.Name}: {point.AsTooltipString}";
            }
            else if (!string.IsNullOrWhiteSpace(point.AsTooltipString))
            {
                value = point.AsTooltipString;
            }

            StackPanel content = new() { Margin = new Thickness(0, 1, 0, 3) };
            if (!string.IsNullOrWhiteSpace(heading))
                content.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(22, 34, 53)) });
            if (!string.IsNullOrWhiteSpace(shipName))
            {
                StackPanel ship = new() { Orientation = Orientation.Horizontal };
                ship.Children.Add(new TextBlock { Text = shipName, Foreground = new SolidColorBrush(Color.FromRgb(83, 97, 116)), VerticalAlignment = VerticalAlignment.Center });
                ImageSource? source = Properties.Settings.Default.ShowShipTypeIcon ? ShipTypePresentation.GetIcon(shipType) : null;
                if (source != null)
                {
                    ship.Children.Add(new Image { Source = source, Width = 18, Height = 18, Margin = new Thickness(4, 0, 0, 0), ToolTip = ShipTypePresentation.GetDisplayName(shipType) });
                }
                content.Children.Add(ship);
            }
            content.Children.Add(new TextBlock { Text = value, Foreground = Brushes.Black });
            return content;
        }
    }
}
