using System;
using System.Globalization;

namespace ApeRadar.ViewModels
{
    // One display contract for both roster interfaces. Missing values never become zero.
    internal static class RosterStatistic
    {
        public static bool IsAvailable(double value, RosterMetricKind kind = RosterMetricKind.Neutral, bool hidden = false) =>
            !hidden && double.IsFinite(value) && value >= 0 && (kind != RosterMetricKind.Winrate || value <= 1);

        public static double Value(double value, RosterMetricKind kind = RosterMetricKind.Neutral, bool hidden = false) =>
            IsAvailable(value, kind, hidden) ? value : -1;

        public static string Format(double value, string format, RosterMetricKind kind = RosterMetricKind.Neutral) =>
            IsAvailable(value, kind) ? value.ToString(format, CultureInfo.CurrentCulture) : "—";
    }
}
