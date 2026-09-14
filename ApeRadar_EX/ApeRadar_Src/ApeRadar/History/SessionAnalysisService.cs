using System;
using System.Collections.Generic;
using System.Linq;

namespace ApeRadar.History
{
    internal sealed class SessionAnalysisService : ISessionAnalysisService
    {
        private readonly IHistoryAnalysisService historyAnalysis;

        public SessionAnalysisService(IHistoryAnalysisService historyAnalysis) => this.historyAnalysis = historyAnalysis;

        public SessionSummary CalculateSession(BattleSession session, IReadOnlyList<BattleRecord> battles, IReadOnlyDictionary<long, BattleAdvancedMetrics> advancedMetrics, bool includeExperimental = false)
        {
            HistorySummary basic = historyAnalysis.CalculateSummary(battles);
            List<BattleAdvancedMetrics> metrics = battles.Where(x => advancedMetrics.ContainsKey(x.Id)).Select(x => advancedMetrics[x.Id]).ToList();
            List<BattleAdvancedMetrics> survival = metrics.Where(x => x.Survived.HasValue && Available(x.SurvivalAvailability, includeExperimental)).ToList();
            List<BattleAdvancedMetrics> potential = metrics.Where(x => x.PotentialDamage.HasValue && Available(x.PotentialDamageAvailability, includeExperimental)).ToList();
            List<BattleAdvancedMetrics> taken = metrics.Where(x => x.DamageTaken.HasValue && Available(x.DamageTakenAvailability, includeExperimental)).ToList();
            List<double> damagePerMinute = CalculatePerBattle(battles, advancedMetrics, DamagePerMinute, includeExperimental);
            List<double> tradeRatios = CalculatePerBattle(battles, advancedMetrics, TradeRatio, includeExperimental);

            HistorySummary combined = new()
            {
                RecordedBattles = basic.RecordedBattles,
                EffectiveBattles = basic.EffectiveBattles,
                Winrate = basic.Winrate,
                AverageDamage = basic.AverageDamage,
                AverageDamageRating = basic.AverageDamageRating,
                AverageFrags = basic.AverageFrags,
                AverageFragsRating = basic.AverageFragsRating,
                AveragePr = basic.AveragePr,
                CompletenessRate = basic.CompletenessRate,
                SurvivalRate = survival.Count > 0 ? survival.Count(x => x.Survived == true) / (double)survival.Count : null,
                AverageSurvivalSeconds = Average(survival.Select(x => x.SurvivalSeconds)),
                AveragePotentialDamage = Average(potential.Select(x => x.PotentialDamage.HasValue ? (double?)x.PotentialDamage.Value : null)),
                AverageDamageTaken = Average(taken.Select(x => x.DamageTaken.HasValue ? (double?)x.DamageTaken.Value : null)),
                AverageDamagePerMinute = damagePerMinute.Count > 0 ? damagePerMinute.Average() : null,
                AverageTradeRatio = tradeRatios.Count > 0 ? tradeRatios.Average() : null,
                SurvivalSampleCount = survival.Count,
                PotentialDamageSampleCount = potential.Count,
                DamageTakenSampleCount = taken.Count
            };
            return new SessionSummary
            {
                Session = session,
                Metrics = combined,
                PendingBattles = battles.Count(x => x.Completeness == BattleCompleteness.Pending),
                Battles = battles
            };
        }

        public IReadOnlyList<HistoryTrendPoint> CalculateAdvancedTrend(IReadOnlyList<BattleRecord> battles, IReadOnlyDictionary<long, BattleAdvancedMetrics> advancedMetrics, string metric, int rollingWindow, bool includeExperimental)
        {
            List<BattleRecord> ordered = battles.OrderBy(x => x.StartedAt).Where(x => x.BattleCount == 1).ToList();
            List<HistoryTrendPoint> result = new();
            for (int i = 0; i < ordered.Count; i++)
            {
                int first = rollingWindow <= 0 ? 0 : Math.Max(0, i - rollingWindow + 1);
                List<double> values = new();
                for (int j = first; j <= i; j++)
                {
                    BattleRecord battle = ordered[j];
                    if (!advancedMetrics.TryGetValue(battle.Id, out BattleAdvancedMetrics? advanced)) continue;
                    double? value = MetricValue(battle, advanced, metric, includeExperimental);
                    if (value.HasValue) values.Add(value.Value);
                }
                if (values.Count == 0) continue;
                BattleRecord point = ordered[i];
                result.Add(new HistoryTrendPoint
                {
                    Index = result.Count,
                    BattleId = point.Id,
                    StartedAt = point.StartedAt,
                    Label = $"{point.StartedAt.ToLocalTime():MM-dd HH:mm} · {point.ShipName}",
                    ShipName = point.ShipName,
                    ShipType = point.ShipType,
                    Value = values.Average()
                });
            }
            return result;
        }

