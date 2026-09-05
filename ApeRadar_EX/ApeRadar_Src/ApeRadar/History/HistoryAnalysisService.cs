using ApeRadar.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ApeRadar.History
{
    internal sealed class HistoryAnalysisService : IHistoryAnalysisService
    {
        public HistorySummary CalculateSummary(IReadOnlyList<BattleRecord> battles)
        {
            int recorded = battles.Sum(x => Math.Max(1, x.BattleCount));
            List<BattleRecord> effective = battles.Where(HasBaseMetrics).ToList();
            double effectiveCount = effective.Sum(x => Math.Max(1, x.BattleCount));
            double wins = effective.Where(x => x.WinCount.HasValue).Sum(x => x.WinCount!.Value);
            double knownResultCount = effective.Where(x => x.WinCount.HasValue).Sum(x => Math.Max(1, x.BattleCount));
            double damageCount = effective.Where(x => x.Damage.HasValue).Sum(x => Math.Max(1, x.BattleCount));
            double fragCount = effective.Where(x => x.Frags.HasValue).Sum(x => Math.Max(1, x.BattleCount));

            return new HistorySummary
            {
                RecordedBattles = recorded,
                EffectiveBattles = Convert.ToInt32(effectiveCount),
                Winrate = knownResultCount > 0 ? wins / knownResultCount : null,
                AverageDamage = damageCount > 0 ? effective.Where(x => x.Damage.HasValue).Sum(x => x.Damage!.Value) / damageCount : null,
                AverageFrags = fragCount > 0 ? effective.Where(x => x.Frags.HasValue).Sum(x => x.Frags!.Value) / fragCount : null,
                AveragePr = CalculateAggregatePr(effective),
                CompletenessRate = recorded > 0 ? effectiveCount / recorded : 0
            };
        }

        public IReadOnlyList<HistoryTrendPoint> CalculateTrend(IReadOnlyList<BattleRecord> battles, string metric, int rollingWindow)
        {
            List<BattleRecord> ordered = battles.OrderBy(x => x.StartedAt).ToList();
            List<HistoryTrendPoint> points = new();
            for (int i = 0; i < ordered.Count; i++)
            {
                List<BattleRecord> window = TakeWindow(ordered, i, rollingWindow);
                double? value = metric switch
                {
                    "Winrate" => WeightedAverage(window, x => x.WinCount, true),
                    "Damage" => WeightedAverage(window, x => x.Damage, false),
                    "Frags" => WeightedAverage(window, x => x.Frags, false),
                    "PR" => CalculateAggregatePr(window),
                    _ => null
                };
                if (value.HasValue)
                {
                    BattleRecord battle = ordered[i];
                    points.Add(new HistoryTrendPoint
                    {
                        BattleId = battle.Id,
                        StartedAt = battle.StartedAt,
                        Label = $"{battle.StartedAt.ToLocalTime():MM-dd HH:mm} · {battle.ShipName}",
                        Value = value.Value
                    });
                }
            }
            return points;
        }

        public double? CalculateBattlePr(BattleRecord battle)
        {
            if (!HasBaseMetrics(battle) || !battle.Damage.HasValue || !battle.Frags.HasValue || !battle.WinCount.HasValue)
                return null;
            double value = PRUtils.CalculateShipPR(battle.ShipId, Math.Max(1, battle.BattleCount), battle.Damage.Value, battle.Frags.Value, battle.WinCount.Value);
            return value < 0 ? null : value;
        }

        private static bool HasBaseMetrics(BattleRecord battle) =>
            battle.Completeness is BattleCompleteness.Complete or BattleCompleteness.Partial &&
            battle.Source != BattleMetricSource.MetadataOnly;

        private static List<BattleRecord> TakeWindow(List<BattleRecord> ordered, int endIndex, int rollingWindow)
        {
            if (rollingWindow <= 0) return ordered.Take(endIndex + 1).ToList();
            List<BattleRecord> result = new();
            int count = 0;
            for (int i = endIndex; i >= 0 && count < rollingWindow; i--)
            {
                result.Add(ordered[i]);
                count += Math.Max(1, ordered[i].BattleCount);
            }
            result.Reverse();
            return result;
        }

        private static double? WeightedAverage<T>(IEnumerable<BattleRecord> battles, Func<BattleRecord, T?> selector, bool valueIsTotal)
            where T : struct, IConvertible
        {
            double total = 0;
            double count = 0;
            foreach (BattleRecord battle in battles)
            {
                T? selected = selector(battle);
                if (!selected.HasValue) continue;
                total += selected.Value.ToDouble(null);
                count += Math.Max(1, battle.BattleCount);
            }
            if (count <= 0) return null;
            return valueIsTotal ? total / count : total / count;
        }

        private static double? CalculateAggregatePr(IEnumerable<BattleRecord> battles)
        {
            List<(string shipId, double battles, double damageDealt, double frags, double wins)> values = battles
                .Where(x => x.Damage.HasValue && x.Frags.HasValue && x.WinCount.HasValue)
                .Select(x => (x.ShipId, (double)Math.Max(1, x.BattleCount), (double)x.Damage!.Value, x.Frags!.Value, x.WinCount!.Value))
                .ToList();
            if (values.Count == 0) return null;
            double pr = PRUtils.CalculateAccountPR(values);
            return pr < 0 ? null : pr;
        }
    }
}
