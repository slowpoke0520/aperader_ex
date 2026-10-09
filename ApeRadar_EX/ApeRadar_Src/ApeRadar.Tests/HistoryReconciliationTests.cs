using ApeRadar.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ApeRadar.Tests;

public sealed class HistoryReconciliationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aperadar-reconciliation-" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "history.db");
    private static readonly DateTimeOffset Started = new(2026, 10, 7, 14, 34, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApiAndReplay_ProduceOneBattleWithPreciseMetricsAndReplayDetails(bool apiFirst)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(Battle(), Roster(), Snapshot(100));
        if (apiFirst) await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(101));
        ReplayParseResult replay = Replay();
        BattleRecord match = Assert.IsType<BattleRecord>(await repository.FindDraftForReplayAsync(replay));
        Assert.Equal(id, match.Id);
        await repository.CompleteFromReplayAsync(id, replay, "battle.wowsreplay");
        if (!apiFirst) await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(101));

        BattleRecord result = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(BattleMetricSource.ApiExact, result.Source);
        Assert.Equal(120_000, result.Damage);
        Assert.Equal(1, result.BattleCount);
        Assert.Equal(BattleCompleteness.Complete, result.Completeness);
        Assert.Equal("replay-hash", result.ReplayHash);
        Assert.Equal(2, (await repository.GetBattlePlayersAsync(id)).Count);
        Assert.Equal(987_000, (await repository.GetAdvancedMetricsAsync(new[] { id }))[id].PotentialDamage);
    }

    [Fact]
    public async Task CompletedReplay_ReplacesMergedApiIntervalWithOneBattle()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(Battle(), Roster(), null);
        await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(102));
        BattleRecord match = Assert.IsType<BattleRecord>(await repository.FindDraftForReplayAsync(Replay()));
        Assert.Equal(id, match.Id);
        await repository.CompleteFromReplayAsync(id, Replay(), "battle.wowsreplay");
        BattleRecord result = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(1, result.BattleCount);
        Assert.Equal(BattleMetricSource.ReplayDerived, result.Source);
        Assert.Equal(119_997, result.Damage);
        // An already queued, delayed interval check cannot overwrite this single battle.
        await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(102));
        Assert.Equal(1, (await repository.GetBattleAsync(id))!.BattleCount);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("account")]
    [InlineData("map")]
    [InlineData("roster")]
    [InlineData("missing-roster")]
    public async Task NearbyDistinctIdentities_AreNotMatched(string difference)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        await repository.UpsertDraftAsync(Battle(), Roster(), null);
        ReplayParseResult next = new()
        {
            Server = difference == "server" ? "EU" : "ASIA", AccountId = difference == "account" ? "2" : "1",
            AccountName = "Tester", ShipId = "101", MapName = difference == "map" ? "AnotherMap" : "Map",
            StartedAt = Started.AddMinutes(4), BattleKey = "another-key", FileHash = "another-hash",
            RosterSignature = difference == "roster" ? "newplayer:303|tester:101" :
                difference == "missing-roster" ? "" : "other:202|tester:101"
        };
        Assert.Null(await repository.FindDraftForReplayAsync(next));
    }

    [Fact]
    public async Task PartialSingleReplay_DoesNotInheritMultiBattleApiTotalsOrInventMissingValues()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(Battle(), Roster(), null);
        await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(102));
        ReplayParseResult partial = new()
        {
            FileHash = "partial-replay", ParserVersion = "test", Source = BattleMetricSource.ReplayDerived,
            Status = ReplayParseStatus.Partial, Damage = 32_100, Result = BattleResult.Unknown
        };
        await repository.CompleteFromReplayAsync(id, partial, "partial.wowsreplay");
        await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(102));
        await repository.MarkPendingAttemptAsync(new PendingResultCheck { BattleId = id, LastError = "StatisticsNotUpdated" }, true);
        BattleRecord stored = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(1, stored.BattleCount);
        Assert.Equal(32_100, stored.Damage);
        Assert.Null(stored.Frags);
        Assert.Null(stored.WinCount);
        Assert.Equal(BattleMetricSource.ReplayDerived, stored.Source);
        Assert.Equal(BattleCompleteness.Partial, stored.Completeness);
    }

    [Fact]
    public async Task ReconnectAfterApiCompletion_ReusesOriginalBattleAndPreservesRoster()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(Battle(), Roster(), null);
        await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(101));
        BattleRecord reconnect = Battle();
        reconnect.BattleKey = "reconnect";
        reconnect.StartedAt = Started.AddMinutes(4);
        reconnect.MapName = "Map";
        long rejoined = await repository.UpsertDraftAsync(reconnect, Roster(), null);
        Assert.Equal(id, rejoined);
        Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        BattleRecord emptyRosterUpdate = Battle();
        emptyRosterUpdate.BattleKey = "reconnect";
        emptyRosterUpdate.RosterSignature = "other:202|tester:101";
        await repository.UpsertDraftAsync(emptyRosterUpdate, Array.Empty<BattlePlayerRecord>(), null);
        Assert.Equal(2, (await repository.GetBattlePlayersAsync(id)).Count);
    }

    [Fact]
    public async Task ExpiredApiCheck_DoesNotEraseCompletedBattle()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(Battle(), Roster(), null);
        await repository.CompleteFromReplayAsync(id, Replay(), "battle.wowsreplay");
        await repository.MarkPendingAttemptAsync(new PendingResultCheck { BattleId = id, LastError = "StatisticsNotUpdated" }, true);
        BattleRecord stored = (await repository.GetBattleAsync(id))!;
        Assert.Equal(BattleCompleteness.Complete, stored.Completeness);
        Assert.Equal(BattleMetricSource.ReplayDerived, stored.Source);
    }

    [Fact]
    public async Task ReparseEarlierReconnectSegment_UsesSavedFileAssociationWithoutRoster()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(Battle(), Roster(), null);
        await repository.CompleteFromReplayAsync(id, Replay(), "first.wowsreplay");
        await repository.CompleteFromReplayAsync(id, new ReplayParseResult
        {
            FileHash = "middle-segment", ParserVersion = "test", Source = BattleMetricSource.ReplayDerived,
            Status = ReplayParseStatus.Partial
        }, "middle.wowsreplay");
        await repository.CompleteFromReplayAsync(id, new ReplayParseResult
        {
            FileHash = "last-segment", ParserVersion = "test", Source = BattleMetricSource.ReplayDerived,
            Status = ReplayParseStatus.Partial
        }, "last.wowsreplay");
        BattleRecord match = Assert.IsType<BattleRecord>(await repository.FindDraftForReplayAsync(new ReplayParseResult
        {
            FileHash = "middle-segment", ParserVersion = "updated-parser", StartedAt = Started.AddMinutes(4),
            AccountName = "Tester", AccountId = "1", Server = "ASIA", ShipId = "101", MapName = "Map"
        }));
        Assert.Equal(id, match.Id);
        Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
    }

    [Fact]
    public async Task LegacyDuplicates_AreBackedUpMergedAndKeepAllRelatedData()
    {
        SqliteHistoryRepository initial = new(DatabasePath);
        long original = await initial.UpsertDraftAsync(Battle(), Roster(), Snapshot(100));
        await initial.ResolveFromApiAsync(original, Snapshot(100), Snapshot(101));
        long duplicate;
        await using (SqliteConnection connection = new($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Battles(SessionId,BattleKey,StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,
                    ShipType,RosterSignature,Result,WinCount,Damage,Frags,BattleCount,Source,Completeness,UpdatedAt)
                SELECT SessionId,'legacy-replay',StartedAt,Server,Mode,'Map',AccountId,AccountName,ShipId,ShipName,
                    ShipType,RosterSignature,Result,WinCount,119997,Frags,1,1,0,UpdatedAt FROM Battles WHERE Id=$id RETURNING Id;
                """;
            command.Parameters.AddWithValue("$id", original);
            duplicate = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        await initial.CompleteFromReplayAsync(duplicate, Replay(), "legacy.wowsreplay");
        await initial.SaveBattleReviewAsync(new BattleReview { BattleId = original, Tags = new() { "Positioning" }, Note = "Original note" });
        await initial.SaveBattleReviewAsync(new BattleReview { BattleId = duplicate, IsFavorite = true, Tags = new() { "ReviewReplay" }, Note = "Replay note" });
        await initial.AddOrUpdatePendingCheckAsync(new PendingResultCheck { BattleId = duplicate, NextAttemptAt = Started });
        await using (SqliteConnection connection = new($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ShipSnapshots(BattleId,CapturedAt,Provider,AccountId,ShipId,Battles,Wins,Losses,Damage,Frags)
                SELECT $duplicate,CapturedAt,Provider,AccountId,ShipId,Battles,Wins,Losses,Damage,Frags FROM ShipSnapshots WHERE BattleId=$original;
                UPDATE BattleSessions SET IsManual=1;
                DELETE FROM SchemaMigrations WHERE Version>=5;
                """;
            command.Parameters.AddWithValue("$original", original);
            command.Parameters.AddWithValue("$duplicate", duplicate);
            await command.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();

        SqliteHistoryRepository migrated = new(DatabasePath);
        await migrated.InitializeAsync();
        BattleRecord battle = Assert.Single(await migrated.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(original, battle.Id);
        Assert.Equal(120_000, battle.Damage);
        Assert.Equal("replay-hash", battle.ReplayHash);
        Assert.Equal(2, (await migrated.GetBattlePlayersAsync(original)).Count);
        Assert.Equal(987_000, (await migrated.GetAdvancedMetricsAsync(new[] { original }))[original].PotentialDamage);
        Assert.Single(await migrated.GetDamageBreakdownsAsync(original));
        Assert.True(await migrated.HasReplayAsync("replay-hash", "test"));
        Assert.Empty(await migrated.GetDuePendingChecksAsync(DateTimeOffset.UtcNow));
        BattleReview review = (await migrated.GetBattleReviewAsync(original))!;
        Assert.True(review.IsFavorite);
        Assert.Equal(new[] { "Positioning", "ReviewReplay" }, review.Tags);
        Assert.Contains("Original note", review.Note);
        Assert.Contains("Replay note", review.Note);
        Assert.True(Assert.Single(await migrated.GetSessionsAsync()).IsManual);
        string backupPath = Assert.Single(Directory.GetFiles(directory, "history.db.pre-v6-*.bak"));
        await using (SqliteConnection backup = new($"Data Source={backupPath};Mode=ReadOnly"))
        {
            await backup.OpenAsync();
            await using SqliteCommand command = backup.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Battles";
            Assert.Equal(2L, await command.ExecuteScalarAsync());
        }
        await using (SqliteConnection connection = new($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ShipSnapshots WHERE BattleId=$id";
            command.Parameters.AddWithValue("$id", original);
            Assert.Equal(2L, await command.ExecuteScalarAsync());
            command.CommandText = "PRAGMA foreign_key_check";
            Assert.Null(await command.ExecuteScalarAsync());
        }
        await new SqliteHistoryRepository(DatabasePath).InitializeAsync();
        Assert.Single(Directory.GetFiles(directory, "history.db.pre-v6-*.bak"));
    }

    private static BattleRecord Battle() => new()
    {
        BattleKey = "live-battle", StartedAt = Started, Server = "ASIA", AccountId = "1", AccountName = "Tester",
        Mode = "RandomBattle", ShipId = "101", ShipName = "Yamato", ShipType = "Battleship", MapName = "spaces/Map"
    };
    private static BattlePlayerRecord[] Roster() => new[]
    {
        new BattlePlayerRecord { PlayerKey = "ASIA:1", AccountId = "1", AccountName = "Tester", Relation = "0", ShipId = "101", ShipName = "Yamato", ShipType = "Battleship" },
        new BattlePlayerRecord { PlayerKey = "ASIA:2", AccountId = "2", AccountName = "Other", Relation = "2", ShipId = "202", ShipName = "Des Moines", ShipType = "Cruiser" }
    };
    private static ShipStatSnapshot Snapshot(int battles) => new()
    {
        AccountId = "1", ShipId = "101", CapturedAt = Started.AddMinutes(battles - 100), Provider = "test",
        Battles = battles, Wins = 50 + battles - 100, Losses = 50, Damage = 6_000_000 + (battles - 100) * 120_000, Frags = 100 + battles - 100
    };
    private static ReplayParseResult Replay() => new()
    {
        Server = "ASIA", AccountId = "1", BattleKey = "replay-key", StartedAt = Started,
        AccountName = "Tester", ShipId = "101", MapName = "Map", RosterSignature = "other:202|tester:101",
        FileHash = "replay-hash", ParserVersion = "test", GameVersion = "15.8", Status = ReplayParseStatus.Parsed,
        Source = BattleMetricSource.ReplayDerived, Result = BattleResult.Win, Damage = 119_997, Frags = 1,
        AdvancedMetrics = new BattleAdvancedMetrics { PotentialDamage = 987_000, PotentialDamageAvailability = MetricAvailability.Stable },
        DamageBreakdowns = new[] { new BattleDamageBreakdown { RawTypeCode = 1, Damage = 119_997, Category = DamageCategory.Artillery, Availability = MetricAvailability.Stable } }
    };
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
