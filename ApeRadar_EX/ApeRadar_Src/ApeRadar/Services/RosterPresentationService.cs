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
            bool compact = options.DisplayDensity == RosterDisplayDensity.Compact;
            MetricGroupViewModel account = Group(
                Line(
                    Percent(Text("RosterMetricWinrate", "WR"), player.AccountWinrate, RosterMetricKind.Winrate, options.AccountVisibility, RosterMetricEmphasis.Primary),
                    Number("PR", player.PR, "N0", RosterMetricKind.PersonalRating, options.PersonalRatingVisibility, emphasis: RosterMetricEmphasis.Primary)),
                Line(
                    Number(Text("RosterMetricBattles", "Games"), player.Battles, "N0", RosterMetricKind.Neutral, options.AccountVisibility),
                    compact ? null : Number(Text("RosterMetricAvgExp", "XP"), player.AvgExpPerBattle, "N0", RosterMetricKind.Neutral, options.AccountAverageExperienceVisibility)),
                compact ? null : Line(Percent(Text("RosterMetricWeighted", "Adjusted"), player.WeightedWinrate, RosterMetricKind.Winrate, options.WeightedVisibility, RosterMetricEmphasis.Tertiary)));

            double damageRating = player.ShipBattles > 0 && player.ShipAvgDmgPerBattle >= 0
                ? PRUtils.CalculateDamageRating(player.ShipID, player.ShipBattles, player.ShipAvgDmgPerBattle * player.ShipBattles)
                : -1;
            MetricGroupViewModel ship = Group(
                Line(
                    Percent(Text("RosterMetricWinrate", "WR"), player.ShipWinrate, RosterMetricKind.Winrate, options.ShipVisibility, RosterMetricEmphasis.Primary),
                    Number("PR", player.ShipPR, "N0", RosterMetricKind.PersonalRating, options.PersonalRatingVisibility, emphasis: RosterMetricEmphasis.Primary)),
                Line(
                    Number(Text("RosterMetricBattles", "Games"), player.ShipBattles, "N0", RosterMetricKind.Neutral, options.ShipVisibility),
                    Number(Text("RosterMetricAvgDamage", "Dmg"), player.ShipAvgDmgPerBattle, "N0", RosterMetricKind.DamageRating, options.ShipAverageDamageVisibility, damageRating)),
                compact ? null : Line(Number(Text("RosterMetricAvgExp", "XP"), player.ShipAvgExpPerBattle, "N0", RosterMetricKind.Neutral, options.ShipAverageExperienceVisibility, emphasis: RosterMetricEmphasis.Tertiary)));

            MetricGroupViewModel tier = options.ShowTierPerformance
                ? Group(
                    Line(
                        Percent(Text("RosterMetricWinrate", "WR"), player.TierWinrate, RosterMetricKind.Winrate, 0, RosterMetricEmphasis.Primary),
                        Number("PR", player.TierPR, "N0", RosterMetricKind.PersonalRating, 0, emphasis: RosterMetricEmphasis.Primary)),
                    Line(TierBattleMetric(player)))
                : Group();

            IReadOnlyList<RosterStatusBadgeViewModel> badges = BuildStatusBadges(player, options);
            return new PlayerRosterRowViewModel(player, account, ship, tier, BuildContextPreview(player), badges);
        }

        private static MetricItemViewModel TierBattleMetric(Player player)
        {
            string warningGlyph = player.IsTierSampleSmall && player.TierBattles >= 0 ? "⚠" : "";
            string tooltip = player.IsTierSampleSmall ? Text("TierStatsSmallSample", "Small same-tier sample") : "";
            return new MetricItemViewModel(
                Text("RosterMetricBattles", "Games"),
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

            if (player.IsFixedTeammate)
                badges.Add(new(RosterBadgeKind.FixedTeammate, RosterBadgeSeverity.Positive, "◆", Text("RosterBadgeTeammate", "Teammate"), Text("RosterBadgeTeammateTip", "Fixed teammate")));
            else if (player.RecentEncounterCount > 0)
                badges.Add(new(RosterBadgeKind.RecentEncounter, RosterBadgeSeverity.Info, "↻", $"↻{player.RecentEncounterCount}", BuildRecentEncounterTooltip(player)));

            if (player.IsHidden)
                badges.Add(new(RosterBadgeKind.Hidden, RosterBadgeSeverity.Neutral, "⊘", Text("RosterBadgeHidden", "Hidden"), Text("RosterStateHidden", "Hidden stats")));
            if (player.IsDataStale)
                badges.Add(new(RosterBadgeKind.Cached, RosterBadgeSeverity.Info, "◷", Text("RosterBadgeCached", "Cached"), Text("RosterBadgeCachedTip", "Showing cached data while refreshing")));
            if (player.IsLowTierBiased)
                badges.Add(new(RosterBadgeKind.LowTierBias, RosterBadgeSeverity.Warning, "⚠", Text("RosterBadgeLowTier", "Low tier"), Text("TierStatsLowTierBias", "Low-tier heavy record")));
            if (!player.IsHidden && player.AccountWinrate < 0)
                badges.Add(new(RosterBadgeKind.Loading, RosterBadgeSeverity.Info, "…", Text("RosterBadgeLoading", "Loading"), Text("RosterStateLoading", "Waiting for statistics")));

            RosterStatusBadgeViewModel? legacy = BuildLegacyBadge(player, options);
            if (legacy != null) badges.Add(legacy);
            return badges;
        }

        private static RosterStatusBadgeViewModel? BuildLegacyBadge(Player player, RosterPresentationOptions options)
        {
            if (!options.ShowLegacyPerformanceTag || options.LegacyTagVisibility == 2 || player.IsHidden) return null;
            double winrate = Properties.Settings.Default.WinrateTypeUsed == 0 ? player.AccountWinrate : player.WeightedWinrate;
            string icon;
            string tooltip;
            RosterBadgeSeverity severity;
            if (winrate >= 0 && winrate <= Properties.Settings.Default.ApeWinrateThreshold / 100 && player.Battles >= Properties.Settings.Default.ApeBattleCountThreshold)
            {
                icon = Properties.Settings.Default.ApeIcon;
                tooltip = Text("RosterBadgeLegacyLowTip", "Legacy low win-rate marker");
                severity = RosterBadgeSeverity.Critical;
            }
            else if (winrate > Properties.Settings.Default.UnicumWinrateThreshold / 100 && player.Battles >= Properties.Settings.Default.UnicumBattleCountThreshold)
            {
                icon = Properties.Settings.Default.UnicumIcon;
                tooltip = Text("RosterBadgeLegacyHighTip", "Legacy high win-rate marker");
                severity = RosterBadgeSeverity.Positive;
            }
            else
            {
                return null;
            }

            return new(RosterBadgeKind.LegacySkill, severity, icon, icon, tooltip, options.LegacyTagVisibility == 1 ? 0.55 : 1);
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

        private static MetricGroupViewModel Group(params MetricLineViewModel?[] lines) =>
            new(lines.Where(line => line != null).Cast<MetricLineViewModel>());

        private static MetricLineViewModel? Line(params MetricItemViewModel?[] items)
        {
            MetricItemViewModel[] visible = items.Where(item => item != null).Cast<MetricItemViewModel>().ToArray();
            return visible.Length == 0 ? null : new MetricLineViewModel(visible);
        }

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
