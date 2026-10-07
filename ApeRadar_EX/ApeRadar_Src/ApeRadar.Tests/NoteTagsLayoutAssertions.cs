using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ApeRadar.Controls;
using ApeRadar.History;
using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

internal static class NoteTagsLayoutAssertions
{
    private const string ShortNote = "甲 乙 丙";
    private sealed record RosterBaseline(double Height, Rect NameBounds, Rect WatchBounds);

    internal static void Verify(string language, HistoryServices historyServices)
    {
        Assert.NotNull(Application.Current);
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        var settings = ApeRadar.Properties.Settings.Default;
        string[] settingNames =
        {
            "NoteQuickOptions", "MainInterfaceStyle", "PlayerNamesVisibility", "RosterDisplayDensity",
            "PlayerColumnFontSize", "StatisticsColumnFontSize", "ShowTierPerformanceStats",
            "ShowLegacyPerformanceTag", "ShowAccountRosterColumn", "ShowShipRosterColumn",
            "ShowPerformanceRosterColumn", "AccountWinrateVisibility", "AccountAvgExpVisibility",
            "WeightedWinrateVisibility", "ShipWinrateVisibility", "ShipAvgDmgVisibility",
            "ShipAvgExpVisibility", "PRVisibility", "TagVisibility"
        };
        SettingsSnapshot before = new(settings);
        try
        {
            settings.NoteQuickOptions = NoteQuickOptionUtils.Serialize(new[] { "Quick addition" });
            settings.PlayerNamesVisibility = true;
            settings.RosterDisplayDensity = "Standard";
            settings.PlayerColumnFontSize = 18;
            settings.StatisticsColumnFontSize = 16;
            settings.ShowTierPerformanceStats = false;
            settings.ShowLegacyPerformanceTag = false;
            settings.ShowAccountRosterColumn = settings.ShowShipRosterColumn = settings.ShowPerformanceRosterColumn = true;
            settings.AccountWinrateVisibility = settings.WeightedWinrateVisibility = settings.ShipWinrateVisibility = 0;
            settings.AccountAvgExpVisibility = settings.ShipAvgExpVisibility = settings.TagVisibility = 2;
            settings.ShipAvgDmgVisibility = settings.PRVisibility = 0;

            VerifyEditor(language);
            VerifyCompactOverflow(language);
            VerifyTwoRowCompact(language);
            VerifyDetail(language);
            VerifyRoster("Dashboard", language, historyServices);
            VerifyRoster("Legacy", language, historyServices);
            VerifyWatchLists(language);
        }
        finally
        {
            // These are in-memory test settings only. Never call Save or a settings-management button.
            before.Restore(settings, settingNames);
        }
    }

    private static void VerifyEditor(string language)
    {
        NoteEditWindow window = new("Temporary note preview player");
        try
        {
            window.Show();
            const string original = " \t甲  乙\r\n丙 \t ";
            window.NoteText = original;
            Layout(window);
            Assert.Equal(original, window.NoteText);
            Assert.Equal(original, window.NoteTagsPreview.Note);
            Assert.Equal(original, window.NoteTagsPreview.ToolTip);
            Assert.False(window.NoteTagsPreview.Compact);
            Assert.Equal(int.MaxValue, window.NoteTagsPreview.MaximumVisibleTags);
            AssertTags(window.NoteTagsPreview, new[] { "甲", "乙", "丙" }, $"{language} editor live preview");

            window.NoteText = "One Two Three Four Five Six Seven";
            Layout(window);
            AssertTags(window.NoteTagsPreview, new[] { "One", "Two", "Three", "Four", "Five", "Six", "Seven" }, $"{language} six-color cycle");
            NoteTag[] seven = Tags(window.NoteTagsPreview);
            Assert.Equal(7, seven.Length);
            Assert.Equal(6, seven.Take(6).Select(tag => tag.BackgroundColor).Distinct().Count());
            Assert.Equal(seven[0].BackgroundColor, seven[6].BackgroundColor);
            Assert.Equal(seven[0].ForegroundColor, seven[6].ForegroundColor);

            window.NoteText = " \t\r\n  ";
            Layout(window);
            Assert.Equal(Visibility.Collapsed, window.NoteTagsPreview.Visibility);
            Assert.Empty(Tags(window.NoteTagsPreview));

            Button quickOption = Assert.Single(window.QuickOptionsPanel.Children.OfType<Button>());
            foreach ((string existing, string expected) in new[]
            {
                ("", "Quick addition"),
                (" \t\r\n ", "Quick addition"),
                ("可靠队友 输出稳定", "可靠队友 输出稳定 Quick addition"),
                ("可靠队友 输出稳定 \t\r\n  ", "可靠队友 输出稳定 Quick addition"),
                ("可靠队友\n输出稳定", "可靠队友\n输出稳定 Quick addition")
            })
            {
                window.NoteText = existing;
                window.TxtNote.SelectAll();
                quickOption.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Layout(window);
                Assert.Equal(expected, window.NoteText);
                Assert.Equal(expected, window.NoteTagsPreview.Note);
                Assert.Equal(expected.Length, window.TxtNote.CaretIndex);
            }
            AssertTags(window.NoteTagsPreview, new[] { "可靠队友", "输出稳定", "Quick", "addition" }, $"{language} quick option appends tags");

            quickOption.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.Equal("可靠队友\n输出稳定 Quick addition Quick addition", window.NoteText);
            Assert.Equal(Visibility.Collapsed, window.QuickOptionsEditor.Visibility);
        }
        finally { window.Close(); }
    }

