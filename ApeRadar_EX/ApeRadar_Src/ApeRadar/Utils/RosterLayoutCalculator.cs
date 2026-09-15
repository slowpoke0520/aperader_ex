using System;

namespace ApeRadar.Utils
{
    internal readonly record struct RosterLayoutMetrics(double RowHeight, double PlayerFontSize, double StatisticsFontSize);

    internal static class RosterLayoutCalculator
    {
        internal const double MinimumRowHeight = 58;
        internal const double PreferredRowHeight = 68;
        internal const double MinimumSemanticRosterWidth = 640;
        private const double ColumnHeaderAllowance = 33;

        public static bool RequiresHorizontalScroll(double rosterGridWidth)
        {
            return rosterGridWidth < MinimumSemanticRosterWidth;
        }

        public static RosterLayoutMetrics Calculate(double gridHeight, int playerCount, double configuredPlayerFontSize, double configuredStatisticsFontSize)
        {
            int rows = Math.Max(1, playerCount);
            double available = Math.Max(0, gridHeight - ColumnHeaderAllowance);
            double rowHeight = Math.Clamp(Math.Floor(available / rows), MinimumRowHeight, PreferredRowHeight);
            double lineBudget = Math.Max(15, (rowHeight - 8) / 3);
            double playerFont = Math.Min(configuredPlayerFontSize, lineBudget * 0.78);
            double statisticsFont = Math.Min(configuredStatisticsFontSize, lineBudget * 0.68);
            return new RosterLayoutMetrics(
                rowHeight,
                Math.Clamp(Math.Round(playerFont, 1), 11, 15.5),
                Math.Clamp(Math.Round(statisticsFont, 1), 10.5, 13.5));
        }
    }
}
