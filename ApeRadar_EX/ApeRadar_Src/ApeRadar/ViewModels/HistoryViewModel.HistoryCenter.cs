using ApeRadar.History;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.ViewModels
{
    internal sealed partial class HistoryViewModel
    {
        private bool favoritesOnly;
        private HistoryFilterOption? selectedDateRange;
        private string customReviewTag = "";
        private Task draftWrites = Task.CompletedTask;
        private Exception? draftWriteError;
        private readonly HashSet<string> builtInReviewTags = new(StringComparer.Ordinal);
        private string? preferredServer;
        private string? preferredAccount;
        public void SetAccountContext(string server,string account) { preferredServer=server; preferredAccount=account; }
        public ObservableCollection<HistoryFilterOption> DateRanges { get; } = new();
        public ObservableCollection<ApiIntervalRowViewModel> ApiIntervals { get; } = new();
        public ObservableCollection<BattlePlayerRowViewModel> AllyPlayers { get; } = new();
        public ObservableCollection<BattlePlayerRowViewModel> EnemyPlayers { get; } = new();
        public bool FavoritesOnly { get => favoritesOnly; set => Set(ref favoritesOnly, value); }
        public HistoryFilterOption? SelectedDateRange { get => selectedDateRange; set => Set(ref selectedDateRange, value); }
        public string CustomReviewTag { get => customReviewTag; set => Set(ref customReviewTag, value); }
        public string ResultSampleText { get; private set; } = "";
        public string DamageSampleText { get; private set; } = "";
        public string PrSampleText { get; private set; } = "";
        public string DataNotice { get; private set; } = "";
        public string IntervalNotice { get; private set; } = "";
        public string DetailSurvival { get; private set; } = "—";
        public string DetailSurvivalTime { get; private set; } = "—";
        public string DetailPotentialDamage { get; private set; } = "—";
        public string DetailSource { get; private set; } = "";
        public string DetailAvailability { get; private set; } = "";
        public bool CanGoPreviousBattle => SelectedBattle != null && Rows.IndexOf(SelectedBattle) > 0;
        public bool CanGoNextBattle => SelectedBattle != null && Rows.IndexOf(SelectedBattle) >= 0 && Rows.IndexOf(SelectedBattle) < Rows.Count - 1;

        public async Task OpenBattleAsync(long id)
        {
            HistoryRowViewModel? row=Rows.FirstOrDefault(x=>x.Battle.Id==id);
            if (row==null)
            {
                BattleRecord? battle=await repository.GetBattleAsync(id);
                if (battle==null || battle.BattleCount!=1) return;
                row=new HistoryRowViewModel(battle,analysis.CalculateBattlePr(battle),analysis.CalculateBattleDamageRating(battle),analysis.CalculateBattleFragsRating(battle));
            }
            SelectedBattle=row;
            IsBattleDetailOpen=true;
        }

        private void InitializeHistoryCenterOptions()
        {
            foreach (string key in new[] { "week", "month", "all", "custom" })
                DateRanges.Add(new HistoryFilterOption { Value=key, Display=Resource($"HistoryRange{key}", key) });
            SelectedDateRange=DateRanges[0];
            RollingWindowOptions.Insert(0, new HistoryFilterOption { Value="1", Display=Resource("HistoryPerBattle", "Each battle") });
            RollingWindowOptions.Insert(1, new HistoryFilterOption { Value="5", Display="5" });
            SelectedRollingWindow=RollingWindowOptions[0];
            SelectedMetric=MetricOptions.First(x=>x.Value=="PR");
            foreach (ReviewTagOption tag in ReviewTags) builtInReviewTags.Add(tag.Key);
        }

        public void ApplyDateRange(string key, DateTime? today = null)
        {
            SelectedDateRange=DateRanges.FirstOrDefault(x=>x.Value==key) ?? DateRanges[0];
            if (key=="custom") return;
            DateTime day=(today ?? DateTime.Today).Date;
            FromDate=key=="all" ? null : day.AddDays(key=="month"?-29:-6);
            ToDate=key=="all" ? null : day;
        }

        public void NavigateBattle(int step)
        {
            if (SelectedBattle==null) return;
            int index=Rows.IndexOf(SelectedBattle)+step;
            if (index>=0 && index<Rows.Count) SelectedBattle=Rows[index];
            OnPropertyChanged(nameof(CanGoPreviousBattle)); OnPropertyChanged(nameof(CanGoNextBattle));
        }

        private void ApplyMetricSamples(HistorySummary summary)
        {
            string Format(int count)=>string.Format(Resource("HistoryMetricSample", "{0} valid battles"),count);
            ResultSampleText=Format(summary.ResultSampleCount);
            DamageSampleText=Format(summary.DamageSampleCount);
            PrSampleText=Format(summary.PrSampleCount);
            int missing=summary.RecordedBattles-Math.Min(summary.ResultSampleCount,summary.DamageSampleCount);
            DataNotice=missing>0 ? string.Format(Resource("HistoryMissingMetricsNotice", "{0} battles have missing metrics; each metric excludes its missing values."),missing):"";
            foreach (string name in new[] { nameof(ResultSampleText),nameof(DamageSampleText),nameof(PrSampleText),nameof(DataNotice) }) OnPropertyChanged(name);
        }

        private async Task LoadApiIntervalsAsync(HistoryQuery query, CancellationToken token, int version)
        {
            IReadOnlyList<ApiBattleInterval> intervals=await repository.GetApiIntervalsAsync(query,token);
            if (version!=reloadVersion) return;
            ApiIntervals.Clear();
            foreach (ApiBattleInterval interval in intervals) ApiIntervals.Add(new ApiIntervalRowViewModel(interval));
            IntervalNotice=intervals.Count>0 ? string.Format(Resource("HistoryIntervalNotice", "{0} API intervals saved separately; excluded from battle totals and trends."),intervals.Count):"";
            OnPropertyChanged(nameof(IntervalNotice));
        }

        private void ApplyDetailMetrics(HistoryRowViewModel row, BattleAdvancedMetrics? advanced)
        {
            bool stable=advanced?.SurvivalAvailability==MetricAvailability.Stable || ShowExperimentalMetrics && advanced?.SurvivalAvailability==MetricAvailability.Experimental;
            DetailSurvival=stable && advanced?.Survived.HasValue==true ? advanced.Survived.Value ? Resource("HistorySurvived","Survived"):Resource("HistorySunk","Sunk"):"—";
            DetailSurvivalTime=stable ? FormatDuration(advanced?.SurvivalSeconds):"—";
            bool potential=advanced?.PotentialDamageAvailability==MetricAvailability.Stable || ShowExperimentalMetrics && advanced?.PotentialDamageAvailability==MetricAvailability.Experimental;
            DetailPotentialDamage=potential ? advanced?.PotentialDamage?.ToString("N0") ?? "—":"—";
            DetailSource=row.SourceDetail;
            DetailAvailability=string.IsNullOrWhiteSpace(row.Battle.ReplayHash) ? Resource("HistoryNoReplayNotice","No parsed replay is available; replay details cannot be shown.") : "";
            if (row.Battle.Completeness!=BattleCompleteness.Complete)
                DetailAvailability=Resource("HistoryPartialBattleNotice","Some metrics are unavailable. Missing values are excluded from their statistics.") + " " + DetailAvailability;
            foreach (string name in new[] { nameof(DetailSurvival),nameof(DetailSurvivalTime),nameof(DetailPotentialDamage),nameof(DetailSource),nameof(DetailAvailability),nameof(CanGoPreviousBattle),nameof(CanGoNextBattle) }) OnPropertyChanged(name);
        }

        public async Task ToggleFavoriteAsync(HistoryRowViewModel? row = null)
        {
            row ??=SelectedBattle;
            if (row==null) return;
            if (ReferenceEquals(row,SelectedBattle) && !CanEditBattleReview) return;
            bool favorite=ReferenceEquals(row,SelectedBattle) ? !IsFavorite : !row.IsFavorite;
            await FlushReviewDraftsAsync();
            await repository.SetBattleFavoriteAsync(row.Battle.Id,favorite);
            row.SetFavorite(favorite);
            if (loadedBattleReview?.BattleId==row.Battle.Id)
            {
                IsFavorite=favorite;
                loadedBattleReview.IsFavorite=favorite;
            }
            if (reviewDrafts.TryGetValue(row.Battle.Id,out BattleReview? draft)) draft.IsFavorite=favorite;
        }

        public void AddCustomReviewTag()
        {
            string label=CustomReviewTag.Trim();
            if (!CanEditBattleReview || label.Length==0 || label.Length>24) return;
            if (ReviewTags.Count(x=>x.IsSelected)>=8) { StatusText=Resource("HistoryTagLimit","Up to 8 tags per battle."); return; }
            ReviewTagOption? existing=ReviewTags.FirstOrDefault(x=>x.Key==label || x.Display==label);
            if (existing!=null) existing.IsSelected=true;
            else AddReviewTagOption(label,label,true);
            CustomReviewTag="";
            QueueReviewDraft();
        }

        private void AddReviewTagOption(string key,string display,bool selected)
        {
            ReviewTagOption option=new(key,display) { IsSelected=selected };
            option.PropertyChanged+=ReviewTagChanged;
            ReviewTags.Add(option);
        }

        private void ReviewTagChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!CanEditBattleReview) return;
            if (sender is ReviewTagOption { IsSelected:true } tag && ReviewTags.Count(x=>x.IsSelected)>8)
            {
                tag.IsSelected=false; StatusText=Resource("HistoryTagLimit","Up to 8 tags per battle."); return;
            }
            QueueReviewDraft();
        }

        private void QueueReviewDraft()
        {
            if (!CanEditBattleReview || SelectedBattle==null) return;
            BattleReview draft=CurrentReview(SelectedBattle.Battle.Id);
            reviewDrafts[draft.BattleId]=draft;
            QueueCapturedDraft(draft);
        }

        private void QueueCapturedDraft(BattleReview draft)
        {
            // Writes run serially off the dispatcher, including the last edit during window closing.
            draftWrites=draftWrites.ContinueWith(async _=>
            {
                try { await repository.SaveReviewDraftAsync(draft).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    draftWriteError=ex;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ReportError(ex));
                }
            },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default).Unwrap();
        }

        internal async Task FlushReviewDraftsAsync()
        {
            await draftWrites;
            if (draftWriteError!=null) { Exception error=draftWriteError; draftWriteError=null; throw error; }
        }
    }

    internal sealed partial class HistoryRowViewModel
    {
        private bool favorite;
        private bool hasNote;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsFavorite=>favorite;
        public string FavoriteSymbol=>favorite?"★":"☆";
        public string NoteSymbol=>hasNote?"●":"";
        public string DateGroup=>Battle.StartedAt.ToLocalTime().ToString("yyyy-MM-dd");
        public string Time=>Battle.StartedAt.ToLocalTime().ToString("HH:mm");
        public void ApplyReview(BattleReview? review)
        {
            SetFavorite(review?.IsFavorite==true);
            hasNote=!string.IsNullOrWhiteSpace(review?.Note) || review?.Tags.Count>0;
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(NoteSymbol)));
        }
        public void SetFavorite(bool value)
        {
            favorite=value;
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(FavoriteSymbol)));
        }
    }

    internal sealed class ApiIntervalRowViewModel
    {
        public ApiIntervalRowViewModel(ApiBattleInterval interval)
        {
            Period=$"{interval.From.ToLocalTime():MM-dd HH:mm} – {interval.To.ToLocalTime():MM-dd HH:mm}";
            Ship=interval.ShipName; Account=interval.AccountName; Count=interval.BattleCount.ToString();
            Wins=interval.Wins?.ToString("0") ?? "—"; Damage=interval.Damage?.ToString("N0") ?? "—";
            Frags=interval.Frags?.ToString("0") ?? "—";
        }
        public string Period { get; }
        public string Ship { get; }
        public string Account { get; }
        public string Count { get; }
        public string Wins { get; }
        public string Damage { get; }
        public string Frags { get; }
    }
}