    private static void VerifyCompactOverflow(string language)
    {
        NoteTagsControl tags = new() { Note = "甲 乙 丙 丁 戊", Compact = true, MaximumVisibleTags = 3 };
        Border host = new() { Width = 220, Child = tags };
        LayoutDetached(host);
        AssertTags(tags, new[] { "甲", "乙", "丙" }, $"{language} compact tag limit", requireVisible: false);
        Assert.Equal("+2", tags.OverflowText.Text);
        Assert.Equal(Visibility.Visible, tags.OverflowText.Visibility);
        Assert.Equal(tags.Note, tags.ToolTip);

        host.Width = 48;
        LayoutDetached(host);
        Assert.InRange(Tags(tags).Length, 0, 2);
        Assert.Equal($"+{5 - Tags(tags).Length}", tags.OverflowText.Text);
        Assert.Equal(Visibility.Visible, tags.OverflowText.Visibility);
        AssertInside(tags.OverflowText, tags, $"{language} narrow overflow");
        AssertTextFits(tags.OverflowText, $"{language} narrow overflow");
        foreach (Border badge in TagBorders(tags)) AssertInside(badge, tags, $"{language} narrow tag");
        Assert.Equal("甲 乙 丙 丁 戊", tags.Note);

        host.Width = 220;
        LayoutDetached(host);
        Assert.Equal(3, Tags(tags).Length);
        Assert.Equal("+2", tags.OverflowText.Text);
    }

    private static void VerifyTwoRowCompact(string language)
    {
        const string note = "可靠队友 输出稳定 支援及时 偏好远程";
        NoteTagsControl tags = new() { Note = note, Compact = true, CompactRows = 2, FontSize = 11, MaximumVisibleTags = 6 };
        Border host = new() { Width = 98, Height = 38, Child = tags };
        LayoutDetached(host);
        AssertTags(tags, new[] { "可靠队友", "输出稳定" }, $"{language} two-row compact tags", requireVisible: false);
        Assert.Equal("+2", tags.OverflowText.Text);
        Assert.Equal(2, TagBorders(tags).Select(border => Math.Round(Bounds(border, tags).Top)).Distinct().Count());
        AssertInside(tags.OverflowText, tags, $"{language} two-row overflow");
        Assert.Equal(38, host.ActualHeight);

        host.Height = 18;
        LayoutDetached(host);
        AssertTags(tags, new[] { "可靠队友" }, $"{language} one-row height budget", requireVisible: false);
        Assert.Equal("+3", tags.OverflowText.Text);

        host.Width = 60;
        host.Height = 38;
        LayoutDetached(host);
        AssertTags(tags, new[] { "可靠队友" }, $"{language} narrow two-row note area", requireVisible: false);
        Assert.Equal("+3", tags.OverflowText.Text);
        AssertInside(tags.OverflowText, tags, $"{language} narrow two-row overflow");
        AssertNoOverlap(Assert.Single(TagBorders(tags)), tags.OverflowText, tags, $"{language} narrow two-row overflow");
        Assert.Equal(38, host.ActualHeight);

        host.Width = 122;
        host.Height = 38;
        tags.MaximumVisibleTags = 3;
        LayoutDetached(host);
        AssertTags(tags, new[] { "可靠队友", "输出稳定", "支援及时" }, $"{language} counter shares last tag row", requireVisible: false);
        Assert.Equal("+1", tags.OverflowText.Text);
        Assert.Equal(2, TagBorders(tags).Select(border => Math.Round(Bounds(border, tags).Top)).Distinct().Count());
        foreach (Border badge in TagBorders(tags)) AssertNoOverlap(badge, tags.OverflowText, tags, $"{language} inline overflow counter");

        host.Width = 220;
        host.Height = 38;
        tags.MaximumVisibleTags = 6;
        LayoutDetached(host);
        AssertTags(tags, new[] { "可靠队友", "输出稳定", "支援及时", "偏好远程" }, $"{language} expanded tag width", requireVisible: false);
        Assert.Equal(Visibility.Collapsed, tags.OverflowText.Visibility);
        Assert.Equal(note, tags.Note);
    }

