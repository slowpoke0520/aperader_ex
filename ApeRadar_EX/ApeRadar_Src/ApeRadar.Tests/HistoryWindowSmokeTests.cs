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
            SynchronizationContext? previousContext = SynchronizationContext.Current;
            Application? app = null;
            try
            {
                // The test drives this STA dispatcher with nested frames rather than Application.Run.
                // Keep async chart continuations on the owning UI thread even between those frames.
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher));
                app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                HistoryServices historyServices = new();
                try
                {
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
                        progress = $"{language}: player detail numbers";
                        PlayerDetailCardLayoutAssertions.Verify(language);
                        progress = $"{language}: history window";
                        bool previousShipTypeIconSetting = ApeRadar.Properties.Settings.Default.ShowShipTypeIcon;
                        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
                        HistoryWindow window = new(historyServices, initializeOnLoaded: false);
                        Assert.Same(historyServices, window.HistoryServices);
                        HistoryViewModel viewModel = Assert.IsType<HistoryViewModel>(window.DataContext);
                        // Actual table rows cover complete, partial, metadata-only and failed imports.
                        BattleRecord[] examples = Enumerable.Range(1, 12).Select(HistoryPagingTests.Record).ToArray();
                        examples[0].Completeness = BattleCompleteness.Partial;
                        examples[0].Source = BattleMetricSource.ApiMerged;
                        examples[0].StatusMessage = "API merged results; partial metrics";
                        examples[1].Completeness = BattleCompleteness.Pending;
                        examples[1].Source = BattleMetricSource.MetadataOnly;
                        examples[1].Damage = null;
                        examples[1].Frags = null;
                        examples[1].WinCount = null;
                        foreach (BattleRecord example in examples)
                            viewModel.Rows.Add(new HistoryRowViewModel(example, null, null, null));
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
                                foreach (ComboBox combo in FindVisualChildren<ComboBox>(window).Where(c => c.SelectedItem != null))
                                    AssertSelectorText(combo);
                                SaveSnapshotIfRequested(window, language, width, height, tab);
                            }
                        }
                        window.Close();
                        ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = previousShipTypeIconSetting;
                        progress = $"{language}: note tag layouts";
                        NoteTagsLayoutAssertions.Verify(language, historyServices);
                        progress = $"{language}: main window";
                        ValidateMainWindowLayout(language, historyServices);
                        progress = $"{language}: dashboard window";
                        ValidateDashboardWindowLayout(language, historyServices);
                        progress = $"{language}: config window";
                        ValidateConfigWindowLayout(language);
                        progress = $"{language}: note editor";
                        ValidateNoteEditor(language);
                    }
                }
                finally { historyServices.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                try
                {
                    app?.Shutdown();
                    // Shutdown is queued on the dispatcher; finish it before this STA exits.
                    // Otherwise Application.Current retains the last language resources.
                    if (app != null && !app.Dispatcher.HasShutdownStarted)
                        app.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                catch (Exception ex) { error ??= ex; }
                finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), $"The UI smoke test did not complete in time; last stage: {progress}.");
        Assert.Null(error);
        Assert.Null(Application.Current);
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

    private static void ValidateMainWindowLayout(string language, HistoryServices historyServices)
    {
        string previousInterface = ApeRadar.Properties.Settings.Default.MainInterfaceStyle;
        bool previousTierPerformanceSetting = ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats;
        bool previousShipTypeIconSetting = ApeRadar.Properties.Settings.Default.ShowShipTypeIcon;
        string previousDensity = ApeRadar.Properties.Settings.Default.RosterDisplayDensity;
        bool previousLegacyTag = ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag;
        bool previousAccountColumn = ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn;
        bool previousShipColumn = ApeRadar.Properties.Settings.Default.ShowShipRosterColumn;
        bool previousPerformanceColumn = ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn;
        string previousPerformanceMetric = ApeRadar.Properties.Settings.Default.RosterPerformanceMetric;
        int previousAccountWinrate = ApeRadar.Properties.Settings.Default.AccountWinrateVisibility;
        int previousAccountAvgExp = ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility;
        int previousWeightedWinrate = ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility;
        int previousShipWinrate = ApeRadar.Properties.Settings.Default.ShipWinrateVisibility;
        int previousShipAvgDamage = ApeRadar.Properties.Settings.Default.ShipAvgDmgVisibility;
        int previousShipAvgExp = ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility;
        int previousPr = ApeRadar.Properties.Settings.Default.PRVisibility;
        MainWindow? window = null;
        try
        {
            ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats = false;
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = true;
            ApeRadar.Properties.Settings.Default.RosterDisplayDensity = "Standard";
            ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = false;
            ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn = true;
            ApeRadar.Properties.Settings.Default.ShowShipRosterColumn = true;
            ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn = true;
            ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = "PR";
            ApeRadar.Properties.Settings.Default.AccountWinrateVisibility = 0;
            ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility = 2;
            ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility = 0;
            ApeRadar.Properties.Settings.Default.ShipWinrateVisibility = 0;
            ApeRadar.Properties.Settings.Default.ShipAvgDmgVisibility = 0;
            ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility = 2;
            ApeRadar.Properties.Settings.Default.PRVisibility = 0;
            ApeRadar.Properties.Settings.Default.MainInterfaceStyle = "Legacy";
            window = new(historyServices, initializeRuntime: false)
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
            ValidateBattleChartRendering(window, dashboard: false);
            ValidateLegacySummaryAvailability(window);
            ValidateNotificationEscape(window, dashboard: false);
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
                Assert.Equal(Visibility.Collapsed, analysis.Visibility);
                Assert.Equal(6, window.DataGridAlliesList.Columns.Count);
                Assert.Equal(6, window.DataGridEnemiesList.Columns.Count);
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
                    Assert.True(window.DataGridAlliesList.RowHeight >= 61);
                    Assert.Equal(17, window.EffectivePlayerFontSize);
                    Assert.Equal(14, window.EffectiveStatisticsFontSize);
                    DataGridCell accountCell = Assert.Single(FindVisualChildren<DataGridCell>(window.DataGridAlliesList)
                        .Where(cell => cell.IsVisible && cell.Column?.SortMemberPath == "Account").Take(1));
                    Assert.Equal(new Thickness(0, 0, 1, 1), accountCell.BorderThickness);
                    Assert.IsType<SolidColorBrush>(accountCell.BorderBrush);
                    DataGridCell performanceCell = Assert.Single(FindVisualChildren<DataGridCell>(window.DataGridAlliesList)
                        .Where(cell => cell.IsVisible && cell.Column?.SortMemberPath == "Performance").Take(1));
                    Assert.Equal(new Thickness(1, 0, 0, 1), performanceCell.BorderThickness);

                    double alliesWidthBeforeDrawer = window.DataGridAlliesList.ActualWidth;
                    window.BtnToggleAnalysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Visible, analysis.Visibility);
                    Assert.Equal(alliesWidthBeforeDrawer, window.DataGridAlliesList.ActualWidth, 1);
                    window.BtnToggleAnalysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, analysis.Visibility);
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
            OpenHoverPopup(window, firstRow, window.PlayerDetailPopup, $"{language} Legacy hover");
            Assert.True(window.PlayerDetailPopup.IsOpen);
            PlayerDetailCardViewModel detail = Assert.IsType<PlayerDetailCardViewModel>(window.PlayerDetailCardContent.DataContext);
            PlayerDetailCardLayoutAssertions.AssertCompleteNumbers(window.PlayerDetailCardContent, detail.Player, language, "main popup");
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
            NativeMouseInput.MoveTo(new Point(virtualScreen.Right - 2, virtualScreen.Bottom - 2));
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
        }
        finally
        {
            window?.Close();
            ApeRadar.Properties.Settings.Default.ShowTierPerformanceStats = previousTierPerformanceSetting;
            ApeRadar.Properties.Settings.Default.ShowShipTypeIcon = previousShipTypeIconSetting;
            ApeRadar.Properties.Settings.Default.RosterDisplayDensity = previousDensity;
            ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = previousLegacyTag;
            ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn = previousAccountColumn;
            ApeRadar.Properties.Settings.Default.ShowShipRosterColumn = previousShipColumn;
            ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn = previousPerformanceColumn;
            ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = previousPerformanceMetric;
            ApeRadar.Properties.Settings.Default.AccountWinrateVisibility = previousAccountWinrate;
            ApeRadar.Properties.Settings.Default.AccountAvgExpVisibility = previousAccountAvgExp;
            ApeRadar.Properties.Settings.Default.WeightedWinrateVisibility = previousWeightedWinrate;
            ApeRadar.Properties.Settings.Default.ShipWinrateVisibility = previousShipWinrate;
            ApeRadar.Properties.Settings.Default.ShipAvgDmgVisibility = previousShipAvgDamage;
            ApeRadar.Properties.Settings.Default.ShipAvgExpVisibility = previousShipAvgExp;
            ApeRadar.Properties.Settings.Default.PRVisibility = previousPr;
            ApeRadar.Properties.Settings.Default.MainInterfaceStyle = previousInterface;
        }
    }

    private static void ValidateDashboardWindowLayout(string language, HistoryServices historyServices)
    {
        string previousInterface = ApeRadar.Properties.Settings.Default.MainInterfaceStyle;
        bool previousAccountColumn = ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn;
        bool previousShipColumn = ApeRadar.Properties.Settings.Default.ShowShipRosterColumn;
        bool previousPerformanceColumn = ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn;
        bool previousLegacyPerformanceTag = ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag;
        string previousPerformanceMetric = ApeRadar.Properties.Settings.Default.RosterPerformanceMetric;
        int previousWinrateType = ApeRadar.Properties.Settings.Default.WinrateTypeUsed;
        int previousPr = ApeRadar.Properties.Settings.Default.PRVisibility;
        try
        {
            ApeRadar.Properties.Settings.Default.MainInterfaceStyle = "Dashboard";
            ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn = true;
            ApeRadar.Properties.Settings.Default.ShowShipRosterColumn = true;
            ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn = true;
            ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = true;
            ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = "PR";
            ApeRadar.Properties.Settings.Default.PRVisibility = 0;

            MainWindow window = new(historyServices, initializeRuntime: false)
            {
                WindowState = WindowState.Normal,
                ShowInTaskbar = false
            };
            List<Player> players = Enumerable.Range(1, 12)
                .Select(i => CreateTierPerformancePlayer($"Allied dashboard sample {i}", i == 1 ? "0" : "1"))
                .Concat(Enumerable.Range(1, 12).Select(i => CreateTierPerformancePlayer($"Enemy dashboard sample {i}", "2")))
                .ToList();
            // Render every grade, including long English labels, in the actual cells.
            double[] grades = { 500, 900, 1200, 1400, 1600, 1900, 2200, 2600 };
            for (int i = 0; i < grades.Length; i++) players[i + 2].PR = grades[i];
            players[0].Note = "可靠队友";
            players[0].IsCustomMarked = true;
            players[0].Karma = 27;
            players[1].Karma = 0;
            players[12].Note = "谨慎推进";
            players[13].Battles = -1;
            players[13].AccountWinrate = -1;
            players[13].PR = -1;
            players[13].ShipBattles = -1;
            players[13].ShipWinrate = -1;
            players[13].ShipAvgDmgPerBattle = -1;
            players[13].ShipPR = -1;
            players[13].TierBattles = -1;
            players[13].TierWinrate = -1;
            players[13].TierPR = -1;
            players[13].IsDataFetchFailed = true;
            Battlefield battlefield = (Battlefield)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Battlefield));
            battlefield.BattleType = "RandomBattle";
            battlefield.BattleStartTime = DateTimeOffset.Now;
            battlefield.Allies = players.Where(player => player.Relation is "0" or "1").ToList();
            battlefield.Enemies = players.Where(player => player.Relation is not ("0" or "1")).ToList();
            window.Dashboard.Update(battlefield, true, new DashboardBattleMetadata(
                "Northern Lights", "Random battle", "ASIA", DateTimeOffset.Now, "Vortex", DateTimeOffset.Now));

            // MainWindow starts maximized in production. Force a normal window here so the
            // requested render size is deterministic on CI runners with smaller desktops.
            window.WindowState = WindowState.Normal;
            window.Width = 1600;
            window.Height = 940;
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Assert.Equal(Visibility.Visible, window.DashboardView.Visibility);
            Assert.Equal(Visibility.Collapsed, window.LegacyRoot.Visibility);
            Assert.Equal(12, window.DashboardView.AlliesGrid.Items.Count);
            Assert.Equal(12, window.DashboardView.EnemiesGrid.Items.Count);
            Assert.DoesNotContain(window.Dashboard.Allies, row => row.IsFetchFailed);
            Assert.Single(window.Dashboard.Enemies, row => row.IsFetchFailed);
            Assert.Equal(12, window.Dashboard.Allies.Count(row => row.HasValidAccount));
            Assert.Equal(11, window.Dashboard.Enemies.Count(row => row.HasValidAccount));
            Assert.Equal(window.DashboardView.AlliesGrid.RowHeight, window.DashboardView.EnemiesGrid.RowHeight);
            Assert.InRange(window.DashboardView.AlliesGrid.RowHeight, 46, 56);
            Assert.Equal(window.DashboardView.AlliesGrid.Columns.Count, window.DashboardView.EnemiesGrid.Columns.Count);
            Assert.Equal(5, window.DashboardView.AlliesGrid.Columns.Count);
            for (int i = 0; i < window.DashboardView.AlliesGrid.Columns.Count; i++)
                Assert.Equal(window.DashboardView.AlliesGrid.Columns[i].ActualWidth, window.DashboardView.EnemiesGrid.Columns[i].ActualWidth, 1);
            Assert.False(window.DashboardView.AlliesGrid.CanUserResizeColumns);
            Assert.False(window.DashboardView.EnemiesGrid.CanUserResizeColumns);
            Assert.Equal(Visibility.Collapsed, window.DashboardView.AnalysisDrawer.Visibility);
            Assert.NotNull(window.DashboardView.SortCombo.SelectedItem);
            AssertSelectorText(window.DashboardView.SortCombo);
            AssertSelectorText(window.DashboardView.LanguageCombo);
            double expectedSidebarWidth = window.DashboardView.ActualWidth < 1400 ? 64 : 208;
            Assert.Equal(new GridLength(expectedSidebarWidth), window.DashboardView.SidebarColumn.Width);
            Assert.Equal(28, window.DashboardView.AlliesGrid.ColumnHeaderHeight);
            Assert.True(window.DashboardView.AllyContextColumn.ActualWidth > 0);
            Assert.True(window.DashboardView.AllyShipColumn.ActualWidth > 0);
            Rect comparisonBounds = window.DashboardView.ComparisonPanel.TransformToAncestor(window.DashboardView.ComparisonCard)
                .TransformBounds(new Rect(window.DashboardView.ComparisonPanel.RenderSize));
            Assert.InRange(
                Math.Abs((comparisonBounds.Left + comparisonBounds.Right) / 2 - window.DashboardView.ComparisonCard.ActualWidth / 2),
                0,
                1);
            Assert.True(window.DashboardView.RosterToolbar.ActualHeight >= 30);
            Assert.Equal(90, window.DashboardView.AllyContextColumn.MinWidth);
            Assert.Equal(112, window.DashboardView.AllyShipColumn.MinWidth);
            if (window.DashboardView.ActualWidth >= 1400)
            {
                Assert.Equal(90, window.DashboardView.AllyContextColumn.ActualWidth, 1);
                Assert.InRange(window.DashboardView.AllyShipColumn.ActualWidth, 108, 118);
            }
            Assert.NotNull(window.DashboardView.AllyContextColumn.HeaderTemplate);
            Assert.NotNull(window.DashboardView.AllyShipColumn.HeaderTemplate);

            ScrollViewer alliesScroll = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(window.DashboardView.AlliesGrid));
            ScrollViewer enemiesScroll = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(window.DashboardView.EnemiesGrid));
            AssertRosterScrollBehavior(window.DashboardView.AlliesGrid, alliesScroll);
            AssertRosterScrollBehavior(window.DashboardView.EnemiesGrid, enemiesScroll);
            AssertDataGridCellContentsStayInside(window.DashboardView.AlliesGrid, 1600, 940);
            AssertDataGridCellContentsStayInside(window.DashboardView.EnemiesGrid, 1600, 940);
            AssertFullDashboardRosterVisible(window);
            SaveWindowSnapshot(window, $"dashboard-{language}-1600x940.png");

            DataGridRow dashboardFirstRow = Assert.IsType<DataGridRow>(window.DashboardView.AlliesGrid.ItemContainerGenerator.ContainerFromIndex(0));
            DataGridRow dashboardEnemyFirstRow = Assert.IsType<DataGridRow>(window.DashboardView.EnemiesGrid.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Contains("可靠队友", ((DashboardPlayerRowViewModel)dashboardFirstRow.Item).AllStatusToolTip);
            Assert.Contains("谨慎推进", ((DashboardPlayerRowViewModel)dashboardEnemyFirstRow.Item).AllStatusToolTip);
            FrameworkElement accountContent = Assert.IsAssignableFrom<FrameworkElement>(window.DashboardView.AllyContextColumn.GetCellContent(dashboardFirstRow));
            Assert.Contains(FindVisualChildren<TextBlock>(accountContent), text => text.Text == "60.0%");
            Assert.Contains(FindVisualChildren<TextBlock>(accountContent), text => text.Text.StartsWith("5000"));
            FrameworkElement shipContent = Assert.IsAssignableFrom<FrameworkElement>(window.DashboardView.AllyShipColumn.GetCellContent(dashboardFirstRow));
            Assert.Contains(FindVisualChildren<TextBlock>(shipContent), text => text.Text == "56.2%");
            Assert.Contains(FindVisualChildren<TextBlock>(shipContent), text => text.Text.StartsWith("13850"));
            FrameworkElement prContent = Assert.IsAssignableFrom<FrameworkElement>(window.DashboardView.AllyPrColumn.GetCellContent(dashboardFirstRow));
            Assert.Contains(FindVisualChildren<TextBlock>(prContent), text => text.Text.Contains("1700"));
            Assert.Contains(FindVisualChildren<TextBlock>(prContent), text => text.Text.Contains("1250"));
            System.Windows.Documents.Run karmaRun = FindVisualChildren<TextBlock>(dashboardFirstRow)
                .SelectMany(text => text.Inlines.OfType<System.Windows.Documents.Run>())
                .Single(run => run.Text == "27");
            Assert.Equal(BaselineAlignment.Superscript, karmaRun.BaselineAlignment);
            FrameworkElement performanceContent = Assert.IsAssignableFrom<FrameworkElement>(window.DashboardView.AllyPerformanceColumn.GetCellContent(dashboardFirstRow));
            TextBlock performanceIcon = Assert.Single(FindVisualChildren<TextBlock>(performanceContent), text => text.Name == "LegacyPerformanceIcon");
            Assert.Equal(ApeRadar.Properties.Settings.Default.UnicumIcon, performanceIcon.Text);
            TextBlock performanceLabel = Assert.Single(FindVisualChildren<TextBlock>(performanceContent), text => text.Name == "PerformanceLabel");
            Assert.Equal(((DashboardPlayerRowViewModel)dashboardFirstRow.Item).PerformanceDisplay, performanceLabel.Text);
            Assert.False(int.TryParse(performanceLabel.Text, out _));
            Assert.Equal(12, performanceLabel.FontSize);
            Assert.Contains("PR", window.DashboardView.AllyPerformanceColumn.Header.ToString());
            AssertDashboardSkillLabels(window, language);
            foreach ((int type, string basis, string resource) in new[]
            {
                (0, "WR", "RosterPerformanceAccountWinrate"),
                (1, "WWR", "RosterPerformanceWeightedWinrate")
            })
            {
                ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = "Winrate";
                ApeRadar.Properties.Settings.Default.WinrateTypeUsed = type;
                window.DashboardView.RefreshSettings();
                Assert.EndsWith(basis, window.DashboardView.AllyPerformanceColumn.Header.ToString());
                Assert.Equal(window.DashboardView.AllyPerformanceColumn.Header, window.DashboardView.EnemyPerformanceColumn.Header);
                Setter tooltip = Assert.Single(window.DashboardView.AllyPerformanceColumn.HeaderStyle.Setters.OfType<Setter>(),
                    setter => setter.Property == FrameworkElement.ToolTipProperty);
                Assert.Equal(Application.Current.FindResource(resource), tooltip.Value);
            }
            ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = "PR";
            ApeRadar.Properties.Settings.Default.WinrateTypeUsed = previousWinrateType;
            window.DashboardView.RefreshSettings();
            Assert.NotNull(window.DashboardView.HelpNavButton.ToolTip);
            Assert.NotNull(window.DashboardView.UpdateNavButton.ToolTip);
            Rect brandBounds = window.DashboardView.BrandText.TransformToAncestor(window.DashboardView).TransformBounds(new Rect(window.DashboardView.BrandText.RenderSize));
            Rect versionBounds = window.DashboardView.SidebarVersion.TransformToAncestor(window.DashboardView).TransformBounds(new Rect(window.DashboardView.SidebarVersion.RenderSize));
            Assert.InRange(brandBounds.Right, 0, window.DashboardView.SidebarColumn.ActualWidth + 0.5);
            Assert.InRange(versionBounds.Right, 0, window.DashboardView.SidebarColumn.ActualWidth + 0.5);

            window.Width = 1920;
            window.Height = 1040;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            AssertDataGridCellContentsStayInside(window.DashboardView.AlliesGrid, 1920, 1040);
            AssertDataGridCellContentsStayInside(window.DashboardView.EnemiesGrid, 1920, 1040);
            bool hasWideViewport = window.DashboardView.ActualWidth >= 1400;
            AssertFullDashboardRosterVisible(window);
            if (hasWideViewport)
            {
                Assert.True(alliesScroll.ScrollableWidth <= 1);
                Assert.True(enemiesScroll.ScrollableWidth <= 1);
            }
            AssertRosterScrollBehavior(window.DashboardView.AlliesGrid, alliesScroll);
            AssertRosterScrollBehavior(window.DashboardView.EnemiesGrid, enemiesScroll);
            {
                DataGridRow alliedLastRow = Assert.IsType<DataGridRow>(window.DashboardView.AlliesGrid.ItemContainerGenerator.ContainerFromIndex(11));
                DataGridRow enemyLastRow = Assert.IsType<DataGridRow>(window.DashboardView.EnemiesGrid.ItemContainerGenerator.ContainerFromIndex(11));
                Rect alliedLastBounds = alliedLastRow.TransformToAncestor(window.DashboardView.AlliesGrid).TransformBounds(new Rect(alliedLastRow.RenderSize));
                Rect enemyLastBounds = enemyLastRow.TransformToAncestor(window.DashboardView.EnemiesGrid).TransformBounds(new Rect(enemyLastRow.RenderSize));
                Assert.InRange(window.DashboardView.AlliesGrid.ActualHeight - alliedLastBounds.Bottom, 0, 24);
                Assert.InRange(window.DashboardView.EnemiesGrid.ActualHeight - enemyLastBounds.Bottom, 0, 24);
            }
            SaveWindowSnapshot(window, $"dashboard-{language}-1920x1040.png");
            ValidateBattleChartRendering(window, dashboard: true);
            ValidateNotificationEscape(window, dashboard: true);

            window.Width = 1600;
            window.Height = 940;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            dashboardFirstRow = Assert.IsType<DataGridRow>(window.DashboardView.AlliesGrid.ItemContainerGenerator.ContainerFromIndex(0));
            dashboardEnemyFirstRow = Assert.IsType<DataGridRow>(window.DashboardView.EnemiesGrid.ItemContainerGenerator.ContainerFromIndex(0));
            ContextMenu dashboardRowMenu = Assert.IsType<ContextMenu>(dashboardFirstRow.ContextMenu);
            ContextMenu dashboardEnemyRowMenu = Assert.IsType<ContextMenu>(dashboardEnemyFirstRow.ContextMenu);
            Assert.NotSame(dashboardRowMenu, dashboardEnemyRowMenu);
            dashboardRowMenu.PlacementTarget = dashboardFirstRow;
            dashboardRowMenu.IsOpen = true;
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            Assert.True(dashboardRowMenu.IsOpen);
            dashboardRowMenu.IsOpen = false;
            OpenHoverPopup(window, dashboardFirstRow, window.DashboardView.PlayerDetailPopup, $"{language} Dashboard hover");
            Assert.True(window.DashboardView.PlayerDetailPopup.IsOpen);
            PlayerDetailCardViewModel dashboardDetail = Assert.IsType<PlayerDetailCardViewModel>(window.DashboardView.PlayerDetailCardContent.DataContext);
            PlayerDetailCardLayoutAssertions.AssertCompleteNumbers(window.DashboardView.PlayerDetailCardContent, dashboardDetail.Player, language, "dashboard popup");
            DataGridRow popupTarget = Assert.IsType<DataGridRow>(window.DashboardView.PlayerDetailPopup.PlacementTarget);
            Assert.Same(dashboardFirstRow.DataContext, popupTarget.DataContext);
            Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Custom, window.DashboardView.PlayerDetailPopup.Placement);
            dashboardFirstRow.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent,
                Source = dashboardFirstRow
            });
            window.DashboardView.PlayerDetailPopup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent,
                Source = window.DashboardView.PlayerDetailPopup
            });
            PumpDispatcher(TimeSpan.FromMilliseconds(300));
            Assert.True(window.DashboardView.PlayerDetailPopup.IsOpen);
            System.Drawing.Rectangle dashboardScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
            NativeMouseInput.MoveTo(new Point(dashboardScreen.Right - 2, dashboardScreen.Bottom - 2));
            window.DashboardView.PlayerDetailPopup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent,
                Source = window.DashboardView.PlayerDetailPopup
            });
            PumpDispatcher(TimeSpan.FromMilliseconds(300));
            Assert.False(window.DashboardView.PlayerDetailPopup.IsOpen);

            double widthBeforeDrawer = window.DashboardView.AlliesGrid.ActualWidth;
            Button analysisButton = Assert.IsType<Button>(FindVisualParent<Button>(window.DashboardView.NavAnalysisText));
            analysisButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, window.DashboardView.AnalysisDrawer.Visibility);
            window.UpdateLayout();
            Assert.Equal(widthBeforeDrawer, window.DashboardView.AlliesGrid.ActualWidth, 1);
            PresentationSource presentationSource = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(window));
            System.Windows.Input.KeyEventArgs escape = new(System.Windows.Input.Keyboard.PrimaryDevice, presentationSource,
                Environment.TickCount, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
            };
            window.DashboardView.Root.RaiseEvent(escape);
            Assert.True(escape.Handled);
            Assert.Equal(Visibility.Collapsed, window.DashboardView.AnalysisDrawer.Visibility);

            window.DashboardView.TierContext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(DashboardRosterContext.Tier, window.Dashboard.Context);
            Assert.Equal(13_850, Assert.Single(window.Dashboard.Allies, row => row.Player.Name == "Allied dashboard sample 1").Player.TierBattles);
            window.DashboardView.AccountContext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(DashboardRosterContext.Account, window.Dashboard.Context);

            window.DashboardView.AllyMarked.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DashboardPlayerRowViewModel marked = Assert.Single(window.Dashboard.Allies);
            Assert.Equal("Allied dashboard sample 1", marked.Player.Name);
            Assert.Equal(12, window.Dashboard.Enemies.Count);
            window.DashboardView.AllyAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(12, window.Dashboard.Allies.Count);

            window.Width = 1040;
            window.Height = 680;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal(new GridLength(64), window.DashboardView.SidebarColumn.Width);
            Assert.True(window.DashboardView.AlliesGrid.ActualWidth > 0);
            Assert.True(window.DashboardView.EnemiesGrid.ActualWidth > 0);
            SaveWindowSnapshot(window, $"dashboard-{language}-1040x680.png");
            ValidateDashboardScaling(window, language);
            window.Width = 1280;
            window.Height = 720;
            players[0].Name = "A_long_player_name_for_real_layout_validation_12345";
            players[1].PR = players[1].ShipPR = players[1].TierPR = -1;
            players[2].ShipBattles = 3;
            players[3].IsHidden = true;
            players[4].IsDataStale = true;
            players[5].AccountWinrate = players[5].ShipWinrate = 0;
            players[6].AccountWinrate = players[6].ShipWinrate = 1;
            window.Dashboard.Update(battlefield, true, window.Dashboard.Metadata);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Contains(window.Dashboard.Allies, row => row.Player.IsHidden);
            Assert.Contains(window.Dashboard.Allies, row => row.IsShipLowSample);
            Assert.Contains(FindVisualChildren<TextBlock>(window.DashboardView.AlliesGrid), text => text.Text == "100.0%");
            SaveWindowSnapshot(window, $"dashboard-{language}-partial-states.png");
            battlefield.Allies = players.Where(player => player.Relation is "0" or "1")
                .Select(player => new Player(player.Name, player.Server, player.Relation, player.ShipID) { IsDataFetchFailed = true }).ToList();
            battlefield.Enemies = players.Where(player => player.Relation == "2")
                .Select(player => new Player(player.Name, player.Server, player.Relation, player.ShipID) { IsDataFetchFailed = true }).ToList();
            window.Dashboard.Update(battlefield, true, window.Dashboard.Metadata);
            window.RosterStatus.Set(RosterLoadState.Failed, language == "zh-cn" ? "战绩暂时不可用" : "Statistics temporarily unavailable");
            window.UpdateLayout();
            Assert.All(window.Dashboard.Allies, row => Assert.False(row.HasValidAccount));
            Assert.Equal(0, window.Dashboard.Summary.Ally.ContextValidCount);
            SaveWindowSnapshot(window, $"dashboard-{language}-unavailable.png");
            battlefield.Allies.Clear();
            battlefield.Enemies.Clear();
            window.Dashboard.Update(battlefield, false, window.Dashboard.Metadata);
            window.RosterStatus.Set(RosterLoadState.Idle, "");
            window.UpdateLayout();
            Assert.Empty(window.DashboardView.AlliesGrid.Items);
            SaveWindowSnapshot(window, $"dashboard-{language}-empty.png");
            window.Close();
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.MainInterfaceStyle = previousInterface;
            ApeRadar.Properties.Settings.Default.ShowAccountRosterColumn = previousAccountColumn;
            ApeRadar.Properties.Settings.Default.ShowShipRosterColumn = previousShipColumn;
            ApeRadar.Properties.Settings.Default.ShowPerformanceRosterColumn = previousPerformanceColumn;
            ApeRadar.Properties.Settings.Default.ShowLegacyPerformanceTag = previousLegacyPerformanceTag;
            ApeRadar.Properties.Settings.Default.RosterPerformanceMetric = previousPerformanceMetric;
            ApeRadar.Properties.Settings.Default.WinrateTypeUsed = previousWinrateType;
            ApeRadar.Properties.Settings.Default.PRVisibility = previousPr;
        }
    }

    private static void ValidateLegacySummaryAvailability(MainWindow window)
    {
        string[] paths = { "AllyAvgAccountWinrate", "EnemyAvgAccountWinrate", "AllyAvgBattleCount", "EnemyAvgBattleCount" };
        TextBlock[] metrics = FindVisualChildren<TextBlock>(window)
            .Where(text => paths.Contains(text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path))
            .ToArray();
        Assert.Equal(4, metrics.Length);
        object previousContext = window.DataContext;
        try
        {
            foreach (double value in new[] { -1d, 0d, 0.5d })
            {
                Battlefield battlefield = (Battlefield)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Battlefield));
                battlefield.AllyAvgAccountWinrate = battlefield.EnemyAvgAccountWinrate = value;
                battlefield.AllyAvgBattleCount = battlefield.EnemyAvgBattleCount = value < 0 ? -1 : 5000 * value;
                window.DataContext = battlefield;
                window.UpdateLayout();
                foreach (TextBlock metric in metrics)
                {
                    string path = metric.GetBindingExpression(TextBlock.TextProperty)!.ParentBinding.Path.Path;
                    string expected = value < 0 ? "-" : path.EndsWith("Winrate")
                        ? value.ToString("p1", CultureInfo.GetCultureInfo(metric.Language.IetfLanguageTag))
                        : (5000 * value).ToString("f0", CultureInfo.GetCultureInfo(metric.Language.IetfLanguageTag));
                    Assert.Equal(expected, metric.Text);
                }
            }
        }
        finally { window.DataContext = previousContext; }
    }

    private static void ValidateNotificationEscape(MainWindow window, bool dashboard)
    {
        window.RosterStatus.Set(RosterLoadState.Complete, "Player data loaded.");
        window.UpdateLayout();
        var popup = dashboard ? window.DashboardView.NotificationPopup : window.NotificationPopup;
        Button button = dashboard ? window.DashboardView.StatusButton : window.BtnToggleNotifications;
        UIElement root = dashboard ? window.DashboardView.Root : window.MainWindowGrid;
        foreach (bool insidePopup in new[] { false, true })
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            Assert.True(popup.IsOpen);
            UIElement source = insidePopup ? Assert.IsType<DataGrid>(FindVisualChild<DataGrid>(popup.Child)) : root;
            if (insidePopup) source.Focus();
            var presentationSource = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(source));
            System.Windows.Input.KeyEventArgs escape = new(System.Windows.Input.Keyboard.PrimaryDevice, presentationSource,
                Environment.TickCount, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
            };
            source.RaiseEvent(escape);
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            Assert.True(escape.Handled);
            Assert.False(popup.IsOpen);
            Assert.True(button.IsKeyboardFocused);
            if (!dashboard) Assert.Equal("＋", button.Content);
        }
    }

    private static void ValidateBattleChartRendering(MainWindow window, bool dashboard)
    {
        bool previousTopmost = window.Topmost;
        try
        {
            window.Topmost = true;
            window.Activate();
            ValidateBattleChartRenderingCore(window, dashboard);
        }
        finally { window.Topmost = previousTopmost; }
    }

    private static void ValidateBattleChartRenderingCore(MainWindow window, bool dashboard)
    {
        List<Player> players = Enumerable.Range(0, 24).Select(i =>
        {
            Player player = CreateTierPerformancePlayer($"Chart sample {i}", i < 12 ? "1" : "2");
            player.AccountWinrate = player.WeightedWinrate = (i < 12 ? 0.35 : 0.30) + i % 12 * 0.025;
            return player;
        }).ToList();
        Battlefield battlefield = (Battlefield)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Battlefield));
        battlefield.BattleType = "RandomBattle";
        battlefield.Allies = players.Take(12).ToList();
        battlefield.Enemies = players.Skip(12).ToList();
        FrameworkElement panel = dashboard ? window.DashboardView.AnalysisDrawer : window.AnalysisPanel;
        var winrate = dashboard ? window.DashboardView.DashboardWinrateChart : window.WinrateChart;
        var kde = dashboard ? window.DashboardView.DashboardKdeChart : window.KDEChart;
        Button toggle = dashboard
            ? Assert.IsType<Button>(FindVisualParent<Button>(window.DashboardView.NavAnalysisText))
            : window.BtnToggleAnalysis;

        // Data arrives while the drawer is closed, as it does for an actual roster load.
        winrate.Series = ChartUtils.GetWinrateChartSeries(battlefield, 0);
        kde.Series = ChartUtils.GetKDEChartSeries(battlefield);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        AssertChartPixels(panel, winrate, dashboard ? "Dashboard winrate" : "Legacy winrate");
        AssertChartPixels(panel, kde, dashboard ? "Dashboard distribution" : "Legacy distribution");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        AssertChartPixels(panel, winrate, "Reopened winrate");
        AssertChartPixels(panel, kde, "Reopened distribution");
        winrate.Series = ChartUtils.GetWinrateChartSeries(battlefield, 4);
        AssertChartPixels(panel, winrate, "Line winrate");
        battlefield.Allies[0].AccountWinrate = battlefield.Allies[0].WeightedWinrate = 0;
        battlefield.Enemies[0].AccountWinrate = battlefield.Enemies[0].WeightedWinrate = 1;
        battlefield.Enemies[1].AccountWinrate = battlefield.Enemies[1].WeightedWinrate = -1;
        winrate.Series = ChartUtils.GetWinrateChartSeries(battlefield, 0);
        kde.Series = ChartUtils.GetKDEChartSeries(battlefield);
        AssertChartPixels(panel, winrate, "Reloaded winrate with boundary and missing values");
        AssertChartPixels(panel, kde, "Reloaded distribution with boundary and missing values");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
    }

    private static void AssertChartPixels(FrameworkElement panel, FrameworkElement chart, string description)
    {
        Assert.True(chart.ActualWidth > 100 && chart.ActualHeight > 50, $"{description}: no chart viewport.");
        var liveChart = Assert.IsType<LiveChartsCore.SkiaSharpView.WPF.CartesianChart>(chart);
        Window window = Window.GetWindow(panel);
        int greenPixels = 0, redPixels = 0;
        int measured = 0, painted = 0, updated = 0;
        var surface = FindVisualChild<SkiaSharp.Views.WPF.SKElement>(chart);
        var motionCanvas = FindVisualChild<LiveChartsCore.SkiaSharpView.WPF.MotionCanvas>(chart);
        bool? initiallyLoaded = motionCanvas?.IsLoaded;
        string lifecycle = "";
        RoutedEventHandler OnCanvasLoaded = (_, _) => lifecycle += " Loaded";
        Action<LiveChartsCore.Motion.MotionCanvas<LiveChartsCore.SkiaSharpView.Drawing.SkiaSharpDrawingContext>> OnInvalidated = _ => lifecycle += " Invalidated";
        if (motionCanvas != null)
        {
            motionCanvas.Loaded += OnCanvasLoaded;
            motionCanvas.CanvasCore.Invalidated += OnInvalidated;
        }
        LiveChartsCore.Kernel.Events.ChartEventHandler<LiveChartsCore.SkiaSharpView.Drawing.SkiaSharpDrawingContext> OnMeasuring = _ => measured++;
        LiveChartsCore.Kernel.Events.ChartEventHandler<LiveChartsCore.SkiaSharpView.Drawing.SkiaSharpDrawingContext> OnUpdated = _ => updated++;
        void OnPainted(object? sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs args) => painted++;
        liveChart.Measuring += OnMeasuring;
        liveChart.UpdateFinished += OnUpdated;
        if (surface != null) surface.PaintSurface += OnPainted;
        bool CoreLoaded() => liveChart.CoreChart is LiveChartsCore.Chart<LiveChartsCore.SkiaSharpView.Drawing.SkiaSharpDrawingContext> core && core.IsLoaded;
        string Diagnostic() => $"green={greenPixels}, red={redPixels}, windowLoaded={window.IsLoaded}, windowActive={window.IsActive}, "
            + $"topmost={window.Topmost}, panelLoaded={panel.IsLoaded}, panelVisible={panel.IsVisible}, chartLoaded={chart.IsLoaded}, chartVisible={chart.IsVisible}, "
            + $"windowBounds={window.Left:F1},{window.Top:F1},{window.ActualWidth:F1}x{window.ActualHeight:F1}, surfaceVisible={surface?.IsVisible}, "
            + $"viewport={chart.ActualWidth:F1}x{chart.ActualHeight:F1}, source={(PresentationSource.FromVisual(chart) as System.Windows.Interop.HwndSource)?.Handle}, "
            + $"coreLoaded={CoreLoaded()}, canvasValid={liveChart.CoreCanvas?.IsValid}, series={liveChart.Series?.Count()}, "
            + $"measured={measured}, painted={painted}, updated={updated}, surfaceLoaded={surface?.IsLoaded}, surfaceSize={surface?.ActualWidth:F1}x{surface?.ActualHeight:F1}, "
            + $"canvasInitiallyLoaded={initiallyLoaded}, lifecycle={lifecycle}, "
            + $"foreground={NativeMouseInput.DescribeWindow(NativeMouseInput.GetForegroundWindow())}";
        // Observe the current drawing only. Never update the core, reopen the drawer, or reset Series to retry it.
        try
        {
            WaitForUiCondition(() =>
            {
                if (!chart.IsLoaded || !chart.IsVisible || !panel.IsVisible || !CoreLoaded()) return false;
                (greenPixels, redPixels) = CountChartPixels(window, chart);
                return greenPixels > 50 && redPixels > 50;
            }, TimeSpan.FromSeconds(5), $"{description}: rendered team pixels", Diagnostic, TimeSpan.FromMilliseconds(50));
            Assert.True(greenPixels > 50 && redPixels > 50,
                $"{description}: expected both teams to be plotted, found {greenPixels} green and {redPixels} red pixels.");
        }
        finally
        {
            liveChart.Measuring -= OnMeasuring;
            liveChart.UpdateFinished -= OnUpdated;
            if (surface != null) surface.PaintSurface -= OnPainted;
            if (motionCanvas != null)
            {
                motionCanvas.Loaded -= OnCanvasLoaded;
                motionCanvas.CanvasCore.Invalidated -= OnInvalidated;
            }
        }
    }

    private static (int Green, int Red) CountChartPixels(Window window, FrameworkElement chart)
    {
        RenderTargetBitmap bitmap = new((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        Rect bounds = chart.TransformToAncestor(window).TransformBounds(new Rect(chart.RenderSize));
        int greenPixels = 0, redPixels = 0;
        for (int y = Math.Max(0, (int)bounds.Top); y < Math.Min(bitmap.PixelHeight, (int)bounds.Bottom); y++)
            for (int x = Math.Max(0, (int)bounds.Left); x < Math.Min(bitmap.PixelWidth, (int)bounds.Right); x++)
            {
                int offset = y * stride + x * 4;
                byte blue = pixels[offset], green = pixels[offset + 1], red = pixels[offset + 2];
                if (green > 170 && red < 120 && blue > 100) greenPixels++;
                if (red > 200 && green < 120 && blue < 80) redPixels++;
            }
        return (greenPixels, redPixels);
    }

    private static void OpenHoverPopup(MainWindow window, DataGridRow row, System.Windows.Controls.Primitives.Popup popup, string scenario)
    {
        int opened = 0, closed = 0, activated = 0, deactivated = 0, entered = 0, left = 0, resized = 0;
        EventHandler onOpened = (_, _) => opened++;
        EventHandler onClosed = (_, _) => closed++;
        EventHandler onActivated = (_, _) => activated++;
        EventHandler onDeactivated = (_, _) => deactivated++;
        System.Windows.Input.MouseEventHandler onEntered = (_, _) => entered++;
        System.Windows.Input.MouseEventHandler onLeft = (_, _) => left++;
        SizeChangedEventHandler onResized = (_, _) => resized++;
        popup.Opened += onOpened;
        popup.Closed += onClosed;
        window.Activated += onActivated;
        window.Deactivated += onDeactivated;
        window.SizeChanged += onResized;
        row.MouseEnter += onEntered;
        row.MouseLeave += onLeft;
        Point target = new();
        IInputElement? visualHit = null;
        bool rowHit = false;
        bool activationRequested = false;
        int enteredBeforeTarget = 0;
        bool previousTopmost = window.Topmost;
        var source = PresentationSource.FromVisual(row) as System.Windows.Interop.HwndSource;
        string Diagnostic() => $"IsOpen={popup.IsOpen}, windowActive={window.IsActive}, windowVisible={window.IsVisible}, "
            + $"windowSize={window.ActualWidth:F1}x{window.ActualHeight:F1}, rowLoaded={row.IsLoaded}, rowVisible={row.IsVisible}, "
            + $"rowMouseOver={row.IsMouseOver}, rowSize={row.ActualWidth:F1}x{row.ActualHeight:F1}, "
            + $"cursor={System.Windows.Forms.Cursor.Position}, targetScreen={target}, directlyOver={System.Windows.Input.Mouse.DirectlyOver?.GetType().Name ?? "null"}, "
            + $"visualHit={visualHit?.GetType().Name ?? "null"}, rowHit={rowHit}, mouseCaptured={System.Windows.Input.Mouse.Captured?.GetType().Name ?? "null"}, "
            + $"activationRequested={activationRequested}, sourceHwnd={source?.Handle}, "
            + $"mouseSourceHwnd={(System.Windows.Input.Mouse.PrimaryDevice.ActiveSource as System.Windows.Interop.HwndSource)?.Handle}, "
            + $"foreground={NativeMouseInput.DescribeWindow(NativeMouseInput.GetForegroundWindow())}, targetWindow={NativeMouseInput.DescribeWindow(NativeMouseInput.WindowAt(target))}, "
            + $"placementTargetMatches={ReferenceEquals(popup.PlacementTarget, row)}, opened={opened}, closed={closed}, "
            + $"activated={activated}, deactivated={deactivated}, rowEntered={entered}, enteredBeforeTarget={enteredBeforeTarget}, rowLeft={left}, resized={resized}";
        try
        {
            // Activate once, then establish a fresh input source using real OS mouse moves.
            // SetCursorPos + Mouse.Synchronize can reuse a null source or a cached WPF position.
            // Waiting only pumps the dispatcher; it never reactivates or retries the movement.
            // Keep the real target above the test runner without changing the production window.
            window.Topmost = true;
            activationRequested = window.Activate();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            WaitForUiCondition(() => window.IsActive && row.IsLoaded && row.IsVisible && source != null &&
                System.Windows.Input.Mouse.Captured == null, TimeSpan.FromSeconds(2), $"{scenario}: input readiness", Diagnostic);
            target = row.PointToScreen(new Point(row.ActualWidth / 2, row.ActualHeight / 2));
            visualHit = window.InputHitTest(window.PointFromScreen(target));
            rowHit = row.InputHitTest(row.PointFromScreen(target)) != null;
            Assert.True(source!.Handle == NativeMouseInput.WindowAt(target), $"{scenario}: native target is obscured: {Diagnostic()}");
            // The first row's column header is a neutral client-area target, not another row.
            NativeMouseInput.MoveTo(row.PointToScreen(new Point(row.ActualWidth / 2, -8)));
            WaitForUiCondition(() => ReferenceEquals(System.Windows.Input.Mouse.PrimaryDevice.ActiveSource, source) &&
                !row.IsMouseOver, TimeSpan.FromSeconds(2), $"{scenario}: neutral mouse target", Diagnostic);
            enteredBeforeTarget = entered;
            NativeMouseInput.MoveTo(target);
            WaitForUiCondition(() => window.IsActive && row.IsMouseOver && entered - enteredBeforeTarget == 1 &&
                ReferenceEquals(System.Windows.Input.Mouse.PrimaryDevice.ActiveSource, source),
                TimeSpan.FromSeconds(2), $"{scenario}: real hover target", Diagnostic);
            WaitForUiCondition(() => popup.IsOpen, TimeSpan.FromSeconds(2), $"{scenario}: popup opening", Diagnostic);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            window.Topmost = previousTopmost;
            popup.Opened -= onOpened;
            popup.Closed -= onClosed;
            window.Activated -= onActivated;
            window.Deactivated -= onDeactivated;
            window.SizeChanged -= onResized;
            row.MouseEnter -= onEntered;
            row.MouseLeave -= onLeft;
        }
    }

    private static class NativeMouseInput
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int X;
            public int Y;
            public uint Data;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public MouseInput Mouse;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ScreenPoint
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(ScreenPoint point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int count);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        internal static string DescribeWindow(IntPtr window)
        {
            System.Text.StringBuilder title = new(256), type = new(256);
            GetWindowText(window, title, title.Capacity);
            GetClassName(window, type, type.Capacity);
            GetWindowThreadProcessId(window, out uint processId);
            return $"{{hwnd={window}, class={type}, title={title}, pid={processId}, testPid={Environment.ProcessId}}}";
        }

        internal static IntPtr WindowAt(Point screenPoint) => GetAncestor(WindowFromPoint(new ScreenPoint
        {
            X = (int)Math.Round(screenPoint.X),
            Y = (int)Math.Round(screenPoint.Y)
        }), 2); // GA_ROOT

        internal static void MoveTo(Point screenPoint)
        {
            System.Drawing.Rectangle desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            Assert.True(desktop.Contains((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)),
                $"Mouse target {screenPoint} is outside the virtual desktop {desktop}.");
            Input[] inputs =
            {
                new()
                {
                    Type = 0, // INPUT_MOUSE; no button or keyboard input is generated.
                    Mouse = new MouseInput
                    {
                        X = (int)Math.Round((screenPoint.X - desktop.Left) * 65535 / (desktop.Width - 1)),
                        Y = (int)Math.Round((screenPoint.Y - desktop.Top) * 65535 / (desktop.Height - 1)),
                        Flags = 0x0001 | 0x2000 | 0x4000 | 0x8000 // MOVE | MOVE_NOCOALESCE | VIRTUALDESK | ABSOLUTE
                    }
                }
            };
            uint accepted = SendInput(1, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Input>());
            Assert.True(accepted == 1, $"SendInput mouse move failed: accepted={accepted}, Win32 error={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.");
        }
    }

    private static void WaitForUiCondition(Func<bool> condition, TimeSpan timeout, string scenario, Func<string> diagnostic, TimeSpan? pollInterval = null)
    {
        if (condition()) return;
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        System.Windows.Threading.DispatcherFrame frame = new();
        System.Windows.Threading.DispatcherTimer timer = new(System.Windows.Threading.DispatcherPriority.Background,
            System.Windows.Threading.Dispatcher.CurrentDispatcher)
        { Interval = pollInterval ?? TimeSpan.FromMilliseconds(20) };
        bool reached = false;
        timer.Tick += (_, _) =>
        {
            reached = condition();
            if (reached || elapsed.Elapsed >= timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        try
        {
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        finally { timer.Stop(); }
        Assert.True(reached, $"{scenario} timed out after {elapsed.Elapsed.TotalMilliseconds:F0} ms: {diagnostic()}");
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

    private static void AssertFullDashboardRosterVisible(MainWindow window)
    {
        foreach (DataGrid grid in new[] { window.DashboardView.AlliesGrid, window.DashboardView.EnemiesGrid })
        {
            ScrollViewer scroll = Assert.IsType<ScrollViewer>(FindVisualChild<ScrollViewer>(grid));
            Assert.Equal(12, grid.Items.Count);
            Assert.Equal(Visibility.Collapsed, scroll.ComputedVerticalScrollBarVisibility);
            Assert.True(scroll.ScrollableHeight <= 0.5, $"12 rows need {scroll.ScrollableHeight} extra DIP at {window.Width}x{window.Height}");
            DataGridRow last = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(11));
            Rect lastBounds = last.TransformToAncestor(grid).TransformBounds(new Rect(last.RenderSize));
            Assert.True(lastBounds.Bottom <= grid.ActualHeight + 0.5, $"Twelfth row clipped at {window.Width}x{window.Height}");
            AssertDataGridCellContentsStayInside(grid, window.Width, window.Height);
        }
    }

    private static void AssertDashboardSkillLabels(MainWindow window, string language)
    {
        string[] expected = language == "zh-cn"
            ? new[] { "较差", "偏低", "平均", "良好", "很好", "优秀", "顶尖", "超级顶尖" }
            : new[] { "Bad", "Below average", "Average", "Good", "Very good", "Great", "Unicum", "Super unicum" };
        TextBlock[] labels = FindVisualChildren<TextBlock>(window.DashboardView.AlliesGrid)
            .Where(text => text.Name == "PerformanceLabel" && text.IsVisible).ToArray();
        Assert.All(expected, grade => Assert.Contains(labels, text => text.Text == grade));
        Assert.All(labels, label =>
        {
            Assert.False(int.TryParse(label.Text, out _));
            Assert.True(label.ActualHeight + 0.5 >= label.DesiredSize.Height, $"Skill label '{label.Text}' is clipped.");
        });
        Assert.Contains(FindVisualChildren<TextBlock>(window.DashboardView.EnemiesGrid),
            text => text.Name == "PerformanceLabel" && text.Text == "—");
    }

    private static void AssertSelectorText(ComboBox combo)
    {
        combo.ApplyTemplate();
        ContentPresenter presenter = Assert.IsType<ContentPresenter>(combo.Template.FindName("SelectionContent", combo));
        string expected = combo.SelectedItem switch
        {
            ListItem item => item.Content.ToString() ?? "",
            HistoryFilterOption item => item.Display,
            _ => combo.SelectedItem?.ToString() ?? ""
        };
        Assert.Contains(FindVisualChildren<TextBlock>(presenter), text => text.Text == expected);
        combo.Focus();
        Assert.True(combo.IsKeyboardFocusWithin);
        combo.IsDropDownOpen = true;
        combo.UpdateLayout();
        Assert.True(combo.IsDropDownOpen);
        combo.IsDropDownOpen = false;
    }

    private static void ValidateDashboardScaling(MainWindow window, string language)
    {
        FrameworkElement root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        Transform previous = root.LayoutTransform;
        try
        {
            foreach ((int width, int height) in new[] { (1280, 720), (1366, 768), (1600, 900), (1920, 1080) })
                foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
                {
                    window.Width = width;
                    window.Height = height;
                    root.LayoutTransform = new ScaleTransform(scale, scale);
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var view = window.DashboardView;
                    Assert.True(view.AlliesGrid.ActualHeight >= 46, $"No readable row: {width}x{height}/{scale}");
                    Assert.Equal(view.AlliesGrid.RowHeight, view.EnemiesGrid.RowHeight);
                    Assert.True(view.AllyContextColumn.ActualWidth >= 90);
                    Assert.True(view.AllyShipColumn.ActualWidth >= 112);
                    Assert.True(view.AlliesGrid.Columns[0].ActualWidth >= 210);
                    Assert.True(view.HelpNavButton.IsVisible);
                    Assert.True(view.UpdateNavButton.IsVisible);
                    Assert.NotEmpty(System.Windows.Automation.AutomationProperties.GetName(view.HelpNavButton));
                    Rect help = view.HelpNavButton.TransformToAncestor(view).TransformBounds(new Rect(view.HelpNavButton.RenderSize));
                    Assert.InRange(help.Right, 0, view.SidebarColumn.ActualWidth + 0.5);
                    Button settings = Assert.IsType<Button>(view.NavigationButtons.Children[4]);
                    Rect settingsBounds = settings.TransformToAncestor(view).TransformBounds(new Rect(settings.RenderSize));
                    Rect sessionBounds = view.CompactSessionButton.TransformToAncestor(view).TransformBounds(new Rect(view.CompactSessionButton.RenderSize));
                    if (view.CompactSessionButton.IsVisible) Assert.True(settingsBounds.Bottom <= sessionBounds.Top + 0.5);
                    FrameworkElement title = Assert.IsAssignableFrom<FrameworkElement>(((Grid)VisualTreeHelper.GetParent(view.AllyAll.Parent)).Children[0]);
                    AssertElementsDoNotOverlap(window, title, view.AllyAll, width, height);
                    DataGridRow row = Assert.IsType<DataGridRow>(view.AlliesGrid.ItemContainerGenerator.ContainerFromIndex(0));
                    FrameworkElement content = Assert.IsAssignableFrom<FrameworkElement>(view.AllyContextColumn.GetCellContent(row));
                    TextBlock number = Assert.Single(FindVisualChildren<TextBlock>(content), t => t.Text == "60.0%");
                    Assert.True(number.FontSize >= 14);
                    Assert.True(number.ActualWidth + 0.5 >= number.DesiredSize.Width);
                    AssertDataGridCellContentsStayInside(view.AlliesGrid, width, height);
                    AssertDataGridCellContentsStayInside(view.EnemiesGrid, width, height);
                    if (scale == 1 && height >= 900)
                    {
                        AssertFullDashboardRosterVisible(window);
                        AssertDashboardSkillLabels(window, language);
                    }
                    SaveWindowSnapshot(window, $"dashboard-{language}-{width}x{height}-{scale.ToString("0.##", CultureInfo.InvariantCulture)}x.png");
                }
        }
        finally { root.LayoutTransform = previous; }
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

    private static void AssertRosterScrollBehavior(DataGrid dataGrid, ScrollViewer scrollViewer)
    {
        var headers = FindVisualChild<System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter>(dataGrid);
        double requiredHeight = dataGrid.Items.Count * dataGrid.RowHeight
            + (headers?.ActualHeight ?? 0)
            + dataGrid.BorderThickness.Top
            + dataGrid.BorderThickness.Bottom
            + 1;
        Visibility expected = dataGrid.ActualHeight + 0.5 >= requiredHeight
            ? Visibility.Collapsed
            : Visibility.Visible;

        Assert.True(scrollViewer.ComputedVerticalScrollBarVisibility == expected,
            $"Roster scrollbar should be {expected} when the grid has {dataGrid.ActualHeight:F1} DIP for {requiredHeight:F1} DIP of rows.");
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
                Assert.True(bounds.Top >= -1 && bounds.Bottom <= cell.ActualHeight + 1,
                    $"{content.GetType().Name} '{(content as TextBlock)?.Text}' is vertically clipped in roster column {cell.Column?.DisplayIndex} " +
                    $"at {width}x{height}: {bounds} outside height {cell.ActualHeight:0.##}.");
                if (content is TextBlock text && text.TextTrimming == TextTrimming.None)
                {
                    Assert.True(text.DesiredSize.Height <= cell.ActualHeight + 1,
                        $"Text '{text.Text}' needs {text.DesiredSize.Height:0.##} DIP but the cell provides {cell.ActualHeight:0.##} DIP.");
                }
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

    private static void ValidateNoteEditor(string language)
    {
        string previous = ApeRadar.Properties.Settings.Default.NoteQuickOptions;
        try
        {
            ApeRadar.Properties.Settings.Default.NoteQuickOptions = "";
            NoteEditWindow window = new("ExamplePlayer");
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Assert.Equal(4, window.QuickOptionsPanel.Children.Count);
            Assert.All(window.QuickOptionsPanel.Children.OfType<Button>(), button => Assert.True(button.ActualHeight >= 28));
            Assert.Equal(Visibility.Collapsed, window.QuickOptionsEditor.Visibility);
            SaveWindowSnapshot(window, $"note-editor-{language}.png");
            window.Close();
        }
        finally
        {
            ApeRadar.Properties.Settings.Default.NoteQuickOptions = previous;
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
        window.ComboBoxAPIType.SelectedIndex = 0;
        window.ComboBoxMainInterfaceStyle.SelectedValue = "Dashboard";
        window.ComboBoxSoftwareUpdateChannel.SelectedValue = SoftwareReleaseSelector.StableSettingValue;
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        window.ConfigTabs.SelectedIndex = 0;
        window.UpdateLayout();
        TextBlock version = Assert.Single(FindVisualChildren<TextBlock>(window), text => text.Inlines.OfType<System.Windows.Documents.Run>()
            .Any(run => run.GetBindingExpression(System.Windows.Documents.Run.TextProperty)?.ParentBinding.Path?.Path == "SoftwareDate"));
        Rect versionEnd = version.Inlines.OfType<System.Windows.Documents.Run>().Last().ContentEnd
            .GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
        Assert.False(versionEnd.IsEmpty);
        Assert.InRange(versionEnd.Right, 0, version.ActualWidth + 1);
        Assert.InRange(versionEnd.Bottom, 0, version.ActualHeight + 1);
        window.ConfigTabs.SelectedIndex = window.ConfigTabs.Items.Count - 1;
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

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static SessionSummary CreateOneBattleSession()
    {
        BattleRecord battle = new()
        {
            Id = 1,
            BattleKey = "ui-sample",
            StartedAt = DateTimeOffset.Now,
            Server = "ASIA",
            AccountId = "1",
            AccountName = "Sample",
            ShipId = "101",
            ShipName = "Yamato",
            ShipType = "Battleship",
            BattleCount = 1,
            Result = BattleResult.Win,
            WinCount = 1,
            Damage = 100_000,
            Frags = 1,
            Completeness = BattleCompleteness.Complete
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
