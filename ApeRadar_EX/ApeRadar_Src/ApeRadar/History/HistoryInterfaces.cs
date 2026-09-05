using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal interface IHistoryRepository
    {
        string DatabasePath { get; }
        Task InitializeAsync(CancellationToken cancellationToken = default);
        Task<long> UpsertDraftAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default);
        Task<BattleRecord?> FindDraftForReplayAsync(ReplayParseResult replay, CancellationToken cancellationToken = default);
        Task CompleteFromReplayAsync(long battleId, ReplayParseResult replay, string replayPath, CancellationToken cancellationToken = default);
        Task RecordReplayFailureAsync(ReplayParseResult replay, string replayPath, CancellationToken cancellationToken = default);
        Task<bool> HasReplayAsync(string replayHash, CancellationToken cancellationToken = default);
        Task<BattleRecord?> GetBattleAsync(long battleId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BattleRecord>> GetBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<HistoryFilterOption>> GetServersAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<HistoryFilterOption>> GetAccountsAsync(string? server, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<HistoryFilterOption>> GetShipsAsync(string? server, string? accountId, CancellationToken cancellationToken = default);
        Task<ShipStatSnapshot?> GetPreBattleSnapshotAsync(long battleId, CancellationToken cancellationToken = default);
        Task AddOrUpdatePendingCheckAsync(PendingResultCheck check, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PendingResultCheck>> GetDuePendingChecksAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
        Task ResolveFromApiAsync(long battleId, ShipStatSnapshot before, ShipStatSnapshot after, CancellationToken cancellationToken = default);
        Task MarkPendingAttemptAsync(PendingResultCheck check, bool exhausted, CancellationToken cancellationToken = default);
        Task MakePendingChecksDueAsync(CancellationToken cancellationToken = default);
        Task DeleteAllAsync(CancellationToken cancellationToken = default);
    }

    internal interface IReplayParser
    {
        string ParserVersion { get; }
        Task<ReplayParseResult> ParseAsync(string path, CancellationToken cancellationToken = default);
    }

    internal interface IReplayMonitor : IAsyncDisposable
    {
        event EventHandler<ReplayImportProgress>? ImportProgressChanged;
        Task StartAsync(string gamePath, CancellationToken cancellationToken = default);
        Task RescanAsync(CancellationToken cancellationToken = default);
        Task RetryFailedAsync(CancellationToken cancellationToken = default);
        void CancelImport();
    }

    internal interface ITrackedPlayerStatsProvider
    {
        Task<ShipStatSnapshot?> GetCurrentShipStatsAsync(BattleRecord battle, CancellationToken cancellationToken = default);
    }

    internal interface IBattleTrackingCoordinator : IAsyncDisposable
    {
        IReplayMonitor ReplayMonitor { get; }
        Task InitializeAsync(string gamePath, CancellationToken cancellationToken = default);
        Task CapturePreBattleAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default);
        Task RetryPendingAsync(CancellationToken cancellationToken = default);
    }

    internal interface IHistoryAnalysisService
    {
        HistorySummary CalculateSummary(IReadOnlyList<BattleRecord> battles);
        IReadOnlyList<HistoryTrendPoint> CalculateTrend(IReadOnlyList<BattleRecord> battles, string metric, int rollingWindow);
        double? CalculateBattlePr(BattleRecord battle);
    }
}
