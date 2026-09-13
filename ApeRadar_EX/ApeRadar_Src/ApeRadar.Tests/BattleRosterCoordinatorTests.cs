using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ApeRadar.Tests;

public sealed class BattleRosterCoordinatorTests
{
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
}
