using ApeRadar.ViewModels;
using ApeRadar.History;
using ApeRadar.Utils;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;
using System.Collections.Generic;

namespace ApeRadar
{
    public partial class HistoryWindow : Window
    {
        private readonly History.HistoryServices historyServices;
        private readonly bool ownsHistoryServices;
        private readonly HistoryViewModel viewModel;
        private bool ready;
        private bool filterRefreshInProgress;
        private HistoryFilterOption? recordsShip;
        private bool trendOwnsShip;
        private readonly Dictionary<DataGridColumn,double> flexibleColumns = new();

        public HistoryWindow() : this(new History.HistoryServices(), initializeOnLoaded: true, ownsHistoryServices: true) { }

        internal HistoryWindow(bool initializeOnLoaded) : this(new History.HistoryServices(), initializeOnLoaded, ownsHistoryServices: true) { }

        internal HistoryWindow(History.HistoryServices historyServices, bool initializeOnLoaded) : this(historyServices, initializeOnLoaded, ownsHistoryServices: false) { }

        private HistoryWindow(History.HistoryServices historyServices, bool initializeOnLoaded, bool ownsHistoryServices)
        {
            this.historyServices = historyServices ?? throw new ArgumentNullException(nameof(historyServices));
            this.ownsHistoryServices = ownsHistoryServices;
            InitializeComponent();
            HistoryChart.Tooltip = new ShipAwareChartTooltip();
            string chartFontFamily = ChartFontUtils.Resolve(HistoryChart.FontFamily);
            HistoryChart.TooltipTextPaint = new SolidColorPaint
            {
                Color = SKColors.Black,
                FontFamily = chartFontFamily
            };
            viewModel = new HistoryViewModel(
                historyServices.Repository,
                historyServices.Analysis,
                historyServices.SessionAnalysis,
                historyServices.Insights,
                historyServices.Coordinator,
                chartFontFamily);
            DataContext = viewModel;
            HistoryChart.DataPointerDown += async (_, points) =>
            {
                if (points.FirstOrDefault()?.Context.DataSource is History.HistoryTrendPoint point)
                    await RunSafeAsync(() => viewModel.OpenBattleAsync(point.BattleId));
            };
            if (initializeOnLoaded) Loaded += HistoryWindow_Loaded;
            Closed += HistoryWindow_Closed;
        }

        internal History.HistoryServices HistoryServices => historyServices;
        internal void SetAccountContext(string server,string account) => viewModel.SetAccountContext(server,account);

        private async void HistoryWindow_Closed(object? sender, EventArgs e)
        {
            viewModel.Dispose();
            await RunSafeAsync(viewModel.FlushReviewDraftsAsync);
            if (ownsHistoryServices) historyServices.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
            if (ready && !filterRefreshInProgress)
            {
                viewModel.ResetPage();
                await RunSafeAsync(() => viewModel.ReloadAsync(debounce: true));
            }
        }

