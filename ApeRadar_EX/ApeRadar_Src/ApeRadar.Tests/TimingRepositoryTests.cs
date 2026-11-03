using ApeRadar.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ApeRadar.Tests;

public sealed class TimingRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ApeRadar.Timing.{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(directory, "history.db");
    private static readonly DateTimeOffset Started = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("2", false, true)]
    [InlineData("3", false, true)]
    [InlineData("4", true, true)]
    [InlineData("5", false, true)]
    [InlineData("6", false, false)]
    public async Task LegacyEventTimes_AreInvalidatedWithBackupAndOtherDataPreserved(string schema, bool exact, bool survived)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id, Replay("one", schema, exact, Metrics(schema, survived, 900, 600)), "one.wowsreplay");
        await repository.SaveBattleReviewAsync(new BattleReview { BattleId = id, IsFavorite = true, Note = "saved" });
        await repository.SaveReviewDraftAsync(new BattleReview { BattleId = id, Note = "draft" });
        await DowngradeToSevenAsync();

        SqliteHistoryRepository migrated = new(DatabasePath);
        await migrated.InitializeAsync();
        BattleAdvancedMetrics result = (await migrated.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Null(result.BattleDurationSeconds);
        Assert.Null(result.SurvivalSeconds);
        Assert.Equal(ReplayTimingSource.Unknown, result.BattleDurationSource);
        Assert.Equal(ReplayTimingSource.Unknown, result.SurvivalTimeSource);
        Assert.Equal(survived ? null : (bool?)false, result.Survived);
        Assert.Equal(survived ? MetricAvailability.Unavailable : MetricAvailability.Stable, result.SurvivalAvailability);
        Assert.Equal(123_456, (await migrated.GetBattleAsync(id))!.Damage);
        Assert.Equal(50_000, result.DamageTaken);
        Assert.Equal(12_345, result.SpottingDamage);
        Assert.Equal(700_000, result.PotentialDamage);
        Assert.Equal("saved", (await migrated.GetBattleReviewAsync(id))!.Note);
        Assert.Equal("draft", (await migrated.GetReviewDraftAsync(id))!.Note);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles"));
        string backup = Assert.Single(Directory.GetFiles(directory, "history.db.pre-v8-*.bak"));
        Assert.Equal(900d, await ScalarAsync("SELECT BattleDurationSeconds FROM BattleAdvancedMetrics", backup));
        Assert.Equal(7L, await ScalarAsync("SELECT MAX(Version) FROM SchemaMigrations", backup));
        Assert.Equal(8L, await ScalarAsync("SELECT MAX(Version) FROM SchemaMigrations"));
        await new SqliteHistoryRepository(DatabasePath).InitializeAsync();
        Assert.Single(Directory.GetFiles(directory, "history.db.pre-v8-*.bak"));
    }

    [Theory]
    [InlineData("5", true, 900d)]
    [InlineData("6", false, 600d)]
    [InlineData("6", false, 0d)]
    public async Task ValidatedLegacySettlements_KeepTheirKnownTimesAndZero(string schema, bool survived, double survival)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id, Replay("one", schema, true, Metrics(schema, survived, 900, survival)), "one.wowsreplay");
        await DowngradeToSevenAsync();
        SqliteHistoryRepository migrated = new(DatabasePath);
        await migrated.InitializeAsync();
        BattleAdvancedMetrics result = (await migrated.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(900, result.BattleDurationSeconds);
        Assert.Equal(survival, result.SurvivalSeconds);
        Assert.Equal(survived, result.Survived);
        Assert.Equal(ReplayTimingSource.BattleResults, result.BattleDurationSource);
        Assert.Equal(ReplayTimingSource.BattleResults, result.SurvivalTimeSource);
        Assert.Equal(schema, result.ParserSchemaVersion);
    }

    [Fact]
    public async Task LegacySettlement_InvalidTimesCannotAcquireAValidSource()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id, Replay("one", "6", true, Metrics("6", false, 900, 902)), "one.wowsreplay");
        await DowngradeToSevenAsync();
        SqliteHistoryRepository migrated = new(DatabasePath);
        await migrated.InitializeAsync();
        BattleAdvancedMetrics result = (await migrated.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(900, result.BattleDurationSeconds);
        Assert.Null(result.SurvivalSeconds);
        Assert.False(result.Survived);
        Assert.Equal(ReplayTimingSource.BattleResults, result.BattleDurationSource);
        Assert.Equal(ReplayTimingSource.Unknown, result.SurvivalTimeSource);
    }

    [Fact]
    public async Task FailedTimingMigration_RollsBackBothColumnsAndInvalidation()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id, Replay("one", "4", false, Metrics("4", true, 900, 900)), "one.wowsreplay");
        await DowngradeToSevenAsync();
        await ExecuteAsync("CREATE TRIGGER RejectTiming BEFORE INSERT ON SchemaMigrations WHEN NEW.Version=8 BEGIN SELECT RAISE(ABORT,'timing failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => new SqliteHistoryRepository(DatabasePath).InitializeAsync());
        Assert.Equal(900d, await ScalarAsync("SELECT BattleDurationSeconds FROM BattleAdvancedMetrics"));
        Assert.Equal(1L, await ScalarAsync("SELECT Survived FROM BattleAdvancedMetrics"));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('BattleAdvancedMetrics') WHERE name='BattleDurationSource'"));
        Assert.Equal(7L, await ScalarAsync("SELECT MAX(Version) FROM SchemaMigrations"));
        Assert.Single(Directory.GetFiles(directory, "history.db.pre-v8-*.bak"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewEmptySnapshot_ClearsOldTimingInsteadOfSkippingTheWrite(bool exact)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id, Replay("one", "6", exact, Metrics("6", true, 900, 900)), "one.wowsreplay");
        await repository.CompleteFromReplayAsync(id, Replay("one", "7", exact, new BattleAdvancedMetrics { ParserSchemaVersion = "7" }), "one.wowsreplay");
        BattleAdvancedMetrics result = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Null(result.BattleDurationSeconds);
        Assert.Null(result.SurvivalSeconds);
        Assert.Null(result.Survived);
        Assert.Equal("7", result.ParserSchemaVersion);
        Assert.Equal(ReplayTimingSource.Unknown, result.BattleDurationSource);
        Assert.Equal(ReplayTimingSource.Unknown, result.SurvivalTimeSource);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles"));
    }

    [Fact]
    public async Task LaterPartialRecording_CannotEraseValidatedSettlementTiming()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        BattleAdvancedMetrics settlement = Metrics("7", false, 900, 600, ReplayTimingSource.BattleResults);
        await repository.CompleteFromReplayAsync(id, Replay("complete", "7", true, settlement), "complete.wowsreplay");
        await repository.CompleteFromReplayAsync(id, Replay("partial", "7", false, new BattleAdvancedMetrics { ParserSchemaVersion = "7" }), "partial.wowsreplay");
        BattleAdvancedMetrics result = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(900, result.BattleDurationSeconds);
        Assert.Equal(600, result.SurvivalSeconds);
        Assert.False(result.Survived);
        Assert.Equal(ReplayTimingSource.BattleResults, result.SurvivalTimeSource);
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateMerge_DoesNotRefillSchemaSevenUnknownTimesFromLegacy(bool newFirst)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long first = await CreateBattleAsync(repository);
        BattleAdvancedMetrics newer = new() { ParserSchemaVersion = "7" };
        BattleAdvancedMetrics older = Metrics("6", true, 900, 900);
        await repository.CompleteFromReplayAsync(first, Replay("one", newFirst ? "7" : "6", false, newFirst ? newer : older), "one.wowsreplay");
        long second = await InsertDuplicateAsync(first);
        await repository.CompleteFromReplayAsync(second, Replay("two", newFirst ? "6" : "7", false, newFirst ? older : newer), "two.wowsreplay");
        await ExecuteAsync("DELETE FROM SchemaMigrations WHERE Version=5");
        SqliteHistoryRepository reconciled = new(DatabasePath);
        await reconciled.InitializeAsync();
        BattleRecord battle = Assert.Single(await reconciled.GetBattlesAsync(new HistoryQuery()));
        BattleAdvancedMetrics result = (await reconciled.GetAdvancedMetricsAsync(new[] { battle.Id }))[battle.Id];
        Assert.Null(result.BattleDurationSeconds);
        Assert.Null(result.SurvivalSeconds);
        Assert.Null(result.Survived);
        Assert.Equal("7", result.ParserSchemaVersion);
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles"));
    }

    [Fact]
    public async Task DuplicateMerge_ChoosesOneTimingSnapshotWithoutMixingDeathAndSurvival()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long first = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(first, Replay("one", "7", false, Metrics("7", false, null, 600, ReplayTimingSource.BattleClock)), "one.wowsreplay");
        long second = await InsertDuplicateAsync(first);
        await repository.CompleteFromReplayAsync(second, Replay("two", "7", false, Metrics("7", true, 900, 900, ReplayTimingSource.BattleClock)), "two.wowsreplay");
        await ExecuteAsync("DELETE FROM SchemaMigrations WHERE Version=5");
        SqliteHistoryRepository reconciled = new(DatabasePath);
        await reconciled.InitializeAsync();
        BattleRecord battle = Assert.Single(await reconciled.GetBattlesAsync(new HistoryQuery()));
        BattleAdvancedMetrics result = (await reconciled.GetAdvancedMetricsAsync(new[] { battle.Id }))[battle.Id];
        Assert.True(result.Survived);
        Assert.Equal(900, result.BattleDurationSeconds);
        Assert.Equal(900, result.SurvivalSeconds);
        Assert.Equal(ReplayTimingSource.BattleClock, result.BattleDurationSource);
        Assert.Equal(ReplayTimingSource.BattleClock, result.SurvivalTimeSource);
    }

    private static Task<long> CreateBattleAsync(SqliteHistoryRepository repository) => repository.UpsertDraftAsync(new BattleRecord
    {
        BattleKey = "timing-battle", StartedAt = Started, Server = "ASIA", AccountId = "1", AccountName = "Tester",
        Mode = "pvp", ShipId = "101", ShipName = "Test ship", MapName = "Map", RosterSignature = "tester:101"
    }, Array.Empty<BattlePlayerRecord>(), null);

    private static BattleAdvancedMetrics Metrics(string schema, bool? survived, double? duration, double? survival,
        ReplayTimingSource source = ReplayTimingSource.Unknown) => new()
    {
        ParserSchemaVersion = schema, Survived = survived, BattleDurationSeconds = duration, SurvivalSeconds = survival,
        BattleDurationSource = duration.HasValue ? source : ReplayTimingSource.Unknown,
        SurvivalTimeSource = survival.HasValue ? source : ReplayTimingSource.Unknown,
        SurvivalAvailability = survived.HasValue ? MetricAvailability.Stable : MetricAvailability.Unavailable,
        DamageTaken = 50_000, DamageTakenAvailability = MetricAvailability.Experimental,
        SpottingDamage = 12_345, SpottingDamageAvailability = MetricAvailability.Stable,
        PotentialDamage = 700_000, PotentialDamageAvailability = MetricAvailability.Stable
    };

    private static ReplayParseResult Replay(string hash, string schema, bool exact, BattleAdvancedMetrics metrics) => new()
    {
        FileHash = hash, ParserVersion = "test/aperadar-" + schema, GameVersion = "15.9.0", Status = exact ? ReplayParseStatus.Parsed : ReplayParseStatus.Partial,
        StartedAt = Started, Server = "ASIA", AccountId = "1", AccountName = "Tester", ShipId = "101", MapName = "Map",
        RosterSignature = "tester:101", Source = exact ? BattleMetricSource.ReplayExact : BattleMetricSource.ReplayDerived,
        Damage = 123_456, Frags = 2, Result = BattleResult.Win, AdvancedMetrics = metrics
    };

    private async Task DowngradeToSevenAsync() => await ExecuteAsync("""
        DELETE FROM SchemaMigrations WHERE Version=8;
        ALTER TABLE BattleAdvancedMetrics DROP COLUMN BattleDurationSource;
        ALTER TABLE BattleAdvancedMetrics DROP COLUMN SurvivalTimeSource;
        """);

    private async Task<long> InsertDuplicateAsync(long sourceId)
    {
        await using SqliteConnection connection = new($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Battles(SessionId,BattleKey,StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,
                ShipType,RosterSignature,Result,BattleCount,Source,Completeness,UpdatedAt)
            SELECT SessionId,'duplicate',StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,
                ShipType,RosterSignature,4,1,4,2,UpdatedAt FROM Battles WHERE Id=$id RETURNING Id;
            """;
        command.Parameters.AddWithValue("$id", sourceId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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
