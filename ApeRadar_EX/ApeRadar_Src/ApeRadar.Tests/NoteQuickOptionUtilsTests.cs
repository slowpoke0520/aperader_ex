using ApeRadar.Utils;
using Xunit;

namespace ApeRadar.Tests;

public sealed class NoteQuickOptionUtilsTests
{
    [Fact]
    public void EmptySetting_UsesLocalizedDefaults()
    {
        IReadOnlyList<string> options = NoteQuickOptionUtils.GetOptions("");

        Assert.Equal(4, options.Count);
        Assert.All(options, option => Assert.False(string.IsNullOrWhiteSpace(option)));
    }

    [Fact]
    public void CustomOptions_AreTrimmedDeduplicatedAndLimited()
    {
        string serialized = NoteQuickOptionUtils.Serialize(new[]
        {
            " Reliable ", "reliable", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"
        });
        IReadOnlyList<string> options = NoteQuickOptionUtils.GetOptions(serialized);

        Assert.Equal(NoteQuickOptionUtils.MaximumOptionCount, options.Count);
        Assert.Equal("Reliable", options[0]);
        Assert.Single(options, option => option.Equals("reliable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExplicitEmptyList_DisablesQuickOptions()
    {
        Assert.Empty(NoteQuickOptionUtils.GetOptions("[]"));
    }

    [Fact]
    public void BadgeText_CollapsesLinesAndTruncatesLongNotes()
    {
        string text = NoteQuickOptionUtils.ToBadgeText("Needs\nattention for positioning");

        Assert.DoesNotContain('\n', text);
        Assert.EndsWith("…", text);
        Assert.True(new System.Globalization.StringInfo(text[..^1]).LengthInTextElements <= NoteQuickOptionUtils.MaximumBadgeLength);
    }
}
