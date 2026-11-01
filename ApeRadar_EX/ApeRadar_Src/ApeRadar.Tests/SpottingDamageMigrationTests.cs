using ApeRadar.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ApeRadar.Tests;

public sealed class SpottingDamageMigrationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ApeRadar.SpottingMigration.{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(directory, "history.db");

    [Theory]
    [InlineData("2", 25_000)]
    [InlineData("3", 25_000)]
    [InlineData("3", 0)]
    public async Task LegacySpotting_IsRelabelledAndBackedUpWithoutLosingRelatedData(string schema, long damage)
    {
        long id = await CreateLegacyDatabaseAsync(schema, damage);
        SqliteHistoryRepository migrated = new(DatabasePath);
        await migrated.InitializeAsync();

        BattleAdvancedMetrics metrics = (await migrated.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(damage, metrics.SpottingDamage);
        Assert.Equal(MetricAvailability.Stable, metrics.SpottingDamageAvailability);
        Assert.Null(metrics.DamageTaken);
        Assert.Equal(MetricAvailability.Unavailable, metrics.DamageTakenAvailability);
        Assert.Equal(schema, metrics.ParserSchemaVersion);
        Assert.Equal(1_000_000, metrics.PotentialDamage);
        Assert.True(metrics.Survived);
        Assert.Null(SessionAnalysisService.MetricValue((await migrated.GetBattleAsync(id))!, metrics, "TradeRatio", true));
        IReadOnlyList<BattleDamageBreakdown> breakdowns = await migrated.GetDamageBreakdownsAsync(id);
        Assert.Equal(damage, Assert.Single(breakdowns, x => x.Direction == DamageDirection.Spotting).Damage);
        Assert.Single(breakdowns, x => x.Direction == DamageDirection.Dealt);
        Assert.DoesNotContain(breakdowns, x => x.Direction == DamageDirection.Received);
        Assert.True((await migrated.GetBattleReviewAsync(id))!.IsFavorite);
        Assert.Equal("saved note", (await migrated.GetBattleReviewAsync(id))!.Note);
        Assert.Equal("unsaved draft", (await migrated.GetReviewDraftAsync(id))!.Note);
        Assert.Single(await migrated.GetBattlePlayersAsync(id));
        Assert.True(await migrated.HasReplayAsync("same-replay", "legacy-parser"));
        Assert.Single(await migrated.GetBattlesAsync(new HistoryQuery()));

        string backup = Assert.Single(Directory.GetFiles(directory, "history.db.pre-v7-*.bak"));
        Assert.Equal(damage, await ScalarAsync("SELECT DamageTaken FROM BattleAdvancedMetrics", backup));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM BattleDamageBreakdowns WHERE Direction=1", backup));
        Assert.Equal(6L, await ScalarAsync("SELECT MAX(Version) FROM SchemaMigrations", backup));
        Assert.Equal(7L, await ScalarAsync("SELECT MAX(Version) FROM SchemaMigrations"));
        Assert.Null(await ScalarAsync("PRAGMA foreign_key_check"));

        await new SqliteHistoryRepository(DatabasePath).InitializeAsync();
        Assert.Single(Directory.GetFiles(directory, "history.db.pre-v7-*.bak"));
        Assert.Equal(damage, (await migrated.GetAdvancedMetricsAsync(new[] { id }))[id].SpottingDamage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("unknown")]
    [InlineData("4unknown")]
    public async Task UnknownLegacySource_IsInvalidatedWithoutGuessingSpotting(string schema)
    {
        long id = await CreateLegacyDatabaseAsync(schema, 25_000);
        SqliteHistoryRepository repository = new(DatabasePath);
        BattleAdvancedMetrics metrics = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];

        Assert.Null(metrics.DamageTaken);
        Assert.Null(metrics.SpottingDamage);
        Assert.Equal(MetricAvailability.Unavailable, metrics.DamageTakenAvailability);
        Assert.Equal(MetricAvailability.Unavailable, metrics.SpottingDamageAvailability);
        Assert.DoesNotContain(await repository.GetDamageBreakdownsAsync(id), x => x.Direction != DamageDirection.Dealt);
        Assert.Equal(1_000_000, metrics.PotentialDamage);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("5")]
    public async Task CorrectedOrFutureSchema_DoesNotLoseReceivedDamage(string schema)
    {
        long id = await CreateLegacyDatabaseAsync(schema, 25_000);
        SqliteHistoryRepository repository = new(DatabasePath);
        BattleAdvancedMetrics metrics = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];

        Assert.Equal(25_000, metrics.DamageTaken);
        Assert.Equal(MetricAvailability.Experimental, metrics.DamageTakenAvailability);
        Assert.Null(metrics.SpottingDamage);
        Assert.Contains(await repository.GetDamageBreakdownsAsync(id), x => x.Direction == DamageDirection.Received);
    }

