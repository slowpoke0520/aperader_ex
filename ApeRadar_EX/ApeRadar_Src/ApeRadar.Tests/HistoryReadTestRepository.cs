using ApeRadar.History;

namespace ApeRadar.Tests;

// Tests fail on accidental writes or unrelated queries.
internal abstract class HistoryReadTestRepository : IHistoryRepository
{
    public string DatabasePath => "test-history.db";
    public virtual Task InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<long> UpsertDraftAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<BattleRecord?> FindDraftForReplayAsync(ReplayParseResult replay, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task CompleteFromReplayAsync(long battleId, ReplayParseResult replay, string replayPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task RecordReplayFailureAsync(ReplayParseResult replay, string replayPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<bool> HasReplayAsync(string replayHash, string parserVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<BattleRecord?> GetBattleAsync(long battleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<int> CountBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<BattleRecord>> GetBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<HistoryFilterOption>> GetServersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<HistoryFilterOption>> GetAccountsAsync(string? server, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<HistoryFilterOption>> GetShipsAsync(string? server, string? accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<ShipStatSnapshot?> GetPreBattleSnapshotAsync(long battleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task AddOrUpdatePendingCheckAsync(PendingResultCheck check, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<PendingResultCheck>> GetDuePendingChecksAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task ResolveFromApiAsync(long battleId, ShipStatSnapshot before, ShipStatSnapshot after, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task MarkPendingAttemptAsync(PendingResultCheck check, bool exhausted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task MakePendingChecksDueAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<BattleSession>> GetSessionsAsync(string? server = null, string? accountId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<BattleSession?> GetLatestSessionAsync(string? server = null, string? accountId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<BattleRecord>> GetSessionBattlesAsync(long sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyDictionary<long, BattleAdvancedMetrics>> GetAdvancedMetricsAsync(IEnumerable<long> battleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<BattleDamageBreakdown>> GetDamageBreakdownsAsync(long battleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<BattlePlayerRecord>> GetBattlePlayersAsync(long battleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<BattleReview?> GetBattleReviewAsync(long battleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task SaveBattleReviewAsync(BattleReview review, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task MergeSessionsAsync(IReadOnlyCollection<long> sessionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<long> SplitSessionAsync(long sessionId, long firstBattleIdOfNewSession, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task DeleteAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
