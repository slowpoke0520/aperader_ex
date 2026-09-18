using ApeRadar.ViewModels;
using System;

namespace ApeRadar.Utils
{
    internal readonly record struct RosterLayoutMetrics(
        double RowHeight,
        double ColumnHeaderHeight,
        double PlayerFontSize,
        double StatisticsFontSize,
        double Scale,
        bool UseCompactStatusBadges,
        bool RequiresHorizontalScroll,
        bool RequiresVerticalScroll);

    internal readonly record struct RosterColumnWidths(
        double Player,
        double Account,
        double Ship,
        double Tier,
        double Performance,
        bool RequiresHorizontalScroll)
    {
        public double Total => Player + Account + Ship + Tier + Performance;
    }

    internal readonly record struct RosterFitMetrics(
        double NaturalWidth,
        double NaturalHeight,
        RosterColumnWidths Columns,
        RosterLayoutMetrics Layout);

    internal static class RosterLayoutCalculator
    {
        internal const double MinimumScale = 0.78;
        internal const double PlayerWidth = 240;
        internal const double AccountWidth = 96;
        internal const double ShipWidth = 124;
        internal const double TierWidth = 96;
        internal const double PerformanceWidth = 56;
        internal const double HeaderHeight = 28;
        internal const double ScrollBarAllowance = 18;
        internal const double MinimumSemanticRosterWidth = PlayerWidth + AccountWidth + ShipWidth + TierWidth + PerformanceWidth;

        public static RosterFitMetrics CalculateFit(
            double availableTeamWidth,
            double availableHeight,
            int playerCount,
            double configuredPlayerFontSize,
            double configuredStatisticsFontSize,
            RosterDisplayDensity density,
            bool showAccount,
            bool showShip,
            bool showTier,
            bool showPerformance)
        {
            double naturalRowHeight = density switch
            {
                RosterDisplayDensity.Compact => 48,
                RosterDisplayDensity.Comfortable => 60,
                _ => 54
            };
            int rows = Math.Max(1, playerCount);
            double naturalWidth = PlayerWidth +
                (showAccount ? AccountWidth : 0) +
                (showShip ? ShipWidth : 0) +
                (showTier ? TierWidth : 0) +
                (showPerformance ? PerformanceWidth : 0);
            double naturalHeight = HeaderHeight + rows * naturalRowHeight;

            double widthRatio = availableTeamWidth > 0 ? availableTeamWidth / naturalWidth : 0;
            double heightRatio = availableHeight > 0 ? availableHeight / naturalHeight : 0;
            double requestedScale = Math.Min(1, Math.Min(widthRatio, heightRatio));
            double scale = requestedScale >= MinimumScale ? requestedScale : MinimumScale;

            double maximumRowHeight = density switch
            {
                RosterDisplayDensity.Compact => 56,
                RosterDisplayDensity.Comfortable => 78,
                _ => 68
            };
            double rowHeight = naturalRowHeight * scale;
            if (requestedScale >= 1 && playerCount <= 12 && availableHeight > HeaderHeight)
            {
                rowHeight = Math.Clamp((availableHeight - HeaderHeight) / rows, naturalRowHeight, maximumRowHeight);
            }

            double arrangedHeight = HeaderHeight * scale + rows * rowHeight;
            bool requiresVerticalScroll = playerCount > 12 || arrangedHeight > availableHeight + 0.5;
            double usableWidth = Math.Max(0, availableTeamWidth - (requiresVerticalScroll ? ScrollBarAllowance : 0));
            bool requiresHorizontalScroll = naturalWidth * scale > usableWidth + 0.5;

            double playerWidth = PlayerWidth * scale;
            double accountWidth = showAccount ? AccountWidth * scale : 0;
            double shipWidth = showShip ? ShipWidth * scale : 0;
            double tierWidth = showTier ? TierWidth * scale : 0;
            double performanceWidth = showPerformance ? PerformanceWidth * scale : 0;
            double scaledTotal = playerWidth + accountWidth + shipWidth + tierWidth + performanceWidth;
            if (!requiresHorizontalScroll)
            {
                // Keep statistics stable and give all spare room to names.
                playerWidth += Math.Max(0, usableWidth - scaledTotal);
            }

            double playerFontMaximum = density switch
            {
                RosterDisplayDensity.Compact => 13.5,
                RosterDisplayDensity.Comfortable => 16,
                _ => 15
            };
            double statisticsFontMaximum = density switch
            {
                RosterDisplayDensity.Compact => 10,
                RosterDisplayDensity.Comfortable => 12.5,
                _ => 11.5
            };
            double playerFont = Math.Clamp(Math.Min(configuredPlayerFontSize, playerFontMaximum) * scale, 9.5, playerFontMaximum);
            double statisticsFont = Math.Clamp(Math.Min(configuredStatisticsFontSize, statisticsFontMaximum) * scale, 8.5, statisticsFontMaximum);

            RosterColumnWidths columns = new(
                Math.Round(playerWidth, 1),
                Math.Round(accountWidth, 1),
                Math.Round(shipWidth, 1),
                Math.Round(tierWidth, 1),
                Math.Round(performanceWidth, 1),
                requiresHorizontalScroll);
            RosterLayoutMetrics layout = new(
                Math.Round(rowHeight, 1),
                Math.Round(HeaderHeight * scale, 1),
                Math.Round(playerFont, 1),
                Math.Round(statisticsFont, 1),
                Math.Round(scale, 3),
                density == RosterDisplayDensity.Compact || scale < 0.9,
                requiresHorizontalScroll,
                requiresVerticalScroll);
            return new(naturalWidth, naturalHeight, columns, layout);
        }

        public static bool RequiresHorizontalScroll(double rosterGridWidth) =>
            rosterGridWidth < MinimumSemanticRosterWidth * MinimumScale;

        public static RosterLayoutMetrics Calculate(
            double gridHeight,
            int playerCount,
            double configuredPlayerFontSize,
            double configuredStatisticsFontSize,
            RosterDisplayDensity density = RosterDisplayDensity.Standard,
            double rosterGridWidth = double.PositiveInfinity)
        {
            double width = double.IsPositiveInfinity(rosterGridWidth) ? MinimumSemanticRosterWidth : rosterGridWidth;
            return CalculateFit(width, gridHeight, playerCount, configuredPlayerFontSize,
                configuredStatisticsFontSize, density, true, true, true, true).Layout;
        }

        public static RosterColumnWidths CalculateColumns(
            double rosterGridWidth,
            RosterDisplayDensity density,
            bool showAccount,
            bool showShip,
            bool showTier,
            bool showPerformance = true) =>
            CalculateFit(rosterGridWidth, 10_000, 12, 18, 16, density,
                showAccount, showShip, showTier, showPerformance).Columns;
    }
}
