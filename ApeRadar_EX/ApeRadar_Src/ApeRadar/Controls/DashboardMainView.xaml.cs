using ApeRadar.Models;
using ApeRadar.Utils;
using ApeRadar.ViewModels;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ApeRadar.Controls
{
    internal enum DashboardPlayerAction
    {
        Refresh,
        Copy,
        Official,
        Numbers,
        CustomMarker,
        FixedTeammate,
        EditNote,
        ClearNote,
        WatchPositive,
        WatchNegative,
        WatchCheater,
        WatchRemove
    }

    internal sealed class DashboardPlayerActionEventArgs : EventArgs
    {
        public DashboardPlayerAction Action { get; }
        public Player Player { get; }

        public DashboardPlayerActionEventArgs(DashboardPlayerAction action, Player player)
        {
            Action = action;
            Player = player;
        }
    }

    public partial class DashboardMainView : UserControl
    {
        private readonly DispatcherTimer detailOpenTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly DispatcherTimer detailCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        private DashboardPlayerRowViewModel? pendingDetail;
        private DataGridRow? pendingDetailTarget;
        private bool pointerOverDetailRow;
        private bool pointerOverDetail;
        private bool suppressSelectors;
        private bool analysisOpen;

        public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
            nameof(Status), typeof(RosterStatusViewModel), typeof(DashboardMainView), new PropertyMetadata(null));

        public RosterStatusViewModel? Status
        {
            get => (RosterStatusViewModel?)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public string SessionToolTip { get; private set; } = "";

        internal event EventHandler? RefreshRequested;
        internal event EventHandler? OpenReplayRequested;
        internal event EventHandler? HistoryRequested;
        internal event EventHandler? SettingsRequested;
        internal event EventHandler? UpdateRequested;
        internal event EventHandler? ScreenshotRequested;
        internal event Action<int>? SortModeChanged;
        internal event Action<string>? LanguageChanged;
        internal event EventHandler<DashboardPlayerActionEventArgs>? PlayerActionRequested;

        public DashboardMainView()
        {
            InitializeComponent();
            detailOpenTimer.Tick += DetailOpenTimer_Tick;
            detailCloseTimer.Tick += DetailCloseTimer_Tick;
            PlayerDetailPopup.CustomPopupPlacementCallback = PlacePlayerDetailPopup;
            DataContextChanged += DashboardMainView_DataContextChanged;
            Loaded += (_, _) =>
            {
                RefreshLocalizedSelectors();
                RefreshSettings();
                UpdateResponsiveLayout();
            };
            InitializeCharts();
        }

        internal void SetDashboard(BattleDashboardViewModel dashboard)
        {
            DataContext = dashboard;
            SortCombo.SelectedValue = dashboard.SortMode.ToString();
            RefreshSettings();
            UpdateContextHeader();
            UpdateFilterLabels();
        }

        internal void RefreshSettings()
        {
            Visibility account = Properties.Settings.Default.ShowAccountRosterColumn ? Visibility.Visible : Visibility.Collapsed;
            Visibility ship = Properties.Settings.Default.ShowShipRosterColumn ? Visibility.Visible : Visibility.Collapsed;
            Visibility pr = Properties.Settings.Default.PRVisibility == 2 ? Visibility.Collapsed : Visibility.Visible;
            Visibility performance = Properties.Settings.Default.ShowPerformanceRosterColumn ? Visibility.Visible : Visibility.Collapsed;

            AllyContextColumn.Visibility = EnemyContextColumn.Visibility = account;
            AllyShipColumn.Visibility = EnemyShipColumn.Visibility = ship;
            AllyPrColumn.Visibility = EnemyPrColumn.Visibility = pr;
            AllyPerformanceColumn.Visibility = EnemyPerformanceColumn.Visibility = performance;
        }

        internal void UpdateSessionSummary(string text, string tooltip)
        {
            SessionText.Text = text;
            SessionToolTip = tooltip;
            SessionCard.ToolTip = tooltip;
        }

        internal void SetRosterInputEnabled(bool enabled)
        {
            RefreshButton.IsEnabled = enabled;
            OpenReplayButton.IsEnabled = enabled;
        }

        internal void UpdateBattlefield(Battlefield battlefield)
        {
            DashboardWinrateChart.Series = ChartUtils.GetWinrateChartSeries(battlefield, Properties.Settings.Default.WinrateChartType);
            DashboardWinrateChart.Sections = ChartUtils.GetWinrateChartSections(battlefield, Properties.Settings.Default.WinrateChartType);
            DashboardKdeChart.Series = ChartUtils.GetKDEChartSeries(battlefield);
            DashboardOutputText.Text = TextUtils.GenerateGeneralStatisticsOutputText(battlefield);
        }

        internal void RefreshLocalizedSelectors()
        {
            suppressSelectors = true;
            LanguageCombo.Items.Clear();
            LanguageCombo.Items.Add(new ListItem { Content = Find("ComboBoxItemLanguageAuto", "Auto"), Value = "AUTO" });
            LanguageCombo.Items.Add(new ListItem { Content = Find("ComboBoxItemLanguageEnglish", "English"), Value = "EN_US" });
            LanguageCombo.Items.Add(new ListItem { Content = Find("ComboBoxItemLanguageSimplifiedChinese", "简体中文"), Value = "ZH_CN" });
            LanguageCombo.DisplayMemberPath = nameof(ListItem.Content);
            LanguageCombo.SelectedValuePath = nameof(ListItem.Value);
            LanguageCombo.SelectedValue = Properties.Settings.Default.Language;

            SortCombo.Items.Clear();
            AddSort(0, "ComboBoxItemSortByShipTypeAndShipTierAndWinrateDescending", "Ship type / tier / win rate");
            AddSort(1, "ComboBoxItemSortByShipTierAndShipTypeAndWinrateDescending", "Tier / ship type / win rate");
            AddSort(2, "ComboBoxItemSortByShipTypeAndWinrateDescending", "Ship type / win rate");
            AddSort(3, "ComboBoxItemSortByShipTierAndWinrateDescending", "Tier / win rate");
            AddSort(4, "ComboBoxItemSortByWinrateDescending", "Win rate descending");
            AddSort(5, "ComboBoxItemSortByWinrateAscending", "Win rate ascending");
            AddSort(6, "ComboBoxItemSortByBattlesDescending", "Battles descending");
            AddSort(7, "ComboBoxItemSortByBattlesAscending", "Battles ascending");
            SortCombo.DisplayMemberPath = nameof(ListItem.Content);
            SortCombo.SelectedValuePath = nameof(ListItem.Value);
            int selectedSort = DataContext is BattleDashboardViewModel dashboard ? dashboard.SortMode : Properties.Settings.Default.PlayerListSortBy;
            SortCombo.SelectedValue = Math.Clamp(selectedSort, 0, 7).ToString();
            suppressSelectors = false;
            UpdateFilterLabels();
        }

        internal void CloseTransientUi()
        {
            ClosePlayerDetail();
            NotificationPopup.IsOpen = false;
        }

        private void AddSort(int value, string resource, string fallback) =>
            SortCombo.Items.Add(new ListItem { Content = Find(resource, fallback), Value = value.ToString() });

        private void InitializeCharts()
        {
            DashboardWinrateChart.Tooltip = new ShipAwareChartTooltip();
            string chartFontFamily = ChartFontUtils.Resolve(DashboardWinrateChart.FontFamily);
            DashboardWinrateChart.TooltipTextPaint = new SolidColorPaint { Color = SKColors.Black, FontFamily = chartFontFamily };
            DashboardWinrateChart.XAxes = new Axis[] { new Axis { IsVisible = false } };
            DashboardWinrateChart.YAxes = new Axis[] { new Axis { Labeler = d => d.ToString("p1"), LabelsPaint = new SolidColorPaint { Color = SKColors.Black, FontFamily = chartFontFamily }, CrosshairPaint = new SolidColorPaint(SKColors.Gray) } };
            DashboardKdeChart.XAxes = new Axis[] { new Axis { Labeler = d => d.ToString("p1"), LabelsPaint = new SolidColorPaint { Color = SKColors.Black, FontFamily = chartFontFamily }, SeparatorsPaint = new SolidColorPaint(SKColors.LightGray), CrosshairPaint = new SolidColorPaint(SKColors.Gray) } };
            DashboardKdeChart.YAxes = new Axis[] { new Axis { IsVisible = false } };
        }

        private void DashboardMainView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is BattleDashboardViewModel oldDashboard)
                oldDashboard.PropertyChanged -= Dashboard_PropertyChanged;
            if (e.NewValue is BattleDashboardViewModel dashboard)
                dashboard.PropertyChanged += Dashboard_PropertyChanged;
            UpdateContextHeader();
            UpdateFilterLabels();
        }

        private void Dashboard_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BattleDashboardViewModel.Context) or nameof(BattleDashboardViewModel.ContextHeader))
                UpdateContextHeader();
            if (e.PropertyName is nameof(BattleDashboardViewModel.Allies) or nameof(BattleDashboardViewModel.Enemies) || e.PropertyName?.Contains("Count", StringComparison.Ordinal) == true)
                UpdateFilterLabels();
            if (e.PropertyName == nameof(BattleDashboardViewModel.Summary))
                UpdateMatchupLayout();
        }

        private void UpdateContextHeader()
        {
            if (DataContext is not BattleDashboardViewModel dashboard) return;
            AllyContextColumn.Header = dashboard.ContextHeader;
            EnemyContextColumn.Header = dashboard.ContextHeader;
            AccountContext.Style = (Style)FindResource(dashboard.Context == DashboardRosterContext.Account ? "SegmentActive" : "SegmentButton");
            TierContext.Style = (Style)FindResource(dashboard.Context == DashboardRosterContext.Tier ? "SegmentActive" : "SegmentButton");
        }

        private void UpdateFilterLabels()
        {
            if (DataContext is not BattleDashboardViewModel dashboard) return;
            AllyAll.Content = $"{Find("DashboardFilterAll", "All")} {dashboard.AllyTotalCount}";
            AllyMarked.Content = $"{Find("DashboardFilterMarked", "Marked")} {dashboard.AllyMarkedCount}";
            AllyLow.Content = $"{Find("DashboardFilterLowSample", "Low")} {dashboard.AllyLowSampleCount}";
            AllyAnomaly.Content = $"{Find("DashboardFilterAnomaly", "Issues")} {dashboard.AllyAnomalyCount}";
            EnemyAll.Content = $"{Find("DashboardFilterAll", "All")} {dashboard.EnemyTotalCount}";
            EnemyMarked.Content = $"{Find("DashboardFilterMarked", "Marked")} {dashboard.EnemyMarkedCount}";
            EnemyLow.Content = $"{Find("DashboardFilterLowSample", "Low")} {dashboard.EnemyLowSampleCount}";
            EnemyAnomaly.Content = $"{Find("DashboardFilterAnomaly", "Issues")} {dashboard.EnemyAnomalyCount}";
            UpdateFilterStyles(dashboard);
        }

        private void UpdateFilterStyles(BattleDashboardViewModel dashboard)
        {
            SetFilterStyle(AllyAll, dashboard.AllyFilter == DashboardRosterFilter.All);
            SetFilterStyle(AllyMarked, dashboard.AllyFilter == DashboardRosterFilter.Marked);
            SetFilterStyle(AllyLow, dashboard.AllyFilter == DashboardRosterFilter.LowSample);
            SetFilterStyle(AllyAnomaly, dashboard.AllyFilter == DashboardRosterFilter.Anomaly);
            SetFilterStyle(EnemyAll, dashboard.EnemyFilter == DashboardRosterFilter.All);
            SetFilterStyle(EnemyMarked, dashboard.EnemyFilter == DashboardRosterFilter.Marked);
            SetFilterStyle(EnemyLow, dashboard.EnemyFilter == DashboardRosterFilter.LowSample);
            SetFilterStyle(EnemyAnomaly, dashboard.EnemyFilter == DashboardRosterFilter.Anomaly);
        }

        private void SetFilterStyle(Button button, bool active) =>
            button.Style = (Style)FindResource(active ? "FilterButtonActive" : "FilterButton");

        private void UpdateResponsiveLayout()
        {
            bool compactSidebar = ActualWidth < 1400;
            SidebarColumn.Width = new GridLength(compactSidebar ? 64 : 172);
            BrandText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavBattleText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavHistoryText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavAnalysisText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavReplayText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavSettingsText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            SessionCard.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            SidebarVersion.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            SidebarLinks.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            UpdateMatchupLayout();
            AnalysisDrawer.Width = Math.Min(420, Math.Max(320, ActualWidth - 64));
            UpdatePlayerDetailBounds();
        }

        private void UpdateMatchupLayout()
        {
            bool show = ActualWidth >= 1180 && DataContext is BattleDashboardViewModel { Summary.HasKeyShipMatchup: true };
            AllyMatchupPanel.Visibility = EnemyMatchupPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            AllyMatchupColumn.Width = EnemyMatchupColumn.Width = show ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            AllyMatchupSeparatorColumn.Width = EnemyMatchupSeparatorColumn.Width = show ? new GridLength(1) : new GridLength(0);
            ComparisonColumn.Width = show ? new GridLength(430) : new GridLength(1, GridUnitType.Star);
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BattleDashboardViewModel dashboard || sender is not Button { Tag: string tag }) return;
            string[] parts = tag.Split(':');
            if (parts.Length != 2 || !Enum.TryParse(parts[1], out DashboardRosterFilter filter)) return;
            dashboard.SetFilter(string.Equals(parts[0], "Ally", StringComparison.OrdinalIgnoreCase), filter);
            ClosePlayerDetail();
        }

        private void Context_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BattleDashboardViewModel dashboard || sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out DashboardRosterContext context)) return;
            dashboard.SetContext(context);
            ClosePlayerDetail();
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectors || SortCombo.SelectedValue == null) return;
            int value = Convert.ToInt32(SortCombo.SelectedValue);
            if (DataContext is BattleDashboardViewModel dashboard) dashboard.SetSortMode(value);
            SortModeChanged?.Invoke(value);
            ClosePlayerDetail();
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectors || LanguageCombo.SelectedValue is not string value) return;
            LanguageChanged?.Invoke(value);
        }

        private void ToggleAnalysis_Click(object sender, RoutedEventArgs e)
        {
            analysisOpen = !analysisOpen;
            AnalysisDrawer.Visibility = analysisOpen ? Visibility.Visible : Visibility.Collapsed;
            ClosePlayerDetail();
        }

        private void Status_Click(object sender, RoutedEventArgs e) => NotificationPopup.IsOpen = !NotificationPopup.IsOpen;
        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        private void OpenReplay_Click(object sender, RoutedEventArgs e) => OpenReplayRequested?.Invoke(this, EventArgs.Empty);
        private void History_Click(object sender, RoutedEventArgs e) => HistoryRequested?.Invoke(this, EventArgs.Empty);
        private void SessionCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => HistoryRequested?.Invoke(this, EventArgs.Empty);
        private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        private void Update_Click(object sender, RoutedEventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        private void Screenshot_Click(object sender, RoutedEventArgs e) => ScreenshotRequested?.Invoke(this, EventArgs.Empty);
        private void Help_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", "https://lxdev.org/aperadar/");

        private void RosterGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            // A ContextMenu stored in a shared Style setter can resolve to
            // DependencyProperty.UnsetValue when virtualized rows are reused. Give every
            // realized row its own menu so right-click and Shift+F10 are always safe.
            if (e.Row.ContextMenu == null)
                e.Row.ContextMenu = CreatePlayerContextMenu();
        }

        private ContextMenu CreatePlayerContextMenu()
        {
            ContextMenu menu = new();
            AddPlayerMenuItem(menu, "ContextMenuRefreshPlayer", DashboardPlayerAction.Refresh);
            AddPlayerMenuItem(menu, "ContextMenuCopyPlayerStatistics", DashboardPlayerAction.Copy);
            menu.Items.Add(new Separator());
            AddPlayerMenuItem(menu, "ContextMenuCheckOnWoWSOfficialSite", DashboardPlayerAction.Official);
            AddPlayerMenuItem(menu, "ContextMenuCheckOnWoWSNumbers", DashboardPlayerAction.Numbers);
            menu.Items.Add(new Separator());
            AddPlayerMenuItem(menu, "ContextMenuCustomMarker", DashboardPlayerAction.CustomMarker);
            AddPlayerMenuItem(menu, "ContextMenuFixedTeammate", DashboardPlayerAction.FixedTeammate);
            AddPlayerMenuItem(menu, "ContextMenuEditNote", DashboardPlayerAction.EditNote);
            AddPlayerMenuItem(menu, "ContextMenuClearNote", DashboardPlayerAction.ClearNote);
            menu.Items.Add(new Separator());
            AddPlayerMenuItem(menu, "ContextMenuAddToWatchListPositive", DashboardPlayerAction.WatchPositive);
            AddPlayerMenuItem(menu, "ContextMenuAddToWatchListNegtive", DashboardPlayerAction.WatchNegative);
            AddPlayerMenuItem(menu, "ContextMenuAddToWatchListCheater", DashboardPlayerAction.WatchCheater);
            AddPlayerMenuItem(menu, "ContextMenuRemoveFromWatchList", DashboardPlayerAction.WatchRemove);
            return menu;
        }

        private void AddPlayerMenuItem(ContextMenu menu, string resourceKey, DashboardPlayerAction action)
        {
            MenuItem item = new() { Tag = action.ToString() };
            item.SetResourceReference(HeaderedItemsControl.HeaderProperty, resourceKey);
            item.Click += PlayerMenu_Click;
            menu.Items.Add(item);
        }

        private void PlayerMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string tag } item || !Enum.TryParse(tag, out DashboardPlayerAction action)) return;
            ContextMenu? menu = ItemsControl.ItemsControlFromItemContainer(item) as ContextMenu;
            if (menu?.PlacementTarget is not DataGridRow { DataContext: DashboardPlayerRowViewModel row }) return;
            PlayerActionRequested?.Invoke(this, new DashboardPlayerActionEventArgs(action, row.Player));
        }

        private void RosterRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not DataGridRow { DataContext: DashboardPlayerRowViewModel row } target) return;
            detailCloseTimer.Stop();
            pointerOverDetailRow = true;
            pendingDetail = row;
            pendingDetailTarget = target;
            detailOpenTimer.Stop();
            detailOpenTimer.Start();
        }

        private void RosterRow_MouseLeave(object sender, MouseEventArgs e)
        {
            detailOpenTimer.Stop();
            pointerOverDetailRow = false;
            detailCloseTimer.Stop();
            detailCloseTimer.Start();
        }

        private void DetailOpenTimer_Tick(object? sender, EventArgs e)
        {
            detailOpenTimer.Stop();
            if (pendingDetail == null || pendingDetailTarget == null || (!pointerOverDetailRow && !pendingDetailTarget.IsMouseOver)) return;
            PlayerDetailCardContent.DataContext = pendingDetail.BaseRow.Detail;
            PlayerDetailPopup.PlacementTarget = pendingDetailTarget;
            PlayerDetailPopup.IsOpen = true;
            UpdatePlayerDetailBounds();
            detailCloseTimer.Start();
        }

        private void PlayerDetailPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            pointerOverDetail = true;
            detailCloseTimer.Stop();
        }

        private void PlayerDetailPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            pointerOverDetail = false;
            detailCloseTimer.Stop();
            detailCloseTimer.Start();
        }

        private void DetailCloseTimer_Tick(object? sender, EventArgs e)
        {
            detailCloseTimer.Stop();
            if (pointerOverDetailRow || pointerOverDetail || PlayerDetailBorder.IsMouseOver || pendingDetailTarget?.IsMouseOver == true)
            {
                detailCloseTimer.Start();
                return;
            }
            ClosePlayerDetail();
        }

        private void UpdatePlayerDetailBounds()
        {
            System.Windows.Forms.Screen screen;
            if (pendingDetailTarget is FrameworkElement target && target.IsLoaded)
            {
                Point point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
            }
            else
            {
                screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            }
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            PlayerDetailBorder.Width = Math.Min(560, Math.Max(360, screen.WorkingArea.Width / dpi.DpiScaleX - 32));
            PlayerDetailBorder.MaxHeight = Math.Min(720, Math.Max(300, screen.WorkingArea.Height / dpi.DpiScaleY * 0.70));
        }

        private CustomPopupPlacement[] PlacePlayerDetailPopup(Size popupSize, Size targetSize, Point offset)
        {
            double y = targetSize.Height / 2 - popupSize.Height / 2;
            bool ally = FindVisualParent<DataGrid>(pendingDetailTarget) == AlliesGrid;
            double preferredX = ally ? targetSize.Width + 8 : -popupSize.Width - 8;
            double fallbackX = ally ? -popupSize.Width - 8 : targetSize.Width + 8;
            return new[]
            {
                new CustomPopupPlacement(new Point(preferredX, y), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(fallbackX, y), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(preferredX, targetSize.Height + 6), PopupPrimaryAxis.Vertical),
                new CustomPopupPlacement(new Point(preferredX, -popupSize.Height - 6), PopupPrimaryAxis.Vertical)
            };
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match) return match;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void ClosePlayerDetail()
        {
            detailOpenTimer.Stop();
            detailCloseTimer.Stop();
            PlayerDetailPopup.IsOpen = false;
            PlayerDetailCardContent.DataContext = null;
            pendingDetail = null;
            pendingDetailTarget = null;
            pointerOverDetailRow = false;
            pointerOverDetail = false;
        }

        private void RosterGrid_ScrollChanged(object sender, ScrollChangedEventArgs e) => ClosePlayerDetail();

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (PlayerDetailPopup.IsOpen)
            {
                ClosePlayerDetail();
                e.Handled = true;
            }
            else if (analysisOpen)
            {
                analysisOpen = false;
                AnalysisDrawer.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClosePlayerDetail();
            UpdateResponsiveLayout();
        }

        private string Find(string resourceKey, string fallback) => TryFindResource(resourceKey) as string ?? fallback;
    }
}
