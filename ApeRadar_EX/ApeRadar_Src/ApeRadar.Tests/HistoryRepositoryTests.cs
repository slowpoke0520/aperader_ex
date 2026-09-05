using ApeRadar.History;
using Xunit;

namespace ApeRadar.Tests;

public sealed class HistoryRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ApeRadar.Tests.{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(directory, "history.db");

    [Fact]
    public async Task UpsertDraft_IsIdempotent_AndPersistsPlayers()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        await repository.InitializeAsync();
        BattleRecord battle = CreateBattle();
        BattlePlayerRecord player = new() { PlayerKey = "ASIA:1", AccountId = "1", AccountName = "Tester", Relation = "0", ShipId = "101", ShipName = "Yamato" };

        long first = await repository.UpsertDraftAsync(battle, new[] { player }, null);
        battle.MapName = "Updated map";
        long second = await repository.UpsertDraftAsync(battle, new[] { player }, null);

        Assert.Equal(first, second);
        IReadOnlyList<BattleRecord> rows = await repository.GetBattlesAsync(new HistoryQuery());
        Assert.Single(rows);
        Assert.Equal("Updated map", rows[0].MapName);
    }

    [Fact]
    public async Task ApiDifference_ProducesOneExactBattle()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        BattleRecord battle = CreateBattle();
        ShipStatSnapshot before = Snapshot(100, 52, 6_000_000, 80);
        long id = await repository.UpsertDraftAsync(battle, Array.Empty<BattlePlayerRecord>(), before);
        ShipStatSnapshot after = Snapshot(101, 53, 6_120_000, 82);

        await repository.ResolveFromApiAsync(id, before, after);

        BattleRecord result = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(BattleMetricSource.ApiExact, result.Source);
        Assert.Equal(BattleCompleteness.Complete, result.Completeness);
        Assert.Equal(BattleResult.Win, result.Result);
        Assert.Equal(120_000, result.Damage);
        Assert.Equal(2, result.Frags);
        Assert.Equal(1, result.WinCount);
    }

    [Fact]
    public async Task ApiDifference_DoesNotInventMultipleIndividualBattles()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        BattleRecord battle = CreateBattle();
        ShipStatSnapshot before = Snapshot(100, 52, 6_000_000, 80);
        long id = await repository.UpsertDraftAsync(battle, Array.Empty<BattlePlayerRecord>(), before);

        await repository.ResolveFromApiAsync(id, before, Snapshot(103, 54, 6_240_000, 83));

        BattleRecord result = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(BattleMetricSource.ApiMerged, result.Source);
        Assert.Equal(3, result.BattleCount);
        Assert.Equal(2, result.WinCount);
        Assert.Equal(BattleResult.Unknown, result.Result);
    }

    [Fact]
    public async Task PendingChecks_ArePersistedAndCanBeForcedDue()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(CreateBattle(), Array.Empty<BattlePlayerRecord>(), null);
        await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck { BattleId = id, Attempt = 1, NextAttemptAt = DateTimeOffset.UtcNow.AddDays(1), LastError = "offline" });

        Assert.Empty(await repository.GetDuePendingChecksAsync(DateTimeOffset.UtcNow));
        await repository.MakePendingChecksDueAsync();
        Assert.Single(await repository.GetDuePendingChecksAsync(DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public async Task Query_FiltersByServerAccountShipAndDate()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BattleRecord wanted = CreateBattle();
        wanted.BattleKey = "wanted";
        wanted.StartedAt = now;
        await repository.UpsertDraftAsync(wanted, Array.Empty<BattlePlayerRecord>(), null);

        BattleRecord other = CreateBattle();
        other.BattleKey = "other";
        other.Server = "EU";
        other.AccountId = "2";
        other.ShipId = "202";
        other.StartedAt = now.AddDays(-2);
        await repository.UpsertDraftAsync(other, Array.Empty<BattlePlayerRecord>(), null);

        IReadOnlyList<BattleRecord> result = await repository.GetBattlesAsync(new HistoryQuery
        {
            Server = "ASIA", AccountId = "1", ShipId = "101",
            From = now.AddHours(-1), To = now.AddHours(1)
        });

        Assert.Single(result);
        Assert.Equal("wanted", result[0].BattleKey);
    }

    [Fact]
    public async Task ReplayHash_IsPersistedForDuplicateDetection()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(CreateBattle(), Array.Empty<BattlePlayerRecord>(), null);
        ReplayParseResult replay = new()
        {
            Status = ReplayParseStatus.Partial, FileHash = "ABC123", ParserVersion = "test",
            GameVersion = "15.8", BattleKey = "battle-1", Mode = "random",
            AccountName = "Tester", ShipId = "101", ErrorCode = "BattleResultsMissing"
        };

        await repository.CompleteFromReplayAsync(id, replay, "test.wowsreplay");

        Assert.True(await repository.HasReplayAsync("ABC123"));
        Assert.False(await repository.HasReplayAsync("different"));
    }

    [Fact]
    public async Task CorruptDatabase_IsBackedUpBeforeCreatingANewDatabase()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(DatabasePath, "not a sqlite database");
        SqliteHistoryRepository repository = new(DatabasePath);

        await repository.InitializeAsync();

        Assert.Empty(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Single(Directory.GetFiles(directory, "history.db.corrupt-*"));
    }

    private static BattleRecord CreateBattle() => new()
    {
        BattleKey = "battle-1", StartedAt = DateTimeOffset.UtcNow, Server = "ASIA", Mode = "random", MapName = "Map",
        AccountId = "1", AccountName = "Tester", ShipId = "101", ShipName = "Yamato"
    };

    private static ShipStatSnapshot Snapshot(double battles, double wins, double damage, double frags) => new()
    {
        CapturedAt = DateTimeOffset.UtcNow, Provider = "test", AccountId = "1", ShipId = "101",
        Battles = battles, Wins = wins, Losses = battles - wins, Damage = damage, Frags = frags
    };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