    private static void VerifyDetail(string language)
    {
        const string original = "甲  乙\r\n丙 ThisLabelMustRemainComplete 第五项 第六项 第七项";
        Player player = CreatePlayer("DetailNotePlayer", "1", original);
        PlayerDetailCard card = new() { DataContext = new PlayerDetailCardViewModel(player, Array.Empty<RosterStatusBadgeViewModel>()) };
        ScrollViewer scroll = new() { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Window window = new() { Content = scroll, Width = 560, Height = 680, ShowInTaskbar = false };
        try
        {
            window.Show();
            Layout(window);
            scroll.ScrollToEnd();
            Layout(window);
            NoteTagsControl tags = Assert.Single(Descendants(card).OfType<NoteTagsControl>());
            Assert.False(tags.Compact);
            Assert.Equal(int.MaxValue, tags.MaximumVisibleTags);
            AssertTags(tags, new[] { "甲", "乙", "丙", "ThisLabelMustRemainComplete", "第五项", "第六项", "第七项" }, $"{language} detail full tags");
            Assert.Equal(Visibility.Collapsed, tags.OverflowText.Visibility);
            Assert.Equal(original, player.Note);
            Assert.Equal(original, tags.ToolTip);
        }
        finally { window.Close(); }
    }

    private static void VerifyRoster(string style, string language, HistoryServices historyServices)
    {
        ApeRadar.Properties.Settings.Default.MainInterfaceStyle = style;
        MainWindow window = new(historyServices, initializeRuntime: false)
        {
            // Hosted CI desktops can clamp a Window to their native maximum tracking size.
            // Keep the requested logical viewport, rather than testing a narrower badge layout.
            WindowState = WindowState.Normal, Width = 1920, Height = 1040,
            MinWidth = 1920, MinHeight = 1040, ShowInTaskbar = false
        };
        List<Player> players = Enumerable.Range(1, 12).Select(i => CreatePlayer($"Ally{i}", "1", ""))
            .Concat(Enumerable.Range(1, 12).Select(i => CreatePlayer($"Enemy{i}", "2", ""))).ToList();
        Player ally = players[0];
        Player enemy = players[12];
        DataGrid allies = style == "Dashboard" ? window.DashboardView.AlliesGrid : window.DataGridAlliesList;
        DataGrid enemies = style == "Dashboard" ? window.DashboardView.EnemiesGrid : window.DataGridEnemiesList;
        IEnumerable? previousAllies = allies.ItemsSource;
        IEnumerable? previousEnemies = enemies.ItemsSource;
        void Present()
        {
            if (style == "Dashboard")
            {
                Battlefield battlefield = (Battlefield)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Battlefield));
                battlefield.Allies = players.Take(12).ToList();
                battlefield.Enemies = players.Skip(12).ToList();
                battlefield.BattleType = "RandomBattle";
                battlefield.BattleStartTime = DateTimeOffset.Now;
                window.Dashboard.Update(battlefield, true, new DashboardBattleMetadata(
                    "Test map", "Random battle", "ASIA", battlefield.BattleStartTime, "Vortex", DateTimeOffset.Now));
            }
            else
            {
                RosterPresentationService presentation = new();
                RosterPresentationOptions options = RosterPresentationOptions.FromCurrentSettings();
                allies.ItemsSource = presentation.CreateRows(players.Take(12), options);
                enemies.ItemsSource = presentation.CreateRows(players.Skip(12), options);
            }
            Layout(window);
        }
        try
        {
            if (style == "Legacy") window.RefreshDataGridColumns(mirrored: false);
            Present();
            window.WindowState = WindowState.Normal;
            window.Show();
            Layout(window);
            Assert.Equal(1920d, window.ActualWidth, 1);
            Assert.Equal(1040d, window.ActualHeight, 1);
            RosterBaseline allyBaseline = CaptureBaseline(RealizeRow(allies, ally, window), ally, style, language);
            RosterBaseline enemyBaseline = CaptureBaseline(RealizeRow(enemies, enemy, window), enemy, style, language);
            double rowHeight = allies.RowHeight;
            ally.Note = enemy.Note = ShortNote;
            Present();
            Assert.Equal(rowHeight, allies.RowHeight, 2);
            AssertRosterRow(RealizeRow(allies, ally, window), ally, allyBaseline, style, $"{language} {style} ally 1920x1040");
            AssertRosterRow(RealizeRow(enemies, enemy, window), enemy, enemyBaseline, style, $"{language} {style} enemy 1920x1040");
            Assert.Equal(ShortNote, ally.Note);
            Assert.Equal(ShortNote, enemy.Note);
        }
        finally
        {
            allies.ItemsSource = previousAllies;
            enemies.ItemsSource = previousEnemies;
            window.Close();
        }
    }

    private static RosterBaseline CaptureBaseline(DataGridRow row, Player player, string style, string language)
    {
        Border watch = WatchBadge(row);
        TextBlock name = PlayerName(row, player);
        Rect nameBounds = Bounds(name, row);
        Rect watchBounds = Bounds(watch, row);
        if (style == "Dashboard") AssertNoGlyphOverlap(name, watch, row, $"{language} Dashboard no-note baseline");
        else AssertNoOverlap(name, watch, row, $"{language} Legacy no-note baseline");
        return new RosterBaseline(row.ActualHeight, nameBounds, watchBounds);
    }

    private static void AssertRosterRow(DataGridRow row, Player player, RosterBaseline baseline, string style, string scenario)
    {
        Assert.Equal(baseline.Height, row.ActualHeight, 2);
        NoteTagsControl tags = Assert.Single(Descendants(row).OfType<NoteTagsControl>(), control => control.Note == ShortNote);
        AssertTags(tags, new[] { "甲", "乙", "丙" }, scenario);
        Assert.Equal(Visibility.Collapsed, tags.OverflowText.Visibility);
        DataGridCell cell = Ancestor<DataGridCell>(tags);
        foreach (Border badge in TagBorders(tags)) AssertInside(badge, cell, scenario);

        Border watch = WatchBadge(row);
        Assert.True(watch.IsVisible && watch.ActualWidth > 0, $"{scenario}: Watch badge is hidden.");
        AssertInside(watch, cell, scenario);
        AssertNoOverlap(watch, Ancestor<Border>(tags), row, scenario);
        TextBlock name = PlayerName(cell, player);
        Assert.True(name.IsVisible && name.ActualWidth >= 24, $"{scenario}: player name lost its readable space.");
        Assert.Contains(player.Name, name.Text);
        AssertInside(name, cell, scenario);
        if (style == "Dashboard")
        {
            bool baselineOverlap = Overlap(baseline.NameBounds, baseline.WatchBounds);
            Assert.True(!Overlap(Bounds(name, row), Bounds(watch, row)) || baselineOverlap,
                $"{scenario}: a new name/Watch render-box overlap appeared; no-note baseline name={baseline.NameBounds}, Watch={baseline.WatchBounds}.");
            string diagnostic = $"{scenario}; no-note name={baseline.NameBounds}, Watch={baseline.WatchBounds}, renderOverlap={baselineOverlap}";
            // Dashboard uses different grid rows. TextBlock's transparent layout box may extend
            // into the next row; inspect the actually drawn glyph geometry, not that empty box.
            AssertNoGlyphOverlap(name, watch, row, diagnostic);
            AssertNoGlyphOverlap(name, Ancestor<Border>(tags), row, diagnostic);
        }
        else
        {
            AssertNoOverlap(name, watch, row, scenario);
            AssertNoOverlap(name, Ancestor<Border>(tags), row, scenario);
        }
    }

    private static Border WatchBadge(DependencyObject parent) => Assert.Single(Descendants(parent).OfType<Border>(),
        border => border.Name == "BadgeBorder" && border.DataContext is RosterStatusBadgeViewModel { Kind: RosterBadgeKind.Watch });

    private static TextBlock PlayerName(DependencyObject parent, Player player) => Assert.Single(Descendants(parent).OfType<TextBlock>(),
        text => Equals(text.ToolTip, player.Name));

    private static void VerifyWatchLists(string language)
    {
        ConfigWindow window = new(initializeRuntime: false) { Width = 1100, Height = 700, ShowInTaskbar = false };
        DataGrid[] grids = { window.DataGridWatchListPositive, window.DataGridWatchListNegtive, window.DataGridWatchListCheater, window.DataGridWatchListNote };
        IEnumerable?[] previous = grids.Select(grid => grid.ItemsSource).ToArray();
        try
        {
            DataTemplate template = Assert.IsType<DataTemplate>(window.FindResource("NoteTemplate"));
            window.ConfigTabs.SelectedIndex = 5;
            Player player = CreatePlayer("TemporaryWatchPlayer", "1", ShortNote);
            foreach (DataGrid grid in grids)
            {
                Assert.Same(template, Assert.Single(grid.Columns.OfType<DataGridTemplateColumn>(), column => ReferenceEquals(column.CellTemplate, template)).CellTemplate);
                grid.ItemsSource = new[] { player };
            }
            window.Show();
            Layout(window);
            foreach (DataGrid grid in grids)
            {
                DataGridColumn noteColumn = Assert.Single(grid.Columns.OfType<DataGridTemplateColumn>(), column => ReferenceEquals(column.CellTemplate, template));
                grid.ScrollIntoView(player, noteColumn);
                Layout(window);
                DataGridRow row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(player));
                NoteTagsControl tags = Assert.Single(Descendants(row).OfType<NoteTagsControl>());
                AssertTags(tags, new[] { "甲", "乙", "丙" }, $"{language} {grid.Name} shared NoteTemplate");
                foreach (Border badge in TagBorders(tags)) AssertInside(badge, Ancestor<DataGridCell>(tags), grid.Name);
            }
            Assert.Equal(ShortNote, player.Note);
        }
        finally
        {
            for (int i = 0; i < grids.Length; i++) grids[i].ItemsSource = previous[i];
            window.Close();
        }
    }

    private static Player CreatePlayer(string name, string relation, string note) => new(name, Server.ASIA, relation, "3760142160")
    {
        ID = relation + name, ShipName = "Test ship", ShipTier = 10, ShipType = "Destroyer", WatchStatus = WatchStatus.POSITIVE,
        Note = note, Battles = 8000, AccountWinrate = 0.58, WeightedWinrate = 0.57, AvgExpPerBattle = 1800,
        ShipBattles = 200, ShipWinrate = 0.55, ShipAvgDmgPerBattle = 90000, ShipAvgExpPerBattle = 1600,
        PR = 1500, ShipPR = 1400, Karma = 0
    };

    private static DataGridRow RealizeRow(DataGrid grid, Player player, Window window)
    {
        object item = Assert.Single(grid.Items.Cast<object>(), item => item switch
        {
            DashboardPlayerRowViewModel dashboard => ReferenceEquals(dashboard.Player, player),
            PlayerRosterRowViewModel legacy => ReferenceEquals(legacy.Player, player),
            _ => false
        });
        grid.ScrollIntoView(item, grid.Columns[0]);
        Layout(window);
        return Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(item));
    }

    private static NoteTag[] Tags(NoteTagsControl control) => control.TagItems.Items.Cast<NoteTag>().ToArray();

    private static Border[] TagBorders(NoteTagsControl control) => Descendants(control.TagItems).OfType<Border>()
        .Where(border => border.DataContext is NoteTag).ToArray();

    private static void AssertTags(NoteTagsControl control, string[] expected, string scenario, bool requireVisible = true)
    {
        Assert.Equal(expected, Tags(control).Select(tag => tag.Text));
        Assert.Equal(Visibility.Visible, control.Visibility);
        if (requireVisible) Assert.True(control.IsVisible, $"{scenario}: tags are hidden by an ancestor.");
        Border[] borders = TagBorders(control);
        Assert.Equal(expected.Length, borders.Length);
        if (expected.Length >= 3) Assert.Equal(3, Tags(control).Take(3).Select(tag => tag.BackgroundColor).Distinct().Count());
        foreach (Border border in borders)
        {
            NoteTag tag = Assert.IsType<NoteTag>(border.DataContext);
            TextBlock text = Assert.Single(Descendants(border).OfType<TextBlock>());
            Assert.Equal(control.Compact ? tag.CompactText : tag.Text, text.Text);
            Assert.Equal(tag.Text, border.ToolTip);
            Assert.Equal((Color)ColorConverter.ConvertFromString(tag.BackgroundColor), Assert.IsType<SolidColorBrush>(border.Background).Color);
            Assert.Equal((Color)ColorConverter.ConvertFromString(tag.ForegroundColor), Assert.IsType<SolidColorBrush>(text.Foreground).Color);
            Assert.True(border.ActualWidth > 0 && border.ActualHeight > 0, $"{scenario}: an empty tag was rendered.");
            if (control.Compact) AssertTextFits(text, scenario);
            AssertInside(border, control, scenario);
        }
        for (int i = 0; i < borders.Length; i++)
            for (int j = i + 1; j < borders.Length; j++) AssertNoOverlap(borders[i], borders[j], control, scenario);
    }

    private static void AssertInside(FrameworkElement child, FrameworkElement parent, string scenario)
    {
        Rect bounds = child.TransformToAncestor(parent).TransformBounds(new Rect(child.RenderSize));
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= parent.ActualWidth + 1 && bounds.Bottom <= parent.ActualHeight + 1,
            $"{scenario}: {child.GetType().Name} {bounds} escapes {parent.GetType().Name} {parent.ActualWidth:F1}x{parent.ActualHeight:F1}.");
    }

    private static void AssertTextFits(TextBlock text, string scenario)
    {
        TextBlock full = new()
        {
            Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
            FontStyle = text.FontStyle, FontWeight = text.FontWeight, FontStretch = text.FontStretch,
            Language = text.Language, FlowDirection = text.FlowDirection
        };
        TextOptions.SetTextFormattingMode(full, TextOptions.GetTextFormattingMode(text));
        full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(text.ActualWidth + 1 >= full.DesiredSize.Width,
            $"{scenario}: '{text.Text}' needs {full.DesiredSize.Width:F1} DIP, but has only {text.ActualWidth:F1} DIP.");
    }

    private static void AssertNoOverlap(FrameworkElement first, FrameworkElement second, FrameworkElement parent, string scenario)
    {
        Rect a = Bounds(first, parent);
        Rect b = Bounds(second, parent);
        Assert.False(Overlap(a, b), $"{scenario}: elements overlap at {a} and {b}.");
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement parent) =>
        element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));

    private static bool Overlap(Rect first, Rect second)
    {
        Rect overlap = Rect.Intersect(first, second);
        return overlap.Width > 0.5 && overlap.Height > 0.5;
    }

    private static void AssertNoGlyphOverlap(TextBlock text, FrameworkElement second, FrameworkElement parent, string scenario)
    {
        DrawingGroup? drawing = VisualTreeHelper.GetDrawing(text);
        Assert.NotNull(drawing);
        Rect local = GlyphBounds(drawing);
        Assert.True(!local.IsEmpty && local.Width > 0 && local.Height > 0, $"{scenario}: no rendered player-name glyphs were found.");
        Rect glyphs = text.TransformToAncestor(parent).TransformBounds(local);
        Rect other = Bounds(second, parent);
        Assert.False(Overlap(glyphs, other), $"{scenario}: player-name glyphs {glyphs} overlap badge {other}; text render box={Bounds(text, parent)}.");
    }

    private static Rect GlyphBounds(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) return glyph.GlyphRun.BuildGeometry().Bounds;
        Rect bounds = Rect.Empty;
        if (drawing is DrawingGroup group)
        {
            foreach (Drawing child in group.Children) bounds.Union(GlyphBounds(child));
            if (!bounds.IsEmpty && group.Transform != null) bounds = group.Transform.TransformBounds(bounds);
        }
        return bounds;
    }

    private static T Ancestor<T>(DependencyObject child) where T : FrameworkElement
    {
        for (DependencyObject? parent = VisualTreeHelper.GetParent(child); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T match) return match;
        throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} ancestor for {child.GetType().Name}.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Layout(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static void LayoutDetached(Border host)
    {
        for (int i = 0; i < 3; i++)
        {
            host.Measure(new Size(host.Width, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, host.Width, host.DesiredSize.Height));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }
}
