using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;

using System.IO;
using System.Diagnostics;
using System.Windows.Threading;

using ApeRadar.Models;
using ApeRadar.Utils;
using ApeRadar.Utils.Sorters;
using ApeRadar.History;
using ApeRadar.Services;

using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Globalization;
using System.Threading;
using ApeRadar.ViewModels;
using ApeRadar.Controls;

namespace ApeRadar
{
    partial class MainWindow : Window
    {
        private readonly IRosterLoadSettings rosterSettings = new RosterLoadSettings();
        private readonly IStatsCache statsCache = new PersistentStatsCache();
        private string currentBattleFilename = "";
        private string currentBattleID = "";
        private DateTimeOffset currentBattleStartTime = DateTimeOffset.MinValue;
        private int sessionSummaryTicks;
        private bool sessionSummaryRefreshing;
        private long rosterUiGeneration;
        private bool analysisDrawerOpen;
        private bool notificationsExpanded;
        private readonly PlayerDetailPopupController<PlayerRosterRowViewModel> detailPopup;
        private readonly IRosterLoadUseCase rosterLoadUseCase;
        private readonly IRosterHistoryCaptureService rosterHistoryCaptureService;
        private readonly IRosterPresentationService rosterPresentationService = new RosterPresentationService();
        private readonly IDashboardPresentationService dashboardPresentationService = new DashboardPresentationService();
        private readonly HistoryServices historyServices;
        private readonly bool ownsHistoryServices;
        private readonly BattleDashboardViewModel dashboard;
        private readonly bool useDashboardInterface;
        private DashboardBattleMetadata currentDashboardMetadata = DashboardBattleMetadata.Empty;

        private readonly ObservableCollection<PlayerRosterRowViewModel> alliesRosterRows = new();
        private readonly ObservableCollection<PlayerRosterRowViewModel> enemiesRosterRows = new();
        private readonly ObservableCollection<string> accountMetricHeaders = new();
        private readonly ObservableCollection<string> weightedMetricHeaders = new();
        private readonly ObservableCollection<string> shipMetricHeaders = new();
        private readonly ObservableCollection<string> personalRatingMetricHeaders = new();
        private readonly ObservableCollection<string> tierMetricHeaders = new();
        public IEnumerable AlliesRosterRows => alliesRosterRows;
        public IEnumerable EnemiesRosterRows => enemiesRosterRows;
        public IEnumerable<string> AccountMetricHeaders => accountMetricHeaders;
        public IEnumerable<string> WeightedMetricHeaders => weightedMetricHeaders;
        public IEnumerable<string> ShipMetricHeaders => shipMetricHeaders;
        public IEnumerable<string> PersonalRatingMetricHeaders => personalRatingMetricHeaders;
        public IEnumerable<string> TierMetricHeaders => tierMetricHeaders;

        public static readonly DependencyProperty EffectivePlayerFontSizeProperty = DependencyProperty.Register(
            nameof(EffectivePlayerFontSize), typeof(double), typeof(MainWindow), new PropertyMetadata(18d));
        public static readonly DependencyProperty EffectiveStatisticsFontSizeProperty = DependencyProperty.Register(
            nameof(EffectiveStatisticsFontSize), typeof(double), typeof(MainWindow), new PropertyMetadata(16d));
        public static readonly DependencyProperty EffectiveColumnHeaderHeightProperty = DependencyProperty.Register(
            nameof(EffectiveColumnHeaderHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(28d));
        public static readonly DependencyProperty IsCompactRosterProperty = DependencyProperty.Register(
            nameof(IsCompactRoster), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
        public static readonly DependencyProperty UseCompactStatusBadgesProperty = DependencyProperty.Register(
            nameof(UseCompactStatusBadges), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public double EffectivePlayerFontSize
        {
            get => (double)GetValue(EffectivePlayerFontSizeProperty);
            private set => SetValue(EffectivePlayerFontSizeProperty, value);
        }

        public double EffectiveStatisticsFontSize
        {
            get => (double)GetValue(EffectiveStatisticsFontSizeProperty);
            private set => SetValue(EffectiveStatisticsFontSizeProperty, value);
        }

        public double EffectiveColumnHeaderHeight
        {
            get => (double)GetValue(EffectiveColumnHeaderHeightProperty);
            private set => SetValue(EffectiveColumnHeaderHeightProperty, value);
        }

        public bool IsCompactRoster
        {
            get => (bool)GetValue(IsCompactRosterProperty);
            private set => SetValue(IsCompactRosterProperty, value);
        }

        public bool UseCompactStatusBadges
        {
            get => (bool)GetValue(UseCompactStatusBadgesProperty);
            private set => SetValue(UseCompactStatusBadgesProperty, value);
        }

        public RosterStatusViewModel RosterStatus { get; } = new();
        internal BattleDashboardViewModel Dashboard => dashboard;
        internal HistoryServices HistoryServices => historyServices;

        private void SwitchLanguage(Language language)
        {
            Application.Current.Resources.MergedDictionaries[0] = LanguageExt.GetResourceDictionaryByLanguage(language);
            LabelGamePathIsSetOrNot.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();

            ComboBoxLanguage.SelectionChanged -= ComboBoxLanguage_SelectionChanged;
            ComboBoxLanguage.Items.Clear();
            ComboBoxLanguage.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemLanguageAuto"), Value = "AUTO" });
            ComboBoxLanguage.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemLanguageEnglish"), Value = "EN_US" });
            ComboBoxLanguage.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemLanguageSimplifiedChinese"), Value = "ZH_CN" });
            ComboBoxLanguage.SelectedValue = Properties.Settings.Default.Language;
            ComboBoxLanguage.SelectionChanged += ComboBoxLanguage_SelectionChanged;

            ComboBoxChartType.SelectionChanged -= ComboBoxChartType_SelectionChanged;
            ComboBoxChartType.Items.Clear();
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTypeAndShipTierAndWinrateDescending"), Value = "0" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTierAndShipTypeAndWinrateDescending"), Value = "1" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTypeAndWinrateDescending"), Value = "2" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeShipTierAndWinrateDescending"), Value = "3" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeWinrateDescending"), Value = "4" });
            ComboBoxChartType.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemChartTypeWinrateAscending"), Value = "5" });
            ComboBoxChartType.SelectedValue = Properties.Settings.Default.WinrateChartType;
            ComboBoxChartType.SelectionChanged += ComboBoxChartType_SelectionChanged;

            ComboBoxMirrored.SelectionChanged -= ComboBoxMirrored_SelectionChanged;
            ComboBoxMirrored.Items.Clear();
            ComboBoxMirrored.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemMirroredDisplayDisabled"), Value = "False" });
            ComboBoxMirrored.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemMirroredDisplayEnabled"), Value = "True" });
            ComboBoxMirrored.SelectedValue = Properties.Settings.Default.EnemiesDisplayMirrored.ToString();
            ComboBoxMirrored.SelectionChanged += ComboBoxMirrored_SelectionChanged;

            ComboBoxSortBy.SelectionChanged -= ComboBoxSortBy_SelectionChanged;
            ComboBoxSortBy.Items.Clear();
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTypeAndShipTierAndWinrateDescending"), Value = "0" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTierAndShipTypeAndWinrateDescending"), Value = "1" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTypeAndWinrateDescending"), Value = "2" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByShipTierAndWinrateDescending"), Value = "3" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByWinrateDescending"), Value = "4" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByWinrateAscending"), Value = "5" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByBattlesDescending"), Value = "6" });
            ComboBoxSortBy.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemSortByBattlesAscending"), Value = "7" });
            ComboBoxSortBy.SelectedValue = Properties.Settings.Default.PlayerListSortBy;
            ComboBoxSortBy.SelectionChanged += ComboBoxSortBy_SelectionChanged;

