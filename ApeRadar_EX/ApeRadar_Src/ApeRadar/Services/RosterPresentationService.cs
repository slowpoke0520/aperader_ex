using ApeRadar.Models;
using ApeRadar.Utils;
using ApeRadar.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace ApeRadar.Services
{
    internal interface IRosterPresentationService
    {
        IReadOnlyList<PlayerRosterRowViewModel> CreateRows(IEnumerable<Player> players, RosterPresentationOptions options);
    }

    internal sealed class RosterPresentationService : IRosterPresentationService
    {
        public IReadOnlyList<PlayerRosterRowViewModel> CreateRows(IEnumerable<Player> players, RosterPresentationOptions options) =>
            players.Select(player => CreateRow(player, options)).ToArray();

        private static PlayerRosterRowViewModel CreateRow(Player player, RosterPresentationOptions options)
        {
            RosterMetricColumnViewModel account = Column(
                RosterColumnKind.Account,
                Number(Text("RosterMetricBattlesShort", "Games"), player.Battles, "N0", RosterMetricKind.Neutral, options.AccountVisibility),
                Percent(Text("RosterMetricWinrateShort", "WR"), player.AccountWinrate, RosterMetricKind.Winrate, options.AccountVisibility, RosterMetricEmphasis.Primary),
                Number(Text("RosterMetricAvgExpShort", "XP"), player.AvgExpPerBattle, "N0", RosterMetricKind.Neutral, options.AccountAverageExperienceVisibility));

            RosterMetricColumnViewModel weighted = Column(
                RosterColumnKind.Weighted,
                Percent(Text("RosterMetricWinrateShort", "WR"), player.WeightedWinrate, RosterMetricKind.Winrate,
                    options.WeightedVisibility, RosterMetricEmphasis.Primary));

            double damageRating = player.ShipBattles > 0 && player.ShipAvgDmgPerBattle >= 0
                ? PRUtils.CalculateDamageRating(player.ShipID, player.ShipBattles, player.ShipAvgDmgPerBattle * player.ShipBattles)
                : -1;
            RosterMetricColumnViewModel ship = Column(
                RosterColumnKind.Ship,
                Number(Text("RosterMetricBattlesShort", "Games"), player.ShipBattles, "N0", RosterMetricKind.Neutral, options.ShipVisibility),
                Percent(Text("RosterMetricWinrateShort", "WR"), player.ShipWinrate, RosterMetricKind.Winrate, options.ShipVisibility, RosterMetricEmphasis.Primary),
                Number(Text("RosterMetricAvgDamageShort", "Dmg"), player.ShipAvgDmgPerBattle, "N0", RosterMetricKind.DamageRating,
                    options.ShipAverageDamageVisibility, damageRating),
                Number(Text("RosterMetricAvgExpShort", "XP"), player.ShipAvgExpPerBattle, "N0", RosterMetricKind.Neutral,
                    options.ShipAverageExperienceVisibility, emphasis: RosterMetricEmphasis.Tertiary));

            RosterMetricColumnViewModel personalRating = Column(
                RosterColumnKind.PersonalRating,
                Number(Text("DataGridToolTipAccount", "Account"), player.PR, "N0", RosterMetricKind.PersonalRating,
                    options.PersonalRatingVisibility, emphasis: RosterMetricEmphasis.Primary),
                Number(Text("DataGridToolTipShip", "Ship"), player.ShipPR, "N0", RosterMetricKind.PersonalRating,
                    options.PersonalRatingVisibility, emphasis: RosterMetricEmphasis.Primary));

            RosterMetricColumnViewModel tier = options.ShowTierPerformance
                ? Column(
                    RosterColumnKind.Tier,
                    TierBattleMetric(player),
                    Percent(Text("RosterMetricWinrateShort", "WR"), player.TierWinrate, RosterMetricKind.Winrate, 0, RosterMetricEmphasis.Primary),
                    Number("PR", player.TierPR, "N0", RosterMetricKind.PersonalRating, 0, emphasis: RosterMetricEmphasis.Primary))
                : Column(RosterColumnKind.Tier);

            IReadOnlyList<RosterStatusBadgeViewModel> badges = BuildStatusBadges(player, options);
            PerformanceCellViewModel performance = BuildPerformanceCell(player, options);
            PlayerSkillBand band = performance.Band;
            string bandText = SkillBandText(band);
            return new PlayerRosterRowViewModel(player, account, weighted, ship, personalRating, tier, BuildContextPreview(player), badges,
                band, bandText, performance.ToolTip, performance);
        }

        private static MetricItemViewModel TierBattleMetric(Player player)
        {
            string warningGlyph = player.IsTierSampleSmall && player.TierBattles >= 0 ? "⚠" : "";
            string tooltip = player.IsTierSampleSmall ? Text("TierStatsSmallSample", "Small same-tier sample") : "";
            return new MetricItemViewModel(
                Text("RosterMetricBattlesShort", "Games"),
                Format(player.TierBattles, "N0"),
                null,
                RosterMetricKind.Neutral,
                player.TierBattles >= 0,
                1,
                RosterMetricEmphasis.Secondary,
                warningGlyph,
                tooltip);
        }

        private static IReadOnlyList<RosterStatusBadgeViewModel> BuildStatusBadges(Player player, RosterPresentationOptions options)
        {
            List<RosterStatusBadgeViewModel> badges = new();
            if (player.WatchStatus != WatchStatus.NONE)
            {
                (RosterBadgeSeverity severity, string compact, string display, string resourceKey) = player.WatchStatus switch
                {
                    WatchStatus.POSITIVE => (RosterBadgeSeverity.Positive, "✓", Text("RosterBadgeWatchPositive", "Positive"), "RosterBadgeWatchPositiveTip"),
                    WatchStatus.NEGTIVE => (RosterBadgeSeverity.Warning, "!", Text("RosterBadgeWatchNegative", "Watch"), "RosterBadgeWatchNegativeTip"),
                    WatchStatus.CHEATER => (RosterBadgeSeverity.Critical, "⚠", Text("RosterBadgeWatchCheater", "Suspicious"), "RosterBadgeWatchCheaterTip"),
                    _ => (RosterBadgeSeverity.Neutral, "•", Text("RosterBadgeWatch", "Watch"), "RosterBadgeWatchTip")
                };
                badges.Add(new(RosterBadgeKind.Watch, severity, compact, display, Text(resourceKey, display)));
            }

            if (player.IsCustomMarked)
                badges.Add(new(RosterBadgeKind.CustomMark, RosterBadgeSeverity.Warning, "★", Text("RosterBadgeMarked", "Marked"), Text("RosterBadgeMarkedTip", "Personal marker")));

            if (player.IsFixedTeammate && options.ShowFixedTeammateBadges)
                badges.Add(new(RosterBadgeKind.FixedTeammate, RosterBadgeSeverity.Positive, "◆", Text("RosterBadgeTeammate", "Teammate"), Text("RosterBadgeTeammateTip", "Fixed teammate")));
            else if (player.RecentEncounterCount > 0 && options.ShowRecentEncounterBadges)
                badges.Add(new(RosterBadgeKind.RecentEncounter, RosterBadgeSeverity.Info, "↻", $"↻{player.RecentEncounterCount}", BuildRecentEncounterTooltip(player)));

            if (player.IsHidden)
                badges.Add(new(RosterBadgeKind.Hidden, RosterBadgeSeverity.Neutral, "⊘", Text("RosterBadgeHidden", "Hidden"), Text("RosterStateHidden", "Hidden stats")));
            if (player.IsDataStale && options.ShowCachedDataBadges)
                badges.Add(new(RosterBadgeKind.Cached, RosterBadgeSeverity.Info, "◷", Text("RosterBadgeCached", "Cached"), Text("RosterBadgeCachedTip", "Showing cached data while refreshing")));
            if (player.IsLowTierBiased)
                badges.Add(new(RosterBadgeKind.LowTierBias, RosterBadgeSeverity.Warning, Text("RosterBadgeSealClubCompact", "屠"), Text("RosterBadgeSealClub", "屠幼"), BuildSealClubTooltip(player)));
            if (!player.IsHidden && player.AccountWinrate < 0)
                badges.Add(new(RosterBadgeKind.Loading, RosterBadgeSeverity.Info, "…", Text("RosterBadgeLoading", "Loading"), Text("RosterStateLoading", "Waiting for statistics")));

            return badges;
        }

        private static PerformanceCellViewModel BuildPerformanceCell(Player player, RosterPresentationOptions options)
        {
            bool hidden = player.IsHidden;
            double winrate = Properties.Settings.Default.WinrateTypeUsed == 0
                ? player.AccountWinrate
                : player.WeightedWinrate;
            double value = options.PerformanceMetric == RosterPerformanceMetric.PR ? player.PR : winrate;
            bool available = !hidden && value >= 0;
            PlayerSkillBand band = options.PerformanceMetric == RosterPerformanceMetric.PR
                ? PlayerSkillBandUtils.FromPr(available ? value : -1)
                : PlayerSkillBandFromWinrate(available ? value : -1);
            string background = Properties.Settings.Default.ColorStyle == 0 || !available
                ? "#B7C0CA"
                : options.PerformanceMetric == RosterPerformanceMetric.PR
                    ? SkillBandColor(band)
                    : WinrateColor(value, Properties.Settings.Default.ColorStyle);
            string foreground = band is PlayerSkillBand.Average or PlayerSkillBand.Good ? "#202A34" : "#FFFFFF";
            string icon = hidden ? Properties.Settings.Default.HiddenIcon : "";

            if (!hidden && options.ShowLegacyPerformanceTag && options.LegacyTagVisibility != 2)
            {
                if (winrate >= 0 && winrate <= Properties.Settings.Default.ApeWinrateThreshold / 100 &&
                    player.Battles >= Properties.Settings.Default.ApeBattleCountThreshold)
                    icon = Properties.Settings.Default.ApeIcon;
                else if (winrate > Properties.Settings.Default.UnicumWinrateThreshold / 100 &&
                    player.Battles >= Properties.Settings.Default.UnicumBattleCountThreshold)
                    icon = Properties.Settings.Default.UnicumIcon;
            }

            string metricName = options.PerformanceMetric == RosterPerformanceMetric.PR
                ? Text("RosterPerformancePR", "Account PR")
                : Properties.Settings.Default.WinrateTypeUsed == 0
                    ? Text("RosterPerformanceAccountWinrate", "Account win rate")
                    : Text("RosterPerformanceWeightedWinrate", "Weighted win rate");
            string formatted = !available ? "—" : options.PerformanceMetric == RosterPerformanceMetric.PR
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("P1", CultureInfo.CurrentCulture);
            string bandText = SkillBandText(band);
            string tooltip = !available
                ? Text("RosterSkillUnavailableTip", "Performance data is unavailable")
                : string.Format(Text("RosterPerformanceTip", "{0} {1}: {2}"), metricName, formatted, bandText);
            double iconOpacity = hidden || options.LegacyTagVisibility != 1 ? 1 : 0.55;
            return new(options.PerformanceMetric, available ? value : null, band, background, foreground, icon, iconOpacity,
                tooltip, tooltip);
        }

        private static PlayerSkillBand PlayerSkillBandFromWinrate(double value) => value switch
        {
            < 0 => PlayerSkillBand.Unavailable,
            < 0.47 => PlayerSkillBand.Bad,
            < 0.49 => PlayerSkillBand.BelowAverage,
            < 0.52 => PlayerSkillBand.Average,
            < 0.54 => PlayerSkillBand.Good,
            < 0.56 => PlayerSkillBand.VeryGood,
            < 0.60 => PlayerSkillBand.Great,
            < 0.65 => PlayerSkillBand.Unicum,
            _ => PlayerSkillBand.SuperUnicum
        };

        private static string SkillBandColor(PlayerSkillBand band) => band switch
        {
            PlayerSkillBand.Bad => "#D92D20",
            PlayerSkillBand.BelowAverage => "#E86B19",
            PlayerSkillBand.Average => "#E8B510",
            PlayerSkillBand.Good => "#55A630",
            PlayerSkillBand.VeryGood => "#2F7D32",
            PlayerSkillBand.Great => "#089E91",
            PlayerSkillBand.Unicum => "#A43AC2",
            PlayerSkillBand.SuperUnicum => "#7A1593",
            _ => "#B7C0CA"
        };

        private static string WinrateColor(double value, int colorStyle)
        {
            if (colorStyle == 1)
            {
                return value switch
                {
                    > 0.60 => "#D042F3",
                    > 0.52 => "#318000",
                    > 0.47 => "#FFC71F",
                    _ => "#FE0E00"
                };
            }
            if (colorStyle == 3)
            {
                double hue = value <= 0.47 ? 0 : value >= 0.65 ? 0.8 : (value - 0.47) / 0.18 * 0.8;
                return HslColor(hue, 1, 0.5);
            }
            return SkillBandColor(PlayerSkillBandFromWinrate(value));
        }

        private static string HslColor(double hue, double saturation, double lightness)
        {
            double q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
            double p = 2 * lightness - q;
            double Channel(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1d / 6) return p + (q - p) * 6 * t;
                if (t < 1d / 2) return q;
                if (t < 2d / 3) return p + (q - p) * (2d / 3 - t) * 6;
                return p;
            }
            int r = (int)Math.Round(Channel(hue + 1d / 3) * 255);
            int g = (int)Math.Round(Channel(hue) * 255);
            int b = (int)Math.Round(Channel(hue - 1d / 3) * 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static string BuildContextPreview(Player player)
        {
            if (!string.IsNullOrWhiteSpace(player.Note)) return $"{Text("LabelNote", "Note")}: {player.Note}";
            if (player.RecentEncounterCount > 0) return string.Format(Text("RosterRecentEncounterPreview", "Met recently {0} times"), player.RecentEncounterCount);
            if (player.IsFixedTeammate) return Text("RosterBadgeTeammateTip", "Fixed teammate");
            return "";
        }

        private static string BuildRecentEncounterTooltip(Player player)
        {
            string title = string.Format(Text("RosterRecentEncounterPreview", "Met recently {0} times"), player.RecentEncounterCount);
            return string.IsNullOrWhiteSpace(player.RecentEncounterDetails) ? title : $"{title}{Environment.NewLine}{player.RecentEncounterDetails}";
        }

        private static string BuildSealClubTooltip(Player player)
        {
            string format = Text("RosterBadgeSealClubTip",
                "Low tiers: {0:N0} games, {1:P1}, PR {2:N0}; high tiers: {3:N0} games, {4:P1}, PR {5:N0}.");
            return string.Format(format, player.LowTierBattles, player.LowTierWinrate, player.LowTierPR,
                player.HighTierBattles, player.HighTierWinrate, player.HighTierPR);
        }

        private static string SkillBandText(PlayerSkillBand band) => band switch
        {
            PlayerSkillBand.Bad => Text("RosterSkillBad", "Bad"),
            PlayerSkillBand.BelowAverage => Text("RosterSkillBelowAverage", "Below average"),
            PlayerSkillBand.Average => Text("RosterSkillAverage", "Average"),
            PlayerSkillBand.Good => Text("RosterSkillGood", "Good"),
            PlayerSkillBand.VeryGood => Text("RosterSkillVeryGood", "Very good"),
            PlayerSkillBand.Great => Text("RosterSkillGreat", "Great"),
            PlayerSkillBand.Unicum => Text("RosterSkillUnicum", "Unicum"),
            PlayerSkillBand.SuperUnicum => Text("RosterSkillSuperUnicum", "Super unicum"),
            _ => "—"
        };

        private static RosterMetricColumnViewModel Column(RosterColumnKind kind, params MetricItemViewModel?[] items) =>
            new(kind, items);

        private static MetricItemViewModel? Percent(string label, double value, RosterMetricKind kind, int visibility, RosterMetricEmphasis emphasis = RosterMetricEmphasis.Secondary) =>
            visibility == 2 ? null : new(label, Format(value, "P1"), value, kind, value >= 0, Opacity(visibility), emphasis);

        private static MetricItemViewModel? Number(string label, double value, string format, RosterMetricKind kind, int visibility, double? colorScore = null, RosterMetricEmphasis emphasis = RosterMetricEmphasis.Secondary) =>
            visibility == 2 ? null : new(label, Format(value, format), colorScore ?? value, kind, value >= 0, Opacity(visibility), emphasis);

        private static double Opacity(int visibility) => visibility == 1 ? 0.55 : 1;

        private static string Format(double value, string format) =>
            value < 0 ? "—" : value.ToString(format, CultureInfo.CurrentCulture);

        private static string Text(string resourceKey, string fallback) =>
            Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }
}
