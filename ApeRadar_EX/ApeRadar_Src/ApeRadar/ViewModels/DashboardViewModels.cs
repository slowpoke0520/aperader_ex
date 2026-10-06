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
        public int AllyValidCount { get; init; }
        public int EnemyValidCount { get; init; }
        public int AllyTotalCount { get; init; }
        public int EnemyTotalCount { get; init; }
        public string CoverageText => $"{AllyValidCount}/{AllyTotalCount} · {EnemyValidCount}/{EnemyTotalCount}";
        public DashboardComparisonMetric WithCoverage(int allyValid, int allyTotal, int enemyValid, int enemyTotal) =>
            this with { AllyValidCount = allyValid, AllyTotalCount = allyTotal, EnemyValidCount = enemyValid, EnemyTotalCount = enemyTotal };

        public static DashboardComparisonMetric Percentage(double? ally, double? enemy, string label) =>
            Create(label, ally, enemy, 3, value => value.ToString("P1", CultureInfo.CurrentCulture));

        public static DashboardComparisonMetric Integer(double? ally, double? enemy, string label) =>
            Create(label, ally, enemy, 0, value => Math.Round(value).ToString("0", CultureInfo.CurrentCulture));

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
        private const int VisibleBadgeLimit = 1;

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
        public bool HasKarma => Player.Karma >= 0;
        public string KarmaText => HasKarma
            ? Player.Karma.ToString("0", CultureInfo.CurrentCulture)
            : "";
        public string KarmaToolTip => HasKarma
            ? string.Format(CultureInfo.CurrentCulture, Text("DashboardKarmaTip", "Player karma: {0}"), Player.Karma.ToString("0", CultureInfo.CurrentCulture))
            : "";

        public bool HasValidAccount => Available(Player.AccountWinrate, RosterMetricKind.Winrate);
        public bool HasValidTier => Available(Player.TierWinrate, RosterMetricKind.Winrate);
        public bool HasValidShip => Available(Player.ShipWinrate, RosterMetricKind.Winrate);
        public bool HasContextWinrate => ContextWinrateMetric.IsAvailable;
        public bool HasContextPr => ContextPrMetric.IsAvailable;
        public bool HasShipPr => Available(Player.ShipPR);
        public bool IsMarked => Player.WatchStatus != WatchStatus.NONE || Player.IsCustomMarked;
        public bool IsShipLowSample => Available(Player.ShipBattles) && Player.ShipBattles < 20;
        public bool IsTierLowSample => Available(Player.TierBattles) && Player.TierBattles < 50;
        public bool IsFetchFailed => LoadCompleted && !Player.IsHidden && (Player.ID == "-1" || Player.IsDataFetchFailed);
        public bool IsLoading => !LoadCompleted && !Player.IsHidden && Player.ID == "-1";
        public bool IsAnomaly => Player.IsHidden || IsFetchFailed || Player.IsLowTierBiased ||
            (LoadCompleted && (!HasContextWinrate || !HasContextPr || !HasValidShip || !HasShipPr));

        public string ContextBattlesText => Format(Value(Context == DashboardRosterContext.Account ? Player.Battles : Player.TierBattles), "0");
        public MetricItemViewModel ContextWinrateMetric => Metric(
            Value(Context == DashboardRosterContext.Account ? Player.AccountWinrate : Player.TierWinrate, RosterMetricKind.Winrate),
            "P1", RosterMetricKind.Winrate);
        public MetricItemViewModel ContextPrMetric => Metric(
            Value(Context == DashboardRosterContext.Account ? Player.PR : Player.TierPR),
            "0", RosterMetricKind.PersonalRating);
        public string ContextWarning => Context == DashboardRosterContext.Tier && IsTierLowSample ? "⚠" : "";
        public string ContextToolTip => Context == DashboardRosterContext.Tier && IsTierLowSample
            ? Text("TierStatsSmallSample", "Small same-tier sample")
            : "";

        public string ShipBattlesText => Format(Value(Player.ShipBattles), "0");
        public MetricItemViewModel ShipWinrateMetric => Metric(Value(Player.ShipWinrate, RosterMetricKind.Winrate), "P1", RosterMetricKind.Winrate);
        public string ShipDamageText => Format(Value(Player.ShipAvgDmgPerBattle), "0");
        public MetricItemViewModel AccountPrMetric => Metric(Value(Player.PR), "0", RosterMetricKind.PersonalRating);
        public MetricItemViewModel ShipPrMetric => Metric(Value(Player.ShipPR), "0", RosterMetricKind.PersonalRating);
        public PerformanceCellViewModel Performance => BaseRow.Performance;
        public string PerformanceDisplay => RosterPresentationService.SkillBandText(Performance.Band);
        public MetricItemViewModel PerformanceLabelMetric => Metric(Performance.RawValue ?? -1, "0",
            Performance.Metric == RosterPerformanceMetric.PR ? RosterMetricKind.PersonalRating : RosterMetricKind.Winrate);
        public string ContextPrLabel => Context == DashboardRosterContext.Account ? Text("DashboardContextAccount", "Account") : Text("DashboardContextTier", "Tier");

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
            else if (LoadCompleted && !Player.IsHidden && (!HasContextWinrate || !HasContextPr || !HasValidShip || !HasShipPr))
                badges.Add(new(RosterBadgeKind.PartialData, RosterBadgeSeverity.Info, "…", Text("DashboardBadgePartial", "Partial"), Text("DashboardBadgePartialTip", "Available statistics are shown; missing metrics use a dash")));

            StatusBadges = badges;
            VisibleStatusBadges = badges.Take(VisibleBadgeLimit)
                .Concat(badges.Where(badge => badge.Kind == RosterBadgeKind.Note)).Distinct().ToArray();
            OverflowBadgeCount = Math.Max(0, badges.Count - VisibleStatusBadges.Count);
            AllStatusToolTip = string.Join(Environment.NewLine, badges.Select(badge => badge.ToolTip));
        }

        private static MetricItemViewModel Metric(double value, string format, RosterMetricKind kind) =>
            new("", RosterStatistic.Format(value, format, kind), value, kind, RosterStatistic.IsAvailable(value, kind), 1, RosterMetricEmphasis.Primary);

        private static string Format(double value, string format) =>
            RosterStatistic.Format(value, format);

        private bool Available(double value, RosterMetricKind kind = RosterMetricKind.Neutral) => RosterStatistic.IsAvailable(value, kind, Player.IsHidden);
        private double Value(double value, RosterMetricKind kind = RosterMetricKind.Neutral) => RosterStatistic.Value(value, kind, Player.IsHidden);

        private static string Text(string resourceKey, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }

    internal sealed class DashboardTeamSummary
    {
        public int TeamSize { get; init; }
        public int ContextValidCount { get; init; }
        public int ShipValidCount { get; init; }
        public int ContextPrValidCount { get; init; }
        public int ShipPrValidCount { get; init; }
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
        public string AllySummaryText => FormatSide(Name, AllyCount, Winrate.AllyText, Pr.AllyText);
        public string EnemySummaryText => FormatSide(Name, EnemyCount, Winrate.EnemyText, Pr.EnemyText);

        private static string FormatSide(string name, int count, string winrate, string pr) =>
            count == 0
                ? $"{name}  {Text("DashboardNoShipClass", "None")}"
                : $"{name}  {count} · {winrate} · PR {pr}";

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
        public string AllyCoverageText => FormatCoverage(Ally.ContextValidCount, Ally.TeamSize);
        public string EnemyCoverageText => FormatCoverage(Enemy.ContextValidCount, Enemy.TeamSize);
        public bool CoverageInsufficient => new[] { Ally, Enemy }.Any(team =>
            Insufficient(team.ContextValidCount, team.TeamSize) || Insufficient(team.ShipValidCount, team.TeamSize) ||
            Insufficient(team.ContextPrValidCount, team.TeamSize) || Insufficient(team.ShipPrValidCount, team.TeamSize));
        public string CoverageNotice => CoverageInsufficient
            ? Text("DashboardCoverageInsufficient", "Coverage is incomplete; comparisons are for reference only")
            : Text("DashboardCoverageSufficient", "Data coverage is sufficient");
        public string CoverageObservation => $"{Text("DashboardObservationCoverage", "Coverage")}  {Ally.ContextValidCount}/{Ally.TeamSize} · {Enemy.ContextValidCount}/{Enemy.TeamSize}" +
            (CoverageInsufficient ? $" · {Text("DashboardCoverageLowShort", "Low coverage")}" : "");
        public string LowSampleObservation => $"{Text("DashboardObservationLowSample", "Low sample")}  {Ally.ShipLowSampleCount} · {Enemy.ShipLowSampleCount}";
        public string AnomalyObservation => $"{Text("DashboardObservationAnomaly", "Anomaly")}  {Ally.AnomalyCount} · {Enemy.AnomalyCount}";

        private static bool Insufficient(int valid, int total) => total > 0 && valid * 3 < total * 2;
        private static string FormatCoverage(int valid, int total) =>
            string.Format(CultureInfo.CurrentCulture, Text("DashboardCoverageValue", "Valid {0}/{1}"), valid, total);
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