        internal static double? MetricValue(BattleRecord battle, BattleAdvancedMetrics advanced, string metric, bool includeExperimental) => metric switch
        {
            "Survival" when Available(advanced.SurvivalAvailability, includeExperimental) && advanced.Survived.HasValue => advanced.Survived.Value ? 1 : 0,
            "SurvivalTime" when Available(advanced.SurvivalAvailability, includeExperimental) => advanced.SurvivalSeconds,
            "PotentialDamage" when Available(advanced.PotentialDamageAvailability, includeExperimental) => advanced.PotentialDamage,
            "DamageTaken" when Available(advanced.DamageTakenAvailability, includeExperimental) => advanced.DamageTaken,
            "DamagePerMinute" => DamagePerMinute(battle, advanced, includeExperimental),
            "TradeRatio" => TradeRatio(battle, advanced, includeExperimental),
            _ => null
        };

        private static bool Available(MetricAvailability availability, bool includeExperimental) =>
            availability == MetricAvailability.Stable || includeExperimental && availability == MetricAvailability.Experimental;

        private static double? DamagePerMinute(BattleRecord battle, BattleAdvancedMetrics advanced, bool includeExperimental = true)
        {
            if (!battle.Damage.HasValue || !Available(advanced.SurvivalAvailability, includeExperimental)) return null;
            double? activeSeconds = advanced.Survived == false ? advanced.SurvivalSeconds : advanced.BattleDurationSeconds;
            return activeSeconds > 0 ? battle.Damage.Value / (activeSeconds.Value / 60d) : null;
        }

        private static double? TradeRatio(BattleRecord battle, BattleAdvancedMetrics advanced, bool includeExperimental = true)
        {
            if (!battle.Damage.HasValue || !Available(advanced.DamageTakenAvailability, includeExperimental) || !advanced.DamageTaken.HasValue || advanced.DamageTaken.Value <= 0) return null;
            return battle.Damage.Value / (double)advanced.DamageTaken.Value;
        }

        private static List<double> CalculatePerBattle(IReadOnlyList<BattleRecord> battles, IReadOnlyDictionary<long, BattleAdvancedMetrics> metrics, Func<BattleRecord, BattleAdvancedMetrics, bool, double?> selector, bool includeExperimental) =>
            battles.Where(x => x.BattleCount == 1 && metrics.ContainsKey(x.Id))
                .Select(x => selector(x, metrics[x.Id], includeExperimental)).Where(x => x.HasValue).Select(x => x!.Value).ToList();

        private static double? Average(IEnumerable<double?> values)
        {
            double[] known = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
            return known.Length == 0 ? null : known.Average();
        }
    }

    internal sealed class ImprovementInsightService : IImprovementInsightService
    {
        private const int CurrentMinimum = 5;
        private const int BaselineMinimum = 20;
        private readonly IHistoryAnalysisService historyAnalysis;

        public ImprovementInsightService(IHistoryAnalysisService historyAnalysis) => this.historyAnalysis = historyAnalysis;

