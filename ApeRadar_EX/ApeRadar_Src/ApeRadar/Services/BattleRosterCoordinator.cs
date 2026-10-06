using ApeRadar.Models;
using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.Services
{
    internal sealed record BattleRosterRequest(
        JObject Arena,
        int PlayerCount,
        Server PrimaryServer,
        Server SecondaryServer,
        bool SecondaryServerEnabled,
        APIType ApiType,
        bool ForceRefresh,
        string? ForceRefreshPlayerId,
        Server? ForceRefreshPlayerServer)
    {
        public PlayerStatisticsOptions? StatisticsOptions { get; init; }
    }

    internal sealed record BattleRosterLoadResult(
        IReadOnlyList<Player> Players,
        APIType Provider,
        int StalePlayerCount,
        int SuccessfulRequestCount,
        IReadOnlyList<ApiFailureKind> Failures)
    {
        public bool IsFailed =>
            (SuccessfulRequestCount == 0 && Failures.Count > 0) ||
            (Players.Count > 0 && Players.All(player => player.Availability.IsUnavailable));
        public bool IsPartial => IsFailed || Failures.Count > 0 || Players.Count == 0 ||
            Players.Any(player => player.Availability.IsPartial);
    }

    internal interface IBattleRosterCoordinator
    {
        IReadOnlyList<Player> CreateMetadataRoster(BattleRosterRequest request);
        Task<BattleRosterLoadResult> LoadAsync(BattleRosterRequest request, CancellationToken cancellationToken);
    }

    internal sealed class BattleRosterCoordinator : IBattleRosterCoordinator
    {
        private readonly IPlayerStatsProviderResolver providerResolver;

        public BattleRosterCoordinator() : this(new PlayerStatsProviderResolver())
        {
        }

        internal BattleRosterCoordinator(IPlayerStatsProviderResolver providerResolver) =>
            this.providerResolver = providerResolver ?? throw new ArgumentNullException(nameof(providerResolver));

        public IReadOnlyList<Player> CreateMetadataRoster(BattleRosterRequest request)
        {
            List<Player> players = new();
            JToken? vehicles = request.Arena["vehicles"];
            if (vehicles == null) return players;

            foreach (JToken vehicle in vehicles)
            {
                if ((vehicle["id"]?.Value<int>() ?? 0) <= 30) continue;
                string relation = vehicle["relation"]?.Value<string>() ?? "-1";
                Server server = request.SecondaryServerEnabled && int.TryParse(relation, out int relationValue) && relationValue > 1
                    ? request.SecondaryServer
                    : request.PrimaryServer;
                players.Add(new Player(
                    vehicle["name"]?.Value<string>() ?? "-",
                    server,
                    relation,
                    vehicle["shipId"]?.Value<string>() ?? "-1"));
            }
            return players;
        }

        public async Task<BattleRosterLoadResult> LoadAsync(BattleRosterRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request with { StatisticsOptions = request.StatisticsOptions ?? PlayerStatisticsOptions.Capture() };
            Stopwatch stopwatch = Stopwatch.StartNew();
            (long _, long requestCountBefore, long retryCountBefore) = NetworkUtils.GetMetricsSnapshot();
            (long cacheLookupsBefore, long cacheHitsBefore, _) = PlayerDataCache.GetMetricsSnapshot();
            IPlayerStatsProvider provider = providerResolver.Resolve(request);
            string? primaryForcedId = request.ForceRefreshPlayerServer == request.PrimaryServer ? request.ForceRefreshPlayerId : null;
            string? secondaryForcedId = request.ForceRefreshPlayerServer == request.SecondaryServer ? request.ForceRefreshPlayerId : null;

            List<Task<ApiResult<List<Player>>>> tasks = new();
            if (request.SecondaryServerEnabled)
            {
                tasks.Add(provider.LoadAsync(request, 1, request.PrimaryServer, primaryForcedId, cancellationToken));
                tasks.Add(provider.LoadAsync(request, 2, request.SecondaryServer, secondaryForcedId, cancellationToken));
            }
            else
            {
                tasks.Add(provider.LoadAsync(request, 0, request.PrimaryServer, primaryForcedId, cancellationToken));
            }

            ApiResult<List<Player>>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            List<Player> loaded = results.Where(result => result.IsSuccess && result.Value != null)
                .SelectMany(result => result.Value!)
                .ToList();
            List<ApiFailureKind> failures = results.Where(result => !result.IsSuccess).Select(result => result.Failure).ToList();
            List<Player> players = CreateMetadataRoster(request)
                .Select(metadata => loaded.FirstOrDefault(player =>
                    player.Identity.MatchesRosterEntry(metadata.Identity)) ?? metadata)
                .ToList();
            (long _, long requestCountAfter, long retryCountAfter) = NetworkUtils.GetMetricsSnapshot();
            (long cacheLookupsAfter, long cacheHitsAfter, _) = PlayerDataCache.GetMetricsSnapshot();
            long cacheLookups = cacheLookupsAfter - cacheLookupsBefore;
            long cacheHits = cacheHitsAfter - cacheHitsBefore;
            LogUtils.WriteInfo($"Roster load completed: provider={provider.Type}, players={players.Count}, succeeded={results.Count(result => result.IsSuccess)}/{results.Length}, failures={string.Join(',', failures)}, elapsedMs={stopwatch.ElapsedMilliseconds}, httpRequests={requestCountAfter - requestCountBefore}, retries={retryCountAfter - retryCountBefore}, cacheHits={cacheHits}/{cacheLookups}");
            return new BattleRosterLoadResult(players, provider.Type, players.Count(player => player.IsDataStale), results.Count(result => result.IsSuccess), failures);
        }

    }
}
