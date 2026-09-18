using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using ApeRadar.History;
using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

public sealed class HistoryWindowSmokeTests
{
    [Fact]
    public void HistoryWindow_ConstructsWithApplicationResources()
    {
        Exception? error = null;
        string progress = "starting";
        Thread thread = new(() =>
        {
            try
            {
                Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string language in new[] { "en-us", "zh-cn" })
                {
                    progress = $"{language}: resources";
                    app.Resources.MergedDictionaries.Clear();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/ApeRadar;component/Resources/Localization/{language}.xaml", UriKind.Relative)
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/ApeRadar;component/Resources/Styles/ModernLight.xaml", UriKind.Relative)
                    });
                    ValidateShipTypePresentation(language);
                    progress = $"{language}: history window";
                    bool previousShipTypeIconSetting = ApeRadar.Properties.Settings.Default.ShowShipTypeIcon;
                    ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
                    HistoryWindow window = new(initializeOnLoaded: false);
                    HistoryViewModel viewModel = Assert.IsType<HistoryViewModel>(window.DataContext);
                    viewModel.ApplyCurrentSession(CreateOneBattleSession());
                    Assert.Equal(HistorySampleTier.VerySmall, HistoryViewModel.ClassifySample(1));
                    Assert.DoesNotContain("100", viewModel.CurrentSessionWinrateText, StringComparison.OrdinalIgnoreCase);
                    window.Show();
                    foreach ((double width, double height) in new[] { (860d, 620d), (1000d, 700d), (1180d, 760d) })
                    {
                        window.Width = width;
                        window.Height = height;
                        for (int tab = 0; tab < window.HistoryTabs.Items.Count; tab++)
                        {
                            window.HistoryTabs.SelectedIndex = tab;
                            window.UpdateLayout();
                            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                            AssertChildrenDoNotOverlap(window, Assert.IsAssignableFrom<Panel>(window.FindName("HistoryFilters")));
                            AssertChildrenDoNotOverlap(window, Assert.IsAssignableFrom<Panel>(window.FindName("HistoryFooterButtons")));
                            AssertChildrenDoNotOverlap(window, Assert.IsAssignableFrom<Panel>(window.FindName(tab == 0 ? "CurrentSessionCards" : "TrendSummaryCards")));
                            SaveSnapshotIfRequested(window, language, width, height, tab);
                        }
                    }
                    window.Close();
                    ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = previousShipTypeIconSetting;
                    progress = $"{language}: main window";
                    ValidateMainWindowLayout(language);
                    progress = $"{language}: config window";
                    ValidateConfigWindowLayout(language);
                }
                app.Shutdown();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), $"The UI smoke test did not complete in time; last stage: {progress}.");
        Assert.Null(error);
    }

    [Fact]
    public void SmallSamplePolicy_DoesNotPresentSparseDataAsATrend()
    {
        Assert.Equal(HistorySampleTier.None, HistoryViewModel.ClassifySample(0));
        Assert.Equal(HistorySampleTier.VerySmall, HistoryViewModel.ClassifySample(1));
        Assert.Equal(HistorySampleTier.VerySmall, HistoryViewModel.ClassifySample(4));
        Assert.Equal(HistorySampleTier.Short, HistoryViewModel.ClassifySample(5));
        Assert.Equal(HistorySampleTier.Established, HistoryViewModel.ClassifySample(20));
        Assert.False(HistoryViewModel.ShouldRenderTrend(2));
        Assert.True(HistoryViewModel.ShouldRenderTrend(3));
    }

    private static void AssertChildrenDoNotOverlap(Window window, Panel panel)
    {
        FrameworkElement[] children = panel.Children.OfType<FrameworkElement>().Where(x => x.IsVisible).ToArray();
        for (int i = 0; i < children.Length; i++)
        {
            Rect first = children[i].TransformToAncestor(window).TransformBounds(new Rect(children[i].RenderSize));
            for (int j = i + 1; j < children.Length; j++)
            {
                Rect second = children[j].TransformToAncestor(window).TransformBounds(new Rect(children[j].RenderSize));
                Rect overlap = Rect.Intersect(first, second);
                Assert.False(overlap.Width > 0.5 && overlap.Height > 0.5,
                    $"{children[i].GetType().Name} overlaps {children[j].GetType().Name} at {window.Width}x{window.Height}.");
            }
        }
    }

    private static void ValidateMainWindowLayout(string language)
    {
        bool previousTierPerformanceSetting = ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats;
        bool previousShipTypeIconSetting = ApeRadar.Properties.Settings.Default.ShowShipTypeIcon;
        string previousDensity = ApeRadar.Properties.Settings.Default.RosterDisplayDensity;
        bool previousLegacyTag = ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag;
        bool previousAccountColumn = ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn;
        bool previousShipColumn = ApeRadar.Properties.Settings.Default.ShowShipRosterColumn;
        bool previousPerformanceColumn = ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn;
        string previousPerformanceMetric = ApeRadar.Properties.Settings.Default.RosterPerformanceMetric;
        bool previousAnalysisExpanded = ApeRadar.Properties.Settings.Default.AnalysisPanelExpanded;
        int previousAccountWinrate = ApeRadar.Properties.Settings.Default.AccountWinrateVisibility;
        int previousAccountAvgExp = ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility;
        int previousWeightedWinrate = ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility;
        int previousShipWinrate = ApeRadar.Properties.Settings.Default.ShipWinrateVisibility;
        int previousShipAvgDamage = ApeRadar.Properties.Settings.Default.ShipAvgDmgVisibility;
        int previousShipAvgExp = ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility;
        int previousPr = ApeRadar.Properties.Settings.Default.PRVisibility;
        ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats = false;
        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
        ApeRadar.Properties.Settings.Default.RosterDisplayDensity = "Standard";
        ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = false;
        ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn = true;
        ApeRadar.Properties.Settings.Default.ShowShipRosterColumn = true;
        ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn = true;
        ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = "PR";
        ApeRadar.Properties.Settings.Default.AnalysisPanelExpanded = true;
        ApeRadar.Properties.Settings.Default.AccountWinrateVisibility = 0;
        ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility = 2;
        ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility = 2;
        ApeRadar.Properties.Settings.Default.ShipWinrateVisibility = 0;
        ApeRadar.Properties.Settings.Default.ShipAvgDmgVisibility = 0;
        ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility = 2;
        ApeRadar.Properties.Settings.Default.PRVisibility = 0;
        MainWindow window = new(initializeRuntime: false)
        {
            WindowState = WindowState.Normal,
            ShowInTaskbar = false
        };
        window.RefreshDataGridColumns(mirrored: false);
        RosterPresentationService presentation = new();
        RosterPresentationOptions options = RosterPresentationOptions.FromCurrentSettings();
        window.DataGridAlliesList.ItemsSource = presentation.CreateRows(Enumerable.Range(1, 12).Select(i => CreateTierPerformancePlayer($"Allied sample {i}", i == 1 ? "0" : "1")), options);
        window.DataGridEnemiesList.ItemsSource = presentation.CreateRows(Enumerable.Range(1, 12).Select(i => CreateTierPerformancePlayer($"Enemy sample {i}", "2")), options);
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        AssertShipTypeIconsRefreshWithoutReload(window, language);
        foreach ((double width, double height) in new[] { (800d, 500d), (1180d, 760d), (1280d, 720d), (1366d, 768d), (1600d, 900d), (1920d, 1040d), (800d, 500d) })
        {
            window.Width = width;
            window.Height = height;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            double renderedWidth = window.ActualWidth;

            FrameworkElement messages = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("DataGridNotificationMessages"));
            FrameworkElement buttons = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("MainFooterButtons"));
            FrameworkElement summary = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("CurrentSessionSummaryCard"));
            AssertElementsDoNotOverlap(window, messages, buttons, width, height);
            AssertElementsDoNotOverlap(window, messages, summary, width, height);
            FrameworkElement analysis = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("AnalysisPanel"));
            Assert.Equal(renderedWidth >= 1900 ? Visibility.Visible : Visibility.Collapsed, analysis.Visibility);
            Assert.Equal(4, window.DataGridAlliesList.Columns.Count);
            Assert.Equal(4, window.DataGridEnemiesList.Columns.Count);
            Assert.False(window.DataGridAlliesList.CanUserResizeColumns);
            Assert.False(window.DataGridEnemiesList.CanUserResizeColumns);
            Assert.All(window.DataGridAlliesList.Columns, column => Assert.False(column.CanUserResize));
            Assert.All(window.DataGridEnemiesList.Columns, column => Assert.False(column.CanUserResize));
            Assert.Equal(renderedWidth < 1030 ? Visibility.Collapsed : Visibility.Visible, summary.Visibility);
            Assert.Equal(window.DataGridAlliesList.Columns[1].ActualWidth, window.DataGridEnemiesList.Columns[1].ActualWidth, 1);
            AssertDataGridCellContentsStayInside(window.DataGridAlliesList, width, height);
            AssertDataGridCellContentsStayInside(window.DataGridEnemiesList, width, height);

            if (renderedWidth >= 1900 && height >= 1000)
            {
                ScrollViewer alliesScroll = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(window.DataGridAlliesList));
                ScrollViewer enemiesScroll = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(window.DataGridEnemiesList));
                Assert.Equal(Visibility.Collapsed, alliesScroll.ComputedVerticalScrollBarVisibility);
                Assert.Equal(Visibility.Collapsed, enemiesScroll.ComputedVerticalScrollBarVisibility);
                Assert.Equal(Visibility.Collapsed, alliesScroll.ComputedHorizontalScrollBarVisibility);
                Assert.Equal(Visibility.Collapsed, enemiesScroll.ComputedHorizontalScrollBarVisibility);
                Assert.InRange(window.DataGridAlliesList.RowHeight, 54 * RosterLayoutCalculator.MinimumScale, 68);

                double alliesWidthBeforeCollapse = window.DataGridAlliesList.ActualWidth;
                window.BtnToggleAnalysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, analysis.Visibility);
                Assert.True(window.DataGridAlliesList.ActualWidth > alliesWidthBeforeCollapse);
                window.BtnToggleAnalysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, analysis.Visibility);
            }

            if (Math.Abs(width - 1280) < 0.1)
            {
                double alliesWidthBeforeDrawer = window.DataGridAlliesList.ActualWidth;
                window.BtnToggleAnalysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, analysis.Visibility);
                Assert.Equal(alliesWidthBeforeDrawer, window.DataGridAlliesList.ActualWidth, 1);
                window.BtnToggleAnalysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, analysis.Visibility);
            }

            if (Math.Abs(width - 800) < 0.1 || Math.Abs(width - 1280) < 0.1 || Math.Abs(width - 1366) < 0.1 || Math.Abs(width - 1600) < 0.1 || Math.Abs(width - 1920) < 0.1)
            {
                SaveWindowSnapshot(window, $"main-{language}-{width:0}x{height:0}.png");
            }
        }
        DataGridRow firstRow = Assert.IsType<DataGridRow>(window.DataGridAlliesList.ItemContainerGenerator.ContainerFromIndex(0));
        firstRow.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent,
            Source = firstRow
        });
        PumpDispatcher(TimeSpan.FromMilliseconds(400));
        Assert.True(window.PlayerDetailPopup.IsOpen);
        PlayerDetailCardViewModel detail = Assert.IsType<PlayerDetailCardViewModel>(window.PlayerDetailCardContent.DataContext);
        Assert.Equal("Allied sample 1", detail.Player.Name);
        Assert.InRange(window.PlayerDetailCardBorder.Width, 360, 560);
        firstRow.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent,
            Source = firstRow
        });
        window.PlayerDetailPopup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent,
            Source = window.PlayerDetailPopup
        });
        PumpDispatcher(TimeSpan.FromMilliseconds(300));
        Assert.True(window.PlayerDetailPopup.IsOpen);
        System.Drawing.Rectangle virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        System.Windows.Forms.Cursor.Position = new System.Drawing.Point(virtualScreen.Right - 2, virtualScreen.Bottom - 2);
        PumpDispatcher(TimeSpan.FromMilliseconds(50));
        window.PlayerDetailPopup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent,
            Source = window.PlayerDetailPopup
        });
        PumpDispatcher(TimeSpan.FromMilliseconds(300));
        Assert.False(window.PlayerDetailPopup.IsOpen);
        firstRow.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent,
            Source = firstRow
        });
        PumpDispatcher(TimeSpan.FromMilliseconds(400));
        Assert.True(window.PlayerDetailPopup.IsOpen);
        PumpDispatcher(TimeSpan.FromMilliseconds(250));
        Assert.False(window.PlayerDetailPopup.IsOpen);
        window.Close();
        ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats = previousTierPerformanceSetting;
        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = previousShipTypeIconSetting;
        ApeRadar.Properties.Settings.Default.RosterDisplayDensity = previousDensity;
        ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = previousLegacyTag;
        ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn = previousAccountColumn;
        ApeRadar.Properties.Settings.Default.ShowShipRosterColumn = previousShipColumn;
        ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn = previousPerformanceColumn;
        ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = previousPerformanceMetric;
        ApeRadar.Properties.Settings.Default.AnalysisPanelExpanded = previousAnalysisExpanded;
        ApeRadar.Properties.Settings.Default.AccountWinrateVisibility = previousAccountWinrate;
        ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility = previousAccountAvgExp;
        ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility = previousWeightedWinrate;
        ApeRadar.Properties.Settings.Default.ShipWinrateVisibility = previousShipWinrate;
        ApeRadar.Properties.Settings.Default.ShipAvgDmgVisibility = previousShipAvgDamage;
        ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility = previousShipAvgExp;
        ApeRadar.Properties.Settings.Default.PRVisibility = previousPr;
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        System.Windows.Threading.DispatcherFrame frame = new();
        System.Windows.Threading.DispatcherTimer timer = new(
            System.Windows.Threading.DispatcherPriority.Background,
            System.Windows.Threading.Dispatcher.CurrentDispatcher)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void AssertShipTypeIconsRefreshWithoutReload(MainWindow window, string language)
    {
        string expectedTooltip = language == "zh-cn" ? "巡洋舰" : "Cruiser";
        ShipTypeIconBadge[] icons = FindVisualChildren<ShipTypeIconBadge>(window)
            .Where(badge => Equals(badge.ToolTip, expectedTooltip))
            .ToArray();
        Assert.NotEmpty(icons);
        Assert.All(icons, badge =>
        {
            Assert.Equal(Visibility.Visible, badge.Visibility);
            Assert.Equal(20, badge.Width);
            SolidColorBrush background = Assert.IsType<SolidColorBrush>(badge.Background);
            Assert.Equal(0, background.Color.A);
            Assert.Equal(new Thickness(0), badge.BorderThickness);
            Assert.NotNull(badge.IconSource);

            PlayerRosterRowViewModel row = Assert.IsType<PlayerRosterRowViewModel>(badge.DataContext);
            SolidColorBrush tint = Assert.IsType<SolidColorBrush>(badge.TintBrush);
            if (row.Player.Relation is "0" or "1")
                Assert.True(tint.Color.G > tint.Color.R && tint.Color.G > tint.Color.B);
            else
                Assert.True(tint.Color.R > tint.Color.G && tint.Color.R > tint.Color.B);

            StackPanel shipLine = Assert.IsType<StackPanel>(VisualTreeHelper.GetParent(badge));
            Assert.Equal(1, shipLine.Children.IndexOf(badge));
            TextBlock shipName = Assert.IsType<TextBlock>(shipLine.Children[0]);
            Rect shipNameBounds = shipName.TransformToAncestor(shipLine).TransformBounds(new Rect(shipName.RenderSize));
            Rect iconBounds = badge.TransformToAncestor(shipLine).TransformBounds(new Rect(badge.RenderSize));
            Assert.InRange(iconBounds.Left - shipNameBounds.Right, 4, 6);
        });

        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = false;
        ShipTypePresentation.RefreshOpenWindows();
        Assert.All(icons, badge =>
        {
            Assert.Equal(Visibility.Collapsed, badge.Visibility);
            Assert.Null(badge.IconSource);
        });

        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
        ShipTypePresentation.RefreshOpenWindows();
        Assert.All(icons, badge =>
        {
            Assert.Equal(Visibility.Visible, badge.Visibility);
            Assert.NotNull(badge.IconSource);
        });
    }

    private static Player CreateTierPerformancePlayer(string name, string relation)
    {
        return new Player(name, relation, Server.ASIA, WatchStatus.NONE)
        {
            Relation = relation,
            ShipName = "Sample ship",
            ShipType = "Cruiser",
            ShipTier = 11,
            Battles = 5_000,
            AccountWinrate = 0.60,
            AvgExpPerBattle = 2_500,
            WeightedWinrate = 0.55,
            ShipBattles = 13_850,
            ShipWinrate = 0.5619,
            ShipAvgDmgPerBattle = 136_869,
            ShipAvgExpPerBattle = 2_500,
            PR = 1_700,
            ShipPR = 1_250,
            TierBattles = 13_850,
            TierWinrate = 0.5833,
            TierPR = 1_480,
            IsTierSampleSmall = true,
            TierReferenceMin = 10,
            TierReferenceMax = 11,
            TierReferenceBattles = 420,
            TierReferenceWinrate = 0.535,
            TierReferencePR = 1_320,
            HasTierReference = true,
            MostPlayedTier = 5,
            MostPlayedTierBattles = 4_000,
            MostPlayedTierShare = 0.80,
            IsLowTierBiased = true,
        };
    }

    private static void AssertDataGridCellContentsStayInside(DataGrid dataGrid, double width, double height)
    {
        foreach (DataGridCell cell in FindVisualChildren<DataGridCell>(dataGrid).Where(x => x.IsVisible && x.ActualWidth > 0))
        {
            foreach (FrameworkElement content in FindVisualChildren<FrameworkElement>(cell)
                         .Where(x => x is TextBlock or Image && x.IsVisible && x.ActualWidth > 0))
            {
                Rect bounds = content.TransformToAncestor(cell).TransformBounds(new Rect(content.RenderSize));
                Assert.True(bounds.Left >= -1 && bounds.Right <= cell.ActualWidth + 1,
                    $"{content.GetType().Name} '{(content as TextBlock)?.Text}' escapes roster column {cell.Column?.DisplayIndex} " +
                    $"at {width}x{height}: {bounds} outside width {cell.ActualWidth:0.##}.");
            }
        }
    }

    private static void ValidateShipTypePresentation(string language)
    {
        bool previous = ApeRadar.Properties.Settings.Default.ShowShipTypeIcon;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = false;
            Assert.False(ShipTypePresentation.ShouldShow("Cruiser"));
            Assert.Null(ShipTypePresentation.GetIcon("Unknown"));
            foreach (string shipType in new[] { "AirCarrier", "Battleship", "Cruiser", "Destroyer", "Submarine" })
            {
                ImageSource icon = Assert.IsAssignableFrom<ImageSource>(ShipTypePresentation.GetIcon(shipType));
                Assert.True(icon.IsFrozen);
                Assert.Same(icon, ShipTypePresentation.GetIcon(shipType));
            }

            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
            Assert.True(ShipTypePresentation.ShouldShow("Cruiser"));
            Assert.Equal(language == "zh-cn" ? "巡洋舰" : "Cruiser", ShipTypePresentation.GetDisplayName("Cruiser"));

            ShipRelationBrushConverter converter = new();
            SolidColorBrush ally = Assert.IsType<SolidColorBrush>(converter.Convert("1", typeof(Brush), null!, CultureInfo.InvariantCulture));
            SolidColorBrush enemy = Assert.IsType<SolidColorBrush>(converter.Convert("2", typeof(Brush), null!, CultureInfo.InvariantCulture));
            Assert.True(ally.Color.G > ally.Color.R);
            Assert.True(enemy.Color.R > enemy.Color.G);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = previous;
        }
    }

    private static void ValidateConfigWindowLayout(string language)
    {
        ConfigWindow window = new(initializeRuntime: false)
        {
            WindowState = WindowState.Normal,
            ShowInTaskbar = false
        };
        window.ConfigTabs.SelectedIndex = window.ConfigTabs.Items.Count - 1;
        window.ComboBoxSoftwareUpdateChannel.SelectedValue = SoftwareReleaseSelector.StableSettingValue;
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        Assert.Equal(2, window.ComboBoxSoftwareUpdateChannel.Items.Count);
        Assert.Equal(3, Grid.GetRow(window.WgApplicationIdLabelPanel));
        Assert.Equal(1, Grid.GetRow(Assert.IsType<Grid>(window.ComboBoxSoftwareUpdateChannel.Parent)));
        Assert.Equal(3, Grid.GetColumn(Assert.IsType<Grid>(window.ComboBoxSoftwareUpdateChannel.Parent)));
        Assert.Equal(200, window.ComboBoxSoftwareUpdateChannel.Width);
        CheckBox tierPerformanceCheckBox = Assert.IsType<CheckBox>(window.FindName("ChkBoxShowTierPerformanceStats"));
        WrapPanel tierPerformancePanel = Assert.IsType<WrapPanel>(tierPerformanceCheckBox.Parent);
        StackPanel rosterOptions = Assert.IsType<StackPanel>(tierPerformancePanel.Parent);
        Grid tierPerformanceCell = Assert.IsType<Grid>(rosterOptions.Parent);
        Assert.Equal(8, Grid.GetRow(tierPerformanceCell));
        window.ConfigTabs.SelectedIndex = 5;
        window.UpdateLayout();
        Assert.False(window.BtnDefault.IsEnabled);
        window.ConfigTabs.SelectedIndex = 1;
        bool persistedIconSetting = ApeRadar.Properties.Settings.Default.ShowShipTypeIcon;
        string persistedDensity = ApeRadar.Properties.Settings.Default.RosterDisplayDensity;
        bool persistedLegacyTag = ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag;
        string persistedPerformanceMetric = ApeRadar.Properties.Settings.Default.RosterPerformanceMetric;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = false;
            ApeRadar.Properties.Settings.Default.RosterDisplayDensity = "Comfortable";
            ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = true;
            ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = "Winrate";
            window.ChkBoxShowShipTypeIcon.IsChecked = false;
            window.ComboBoxRosterDisplayDensity.SelectedValue = "Comfortable";
            window.ChkBoxShowLegacyPerformanceTag.IsChecked = true;
            window.ComboBoxRosterPerformanceMetric.SelectedValue = "Winrate";
            window.ApplyDefaultsForSelectedPage();
            Assert.True(window.ChkBoxShowShipTypeIcon.IsChecked);
            Assert.Equal("Standard", window.ComboBoxRosterDisplayDensity.SelectedValue);
            Assert.False(window.ChkBoxShowLegacyPerformanceTag.IsChecked);
            Assert.Equal("PR", window.ComboBoxRosterPerformanceMetric.SelectedValue);
            Assert.False(ApeRadar.Properties.Settings.Default.ShowShipTypeIcon);
            Assert.Equal("Comfortable", ApeRadar.Properties.Settings.Default.RosterDisplayDensity);
            Assert.True(ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag);
            Assert.Equal("Winrate", ApeRadar.Properties.Settings.Default.RosterPerformanceMetric);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = persistedIconSetting;
            ApeRadar.Properties.Settings.Default.RosterDisplayDensity = persistedDensity;
            ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = persistedLegacyTag;
            ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = persistedPerformanceMetric;
        }
        window.ConfigTabs.SelectedIndex = window.ConfigTabs.Items.Count - 1;
        foreach ((double width, double height) in new[] { (600d, 360d), (900d, 560d), (1100d, 700d) })
        {
            window.Width = width;
            window.Height = height;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            AssertChildrenDoNotOverlap(window, window.AdvancedOptionsGrid);
            Rect saveButtonBounds = window.BtnSave.TransformToAncestor(window).TransformBounds(new Rect(window.BtnSave.RenderSize));
            Assert.InRange(saveButtonBounds.Bottom, 0, window.ActualHeight + 0.5);
            if (Math.Abs(width - 1100) < 0.1)
            {
                SaveWindowSnapshot(window, $"config-advanced-{language}-{width:0}x{height:0}.png");
            }
        }
        window.Close();
    }

    private static void AssertElementsDoNotOverlap(Window window, FrameworkElement firstElement, FrameworkElement secondElement, double width, double height)
    {
        Rect first = firstElement.TransformToAncestor(window).TransformBounds(new Rect(firstElement.RenderSize));
        Rect second = secondElement.TransformToAncestor(window).TransformBounds(new Rect(secondElement.RenderSize));
        Rect overlap = Rect.Intersect(first, second);
        Assert.False(overlap.Width > 0.5 && overlap.Height > 0.5,
            $"{firstElement.Name} overlaps {secondElement.Name} at {width}x{height}.");
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    private static SessionSummary CreateOneBattleSession()
    {
        BattleRecord battle = new()
        {
            Id = 1, BattleKey = "ui-sample", StartedAt = DateTimeOffset.Now, Server = "ASIA", AccountId = "1",
            AccountName = "Sample", ShipId = "101", ShipName = "Yamato", ShipType = "Battleship", BattleCount = 1, Result = BattleResult.Win,
            WinCount = 1, Damage = 100_000, Frags = 1, Completeness = BattleCompleteness.Complete
        };
        return new SessionSummary
        {
            Session = new BattleSession { Id = 1, StartedAt = battle.StartedAt, EndedAt = battle.StartedAt, AccountName = battle.AccountName, BattleCount = 1 },
            Battles = new[] { battle },
            Metrics = new HistorySummary { RecordedBattles = 1, EffectiveBattles = 1, Winrate = 1, AverageDamage = 100_000, AverageFrags = 1, AveragePr = 1_500, CompletenessRate = 1 }
        };
    }

    private static void SaveSnapshotIfRequested(Window window, string language, double width, double height, int tab)
    {
        string? directory = Environment.GetEnvironmentVariable("APERADAR_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory) || (Math.Abs(width - 860) > 0.1 && Math.Abs(width - 1000) > 0.1)) return;
        SaveWindowSnapshot(window, $"history-{language}-{width:0}x{height:0}-tab-{tab}.png");
    }

    private static void SaveWindowSnapshot(Window window, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("APERADAR_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        int bitmapWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int bitmapHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        RenderTargetBitmap bitmap = new(bitmapWidth, bitmapHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }
}
