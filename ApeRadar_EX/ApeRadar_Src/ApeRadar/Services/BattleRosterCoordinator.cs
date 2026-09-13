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
        Server? ForceRefreshPlayerServer);

    internal sealed record BattleRosterLoadResult(
        IReadOnlyList<Player> Players,
        APIType Provider,
        int StalePlayerCount,
        int SuccessfulRequestCount,
        IReadOnlyList<ApiFailureKind> Failures)
    {
        public bool IsFailed => SuccessfulRequestCount == 0 && Failures.Count > 0;
        public bool IsPartial => IsFailed || Failures.Count > 0 || Players.Count == 0 || Players.Any(player => player.ID == "-1" || player.IsDataStale);
    }

    internal interface IPlayerStatsProvider
    {
        APIType Type { get; }
        Task<ApiResult<List<Player>>> LoadAsync(BattleRosterRequest request, int relationFilter, Server server, string? forcedPlayerId, CancellationToken cancellationToken);
    }

    internal interface IBattleRosterCoordinator
    {
        IReadOnlyList<Player> CreateMetadataRoster(BattleRosterRequest request);
        Task<BattleRosterLoadResult> LoadAsync(BattleRosterRequest request, CancellationToken cancellationToken);
    }

    internal sealed class WgPlayerStatsProvider : IPlayerStatsProvider
    {
        private readonly bool useProxy;
        public WgPlayerStatsProvider(bool useProxy) => this.useProxy = useProxy;
        public APIType Type => useProxy ? APIType.WG_PUBLIC_WITH_YUYUKO_PROXY : APIType.WG_PUBLIC;

        public Task<ApiResult<List<Player>>> LoadAsync(BattleRosterRequest request, int relationFilter, Server server, string? forcedPlayerId, CancellationToken cancellationToken) =>
            PlayerStatsProviderResult.CaptureAsync(() => ApiUtils.WgPublicApiGetPlayersStatistics(request.PlayerCount, relationFilter, request.Arena, server, useProxy,
                request.ForceRefresh, forcedPlayerId, cancellationToken), cancellationToken);
    }

    internal sealed class VortexPlayerStatsProvider : IPlayerStatsProvider
    {
        public APIType Type => APIType.VORTEX;

        public Task<ApiResult<List<Player>>> LoadAsync(BattleRosterRequest request, int relationFilter, Server server, string? forcedPlayerId, CancellationToken cancellationToken) =>
            PlayerStatsProviderResult.CaptureAsync(() => ApiUtils.VortexApiGetPlayersStatistics(request.PlayerCount, relationFilter, request.Arena, server,
                request.ForceRefresh, forcedPlayerId, cancellationToken), cancellationToken);
    }

    internal static class PlayerStatsProviderResult
    {
        public static async Task<ApiResult<List<Player>>> CaptureAsync(Func<Task<List<Player>>> loader, CancellationToken cancellationToken)
        {
            try
            {
                return ApiResult<List<Player>>.Success(await loader().ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (NetworkRequestException ex)
            {
                return ApiResult<List<Player>>.Failed(ex.FailureKind, ex.InnerException?.Message, ex.RetryAfter);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                return ApiResult<List<Player>>.Failed(ApiFailureKind.InvalidResponse, ex.Message);
            }
            catch (Exception ex)
            {
                return ApiResult<List<Player>>.Failed(ApiFailureKind.Unknown, ex.Message);
            }
        }
    }

    internal sealed class BattleRosterCoordinator : IBattleRosterCoordinator
    {
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
            Stopwatch stopwatch = Stopwatch.StartNew();
            (long _, long requestCountBefore, long retryCountBefore) = NetworkUtils.GetMetricsSnapshot();
            (long cacheLookupsBefore, long cacheHitsBefore, _) = PlayerDataCache.GetMetricsSnapshot();
            IPlayerStatsProvider provider = CreateProvider(request);
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
                    player.Name == metadata.Name && player.Relation == metadata.Relation && player.Server == metadata.Server) ?? metadata)
                .ToList();
            (long _, long requestCountAfter, long retryCountAfter) = NetworkUtils.GetMetricsSnapshot();
            (long cacheLookupsAfter, long cacheHitsAfter, _) = PlayerDataCache.GetMetricsSnapshot();
            long cacheLookups = cacheLookupsAfter - cacheLookupsBefore;
            long cacheHits = cacheHitsAfter - cacheHitsBefore;
            LogUtils.WriteInfo($"Roster load completed: provider={provider.Type}, players={players.Count}, succeeded={results.Count(result => result.IsSuccess)}/{results.Length}, failures={string.Join(',', failures)}, elapsedMs={stopwatch.ElapsedMilliseconds}, httpRequests={requestCountAfter - requestCountBefore}, retries={retryCountAfter - retryCountBefore}, cacheHits={cacheHits}/{cacheLookups}");
            return new BattleRosterLoadResult(players, provider.Type, players.Count(player => player.IsDataStale), results.Count(result => result.IsSuccess), failures);
        }

        private static IPlayerStatsProvider CreateProvider(BattleRosterRequest request)
        {
            bool wgSupported = request.PrimaryServer is not Server.RU and not Server.CN &&
                (!request.SecondaryServerEnabled || request.SecondaryServer is not Server.RU and not Server.CN);
            if (wgSupported && request.ApiType is APIType.WG_PUBLIC or APIType.WG_PUBLIC_WITH_YUYUKO_PROXY)
            {
                return new WgPlayerStatsProvider(request.ApiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY);
            }
            return new VortexPlayerStatsProvider();
        }
    }
}