        public IReadOnlyList<ImprovementInsight> CreateInsights(
            IReadOnlyList<BattleRecord> currentBattles,
            IReadOnlyDictionary<long, BattleAdvancedMetrics> currentAdvanced,
            IReadOnlyList<BattleRecord> baselineBattles,
            IReadOnlyDictionary<long, BattleAdvancedMetrics> baselineAdvanced,
            bool includeExperimental)
        {
            List<ImprovementInsight> result = new();
            foreach (IGrouping<string, BattleRecord> shipGroup in currentBattles.Where(x => !string.IsNullOrWhiteSpace(x.ShipId)).GroupBy(x => x.ShipId))
            {
                List<BattleRecord> current = shipGroup.Where(x => x.BattleCount == 1).ToList();
                List<BattleRecord> baseline = baselineBattles.Where(x => x.ShipId == shipGroup.Key && x.BattleCount == 1 && current.All(c => c.Id != x.Id)).ToList();
                if (current.Count < CurrentMinimum || baseline.Count < BaselineMinimum) continue;

                Add(result, shipGroup.First().ShipName, "Damage", AverageDamage(current), AverageDamage(baseline), current.Count, baseline.Count);
                Add(result, shipGroup.First().ShipName, "PR", historyAnalysis.CalculateSummary(current).AveragePr, historyAnalysis.CalculateSummary(baseline).AveragePr, current.Count, baseline.Count);
                AddAdvanced(result, shipGroup.First().ShipName, "Survival", current, currentAdvanced, baseline, baselineAdvanced, includeExperimental);
                AddAdvanced(result, shipGroup.First().ShipName, "PotentialDamage", current, currentAdvanced, baseline, baselineAdvanced, includeExperimental);
                AddAdvanced(result, shipGroup.First().ShipName, "DamagePerMinute", current, currentAdvanced, baseline, baselineAdvanced, includeExperimental);
                AddAdvanced(result, shipGroup.First().ShipName, "TradeRatio", current, currentAdvanced, baseline, baselineAdvanced, includeExperimental);
            }
            if (result.Count == 0)
                result.Add(new ImprovementInsight { Kind = ImprovementInsightKind.Information, Metric = "CollectingData", Message = "CollectingData" });
            return result;
        }

        private static void AddAdvanced(List<ImprovementInsight> target, string shipName, string metric, IReadOnlyList<BattleRecord> current,
            IReadOnlyDictionary<long, BattleAdvancedMetrics> currentAdvanced, IReadOnlyList<BattleRecord> baseline,
            IReadOnlyDictionary<long, BattleAdvancedMetrics> baselineAdvanced, bool includeExperimental)
        {
            double[] currentValues = Values(current, currentAdvanced, metric, includeExperimental);
            double[] baselineValues = Values(baseline, baselineAdvanced, metric, includeExperimental);
            if (currentValues.Length < CurrentMinimum || baselineValues.Length < BaselineMinimum) return;
            Add(target, shipName, metric, currentValues.Average(), baselineValues.Average(), currentValues.Length, baselineValues.Length);
        }

        private static double[] Values(IReadOnlyList<BattleRecord> battles, IReadOnlyDictionary<long, BattleAdvancedMetrics> metrics, string metric, bool includeExperimental) =>
            battles.Where(x => metrics.ContainsKey(x.Id)).Select(x => SessionAnalysisService.MetricValue(x, metrics[x.Id], metric, includeExperimental))
                .Where(x => x.HasValue).Select(x => x!.Value).ToArray();

        private static double? AverageDamage(IEnumerable<BattleRecord> battles)
        {
            long[] values = battles.Where(x => x.Damage.HasValue).Select(x => x.Damage!.Value).ToArray();
            return values.Length == 0 ? null : values.Average();
        }

        private static void Add(List<ImprovementInsight> target, string shipName, string metric, double? current, double? baseline, int currentCount, int baselineCount)
        {
            if (!current.HasValue || !baseline.HasValue || Math.Abs(baseline.Value) < double.Epsilon) return;
            double change = (current.Value - baseline.Value) / Math.Abs(baseline.Value);
            target.Add(new ImprovementInsight
            {
                Kind = change >= .05 ? ImprovementInsightKind.Positive : change <= -.05 ? ImprovementInsightKind.Attention : ImprovementInsightKind.Information,
                Metric = $"{shipName}|{metric}", CurrentValue = current.Value, BaselineValue = baseline.Value,
                CurrentSampleCount = currentCount, BaselineSampleCount = baselineCount, Message = metric
            });
        }
    }
}