        private async void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            if (!ready || filterRefreshInProgress) return;
            filterRefreshInProgress = true;
            try
            {
                viewModel.ApplyDateRange("week");
                viewModel.FavoritesOnly = false;
                viewModel.SelectedShip = viewModel.Ships.FirstOrDefault();
                viewModel.ResetPage();
                await RunSafeAsync(() => viewModel.ReloadAsync());
            }
            finally { filterRefreshInProgress = false; }
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu is ContextMenu menu)
            {
                menu.PlacementTarget = button;
                menu.IsOpen = true;
            }
        }

        private async void Range_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || filterRefreshInProgress) return;
            filterRefreshInProgress = true;
            try
            {
                viewModel.ApplyDateRange(viewModel.SelectedDateRange?.Value ?? "week");
                viewModel.ResetPage();
                await RunSafeAsync(() => viewModel.ReloadAsync());
            }
            finally { filterRefreshInProgress = false; }
        }

        private async void FavoritesFilter_Click(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            viewModel.ResetPage();
            await RunSafeAsync(() => viewModel.ReloadAsync());
        }

        private void ShowManagement_Click(object sender, RoutedEventArgs e)
        {
            viewModel.IsBattleDetailOpen = false;
            ManagementTab.Visibility = Visibility.Visible;
            HistoryTabs.SelectedItem = ManagementTab;
        }

        private void HistoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ready || !ReferenceEquals(e.Source,HistoryTabs)) return;
            if (HistoryTabs.SelectedIndex == 1 && !trendOwnsShip)
            {
                recordsShip = viewModel.SelectedShip;
                trendOwnsShip = true;
                if (string.IsNullOrWhiteSpace(viewModel.SelectedShip?.Value))
                {
                    string? shipId = viewModel.Rows.FirstOrDefault()?.Battle.ShipId;
                    viewModel.SelectedShip = viewModel.Ships.FirstOrDefault(x => x.Value == shipId) ?? viewModel.Ships.Skip(1).FirstOrDefault();
                }
            }
            else if (HistoryTabs.SelectedIndex != 1 && trendOwnsShip)
            {
                trendOwnsShip = false;
                viewModel.SelectedShip = recordsShip;
            }
        }

        private void BackToRecords_Click(object sender, RoutedEventArgs e)
        {
            viewModel.IsBattleDetailOpen = false;
            if (HistoryTabs.SelectedIndex == 1) HistoryChart.Focus();
            else RecordsGrid.Focus();
        }

        private void PreviousBattle_Click(object sender, RoutedEventArgs e) => viewModel.NavigateBattle(-1);
        private void NextBattle_Click(object sender, RoutedEventArgs e) => viewModel.NavigateBattle(1);
        private async void DetailFavorite_Click(object sender, RoutedEventArgs e) => await RunSafeAsync(() => viewModel.ToggleFavoriteAsync());
        private async void RowFavorite_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button { DataContext: HistoryRowViewModel row })
                await RunSafeAsync(() => viewModel.ToggleFavoriteAsync(row));
        }
        private void AddReviewTag_Click(object sender, RoutedEventArgs e) => viewModel.AddCustomReviewTag();

        private void HistoryTable_Loaded(object sender,RoutedEventArgs e) => FitHistoryColumns((DataGrid)sender);
        private void HistoryTable_SizeChanged(object sender,SizeChangedEventArgs e) => FitHistoryColumns((DataGrid)sender);

        private void FitHistoryColumns(DataGrid grid)
        {
            foreach (DataGridColumn column in grid.Columns.Where(c=>c.Width.IsStar))
                flexibleColumns.TryAdd(column,column.Width.Value);
            DataGridColumn[] flexible=grid.Columns.Where(flexibleColumns.ContainsKey).ToArray();
            if (flexible.Length==0 || grid.ActualWidth<=0) return;
            double fixedWidth=grid.Columns.Where(c=>!flexibleColumns.ContainsKey(c)).Sum(c=>Math.Max(c.MinWidth,c.Width.Value));
            double remaining=Math.Max(0,grid.ActualWidth-SystemParameters.VerticalScrollBarWidth-8-fixedWidth);
            List<DataGridColumn> pending=flexible.ToList();
            while (pending.Count>0)
            {
                double weight=pending.Sum(c=>flexibleColumns[c]);
                DataGridColumn[] constrained=pending.Where(c=>remaining*flexibleColumns[c]/weight<c.MinWidth).ToArray();
                if (constrained.Length==0)
                {
                    foreach (DataGridColumn column in pending)
                        column.Width=new DataGridLength(remaining*flexibleColumns[column]/weight);
                    break;
                }
                foreach (DataGridColumn column in constrained)
                {
                    column.Width=new DataGridLength(column.MinWidth);
                    remaining=Math.Max(0,remaining-column.MinWidth); pending.Remove(column);
                }
            }
        }

        private void RecordsGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? item = e.OriginalSource as DependencyObject;
            while (item != null && item is not DataGridRow)
            {
                if (item is Button) return;
                item = item is Visual ? VisualTreeHelper.GetParent(item) : null;
            }
            if (item is DataGridRow { Item: HistoryRowViewModel row })
            {
                viewModel.SelectedBattle = row;
                viewModel.IsBattleDetailOpen = true;
                e.Handled = true;
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
