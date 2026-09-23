using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ApeRadar.ViewModels
{
    internal enum DashboardRosterContext
    {
        Account,
        Tier
    }

    internal enum DashboardRosterFilter
    {
        All,
        Marked,
        LowSample,
        Anomaly
    }

    internal sealed record DashboardBattleMetadata(
        string MapName,
        string Mode,
        string Server,
        DateTimeOffset StartedAt,
        string Provider,
        DateTimeOffset? UpdatedAt)
    {
        public static DashboardBattleMetadata Empty { get; } = new("—", "—", "—", DateTimeOffset.MinValue, "—", null);
        public string StartedAtText => StartedAt == DateTimeOffset.MinValue ? "—" : StartedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        public string UpdatedAtText => UpdatedAt?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
    }

    internal sealed record DashboardComparisonMetric(
        string Label,
        string AllyText,
        string EnemyText,
        string Direction,
        bool HasBothValues)
    {
        public static DashboardComparisonMetric Percentage(double? ally, double? enemy, string label) =>
            Create(label, ally, enemy, 3, value => value.ToString("P1", CultureInfo.CurrentCulture));

        public static DashboardComparisonMetric Integer(double? ally, double? enemy, string label) =>
            Create(label, ally, enemy, 0, value => Math.Round(value).ToString("N0", CultureInfo.CurrentCulture));

        private static DashboardComparisonMetric Create(string label, double? ally, double? enemy, int digits, Func<double, string> formatter)
        {
            string allyText = ally.HasValue ? formatter(ally.Value) : "—";
            string enemyText = enemy.HasValue ? formatter(enemy.Value) : "—";
            if (!ally.HasValue || !enemy.HasValue)
                return new(label, allyText, enemyText, "", false);

            double left = Math.Round(ally.Value, digits, MidpointRounding.AwayFromZero);
            double right = Math.Round(enemy.Value, digits, MidpointRounding.AwayFromZero);
            string direction = left > right ? "←" : right > left ? "→" : "=";
            return new(label, allyText, enemyText, direction, true);
        }
    }

    internal sealed class DashboardPlayerRowViewModel
    {
        private const int VisibleBadgeLimit = 3;

        public int OriginalOrder { get; }
        public Player Player { get; }
        public PlayerRosterRowViewModel BaseRow { get; }
        public DashboardRosterContext Context { get; }
        public bool LoadCompleted { get; }
        public IReadOnlyList<RosterStatusBadgeViewModel> StatusBadges { get; }
        public IReadOnlyList<RosterStatusBadgeViewModel> VisibleStatusBadges { get; }
        public int OverflowBadgeCount { get; }
        public string OverflowBadgeText => OverflowBadgeCount > 0 ? $"+{OverflowBadgeCount}" : "";
        public string AllStatusToolTip { get; }
        public string PlayerDisplayName => string.IsNullOrWhiteSpace(Player.ClanTag) ? Player.Name : $"[{Player.ClanTag}] {Player.Name}";
        public string KarmaText => Player.Karma >= 0 ? $"K {Player.Karma:N0}" : "";

        public bool HasValidAccount => !Player.IsHidden && Player.Battles >= 0 && Player.AccountWinrate >= 0 && Player.PR >= 0;
        public bool HasValidTier => !Player.IsHidden && Player.TierBattles >= 0 && Player.TierWinrate >= 0 && Player.TierPR >= 0;
        public bool HasValidShip => !Player.IsHidden && Player.ShipBattles >= 0 && Player.ShipWinrate >= 0 && Player.ShipPR >= 0;
        public bool IsMarked => Player.WatchStatus != WatchStatus.NONE || Player.IsCustomMarked;
        public bool IsShipLowSample => HasValidShip && Player.ShipBattles < 20;
        public bool IsTierLowSample => HasValidTier && Player.TierBattles < 50;
        public bool IsFetchFailed => LoadCompleted && !Player.IsHidden && Player.ID == "-1";
        public bool IsLoading => !LoadCompleted && !Player.IsHidden && Player.ID == "-1";
        public bool IsAnomaly => Player.IsHidden || IsFetchFailed || Player.IsLowTierBiased;

        public string ContextBattlesText => Format(Context == DashboardRosterContext.Account && HasValidAccount ? Player.Battles : Context == DashboardRosterContext.Tier && HasValidTier ? Player.TierBattles : -1, "N0");
        public MetricItemViewModel ContextWinrateMetric => Metric(
            Context == DashboardRosterContext.Account && HasValidAccount ? Player.AccountWinrate : Context == DashboardRosterContext.Tier && HasValidTier ? Player.TierWinrate : -1,
            "P1", RosterMetricKind.Winrate);
        public string ContextWarning => Context == DashboardRosterContext.Tier && IsTierLowSample ? "⚠" : "";
        public string ContextToolTip => Context == DashboardRosterContext.Tier && IsTierLowSample
            ? Text("TierStatsSmallSample", "Small same-tier sample")
            : "";

        public string ShipBattlesText => Format(HasValidShip ? Player.ShipBattles : -1, "N0");
        public MetricItemViewModel ShipWinrateMetric => Metric(HasValidShip ? Player.ShipWinrate : -1, "P1", RosterMetricKind.Winrate);
        public string ShipDamageText => Format(HasValidShip ? Player.ShipAvgDmgPerBattle : -1, "N0");
        public MetricItemViewModel AccountPrMetric => Metric(HasValidAccount ? Player.PR : -1, "N0", RosterMetricKind.PersonalRating);
        public MetricItemViewModel ShipPrMetric => Metric(HasValidShip ? Player.ShipPR : -1, "N0", RosterMetricKind.PersonalRating);
        public PerformanceCellViewModel Performance => BaseRow.Performance;

        public DashboardPlayerRowViewModel(
            int originalOrder,
            PlayerRosterRowViewModel baseRow,
            DashboardRosterContext context,
            bool loadCompleted)
        {
            OriginalOrder = originalOrder;
            BaseRow = baseRow;
            Player = baseRow.Player;
            Context = context;
            LoadCompleted = loadCompleted;

            List<RosterStatusBadgeViewModel> badges = new(baseRow.StatusBadges.Where(badge => badge.Kind != RosterBadgeKind.Loading));
            if (IsShipLowSample)
                badges.Add(new(RosterBadgeKind.LowSample, RosterBadgeSeverity.Warning, "低", Text("DashboardBadgeLowSample", "Low sample"), Text("DashboardBadgeLowSampleTip", "Current ship has fewer than 20 battles")));
            if (IsLoading)
                badges.Add(new(RosterBadgeKind.Loading, RosterBadgeSeverity.Info, "…", Text("RosterBadgeLoading", "Loading"), Text("RosterStateLoading", "Waiting for statistics")));
            else if (IsFetchFailed)
                badges.Add(new(RosterBadgeKind.FetchFailed, RosterBadgeSeverity.Critical, "!", Text("DashboardBadgeFetchFailed", "Failed"), Text("DashboardBadgeFetchFailedTip", "Player statistics could not be loaded")));

            StatusBadges = badges;
            VisibleStatusBadges = badges.Take(VisibleBadgeLimit).ToArray();
            OverflowBadgeCount = Math.Max(0, badges.Count - VisibleBadgeLimit);
            AllStatusToolTip = string.Join(Environment.NewLine, badges.Select(badge => badge.ToolTip));
        }

        private static MetricItemViewModel Metric(double value, string format, RosterMetricKind kind) =>
            new("", Format(value, format), value, kind, value >= 0, 1, RosterMetricEmphasis.Primary);

        private static string Format(double value, string format) =>
            value < 0 ? "—" : value.ToString(format, CultureInfo.CurrentCulture);

        private static string Text(string resourceKey, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }

    internal sealed class DashboardTeamSummary
    {
        public int TeamSize { get; init; }
        public int ContextValidCount { get; init; }
        public int ShipValidCount { get; init; }
        public int ShipLowSampleCount { get; init; }
        public int AnomalyCount { get; init; }
        public double? ContextWinrate { get; init; }
        public double? ContextPr { get; init; }
        public double? ShipWinrate { get; init; }
        public double? ShipPr { get; init; }
    }

    internal sealed class DashboardShipClassMatchup
    {
        public string Name { get; init; } = "";
        public int AllyCount { get; init; }
        public int EnemyCount { get; init; }
        public bool IsVisible => AllyCount + EnemyCount > 0;
        public DashboardComparisonMetric Winrate { get; init; } = DashboardComparisonMetric.Percentage(null, null, "");
        public DashboardComparisonMetric Pr { get; init; } = DashboardComparisonMetric.Integer(null, null, "");
        public string AllySummaryText => FormatSide(AllyCount, Winrate.AllyText, Pr.AllyText);
        public string EnemySummaryText => FormatSide(EnemyCount, Winrate.EnemyText, Pr.EnemyText);

        private static string FormatSide(int count, string winrate, string pr) =>
            count == 0 ? Text("DashboardNoShipClass", "None") : $"{count} · {winrate} · PR {pr}";

        private static string Text(string resourceKey, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }

    internal sealed class DashboardSummary
    {
        public DashboardTeamSummary Ally { get; init; } = new();
        public DashboardTeamSummary Enemy { get; init; } = new();
        public DashboardComparisonMetric ContextWinrate { get; init; } = DashboardComparisonMetric.Percentage(null, null, "");
        public DashboardComparisonMetric ContextPr { get; init; } = DashboardComparisonMetric.Integer(null, null, "");
        public DashboardComparisonMetric ShipWinrate { get; init; } = DashboardComparisonMetric.Percentage(null, null, "");
        public DashboardComparisonMetric ShipPr { get; init; } = DashboardComparisonMetric.Integer(null, null, "");
        public DashboardShipClassMatchup Carrier { get; init; } = new();
        public DashboardShipClassMatchup Destroyer { get; init; } = new();
        public bool HasKeyShipMatchup => Carrier.IsVisible || Destroyer.IsVisible;
        public string AllyCoverageText => $"{Ally.ContextValidCount}/{Ally.TeamSize}";
        public string EnemyCoverageText => $"{Enemy.ContextValidCount}/{Enemy.TeamSize}";
        public bool CoverageInsufficient => Insufficient(Ally.ContextValidCount, Ally.TeamSize) || Insufficient(Enemy.ContextValidCount, Enemy.TeamSize);
        public string CoverageNotice => CoverageInsufficient
            ? Text("DashboardCoverageInsufficient", "Coverage is incomplete; comparisons are for reference only")
            : Text("DashboardCoverageSufficient", "Data coverage is sufficient");
        public string CoverageObservation => $"{Text("DashboardObservationCoverage", "Coverage")}  {AllyCoverageText} · {EnemyCoverageText}" +
            (CoverageInsufficient ? $" · {Text("DashboardCoverageLowShort", "Low coverage")}" : "");
        public string LowSampleObservation => $"{Text("DashboardObservationLowSample", "Low sample")}  {Ally.ShipLowSampleCount} · {Enemy.ShipLowSampleCount}";
        public string AnomalyObservation => $"{Text("DashboardObservationAnomaly", "Anomaly")}  {Ally.AnomalyCount} · {Enemy.AnomalyCount}";

        private static bool Insufficient(int valid, int total) => total > 0 && valid * 3 < total * 2;
        private static string Text(string resourceKey, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }

    internal sealed class BattleDashboardViewModel : INotifyPropertyChanged
    {
        private readonly IDashboardPresentationService presentationService;
        private Battlefield? battlefield;
        private bool loadCompleted;
        private DashboardRosterContext context = DashboardRosterContext.Account;
        private DashboardRosterFilter allyFilter;
        private DashboardRosterFilter enemyFilter;
        private int sortMode;
        private IReadOnlyList<DashboardPlayerRowViewModel> allAllies = Array.Empty<DashboardPlayerRowViewModel>();
        private IReadOnlyList<DashboardPlayerRowViewModel> allEnemies = Array.Empty<DashboardPlayerRowViewModel>();
        private IReadOnlyList<DashboardPlayerRowViewModel> allies = Array.Empty<DashboardPlayerRowViewModel>();
        private IReadOnlyList<DashboardPlayerRowViewModel> enemies = Array.Empty<DashboardPlayerRowViewModel>();
        private DashboardSummary summary = new();
        private DashboardBattleMetadata metadata = DashboardBattleMetadata.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<DashboardPlayerRowViewModel> Allies { get => allies; private set => Set(ref allies, value); }
        public IReadOnlyList<DashboardPlayerRowViewModel> Enemies { get => enemies; private set => Set(ref enemies, value); }
        public DashboardSummary Summary { get => summary; private set => Set(ref summary, value); }
        public DashboardBattleMetadata Metadata { get => metadata; private set => Set(ref metadata, value); }
        public DashboardRosterContext Context => context;
        public DashboardRosterFilter AllyFilter => allyFilter;
        public DashboardRosterFilter EnemyFilter => enemyFilter;
        public int SortMode => sortMode;
        public string ContextHeader => context == DashboardRosterContext.Account ? Text("DashboardContextAccount", "Account") : Text("DashboardContextTier", "Tier");
        public int AllyTotalCount => allAllies.Count;
        public int EnemyTotalCount => allEnemies.Count;
        public int AllyMarkedCount => allAllies.Count(row => row.IsMarked);
        public int EnemyMarkedCount => allEnemies.Count(row => row.IsMarked);
        public int AllyLowSampleCount => allAllies.Count(row => row.IsShipLowSample);
        public int EnemyLowSampleCount => allEnemies.Count(row => row.IsShipLowSample);
        public int AllyAnomalyCount => allAllies.Count(row => row.IsAnomaly);
        public int EnemyAnomalyCount => allEnemies.Count(row => row.IsAnomaly);

        public BattleDashboardViewModel(IDashboardPresentationService presentationService)
        {
            this.presentationService = presentationService;
        }

        public void Update(Battlefield newBattlefield, bool completed, DashboardBattleMetadata newMetadata)
        {
            battlefield = newBattlefield;
            loadCompleted = completed;
            Metadata = newMetadata;
            Rebuild();
        }

        public void SetMetadata(DashboardBattleMetadata value) => Metadata = value;

        public void SetContext(DashboardRosterContext value)
        {
            if (context == value) return;
            context = value;
            NotifyPropertyChanged(nameof(Context));
            NotifyPropertyChanged(nameof(ContextHeader));
            Rebuild();
        }

        public void SetFilter(bool ally, DashboardRosterFilter value)
        {
            if (ally) allyFilter = value;
            else enemyFilter = value;
            NotifyPropertyChanged(ally ? nameof(AllyFilter) : nameof(EnemyFilter));
            ApplyFilters();
        }

        public void SetSortMode(int value)
        {
            if (sortMode == value) return;
            sortMode = value;
            NotifyPropertyChanged(nameof(SortMode));
            Rebuild();
        }

        public void RefreshPresentation() => Rebuild();

        private void Rebuild()
        {
            if (battlefield == null)
            {
                allAllies = allEnemies = Array.Empty<DashboardPlayerRowViewModel>();
                Summary = new();
                ApplyFilters();
                return;
            }

            DashboardPresentationSnapshot snapshot = presentationService.Create(battlefield, context, loadCompleted, sortMode);
            allAllies = snapshot.Allies;
            allEnemies = snapshot.Enemies;
            Summary = snapshot.Summary;
            NotifyCounts();
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            Allies = Filter(allAllies, allyFilter);
            Enemies = Filter(allEnemies, enemyFilter);
        }

        private static IReadOnlyList<DashboardPlayerRowViewModel> Filter(IReadOnlyList<DashboardPlayerRowViewModel> rows, DashboardRosterFilter filter) => filter switch
        {
            DashboardRosterFilter.Marked => rows.Where(row => row.IsMarked).ToArray(),
            DashboardRosterFilter.LowSample => rows.Where(row => row.IsShipLowSample).ToArray(),
            DashboardRosterFilter.Anomaly => rows.Where(row => row.IsAnomaly).ToArray(),
            _ => rows
        };

        private void NotifyCounts()
        {
            NotifyPropertyChanged(nameof(AllyTotalCount));
            NotifyPropertyChanged(nameof(EnemyTotalCount));
            NotifyPropertyChanged(nameof(AllyMarkedCount));
            NotifyPropertyChanged(nameof(EnemyMarkedCount));
            NotifyPropertyChanged(nameof(AllyLowSampleCount));
            NotifyPropertyChanged(nameof(EnemyLowSampleCount));
            NotifyPropertyChanged(nameof(AllyAnomalyCount));
            NotifyPropertyChanged(nameof(EnemyAnomalyCount));
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            NotifyPropertyChanged(propertyName);
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static string Text(string resourceKey, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }
}
