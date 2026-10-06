using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ApeRadar.Controls;
using ApeRadar.Models;
using ApeRadar.ViewModels;
using Xunit;

namespace ApeRadar.Tests;

internal static class PlayerDetailCardLayoutAssertions
{
    internal static void Verify(string language)
    {
        Player player = new("Chihaya_Anon", Server.ASIA, "1", "3760142160")
        {
            ClanTag = "[TPY]", ShipName = "雷神", ShipTier = 10,
            Battles = 5915, Battles_Solo = 3319, Battles_Div2 = 773, Battles_Div3 = 1823,
            AccountWinrate = 0.5726, AccountWinrate_Solo = 0.5438,
            AccountWinrate_Div2 = 0.5877, AccountWinrate_Div3 = 0.5610,
            AvgExpPerBattle = 1372, AvgExpPerBattle_Solo = 1334,
            AvgExpPerBattle_Div2 = 1208, AvgExpPerBattle_Div3 = 1510,
            ShipBattles = 32, ShipBattles_Solo = 5, ShipBattles_Div2 = 10, ShipBattles_Div3 = 17,
            ShipWinrate = 0.50, ShipWinrate_Solo = 0.40,
            ShipWinrate_Div2 = 0.50, ShipWinrate_Div3 = 11d / 17,
            ShipAvgDmgPerBattle = 17297, ShipAvgDmgPerBattle_Solo = 71118,
            ShipAvgDmgPerBattle_Div2 = 36565, ShipAvgDmgPerBattle_Div3 = 37191,
            ShipAvgExpPerBattle = 1189, ShipAvgExpPerBattle_Solo = 841,
            ShipAvgExpPerBattle_Div2 = 935, ShipAvgExpPerBattle_Div3 = 1442,
            PR = 1818, ShipPR = 1434
        };
        VerifyPlayer(player, language, "sample");
        player.AccountWinrate = 1;
        player.AccountWinrate_Solo = 0;
        player.AccountWinrate_Div2 = -1;
        player.AccountWinrate_Div3 = 0.9999;
        player.ShipWinrate = 1;
        player.ShipWinrate_Solo = 0;
        player.ShipWinrate_Div2 = -1;
        player.ShipWinrate_Div3 = 0.9999;
        player.Battles = player.Battles_Solo = player.Battles_Div2 = player.Battles_Div3 = 100000;
        player.ShipAvgDmgPerBattle = player.ShipAvgDmgPerBattle_Solo =
            player.ShipAvgDmgPerBattle_Div2 = player.ShipAvgDmgPerBattle_Div3 = 123456;
        VerifyPlayer(player, language, "limits");
    }

    private static void VerifyPlayer(Player player, string language, string scenario)
    {
        PlayerDetailCard card = new()
        {
            DataContext = new PlayerDetailCardViewModel(player, Array.Empty<RosterStatusBadgeViewModel>()),
            FontFamily = (FontFamily)Application.Current.FindResource("AppFontFamily"), FontSize = 12
        };
        Border border = new()
        {
            Width = 560, Padding = new Thickness(10), BorderThickness = new Thickness(1),
            Language = XmlLanguage.GetLanguage(language),
            Background = (Brush)Application.Current.FindResource("SurfaceBrush"),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = card
            }
        };
        TextOptions.SetTextFormattingMode(border, TextFormattingMode.Display);
        Border host = new() { Child = border };
        foreach (double fontSize in new[] { 12d, 14d })
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
        foreach (double popupWidth in new[] { 560d, 360d, 480d, 720d })
        {
            card.FontSize = fontSize;
            border.Width = popupWidth;
            border.MaxHeight = Math.Min(720, Math.Max(300, 720 / scale * 0.70));
            border.LayoutTransform = new ScaleTransform(scale, scale);
            host.Width = popupWidth * scale;
            Layout(host);
            AssertCompleteNumbers(card, player, language, $"{scenario}, {popupWidth} DIP, font {fontSize}, scale {scale}");
            if (fontSize == 12 && scenario == "sample" && (scale == 1 || popupWidth == 560))
                SaveSnapshot(host, $"player-detail-{language}-{popupWidth:0}-{scale:0.##}x.png");
        }
    }

    internal static void AssertCompleteNumbers(PlayerDetailCard card, Player player, string language, string scenario)
    {
        TextBlock[] winrates = Descendants(card).OfType<TextBlock>()
            .Where(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path.Contains("Winrate") == true)
            .Where(text => text.IsVisible || text.ActualHeight > 0).ToArray();
        Assert.Equal(8, winrates.Length);
        foreach (TextBlock text in winrates)
        {
            string path = BindingOperations.GetBinding(text, TextBlock.TextProperty)!.Path.Path;
            string propertyName = path["Player.".Length..];
            double value = (double)typeof(Player).GetProperty(propertyName)!.GetValue(player)!;
            Assert.Equal(value < 0 ? "-" : value.ToString("P2", CultureInfo.GetCultureInfo(language)), text.Text);
        }
        foreach (TextBlock text in Descendants(card).OfType<TextBlock>()
            .Where(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path.StartsWith("Player.") == true)
            .Where(text => text.ActualHeight > 0))
        {
            TextBlock fullText = new()
            {
                Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
                FontStyle = text.FontStyle, FontWeight = text.FontWeight, FontStretch = text.FontStretch,
                Language = text.Language, FlowDirection = text.FlowDirection, UseLayoutRounding = text.UseLayoutRounding
            };
            TextOptions.SetTextFormattingMode(fullText, TextOptions.GetTextFormattingMode(text));
            Typography.SetNumeralAlignment(fullText, Typography.GetNumeralAlignment(text));
            fullText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double availableWidth = LayoutInformation.GetLayoutSlot(text).Width - text.Margin.Left - text.Margin.Right;
            Assert.True(availableWidth + 0.5 >= fullText.DesiredSize.Width,
                $"{language} {scenario}: '{text.Text}' needs {fullText.DesiredSize.Width:F1} DIP, but its cell has {availableWidth:F1} DIP.");
        }
    }

    private static void Layout(Border border)
    {
        for (int i = 0; i < 3; i++)
        {
            border.Measure(new Size(border.Width, double.PositiveInfinity));
            border.Arrange(new Rect(0, 0, border.Width, border.DesiredSize.Height));
            border.UpdateLayout();
            border.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
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

    private static void SaveSnapshot(FrameworkElement element, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("APERADAR_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        RenderTargetBitmap bitmap = new((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }
}
