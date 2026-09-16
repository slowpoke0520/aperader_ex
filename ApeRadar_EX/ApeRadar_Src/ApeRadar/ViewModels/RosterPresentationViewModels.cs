using ApeRadar.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ApeRadar.ViewModels
{
    internal enum RosterDisplayDensity
    {
        Compact,
        Standard,
        Comfortable
    }

    internal static class RosterDisplayDensityExtensions
    {
        public static RosterDisplayDensity Parse(string? value) =>
            Enum.TryParse(value, ignoreCase: true, out RosterDisplayDensity density)
                ? density
                : RosterDisplayDensity.Standard;

        public static string ToSettingValue(this RosterDisplayDensity density) => density.ToString();
    }

    internal enum RosterMetricKind
    {
        Neutral,
        Winrate,
        PersonalRating,
        DamageRating,
        Warning
    }

    internal enum RosterMetricEmphasis
    {
        Primary,
        Secondary,
        Tertiary
    }

    internal enum RosterBadgeKind
    {
        Watch,
        CustomMark,
        RecentEncounter,
        FixedTeammate,
        Hidden,
        Cached,
        LowTierBias,
        Loading,
        LegacySkill
    }

    internal enum RosterBadgeSeverity
    {
        Neutral,
        Info,
        Positive,
        Warning,
        Critical
    }

    internal sealed record MetricItemViewModel(
        string Label,
        string DisplayValue,
        double? ColorScore,
        RosterMetricKind Kind,
        bool IsAvailable = true,
        double Opacity = 1,
        RosterMetricEmphasis Emphasis = RosterMetricEmphasis.Secondary,
        string WarningGlyph = "",
        string ToolTip = "");

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

    internal sealed record RosterStatusBadgeViewModel(
        RosterBadgeKind Kind,
        RosterBadgeSeverity Severity,
        string CompactText,
        string DisplayText,
        string ToolTip,
        double Opacity = 1);

    internal sealed class PlayerDetailCardViewModel
    {
        public Player Player { get; }
        public IReadOnlyList<RosterStatusBadgeViewModel> StatusBadges { get; }
        public string IdentityKey { get; }

        public PlayerDetailCardViewModel(Player player, IReadOnlyList<RosterStatusBadgeViewModel> statusBadges)
        {
            Player = player;
            StatusBadges = statusBadges;
            IdentityKey = $"{player.Server}:{player.ID}:{player.ShipID}";
        }
    }

    internal sealed class PlayerRosterRowViewModel
    {
        private const int VisibleBadgeLimit = 3;

        public Player Player { get; }
        public MetricGroupViewModel AccountMetrics { get; }
        public MetricGroupViewModel ShipMetrics { get; }
        public MetricGroupViewModel TierMetrics { get; }
        public string ContextPreview { get; }
        public IReadOnlyList<RosterStatusBadgeViewModel> StatusBadges { get; }
        public IReadOnlyList<RosterStatusBadgeViewModel> VisibleStatusBadges { get; }
        public int OverflowBadgeCount { get; }
        public string OverflowBadgeText => OverflowBadgeCount > 0 ? $"+{OverflowBadgeCount}" : "";
        public string AllStatusToolTip { get; }
        public bool HasOverflowBadges => OverflowBadgeCount > 0;
        public PlayerDetailCardViewModel Detail { get; }

        public PlayerRosterRowViewModel(
            Player player,
            MetricGroupViewModel accountMetrics,
            MetricGroupViewModel shipMetrics,
            MetricGroupViewModel tierMetrics,
            string contextPreview,
            IReadOnlyList<RosterStatusBadgeViewModel> statusBadges)
        {
            Player = player;
            AccountMetrics = accountMetrics;
            ShipMetrics = shipMetrics;
            TierMetrics = tierMetrics;
            ContextPreview = contextPreview;
            StatusBadges = statusBadges;
            VisibleStatusBadges = statusBadges.Take(VisibleBadgeLimit).ToArray();
            OverflowBadgeCount = Math.Max(0, statusBadges.Count - VisibleBadgeLimit);
            AllStatusToolTip = string.Join(Environment.NewLine, statusBadges.Select(badge => badge.ToolTip));
            Detail = new PlayerDetailCardViewModel(player, statusBadges);
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
        bool ShowTierPerformance,
        RosterDisplayDensity DisplayDensity = RosterDisplayDensity.Standard,
        bool ShowLegacyPerformanceTag = false,
        int LegacyTagVisibility = 2)
    {
        public static RosterPresentationOptions FromCurrentSettings() => new(
            Properties.Settings.Default.AccountWinrateVisibility,
            Properties.Settings.Default.WeightedWinrateVisibility,
            Properties.Settings.Default.ShipWinrateVisibility,
            Properties.Settings.Default.AccountAvgExpVisibility,
            Properties.Settings.Default.ShipAvgExpVisibility,
            Properties.Settings.Default.ShipAvgDmgVisibility,
            Properties.Settings.Default.PRVisibility,
            Properties.Settings.Default.ShowTierPerformanceStats,
            RosterDisplayDensityExtensions.Parse(Properties.Settings.Default.RosterDisplayDensity),
            Properties.Settings.Default.ShowLegacyPerformanceTag,
            Properties.Settings.Default.TagVisibility);
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
