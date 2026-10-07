using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using ApeRadar.ViewModels;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        private readonly PlayerDetailPopupController<DashboardPlayerRowViewModel> detailPopup;
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
            detailPopup = new PlayerDetailPopupController<DashboardPlayerRowViewModel>(
                PlayerDetailPopup, PlayerDetailBorder, PlayerDetailCardContent, this, AlliesGrid,
                row => row.BaseRow.Detail, openIfTargetHovered: true);
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
            RefreshLocalizedSelectors();
            RefreshSettings();
            UpdateContextHeader();
            UpdateFilterLabels();
        }

        internal void RefreshSettings()
        {
            Visibility account = Properties.Settings.Default.ShowAccountRosterColumn ? Visibility.Visible : Visibility.Collapsed;
            Visibility ship = Properties.Settings.Default.ShowShipRosterColumn ? Visibility.Visible : Visibility.Collapsed;
            Visibility performance = Properties.Settings.Default.ShowPerformanceRosterColumn ? Visibility.Visible : Visibility.Collapsed;

            AllyContextColumn.Visibility = EnemyContextColumn.Visibility = account;
            AllyShipColumn.Visibility = EnemyShipColumn.Visibility = ship;
            AllyPerformanceColumn.Visibility = EnemyPerformanceColumn.Visibility = performance;
            RosterPerformanceMetric metric = RosterPerformanceMetricExtensions.Parse(Properties.Settings.Default.RosterPerformanceMetric);
            bool weighted = metric == RosterPerformanceMetric.Winrate && Properties.Settings.Default.WinrateTypeUsed != 0;
            string basis = metric == RosterPerformanceMetric.PR ? "PR" : weighted ? "WWR" : "WR";
            string fullBasis = metric == RosterPerformanceMetric.PR ? Find("RosterPerformancePR", "Account PR") :
                weighted ? Find("RosterPerformanceWeightedWinrate", "Weighted win rate") :
                Find("RosterPerformanceAccountWinrate", "Account win rate");
            AllyPerformanceColumn.Header = EnemyPerformanceColumn.Header = $"{Find("DashboardColumnPerformance", "Skill")} · {basis}";
            Style skillHeader = new(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), (Style)FindResource("DashboardMetricHeader"));
            skillHeader.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, fullBasis));
            AllyPerformanceColumn.HeaderStyle = EnemyPerformanceColumn.HeaderStyle = skillHeader;
        }

        internal void UpdateSessionSummary(string text, string tooltip)
        {
            SessionText.Text = text;
            SessionToolTip = tooltip;
            SessionCard.ToolTip = tooltip;
            CompactSessionButton.ToolTip = tooltip;
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
            GamePathStatusText.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
            suppressSelectors = true;
            UpdateCompactMetricWidths();
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
            SortCombo.SelectedIndex = Math.Clamp(selectedSort, 0, 7);
            suppressSelectors = false;
            UpdateFilterLabels();
            UpdateContextHeader();
            RefreshSettings();
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
                UpdateResponsiveLayout();
        }

        private void UpdateCompactMetricWidths()
        {
            double Measure(string value, string fontResource)
            {
                TextBlock text = new() { Text = value, FontFamily = (System.Windows.Media.FontFamily)FindResource(fontResource), FontSize = 12 };
                System.Windows.Media.TextOptions.SetTextFormattingMode(text, System.Windows.Media.TextFormattingMode.Display);
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return Math.Ceiling(text.DesiredSize.Width) + 13; // Cell border and two 6 DIP margins.
            }

            string Label(string key, string fallback) => Application.Current?.TryFindResource(key) as string ?? Find(key, fallback);
            double contextWidth = Math.Max(80, Measure($"999999 {Label("RosterMetricBattlesShort", "Games")}", "NumericFontFamily"));
            double prWidth = Math.Max(84, new[] { "DashboardContextAccount", "DashboardContextTier", "RosterPrShipShort" }
                .Max(key => Measure($"{Label(key, "Account")} 99999", "AppFontFamily")));
            AllyContextColumn.MinWidth = EnemyContextColumn.MinWidth = contextWidth;
            AllyPrColumn.MinWidth = EnemyPrColumn.MinWidth = prWidth;
            AllyContextColumn.Width = EnemyContextColumn.Width = new DataGridLength(contextWidth);
            AllyPrColumn.Width = EnemyPrColumn.Width = new DataGridLength(prWidth);
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
            SetFilterLabel(AllyAll, "DashboardFilterAll", "All", "•", dashboard.AllyTotalCount);
            SetFilterLabel(AllyMarked, "DashboardFilterMarked", "Marked", "★", dashboard.AllyMarkedCount);
            SetFilterLabel(AllyLow, "DashboardFilterLowSample", "Low", "↓", dashboard.AllyLowSampleCount);
            SetFilterLabel(AllyAnomaly, "DashboardFilterAnomaly", "Issues", "!", dashboard.AllyAnomalyCount);
            SetFilterLabel(EnemyAll, "DashboardFilterAll", "All", "•", dashboard.EnemyTotalCount);
            SetFilterLabel(EnemyMarked, "DashboardFilterMarked", "Marked", "★", dashboard.EnemyMarkedCount);
            SetFilterLabel(EnemyLow, "DashboardFilterLowSample", "Low", "↓", dashboard.EnemyLowSampleCount);
            SetFilterLabel(EnemyAnomaly, "DashboardFilterAnomaly", "Issues", "!", dashboard.EnemyAnomalyCount);
            UpdateFilterStyles(dashboard);
        }

        private void SetFilterLabel(Button button, string key, string fallback, string glyph, int count)
        {
            string label = $"{Find(key, fallback)} {count}";
            button.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.MinWidth = AlliesGrid.ActualWidth < 340 ? 32 : 48;
            button.Padding = new Thickness(AlliesGrid.ActualWidth < 340 ? 3 : 5, 0, AlliesGrid.ActualWidth < 340 ? 3 : 5, 0);
            button.Content = AlliesGrid.ActualWidth < 440 ? $"{glyph}{count}" : label;
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
            UpdateCompactMetricWidths();
            DashboardLayout layout = DashboardLayoutCalculator.Calculate(ActualWidth, ActualHeight);
            bool compactSidebar = layout.CompactSidebar;
            SidebarColumn.Width = new GridLength(layout.SidebarWidth);
            BrandText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavBattleText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavHistoryText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavAnalysisText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavReplayText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavSettingsText.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            SessionCard.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            CompactSessionButton.Visibility = compactSidebar ? Visibility.Visible : Visibility.Collapsed;
            SidebarVersion.Visibility = compactSidebar ? Visibility.Collapsed : Visibility.Visible;
            SidebarLinks.Visibility = Visibility.Visible;
            SidebarLinks.ColumnDefinitions[1].Width = compactSidebar ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(HelpNavButton, compactSidebar ? 0 : 1);
            Grid.SetRow(HelpNavButton, compactSidebar ? 1 : 0);
            if (compactSidebar)
            {
                UpdateNavButton.Content = "↑";
                HelpNavButton.Content = "?";
            }
            else
            {
                UpdateNavButton.SetResourceReference(ContentControl.ContentProperty, "BtnSoftwareUpdate");
                HelpNavButton.SetResourceReference(ContentControl.ContentProperty, "DashboardHelp");
            }
            MainContentGrid.Margin = layout.PageMargin;
            BrandRow.Height = new GridLength(layout.CompactHeight ? 52 : 72);
            BrandHeaderGrid.Margin = new Thickness(14, layout.CompactHeight ? 8 : 12, 14, layout.CompactHeight ? 8 : 12);
            foreach (Button button in NavigationButtons.Children.OfType<Button>())
            {
                button.Height = layout.CompactHeight ? 32 : 44;
                button.Margin = new Thickness(8, layout.CompactHeight ? 1 : 2, 8, layout.CompactHeight ? 1 : 2);
            }
            BattleHeading.Margin = new Thickness(0, 0, 0, layout.CompactHeight ? 0 : 4);
            ((Grid)MainContentGrid.Parent).RowDefinitions[0].Height = new GridLength(layout.CompactHeight ? 36 : DashboardLayout.TopBarHeight);
            MainContentGrid.RowDefinitions[0].Height = new GridLength(layout.CompactHeight ? 32 : DashboardLayout.HeadingHeight);
            MainContentGrid.RowDefinitions[1].Height = new GridLength(DashboardLayout.SummaryHeight);
            MainContentGrid.RowDefinitions[2].Height = new GridLength(DashboardLayout.ToolbarHeight);
            ComparisonCard.Height = 64;
            BattleMetadataText.Visibility = ActualWidth < 1240 ? Visibility.Collapsed : Visibility.Visible;

            // Use the viewport budget, independent of filters or the overlay drawer.
            // Keep the readable two-line minimum on genuinely smaller windows.
            AlliesGrid.RowHeight = EnemiesGrid.RowHeight = layout.RowHeight;
            bool showFixedRosterCapacity = layout.FitsFullRoster;
            RosterAreaRow.Height = showFixedRosterCapacity
                ? GridLength.Auto
                : new GridLength(1, GridUnitType.Star);
            RosterTeamsGrid.Height = showFixedRosterCapacity
                ? layout.RosterHeight
                : double.NaN;
            RosterTeamsGrid.VerticalAlignment = showFixedRosterCapacity
                ? VerticalAlignment.Top
                : VerticalAlignment.Stretch;
            UpdateFilterLabels();
            AnalysisDrawer.Width = Math.Min(420, Math.Max(320, ActualWidth - 64));
            detailPopup.UpdateBounds();
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
            if (analysisOpen)
            {
                ChartUtils.EnsureLoaded(DashboardWinrateChart);
                ChartUtils.EnsureLoaded(DashboardKdeChart);
            }
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
            detailPopup.RowEntered(row, target);
        }

        private void RosterRow_MouseLeave(object sender, MouseEventArgs e) => detailPopup.RowLeft();

        private void PlayerDetailPopup_MouseEnter(object sender, MouseEventArgs e) => detailPopup.PopupEntered();

        private void PlayerDetailPopup_MouseLeave(object sender, MouseEventArgs e) => detailPopup.PopupLeft();

        private void ClosePlayerDetail() => detailPopup.Close();

        private void RosterGrid_ScrollChanged(object sender, ScrollChangedEventArgs e) => ClosePlayerDetail();

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (NotificationPopup.IsOpen)
            {
                NotificationPopup.IsOpen = false;
                StatusButton.Focus();
                e.Handled = true;
            }
            else if (PlayerDetailPopup.IsOpen)
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
