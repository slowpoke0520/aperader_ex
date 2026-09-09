using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ApeRadar.History;
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
                }
                app.Shutdown();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The history window constructor did not complete in time.");
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

    private static SessionSummary CreateOneBattleSession()
    {
        BattleRecord battle = new()
        {
            Id = 1, BattleKey = "ui-sample", StartedAt = DateTimeOffset.Now, Server = "ASIA", AccountId = "1",
            AccountName = "Sample", ShipId = "101", ShipName = "Yamato", BattleCount = 1, Result = BattleResult.Win,
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
        Directory.CreateDirectory(directory);
        int bitmapWidth = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int bitmapHeight = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        RenderTargetBitmap bitmap = new(bitmapWidth, bitmapHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(Path.Combine(directory, $"history-{language}-{width:0}x{height:0}-tab-{tab}.png"));
        encoder.Save(stream);
    }
}
