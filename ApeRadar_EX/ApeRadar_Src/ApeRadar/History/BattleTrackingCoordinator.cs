using ApeRadar.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed class BattleTrackingCoordinator : IBattleTrackingCoordinator
    {
        // The first check is queued one minute after a replay becomes available.
        // These following gaps produce checks at roughly 1, 3, 7 and 15 minutes.
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8)
        };

        private readonly IHistoryRepository repository;
        private readonly ITrackedPlayerStatsProvider statsProvider;
        private readonly CancellationTokenSource lifetime = new();
        private Task? retryLoop;

        public BattleTrackingCoordinator(IHistoryRepository repository, IReplayMonitor replayMonitor, ITrackedPlayerStatsProvider statsProvider)
        {
            this.repository = repository;
            ReplayMonitor = replayMonitor;
            this.statsProvider = statsProvider;
        }

        public IReplayMonitor ReplayMonitor { get; }

        public async Task InitializeAsync(string gamePath, CancellationToken cancellationToken = default)
        {
            await repository.InitializeAsync(cancellationToken);
            await ReplayMonitor.StartAsync(gamePath, cancellationToken);
            retryLoop = RetryLoopAsync(lifetime.Token);
        }

        public async Task CapturePreBattleAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default)
        {
            try
            {
                snapshot ??= await statsProvider.GetCurrentShipStatsAsync(battle, cancellationToken);
            }
            catch (Exception ex) { LogUtils.WriteError("Unable to capture fresh pre-battle ship statistics.", ex); }

            long id = await repository.UpsertDraftAsync(battle, players, snapshot, cancellationToken);
            if (snapshot != null)
            {
                await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck
                {
                    BattleId = id,
                    Attempt = 0,
                    // A random battle can last up to 20 minutes. When no replay event is
                    // available, use that upper bound and begin the same retry sequence.
                    NextAttemptAt = battle.StartedAt.AddMinutes(21),
                    LastError = "WaitingForBattleCompletion"
                }, cancellationToken);
            }
        }

        public async Task RetryPendingAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PendingResultCheck> checks = await repository.GetDuePendingChecksAsync(DateTimeOffset.UtcNow, cancellationToken);
            foreach (PendingResultCheck check in checks)
            {
                BattleRecord? battle = await repository.GetBattleAsync(check.BattleId, cancellationToken);
                ShipStatSnapshot? before = await repository.GetPreBattleSnapshotAsync(check.BattleId, cancellationToken);
                if (battle == null || before == null)
                {
                    await repository.MarkPendingAttemptAsync(check, true, cancellationToken);
                    continue;
                }
                try
                {
                    ShipStatSnapshot? after = await statsProvider.GetCurrentShipStatsAsync(battle, cancellationToken);
                    if (after != null && after.Battles > before.Battles)
                    {
                        await repository.ResolveFromApiAsync(battle.Id, before, after, cancellationToken);
                        continue;
                    }
                    check.LastError = after == null ? "StatisticsUnavailable" : "StatisticsNotUpdated";
                }
                catch (Exception ex)
                {
                    check.LastError = "NetworkError";
                    LogUtils.WriteError($"Post-battle statistics check failed for battle {check.BattleId}.", ex);
                    // Offline is not a terminal data state. Keep the task persistent and
                    // retry at a low frequency without consuming one of the four API checks.
                    check.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(15);
                    await repository.AddOrUpdatePendingCheckAsync(check, cancellationToken);
                    continue;
                }

                check.Attempt++;
                bool exhausted = check.Attempt >= RetryDelays.Length;
                if (!exhausted) check.NextAttemptAt = DateTimeOffset.UtcNow.Add(RetryDelays[check.Attempt]);
                await repository.MarkPendingAttemptAsync(check, exhausted, cancellationToken);
            }
        }

        private async Task RetryLoopAsync(CancellationToken cancellationToken)
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken)) await RetryPendingAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            if (retryLoop != null)
            {
                try { await retryLoop; } catch (OperationCanceledException) { }
            }
            await ReplayMonitor.DisposeAsync();
            lifetime.Dispose();
        }
    }
}
