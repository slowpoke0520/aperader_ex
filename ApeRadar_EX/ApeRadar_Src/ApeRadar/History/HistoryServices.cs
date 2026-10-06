using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed class HistoryServices : IAsyncDisposable
    {
        private readonly Func<IBattleTrackingCoordinator> coordinatorFactory;
        private readonly SemaphoreSlim initializationLock = new(1, 1);
        private bool initialized;
        private bool disposed;
        private string currentGamePath = "";

        public HistoryServices()
        {
            Repository = new SqliteHistoryRepository();
            Analysis = new HistoryAnalysisService();
            SessionAnalysis = new SessionAnalysisService(Analysis);
            Insights = new ImprovementInsightService(Analysis);
            coordinatorFactory = () => CreateCoordinator(Repository);
            Coordinator = coordinatorFactory();
        }

        internal HistoryServices(
            IHistoryRepository repository,
            IHistoryAnalysisService analysis,
            ISessionAnalysisService sessionAnalysis,
            IImprovementInsightService insights,
            Func<IBattleTrackingCoordinator> coordinatorFactory)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            SessionAnalysis = sessionAnalysis ?? throw new ArgumentNullException(nameof(sessionAnalysis));
            Insights = insights ?? throw new ArgumentNullException(nameof(insights));
            this.coordinatorFactory = coordinatorFactory ?? throw new ArgumentNullException(nameof(coordinatorFactory));
            Coordinator = this.coordinatorFactory();
        }

        public IHistoryRepository Repository { get; }
        public IHistoryAnalysisService Analysis { get; }
        public ISessionAnalysisService SessionAnalysis { get; }
        public IImprovementInsightService Insights { get; }
        public IBattleTrackingCoordinator Coordinator { get; private set; }

        private static IBattleTrackingCoordinator CreateCoordinator(IHistoryRepository repository)
        {
            IReplayParser parser = new NodsoftReplayParserAdapter();
            IReplayMonitor monitor = new ReplayMonitor(repository, parser);
            return new BattleTrackingCoordinator(repository, monitor, new TrackedPlayerStatsProvider());
        }

        public async Task InitializeAsync(string gamePath, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            gamePath ??= "";
            await initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (initialized && string.Equals(currentGamePath, gamePath, StringComparison.OrdinalIgnoreCase)) return;
                if (initialized)
                {
                    await Coordinator.DisposeAsync().ConfigureAwait(false);
                    Coordinator = coordinatorFactory();
                    initialized = false;
                }
                await Coordinator.InitializeAsync(gamePath, cancellationToken).ConfigureAwait(false);
                currentGamePath = gamePath;
                initialized = true;
            }
            finally { initializationLock.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await initializationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (disposed) return;
                disposed = true;
                await Coordinator.DisposeAsync().ConfigureAwait(false);
                initialized = false;
            }
            finally { initializationLock.Release(); }
        }
    }
}