    [Fact]
    public async Task UnavailableLegacyValue_IsNotPromotedToValidSpotting()
    {
        long id = await CreateLegacyDatabaseAsync("3", 25_000);
        await ExecuteAsync("UPDATE BattleAdvancedMetrics SET DamageTakenAvailability=0");
        SqliteHistoryRepository repository = new(DatabasePath);
        BattleAdvancedMetrics metrics = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Null(metrics.SpottingDamage);
        Assert.Equal(MetricAvailability.Unavailable, metrics.SpottingDamageAvailability);
        Assert.Null(metrics.DamageTaken);
        Assert.DoesNotContain(await repository.GetDamageBreakdownsAsync(id), x => x.Direction != DamageDirection.Dealt);
    }

    [Fact]
    public async Task DatabaseWithoutMigrationTable_IsBackedUpBeforeRepair()
    {
        long id = await CreateLegacyDatabaseAsync("3", 25_000);
        await ExecuteAsync("DROP TABLE SchemaMigrations");
        SqliteHistoryRepository repository = new(DatabasePath);
        await repository.InitializeAsync();

        string backup = Assert.Single(Directory.GetFiles(directory, "history.db.pre-v7-*.bak"));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name='SchemaMigrations'", backup));
        Assert.Equal(25_000L, await ScalarAsync("SELECT DamageTaken FROM BattleAdvancedMetrics", backup));
        Assert.Equal(25_000, (await repository.GetAdvancedMetricsAsync(new[] { id }))[id].SpottingDamage);
        Assert.Null((await repository.GetAdvancedMetricsAsync(new[] { id }))[id].DamageTaken);
    }

    [Fact]
    public async Task ReceivedBreakdownWithoutMetricProvenance_IsRemovedFromActiveHistory()
    {
        long id = await CreateLegacyDatabaseAsync("3", 25_000);
        await ExecuteAsync("DELETE FROM BattleAdvancedMetrics");
        SqliteHistoryRepository repository = new(DatabasePath);
        Assert.DoesNotContain(await repository.GetDamageBreakdownsAsync(id), x => x.Direction == DamageDirection.Received);
        string backup = Assert.Single(Directory.GetFiles(directory, "history.db.pre-v7-*.bak"));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM BattleDamageBreakdowns WHERE Direction=1", backup));
    }

