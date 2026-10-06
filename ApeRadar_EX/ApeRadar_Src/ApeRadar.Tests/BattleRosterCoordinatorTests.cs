using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ApeRadar.Tests;

public sealed class BattleRosterCoordinatorTests
{
    [Fact]
    public void ProviderResolver_PreservesConfiguredProviderAndUnsupportedServerFallbacks()
    {
        PlayerStatsProviderResolver resolver = new();

        Assert.Equal(APIType.WG_PUBLIC, resolver.Resolve(CreateRequest(APIType.WG_PUBLIC, Server.ASIA)).Type);
        Assert.Equal(APIType.WG_PUBLIC_WITH_YUYUKO_PROXY,
            resolver.Resolve(CreateRequest(APIType.WG_PUBLIC_WITH_YUYUKO_PROXY, Server.EU)).Type);
        Assert.Equal(APIType.VORTEX, resolver.Resolve(CreateRequest(APIType.VORTEX, Server.NA)).Type);
        Assert.Equal(APIType.VORTEX, resolver.Resolve(CreateRequest(APIType.WG_PUBLIC, Server.RU)).Type);
        Assert.Equal(APIType.VORTEX,
            resolver.Resolve(CreateRequest(APIType.WG_PUBLIC, Server.ASIA, Server.CN, secondaryServerEnabled: true)).Type);
    }

    [Fact]
    public async Task Coordinator_RoutesCrossServerRefreshAndKeepsSuccessfulSideWhenOtherSideFails()
    {
        JObject arena = JObject.Parse("""
            {
              "vehicles": [
                { "id": 101, "name": "Ally", "relation": 1, "shipId": "4179601392" },
                { "id": 102, "name": "Enemy", "relation": 2, "shipId": "4179601393" }
              ]
            }
            """);
        Player ally = new("Ally", Server.ASIA, "1", "4179601392") { ID = "100", Battles = 42 };
        RecordingProvider provider = new(
            APIType.WG_PUBLIC,
            (relationFilter, _) => relationFilter == 1
                ? ApiResult<List<Player>>.Success(new List<Player> { ally })
                : ApiResult<List<Player>>.Failed(ApiFailureKind.Network));
        BattleRosterCoordinator coordinator = new(new FixedProviderResolver(provider));
        BattleRosterRequest request = new(
            arena,
            2,
            Server.ASIA,
            Server.EU,
            true,
            APIType.WG_PUBLIC,
            true,
            "200",
            Server.EU);

        BattleRosterLoadResult result = await coordinator.LoadAsync(request, CancellationToken.None);

        Assert.Equal(2, provider.Calls.Count);
        Assert.Contains(provider.Calls, call => call.RelationFilter == 1 && call.Server == Server.ASIA && call.ForcedPlayerId == null);
        Assert.Contains(provider.Calls, call => call.RelationFilter == 2 && call.Server == Server.EU && call.ForcedPlayerId == "200");
        Assert.Equal(APIType.WG_PUBLIC, result.Provider);
        Assert.Equal(1, result.SuccessfulRequestCount);
        Assert.Equal(new[] { ApiFailureKind.Network }, result.Failures);
        Assert.Equal(42, result.Players.Single(player => player.Name == "Ally").Battles);
        Assert.Equal("-1", result.Players.Single(player => player.Name == "Enemy").ID);
        Assert.True(result.IsPartial);
        Assert.False(result.IsFailed);
    }

    [Fact]
    public void MetadataRoster_IsAvailableWithoutNetwork_AndRoutesCrossServerEnemies()
    {
        JObject arena = JObject.Parse("""
            {
              "vehicles": [
                { "id": 101, "name": "Self", "relation": 0, "shipId": "4179601392" },
                { "id": 102, "name": "Ally", "relation": 1, "shipId": "4179601393" },
                { "id": 103, "name": "Enemy", "relation": 2, "shipId": "4179601394" },
                { "id": 2, "name": ":Bot:", "relation": 2, "shipId": "1" }
              ]
            }
            """);
        BattleRosterRequest request = new(arena, 4, Server.ASIA, Server.EU, true, APIType.VORTEX, false, null, null);

        IReadOnlyList<Player> players = new BattleRosterCoordinator().CreateMetadataRoster(request);

        Assert.Equal(3, players.Count);
        Assert.Equal(Server.ASIA, players.Single(x => x.Name == "Self").Server);
        Assert.Equal(Server.EU, players.Single(x => x.Name == "Enemy").Server);
        Assert.All(players, player => Assert.Equal(-1, player.AccountWinrate));
    }

    [Fact]
    public async Task NetworkLayer_RejectsNonHttpsEndpointsWithTypedFailure()
    {
        NetworkRequestException exception = await Assert.ThrowsAsync<NetworkRequestException>(() =>
            NetworkUtils.HttpGet("http://example.invalid/data"));

        Assert.Equal(ApiFailureKind.InvalidResponse, exception.FailureKind);
    }

