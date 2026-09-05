using ApeRadar.History;
using ApeRadar.Utils;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
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
    internal sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IHistoryRepository repository;
        private readonly IHistoryAnalysisService analysis;
        private readonly IBattleTrackingCoordinator coordinator;
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

        public HistoryViewModel(IHistoryRepository repository, IHistoryAnalysisService analysis, IBattleTrackingCoordinator coordinator)
        {
            this.repository = repository;
            this.analysis = analysis;
            this.coordinator = coordinator;
            MetricOptions.Add(new HistoryFilterOption { Value = "Winrate", Display = Resource("HistoryMetricWinrate", "Win rate") });
            MetricOptions.Add(new HistoryFilterOption { Value = "Damage", Display = Resource("HistoryMetricDamage", "Damage") });
            MetricOptions.Add(new HistoryFilterOption { Value = "Frags", Display = Resource("HistoryMetricFrags", "Frags") });
            MetricOptions.Add(new HistoryFilterOption { Value = "PR", Display = "PR" });
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "10", Display = "10" });
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "20", Display = "20" });
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "50", Display = "50" });
            RollingWindowOptions.Add(new HistoryFilterOption { Value = "0", Display = Resource("HistoryRollingAll", "All") });
            SelectedMetric = MetricOptions[0];
            SelectedRollingWindow = RollingWindowOptions[1];
            coordinator.ReplayMonitor.ImportProgressChanged += ReplayMonitor_ImportProgressChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<HistoryFilterOption> Servers { get; } = new();
        public ObservableCollection<HistoryFilterOption> Accounts { get; } = new();
        public ObservableCollection<HistoryFilterOption> Ships { get; } = new();
        public ObservableCollection<HistoryFilterOption> MetricOptions { get; } = new();
        public ObservableCollection<HistoryFilterOption> RollingWindowOptions { get; } = new();
        public ObservableCollection<HistoryRowViewModel> Rows { get; } = new();

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

        public string RecordedBattlesText { get; private set; } = "0";
        public string WinrateText { get; private set; } = "-";
        public string AverageDamageText { get; private set; } = "-";
        public string AverageFragsText { get; private set; } = "-";
        public string AveragePrText { get; private set; } = "-";
        public double? AveragePrValue { get; private set; }
        public string CompletenessText { get; private set; } = "0%";
        public string PrDataVersionText => PRUtils.GetExpectedValuesDateString();

        public async Task InitializeAsync()
        {
            await repository.InitializeAsync();
            await LoadServersAsync();
            await ReloadAsync();
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
                Rows.Clear();
                foreach (BattleRecord battle in battles.OrderByDescending(x => x.StartedAt)) Rows.Add(new HistoryRowViewModel(battle, analysis.CalculateBattlePr(battle)));
                ApplySummary(analysis.CalculateSummary(battles));
                ApplyChart(battles);
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
            await ReloadAsync();
        }

        public async Task ClearAsync()
        {
            await repository.DeleteAllAsync();
            await LoadServersAsync();
            await ReloadAsync();
        }

        public void OpenDataDirectory()
        {
            string directory = System.IO.Path.GetDirectoryName(repository.DatabasePath)!;
            System.IO.Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { directory }, UseShellExecute = true });
        }

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
            AverageFragsText = summary.AverageFrags?.ToString("N2") ?? "-";
            AveragePrText = summary.AveragePr?.ToString("N0") ?? "-";
            AveragePrValue = summary.AveragePr;
            CompletenessText = summary.CompletenessRate.ToString("P1");
            OnPropertyChanged(nameof(RecordedBattlesText)); OnPropertyChanged(nameof(WinrateText));
            OnPropertyChanged(nameof(AverageDamageText)); OnPropertyChanged(nameof(AverageFragsText));
            OnPropertyChanged(nameof(AveragePrText)); OnPropertyChanged(nameof(AveragePrValue)); OnPropertyChanged(nameof(CompletenessText));
            OnPropertyChanged(nameof(PrDataVersionText));
        }

        private void ApplyChart(IReadOnlyList<BattleRecord> battles)
        {
            string metric = SelectedMetric?.Value ?? "Winrate";
            int window = int.TryParse(SelectedRollingWindow?.Value, out int parsed) ? parsed : 20;
            IReadOnlyList<HistoryTrendPoint> points = analysis.CalculateTrend(battles, metric, window);
            ChartSeries = new ISeries[] { new LineSeries<double> { Values = points.Select(x => x.Value).ToArray(), GeometrySize = 6, LineSmoothness = 0.25, Fill = null, Name = SelectedMetric?.Display } };
            ChartXAxes = new[] { new Axis { Labels = points.Select(x => x.Label).ToArray(), LabelsRotation = 25, TextSize = 11 } };
            ChartYAxes = new[] { new Axis { Labeler = metric == "Winrate" ? value => value.ToString("P0") : value => value.ToString("N0") } };
        }

        private void ReplayMonitor_ImportProgressChanged(object? sender, ReplayImportProgress e)
        {
            Application.Current.Dispatcher.BeginInvoke(() => StatusText = e.Cancelled
                ? Resource("HistoryImportCancelled", "Replay import paused; it will resume next time ApeRadar starts.")
                : string.Format(Resource("HistoryImportProgress", "Replay import {0}/{1}; imported {2}, skipped {3}, failed {4}"),
                    e.Processed, e.Total, e.Imported, e.Skipped, e.Failed));
        }

        private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
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
        public HistoryRowViewModel(BattleRecord battle, double? pr)
        {
            StartedAt = battle.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            MapName = battle.MapName; ShipName = battle.ShipName;
            Result = LocalizeResult(battle.Result); Damage = battle.Damage?.ToString("N0") ?? "-";
            Frags = battle.Frags?.ToString("N0") ?? "-"; Pr = pr?.ToString("N0") ?? "-";
            ResultValue = battle.Result; PrValue = pr;
            Source = LocalizeSource(battle.Source); Completeness = LocalizeCompleteness(battle.Completeness);
            Status = battle.StatusMessage ?? "";
        }
        public string StartedAt { get; }
        public string MapName { get; }
        public string ShipName { get; }
        public string Result { get; }
        public BattleResult ResultValue { get; }
        public string Damage { get; }
        public string Frags { get; }
        public string Pr { get; }
        public double? PrValue { get; }
        public string Source { get; }
        public string Completeness { get; }
        public string Status { get; }

        private static string LocalizeResult(BattleResult value) => Resource($"HistoryResult{value}", value.ToString());
        private static string LocalizeSource(BattleMetricSource value) => Resource($"HistorySource{value}", value.ToString());
        private static string LocalizeCompleteness(BattleCompleteness value) => Resource($"HistoryCompleteness{value}", value.ToString());
        private static string Resource(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;
    }
}
