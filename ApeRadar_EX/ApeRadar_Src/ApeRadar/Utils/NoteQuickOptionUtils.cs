using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace ApeRadar.Utils
{
    internal static class NoteQuickOptionUtils
    {
        internal const int MaximumOptionCount = 8;
        internal const int MaximumOptionLength = 24;
        internal const int MaximumBadgeLength = 10;

        public static IReadOnlyList<string> GetOptions(string? serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
                return DefaultOptions();

            try
            {
                return Normalize(JsonConvert.DeserializeObject<List<string>>(serialized) ?? new List<string>());
            }
            catch (JsonException)
            {
                return DefaultOptions();
            }
        }

        public static IReadOnlyList<string> Normalize(IEnumerable<string?> options) =>
            options
                .Select(NormalizeLine)
                .Where(option => option.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(MaximumOptionCount)
                .ToArray();

        public static string Serialize(IEnumerable<string?> options) =>
            JsonConvert.SerializeObject(Normalize(options), Formatting.None);

        public static string ToBadgeText(string? note)
        {
            string normalized = NormalizeLine(note);
            if (normalized.Length == 0) return "";

            StringInfo info = new(normalized);
            return info.LengthInTextElements <= MaximumBadgeLength
                ? normalized
                : info.SubstringByTextElements(0, MaximumBadgeLength) + "…";
        }

        private static string NormalizeLine(string? value)
        {
            string collapsed = string.Join(" ", (value ?? "")
                .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Trim();
            if (collapsed.Length == 0) return "";

            StringInfo info = new(collapsed);
            return info.LengthInTextElements <= MaximumOptionLength
                ? collapsed
                : info.SubstringByTextElements(0, MaximumOptionLength);
        }

        private static IReadOnlyList<string> DefaultOptions() => new[]
        {
            Text("NoteQuickOptionReliable", "Reliable teammate"),
            Text("NoteQuickOptionWatch", "Needs attention"),
            Text("NoteQuickOptionAutomation", "Suspected automation"),
            Text("NoteQuickOptionLowTier", "Low-tier focused")
        };

        private static string Text(string resourceKey, string fallback) =>
            Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }
}
