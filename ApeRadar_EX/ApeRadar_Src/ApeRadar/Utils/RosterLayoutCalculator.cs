using System;

namespace ApeRadar.Utils
{
    internal readonly record struct RosterLayoutMetrics(double RowHeight, double PlayerFontSize, double StatisticsFontSize);

    internal static class RosterLayoutCalculator
    {
        internal const double MinimumRowHeight = 42;
        internal const double PreferredRowHeight = 50;
        internal const double FullStatisticsColumnWidth = 474;
        internal const double MinimumFullRosterGridWidth = 690;
        private const double ColumnHeaderAllowance = 31;

        public static bool ShouldUseCompactColumns(double rosterGridWidth)
        {
            return rosterGridWidth < MinimumFullRosterGridWidth;
        }

        public static RosterLayoutMetrics Calculate(double gridHeight, int playerCount, double configuredPlayerFontSize, double configuredStatisticsFontSize)
        {
            int rows = Math.Max(1, playerCount);
            double available = Math.Max(0, gridHeight - ColumnHeaderAllowance);
            double rowHeight = Math.Clamp(Math.Floor(available / rows), MinimumRowHeight, PreferredRowHeight);
            double maximumFont = Math.Max(configuredPlayerFontSize, configuredStatisticsFontSize);
            double usableLineHeight = Math.Max(20, (rowHeight - 4) / 2);
            double scale = maximumFont <= 0 ? 1 : Math.Min(1, usableLineHeight / (maximumFont * 1.2));
            return new RosterLayoutMetrics(
                rowHeight,
                Math.Max(10, Math.Round(configuredPlayerFontSize * scale, 1)),
                Math.Max(10, Math.Round(configuredStatisticsFontSize * scale, 1)));
        }
    }
}
