using ApeRadar.ViewModels;
using System;
using System.Collections.Generic;

namespace ApeRadar.Utils
{
    internal readonly record struct RosterLayoutMetrics(
        double RowHeight,
        double PlayerFontSize,
        double StatisticsFontSize,
        double StatusColumnWidth,
        bool UseCompactStatusBadges,
        bool RequiresHorizontalScroll);

    internal readonly record struct RosterColumnWidths(
        double Player,
        double Account,
        double Ship,
        double Tier,
        double Status,
        bool RequiresHorizontalScroll);

    internal static class RosterLayoutCalculator
    {
        internal const double MinimumRowHeight = 58;
        internal const double PreferredRowHeight = 68;
        internal const double MinimumSemanticRosterWidth = 708;
        private const double ColumnHeaderAllowance = 33;
        private const double ScrollBarAllowance = 22;

        public static bool RequiresHorizontalScroll(double rosterGridWidth) =>
            rosterGridWidth < MinimumSemanticRosterWidth + ScrollBarAllowance;

        public static RosterLayoutMetrics Calculate(
            double gridHeight,
            int playerCount,
            double configuredPlayerFontSize,
            double configuredStatisticsFontSize,
            RosterDisplayDensity density = RosterDisplayDensity.Standard,
            double rosterGridWidth = double.PositiveInfinity)
        {
            (double minimum, double preferred, double minimumPlayerFont, double maximumPlayerFont, double minimumStatisticsFont, double maximumStatisticsFont, double statusWidth) = density switch
            {
                RosterDisplayDensity.Compact => (50d, 56d, 10.5d, 14.5d, 10d, 12.5d, 68d),
                RosterDisplayDensity.Comfortable => (68d, 78d, 12d, 17d, 11d, 15d, 100d),
                _ => (MinimumRowHeight, PreferredRowHeight, 11d, 15.5d, 10.5d, 13.5d, 92d)
            };

            int rows = Math.Max(1, playerCount);
            double available = Math.Max(0, gridHeight - ColumnHeaderAllowance);
            double rowHeight = Math.Clamp(Math.Floor(available / rows), minimum, preferred);
            int visibleLines = density == RosterDisplayDensity.Compact ? 2 : 3;
            double lineBudget = Math.Max(15, (rowHeight - 8) / visibleLines);
            double playerFont = Math.Min(configuredPlayerFontSize, lineBudget * (density == RosterDisplayDensity.Compact ? 0.68 : 0.78));
            double statisticsFont = Math.Min(configuredStatisticsFontSize, lineBudget * (density == RosterDisplayDensity.Compact ? 0.58 : 0.68));
            bool horizontalScroll = RequiresHorizontalScroll(rosterGridWidth);
            return new RosterLayoutMetrics(
                rowHeight,
                Math.Clamp(Math.Round(playerFont, 1), minimumPlayerFont, maximumPlayerFont),
                Math.Clamp(Math.Round(statisticsFont, 1), minimumStatisticsFont, maximumStatisticsFont),
                statusWidth,
                horizontalScroll || density == RosterDisplayDensity.Compact,
                horizontalScroll);
        }

        public static RosterColumnWidths CalculateColumns(
            double rosterGridWidth,
            RosterDisplayDensity density,
            bool showAccount,
            bool showShip,
            bool showTier)
        {
            double status = density switch
            {
                RosterDisplayDensity.Compact => 68,
                RosterDisplayDensity.Comfortable => 100,
                _ => 92
            };
            Dictionary<string, double> minimums = new()
            {
                ["Player"] = 190,
                ["Account"] = showAccount ? 136 : 0,
                ["Ship"] = showShip ? 158 : 0,
                ["Tier"] = showTier ? 132 : 0
            };
            double minimumTotal = minimums["Player"] + minimums["Account"] + minimums["Ship"] + minimums["Tier"] + status;
            double usableWidth = Math.Max(0, rosterGridWidth - ScrollBarAllowance);
            bool requiresScroll = usableWidth < minimumTotal;
            double extra = Math.Max(0, usableWidth - minimumTotal);

            Dictionary<string, double> weights = new()
            {
                ["Player"] = 0.35,
                ["Account"] = showAccount ? 0.20 : 0,
                ["Ship"] = showShip ? 0.25 : 0,
                ["Tier"] = showTier ? 0.20 : 0
            };
            double weightTotal = weights["Player"] + weights["Account"] + weights["Ship"] + weights["Tier"];
            double Width(string key) => Math.Round(minimums[key] + extra * weights[key] / weightTotal, 1);

            return new RosterColumnWidths(
                Width("Player"),
                showAccount ? Width("Account") : 0,
                showShip ? Width("Ship") : 0,
                showTier ? Width("Tier") : 0,
                status,
                requiresScroll);
        }
    }
}
