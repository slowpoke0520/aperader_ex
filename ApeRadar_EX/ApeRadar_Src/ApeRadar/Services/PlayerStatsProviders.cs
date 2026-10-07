using ApeRadar.Models;
using ApeRadar.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.Services
{
    internal interface IPlayerStatsProvider
    {
        APIType Type { get; }
        Task<ApiResult<List<Player>>> LoadAsync(
            BattleRosterRequest request,
            int relationFilter,
            Server server,
            string? forcedPlayerId,
            CancellationToken cancellationToken);
    }

    internal interface IPlayerStatsProviderResolver
    {
        IPlayerStatsProvider Resolve(BattleRosterRequest request);
    }

    internal sealed class PlayerStatsProviderResolver : IPlayerStatsProviderResolver
    {
        private readonly IPlayerStatsProvider vortexProvider;
        private readonly IPlayerStatsProvider wgProvider;
        private readonly IPlayerStatsProvider wgProxyProvider;

        public PlayerStatsProviderResolver() : this(
            new VortexPlayerStatsProvider(),
            new WgPlayerStatsProvider(useProxy: false),
            new WgPlayerStatsProvider(useProxy: true))
        {
        }

        internal PlayerStatsProviderResolver(
            IPlayerStatsProvider vortexProvider,
            IPlayerStatsProvider wgProvider,
            IPlayerStatsProvider wgProxyProvider)
        {
            this.vortexProvider = vortexProvider ?? throw new ArgumentNullException(nameof(vortexProvider));
            this.wgProvider = wgProvider ?? throw new ArgumentNullException(nameof(wgProvider));
            this.wgProxyProvider = wgProxyProvider ?? throw new ArgumentNullException(nameof(wgProxyProvider));
        }

        public IPlayerStatsProvider Resolve(BattleRosterRequest request)
        {
            bool wgSupported = request.PrimaryServer is not Server.RU and not Server.CN &&
                (!request.SecondaryServerEnabled || request.SecondaryServer is not Server.RU and not Server.CN);
            if (!wgSupported) return vortexProvider;

            return request.ApiType switch
            {
                APIType.WG_PUBLIC => wgProvider,
                APIType.WG_PUBLIC_WITH_YUYUKO_PROXY => wgProxyProvider,
                _ => vortexProvider
            };
        }
    }

    internal sealed class WgPlayerStatsProvider : IPlayerStatsProvider
    {
        private readonly bool useProxy;
        private readonly IStatsCache cache;

        public WgPlayerStatsProvider(bool useProxy, IStatsCache? cache = null)
        {
            this.useProxy = useProxy;
            this.cache = cache ?? new PersistentStatsCache();
        }

        public APIType Type => useProxy ? APIType.WG_PUBLIC_WITH_YUYUKO_PROXY : APIType.WG_PUBLIC;

        public Task<ApiResult<List<Player>>> LoadAsync(
            BattleRosterRequest request,
            int relationFilter,
            Server server,
            string? forcedPlayerId,
            CancellationToken cancellationToken) =>
            PlayerStatsProviderResult.CaptureAsync(
                () => ApiUtils.WgPublicApiGetPlayersStatistics(
                    request.PlayerCount,
                    relationFilter,
                    request.Arena,
                    server,
                    useProxy,
                    request.ForceRefresh,
                    forcedPlayerId,
                    cancellationToken,
                    request.StatisticsOptions,
                    cache),
                cancellationToken);
    }

    internal sealed class VortexPlayerStatsProvider : IPlayerStatsProvider
    {
        private readonly IStatsCache cache;
        public VortexPlayerStatsProvider(IStatsCache? cache = null) => this.cache = cache ?? new PersistentStatsCache();
        public APIType Type => APIType.VORTEX;

        public Task<ApiResult<List<Player>>> LoadAsync(
            BattleRosterRequest request,
            int relationFilter,
            Server server,
            string? forcedPlayerId,
            CancellationToken cancellationToken) =>
            PlayerStatsProviderResult.CaptureAsync(
                () => ApiUtils.VortexApiGetPlayersStatistics(
                    request.PlayerCount,
                    relationFilter,
                    request.Arena,
                    server,
                    request.ForceRefresh,
                    forcedPlayerId,
                    cancellationToken,
                    request.StatisticsOptions,
                    cache),
                cancellationToken);
    }

    internal static class PlayerStatsProviderResult
    {
        public static async Task<ApiResult<List<Player>>> CaptureAsync(
            Func<Task<List<Player>>> loader,
            CancellationToken cancellationToken)
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
}
