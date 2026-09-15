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
            MetricGroupViewModel account = Group(
                Line(
                    Percent(Text("RosterMetricWinrate", "WR"), player.AccountWinrate, RosterMetricKind.Winrate, options.AccountVisibility),
                    Number("PR", player.PR, "N0", RosterMetricKind.PersonalRating, options.PersonalRatingVisibility)),
                Line(
                    Number(Text("RosterMetricBattles", "Games"), player.Battles, "N0", RosterMetricKind.Neutral, options.AccountVisibility),
                    Number(Text("RosterMetricAvgExp", "XP"), player.AvgExpPerBattle, "N0", RosterMetricKind.Neutral, options.AccountAverageExperienceVisibility)),
                Line(Percent(Text("RosterMetricWeighted", "Adjusted"), player.WeightedWinrate, RosterMetricKind.Winrate, options.WeightedVisibility)));

            double damageRating = player.ShipBattles > 0 && player.ShipAvgDmgPerBattle >= 0
                ? PRUtils.CalculateDamageRating(player.ShipID, player.ShipBattles, player.ShipAvgDmgPerBattle * player.ShipBattles)
                : -1;
            MetricGroupViewModel ship = Group(
                Line(
                    Percent(Text("RosterMetricWinrate", "WR"), player.ShipWinrate, RosterMetricKind.Winrate, options.ShipVisibility),
                    Number("PR", player.ShipPR, "N0", RosterMetricKind.PersonalRating, options.PersonalRatingVisibility)),
                Line(
                    Number(Text("RosterMetricBattles", "Games"), player.ShipBattles, "N0", RosterMetricKind.Neutral, options.ShipVisibility),
                    Number(Text("RosterMetricAvgDamage", "Dmg"), player.ShipAvgDmgPerBattle, "N0", RosterMetricKind.DamageRating, options.ShipAverageDamageVisibility, damageRating)),
                Line(Number(Text("RosterMetricAvgExp", "XP"), player.ShipAvgExpPerBattle, "N0", RosterMetricKind.Neutral, options.ShipAverageExperienceVisibility)));

            MetricGroupViewModel tier = options.ShowTierPerformance
                ? Group(
                    Line(
                        Percent(Text("RosterMetricWinrate", "WR"), player.TierWinrate, RosterMetricKind.Winrate, 0),
                        Number("PR", player.TierPR, "N0", RosterMetricKind.PersonalRating, 0)),
                    Line(
                        TextValue(Text("RosterMetricTier", "Tier"), RomanTier(player.ShipTier), player.ShipTier > 0),
                        Number(Text("RosterMetricBattles", "Games"), player.TierBattles, "N0", RosterMetricKind.Neutral, 0)),
                    Line(StatusMetric(player)))
                : Group();

            bool attention = player.IsHidden || player.IsDataStale || player.IsTierSampleSmall || player.IsLowTierBiased;
            return new PlayerRosterRowViewModel(player, account, ship, tier, BuildStatusSummary(player), attention);
        }

        private static MetricItemViewModel? StatusMetric(Player player)
        {
            if (player.IsTierSampleSmall)
                return TextValue("", Text("RosterStateSmallSample", "Small sample"), true, RosterMetricKind.Warning);
            if (player.TierBattles < 0)
                return TextValue("", Text("RosterStateLoading", "Waiting for data"), true);
            return TextValue("", Text("RosterStateSampleReady", "Sample available"), true);
        }

        private static string BuildStatusSummary(Player player)
        {
            List<string> status = new();
            if (player.IsHidden) status.Add(Text("RosterStateHidden", "Hidden stats"));
            if (player.IsDataStale) status.Add(Text("RosterStateCached", "Cached data"));
            if (player.IsTierSampleSmall) status.Add(Text("RosterStateSmallSample", "Small tier sample"));
            if (player.IsLowTierBiased) status.Add(Text("RosterStateLowTier", "Low-tier heavy"));
            if (status.Count == 0)
            {
                status.Add(player.AccountWinrate < 0
                    ? Text("RosterStateLoading", "Waiting for statistics")
                    : Text("RosterStateReady", "Statistics ready"));
            }
            return string.Join(" · ", status);
        }

        private static MetricGroupViewModel Group(params MetricLineViewModel?[] lines) =>
            new(lines.Where(line => line != null).Cast<MetricLineViewModel>());

        private static MetricLineViewModel? Line(params MetricItemViewModel?[] items)
        {
            MetricItemViewModel[] visible = items.Where(item => item != null).Cast<MetricItemViewModel>().ToArray();
            return visible.Length == 0 ? null : new MetricLineViewModel(visible);
        }

        private static MetricItemViewModel? Percent(string label, double value, RosterMetricKind kind, int visibility) =>
            visibility == 2 ? null : new(label, Format(value, "P1"), value, kind, value >= 0, Opacity(visibility));

        private static MetricItemViewModel? Number(string label, double value, string format, RosterMetricKind kind, int visibility, double? colorScore = null) =>
            visibility == 2 ? null : new(label, Format(value, format), colorScore ?? value, kind, value >= 0, Opacity(visibility));

        private static MetricItemViewModel TextValue(string label, string value, bool available, RosterMetricKind kind = RosterMetricKind.Neutral) =>
            new(label, available ? value : "—", null, kind, available);

        private static double Opacity(int visibility) => visibility == 1 ? 0.55 : 1;

        private static string Format(double value, string format) =>
            value < 0 ? "—" : value.ToString(format, CultureInfo.CurrentCulture);

        private static string Text(string resourceKey, string fallback) =>
            Application.Current?.TryFindResource(resourceKey) as string ?? fallback;

        private static string RomanTier(int tier)
        {
            string[] roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "★" };
            return tier >= 1 && tier < roman.Length ? roman[tier] : "—";
        }
    }
}
