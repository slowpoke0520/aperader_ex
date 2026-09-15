using ApeRadar.Models;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ApeRadar.ViewModels
{
    internal enum RosterMetricKind
    {
        Neutral,
        Winrate,
        PersonalRating,
        DamageRating,
        Warning
    }

    internal sealed record MetricItemViewModel(
        string Label,
        string DisplayValue,
        double? ColorScore,
        RosterMetricKind Kind,
        bool IsAvailable = true,
        double Opacity = 1);

    internal sealed record MetricLineViewModel(IReadOnlyList<MetricItemViewModel> Items);

    internal sealed class MetricGroupViewModel
    {
        public IReadOnlyList<MetricLineViewModel> Lines { get; }
        public bool IsVisible => Lines.Count > 0;

        public MetricGroupViewModel(IEnumerable<MetricLineViewModel> lines)
        {
            Lines = lines.Where(line => line.Items.Count > 0).ToArray();
        }
    }

    internal sealed class PlayerRosterRowViewModel
    {
        public Player Player { get; }
        public MetricGroupViewModel AccountMetrics { get; }
        public MetricGroupViewModel ShipMetrics { get; }
        public MetricGroupViewModel TierMetrics { get; }
        public string StatusSummary { get; }
        public bool HasAttention { get; }

        public PlayerRosterRowViewModel(
            Player player,
            MetricGroupViewModel accountMetrics,
            MetricGroupViewModel shipMetrics,
            MetricGroupViewModel tierMetrics,
            string statusSummary,
            bool hasAttention)
        {
            Player = player;
            AccountMetrics = accountMetrics;
            ShipMetrics = shipMetrics;
            TierMetrics = tierMetrics;
            StatusSummary = statusSummary;
            HasAttention = hasAttention;
        }
    }

    internal sealed record RosterPresentationOptions(
        int AccountVisibility,
        int WeightedVisibility,
        int ShipVisibility,
        int AccountAverageExperienceVisibility,
        int ShipAverageExperienceVisibility,
        int ShipAverageDamageVisibility,
        int PersonalRatingVisibility,
        bool ShowTierPerformance)
    {
        public static RosterPresentationOptions FromCurrentSettings() => new(
            Properties.Settings.Default.AccountWinrateVisibility,
            Properties.Settings.Default.WeightedWinrateVisibility,
            Properties.Settings.Default.ShipWinrateVisibility,
            Properties.Settings.Default.AccountAvgExpVisibility,
            Properties.Settings.Default.ShipAvgExpVisibility,
            Properties.Settings.Default.ShipAvgDmgVisibility,
            Properties.Settings.Default.PRVisibility,
            Properties.Settings.Default.ShowTierPerformanceStats);
    }

    internal sealed class PlayerRosterRowComparer : IComparer
    {
        private readonly IComparer playerComparer;

        public PlayerRosterRowComparer(IComparer playerComparer)
        {
            this.playerComparer = playerComparer;
        }

        public int Compare(object? x, object? y)
        {
            Player? left = (x as PlayerRosterRowViewModel)?.Player;
            Player? right = (y as PlayerRosterRowViewModel)?.Player;
            if (left == null || right == null) return left == right ? 0 : left == null ? 1 : -1;
            return playerComparer.Compare(left, right);
        }
    }
}
