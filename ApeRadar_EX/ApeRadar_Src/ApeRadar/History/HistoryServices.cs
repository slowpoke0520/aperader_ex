using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal static class HistoryServices
    {
        public static IHistoryRepository Repository { get; } = new SqliteHistoryRepository();
        public static IHistoryAnalysisService Analysis { get; } = new HistoryAnalysisService();
        public static IBattleTrackingCoordinator Coordinator { get; private set; } = CreateCoordinator();
        private static readonly SemaphoreSlim initializationLock = new(1, 1);
        private static bool initialized;
        private static string currentGamePath = "";

        private static IBattleTrackingCoordinator CreateCoordinator()
        {
            IReplayParser parser = new NodsoftReplayParserAdapter();
            IReplayMonitor monitor = new ReplayMonitor(Repository, parser);
            return new BattleTrackingCoordinator(Repository, monitor, new TrackedPlayerStatsProvider());
        }

        public static async Task InitializeAsync(string gamePath, CancellationToken cancellationToken = default)
        {
            gamePath ??= "";
            await initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (initialized && string.Equals(currentGamePath, gamePath, StringComparison.OrdinalIgnoreCase)) return;
                if (initialized)
                {
                    await Coordinator.DisposeAsync().ConfigureAwait(false);
                    Coordinator = CreateCoordinator();
                    initialized = false;
                }
                await Coordinator.InitializeAsync(gamePath, cancellationToken).ConfigureAwait(false);
                currentGamePath = gamePath;
                initialized = true;
            }
            finally { initializationLock.Release(); }
        }

        public static async ValueTask DisposeAsync()
        {
            if (initialized) await Coordinator.DisposeAsync().ConfigureAwait(false);
            initializationLock.Dispose();
        }
    }
}
