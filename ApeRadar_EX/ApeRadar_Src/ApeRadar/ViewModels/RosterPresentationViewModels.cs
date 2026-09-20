using ApeRadar.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ApeRadar.ViewModels
{
    internal enum RosterColumnKind
    {
        Player,
        Account,
        Weighted,
        Ship,
        PersonalRating,
        Tier,
        Performance
    }

    internal enum PlayerSkillBand
    {
        Unavailable,
        Bad,
        BelowAverage,
        Average,
        Good,
        VeryGood,
        Great,
        Unicum,
        SuperUnicum
    }

    internal static class PlayerSkillBandUtils
    {
        public static PlayerSkillBand FromPr(double pr) => pr switch
        {
            < 0 => PlayerSkillBand.Unavailable,
            < 750 => PlayerSkillBand.Bad,
            < 1100 => PlayerSkillBand.BelowAverage,
            < 1350 => PlayerSkillBand.Average,
            < 1550 => PlayerSkillBand.Good,
            < 1750 => PlayerSkillBand.VeryGood,
            < 2100 => PlayerSkillBand.Great,
            < 2450 => PlayerSkillBand.Unicum,
            _ => PlayerSkillBand.SuperUnicum
        };
    }

    internal enum RosterDisplayDensity
    {
        Compact,
        Standard,
        Comfortable
    }

    internal enum RosterPerformanceMetric
    {
        PR,
        Winrate
    }

    internal static class RosterPerformanceMetricExtensions
    {
        public static RosterPerformanceMetric Parse(string? value) =>
            Enum.TryParse(value, ignoreCase: true, out RosterPerformanceMetric metric)
                ? metric
                : RosterPerformanceMetric.PR;

        public static string ToSettingValue(this RosterPerformanceMetric metric) => metric.ToString();
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
        Loading
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

    internal sealed class RosterMetricColumnViewModel
    {
        public RosterColumnKind Kind { get; }
        public IReadOnlyList<MetricItemViewModel> Items { get; }
        public int MaximumLineCount => Items.Count;
        public bool IsVisible => Items.Count > 0;

        public RosterMetricColumnViewModel(RosterColumnKind kind, IEnumerable<MetricItemViewModel?> items)
        {
            Kind = kind;
            Items = items.Where(item => item != null).Cast<MetricItemViewModel>().ToArray();
        }
    }

    internal sealed record RosterStatusBadgeViewModel(
        RosterBadgeKind Kind,
        RosterBadgeSeverity Severity,
        string CompactText,
        string DisplayText,
        string ToolTip,
        double Opacity = 1);

    internal sealed record PerformanceCellViewModel(
        RosterPerformanceMetric Metric,
        double? RawValue,
        PlayerSkillBand Band,
        string BackgroundColor,
        string ForegroundColor,
        string Icon,
        double IconOpacity,
        string ToolTip,
        string AccessibleName)
    {
        public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
    }

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
        public RosterMetricColumnViewModel AccountMetrics { get; }
        public RosterMetricColumnViewModel WeightedMetrics { get; }
        public RosterMetricColumnViewModel ShipMetrics { get; }
        public RosterMetricColumnViewModel PersonalRatingMetrics { get; }
        public RosterMetricColumnViewModel TierMetrics { get; }
        public string ContextPreview { get; }
        public IReadOnlyList<RosterStatusBadgeViewModel> StatusBadges { get; }
        public IReadOnlyList<RosterStatusBadgeViewModel> VisibleStatusBadges { get; }
        public int OverflowBadgeCount { get; }
        public string OverflowBadgeText => OverflowBadgeCount > 0 ? $"+{OverflowBadgeCount}" : "";
        public string AllStatusToolTip { get; }
        public bool HasOverflowBadges => OverflowBadgeCount > 0;
        public PlayerDetailCardViewModel Detail { get; }
        public PlayerSkillBand SkillBand { get; }
        public string SkillBandText { get; }
        public string SkillBandToolTip { get; }
        public PerformanceCellViewModel Performance { get; }

        public PlayerRosterRowViewModel(
            Player player,
            RosterMetricColumnViewModel accountMetrics,
            RosterMetricColumnViewModel weightedMetrics,
            RosterMetricColumnViewModel shipMetrics,
            RosterMetricColumnViewModel personalRatingMetrics,
            RosterMetricColumnViewModel tierMetrics,
            string contextPreview,
            IReadOnlyList<RosterStatusBadgeViewModel> statusBadges,
            PlayerSkillBand skillBand,
            string skillBandText,
            string skillBandToolTip,
            PerformanceCellViewModel performance)
        {
            Player = player;
            AccountMetrics = accountMetrics;
            WeightedMetrics = weightedMetrics;
            ShipMetrics = shipMetrics;
            PersonalRatingMetrics = personalRatingMetrics;
            TierMetrics = tierMetrics;
            ContextPreview = contextPreview;
            StatusBadges = statusBadges;
            VisibleStatusBadges = statusBadges.Take(VisibleBadgeLimit).ToArray();
            OverflowBadgeCount = Math.Max(0, statusBadges.Count - VisibleBadgeLimit);
            AllStatusToolTip = string.Join(Environment.NewLine, statusBadges.Select(badge => badge.ToolTip));
            Detail = new PlayerDetailCardViewModel(player, statusBadges);
            SkillBand = skillBand;
            SkillBandText = skillBandText;
            SkillBandToolTip = skillBandToolTip;
            Performance = performance;
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
        int LegacyTagVisibility = 2,
        bool ShowAccountColumn = true,
        bool ShowShipColumn = true,
        bool ShowPerformanceColumn = true,
        bool ShowRecentEncounterBadges = true,
        bool ShowFixedTeammateBadges = true,
        bool ShowCachedDataBadges = true,
        RosterPerformanceMetric PerformanceMetric = RosterPerformanceMetric.PR)
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
            Properties.Settings.Default.TagVisibility,
            Properties.Settings.Default.ShowAccountRosterColumn,
            Properties.Settings.Default.ShowShipRosterColumn,
            Properties.Settings.Default.ShowPerformanceRosterColumn,
            Properties.Settings.Default.ShowRecentEncounterBadges,
            Properties.Settings.Default.ShowFixedTeammateBadges,
            Properties.Settings.Default.ShowCachedDataBadges,
            RosterPerformanceMetricExtensions.Parse(Properties.Settings.Default.RosterPerformanceMetric));
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
