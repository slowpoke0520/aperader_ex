using ApeRadar.ViewModels;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ApeRadar
{
    public partial class HistoryWindow : Window
    {
        private readonly HistoryViewModel viewModel;
        private bool ready;

        public HistoryWindow()
        {
            InitializeComponent();
            string chartFontFamily = ResolveChartFontFamily(HistoryChart.FontFamily);
            HistoryChart.TooltipTextPaint = new SolidColorPaint
            {
                Color = SKColors.Black,
                FontFamily = chartFontFamily
            };
            viewModel = new HistoryViewModel(
                History.HistoryServices.Repository,
                History.HistoryServices.Analysis,
                History.HistoryServices.Coordinator,
                chartFontFamily);
            DataContext = viewModel;
            Loaded += HistoryWindow_Loaded;
            Closed += (_, _) => viewModel.Dispose();
        }

        private static string ResolveChartFontFamily(FontFamily preferred)
        {
            IEnumerable<FontFamily> candidates = new[]
            {
                preferred,
                new FontFamily("Microsoft YaHei UI"),
                new FontFamily("Microsoft YaHei"),
                new FontFamily("Microsoft JhengHei UI"),
                new FontFamily("Yu Gothic UI"),
                new FontFamily("Malgun Gothic")
            };

            foreach (FontFamily family in candidates.GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
            {
                if (family.GetTypefaces().Any(typeface =>
                    typeface.TryGetGlyphTypeface(out GlyphTypeface glyphs) &&
                    glyphs.CharacterToGlyphMap.ContainsKey('中')))
                {
                    return family.Source;
                }
            }

            return preferred.Source;
        }

        private async void HistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await viewModel.InitializeAsync();
            ready = true;
        }

        private async void Server_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.RefreshDependentFiltersAsync(true, false);
        }

        private async void Account_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.RefreshDependentFiltersAsync(false, true);
        }

        private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.ReloadAsync();
        }

        private async void Date_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.ReloadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.ReloadAsync();
        private async void RetryReplay_Click(object sender, RoutedEventArgs e) => await viewModel.RetryFailedReplaysAsync();
        private void CancelImport_Click(object sender, RoutedEventArgs e) => viewModel.CancelReplayImport();
        private async void RetryApi_Click(object sender, RoutedEventArgs e) => await viewModel.RetryPendingAsync();
        private void OpenData_Click(object sender, RoutedEventArgs e) => viewModel.OpenDataDirectory();

        private async void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(FindResource("HistoryClearConfirmation") as string, FindResource("HistoryClear") as string,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                await viewModel.ClearAsync();
        }
    }
}
