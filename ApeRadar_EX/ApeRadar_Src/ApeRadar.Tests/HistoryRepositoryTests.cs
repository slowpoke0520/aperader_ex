using ApeRadar.History;
using Microsoft.Data.Sqlite;
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

        Assert.True(await repository.HasReplayAsync("ABC123", "test"));
        Assert.False(await repository.HasReplayAsync("ABC123", "older-parser"));
        Assert.False(await repository.HasReplayAsync("different", "test"));
    }

    [Fact]
    public async Task NewParserVersion_ReprocessesExistingBattleWithoutLosingRoster()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        BattlePlayerRecord player = new() { PlayerKey = "ASIA:1", AccountId = "1", AccountName = "Tester", Relation = "0", ShipId = "101", ShipName = "Yamato" };
        long id = await repository.UpsertDraftAsync(CreateBattle(), new[] { player }, null);
        ReplayParseResult first = new()
        {
            Status = ReplayParseStatus.Parsed, FileHash = "same-file", ParserVersion = "parser-1", GameVersion = "15.8",
            BattleKey = "battle-1", Mode = "random", AccountName = "Tester", ShipId = "101", Result = BattleResult.Win,
            Damage = 90_000, Frags = 1, Source = BattleMetricSource.ReplayDerived
        };
        await repository.CompleteFromReplayAsync(id, first, "same.wowsreplay");

        ReplayParseResult second = new()
        {
            Status = ReplayParseStatus.Parsed, FileHash = "same-file", ParserVersion = "parser-2", GameVersion = "15.8",
            BattleKey = "battle-1", Mode = "random", AccountName = "Tester", ShipId = "101", Result = BattleResult.Win,
            Damage = 100_000, Frags = 2, Source = BattleMetricSource.ReplayDerived,
            AdvancedMetrics = new BattleAdvancedMetrics { Survived = true, SurvivalAvailability = MetricAvailability.Stable, ParserSchemaVersion = "2" }
        };

        Assert.False(await repository.HasReplayAsync(second.FileHash, second.ParserVersion));
        BattleRecord existing = Assert.IsType<BattleRecord>(await repository.FindDraftForReplayAsync(second));
        await repository.CompleteFromReplayAsync(existing.Id, second, "same.wowsreplay");

        BattleRecord stored = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(100_000, stored.Damage);
        Assert.Single(await repository.GetBattlePlayersAsync(stored.Id));
        Assert.Equal("2", Assert.Single(await repository.GetAdvancedMetricsAsync(new[] { stored.Id })).Value.ParserSchemaVersion);
    }

    [Fact]
    public async Task Sessions_AreGroupedByAccountAndFortyFiveMinuteGap()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        DateTimeOffset start = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
        BattleRecord first = CreateBattle(); first.BattleKey = "first"; first.StartedAt = start;
        BattleRecord same = CreateBattle(); same.BattleKey = "same"; same.StartedAt = start.AddMinutes(45);
        BattleRecord later = CreateBattle(); later.BattleKey = "later"; later.StartedAt = start.AddMinutes(91);
        await repository.UpsertDraftAsync(first, Array.Empty<BattlePlayerRecord>(), null);
        await repository.UpsertDraftAsync(same, Array.Empty<BattlePlayerRecord>(), null);
        await repository.UpsertDraftAsync(later, Array.Empty<BattlePlayerRecord>(), null);

        IReadOnlyList<BattleSession> sessions = await repository.GetSessionsAsync("ASIA", "1");

        Assert.Equal(2, sessions.Count);
        Assert.Equal(new[] { 1, 2 }, sessions.Select(x => x.BattleCount).Order().ToArray());
    }

    [Fact]
    public async Task ReviewAndAdvancedMetrics_RoundTrip()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(CreateBattle(), Array.Empty<BattlePlayerRecord>(), null);
        ReplayParseResult replay = new()
        {
            Status = ReplayParseStatus.Parsed, FileHash = "advanced", ParserVersion = "test-2", GameVersion = "15.8",
            BattleKey = "battle-1", Mode = "random", AccountName = "Tester", ShipId = "101", Result = BattleResult.Win,
            Damage = 100_000, Frags = 2, Source = BattleMetricSource.ReplayDerived,
            AdvancedMetrics = new BattleAdvancedMetrics
            {
                Survived = true, SurvivalSeconds = 900, BattleDurationSeconds = 900, PotentialDamage = 1_500_000,
                SurvivalAvailability = MetricAvailability.Stable, PotentialDamageAvailability = MetricAvailability.Stable,
                ParserSchemaVersion = "2"
            }
        };
        await repository.CompleteFromReplayAsync(id, replay, "advanced.wowsreplay");
        await repository.SaveBattleReviewAsync(new BattleReview { BattleId = id, IsFavorite = true, Tags = new() { "发挥良好", "发挥良好" }, Note = "关键残局" });

        BattleAdvancedMetrics metrics = Assert.Single(await repository.GetAdvancedMetricsAsync(new[] { id })).Value;
        BattleReview review = Assert.IsType<BattleReview>(await repository.GetBattleReviewAsync(id));
        Assert.True(metrics.Survived);
        Assert.Equal(1_500_000, metrics.PotentialDamage);
        Assert.True(review.IsFavorite);
        Assert.Single(review.Tags);
        Assert.Equal("关键残局", review.Note);
    }

    [Fact]
    public async Task Sessions_CanBeSplitAndMergedWithoutLosingBattles()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        DateTimeOffset start = DateTimeOffset.UtcNow;
        BattleRecord first = CreateBattle(); first.BattleKey = "first"; first.StartedAt = start;
        BattleRecord second = CreateBattle(); second.BattleKey = "second"; second.StartedAt = start.AddMinutes(10);
        long firstId = await repository.UpsertDraftAsync(first, Array.Empty<BattlePlayerRecord>(), null);
        long secondId = await repository.UpsertDraftAsync(second, Array.Empty<BattlePlayerRecord>(), null);
        BattleSession session = Assert.Single(await repository.GetSessionsAsync());

        long newSessionId = await repository.SplitSessionAsync(session.Id, secondId);
        Assert.Equal(2, (await repository.GetSessionsAsync()).Count);
        await repository.MergeSessionsAsync(new[] { session.Id, newSessionId });

        BattleSession merged = Assert.Single(await repository.GetSessionsAsync());
        Assert.True(merged.IsManual);
        Assert.Equal(2, merged.BattleCount);
        Assert.Equal(new[] { firstId, secondId }, (await repository.GetSessionBattlesAsync(merged.Id)).Select(x => x.Id).ToArray());
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

    [Fact]
    public async Task VersionOneDatabase_IsBackedUpMigratedAndBackfilled()
    {
        Directory.CreateDirectory(directory);
        await using (SqliteConnection connection = new($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE SchemaMigrations(Version INTEGER PRIMARY KEY,AppliedAt TEXT NOT NULL);
                INSERT INTO SchemaMigrations VALUES(1,'2026-09-08T00:00:00Z');
                CREATE TABLE Battles(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,BattleKey TEXT NOT NULL UNIQUE,StartedAt TEXT NOT NULL,Server TEXT NOT NULL,
                    Mode TEXT NOT NULL,MapName TEXT NOT NULL,AccountId TEXT NOT NULL,AccountName TEXT NOT NULL,ShipId TEXT NOT NULL,
                    ShipName TEXT NOT NULL,Result INTEGER NOT NULL,WinCount REAL NULL,Damage INTEGER NULL,Frags REAL NULL,
                    BattleCount INTEGER NOT NULL DEFAULT 1,Source INTEGER NOT NULL,Completeness INTEGER NOT NULL,ReplayHash TEXT NULL,
                    ReplayVersion TEXT NULL,StatusMessage TEXT NULL,UpdatedAt TEXT NOT NULL);
                INSERT INTO Battles(BattleKey,StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,Result,BattleCount,Source,Completeness,UpdatedAt)
                VALUES('legacy','2026-09-08T00:00:00Z','ASIA','random','Map','1','Tester','101','Yamato',4,1,4,2,'2026-09-08T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteHistoryRepository repository = new(DatabasePath);
        await repository.InitializeAsync();

        BattleRecord battle = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.NotNull(battle.SessionId);
        Assert.Single(await repository.GetSessionsAsync());
        Assert.Single(Directory.GetFiles(directory, "history.db.pre-v2-*.bak"));
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