    [Fact]
    public async Task ProviderBoundary_PreservesRateLimitReasonAndRetryAfter()
    {
        TimeSpan retryAfter = TimeSpan.FromSeconds(12);
        ApiResult<List<Player>> result = await PlayerStatsProviderResult.CaptureAsync(
            () => Task.FromException<List<Player>>(new NetworkRequestException(ApiFailureKind.RateLimited, retryAfter: retryAfter)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiFailureKind.RateLimited, result.Failure);
        Assert.Equal(retryAfter, result.RetryAfter);
    }

    [Fact]
    public async Task ProviderBoundary_DoesNotSwallowBattleCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlayerStatsProviderResult.CaptureAsync(
            () => Task.FromCanceled<List<Player>>(cancellation.Token), cancellation.Token));
    }

    [Fact]
    public async Task VortexNotFound_IsolatedWithoutCancellingSuccessfulSiblingRequest()
    {
        Task<string> missing = ApiUtils.VortexHttpGetAllowNotFoundAsync(() =>
            Task.FromException<string>(new NetworkRequestException(ApiFailureKind.NotFound)));
        Task<string> successful = ApiUtils.VortexHttpGetAllowNotFoundAsync(() =>
            Task.FromResult("{\"status\":\"ok\",\"data\":{}}"));

        string[] responses = await Task.WhenAll(missing, successful);

        Assert.Contains("Not Found", responses[0]);
        Assert.Contains("\"status\":\"ok\"", responses[1]);
    }

    [Fact]
    public async Task VortexTransientFailure_RemainsRosterLevelFailure()
    {
        NetworkRequestException exception = await Assert.ThrowsAsync<NetworkRequestException>(() =>
            ApiUtils.VortexHttpGetAllowNotFoundAsync(() =>
                Task.FromException<string>(new NetworkRequestException(ApiFailureKind.Network))));

        Assert.Equal(ApiFailureKind.Network, exception.FailureKind);
    }

    [Fact]
    public void RosterResult_IsPartialForOnePlayerFailureAndFailedWhenEveryPlayerFails()
    {
        Player available = new("Available", Server.ASIA, "1", "3760142160") { ID = "100", Battles = 20 };
        Player missing = new("Missing", Server.ASIA, "1", "3760142160") { ID = "200", IsDataFetchFailed = true };

        BattleRosterLoadResult partial = new(new[] { available, missing }, APIType.VORTEX, 0, 1, Array.Empty<ApiFailureKind>());
        BattleRosterLoadResult failed = new(new[] { missing }, APIType.VORTEX, 0, 1, Array.Empty<ApiFailureKind>());

        Assert.True(partial.IsPartial);
        Assert.False(partial.IsFailed);
        Assert.True(failed.IsFailed);
    }

    [Fact]
    public void EmptyMetadataBattlefield_DoesNotProduceNanTeamSummaries()
    {
        Battlefield battlefield = new("random", DateTimeOffset.UtcNow, Array.Empty<Player>().ToList());

        Assert.Equal(-1, battlefield.AllyAvgAccountWinrate);
        Assert.Equal(-1, battlefield.EnemyAvgAccountWinrate);
        Assert.False(double.IsNaN(battlefield.AllyAvgBattleCount));
    }

    [Fact]
    public void ShipCache_UsesOneHourFreshnessWindow()
    {
        PlayerDataSnapshot fresh = new() { FetchedAt = DateTimeOffset.Now.AddMinutes(-59) };
        PlayerDataSnapshot stale = new() { FetchedAt = DateTimeOffset.Now.AddMinutes(-61) };

        Assert.False(fresh.IsExpired());
        Assert.True(stale.IsExpired());
        Assert.Equal(TimeSpan.FromHours(6), PlayerDataCache.AccountAndTierTtl);
        Assert.Equal(TimeSpan.FromDays(7), PlayerDataCache.IdentityTtl);
        Assert.Equal(TimeSpan.FromDays(30), PlayerDataCache.Retention);
    }

    private static BattleRosterRequest CreateRequest(
        APIType apiType,
        Server primaryServer,
        Server secondaryServer = Server.EU,
        bool secondaryServerEnabled = false) =>
        new(new JObject { ["vehicles"] = new JArray() }, 0, primaryServer, secondaryServer, secondaryServerEnabled, apiType, false, null, null);

    private sealed class FixedProviderResolver : IPlayerStatsProviderResolver
    {
        private readonly IPlayerStatsProvider provider;

        public FixedProviderResolver(IPlayerStatsProvider provider) => this.provider = provider;

        public IPlayerStatsProvider Resolve(BattleRosterRequest request) => provider;
    }

    private sealed class RecordingProvider : IPlayerStatsProvider
    {
        private readonly Func<int, Server, ApiResult<List<Player>>> resultFactory;

        public RecordingProvider(APIType type, Func<int, Server, ApiResult<List<Player>>> resultFactory)
        {
            Type = type;
            this.resultFactory = resultFactory;
        }

        public APIType Type { get; }
        public List<ProviderCall> Calls { get; } = new();

        public Task<ApiResult<List<Player>>> LoadAsync(
            BattleRosterRequest request,
            int relationFilter,
            Server server,
            string? forcedPlayerId,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ProviderCall(relationFilter, server, forcedPlayerId));
            return Task.FromResult(resultFactory(relationFilter, server));
        }
    }

    private sealed record ProviderCall(int RelationFilter, Server Server, string? ForcedPlayerId);
}