            ComboBoxPlayerNamesVisibility.SelectionChanged -= ComboBoxPlayerNamesVisibility_SelectionChanged;
            ComboBoxPlayerNamesVisibility.Items.Clear();
            ComboBoxPlayerNamesVisibility.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemPlayerNamesVisible"), Value = "True" });
            ComboBoxPlayerNamesVisibility.Items.Add(new ListItem() { Content = Application.Current.FindResource("ComboBoxItemPlayerNamesHidden"), Value = "False" });
            ComboBoxPlayerNamesVisibility.SelectedValue = Properties.Settings.Default.PlayerNamesVisibility.ToString();
            ComboBoxPlayerNamesVisibility.SelectionChanged += ComboBoxPlayerNamesVisibility_SelectionChanged;

            if (DataContext is Battlefield battlefield)
            {
                RefreshRosterRows(battlefield);
                RefreshDataGridColumns(Properties.Settings.Default.EnemiesDisplayMirrored);
                SwitchSorting(Properties.Settings.Default.PlayerListSortBy);
                dashboard.RefreshPresentation();
            }
            DashboardView.RefreshLocalizedSelectors();
            DashboardView.RefreshSettings();
        }

        private void SwitchSorting(int sorting)
        {
            if (this.DataContext is not Battlefield)
            {
                return;
            }
            IComparer playerComparer = sorting switch
            {
                0 => new CustomSorterByShipTypeAndShipTierAndWinrateDescending(),
                1 => new CustomSorterByShipTierAndShipTypeAndWinrateDescending(),
                2 => new CustomSorterByShipTypeAndWinrateDescending(),
                3 => new CustomSorterByShipTierAndWinrateDescending(),
                4 => new CustomSorterByWinrateDescending(),
                5 => new CustomSorterByWinrateAscending(),
                6 => new CustomSorterByBattlesDescending(),
                7 => new CustomSorterByBattlesAscending(),
                _ => new CustomSorterByShipTypeAndShipTierAndWinrateDescending(),
            };
            PlayerRosterRowComparer comparer = new(playerComparer);
            if (CollectionViewSource.GetDefaultView(alliesRosterRows) is ListCollectionView alliesCollectionView)
                alliesCollectionView.CustomSort = comparer;
            if (CollectionViewSource.GetDefaultView(enemiesRosterRows) is ListCollectionView enemiesCollectionView)
                enemiesCollectionView.CustomSort = comparer;
        }

        private void SwitchWinrateChartType(int chartType)
        {
            if (this.DataContext is not Battlefield battlefield)
            {
                return;
            }
            WinrateChart.Series = ChartUtils.GetWinrateChartSeries(battlefield, chartType);
            WinrateChart.Sections = ChartUtils.GetWinrateChartSections(battlefield, chartType);
        }

        internal void RefreshDataGridColumns(bool mirrored)
        {
            UpdateMetricHeaders();
            DataGridAlliesList.Columns.Clear();
            AddRosterColumn(DataGridAlliesList, "RosterPlayerColumn");
            AddVisibleMetricColumns(DataGridAlliesList, mirrored: false);
            if (Properties.Settings.Default.ShowPerformanceRosterColumn)
                AddRosterColumn(DataGridAlliesList, "RosterPerformanceColumn");

            DataGridEnemiesList.Columns.Clear();
            if (mirrored)
            {
                if (Properties.Settings.Default.ShowPerformanceRosterColumn)
                    AddRosterColumn(DataGridEnemiesList, "RosterPerformanceColumn");
                AddVisibleMetricColumns(DataGridEnemiesList, mirrored: true);
                AddRosterColumn(DataGridEnemiesList, "RosterPlayerColumnMirrored");
            }
            else
            {
                AddRosterColumn(DataGridEnemiesList, "RosterPlayerColumn");
                AddVisibleMetricColumns(DataGridEnemiesList, mirrored: false);
                if (Properties.Settings.Default.ShowPerformanceRosterColumn)
                    AddRosterColumn(DataGridEnemiesList, "RosterPerformanceColumn");
            }
            Dispatcher.BeginInvoke(() =>
            {
                DataGridAlliesList.UpdateLayout();
                DataGridEnemiesList.UpdateLayout();
                ResetHorizontalScroll(DataGridAlliesList);
                ResetHorizontalScroll(DataGridEnemiesList);
                UpdateResponsiveLayout();
            }, DispatcherPriority.ContextIdle);
        }

        private void AddVisibleMetricColumns(DataGrid dataGrid, bool mirrored)
        {
            bool accountVisible = Properties.Settings.Default.ShowAccountRosterColumn && accountMetricHeaders.Count > 0;
            bool weightedVisible = weightedMetricHeaders.Count > 0;
            bool shipVisible = Properties.Settings.Default.ShowShipRosterColumn && shipMetricHeaders.Count > 0;
            bool personalRatingVisible = personalRatingMetricHeaders.Count > 0;

            string[] keys = mirrored
                ? new[] { "RosterTierColumn", "RosterPersonalRatingColumn", "RosterShipColumn", "RosterWeightedColumn", "RosterAccountColumn" }
                : new[] { "RosterAccountColumn", "RosterWeightedColumn", "RosterShipColumn", "RosterPersonalRatingColumn", "RosterTierColumn" };
            foreach (string key in keys)
            {
                if (key == "RosterAccountColumn" && !accountVisible) continue;
                if (key == "RosterWeightedColumn" && !weightedVisible) continue;
                if (key == "RosterShipColumn" && !shipVisible) continue;
                if (key == "RosterPersonalRatingColumn" && !personalRatingVisible) continue;
                if (key == "RosterTierColumn" && !Properties.Settings.Default.ShowTierPerformanceStats) continue;
                AddRosterColumn(dataGrid, key);
            }
        }

        private void UpdateMetricHeaders()
        {
            accountMetricHeaders.Clear();
            weightedMetricHeaders.Clear();
            shipMetricHeaders.Clear();
            personalRatingMetricHeaders.Clear();
            tierMetricHeaders.Clear();
            string Text(string key, string fallback) => TryFindResource(key) as string ?? fallback;
            void Add(ObservableCollection<string> target, int visibility, string key, string fallback)
            {
                if (visibility != 2) target.Add(Text(key, fallback));
            }

            Add(accountMetricHeaders, Properties.Settings.Default.AccountWinrateVisibility, "RosterMetricBattles", "Games");
            Add(accountMetricHeaders, Properties.Settings.Default.AccountWinrateVisibility, "RosterMetricWinrate", "WR");
            Add(accountMetricHeaders, Properties.Settings.Default.AccountAvgExpVisibility, "RosterMetricAvgExp", "XP");
            Add(weightedMetricHeaders, Properties.Settings.Default.WeightedWinrateVisibility, "RosterMetricWinrate", "WR");

            Add(shipMetricHeaders, Properties.Settings.Default.ShipWinrateVisibility, "RosterMetricBattles", "Games");
            Add(shipMetricHeaders, Properties.Settings.Default.ShipWinrateVisibility, "RosterMetricWinrate", "WR");
            Add(shipMetricHeaders, Properties.Settings.Default.ShipAvgDmgVisibility, "RosterMetricAvgDamage", "Dmg");
            Add(shipMetricHeaders, Properties.Settings.Default.ShipAvgExpVisibility, "RosterMetricAvgExp", "XP");

            Add(personalRatingMetricHeaders, Properties.Settings.Default.PRVisibility, "DataGridToolTipAccount", "Account");
            Add(personalRatingMetricHeaders, Properties.Settings.Default.PRVisibility, "DataGridToolTipShip", "Ship");

            tierMetricHeaders.Add(Text("RosterMetricBattles", "Games"));
            tierMetricHeaders.Add(Text("RosterMetricWinrate", "WR"));
            tierMetricHeaders.Add("PR");
        }

        private void AddRosterColumn(DataGrid dataGrid, string resourceKey)
        {
            if (TryFindResource(resourceKey) is DataGridColumn column)
            {
                column.Header = resourceKey switch
                {
                    "RosterAccountColumn" => accountMetricHeaders,
                    "RosterWeightedColumn" => weightedMetricHeaders,
                    "RosterShipColumn" => shipMetricHeaders,
                    "RosterPersonalRatingColumn" => personalRatingMetricHeaders,
                    "RosterTierColumn" => tierMetricHeaders,
                    _ => null
                };
                dataGrid.Columns.Add(column);
            }
        }

        private static void ResetHorizontalScroll(DataGrid dataGrid)
        {
            ScrollViewer? scrollViewer = dataGrid.Template.FindName("DG_ScrollViewer", dataGrid) as ScrollViewer
                ?? FindVisualChild<ScrollViewer>(dataGrid);
            scrollViewer?.ScrollToHorizontalOffset(0);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                T? nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        public MainWindow() : this(new HistoryServices(), initializeRuntime: true, ownsHistoryServices: true)
        {
        }

        internal MainWindow(bool initializeRuntime) : this(new HistoryServices(), initializeRuntime, ownsHistoryServices: true)
        {
        }

        internal MainWindow(HistoryServices historyServices, bool initializeRuntime) : this(historyServices, initializeRuntime, ownsHistoryServices: false)
        {
        }

        private MainWindow(HistoryServices historyServices, bool initializeRuntime, bool ownsHistoryServices)
        {
            this.historyServices = historyServices ?? throw new ArgumentNullException(nameof(historyServices));
            this.ownsHistoryServices = ownsHistoryServices;
            rosterLoadUseCase = new RosterLoadUseCase(new BattleRosterCoordinator());
            rosterHistoryCaptureService = new RosterHistoryCaptureService(() => this.historyServices.Coordinator);
            dashboard = new BattleDashboardViewModel(dashboardPresentationService);
            useDashboardInterface = ConfigWindow.NormalizeMainInterfaceStyle(Properties.Settings.Default.MainInterfaceStyle) == "Dashboard";
            InitializeComponent();
            LegacyRoot.Visibility = useDashboardInterface ? Visibility.Collapsed : Visibility.Visible;
            DashboardView.Visibility = useDashboardInterface ? Visibility.Visible : Visibility.Collapsed;
            DashboardView.Status = RosterStatus;
            DashboardView.SetDashboard(dashboard);
            DashboardView.RefreshRequested += (_, _) => BtnRefresh_Click(DashboardView, new RoutedEventArgs());
            DashboardView.OpenReplayRequested += (_, _) => BtnOpen_Click(DashboardView, new RoutedEventArgs());
            DashboardView.HistoryRequested += (_, _) => BtnHistory_Click(DashboardView, new RoutedEventArgs());
            DashboardView.SettingsRequested += (_, _) => BtnConfig_Click(DashboardView, new RoutedEventArgs());
            DashboardView.UpdateRequested += (_, _) => BtnSoftwareUpdate_Click(DashboardView, new RoutedEventArgs());
            DashboardView.ScreenshotRequested += (_, _) => BtnScreenshot_Click(DashboardView, new RoutedEventArgs());
            DashboardView.SortModeChanged += DashboardSortModeChanged;
            DashboardView.LanguageChanged += DashboardLanguageChanged;
            DashboardView.PlayerActionRequested += DashboardPlayerActionRequested;
            WinrateChart.Tooltip = new ShipAwareChartTooltip();
            detailPopup = new PlayerDetailPopupController<PlayerRosterRowViewModel>(
                PlayerDetailPopup, PlayerDetailCardBorder, PlayerDetailCardContent, this, DataGridAlliesList,
                row => row.Detail, keepOpenWhileOverRow: false);
            Deactivated += (_, _) =>
            {
                ClosePlayerDetail();
                DashboardView.CloseTransientUi();
            };

            Loaded += (_, _) => UpdateResponsiveLayout();

            if (!initializeRuntime)
            {
                return;
            }

            if (Properties.Settings.Default.DebugMode)
            {
                LogUtils.SetLogLevel(log4net.Core.Level.Debug);
            }
            else
            {
                LogUtils.SetLogLevel(log4net.Core.Level.Info);
            }

            LogUtils.WriteDebug("Debug Mode");
            LogUtils.WriteInfo($"SoftwareVersion: {Properties.Settings.Default.SoftwareVersion}");
            LogUtils.WriteInfo($"SoftwareDate: {Properties.Settings.Default.SoftwareDate}");

            LogUtils.WriteInfo($"OS Version: {Environment.OSVersion.VersionString}");
            Version currentVersion = Environment.OSVersion.Version;
            if (currentVersion.CompareTo(new Version("6.2")) < 0)
            {
                LogUtils.WriteInfo($"OS is Windows 7 or earlier. Some emoji chars may be displayed incorrectly.");
            }

            if (!Properties.Settings.Default.SettingsUpgradeDone)
            {
                LogUtils.WriteInfo($"Settings Upgrade Done");
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.SettingsUpgradeDone = true;
            }

            ApplyRosterClaritySettingsMigration();
            ApplyRosterLegibilitySettingsMigration();

            //solve old version settings migration problem
            try
            {
                LanguageExt.GetLanguageByName(Properties.Settings.Default.Language);
            }
            catch
            {
                Properties.Settings.Default.Language = LanguageExt.GetNameByLanguage(Models.Language.AUTO);
            }

            try
            {
                LanguageExt.GetLanguageByName(Properties.Settings.Default.ShipNameLanguage);
            }
            catch
            {
                Properties.Settings.Default.ShipNameLanguage = LanguageExt.GetNameByLanguage(Models.Language.AUTO);
            }

            try
            {
                ServerExt.GetServerByName(Properties.Settings.Default.Server);
            }
            catch
            {
                Properties.Settings.Default.Server = ServerExt.GetNameByServer(Server.AUTO);
            }

            try
            {
                ServerExt.GetServerByName(Properties.Settings.Default.SecondaryServer);
            }
            catch
            {
                Properties.Settings.Default.SecondaryServer = ServerExt.GetNameByServer(Server.RU);
            }

            Properties.Settings.Default.Save();

            ((App)Application.Current).WindowPlace.Register(this);

            NotificationMessageUtils.InitializeNotificationMessageDataGrid(DataGridNotificationMessages);
            NetworkUtils.InitializeHttpClient();

            ShipInfoUtils.ReadShipInfoFile(@".\Resources\Json\ships.json");
            LogUtils.WriteInfo($"ShipInfoFileVersion: {ShipInfoUtils.GetShipInfoVersion()}");
            LogUtils.WriteInfo($"ShipInfoFileDate: {ShipInfoUtils.GetShipInfoDate()}");

            PRUtils.LoadExpectedValues(@".\Resources\Json\expected_values.json");
            _ = InitializeHistoryAsync();

            SoftwareUpdateUtils.CleanOldVersionFiles();

            if (Properties.Settings.Default.CheckForUpdatesOnStartup)
            {
                _ = CheckForStartupUpdates();
            }

            string chartFontFamily = ChartFontUtils.Resolve(WinrateChart.FontFamily);
            WinrateChart.TooltipTextPaint = new SolidColorPaint { Color = SKColors.Black, FontFamily = chartFontFamily };
            WinrateChart.XAxes = new Axis[] { new Axis { IsVisible = false } };
            WinrateChart.YAxes = new Axis[] { new Axis { Labeler = d => { return d.ToString("p1"); }, LabelsPaint = new SolidColorPaint { Color = SKColors.Black, FontFamily = chartFontFamily }, CrosshairPaint = new SolidColorPaint(SKColors.Gray) } };
            KDEChart.XAxes = new Axis[] { new Axis { Labeler = d => { return d.ToString("p1"); }, LabelsPaint = new SolidColorPaint { Color = SKColors.Black, FontFamily = chartFontFamily }, SeparatorsPaint = new SolidColorPaint(SKColors.LightGray), CrosshairPaint = new SolidColorPaint(SKColors.Gray) } };
            KDEChart.YAxes = new Axis[] { new Axis { IsVisible = false } };

            SwitchLanguage(LanguageExt.GetLanguageByName(Properties.Settings.Default.Language));
            RefreshDataGridColumns(Properties.Settings.Default.EnemiesDisplayMirrored);

            DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += new EventHandler(Timer_Tick);
            timer.Start();
            LogUtils.WriteInfo("Timer Start");
        }

        private static void ApplyRosterClaritySettingsMigration()
        {
            if (Properties.Settings.Default.RosterClarityMigrationDone) return;

            bool oldFieldsStillAtDefault = Properties.Settings.Default.AccountWinrateVisibility == 0 &&
                Properties.Settings.Default.WeightedWinrateVisibility == 0 &&
                Properties.Settings.Default.ShipWinrateVisibility == 0 &&
                Properties.Settings.Default.AccountAvgExpVisibility == 0 &&
                Properties.Settings.Default.ShipAvgExpVisibility == 0 &&
                Properties.Settings.Default.ShipAvgDmgVisibility == 0 &&
                Properties.Settings.Default.PRVisibility == 0;

            Properties.Settings.Default.ShowAccountRosterColumn = true;
            Properties.Settings.Default.ShowShipRosterColumn = true;
            Properties.Settings.Default.ShowTierPerformanceStats = false;
            Properties.Settings.Default.ShowPerformanceRosterColumn = true;
            if (oldFieldsStillAtDefault)
            {
                Properties.Settings.Default.WeightedWinrateVisibility = 2;
                Properties.Settings.Default.AccountAvgExpVisibility = 2;
                Properties.Settings.Default.ShipAvgExpVisibility = 2;
            }
            Properties.Settings.Default.RosterClarityMigrationDone = true;
            Properties.Settings.Default.Save();
        }

        internal static void ApplyRosterLegibilitySettingsMigration(bool persist = true)
        {
            if (Properties.Settings.Default.RosterLegibilityMigrationDone) return;

            Properties.Settings.Default.AccountAvgExpVisibility = 2;
            Properties.Settings.Default.ShipAvgExpVisibility = 2;
            Properties.Settings.Default.WeightedWinrateVisibility = 0;
            Properties.Settings.Default.AnalysisPanelExpanded = false;
            Properties.Settings.Default.RosterLegibilityMigrationDone = true;
            if (persist) Properties.Settings.Default.Save();
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClosePlayerDetail();
            UpdateResponsiveLayout();
        }

        private void BtnToggleAnalysis_Click(object sender, RoutedEventArgs e)
        {
            analysisDrawerOpen = !analysisDrawerOpen;
            UpdateResponsiveLayout();
            if (analysisDrawerOpen)
            {
                ChartUtils.EnsureLoaded(WinrateChart);
                ChartUtils.EnsureLoaded(KDEChart);
            }
        }

        private void MainWindowGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (NotificationPopup.IsOpen)
            {
                NotificationPopup.IsOpen = false;
                BtnToggleNotifications.Focus();
                e.Handled = true;
            }
            else if (PlayerDetailPopup.IsOpen)
            {
                ClosePlayerDetail();
                e.Handled = true;
            }
            else if (analysisDrawerOpen)
            {
                analysisDrawerOpen = false;
                UpdateResponsiveLayout();
                BtnToggleAnalysis.Focus();
                e.Handled = true;
            }
        }

        private void UpdateResponsiveLayout()
        {
            if (!IsLoaded && ActualWidth <= 0) return;
            if (useDashboardInterface)
            {
                DashboardView.CloseTransientUi();
                return;
            }
            bool useOverflowMenu = ActualWidth < 1420;
            BtnSoftwareUpdate.Visibility = useOverflowMenu ? Visibility.Collapsed : Visibility.Visible;
            BtnConfig.Visibility = useOverflowMenu ? Visibility.Collapsed : Visibility.Visible;
            BtnScreenshot.Visibility = useOverflowMenu ? Visibility.Collapsed : Visibility.Visible;
            BtnMore.Visibility = useOverflowMenu ? Visibility.Visible : Visibility.Collapsed;
            CurrentSessionSummaryCard.Visibility = ActualWidth < 1030 ? Visibility.Collapsed : Visibility.Visible;

            RosterDisplayDensity density = RosterDisplayDensityExtensions.Parse(Properties.Settings.Default.RosterDisplayDensity);
            bool showAccount = Properties.Settings.Default.ShowAccountRosterColumn && accountMetricHeaders.Count > 0;
            bool showWeighted = weightedMetricHeaders.Count > 0;
            bool showShip = Properties.Settings.Default.ShowShipRosterColumn && shipMetricHeaders.Count > 0;
            bool showPersonalRating = personalRatingMetricHeaders.Count > 0;
            bool showTier = Properties.Settings.Default.ShowTierPerformanceStats;
            bool showPerformance = Properties.Settings.Default.ShowPerformanceRosterColumn;
            int playerCount = DataContext is Battlefield battlefield
                ? Math.Max(1, Math.Max(battlefield.Allies.Count, battlefield.Enemies.Count))
                : 12;
            double gridHeight = Math.Min(DataGridAlliesList.ActualHeight, DataGridEnemiesList.ActualHeight);
            if (gridHeight <= 0) gridHeight = Math.Max(0, ActualHeight - 155);

            double contentWidth = MainContentGrid.ActualWidth > 0 ? MainContentGrid.ActualWidth : Math.Max(0, ActualWidth - 20);
            bool analysisVisible = analysisDrawerOpen;
            AnalysisPanel.Visibility = analysisVisible ? Visibility.Visible : Visibility.Collapsed;
            AnalysisHostColumn.Width = new GridLength(0);
            Grid.SetColumn(AnalysisPanel, 0);
            Grid.SetColumnSpan(AnalysisPanel, 2);
            AnalysisPanel.Width = Math.Min(420, Math.Max(300, contentWidth - 16));
            BtnToggleAnalysis.FontWeight = analysisVisible ? FontWeights.SemiBold : FontWeights.Normal;

            double teamGridWidth = Math.Max(0, (contentWidth - 8) / 2);
            RosterFitInput fitInput = new(
                teamGridWidth,
                gridHeight,
                playerCount,
                Properties.Settings.Default.PlayerColumnFontSize,
                Properties.Settings.Default.StatisticsColumnFontSize,
                density,
                showAccount,
                showWeighted,
                showShip,
                showPersonalRating,
                showTier,
                showPerformance,
                accountMetricHeaders.Count,
                weightedMetricHeaders.Count,
                shipMetricHeaders.Count,
                personalRatingMetricHeaders.Count,
                tierMetricHeaders.Count);
            RosterFitMetrics fit = RosterLayoutCalculator.CalculateFit(fitInput);
            ScrollBarVisibility horizontalScroll = fit.Layout.RequiresHorizontalScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            ScrollBarVisibility verticalScroll = fit.Layout.RequiresVerticalScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            DataGridAlliesList.HorizontalScrollBarVisibility = DataGridEnemiesList.HorizontalScrollBarVisibility = horizontalScroll;
            DataGridAlliesList.VerticalScrollBarVisibility = DataGridEnemiesList.VerticalScrollBarVisibility = verticalScroll;
            ApplyRosterColumnWidths(DataGridAlliesList, fit.Columns);
            ApplyRosterColumnWidths(DataGridEnemiesList, fit.Columns);
            DataGridAlliesList.RowHeight = DataGridEnemiesList.RowHeight = fit.Layout.RowHeight;
            EffectiveColumnHeaderHeight = fit.Layout.ColumnHeaderHeight;
            EffectivePlayerFontSize = fit.Layout.PlayerFontSize;
            EffectiveStatisticsFontSize = fit.Layout.StatisticsFontSize;
            IsCompactRoster = density == RosterDisplayDensity.Compact;
            UseCompactStatusBadges = fit.Layout.UseCompactStatusBadges;
            detailPopup.UpdateBounds();
        }

        private static void ApplyRosterColumnWidths(DataGrid dataGrid, RosterColumnWidths widths)
        {
            foreach (DataGridColumn column in dataGrid.Columns)
            {
                double width = column.SortMemberPath switch
                {
                    "Player" => widths.Player,
                    "Account" => widths.Account,
                    "Weighted" => widths.Weighted,
                    "Ship" => widths.Ship,
                    "PersonalRating" => widths.PersonalRating,
                    "Tier" => widths.Tier,
                    "Performance" => widths.Performance,
                    _ => column.ActualWidth
                };
                if (width > 0) column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
            }
        }

        private void RosterRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not DataGridRow row || row.DataContext is not PlayerRosterRowViewModel rosterRow) return;
            detailPopup.RowEntered(rosterRow, row);
        }

        private void RosterRow_MouseLeave(object sender, MouseEventArgs e) => detailPopup.RowLeft();

        private void ClosePlayerDetail() => detailPopup.Close();

        private void PlayerDetailPopup_MouseEnter(object sender, MouseEventArgs e) => detailPopup.PopupEntered();

        private void PlayerDetailPopup_MouseLeave(object sender, MouseEventArgs e) => detailPopup.PopupLeft();

        private void RosterDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange != 0 || e.VerticalChange != 0) ClosePlayerDetail();
        }

        private void BtnClosePlayerDetail_Click(object sender, RoutedEventArgs e) => ClosePlayerDetail();

        private void BtnCopyPlayerDetail_Click(object sender, RoutedEventArgs e)
        {
            if (detailPopup.CurrentRow == null) return;
            Clipboard.SetDataObject(TextUtils.GenerateParticularPlayerStatisticsOutputText(detailPopup.CurrentRow.Player));
            NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessagePlayerDetailCopied") as string);
        }

        private void BtnToggleNotifications_Click(object sender, RoutedEventArgs e)
        {
            notificationsExpanded = !notificationsExpanded;
            NotificationPopup.IsOpen = notificationsExpanded;
            BtnToggleNotifications.Content = notificationsExpanded ? "−" : "＋";
            BtnToggleNotifications.ToolTip = FindResource(notificationsExpanded ? "NotificationCollapse" : "NotificationExpand");
            if (DataGridNotificationHistory.Items.Count > 0)
                DataGridNotificationHistory.ScrollIntoView(DataGridNotificationHistory.Items[^1]);
        }

        private void NotificationPopup_Closed(object? sender, EventArgs e)
        {
            notificationsExpanded = false;
            BtnToggleNotifications.Content = "＋";
            BtnToggleNotifications.ToolTip = FindResource("NotificationExpand");
        }

        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            if (BtnMore.ContextMenu == null) return;
            BtnMore.ContextMenu.PlacementTarget = BtnMore;
            BtnMore.ContextMenu.IsOpen = true;
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            string latestFileName = FileUtils.GetLatestTempArenaInfoFile(true);
            if (latestFileName != "")
            {
                await ReadPlayersListAndGetDataFromServer(latestFileName);
            }
            if (++sessionSummaryTicks >= 30)
            {
                sessionSummaryTicks = 0;
                await RefreshSessionSummaryAsync();
            }
        }

        private async Task ReadPlayersListAndGetDataFromServer(string filename, bool forceRefresh = false, string? forceRefreshPlayerID = null, Server? forceRefreshPlayerServer = null)
        {
            long uiGeneration = Interlocked.Increment(ref rosterUiGeneration);
            LogUtils.WriteInfo("Reading Players List");
            LogUtils.WriteInfo($"gamePath={Properties.Settings.Default.GamePath}");
            LogUtils.WriteInfo($"filename={filename}");
            RosterLoadCommand command = rosterSettings.Capture(filename, forceRefresh, forceRefreshPlayerID, forceRefreshPlayerServer);

            try
            {
                RosterLoadExecution? execution = await rosterLoadUseCase.LoadAsync(
                    command,
                    attemptStarted: () => SetRosterInputEnabled(false),
                    metadataReady: metadata => ApplyRosterMetadata(metadata),
                    statisticsLoading: () => NotificationMessageUtils.CreateMessage(
                        MessageType.INFO,
                        FindResource("NotificationMessageRetrivingData") as string),
                    attemptFailed: ReportRosterLoadFailure,
                    retrying: attempt => NotificationMessageUtils.CreateMessage(
                        MessageType.INFO,
                        $"{FindResource("NotificationMessageRetrying")}{attempt}{FindResource("NotificationMessageAttempt")}"));
                if (execution == null) return;

                List<Player> playerList = execution.Players.ToList();
                currentDashboardMetadata = new(
                    HistoryMapNameLocalizer.GetDisplayName(execution.RawMapName),
                    FormatBattleMode(execution.BattleType),
                    FormatDashboardServer(execution.Server),
                    execution.BattleStartTime,
                    APITypeExt.GetNameByAPIType(execution.Provider),
                    DateTimeOffset.Now);
                Battlefield battlefield = new(execution.BattleType, execution.BattleStartTime, playerList);

                foreach (Player player in playerList)
                {
                    if (!string.IsNullOrEmpty(player.Note))
                        NotificationMessageUtils.CreateMessage(MessageType.INFO, $"{FindResource("NotificationMessageNoteEncountered") as string}{player.Name}{FindResource("NotificationMessageNoteEncounteredMiddle") as string}{player.Note}");
                }

                if (Properties.Settings.Default.YuyukoAPIPushEnabled)
                    ApiUtils.YuyukoApiPushBattlefieldInfo(battlefield);

                ApplyBattlefieldToUI(battlefield, loadCompleted: true);
                statsCache.Save();
                EncounterHistoryUtils.RecordBattle(playerList, execution.BattleId, execution.BattleStartTime);
                if (execution.HistoryCapture != null)
                    _ = CaptureHistoryAsync(execution.HistoryCapture);
                currentBattleFilename = execution.Filename;
                currentBattleID = execution.BattleId;
                currentBattleStartTime = execution.BattleStartTime;

                if (execution.BackgroundRefresh != null)
                {
                    int stalePlayerCount = playerList.Count(player => player.IsDataStale);
                    NotificationMessageUtils.CreateMessage(MessageType.INFO, $"{stalePlayerCount}{FindResource("NotificationMessageDataUsingCache") as string}");
                    RosterStatus.Set(RosterLoadState.Refreshing, FindResource("RosterStatusRefreshing") as string ?? "Refreshing cached data…");
                    _ = ApplyBackgroundRefreshAsync(execution.BackgroundRefresh.Start(), battlefield);
                }
                else
                {
                    SetRosterStatus(execution.RosterResult);
                }

                if (execution.RosterResult.IsFailed)
                {
                    ApiFailureKind failure = execution.RosterResult.Failures.FirstOrDefault();
                    string failureResource = failure switch
                    {
                        ApiFailureKind.RateLimited => "NotificationMessageUpdateRateLimited",
                        ApiFailureKind.Network or ApiFailureKind.Timeout or ApiFailureKind.Server => "NotificationMessageConnectionError",
                        ApiFailureKind.InvalidResponse => "NotificationMessageJsonError",
                        _ => "NotificationMessageOtherError"
                    };
                    NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource(failureResource) as string);
                }
                else
                    NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageDataRetrieved") as string);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                rosterLoadUseCase.Cancel();
                if (command.MaximumRetryAttempts > 0 && ex.Message is not "FileFormatIncorrect" and not "ServerAutoDetectionFailed")
                    NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageMaximumAttemptsReached") as string);
            }
            finally
            {
                if (uiGeneration == Interlocked.Read(ref rosterUiGeneration)) SetRosterInputEnabled(true);
            }
        }

        private void SetRosterInputEnabled(bool enabled)
        {
            BtnRefresh.IsEnabled = enabled;
            BtnOpen.IsEnabled = enabled;
            DashboardView.SetRosterInputEnabled(enabled);
        }

        private void ApplyRosterMetadata(RosterLoadMetadata metadata)
        {
            currentDashboardMetadata = new(
                HistoryMapNameLocalizer.GetDisplayName(metadata.RawMapName),
                FormatBattleMode(metadata.BattleType),
                FormatDashboardServer(metadata.Server),
                metadata.BattleStartTime,
                APITypeExt.GetNameByAPIType(metadata.Provider),
                null);
            Battlefield metadataBattlefield = new(metadata.BattleType, metadata.BattleStartTime, metadata.Players.ToList());
            ApplyBattlefieldToUI(metadataBattlefield, loadCompleted: false);
            RosterStatus.Set(RosterLoadState.Metadata, FindResource("RosterStatusMetadata") as string ?? "Loading player statistics…");
        }

        private void ReportRosterLoadFailure(Exception exception)
        {
            LogUtils.WriteError("", exception);
            _ = exception.Message switch
            {
                "FileFormatIncorrect" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageFileError") as string),
                "ServerAutoDetectionFailed" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageServerAutoDetectionFailed") as string),
                "HttpRequestFailed" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageConnectionError") as string),
                "JsonStringNotValid" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageJsonError") as string),
                _ => NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageOtherError") as string),
            };
            RosterStatus.Set(RosterLoadState.Failed, FindResource("RosterStatusFailed") as string ?? "Player statistics could not be loaded.");
        }

        private void SetRosterStatus(BattleRosterLoadResult result)
        {
            string stateResource = result.IsFailed ? "RosterStatusFailed" : result.IsPartial ? "RosterStatusPartial" : "RosterStatusComplete";
            RosterStatus.Set(result.IsFailed ? RosterLoadState.Failed : result.IsPartial ? RosterLoadState.Partial : RosterLoadState.Complete,
                FindResource(stateResource) as string ?? "Player data loaded.");
        }

        private void ApplyBattlefieldToUI(Battlefield battlefield, bool loadCompleted = true)
        {
            DashboardView.CloseTransientUi();
            this.DataContext = battlefield;
            RefreshRosterRows(battlefield);
            dashboard.Update(battlefield, loadCompleted, currentDashboardMetadata);
            DashboardView.UpdateBattlefield(battlefield);
            UpdateResponsiveLayout();

            TxtOutputText.Text = TextUtils.GenerateGeneralStatisticsOutputText(battlefield);
            if (Properties.Settings.Default.OutputTextAutoCopy && Properties.Settings.Default.OutputTextUnlock)
            {
                Clipboard.SetDataObject(TxtOutputText.Text);
            }

            SwitchSorting(Properties.Settings.Default.PlayerListSortBy);
            SwitchWinrateChartType(Properties.Settings.Default.WinrateChartType);
            KDEChart.Series = ChartUtils.GetKDEChartSeries(battlefield);
        }

        private static string FormatBattleMode(string mode) => mode.ToLowerInvariant() switch
        {
            "pvp" or "random" or "randombattle" => Application.Current?.TryFindResource("DashboardModeRandom") as string ?? "Random battle",
            "ranked" or "rank" => Application.Current?.TryFindResource("DashboardModeRanked") as string ?? "Ranked battle",
            "clan" or "clanbattle" => Application.Current?.TryFindResource("DashboardModeClan") as string ?? "Clan battle",
            "pve" or "coop" or "cooperative" => Application.Current?.TryFindResource("DashboardModeCoop") as string ?? "Co-op battle",
            _ => mode
        };

        private static string FormatDashboardServer(Server server) => server switch
        {
            Server.ASIA => Application.Current?.TryFindResource("DashboardServerAsia") as string ?? "Asia",
            Server.EU => Application.Current?.TryFindResource("DashboardServerEurope") as string ?? "Europe",
            Server.NA => Application.Current?.TryFindResource("DashboardServerNorthAmerica") as string ?? "North America",
            Server.CN => Application.Current?.TryFindResource("DashboardServerChina") as string ?? "China",
            Server.RU => Application.Current?.TryFindResource("DashboardServerRussia") as string ?? "Russia",
            _ => ServerExt.GetNameByServer(server)
        };

        private async Task ApplyBackgroundRefreshAsync(Task<RosterBackgroundRefreshResult?> refreshTask, Battlefield currentBattlefield)
        {
            try
            {
                RosterBackgroundRefreshResult? refresh = await refreshTask;
                if (refresh == null || !ReferenceEquals(DataContext, currentBattlefield)) return;

                switch (refresh.State)
                {
                    case RosterBackgroundRefreshState.Failed:
                        RosterStatus.Set(RosterLoadState.Partial, FindResource("RosterStatusPartial") as string ?? "Some player data is unavailable.");
                        break;
                    case RosterBackgroundRefreshState.Refreshed when refresh.RefreshedPlayers != null && refresh.RosterResult != null:
                        Battlefield battlefield = new(currentBattlefield.BattleType, currentBattlefield.BattleStartTime, refresh.RefreshedPlayers.ToList());
                        ApplyBattlefieldToUI(battlefield);
                        statsCache.Save();
                        RosterStatus.Set(refresh.RosterResult.IsPartial ? RosterLoadState.Partial : RosterLoadState.Complete,
                            FindResource(refresh.RosterResult.IsPartial ? "RosterStatusPartial" : "RosterStatusComplete") as string ?? "Player data loaded.");
                        NotificationMessageUtils.CreateMessage(MessageType.INFO, FindResource("NotificationMessageBackgroundUpdateComplete") as string);
                        break;
                    case RosterBackgroundRefreshState.Error:
                        throw refresh.Error ?? new InvalidOperationException("Background roster refresh failed.");
                }
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("background data refresh failed", ex);
                if (!ReferenceEquals(DataContext, currentBattlefield)) return;
                RosterStatus.Set(RosterLoadState.Partial, FindResource("RosterStatusPartial") as string ?? "Some player data is unavailable.");
                NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageOtherError") as string);
            }
        }

        private void BtnConfig_Click(object sender, RoutedEventArgs e)
        {
            ConfigWindow configWindow = new()
            {
                Owner = this
            };
            configWindow.ShowDialog();
            _ = InitializeHistoryAsync();
            RefreshDataGridColumns(Properties.Settings.Default.EnemiesDisplayMirrored);
            if (DataContext is Battlefield battlefield)
            {
                RefreshRosterRows(battlefield);
                SwitchSorting(Properties.Settings.Default.PlayerListSortBy);
                dashboard.RefreshPresentation();
            }
            DashboardView.RefreshLocalizedSelectors();
        }

        private async void BtnSoftwareUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnSoftwareUpdate.IsEnabled = false;
            try
            {
                SoftwareUpdateCheckResult result = await SoftwareUpdateUtils.CheckForSoftwareUpdates();
                if (result.Status == SoftwareUpdateCheckStatus.UpToDate)
                {
                    System.Windows.MessageBox.Show(FindResource("MsgBoxSoftwareUpdateNotFound") as string, FindResource("MsgBoxUpdate") as string, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                BtnSoftwareUpdate.IsEnabled = true;
            }
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            HistoryWindow window = new(historyServices, initializeOnLoaded: true) { Owner = this };
            if (DataContext is Battlefield battlefield && battlefield.Allies.FirstOrDefault(x => x.Relation == "0") is Player self)
                window.SetAccountContext(ServerExt.GetNameByServer(self.Server), self.ID);
            window.Show();
        }

        private void CurrentSessionSummaryCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => BtnHistory_Click(sender, e);

        private async Task InitializeHistoryAsync()
        {
            try
            {
                await historyServices.InitializeAsync(Properties.Settings.Default.GamePath);
                LogUtils.WriteInfo($"Battle history database: {historyServices.Repository.DatabasePath}");
                await RefreshSessionSummaryAsync();
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Battle history initialization failed.", ex);
                NotificationMessageUtils.CreateMessage(MessageType.ERROR, FindResource("NotificationMessageHistoryUnavailable") as string);
            }
        }

        private async Task CaptureHistoryAsync(RosterHistoryCaptureRequest request)
        {
            try
            {
                await rosterHistoryCaptureService.CaptureAsync(request);
                await RefreshSessionSummaryAsync();
            }
            catch (Exception ex) { LogUtils.WriteError("Unable to save the pre-battle history snapshot.", ex); }
        }

        private async Task RefreshSessionSummaryAsync()
        {
            if (sessionSummaryRefreshing) return;
            sessionSummaryRefreshing = true;
            try
            {
                BattleSession? session = await historyServices.Repository.GetLatestSessionAsync();
                if (session == null)
                {
                    TxtMainSessionBattles.Text = TxtMainSessionWinrate.Text = TxtMainSessionDamage.Text = TxtMainSessionPr.Text = "-";
                    TxtMainSessionResultLabel.Text = FindResource("HistorySessionObservedResults") as string ?? "Observed results";
                    DashboardView.UpdateSessionSummary("—", FindResource("MainSessionOpenHint") as string ?? "Open battle history");
                    return;
                }
                IReadOnlyList<BattleRecord> battles = await historyServices.Repository.GetSessionBattlesAsync(session.Id);
                IReadOnlyDictionary<long, BattleAdvancedMetrics> advanced = await historyServices.Repository.GetAdvancedMetricsAsync(battles.Select(x => x.Id));
                SessionSummary summary = historyServices.SessionAnalysis.CalculateSession(session, battles, advanced);
                int total = summary.Metrics.RecordedBattles;
                int resolved = battles.Where(x => x.WinCount.HasValue).Sum(x => Math.Max(1, x.BattleCount));
                int wins = (int)Math.Round(battles.Where(x => x.WinCount.HasValue).Sum(x => x.WinCount ?? 0), MidpointRounding.AwayFromZero);
                int nonWins = Math.Max(0, resolved - wins);
                int pending = Math.Max(0, total - resolved);
                TxtMainSessionBattles.Text = total.ToString(CultureInfo.CurrentCulture);
                TxtMainSessionResultLabel.Text = total is > 0 and < 5
                    ? FindResource("HistorySessionObservedResults") as string ?? "Observed results"
                    : FindResource("HistoryWinrate") as string ?? "Win rate";
                TxtMainSessionWinrate.Text = total is > 0 and < 5
                    ? string.Format(FindResource("HistorySessionSmallResultFormat") as string ?? "{0} wins · {1} non-wins · {2} pending", wins, nonWins, pending)
                    : summary.Metrics.Winrate?.ToString("P1") ?? "-";
                TxtMainSessionDamage.Text = summary.Metrics.AverageDamage?.ToString("0") ?? "-";
                TxtMainSessionPr.Text = summary.Metrics.AveragePr?.ToString("0") ?? "-";
                string dashboardSessionText = $"{total} {FindResource("MainSessionBattles") ?? "battles"} · {TxtMainSessionWinrate.Text}";
                string dashboardSessionTip = $"{dashboardSessionText}{Environment.NewLine}{FindResource("HistoryAverageDamage") ?? "Average damage"} {TxtMainSessionDamage.Text} · PR {TxtMainSessionPr.Text}";
                DashboardView.UpdateSessionSummary(dashboardSessionText, dashboardSessionTip);
            }
            catch (Exception ex) { LogUtils.WriteError("Unable to refresh the current session summary.", ex); }
            finally { sessionSummaryRefreshing = false; }
        }

        private static async Task CheckForStartupUpdates()
        {
            await SoftwareUpdateUtils.CheckForSoftwareUpdates(installWhenFound: false);
        }

        private void ComboBoxLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.Language = ComboBoxLanguage.SelectedValue.ToString()!;
            Properties.Settings.Default.Save();
            SwitchLanguage(LanguageExt.GetLanguageByName(Properties.Settings.Default.Language));
        }

        private void DashboardLanguageChanged(string value)
        {
            Properties.Settings.Default.Language = value;
            Properties.Settings.Default.Save();
            SwitchLanguage(LanguageExt.GetLanguageByName(value));
        }

        private void DashboardSortModeChanged(int value)
        {
            Properties.Settings.Default.PlayerListSortBy = value;
            Properties.Settings.Default.Save();
            dashboard.SetSortMode(value);
            ComboBoxSortBy.SelectedValue = value.ToString();
            SwitchSorting(value);
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            string latestFileName = FileUtils.GetLatestTempArenaInfoFile(false);
            if (latestFileName != "")
            {
                //manual refresh fetches the current battle again without deleting other cached battles
                await ReadPlayersListAndGetDataFromServer(latestFileName, true);
            }
        }

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.OpenFileDialog dialog = new();
            dialog.Filter = "*.json, *.wowsreplay|*.json;*.wowsreplay";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await ReadPlayersListAndGetDataFromServer(dialog.FileName);
            }
        }

        private void ContextMenuCheckOnWoWSNumbers_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            Process.Start("explorer.exe", $"{ServerExt.GetWoWSNumbersUrlStringByServer(p!.Server)}/player/{p.ID}%2C{p.Name}/");
        }

        private void ContextMenuCheckOnWoWSOfficialSite_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            Process.Start("explorer.exe", $"https://profile.{ServerExt.GetFullUrlStringByServer(p!.Server)}/statistics/{p.ID}/");
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            int gridH = (int)MainWindowGrid.ActualHeight;
            int gridW = (int)MainWindowGrid.ActualWidth;
            RenderTargetBitmap bitmap = new(gridW, gridH, 96, 96, PixelFormats.Default);
            Rect rect = new(0, 0, gridW, gridH);
            DrawingVisual drawingVisual = new();
            using (DrawingContext drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawRectangle(Background, null, rect);
            }
            bitmap.Render(drawingVisual);
            bitmap.Render(MainWindowGrid);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string datetimestr = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_ffff");
            if (!Directory.Exists(@".\Screenshot\"))
            {
                Directory.CreateDirectory(@".\Screenshot\");
            }
            string filePath = $@".\Screenshot\{datetimestr}.png";
            using FileStream fs = new(filePath, FileMode.Create);
            encoder.Save(fs);
            NotificationMessageUtils.CreateMessage(MessageType.INFO, $"{FindResource("NotificationMessageScreenshotIsSavedTo") as string}{filePath}{FindResource("NotificationMessagePeriod") as string}");
        }

        //drag and drop a replay file on the window to open it
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                object? files = e.Data.GetData(DataFormats.FileDrop);
                if (files is not null)
                {
                    if ((files as string[])!.Length == 1)
                    {
                        await ReadPlayersListAndGetDataFromServer((files as string[])![0]);
                    }
                }
            }
        }

        private void ContextMenuCopyPlayerStatistics_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            Clipboard.SetDataObject(TextUtils.GenerateParticularPlayerStatisticsOutputText(p!));
        }

        private async void ContextMenuRefreshPlayer_Click(object sender, RoutedEventArgs e)
        {
            Player? p = (sender as MenuItem)?.DataContext as Player;
            if (p == null || !p.CanRefreshData || currentBattleFilename == "" || !BtnRefresh.IsEnabled)
            {
                return;
            }

            LogUtils.WriteInfo($"Manual player data refresh: Name={p.Name}, ID={p.ID}, Server={ServerExt.GetNameByServer(p.Server)}");
            await ReadPlayersListAndGetDataFromServer(currentBattleFilename, false, p.ID, p.Server);
        }

        private void ContextMenuFixedTeammate_Click(object sender, RoutedEventArgs e)
        {
            Player? p = (sender as MenuItem)?.DataContext as Player;
            if (p == null || !p.CanBeFixedTeammate)
            {
                return;
            }

            EncounterHistoryUtils.ToggleFixedTeammate(p);
            if (DataContext is Battlefield battlefield && currentBattleID != "")
            {
                EncounterHistoryUtils.ApplyRecentEncounterMarkers(battlefield.Allies.Concat(battlefield.Enemies), currentBattleID, currentBattleStartTime);
                RefreshPlayerList();
            }
        }

        private void ContextMenuCustomMarker_Click(object sender, RoutedEventArgs e)
        {
            Player? p = (sender as MenuItem)?.DataContext as Player;
            if (p == null || p.ID == "-1")
            {
                return;
            }

            p.IsCustomMarked = !p.IsCustomMarked;
            WatchListUtils.SaveCustomMarker(p, @".\WatchList.json");
            RefreshPlayerList();
        }

        private void ContextMenuAddToWatchListPositive_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.POSITIVE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
        }

        private void ContextMenuAddToWatchListNegtive_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.NEGTIVE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
        }

        private void ContextMenuAddToWatchListCheater_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.CHEATER;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
        }

        private void ContextMenuRemoveFromWatchList_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.WatchStatus = WatchStatus.NONE;
            WatchListUtils.SaveWatchList(p, @".\WatchList.json");
        }

        private void ContextMenuEditNote_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            NoteEditWindow noteEditWindow = new(p!.Name)
            {
                Owner = this,
                NoteText = p.Note
            };
            if (noteEditWindow.ShowDialog() == true)
            {
                p.Note = noteEditWindow.NoteText;
                WatchListUtils.SaveWatchListNote(p, @".\WatchList.json");
                RefreshPlayerList();
            }
        }

        private void ContextMenuClearNote_Click(object sender, RoutedEventArgs e)
        {
            MenuItem? menu = sender as MenuItem;
            Player? p = menu!.DataContext as Player;
            p!.Note = "";
            WatchListUtils.SaveWatchListNote(p, @".\WatchList.json");
            RefreshPlayerList();
        }

        private void RefreshPlayerList()
        {
            if (DataContext is not Battlefield battlefield) return;
            RefreshRosterRows(battlefield);
            SwitchSorting(Properties.Settings.Default.PlayerListSortBy);
            dashboard.RefreshPresentation();
        }

        private void DashboardPlayerActionRequested(object? sender, DashboardPlayerActionEventArgs e)
        {
            MenuItem proxy = new() { DataContext = e.Player };
            switch (e.Action)
            {
                case DashboardPlayerAction.Refresh: ContextMenuRefreshPlayer_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.Copy: ContextMenuCopyPlayerStatistics_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.Official: ContextMenuCheckOnWoWSOfficialSite_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.Numbers: ContextMenuCheckOnWoWSNumbers_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.CustomMarker: ContextMenuCustomMarker_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.FixedTeammate: ContextMenuFixedTeammate_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.EditNote: ContextMenuEditNote_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.ClearNote: ContextMenuClearNote_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.WatchPositive: ContextMenuAddToWatchListPositive_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.WatchNegative: ContextMenuAddToWatchListNegtive_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.WatchCheater: ContextMenuAddToWatchListCheater_Click(proxy, new RoutedEventArgs()); break;
                case DashboardPlayerAction.WatchRemove: ContextMenuRemoveFromWatchList_Click(proxy, new RoutedEventArgs()); break;
            }
        }

        private void RefreshRosterRows(Battlefield battlefield)
        {
            ClosePlayerDetail();
            RosterPresentationOptions options = RosterPresentationOptions.FromCurrentSettings();
            IReadOnlyList<PlayerRosterRowViewModel> allies = rosterPresentationService.CreateRows(battlefield.Allies, options);
            IReadOnlyList<PlayerRosterRowViewModel> enemies = rosterPresentationService.CreateRows(battlefield.Enemies, options);
            ReplaceRows(alliesRosterRows, allies);
            ReplaceRows(enemiesRosterRows, enemies);
        }

        private static void ReplaceRows(ObservableCollection<PlayerRosterRowViewModel> target, IReadOnlyList<PlayerRosterRowViewModel> source)
        {
            target.Clear();
            foreach (PlayerRosterRowViewModel row in source)
                target.Add(row);
        }

        private void ComboBoxChartType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.WinrateChartType = Convert.ToInt32(ComboBoxChartType.SelectedValue);
            Properties.Settings.Default.Save();
            SwitchWinrateChartType(Properties.Settings.Default.WinrateChartType);
        }

        private void ComboBoxMirrored_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.EnemiesDisplayMirrored = Convert.ToBoolean(ComboBoxMirrored.SelectedValue);
            Properties.Settings.Default.Save();
            RefreshDataGridColumns(Properties.Settings.Default.EnemiesDisplayMirrored);
        }

        private void ComboBoxSortBy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.PlayerListSortBy = Convert.ToInt32(ComboBoxSortBy.SelectedValue);
            Properties.Settings.Default.Save();
            SwitchSorting(Properties.Settings.Default.PlayerListSortBy);
        }

        private void ComboBoxPlayerNamesVisibility_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Properties.Settings.Default.PlayerNamesVisibility = Convert.ToBoolean(ComboBoxPlayerNamesVisibility.SelectedValue);
            Properties.Settings.Default.Save();

            RefreshPlayerList();
        }

        private void HyperLinkApeRadarWebsite_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start("explorer.exe", e.Uri.AbsoluteUri);
        }

        protected override void OnClosed(EventArgs e)
        {
            detailPopup.Close();
            rosterLoadUseCase.Dispose();
            if (ownsHistoryServices) historyServices.DisposeAsync().AsTask().GetAwaiter().GetResult();
            base.OnClosed(e);
        }
    }
}
