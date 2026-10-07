using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace ApeRadar.Utils
{
    internal static class ChartFontUtils
    {
        public static string Resolve(FontFamily preferred)
        {
            IEnumerable<FontFamily> candidates = new[]
            {
                preferred,
                new FontFamily("Microsoft YaHei UI"),
                new FontFamily("Microsoft YaHei"),
                new FontFamily("Microsoft JhengHei UI"),
                new FontFamily("Yu Gothic UI"),
                new FontFamily("Malgun Gothic"),
                new FontFamily("Segoe UI")
            };

            foreach (FontFamily family in candidates.GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
            {
                if (family.GetTypefaces().Any(typeface =>
                    typeface.TryGetGlyphTypeface(out GlyphTypeface glyphs) &&
                    glyphs.CharacterToGlyphMap.ContainsKey('中')))
                {
                    return family.Source;
                }
            }

            return preferred.Source.Split(',')[0].Trim();
        }
    }
}
