using ApeRadar.Models;
using ApeRadar.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed class ReplayMonitor : IReplayMonitor
    {
        private readonly IHistoryRepository repository;
        private readonly IReplayParser parser;
        private readonly ConcurrentDictionary<string, CandidateState> candidates = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource lifetime = new();
        private FileSystemWatcher? watcher;
        private Task? processingTask;
        private string replayDirectory = "";
        private int imported;
        private int skipped;
        private int failed;
        private int scanTicks;
        private volatile bool importPaused;

        public ReplayMonitor(IHistoryRepository repository, IReplayParser parser)
        {
            this.repository = repository;
            this.parser = parser;
        }

        public event EventHandler<ReplayImportProgress>? ImportProgressChanged;

        public Task StartAsync(string gamePath, CancellationToken cancellationToken = default)
        {
            replayDirectory = Path.Combine(gamePath ?? "", "replays");
            if (!Directory.Exists(replayDirectory)) return Task.CompletedTask;

            watcher = new FileSystemWatcher(replayDirectory, "*.wowsreplay")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Created += OnReplayChanged;
            watcher.Changed += OnReplayChanged;
            watcher.Renamed += OnReplayRenamed;
            watcher.Error += OnWatcherError;
            processingTask = ProcessLoopAsync(lifetime.Token);
            return RescanAsync(cancellationToken);
        }

        public Task RescanAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(replayDirectory)) return Task.CompletedTask;
            foreach (string path in Directory.EnumerateFiles(replayDirectory, "*.wowsreplay", SearchOption.AllDirectories).OrderBy(File.GetLastWriteTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Queue(path, false);
            }
            PublishProgress();
            return Task.CompletedTask;
        }

        public Task RetryFailedAsync(CancellationToken cancellationToken = default)
        {
            importPaused = false;
            foreach (KeyValuePair<string, CandidateState> item in candidates)
                candidates[item.Key] = item.Value with { Attempted = false, StableChecks = 0 };
            return RescanAsync(cancellationToken);
        }

        public void CancelImport()
        {
            importPaused = true;
            PublishProgress(true);
        }

        private void OnReplayChanged(object sender, FileSystemEventArgs e) => Queue(e.FullPath, true);
        private void OnReplayRenamed(object sender, RenamedEventArgs e) => Queue(e.FullPath, true);
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            LogUtils.WriteError("Replay watcher missed an event; rescanning.", e.GetException());
            _ = RescanAsync(lifetime.Token);
        }

        private void Queue(string path, bool changed)
        {
            candidates.AddOrUpdate(path,
                _ => new CandidateState(0, DateTime.MinValue, 0, false),
                (_, state) => changed ? state with { StableChecks = 0, Attempted = false } : state);
        }

        private async Task ProcessLoopAsync(CancellationToken cancellationToken)
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(3));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    if (importPaused) continue;
                    if (++scanTicks >= 40)
                    {
                        scanTicks = 0;
                        await RescanAsync(cancellationToken);
                    }
                    foreach (string path in candidates.Keys.ToArray().OrderBy(GetLastWriteTimeUtcSafe))
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        await TryProcessAsync(path, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task TryProcessAsync(string path, CancellationToken cancellationToken)
        {
            if (!candidates.TryGetValue(path, out CandidateState? state) || state == null || state.Attempted || !File.Exists(path)) return;
            FileInfo info = new(path);
            bool unchanged = state.Length == info.Length && state.LastWriteUtc == info.LastWriteTimeUtc;
            int stableChecks = unchanged ? state.StableChecks + 1 : 0;
            state = state with { Length = info.Length, LastWriteUtc = info.LastWriteTimeUtc, StableChecks = stableChecks };
            candidates[path] = state;
            if (stableChecks < 2 || !CanOpenExclusively(path)) return;

            candidates[path] = state with { Attempted = true };
            try
            {
                ReplayParseResult replay = await parser.ParseAsync(path, cancellationToken);
                if (replay.ErrorCode == "NotRandomBattle")
                {
                    Interlocked.Increment(ref skipped);
                    PublishProgress();
                    return;
                }
                if (await repository.HasReplayAsync(replay.FileHash, cancellationToken))
                {
                    Interlocked.Increment(ref skipped);
                    PublishProgress();
                    return;
                }

                BattleRecord? battle = await repository.FindDraftForReplayAsync(replay, cancellationToken);
                if (battle == null && replay.StartedAt.HasValue && !string.IsNullOrWhiteSpace(replay.AccountName) && !string.IsNullOrWhiteSpace(replay.ShipId))
                {
                    string server = ResolveServer();
                    string accountId = ResolveAccountId(server, replay.AccountName);
                    battle = new BattleRecord
                    {
                        BattleKey = replay.BattleKey,
                        StartedAt = replay.StartedAt.Value,
                        Server = server,
                        Mode = replay.Mode,
                        MapName = replay.MapName,
                        AccountId = accountId,
                        AccountName = replay.AccountName,
                        ShipId = replay.ShipId,
                        ShipName = ResolveShipName(replay.ShipId),
                        Completeness = BattleCompleteness.Pending
                    };
                    battle.Id = await repository.UpsertDraftAsync(battle, Array.Empty<BattlePlayerRecord>(), null, cancellationToken);
                }

                if (battle != null)
                {
                    await repository.CompleteFromReplayAsync(battle.Id, replay, path, cancellationToken);
                    if (!replay.HasCompleteMetrics && await repository.GetPreBattleSnapshotAsync(battle.Id, cancellationToken) != null)
                    {
                        await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck
                        {
                            BattleId = battle.Id,
                            Attempt = 0,
                            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(1),
                            LastError = replay.ErrorCode
                        }, cancellationToken);
                    }
                    Interlocked.Increment(ref imported);
                }
                else
                {
                    await repository.RecordReplayFailureAsync(replay, path, cancellationToken);
                    Interlocked.Increment(ref failed);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                LogUtils.WriteError($"Replay import failed: {path}", ex);
            }
            finally { PublishProgress(); }
        }

        private static bool CanOpenExclusively(string path)
        {
            try
            {
                using FileStream _ = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static DateTime GetLastWriteTimeUtcSafe(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch { return DateTime.MaxValue; }
        }

        private static string ResolveServer()
        {
            string configured = Properties.Settings.Default.Server;
            if (!configured.Equals("AUTO", StringComparison.OrdinalIgnoreCase)) return configured;
            try
            {
                string log = Path.Combine(Properties.Settings.Default.GamePath, "profile", "clientrunner.log");
                return ServerExt.GetNameByServer(ServerExt.AutoDetectServer(log));
            }
            catch { return "AUTO"; }
        }

        private static string ResolveAccountId(string server, string accountName)
        {
            try
            {
                if (server != "AUTO" && PlayerIDCache.TryGetID(ServerExt.GetServerByName(server), accountName, out string id)) return id;
            }
            catch { }
            return $"name:{accountName.ToLowerInvariant()}";
        }

        private static string ResolveShipName(string shipId)
        {
            try { return ShipInfoUtils.GetShipNameByID(shipId, LanguageExt.GetLanguageByName(Properties.Settings.Default.ShipNameLanguage)); }
            catch { return shipId; }
        }

        private void PublishProgress(bool cancelled = false) => ImportProgressChanged?.Invoke(this, new ReplayImportProgress
        {
            Total = candidates.Count,
            Processed = imported + skipped + failed,
            Imported = imported,
            Skipped = skipped,
            Failed = failed,
            Cancelled = cancelled
        });

        public async ValueTask DisposeAsync()
        {
            watcher?.Dispose();
            lifetime.Cancel();
            if (processingTask != null)
            {
                try { await processingTask; } catch (OperationCanceledException) { }
            }
            lifetime.Dispose();
            if (parser is IDisposable disposable) disposable.Dispose();
        }

        private sealed record CandidateState(long Length, DateTime LastWriteUtc, int StableChecks, bool Attempted);
    }
}
