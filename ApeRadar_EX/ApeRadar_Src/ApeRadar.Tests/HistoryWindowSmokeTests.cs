using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ApeRadar.History;
using ApeRadar.Models;
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
        Thread thread = new(() =>
        {
            try
            {
                Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string language in new[] { "en-us", "zh-cn" })
                {
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
                    ValidateMainWindowLayout(language);
                    ValidateConfigWindowLayout(language);
                }
                app.Shutdown();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The history window constructor did not complete in time.");
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
        ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats = true;
        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
        MainWindow window = new(initializeRuntime: false)
        {
            WindowState = WindowState.Normal,
            ShowInTaskbar = false
        };
        window.DataGridAlliesList.Columns.Add(Assert.IsType<DataGridTemplateColumn>(window.FindResource("AlliesNameColumn")));
        window.DataGridAlliesList.Columns.Add(Assert.IsType<DataGridTemplateColumn>(window.FindResource("AlliesStatisticsColumn")));
        window.DataGridAlliesList.Columns.Add(Assert.IsType<DataGridTemplateColumn>(window.FindResource("AlliesTagColumn")));
        window.DataGridEnemiesList.Columns.Add(Assert.IsType<DataGridTemplateColumn>(window.FindResource("EnemiesNameColumn")));
        window.DataGridEnemiesList.Columns.Add(Assert.IsType<DataGridTemplateColumn>(window.FindResource("EnemiesStatisticsColumn")));
        window.DataGridEnemiesList.Columns.Add(Assert.IsType<DataGridTemplateColumn>(window.FindResource("EnemiesTagColumn")));
        window.DataGridAlliesList.ItemsSource = Enumerable.Range(1, 12).Select(i => CreateTierPerformancePlayer($"Allied sample {i}", i == 1 ? "0" : "1")).ToArray();
        window.DataGridEnemiesList.ItemsSource = Enumerable.Range(1, 12).Select(i => CreateTierPerformancePlayer($"Enemy sample {i}", "2")).ToArray();
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
            FrameworkElement about = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("MainFooterAbout"));
            AssertElementsDoNotOverlap(window, messages, buttons, width, height);
            AssertElementsDoNotOverlap(window, messages, summary, width, height);
            AssertElementsDoNotOverlap(window, summary, about, width, height);
            FrameworkElement analysis = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("AnalysisPanel"));
            Assert.Equal(renderedWidth >= 1440 ? Visibility.Visible : Visibility.Collapsed, analysis.Visibility);
            Assert.Equal(renderedWidth >= 1900 ? 3 : 2, window.DataGridAlliesList.Columns.Count);
            Assert.Equal(renderedWidth < 900 ? Visibility.Collapsed : Visibility.Visible, about.Visibility);
            if (renderedWidth < 1900)
            {
                Assert.InRange(window.DataGridAlliesList.Columns[1].ActualWidth, 181, 183);
                Assert.InRange(window.DataGridEnemiesList.Columns[1].ActualWidth, 181, 183);
            }
            else
            {
                Assert.InRange(window.DataGridAlliesList.Columns[1].ActualWidth, RosterLayoutCalculator.FullStatisticsColumnWidth - 1, RosterLayoutCalculator.FullStatisticsColumnWidth + 1);
                Assert.InRange(window.DataGridEnemiesList.Columns[1].ActualWidth, RosterLayoutCalculator.FullStatisticsColumnWidth - 1, RosterLayoutCalculator.FullStatisticsColumnWidth + 1);
            }
            AssertDataGridCellContentsStayInside(window.DataGridAlliesList, width, height);
            AssertDataGridCellContentsStayInside(window.DataGridEnemiesList, width, height);

            if (renderedWidth >= 1900 && height >= 1000)
            {
                ScrollViewer alliesScroll = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(window.DataGridAlliesList));
                ScrollViewer enemiesScroll = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(window.DataGridEnemiesList));
                Assert.Equal(Visibility.Collapsed, alliesScroll.ComputedVerticalScrollBarVisibility);
                Assert.Equal(Visibility.Collapsed, enemiesScroll.ComputedVerticalScrollBarVisibility);
                Assert.InRange(window.DataGridAlliesList.RowHeight, RosterLayoutCalculator.MinimumRowHeight, RosterLayoutCalculator.PreferredRowHeight);
            }

            if (Math.Abs(width - 800) < 0.1 || Math.Abs(width - 1280) < 0.1 || Math.Abs(width - 1366) < 0.1 || Math.Abs(width - 1600) < 0.1 || Math.Abs(width - 1920) < 0.1)
            {
                SaveWindowSnapshot(window, $"main-{language}-{width:0}x{height:0}.png");
            }
        }
        window.Close();
        ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats = previousTierPerformanceSetting;
        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = previousShipTypeIconSetting;
    }

    private static void AssertShipTypeIconsRefreshWithoutReload(MainWindow window, string language)
    {
        string expectedTooltip = language == "zh-cn" ? "巡洋舰" : "Cruiser";
        Image[] icons = FindVisualChildren<Image>(window)
            .Where(image => image.Width == 18 && Equals(image.ToolTip, expectedTooltip))
            .ToArray();
        Assert.NotEmpty(icons);
        Assert.All(icons, icon => Assert.Equal(Visibility.Visible, icon.Visibility));

        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = false;
        ShipTypePresentation.RefreshOpenWindows();
        Assert.All(icons, icon =>
        {
            Assert.Equal(Visibility.Collapsed, icon.Visibility);
            Assert.Null(icon.Source);
        });

        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
        ShipTypePresentation.RefreshOpenWindows();
        Assert.All(icons, icon =>
        {
            Assert.Equal(Visibility.Visible, icon.Visibility);
            Assert.NotNull(icon.Source);
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
                    $"{content.GetType().Name} escapes its roster cell at {width}x{height}: {bounds} outside width {cell.ActualWidth:0.##}.");
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
        StackPanel tierPerformancePanel = Assert.IsType<StackPanel>(tierPerformanceCheckBox.Parent);
        Grid tierPerformanceCell = Assert.IsType<Grid>(tierPerformancePanel.Parent);
        Assert.Equal(8, Grid.GetRow(tierPerformanceCell));
        window.ConfigTabs.SelectedIndex = 5;
        window.UpdateLayout();
        Assert.False(window.BtnDefault.IsEnabled);
        window.ConfigTabs.SelectedIndex = 1;
        bool persistedIconSetting = ApeRadar.Properties.Settings.Default.ShowShipTypeIcon;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
            window.ChkBoxShowShipTypeIcon.IsChecked = true;
            window.ApplyDefaultsForSelectedPage();
            Assert.False(window.ChkBoxShowShipTypeIcon.IsChecked);
            Assert.True(ApeRadar.Properties.Settings.Default.ShowShipTypeIcon);
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = persistedIconSetting;
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
