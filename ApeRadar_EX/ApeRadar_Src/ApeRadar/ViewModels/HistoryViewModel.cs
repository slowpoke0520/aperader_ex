using ApeRadar.History;
using ApeRadar.Utils;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace ApeRadar.ViewModels
{
    internal enum HistorySampleTier { None, VerySmall, Short, Established }

    internal sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IHistoryRepository repository;
        private readonly IHistoryAnalysisService analysis;
        private readonly ISessionAnalysisService sessionAnalysis;
        private readonly IImprovementInsightService insightService;
        private readonly IBattleTrackingCoordinator coordinator;
        private readonly string chartFontFamily;
        private HistoryFilterOption? selectedServer;
        private HistoryFilterOption? selectedAccount;
        private HistoryFilterOption? selectedShip;
        private HistoryFilterOption? selectedMetric;
        private HistoryFilterOption? selectedRollingWindow;
        private DateTime? fromDate;
        private DateTime? toDate;
        private string statusText = "";
        private bool isBusy;
        private ISeries[] chartSeries = Array.Empty<ISeries>();
        private Axis[] chartXAxes = Array.Empty<Axis>();
        private Axis[] chartYAxes = Array.Empty<Axis>();
        private SessionRowViewModel? selectedSession;
        private HistoryRowViewModel? selectedSessionBattle;
        private HistoryRowViewModel? selectedBattle;
        private string reviewNote = "";
        private bool isFavorite;
        private int sessionLoadVersion;
        private int battleDetailLoadVersion;

        public HistoryViewModel(IHistoryRepository repository, IHistoryAnalysisService analysis, ISessionAnalysisService sessionAnalysis,
            IImprovementInsightService insightService, IBattleTrackingCoordinator coordinator, string chartFontFamily)
        {
            this.repository = repository;
            this.analysis = analysis;
            this.sessionAnalysis = sessionAnalysis;
            this.insightService = insightService;
            this.coordinator = coordinator;
            this.chartFontFamily = chartFontFamily;
            MetricOptions.Add(new HistoryFilterOption { Value = "Winrate", Display = Resource("HistoryMetricWinrate", "Win rate") });
            MetricOptions.Add(new HistoryFilterOption { Value = "Damage", Display = Resource("HistoryMetricDamage", "Damage") });
            MetricOptions.Add(new HistoryFilterOption { Value = "Frags", Display = Resource("HistoryMetricFrags", "Frags") });
            MetricOptions.Add(new HistoryFilterOption { Value = "PR", Display = "PR" });
            MetricOptions.Add(new HistoryFilterOption { Value = "Survival", Display = Resource("HistoryMetricSurvival", "Survival rate") });
            MetricOptions.Add(new HistoryFilterOption { Value = "PotentialDamage", Display = Resource("HistoryMetricPotentialDamage", "Potential damage") });
            MetricOptions.Add(new HistoryFilterOption { Value = "DamagePerMinute", Display = Resource("HistoryMetricDamagePerMinute", "Damage/min") });
            if (Properties.Settings.Default.ShowExperimentalReplayMetrics)
            {
                MetricOptions.Add(new HistoryFilterOption { Value = "DamageTaken", Display = Resource("HistoryMetricDamageTakenExperimental", "Damage taken (experimental)") });
                MetricOptions.Add(new HistoryFilterOption { Value = "TradeRatio", Display = Resource("HistoryMetricTradeRatioExperimental", "Trade ratio (experimental)") });
            }
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "10", Display = "10" });
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "20", Display = "20" });
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "50", Display = "50" });
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "0", Display = Resource("HistoryRollingAll", "All") });
            SelectedMetric = MetricOptions[0];
            SelectedRollingWindow = RollingWindowOptions[1];
            coordinator.ReplayMonitor.ImportProgressChanged += ReplayMonitor_ImportProgressChanged;
            foreach (string tag in new[] { "GoodPerformance", "EarlyDeath", "LowOpportunity", "Positioning", "Clutch", "ReviewReplay" })
                ReviewTags.Add(new ReviewTagOption(tag, Resource($"HistoryReviewTag{tag}", tag)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<HistoryFilterOption> Servers { get; } = new();
        public ObservableCollection<HistoryFilterOption> Accounts { get; } = new();
        public ObservableCollection<HistoryFilterOption> Ships { get; } = new();
        public ObservableCollection<HistoryFilterOption> MetricOptions { get; } = new();
        public ObservableCollection<HistoryFilterOption> RollingWindowOptions { get; } = new();
        public ObservableCollection<HistoryRowViewModel> Rows { get; } = new();
        public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();
        public ObservableCollection<HistoryRowViewModel> SessionBattles { get; } = new();
        public ObservableCollection<InsightRowViewModel> Insights { get; } = new();
        public ObservableCollection<DamageBreakdownRowViewModel> DamageBreakdowns { get; } = new();
        public ObservableCollection<BattlePlayerRowViewModel> BattlePlayers { get; } = new();
        public ObservableCollection<ReviewTagOption> ReviewTags { get; } = new();

        public HistoryFilterOption? SelectedServer { get => selectedServer; set => Set(ref selectedServer, value); }
        public HistoryFilterOption? SelectedAccount { get => selectedAccount; set => Set(ref selectedAccount, value); }
        public HistoryFilterOption? SelectedShip { get => selectedShip; set => Set(ref selectedShip, value); }
        public HistoryFilterOption? SelectedMetric { get => selectedMetric; set => Set(ref selectedMetric, value); }
        public HistoryFilterOption? SelectedRollingWindow { get => selectedRollingWindow; set => Set(ref selectedRollingWindow, value); }
        public DateTime? FromDate { get => fromDate; set => Set(ref fromDate, value); }
        public DateTime? ToDate { get => toDate; set => Set(ref toDate, value); }
        public string StatusText { get => statusText; private set => Set(ref statusText, value); }
        public bool IsBusy { get => isBusy; private set => Set(ref isBusy, value); }
        public ISeries[] ChartSeries { get => chartSeries; private set => Set(ref chartSeries, value); }
        public Axis[] ChartXAxes { get => chartXAxes; private set => Set(ref chartXAxes, value); }
        public Axis[] ChartYAxes { get => chartYAxes; private set => Set(ref chartYAxes, value); }
        public bool ShowExperimentalMetrics => Properties.Settings.Default.ShowExperimentalReplayMetrics;
        public SessionRowViewModel? SelectedSession { get => selectedSession; set { if (Set(ref selectedSession, value)) _ = LoadSelectedSessionAsync(value, ++sessionLoadVersion); } }
        public HistoryRowViewModel? SelectedSessionBattle { get => selectedSessionBattle; set => Set(ref selectedSessionBattle, value); }
        public HistoryRowViewModel? SelectedBattle { get => selectedBattle; set { if (Set(ref selectedBattle, value)) _ = LoadBattleDetailsAsync(value, ++battleDetailLoadVersion); } }
        public string ReviewNote { get => reviewNote; set => Set(ref reviewNote, value); }
        public bool IsFavorite { get => isFavorite; set => Set(ref isFavorite, value); }

        public string RecordedBattlesText { get; private set; } = "0";
        public string WinrateText { get; private set; } = "-";
        public string AverageDamageText { get; private set; } = "-";
        public double? AverageDamageRatingValue { get; private set; }
        public string AverageFragsText { get; private set; } = "-";
        public double? AverageFragsRatingValue { get; private set; }
        public string AveragePrText { get; private set; } = "-";
        public double? AveragePrValue { get; private set; }
        public string CompletenessText { get; private set; } = "0%";
        public string CurrentSessionTitle { get; private set; } = "-";
        public string CurrentSessionBattlesText { get; private set; } = "0";
        public string CurrentSessionResultLabel { get; private set; } = "-";
        public string CurrentSessionWinrateText { get; private set; } = "-";
        public string CurrentSessionSampleText { get; private set; } = "-";
        public string CurrentSessionSampleHint { get; private set; } = "";
        public string CurrentSessionDamageText { get; private set; } = "-";
        public string CurrentSessionPrText { get; private set; } = "-";
        public string CurrentSessionSurvivalText { get; private set; } = "-";
        public string CurrentSessionPotentialText { get; private set; } = "-";
        public string CurrentSessionPendingText { get; private set; } = "0";
        public string BattleDetailTitle { get; private set; } = "-";
        public string BattleDetailMetrics { get; private set; } = "-";
        public string ChartGuidanceText { get; private set; } = "";
        public string PrDataVersionText => PRUtils.GetExpectedValuesDateString();

        public async Task InitializeAsync()
        {
            await repository.InitializeAsync();
            await LoadServersAsync();
            await ReloadAsync();
            await LoadSessionsAsync();
        }

        public async Task RefreshDependentFiltersAsync(bool serverChanged, bool accountChanged)
        {
            if (serverChanged)
            {
                await LoadOptionsAsync(Accounts, await repository.GetAccountsAsync(SelectedServer?.Value), Resource("HistoryAllAccounts", "All accounts"));
                SelectedAccount = Accounts.FirstOrDefault();
            }
            if (serverChanged || accountChanged)
            {
                await LoadOptionsAsync(Ships, await repository.GetShipsAsync(SelectedServer?.Value, SelectedAccount?.Value), Resource("HistoryAllShips", "All ships"));
                SelectedShip = Ships.FirstOrDefault();
            }
            await ReloadAsync();
            await LoadSessionsAsync();
        }

        public async Task ReloadAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                HistoryQuery query = new()
                {
                    Server = EmptyToNull(SelectedServer?.Value),
                    AccountId = EmptyToNull(SelectedAccount?.Value),
                    ShipId = EmptyToNull(SelectedShip?.Value),
                    From = FromDate.HasValue ? new DateTimeOffset(FromDate.Value.Date) : null,
                    To = ToDate.HasValue ? new DateTimeOffset(ToDate.Value.Date.AddDays(1)) : null
                };
                IReadOnlyList<BattleRecord> battles = await repository.GetBattlesAsync(query);
                IReadOnlyDictionary<long, BattleAdvancedMetrics> advanced = await repository.GetAdvancedMetricsAsync(battles.Select(x => x.Id));
                Rows.Clear();
                foreach (BattleRecord battle in battles.OrderByDescending(x => x.StartedAt))
                {
                    Rows.Add(new HistoryRowViewModel(
                        battle,
                        analysis.CalculateBattlePr(battle),
                        analysis.CalculateBattleDamageRating(battle),
                        analysis.CalculateBattleFragsRating(battle),
                        advanced.TryGetValue(battle.Id, out BattleAdvancedMetrics? metric) ? metric : null,
                        ShowExperimentalMetrics));
                }
                ApplySummary(analysis.CalculateSummary(battles));
                ApplyChart(battles, advanced);
                StatusText = string.Format(Resource("HistoryLoadedStatus", "Loaded {0} records"), battles.Count);
            }
            catch (Exception ex)
            {
                StatusText = string.Format(Resource("HistoryLoadFailed", "Unable to load history: {0}"), ex.Message);
            }
            finally { IsBusy = false; }
        }

        public async Task RetryFailedReplaysAsync()
        {
            StatusText = Resource("HistoryRetryReplayStarted", "Replay rescan started.");
            await coordinator.ReplayMonitor.RetryFailedAsync();
        }

        public void CancelReplayImport()
        {
            coordinator.ReplayMonitor.CancelImport();
            StatusText = Resource("HistoryImportCancelled", "Replay import paused; it will resume next time ApeRadar starts.");
        }

        public async Task RetryPendingAsync()
        {
            await repository.MakePendingChecksDueAsync();
            await coordinator.RetryPendingAsync();
            await RefreshAllAsync();
        }

        public async Task ClearAsync()
        {
            await repository.DeleteAllAsync();
            await LoadServersAsync();
            await RefreshAllAsync();
        }

        public async Task RefreshAllAsync()
        {
            await ReloadAsync();
            await LoadSessionsAsync();
        }

        public void OpenDataDirectory()
        {
            string directory = System.IO.Path.GetDirectoryName(repository.DatabasePath)!;
            System.IO.Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { directory }, UseShellExecute = true });
        }

        public void ReportError(Exception exception) =>
            StatusText = string.Format(Resource("HistoryLoadFailed", "Unable to load history: {0}"), exception.Message);

        public async Task SaveReviewAsync()
        {
            if (SelectedBattle == null) return;
            await repository.SaveBattleReviewAsync(new BattleReview
            {
                BattleId = SelectedBattle.Battle.Id,
                IsFavorite = IsFavorite,
                Note = ReviewNote,
                Tags = ReviewTags.Where(x => x.IsSelected).Select(x => x.Key).ToList()
            });
            StatusText = Resource("HistoryReviewSaved", "Review saved.");
        }

        public async Task MergeSessionsAsync(IEnumerable<SessionRowViewModel> selected)
        {
            long[] ids = selected.Select(x => x.Session.Id).Distinct().ToArray();
            if (ids.Length < 2) return;
            try
            {
                await repository.MergeSessionsAsync(ids);
                await LoadSessionsAsync();
                StatusText = Resource("HistorySessionsMerged", "Sessions merged.");
            }
            catch (Exception ex) { StatusText = string.Format(Resource("HistorySessionEditFailed", "Unable to edit sessions: {0}"), ex.Message); }
        }

        public async Task SplitSelectedSessionAsync()
        {
            if (SelectedSession == null || SelectedSessionBattle == null) return;
            try
            {
                await repository.SplitSessionAsync(SelectedSession.Session.Id, SelectedSessionBattle.Battle.Id);
                await LoadSessionsAsync();
                StatusText = Resource("HistorySessionSplit", "Session split.");
            }
            catch (Exception ex) { StatusText = string.Format(Resource("HistorySessionEditFailed", "Unable to edit sessions: {0}"), ex.Message); }
        }

        private async Task LoadSessionsAsync()
        {
            long? selectedId = SelectedSession?.Session.Id;
            IReadOnlyList<BattleSession> values = await repository.GetSessionsAsync(EmptyToNull(SelectedServer?.Value), EmptyToNull(SelectedAccount?.Value));
            Sessions.Clear();
            foreach (BattleSession session in values) Sessions.Add(new SessionRowViewModel(session));
            SelectedSession = Sessions.FirstOrDefault(x => x.Session.Id == selectedId) ?? Sessions.FirstOrDefault();
            if (SelectedSession == null) ClearSessionDisplay();
        }

        private async Task LoadSelectedSessionAsync(SessionRowViewModel? selected, int version)
        {
            if (selected == null)
            {
                if (version == sessionLoadVersion) ClearSessionDisplay();
                return;
            }
            try
            {
                BattleSession session = selected.Session;
                IReadOnlyList<BattleRecord> battles = await repository.GetSessionBattlesAsync(session.Id);
                IReadOnlyDictionary<long, BattleAdvancedMetrics> advanced = await repository.GetAdvancedMetricsAsync(battles.Select(x => x.Id));
                SessionSummary summary = sessionAnalysis.CalculateSession(session, battles, advanced, ShowExperimentalMetrics);
                IReadOnlyList<BattleRecord> baseline = await repository.GetBattlesAsync(new HistoryQuery
                {
                    Server = session.Server,
                    AccountId = session.AccountId,
                    To = session.StartedAt
                });
                IReadOnlyDictionary<long, BattleAdvancedMetrics> baselineAdvanced = await repository.GetAdvancedMetricsAsync(baseline.Select(x => x.Id));
                IReadOnlyList<ImprovementInsight> insights = insightService.CreateInsights(battles, advanced, baseline, baselineAdvanced, ShowExperimentalMetrics);
                if (version != sessionLoadVersion || !ReferenceEquals(selected, SelectedSession)) return;

                SelectedSessionBattle = null;
                ApplyCurrentSession(summary);
                SessionBattles.Clear();
                foreach (BattleRecord battle in battles)
                    SessionBattles.Add(new HistoryRowViewModel(battle, analysis.CalculateBattlePr(battle), analysis.CalculateBattleDamageRating(battle),
                        analysis.CalculateBattleFragsRating(battle), advanced.TryGetValue(battle.Id, out BattleAdvancedMetrics? metric) ? metric : null, ShowExperimentalMetrics));
                Insights.Clear();
                foreach (ImprovementInsight insight in insights)
                    Insights.Add(new InsightRowViewModel(insight));
            }
            catch (Exception ex)
            {
                StatusText = string.Format(Resource("HistoryLoadFailed", "Unable to load history: {0}"), ex.Message);
            }
        }

        private async Task LoadBattleDetailsAsync(HistoryRowViewModel? row, int version)
        {
            if (row == null)
            {
                if (version == battleDetailLoadVersion) ClearBattleDetails();
                return;
            }
            try
            {
                IReadOnlyDictionary<long, BattleAdvancedMetrics> advanced = await repository.GetAdvancedMetricsAsync(new[] { row.Battle.Id });
                advanced.TryGetValue(row.Battle.Id, out BattleAdvancedMetrics? metric);
                IReadOnlyList<BattleDamageBreakdown> breakdowns = await repository.GetDamageBreakdownsAsync(row.Battle.Id);
                IReadOnlyList<BattlePlayerRecord> players = await repository.GetBattlePlayersAsync(row.Battle.Id);
                BattleReview? review = await repository.GetBattleReviewAsync(row.Battle.Id);
                if (version != battleDetailLoadVersion || !ReferenceEquals(row, SelectedBattle)) return;

                ClearBattleDetails();
                BattleDetailTitle = $"{row.StartedAt} · {row.MapName} · {row.ShipName}";
                BattleDetailMetrics = FormatBattleMetrics(row, metric);
                foreach (BattleDamageBreakdown item in breakdowns)
                {
                    if (item.Availability == MetricAvailability.Experimental && !ShowExperimentalMetrics) continue;
                    DamageBreakdowns.Add(new DamageBreakdownRowViewModel(item));
                }
                foreach (BattlePlayerRecord player in players)
                    BattlePlayers.Add(new BattlePlayerRowViewModel(player));
                if (review != null)
                {
                    IsFavorite = review.IsFavorite;
                    ReviewNote = review.Note;
                    foreach (ReviewTagOption tag in ReviewTags) tag.IsSelected = review.Tags.Contains(tag.Key, StringComparer.Ordinal);
                }
                OnPropertyChanged(nameof(BattleDetailTitle));
                OnPropertyChanged(nameof(BattleDetailMetrics));
            }
            catch (Exception ex)
            {
                StatusText = string.Format(Resource("HistoryLoadFailed", "Unable to load history: {0}"), ex.Message);
            }
        }

        private void ClearBattleDetails()
        {
            DamageBreakdowns.Clear();
            BattlePlayers.Clear();
            foreach (ReviewTagOption tag in ReviewTags) tag.IsSelected = false;
            ReviewNote = "";
            IsFavorite = false;
            BattleDetailTitle = "-";
            BattleDetailMetrics = "-";
            OnPropertyChanged(nameof(BattleDetailTitle));
            OnPropertyChanged(nameof(BattleDetailMetrics));
        }

        internal void ApplyCurrentSession(SessionSummary summary)
        {
            HistorySummary metrics = summary.Metrics;
            int total = metrics.RecordedBattles;
            int resolved = summary.Battles.Where(x => x.WinCount.HasValue).Sum(x => Math.Max(1, x.BattleCount));
            int wins = (int)Math.Round(summary.Battles.Where(x => x.WinCount.HasValue).Sum(x => x.WinCount ?? 0), MidpointRounding.AwayFromZero);
            int nonWins = Math.Max(0, resolved - wins);
            int pending = Math.Max(0, total - resolved);
            CurrentSessionTitle = $"{summary.Session.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {summary.Session.AccountName}";
            CurrentSessionBattlesText = total.ToString(CultureInfo.CurrentCulture);
            CurrentSessionResultLabel = total is > 0 and < 5
                ? Resource("HistorySessionObservedResults", "Observed results")
                : Resource("HistoryWinrate", "Win rate");
            CurrentSessionWinrateText = total is > 0 and < 5
                ? string.Format(Resource("HistorySessionSmallResultFormat", "{0} wins · {1} non-wins · {2} pending"), wins, nonWins, pending)
                : metrics.Winrate?.ToString("P1") ?? "-";
            CurrentSessionDamageText = metrics.AverageDamage?.ToString("N0") ?? "-";
            CurrentSessionPrText = metrics.AveragePr?.ToString("N0") ?? "-";
            CurrentSessionSurvivalText = metrics.SurvivalRate.HasValue ? $"{metrics.SurvivalRate:P1} ({metrics.SurvivalSampleCount}/{total})" : "-";
            CurrentSessionPotentialText = metrics.AveragePotentialDamage.HasValue ? $"{metrics.AveragePotentialDamage:N0} ({metrics.PotentialDamageSampleCount}/{total})" : "-";
            CurrentSessionPendingText = summary.PendingBattles.ToString(CultureInfo.CurrentCulture);
            (CurrentSessionSampleText, CurrentSessionSampleHint) = ClassifySample(total) switch
            {
                HistorySampleTier.None => (Resource("HistorySampleNone", "No session data"), Resource("HistorySampleNoneHint", "Play a Random Battle to start this session.")),
                HistorySampleTier.VerySmall => (string.Format(Resource("HistorySampleVerySmall", "Very small sample ({0} battles)"), total), Resource("HistorySampleVerySmallHint", "Only observed results are shown. Wait for at least 5 battles before judging short-term performance.")),
                HistorySampleTier.Short => (string.Format(Resource("HistorySampleShort", "Short sample ({0} battles)"), total), Resource("HistorySampleShortHint", "You can inspect fluctuations, but averages and win rate can still move sharply.")),
                _ => (string.Format(Resource("HistorySampleEstablished", "Established sample ({0} battles)"), total), Resource("HistorySampleEstablishedHint", "The session summary is more stable; compare it with the same-ship personal baseline."))
            };
            OnPropertyChanged(nameof(CurrentSessionTitle)); OnPropertyChanged(nameof(CurrentSessionBattlesText));
            OnPropertyChanged(nameof(CurrentSessionResultLabel));
            OnPropertyChanged(nameof(CurrentSessionWinrateText)); OnPropertyChanged(nameof(CurrentSessionDamageText));
            OnPropertyChanged(nameof(CurrentSessionPrText)); OnPropertyChanged(nameof(CurrentSessionSurvivalText));
            OnPropertyChanged(nameof(CurrentSessionPotentialText)); OnPropertyChanged(nameof(CurrentSessionPendingText));
            OnPropertyChanged(nameof(CurrentSessionSampleText)); OnPropertyChanged(nameof(CurrentSessionSampleHint));
        }

        private void ClearSessionDisplay()
        {
            SessionBattles.Clear(); Insights.Clear();
            CurrentSessionTitle = "-"; CurrentSessionBattlesText = "0"; CurrentSessionWinrateText = "-";
            CurrentSessionResultLabel = Resource("HistorySessionObservedResults", "Observed results");
            CurrentSessionDamageText = "-"; CurrentSessionPrText = "-"; CurrentSessionSurvivalText = "-";
            CurrentSessionPotentialText = "-"; CurrentSessionPendingText = "0";
            CurrentSessionSampleText = Resource("HistorySampleNone", "No session data");
            CurrentSessionSampleHint = Resource("HistorySampleNoneHint", "Play a Random Battle to start this session.");
            OnPropertyChanged(nameof(CurrentSessionTitle)); OnPropertyChanged(nameof(CurrentSessionBattlesText));
            OnPropertyChanged(nameof(CurrentSessionResultLabel));
            OnPropertyChanged(nameof(CurrentSessionWinrateText)); OnPropertyChanged(nameof(CurrentSessionDamageText));
            OnPropertyChanged(nameof(CurrentSessionPrText)); OnPropertyChanged(nameof(CurrentSessionSurvivalText));
            OnPropertyChanged(nameof(CurrentSessionPotentialText)); OnPropertyChanged(nameof(CurrentSessionPendingText));
            OnPropertyChanged(nameof(CurrentSessionSampleText)); OnPropertyChanged(nameof(CurrentSessionSampleHint));
        }

        private string FormatBattleMetrics(HistoryRowViewModel row, BattleAdvancedMetrics? metric)
        {
            List<string> parts = new()
            {
                $"{Resource("HistoryColumnResult", "Result")}: {row.Result}",
                $"{Resource("HistoryColumnDamage", "Damage")}: {row.Damage}",
                $"{Resource("HistoryColumnFrags", "Frags")}: {row.Frags}",
                $"PR: {row.Pr}",
                $"{Resource("HistoryColumnSource", "Source")}: {row.Source}",
                $"{Resource("HistoryColumnCompleteness", "Completeness")}: {row.Completeness}"
            };
            if (!string.IsNullOrWhiteSpace(row.Status))
                parts.Add($"{Resource("HistoryColumnStatus", "Status")}: {row.Status}");
            if (metric == null)
            {
                parts.Add(Resource("HistoryAdvancedUnavailable", "Advanced Replay metrics are unavailable."));
                return string.Join(Environment.NewLine, parts);
            }
            parts.AddRange(new[]
            {
                $"{Resource("HistoryMetricSurvival", "Survival")}: {(metric.Survived.HasValue ? metric.Survived.Value ? Resource("HistorySurvived", "Survived") : Resource("HistorySunk", "Sunk") : "-")}",
                $"{Resource("HistorySurvivalTime", "Survival time")}: {FormatDuration(metric.SurvivalSeconds)}",
                $"{Resource("HistoryMetricPotentialDamage", "Potential damage")}: {(metric.PotentialDamage?.ToString("N0") ?? "-")}"
            });
            if (ShowExperimentalMetrics)
                parts.Add($"{Resource("HistoryMetricDamageTakenExperimental", "Damage taken (experimental)")}: {(metric.DamageTaken?.ToString("N0") ?? "-")}");
            return string.Join(Environment.NewLine, parts);
        }

        private static string FormatDuration(double? seconds) => seconds.HasValue ? TimeSpan.FromSeconds(seconds.Value).ToString(@"mm\:ss") : "-";

        private async Task LoadServersAsync()
        {
            await LoadOptionsAsync(Servers, await repository.GetServersAsync(), Resource("HistoryAllServers", "All servers"));
            SelectedServer = Servers.FirstOrDefault();
            await LoadOptionsAsync(Accounts, await repository.GetAccountsAsync(null), Resource("HistoryAllAccounts", "All accounts"));
            SelectedAccount = Accounts.FirstOrDefault();
            await LoadOptionsAsync(Ships, await repository.GetShipsAsync(null, null), Resource("HistoryAllShips", "All ships"));
            SelectedShip = Ships.FirstOrDefault();
        }

        private static Task LoadOptionsAsync(ObservableCollection<HistoryFilterOption> target, IReadOnlyList<HistoryFilterOption> values, string allLabel)
        {
            target.Clear();
            target.Add(new HistoryFilterOption { Value = "", Display = allLabel });
            foreach (HistoryFilterOption value in values.Where(x => !string.IsNullOrWhiteSpace(x.Value))) target.Add(value);
            return Task.CompletedTask;
        }

        private void ApplySummary(HistorySummary summary)
        {
            RecordedBattlesText = summary.RecordedBattles.ToString(CultureInfo.CurrentCulture);
            WinrateText = summary.Winrate?.ToString("P2") ?? "-";
            AverageDamageText = summary.AverageDamage?.ToString("N0") ?? "-";
            AverageDamageRatingValue = summary.AverageDamageRating;
            AverageFragsText = summary.AverageFrags?.ToString("N2") ?? "-";
            AverageFragsRatingValue = summary.AverageFragsRating;
            AveragePrText = summary.AveragePr?.ToString("N0") ?? "-";
            AveragePrValue = summary.AveragePr;
            CompletenessText = summary.CompletenessRate.ToString("P1");
            OnPropertyChanged(nameof(RecordedBattlesText)); OnPropertyChanged(nameof(WinrateText));
            OnPropertyChanged(nameof(AverageDamageText)); OnPropertyChanged(nameof(AverageDamageRatingValue));
            OnPropertyChanged(nameof(AverageFragsText)); OnPropertyChanged(nameof(AverageFragsRatingValue));
            OnPropertyChanged(nameof(AveragePrText)); OnPropertyChanged(nameof(AveragePrValue)); OnPropertyChanged(nameof(CompletenessText));
            OnPropertyChanged(nameof(PrDataVersionText));
        }

        private void ApplyChart(IReadOnlyList<BattleRecord> battles, IReadOnlyDictionary<long, BattleAdvancedMetrics> advanced)
        {
            string metric = SelectedMetric?.Value ?? "Winrate";
            int window = int.TryParse(SelectedRollingWindow?.Value, out int parsed) ? parsed : 20;
            IReadOnlyList<HistoryTrendPoint> points = metric is "Winrate" or "Damage" or "Frags" or "PR"
                ? analysis.CalculateTrend(battles, metric, window)
                : sessionAnalysis.CalculateAdvancedTrend(battles, advanced, metric, window, ShowExperimentalMetrics);
            ChartGuidanceText = points.Count switch
            {
                < 3 => Resource("HistoryChartNeedThree", "Fewer than 3 valid points are available, so no trend line is drawn."),
                < 10 => string.Format(Resource("HistoryChartShortSample", "Only {0} valid points are available. This curve is highly volatile and is for reference only."), points.Count),
                _ => ""
            };
            OnPropertyChanged(nameof(ChartGuidanceText));
            ChartSeries = !ShouldRenderTrend(points.Count)
                ? Array.Empty<ISeries>()
                : new ISeries[] { new LineSeries<double> { Values = points.Select(x => x.Value).ToArray(), GeometrySize = 6, LineSmoothness = points.Count < 6 ? 0 : 0.25, Fill = null, Name = SelectedMetric?.Display } };
            ChartXAxes = new[]
            {
                new Axis
                {
                    Labels = points.Select(x => x.Label).ToArray(),
                    LabelsRotation = 25,
                    TextSize = 11,
                    LabelsPaint = CreateChartTextPaint()
                }
            };
            ChartYAxes = new[]
            {
                new Axis
                {
                    Labeler = metric is "Winrate" or "Survival" ? value => value.ToString("P0") :
                        metric == "TradeRatio" ? value => value.ToString("N2") : value => value.ToString("N0"),
                    LabelsPaint = CreateChartTextPaint()
                }
            };
        }

        private SolidColorPaint CreateChartTextPaint() => new()
        {
            Color = new SKColor(70, 70, 70),
            FontFamily = chartFontFamily
        };

        private void ReplayMonitor_ImportProgressChanged(object? sender, ReplayImportProgress e)
        {
            Application.Current.Dispatcher.BeginInvoke(() => StatusText = e.Cancelled
                ? Resource("HistoryImportCancelled", "Replay import paused; it will resume next time ApeRadar starts.")
                : string.Format(Resource("HistoryImportProgress", "Replay import {0}/{1}; imported {2}, skipped {3}, failed {4}"),
                    e.Processed, e.Total, e.Imported, e.Skipped, e.Failed));
        }

        private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
        internal static HistorySampleTier ClassifySample(int battleCount) => battleCount switch
        {
            <= 0 => HistorySampleTier.None,
            < 5 => HistorySampleTier.VerySmall,
            < 20 => HistorySampleTier.Short,
            _ => HistorySampleTier.Established
        };
        internal static bool ShouldRenderTrend(int validPointCount) => validPointCount >= 3;
        private static string Resource(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;
        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; OnPropertyChanged(name); return true;
        }
        private void OnPropertyChanged([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void Dispose() => coordinator.ReplayMonitor.ImportProgressChanged -= ReplayMonitor_ImportProgressChanged;
    }

    internal sealed class HistoryRowViewModel
    {
        public HistoryRowViewModel(BattleRecord battle, double? pr, double? damageRating, double? fragsRating, BattleAdvancedMetrics? advanced = null, bool showExperimental = false)
        {
            Battle = battle;
            StartedAt = battle.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            MapName = HistoryMapNameLocalizer.GetDisplayName(battle.MapName); ShipName = battle.ShipName;
            Result = LocalizeResult(battle.Result); Damage = battle.Damage?.ToString("N0") ?? "-";
            Frags = battle.Frags?.ToString("N0") ?? "-"; Pr = pr?.ToString("N0") ?? "-";
            ResultValue = battle.Result; DamageRatingValue = damageRating; FragsRatingValue = fragsRating; PrValue = pr;
            Source = LocalizeSource(battle.Source); Completeness = LocalizeCompleteness(battle.Completeness);
            Status = battle.StatusMessage ?? "";
            Survival = advanced?.Survived.HasValue == true ? advanced.Survived.Value ? Resource("HistorySurvived", "Survived") : Resource("HistorySunk", "Sunk") : "-";
            PotentialDamage = advanced?.PotentialDamage?.ToString("N0") ?? "-";
            DamageTaken = showExperimental ? advanced?.DamageTaken?.ToString("N0") ?? "-" : "-";
        }
        public BattleRecord Battle { get; }
        public string StartedAt { get; }
        public string MapName { get; }
        public string ShipName { get; }
        public string Result { get; }
        public BattleResult ResultValue { get; }
        public string Damage { get; }
        public double? DamageRatingValue { get; }
        public string Frags { get; }
        public double? FragsRatingValue { get; }
        public string Pr { get; }
        public double? PrValue { get; }
        public string Source { get; }
        public string Completeness { get; }
        public string Status { get; }
        public string Survival { get; }
        public string PotentialDamage { get; }
        public string DamageTaken { get; }

        private static string LocalizeResult(BattleResult value) => Resource($"HistoryResult{value}", value.ToString());
        private static string LocalizeSource(BattleMetricSource value) => Resource($"HistorySource{value}", value.ToString());
        private static string LocalizeCompleteness(BattleCompleteness value) => Resource($"HistoryCompleteness{value}", value.ToString());
        private static string Resource(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;
    }

    internal sealed class SessionRowViewModel
    {
        public SessionRowViewModel(BattleSession session)
        {
            Session = session;
            StartedAt = session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            EndedAt = session.EndedAt.ToLocalTime().ToString("HH:mm");
            Account = session.AccountName;
            Server = session.Server;
            Battles = session.BattleCount.ToString(CultureInfo.CurrentCulture);
            Type = session.IsManual ? Resource("HistorySessionManual", "Manual") : Resource("HistorySessionAutomatic", "Automatic");
        }
        public BattleSession Session { get; }
        public string StartedAt { get; }
        public string EndedAt { get; }
        public string Account { get; }
        public string Server { get; }
        public string Battles { get; }
        public string Type { get; }
        private static string Resource(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;
    }

    internal sealed class InsightRowViewModel
    {
        public InsightRowViewModel(ImprovementInsight insight)
        {
            Kind = insight.Kind;
            if (insight.Metric == "CollectingData")
            {
                Text = Resource("HistoryInsightCollecting", "At least 5 current and 20 earlier battles on the same ship are needed for comparisons.");
                return;
            }
            string[] parts = insight.Metric.Split('|', 2);
            string ship = parts[0];
            string metric = Resource($"HistoryInsightMetric{parts.ElementAtOrDefault(1)}", parts.ElementAtOrDefault(1) ?? "");
            double delta = (insight.CurrentValue - insight.BaselineValue) / Math.Abs(insight.BaselineValue);
            Text = string.Format(Resource("HistoryInsightComparison", "{0} · {1}: {2:N1} vs {3:N1} ({4:+0.0%;-0.0%;0%}), samples {5}/{6}."),
                ship, metric, insight.CurrentValue, insight.BaselineValue, delta, insight.CurrentSampleCount, insight.BaselineSampleCount);
        }
        public ImprovementInsightKind Kind { get; }
        public string Text { get; } = "";
        private static string Resource(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;
    }

    internal sealed class DamageBreakdownRowViewModel
    {
        public DamageBreakdownRowViewModel(BattleDamageBreakdown value)
        {
            Direction = value.Direction == DamageDirection.Dealt ? Resource("HistoryDamageDealt", "Dealt") : Resource("HistoryDamageReceived", "Received");
            Type = value.Category == DamageCategory.Unknown ? $"{Resource("HistoryRawDamageType", "Type")} #{value.RawTypeCode}" : value.Category.ToString();
            Damage = value.Damage.ToString("N0");
            Quality = value.Availability == MetricAvailability.Experimental ? Resource("HistoryExperimental", "Experimental") : Resource("HistoryStable", "Stable");
        }
        public string Direction { get; }
        public string Type { get; }
        public string Damage { get; }
        public string Quality { get; }
        private static string Resource(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;
    }

    internal sealed class BattlePlayerRowViewModel
    {
        public BattlePlayerRowViewModel(BattlePlayerRecord value)
        {
            Relation = value.Relation == "0" ? Resource("HistorySelf", "Self") : value.Relation == "1" ? Resource("EncounterAlly", "Ally") : Resource("EncounterEnemy", "Enemy");
            Player = value.AccountName; Ship = value.ShipName;
            Winrate = value.AccountWinrate?.ToString("P2") ?? "-";
            Pr = value.AccountPr?.ToString("N0") ?? "-";
        }
        public string Relation { get; }
        public string Player { get; }
        public string Ship { get; }
        public string Winrate { get; }
        public string Pr { get; }
        private static string Resource(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;
    }

    internal sealed class ReviewTagOption : INotifyPropertyChanged
    {
        private bool isSelected;
        public ReviewTagOption(string key, string display) { Key = key; Display = display; }
        public string Key { get; }
        public string Display { get; }
        public bool IsSelected
        {
            get => isSelected;
            set { if (isSelected == value) return; isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