    [Fact]
    public async Task FailedMigration_RollsBackColumnsAndMetricChanges()
    {
        await CreateLegacyDatabaseAsync("3", 25_000);
        await ExecuteAsync("""
            CREATE TRIGGER RejectMigration BEFORE INSERT ON SchemaMigrations WHEN NEW.Version=7
            BEGIN SELECT RAISE(ABORT,'test migration failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => new SqliteHistoryRepository(DatabasePath).InitializeAsync());

        Assert.Equal(25_000L, await ScalarAsync("SELECT DamageTaken FROM BattleAdvancedMetrics"));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('BattleAdvancedMetrics') WHERE name='SpottingDamage'"));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM SchemaMigrations WHERE Version=7"));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM BattleDamageBreakdowns WHERE Direction=1"));
        Assert.Single(Directory.GetFiles(directory, "history.db.pre-v7-*.bak"));
    }

    [Fact]
    public async Task ReparseWithSpottingOnly_RoundTripsAndDoesNotDuplicateBattleOrEraseReviews()
    {
        long id = await CreateLegacyDatabaseAsync("3", 25_000);
        SqliteHistoryRepository repository = new(DatabasePath);
        await repository.InitializeAsync();
        ReplayParseResult updated = new()
        {
            FileHash = "same-replay", ParserVersion = "fixed/aperadar-4", Status = ReplayParseStatus.Partial,
            AdvancedMetrics = new BattleAdvancedMetrics
            {
                SpottingDamage = 0, SpottingDamageAvailability = MetricAvailability.Stable, ParserSchemaVersion = "4"
            },
            DamageBreakdowns = new[]
            {
                new BattleDamageBreakdown { Direction = DamageDirection.Spotting, RawTypeCode = 2, Damage = 0, Availability = MetricAvailability.Experimental }
            }
        };
        Assert.False(await repository.HasReplayAsync(updated.FileHash, updated.ParserVersion));
        BattleRecord linked = Assert.IsType<BattleRecord>(await repository.FindDraftForReplayAsync(updated));
        Assert.Equal(id, linked.Id);
        await repository.CompleteFromReplayAsync(id, updated, "same.wowsreplay");
        await repository.CompleteFromReplayAsync(id, updated, "same.wowsreplay");

        BattleAdvancedMetrics result = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(0, result.SpottingDamage);
        Assert.Equal("4", result.ParserSchemaVersion);
        Assert.Null(result.DamageTaken);
        Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(DamageDirection.Spotting, Assert.Single(await repository.GetDamageBreakdownsAsync(id)).Direction);
        Assert.Equal("saved note", (await repository.GetBattleReviewAsync(id))!.Note);
        Assert.Equal("unsaved draft", (await repository.GetReviewDraftAsync(id))!.Note);
        Assert.Single(await repository.GetBattlePlayersAsync(id));
    }

    private async Task<long> CreateLegacyDatabaseAsync(string parserSchema, long damage)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await repository.UpsertDraftAsync(new BattleRecord
        {
            BattleKey = "battle", StartedAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
            Server = "ASIA", AccountId = "1", AccountName = "Tester", ShipId = "101", ShipName = "Test ship",
            Mode = "RandomBattle", MapName = "Map"
        }, new[] { new BattlePlayerRecord { PlayerKey = "ASIA:1", AccountId = "1", AccountName = "Tester", ShipId = "101" } }, null);
        await repository.CompleteFromReplayAsync(id, new ReplayParseResult
        {
            FileHash = "same-replay", ParserVersion = "legacy-parser", Status = ReplayParseStatus.Parsed,
            Source = BattleMetricSource.ReplayDerived, Result = BattleResult.Win, Damage = 100_000, Frags = 1,
            AdvancedMetrics = new BattleAdvancedMetrics
            {
                DamageTaken = damage, DamageTakenAvailability = MetricAvailability.Experimental, ParserSchemaVersion = parserSchema,
                PotentialDamage = 1_000_000, PotentialDamageAvailability = MetricAvailability.Stable,
                Survived = true, SurvivalAvailability = MetricAvailability.Stable
            },
            DamageBreakdowns = new[]
            {
                new BattleDamageBreakdown { Direction = DamageDirection.Dealt, RawTypeCode = 2, Damage = 100_000, Availability = MetricAvailability.Experimental },
                new BattleDamageBreakdown { Direction = DamageDirection.Received, RawTypeCode = 2, Damage = damage, Availability = MetricAvailability.Experimental }
            }
        }, "same.wowsreplay");
        await repository.SaveBattleReviewAsync(new BattleReview { BattleId = id, IsFavorite = true, Note = "saved note", Tags = new() { "saved" } });
        await repository.SaveReviewDraftAsync(new BattleReview { BattleId = id, IsFavorite = true, Note = "unsaved draft", Tags = new() { "draft" } });
        await ExecuteAsync("""
            DELETE FROM SchemaMigrations WHERE Version=7;
            ALTER TABLE BattleAdvancedMetrics DROP COLUMN SpottingDamage;
            ALTER TABLE BattleAdvancedMetrics DROP COLUMN SpottingDamageAvailability;
            """);
        return id;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using SqliteConnection connection = new($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, string? path = null)
    {
        await using SqliteConnection connection = new($"Data Source={path ?? DatabasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
