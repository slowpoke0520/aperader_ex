using ApeRadar.ViewModels;
using ApeRadar.Utils;
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
        private bool filterRefreshInProgress;

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
                History.HistoryServices.SessionAnalysis,
                History.HistoryServices.Insights,
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
            await RunSafeAsync(async () =>
            {
                await viewModel.InitializeAsync();
                ready = true;
            });
        }

        private async void Server_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || filterRefreshInProgress) return;
            filterRefreshInProgress = true;
            try { await RunSafeAsync(() => viewModel.RefreshDependentFiltersAsync(true, false)); }
            finally { filterRefreshInProgress = false; }
        }

        private async void Account_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || filterRefreshInProgress) return;
            filterRefreshInProgress = true;
            try { await RunSafeAsync(() => viewModel.RefreshDependentFiltersAsync(false, true)); }
            finally { filterRefreshInProgress = false; }
        }

        private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready && !filterRefreshInProgress) await RunSafeAsync(viewModel.ReloadAsync);
        }

        private async void Date_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await RunSafeAsync(viewModel.ReloadAsync);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(viewModel.RefreshAllAsync);
        private async void RetryReplay_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(viewModel.RetryFailedReplaysAsync);
        private void CancelImport_Click(object sender, RoutedEventArgs e) => viewModel.CancelReplayImport();
        private async void RetryApi_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(viewModel.RetryPendingAsync);
        private async void OpenData_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(() => { viewModel.OpenDataDirectory(); return System.Threading.Tasks.Task.CompletedTask; });
        private async void SaveReview_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(viewModel.SaveReviewAsync);

        private async void MergeSessions_Click(object sender, RoutedEventArgs e)
        {
            SessionRowViewModel[] selected = SessionGrid.SelectedItems.Cast<SessionRowViewModel>().ToArray();
            if (selected.Length < 2)
            {
                MessageBox.Show(FindResource("HistorySelectTwoSessions") as string, FindResource("HistoryMergeSessions") as string,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(FindResource("HistoryMergeConfirmation") as string, FindResource("HistoryMergeSessions") as string,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await RunSafeAsync(() => viewModel.MergeSessionsAsync(selected));
        }

        private async void SplitSession_Click(object sender, RoutedEventArgs e)
        {
            if (viewModel.SelectedSessionBattle == null)
            {
                MessageBox.Show(FindResource("HistorySelectSplitBattle") as string, FindResource("HistorySplitBeforeBattle") as string,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(FindResource("HistorySplitConfirmation") as string, FindResource("HistorySplitBeforeBattle") as string,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await RunSafeAsync(viewModel.SplitSelectedSessionAsync);
        }

        private async void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(FindResource("HistoryClearConfirmation") as string, FindResource("HistoryClear") as string,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                await RunSafeAsync(viewModel.ClearAsync);
        }

        private async System.Threading.Tasks.Task RunSafeAsync(Func<System.Threading.Tasks.Task> action)
        {
            try { await action(); }
            catch (Exception ex)
            {
                LogUtils.WriteError("Battle history window operation failed.", ex);
                viewModel.ReportError(ex);
            }
        }
    }
}
