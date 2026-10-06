using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils.Converters;
using ApeRadar.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Xunit;

namespace ApeRadar.Tests;

public sealed class NoteTagPresentationTests
{
    [Theory]
    [InlineData("  abcdefghijk  ")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    [InlineData("  Alpha \t Beta\r\n Gamma\u3000Delta  ")]
    public void RosterNoteBadge_KeepsOriginalNoteInsteadOfCompactOrNormalizedText(string original)
    {
        Player player = CreatePlayer(original);

        PlayerRosterRowViewModel row = CreateRow(player);
        RosterStatusBadgeViewModel note = Assert.Single(row.StatusBadges, badge => badge.Kind == RosterBadgeKind.Note);

        Assert.Equal(original, note.NoteText);
        Assert.Equal(original, player.Note);
        Assert.NotEqual(note.DisplayText, note.NoteText);
        Assert.Equal(original, Assert.Single(row.Detail.StatusBadges, badge => badge.Kind == RosterBadgeKind.Note).NoteText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dashboard_WatchAndNoteAreBothVisibleWithoutCountingNoteAsOverflow(bool additionalBadges)
    {
        const string original = "  Reliable teammate\t需要复盘  ";
        Player player = CreatePlayer(original);
        player.WatchStatus = WatchStatus.POSITIVE;
        if (additionalBadges)
        {
            player.IsCustomMarked = true;
            player.RecentEncounterCount = 2;
            player.IsDataStale = true;
        }

        DashboardPlayerRowViewModel dashboard = new(1, CreateRow(player), DashboardRosterContext.Account, loadCompleted: true);

        Assert.Equal(new[] { RosterBadgeKind.Watch, RosterBadgeKind.Note }, dashboard.VisibleStatusBadges.Select(badge => badge.Kind));
        RosterStatusBadgeViewModel note = Assert.Single(dashboard.StatusBadges, badge => badge.Kind == RosterBadgeKind.Note);
        Assert.Same(note, Assert.Single(dashboard.VisibleStatusBadges, badge => badge.Kind == RosterBadgeKind.Note));
        Assert.Equal(original, note.NoteText);
        Assert.Equal(additionalBadges ? 5 : 2, dashboard.StatusBadges.Count);
        Assert.Equal(additionalBadges ? 3 : 0, dashboard.OverflowBadgeCount);
        Assert.Equal(additionalBadges ? "+3" : "", dashboard.OverflowBadgeText);
    }

    [Theory]
    [InlineData(false, 0, "")]
    [InlineData(true, 1, "+1")]
    public void Dashboard_WhenNoteIsFirstPriorityItAppearsOnlyOnce(bool customMarker, int overflow, string overflowText)
    {
        Player player = CreatePlayer("One Two Three");
        player.IsCustomMarked = customMarker;

        DashboardPlayerRowViewModel dashboard = new(1, CreateRow(player), DashboardRosterContext.Account, loadCompleted: true);

        Assert.Equal(RosterBadgeKind.Note, Assert.Single(dashboard.VisibleStatusBadges).Kind);
        Assert.Single(dashboard.StatusBadges, badge => badge.Kind == RosterBadgeKind.Note);
        Assert.Equal(overflow, dashboard.OverflowBadgeCount);
        Assert.Equal(overflowText, dashboard.OverflowBadgeText);
    }

    [Theory]
    [InlineData(0, 250d, "")]
    [InlineData(1, 250d, "")]
    [InlineData(2, 250d, "+1")]
    [InlineData(5, 249.99d, "+5")]
    [InlineData(5, 250d, "+4")]
    [InlineData(5, 400d, "+4")]
    [InlineData(0, 249.99d, "")]
    [InlineData(-2, 400d, "")]
    public void OverflowConverter_TwoParametersPreserveOriginalSingleVisibleBadgeBehavior(int count, double width, string expected)
    {
        Assert.Equal(expected, ConvertOverflow(count, width));
    }

    [Theory]
    [InlineData(5, 250d, 2, "+3")]
    [InlineData(5, 249.99d, 2, "+5")]
    [InlineData(2, 250d, 2, "")]
    [InlineData(5, 250d, 0, "+5")]
    [InlineData(2, 250d, 3, "")]
    [InlineData(0, 250d, 2, "")]
    [InlineData(5, 250d, 1, "+4")]
    public void OverflowConverter_ThreeParametersSubtractActualDisplayedBadgeCount(int count, double width, int displayed, string expected)
    {
        Assert.Equal(expected, ConvertOverflow(count, width, displayed));
    }

    [Fact]
    public void OverflowConverter_InvalidRequiredBindingsOrArityReturnEmpty()
    {
        object[][] invalid =
        {
            Array.Empty<object>(),
            new object[] { 5 },
            new object[] { 5, 250d, 2, 0 },
            new object[] { "5", 250d },
            new object[] { 5d, 250d },
            new object[] { 5, 250 },
            new object[] { 5, "250" },
            new object[] { null!, 250d },
            new object[] { 5, null! },
            new object[] { DependencyProperty.UnsetValue, 250d, 2 },
            new object[] { 5, Binding.DoNothing, 2 }
        };

        foreach (object[] values in invalid) Assert.Equal("", ConvertOverflow(values));
    }

    [Fact]
    public void OverflowConverter_InvalidOptionalDisplayedCountFallsBackToTwoParameterBehavior()
    {
        object[] invalidDisplayedCounts = { null!, "2", 2d, DependencyProperty.UnsetValue, Binding.DoNothing };

        foreach (object displayed in invalidDisplayedCounts)
        {
            Assert.Equal(ConvertOverflow(5, 250d), ConvertOverflow(5, 250d, displayed));
            Assert.Equal(ConvertOverflow(5, 249.99d), ConvertOverflow(5, 249.99d, displayed));
        }
    }

    private static string ConvertOverflow(params object[] values) =>
        Assert.IsType<string>(new DashboardBadgeOverflowConverter().Convert(values, typeof(string), null!, CultureInfo.InvariantCulture));

    private static PlayerRosterRowViewModel CreateRow(Player player) =>
        Assert.Single(new RosterPresentationService().CreateRows(new[] { player }, new RosterPresentationOptions(0, 0, 0, 0, 0, 0, 0, false)));

    private static Player CreatePlayer(string note) => new("Note test player", "12345", Server.ASIA, WatchStatus.NONE)
    {
        Relation = "1",
        ShipID = "3760142160",
        ShipName = "Yamato",
        ShipType = "Battleship",
        ShipTier = 10,
        Note = note,
        Battles = 8000,
        AccountWinrate = 0.58,
        WeightedWinrate = 0.55,
        AvgExpPerBattle = 2100,
        PR = 1650,
        ShipBattles = 420,
        ShipWinrate = 0.56,
        ShipAvgDmgPerBattle = 106000,
        ShipAvgExpPerBattle = 2200,
        ShipPR = 1520
    };
}
