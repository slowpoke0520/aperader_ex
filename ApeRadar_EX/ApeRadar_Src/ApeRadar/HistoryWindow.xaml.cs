using ApeRadar.ViewModels;
using ApeRadar.Utils;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ApeRadar
{
    public partial class HistoryWindow : Window
    {
        private readonly HistoryViewModel viewModel;
        private bool ready;
        private bool filterRefreshInProgress;

        public HistoryWindow() : this(true) { }

        internal HistoryWindow(bool initializeOnLoaded)
        {
            InitializeComponent();
            string chartFontFamily = ChartFontUtils.Resolve(HistoryChart.FontFamily);
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
            if (initializeOnLoaded) Loaded += HistoryWindow_Loaded;
            Closed += (_, _) => viewModel.Dispose();
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
            viewModel.ResetPage();
            filterRefreshInProgress = true;
            try { await RunSafeAsync(() => viewModel.RefreshDependentFiltersAsync(true, false)); }
            finally { filterRefreshInProgress = false; }
        }

        private async void Account_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || filterRefreshInProgress) return;
            viewModel.ResetPage();
            filterRefreshInProgress = true;
            try { await RunSafeAsync(() => viewModel.RefreshDependentFiltersAsync(false, true)); }
            finally { filterRefreshInProgress = false; }
        }

        private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready && !filterRefreshInProgress)
            {
                viewModel.ResetPage();
                await RunSafeAsync(() => viewModel.ReloadAsync(debounce: true));
            }
        }

        private async void Date_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ready)
            {
                viewModel.ResetPage();
                await RunSafeAsync(() => viewModel.ReloadAsync(debounce: true));
            }
        }

        private async void PreviousPage_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(viewModel.PreviousPageAsync);
        private async void NextPage_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(viewModel.NextPageAsync);

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
