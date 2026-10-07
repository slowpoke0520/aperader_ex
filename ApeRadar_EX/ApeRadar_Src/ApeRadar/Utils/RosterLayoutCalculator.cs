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
        double Weighted,
        double Ship,
        double PersonalRating,
        double Tier,
        double Performance,
        bool RequiresHorizontalScroll)
    {
        public double Total => Player + Account + Weighted + Ship + PersonalRating + Tier + Performance;
    }

    internal readonly record struct RosterFitInput(
        double AvailableTeamWidth,
        double AvailableHeight,
        int PlayerCount,
        double ConfiguredPlayerFontSize,
        double ConfiguredStatisticsFontSize,
        RosterDisplayDensity Density,
        bool ShowAccount,
        bool ShowWeighted,
        bool ShowShip,
        bool ShowPersonalRating,
        bool ShowTier,
        bool ShowPerformance,
        int AccountMetricLines,
        int WeightedMetricLines,
        int ShipMetricLines,
        int PersonalRatingMetricLines,
        int TierMetricLines)
    {
        public int MaximumMetricLines => Math.Max(1, Math.Max(
            ShowAccount ? AccountMetricLines : 0,
            Math.Max(ShowWeighted ? WeightedMetricLines : 0,
            Math.Max(ShowShip ? ShipMetricLines : 0,
            Math.Max(ShowPersonalRating ? PersonalRatingMetricLines : 0,
                ShowTier ? TierMetricLines : 0)))));
    }

    internal readonly record struct RosterFitMetrics(
        double NaturalWidth,
        double NaturalHeight,
        RosterColumnWidths Columns,
        RosterLayoutMetrics Layout);

    internal static class RosterLayoutCalculator
    {
        internal const double PlayerWidth = 250;
        internal const double AccountWidth = 92;
        internal const double WeightedWidth = 72;
        internal const double ShipWidth = 112;
        internal const double PersonalRatingWidth = 86;
        internal const double TierWidth = 96;
        internal const double PerformanceWidth = 48;
        internal const double HeaderHeight = 30;
        internal const double ScrollBarAllowance = 18;

        private const double MinimumPlayerWidth = 190;
        private const double MinimumAccountWidth = 78;
        private const double MinimumWeightedWidth = 60;
        private const double MinimumShipWidth = 94;
        private const double MinimumPersonalRatingWidth = 72;
        private const double MinimumTierWidth = 82;
        private const double MinimumPerformanceWidth = 48;

        public static RosterFitMetrics CalculateFit(RosterFitInput input)
        {
            int rows = Math.Max(1, input.PlayerCount);
            (double targetPlayerFont, double targetStatisticsFont, double minimumPlayerFont, double minimumStatisticsFont, double verticalPadding) =
                FontTargets(input);

            double playerFont = targetPlayerFont;
            double statisticsFont = targetStatisticsFont;
            double requiredRowHeight = RequiredRowHeight(playerFont, statisticsFont, input.MaximumMetricLines, verticalPadding);
            while (HeaderHeight + rows * requiredRowHeight > input.AvailableHeight + 0.5 &&
                   (playerFont > minimumPlayerFont || statisticsFont > minimumStatisticsFont))
            {
                playerFont = Math.Max(minimumPlayerFont, playerFont - 0.25);
                statisticsFont = Math.Max(minimumStatisticsFont, statisticsFont - 0.25);
                requiredRowHeight = RequiredRowHeight(playerFont, statisticsFont, input.MaximumMetricLines, verticalPadding);
            }

            bool verticalScroll = rows > 12 || HeaderHeight + rows * requiredRowHeight > input.AvailableHeight + 0.5;
            double rowHeight = requiredRowHeight;
            if (!verticalScroll && input.AvailableHeight > HeaderHeight)
            {
                double availablePerRow = (input.AvailableHeight - HeaderHeight) / rows;
                rowHeight = Math.Min(requiredRowHeight + 5, Math.Max(requiredRowHeight, availablePerRow));
            }

            double naturalWidth = NaturalWidth(input);
            double naturalHeight = HeaderHeight + rows * RequiredRowHeight(targetPlayerFont, targetStatisticsFont, input.MaximumMetricLines, verticalPadding);
            double usableWidth = Math.Max(0, input.AvailableTeamWidth - (verticalScroll ? ScrollBarAllowance : 0));
            RosterColumnWidths columns = CalculateColumnWidths(input, usableWidth, naturalWidth);
            double scale = naturalWidth <= 0 ? 1 : Math.Min(1, columns.Total / naturalWidth);

            RosterLayoutMetrics layout = new(
                Round(rowHeight),
                HeaderHeight,
                Round(playerFont),
                Round(statisticsFont),
                Math.Round(scale, 3),
                input.Density == RosterDisplayDensity.Compact || scale < 0.9,
                columns.RequiresHorizontalScroll,
                verticalScroll);
            return new(Round(naturalWidth), Round(naturalHeight), columns, layout);
        }

        private static (double Player, double Statistics, double MinPlayer, double MinStatistics, double Padding) FontTargets(RosterFitInput input)
        {
            return input.Density switch
            {
                RosterDisplayDensity.Compact =>
                    (Math.Clamp(input.ConfiguredPlayerFontSize, 14, 15), Math.Clamp(input.ConfiguredStatisticsFontSize, 12, 12), 14, 12, 5),
                RosterDisplayDensity.Comfortable =>
                    (Math.Clamp(input.ConfiguredPlayerFontSize, 17, 24), Math.Clamp(input.ConfiguredStatisticsFontSize, 14, 20), 14, 12, 9),
                _ =>
                    (Math.Clamp(input.ConfiguredPlayerFontSize, 14, 17), Math.Clamp(input.ConfiguredStatisticsFontSize, 12, 14), 14, 12, 7)
            };
        }

        private static double RequiredRowHeight(double playerFont, double statisticsFont, int metricLines, double verticalPadding)
        {
            double playerHeight = 2 * Math.Ceiling(playerFont * 1.24) + verticalPadding;
            double metricHeight = Math.Max(1, metricLines) * Math.Ceiling(statisticsFont * 1.24) + verticalPadding;
            return Math.Max(playerHeight, metricHeight);
        }

        private static double NaturalWidth(RosterFitInput input) =>
            PlayerWidth +
            (input.ShowAccount ? AccountWidth : 0) +
            (input.ShowWeighted ? WeightedWidth : 0) +
            (input.ShowShip ? ShipWidth : 0) +
            (input.ShowPersonalRating ? PersonalRatingWidth : 0) +
            (input.ShowTier ? TierWidth : 0) +
            (input.ShowPerformance ? PerformanceWidth : 0);

        private static RosterColumnWidths CalculateColumnWidths(RosterFitInput input, double usableWidth, double naturalWidth)
        {
            double player = PlayerWidth;
            double account = input.ShowAccount ? AccountWidth : 0;
            double weighted = input.ShowWeighted ? WeightedWidth : 0;
            double ship = input.ShowShip ? ShipWidth : 0;
            double personalRating = input.ShowPersonalRating ? PersonalRatingWidth : 0;
            double tier = input.ShowTier ? TierWidth : 0;
            double performance = input.ShowPerformance ? PerformanceWidth : 0;

            double minimumTotal = MinimumPlayerWidth +
                (input.ShowAccount ? MinimumAccountWidth : 0) +
                (input.ShowWeighted ? MinimumWeightedWidth : 0) +
                (input.ShowShip ? MinimumShipWidth : 0) +
                (input.ShowPersonalRating ? MinimumPersonalRatingWidth : 0) +
                (input.ShowTier ? MinimumTierWidth : 0) +
                (input.ShowPerformance ? MinimumPerformanceWidth : 0);

            bool horizontalScroll = usableWidth + 0.5 < minimumTotal;
            if (naturalWidth > usableWidth)
            {
                double shrinkCapacity = naturalWidth - minimumTotal;
                double shrinkNeeded = Math.Min(shrinkCapacity, Math.Max(0, naturalWidth - usableWidth));
                double t = shrinkCapacity <= 0 ? 1 : shrinkNeeded / shrinkCapacity;
                player = Lerp(PlayerWidth, MinimumPlayerWidth, t);
                if (input.ShowAccount) account = Lerp(AccountWidth, MinimumAccountWidth, t);
                if (input.ShowWeighted) weighted = Lerp(WeightedWidth, MinimumWeightedWidth, t);
                if (input.ShowShip) ship = Lerp(ShipWidth, MinimumShipWidth, t);
                if (input.ShowPersonalRating) personalRating = Lerp(PersonalRatingWidth, MinimumPersonalRatingWidth, t);
                if (input.ShowTier) tier = Lerp(TierWidth, MinimumTierWidth, t);
                if (input.ShowPerformance) performance = PerformanceWidth;
            }
            else
            {
                double extra = Math.Max(0, usableWidth - naturalWidth);
                double weight = 0.45 + (input.ShowAccount ? 0.15 : 0) + (input.ShowWeighted ? 0.10 : 0) +
                    (input.ShowShip ? 0.20 : 0) + (input.ShowPersonalRating ? 0.10 : 0);
                player += extra * 0.45 / weight;
                if (input.ShowAccount) account += extra * 0.15 / weight;
                if (input.ShowWeighted) weighted += extra * 0.10 / weight;
                if (input.ShowShip) ship += extra * 0.20 / weight;
                if (input.ShowPersonalRating) personalRating += extra * 0.10 / weight;
            }

            return new(
                Round(player), Round(account), Round(weighted), Round(ship), Round(personalRating),
                Round(tier), Round(performance), horizontalScroll);
        }

        private static double Lerp(double from, double to, double amount) => from + (to - from) * amount;
        private static double Round(double value) => Math.Round(value, 1);

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
            bool showPerformance) =>
            CalculateFit(new RosterFitInput(
                availableTeamWidth, availableHeight, playerCount, configuredPlayerFontSize, configuredStatisticsFontSize,
                density, showAccount, true, showShip, true, showTier, showPerformance,
                3, 1, 4, 2, 3));

        public static RosterLayoutMetrics Calculate(
            double gridHeight,
            int playerCount,
            double configuredPlayerFontSize,
            double configuredStatisticsFontSize,
            RosterDisplayDensity density = RosterDisplayDensity.Standard,
            double rosterGridWidth = double.PositiveInfinity)
        {
            double width = double.IsPositiveInfinity(rosterGridWidth) ? 756 : rosterGridWidth;
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
