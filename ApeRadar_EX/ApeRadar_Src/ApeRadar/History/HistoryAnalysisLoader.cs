using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed record HistoryAnalysisSnapshot(
        HistorySummary Summary,
        IReadOnlyList<HistoryTrendPoint> Points,
        IReadOnlyDictionary<long, BattleAdvancedMetrics> Advanced);

    // Reads and CPU work live outside the window and never access WPF resources.
    // The caller captures the query/metric before starting and applies one version.
    internal sealed class HistoryAnalysisLoader
    {
        private readonly IHistoryRepository repository;
        private readonly IHistoryAnalysisService analysis;
        private readonly ISessionAnalysisService sessionAnalysis;

        public HistoryAnalysisLoader(IHistoryRepository repository, IHistoryAnalysisService analysis, ISessionAnalysisService sessionAnalysis)
        {
            this.repository = repository;
            this.analysis = analysis;
            this.sessionAnalysis = sessionAnalysis;
        }

        public Task<HistoryAnalysisSnapshot> LoadAsync(HistoryQuery query, string metric, int rollingWindow,
            bool includeExperimental, CancellationToken cancellationToken) => Task.Run(async () =>
        {
            IReadOnlyList<BattleRecord> battles = await repository.GetBattlesAsync(query, cancellationToken).ConfigureAwait(false);
            IReadOnlyDictionary<long, BattleAdvancedMetrics> advanced = RequiresAdvancedMetrics(metric)
                ? await repository.GetAdvancedMetricsAsync(battles.Select(x => x.Id), cancellationToken).ConfigureAwait(false)
                : new Dictionary<long, BattleAdvancedMetrics>();
            cancellationToken.ThrowIfCancellationRequested();
            HistorySummary summary = analysis.CalculateSummary(battles);
            IReadOnlyList<HistoryTrendPoint> points = RequiresAdvancedMetrics(metric)
                ? sessionAnalysis.CalculateAdvancedTrend(battles, advanced, metric, rollingWindow, includeExperimental, cancellationToken)
                : analysis.CalculateTrend(battles, metric, rollingWindow, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new HistoryAnalysisSnapshot(summary, points, advanced);
        }, cancellationToken);

        internal static bool RequiresAdvancedMetrics(string metric) => metric is
            "Survival" or "PotentialDamage" or "DamagePerMinute" or "DamageTaken" or "TradeRatio";
    }
}
