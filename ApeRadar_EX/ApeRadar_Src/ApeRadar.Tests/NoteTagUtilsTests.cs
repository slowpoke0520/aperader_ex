using ApeRadar.Models;
using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Xunit;

namespace ApeRadar.Tests;

public sealed class NoteTagUtilsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ApeRadar.NoteTagTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t\r\n\u3000\u00A0\u2003\u2028\u2029")]
    public void EmptyOrWhitespaceNote_ProducesNoTags(string? note)
    {
        Assert.Empty(NoteTagUtils.CreateTags(note));
    }

    [Fact]
    public void WhitespaceSeparators_PreserveCaseOrderAndDuplicates()
    {
        IReadOnlyList<NoteTag> tags = NoteTagUtils.CreateTags("  alpha   BETA\talpha\r\n张三\u3000😀  ");

        Assert.Equal(new[] { "alpha", "BETA", "alpha", "张三", "😀" }, tags.Select(tag => tag.Text));
        Assert.Equal(tags.Select(tag => tag.Text), tags.Select(tag => tag.CompactText));
    }

    [Fact]
    public void EveryUnicodeWhitespaceCharacter_SeparatesTags()
    {
        foreach (char separator in Enumerable.Range(char.MinValue, char.MaxValue + 1).Select(value => (char)value).Where(char.IsWhiteSpace))
        {
            IReadOnlyList<NoteTag> tags = NoteTagUtils.CreateTags($"left{separator}{separator}right");

            Assert.Equal(new[] { "left", "right" }, tags.Select(tag => tag.Text));
        }
    }

    [Fact]
    public void PunctuationWithinTokens_IsNotASeparator()
    {
        IReadOnlyList<NoteTag> tags = NoteTagUtils.CreateTags("可靠,队友 疑似/脚本 A;B x.y #Tag");

        Assert.Equal(new[] { "可靠,队友", "疑似/脚本", "A;B", "x.y", "#Tag" }, tags.Select(tag => tag.Text));
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("👍🏽")]
    [InlineData("👨‍👩‍👧‍👦")]
    [InlineData("🇨🇳")]
    [InlineData("e\u0301")]
    public void CompactText_TruncatesOnlyAfterTenCompleteUnicodeTextElements(string element)
    {
        string atLimit = string.Concat(Enumerable.Repeat(element, 10));
        string overLimit = string.Concat(Enumerable.Repeat(element, 12));
        NoteTag shortTag = Assert.Single(NoteTagUtils.CreateTags(atLimit));
        NoteTag longTag = Assert.Single(NoteTagUtils.CreateTags(overLimit));

        Assert.Equal(10, new StringInfo(atLimit).LengthInTextElements);
        Assert.Equal(atLimit, shortTag.Text);
        Assert.Equal(atLimit, shortTag.CompactText);
        Assert.Equal(overLimit, longTag.Text);
        Assert.Equal(atLimit + "…", longTag.CompactText);
        Assert.Equal(10, new StringInfo(longTag.CompactText[..^1]).LengthInTextElements);
    }

    [Fact]
    public void LongToken_DoesNotTruncateItsFullTextOrDiscardLaterTags()
    {
        const string fullText = "abcdefghijklmnopqrstuvwxabcdefghijklmnopqrstuvwx";
        IReadOnlyList<NoteTag> tags = NoteTagUtils.CreateTags(fullText + " Later");

        Assert.Equal(2, tags.Count);
        Assert.Equal(fullText, tags[0].Text);
        Assert.Equal("abcdefghij…", tags[0].CompactText);
        Assert.Equal("Later", tags[1].Text);
        Assert.Equal("Later", tags[1].CompactText);
    }

    [Fact]
    public void ManyTags_AreNotLimitedOrDeduplicated()
    {
        string[] expected = Enumerable.Range(0, 120).Select(index => $"标签{index:000}").Concat(new[] { "标签000", "标签000" }).ToArray();
        IReadOnlyList<NoteTag> tags = NoteTagUtils.CreateTags(string.Join(' ', expected));

        Assert.Equal(expected.Length, tags.Count);
        Assert.Equal(expected, tags.Select(tag => tag.Text));
        Assert.Equal(expected, tags.Select(tag => tag.CompactText));
    }

    [Fact]
    public void SixColorPalette_IsStableAndCyclesByOccurrenceIndex()
    {
        string note = string.Join(' ', Enumerable.Repeat("same", 18));
        IReadOnlyList<NoteTag> tags = NoteTagUtils.CreateTags(note);
        IReadOnlyList<NoteTag> repeated = NoteTagUtils.CreateTags(note);

        Assert.Equal(18, tags.Count);
        Assert.Equal(6, tags.Take(6).Select(tag => tag.BackgroundColor).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (int index = 0; index < tags.Count; index++)
        {
            NoteTag tag = tags[index];
            Assert.Matches("^#[0-9A-Fa-f]{6}$", tag.BackgroundColor);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", tag.ForegroundColor);
            Assert.Equal(tags[index % 6].BackgroundColor, tag.BackgroundColor);
            Assert.Equal(tags[index % 6].ForegroundColor, tag.ForegroundColor);
            Assert.Equal(tag.Text, repeated[index].Text);
            Assert.Equal(tag.CompactText, repeated[index].CompactText);
            Assert.Equal(tag.BackgroundColor, repeated[index].BackgroundColor);
            Assert.Equal(tag.ForegroundColor, repeated[index].ForegroundColor);
        }
    }

    [Fact]
    public void EveryPalettePair_HasAtLeastFourPointFiveContrastRatio()
    {
        IReadOnlyList<NoteTag> tags = NoteTagUtils.CreateTags("one two three four five six");

        Assert.Equal(6, tags.Count);
        foreach (NoteTag tag in tags)
        {
            double background = RelativeLuminance(tag.BackgroundColor);
            double foreground = RelativeLuminance(tag.ForegroundColor);
            double contrast = (Math.Max(background, foreground) + 0.05) / (Math.Min(background, foreground) + 0.05);

            Assert.True(contrast >= 4.5, $"{tag.ForegroundColor} on {tag.BackgroundColor} has contrast {contrast:F3}, expected at least 4.5.");
        }
    }

    [Theory]
    [InlineData(WatchStatus.NONE, false)]
    [InlineData(WatchStatus.NONE, true)]
    [InlineData(WatchStatus.POSITIVE, false)]
    [InlineData(WatchStatus.POSITIVE, true)]
    [InlineData(WatchStatus.NEGTIVE, false)]
    [InlineData(WatchStatus.NEGTIVE, true)]
    [InlineData(WatchStatus.CHEATER, false)]
    [InlineData(WatchStatus.CHEATER, true)]
    public void WatchListRoundTrip_PreservesRawNoteStatusCustomMarkerAndExistingFormat(WatchStatus status, bool customMarker)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "WatchList.json");
        Player player = new("测试玩家", "12345", Server.EU, status) { Note = "旧备注", IsCustomMarked = customMarker };
        WatchListUtils.SaveWatchList(player, path);
        JObject originalRecord = (JObject)WatchListUtils.ReadWatchList(path)["EU"]![player.ID]!.DeepClone();
        const string rawNote = "  Reliable teammate  非常长的备注标签不应该在存储时被截断或者删除任何内容\t需要复盘\r\n保留,标点\u3000😀 e\u0301  ";
        player.Note = rawNote;
        IReadOnlyList<NoteTag> beforeSave = NoteTagUtils.CreateTags(player.Note);

        WatchListUtils.SaveWatchListNote(player, path);
        JObject loaded = WatchListUtils.ReadWatchList(path);
        JObject savedRecord = (JObject)loaded["EU"]![player.ID]!;
        string loadedNote = WatchListUtils.GetPlayerNote(loaded, Server.EU, player.ID);
        IReadOnlyList<NoteTag> afterLoad = NoteTagUtils.CreateTags(loadedNote);

        Assert.Equal(rawNote, player.Note);
        Assert.Equal(rawNote, loadedNote);
        Assert.Equal(rawNote, savedRecord["note"]!.Value<string>());
        Assert.Equal(status, player.WatchStatus);
        Assert.Equal(customMarker, player.IsCustomMarked);
        Assert.Equal(WatchStatusExt.GetNameByStatus(status), savedRecord["status"]!.Value<string>());
        Assert.Equal(customMarker, WatchListUtils.GetPlayerCustomMarker(loaded, Server.EU, player.ID));
        Assert.Equal(player.Name, savedRecord["name"]!.Value<string>());
        Assert.Equal(beforeSave.Select(tag => (tag.Text, tag.CompactText, tag.BackgroundColor, tag.ForegroundColor)),
            afterLoad.Select(tag => (tag.Text, tag.CompactText, tag.BackgroundColor, tag.ForegroundColor)));

        originalRecord.Remove("note");
        JObject savedNonNoteFields = (JObject)savedRecord.DeepClone();
        savedNonNoteFields.Remove("note");
        Assert.True(JToken.DeepEquals(originalRecord, savedNonNoteFields), "Saving displayed tags must not add tag metadata or change unrelated WatchList fields.");
    }

    private static double RelativeLuminance(string hexColor)
    {
        static double LinearChannel(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        Assert.Matches("^#[0-9A-Fa-f]{6}$", hexColor);
        double red = LinearChannel(Convert.ToByte(hexColor.Substring(1, 2), 16));
        double green = LinearChannel(Convert.ToByte(hexColor.Substring(3, 2), 16));
        double blue = LinearChannel(Convert.ToByte(hexColor.Substring(5, 2), 16));
        return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
