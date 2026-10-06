using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ApeRadar.Utils
{
    internal sealed record NoteTag(string Text, string CompactText, string BackgroundColor, string ForegroundColor);

    internal static class NoteTagUtils
    {
        private static readonly (string Background, string Foreground)[] Palette =
        {
            ("#E8F2FF", "#245B95"),
            ("#E7F6EE", "#19714C"),
            ("#FFF4D6", "#805200"),
            ("#F2EBFF", "#62429A"),
            ("#FCECF4", "#9A386A"),
            ("#E5F5F7", "#216C76")
        };

        public static IReadOnlyList<NoteTag> CreateTags(string? note) =>
            (note ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select((text, index) =>
                {
                    StringInfo elements = new(text);
                    string compact = elements.LengthInTextElements <= NoteQuickOptionUtils.MaximumBadgeLength
                        ? text
                        : elements.SubstringByTextElements(0, NoteQuickOptionUtils.MaximumBadgeLength) + "…";
                    (string background, string foreground) = Palette[index % Palette.Length];
                    return new NoteTag(text, compact, background, foreground);
                }).ToArray();
    }
}
