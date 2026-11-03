using ApeRadar.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ApeRadar.Tests;

public sealed class BattleResultsRepositoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ApeRadar.BattleResults.{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(directory, "history.db");
    private static readonly DateTimeOffset Started = new(2026, 10, 7, 13, 6, 36, TimeSpan.Zero);

    [Theory]
    [InlineData((int)ReplayParseStatus.Parsed, false)]
    [InlineData((int)ReplayParseStatus.Partial, false)]
    [InlineData((int)ReplayParseStatus.Unsupported, false)]
    [InlineData((int)ReplayParseStatus.Invalid, false)]
    [InlineData((int)ReplayParseStatus.Partial, true)]
    public async Task LaterWeakerRecording_CannotEraseExactResultsOrTheirDetails(int status, bool exact)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id, Replay("settlement"), "complete.wowsreplay");
        await SaveReviewsAsync(repository, id);
        await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck { BattleId = id, NextAttemptAt = Started });

        ReplayParseResult later = Replay("later-segment", exact, (ReplayParseStatus)status, damage: 3_000, taken: null, spotting: 2_000);
        await repository.CompleteFromReplayAsync(id, later, "partial.wowsreplay");
        // ReplayMonitor and a previously started API callback can enqueue after
        // CompleteFromReplayAsync has preserved the exact settlement.
        await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck
        {
            BattleId = id, Attempt = 2, NextAttemptAt = Started, LastError = "late partial"
        });

        BattleRecord stored = Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(BattleMetricSource.ReplayExact, stored.Source);
        Assert.Equal(BattleCompleteness.Complete, stored.Completeness);
        Assert.Equal(120_222, stored.Damage);
        Assert.Equal("settlement", stored.ReplayHash);
        BattleAdvancedMetrics advanced = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(25_097, advanced.DamageTaken);
        Assert.Equal(16_235, advanced.SpottingDamage);
        Assert.Equal(MetricAvailability.Experimental, advanced.DamageTakenAvailability);
        Assert.Equal(120_222d / 25_097d, SessionAnalysisService.MetricValue(stored, advanced, "TradeRatio", true));
        IReadOnlyList<BattleDamageBreakdown> breakdowns = await repository.GetDamageBreakdownsAsync(id);
        Assert.Equal(25_097, Assert.Single(breakdowns, x => x.Direction == DamageDirection.Received).Damage);
        Assert.Equal(120_222, Assert.Single(breakdowns, x => x.Direction == DamageDirection.Dealt).Damage);
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles WHERE BattleId=" + id));
        Assert.Equal(id, (await repository.FindDraftForReplayAsync(new ReplayParseResult { FileHash = "later-segment" }))!.Id);
        Assert.Empty(await repository.GetDuePendingChecksAsync(DateTimeOffset.UtcNow));
        await AssertReviewsPreservedAsync(repository, id);
    }

    [Fact]
    public async Task PartialReplay_RemainsEligibleForPendingResultChecks()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id,
            Replay("partial", exact: false, status: ReplayParseStatus.Partial), "partial.wowsreplay");
        await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck { BattleId = id, NextAttemptAt = Started });
        await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck
        {
            BattleId = id, Attempt = 1, NextAttemptAt = Started.AddMinutes(1), LastError = "retry"
        });
        PendingResultCheck pending = Assert.Single(await repository.GetDuePendingChecksAsync(DateTimeOffset.UtcNow));
        Assert.Equal(id, pending.BattleId);
        Assert.Equal(1, pending.Attempt);
        Assert.Equal("retry", pending.LastError);
    }

    [Fact]
    public async Task ApiUnknownNonWin_RemainsEligibleForResultRefinement()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(101));
        await ExecuteAsync($"UPDATE Battles SET Result={(int)BattleResult.UnknownNonWin} WHERE Id={id}");
        Assert.Equal(BattleMetricSource.ApiExact, (await repository.GetBattleAsync(id))!.Source);
        await repository.AddOrUpdatePendingCheckAsync(new PendingResultCheck { BattleId = id, NextAttemptAt = Started });
        Assert.Equal(id, Assert.Single(await repository.GetDuePendingChecksAsync(DateTimeOffset.UtcNow)).BattleId);
    }

    [Fact]
    public async Task SettlementReplacesDerivedValues_AndDelayedApiCannotOverwriteIt()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id,
            Replay("partial", exact: false, damage: 116_996, taken: null, spotting: 12_000), "partial.wowsreplay");
        await SaveReviewsAsync(repository, id);
        await repository.CompleteFromReplayAsync(id, Replay("settlement"), "complete.wowsreplay");
        await repository.ResolveFromApiAsync(id, Snapshot(100), Snapshot(101));

        BattleRecord stored = (await repository.GetBattleAsync(id))!;
        Assert.Equal(BattleMetricSource.ReplayExact, stored.Source);
        Assert.Equal(120_222, stored.Damage);
        BattleAdvancedMetrics advanced = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(25_097, advanced.DamageTaken);
        Assert.Equal(16_235, advanced.SpottingDamage);
        Assert.Equal("5", advanced.ParserSchemaVersion);
        Assert.Single(await repository.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles"));
        await AssertReviewsPreservedAsync(repository, id);
    }

    [Fact]
    public async Task ExactReparse_PreservesZeroAndDoesNotInventUnavailableDamageTaken()
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long id = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(id, Replay("settlement"), "complete.wowsreplay");
        await SaveReviewsAsync(repository, id);

        ReplayParseResult corrected = Replay("settlement", damage: 0, taken: 0, spotting: 0);
        await repository.CompleteFromReplayAsync(id, corrected, "complete.wowsreplay");
        await repository.CompleteFromReplayAsync(id, corrected, "complete.wowsreplay");
        BattleRecord stored = (await repository.GetBattleAsync(id))!;
        BattleAdvancedMetrics advanced = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Equal(0, stored.Damage);
        Assert.Equal(0, advanced.DamageTaken);
        Assert.Equal(0, advanced.SpottingDamage);
        Assert.Null(SessionAnalysisService.MetricValue(stored, advanced, "TradeRatio", true));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles"));

        // A newly validated result can explicitly invalidate an untrusted metric.
        await repository.CompleteFromReplayAsync(id, Replay("settlement", taken: null), "complete.wowsreplay");
        advanced = (await repository.GetAdvancedMetricsAsync(new[] { id }))[id];
        Assert.Null(advanced.DamageTaken);
        Assert.Equal(MetricAvailability.Unavailable, advanced.DamageTakenAvailability);
        Assert.DoesNotContain(await repository.GetDamageBreakdownsAsync(id), x => x.Direction == DamageDirection.Received);
        await AssertReviewsPreservedAsync(repository, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyDuplicateMerge_UsesExactSideForTotalsAndDetails(bool exactFirst)
    {
        SqliteHistoryRepository repository = new(DatabasePath);
        long original = await CreateBattleAsync(repository);
        await repository.CompleteFromReplayAsync(original,
            Replay("first", exact: exactFirst, damage: exactFirst ? 120_222 : 116_996,
                taken: exactFirst ? 25_097 : null, spotting: exactFirst ? 16_235 : 12_000), "first.wowsreplay");
        long duplicate = await InsertDuplicateAsync(original);
        await repository.CompleteFromReplayAsync(duplicate,
            Replay("second", exact: !exactFirst, damage: exactFirst ? 116_996 : 120_222,
                taken: exactFirst ? null : 25_097, spotting: exactFirst ? 12_000 : 16_235), "second.wowsreplay");
        await SaveReviewsAsync(repository, original);
        await repository.SaveBattleReviewAsync(new BattleReview { BattleId = duplicate, Note = "second note", Tags = new() { "second" } });
        await ExecuteAsync("DELETE FROM SchemaMigrations WHERE Version=5");

        SqliteHistoryRepository migrated = new(DatabasePath);
        await migrated.InitializeAsync();
        BattleRecord stored = Assert.Single(await migrated.GetBattlesAsync(new HistoryQuery()));
        Assert.Equal(original, stored.Id);
        Assert.Equal(BattleMetricSource.ReplayExact, stored.Source);
        Assert.Equal(120_222, stored.Damage);
        Assert.Equal(exactFirst ? "first" : "second", stored.ReplayHash);
        BattleAdvancedMetrics advanced = (await migrated.GetAdvancedMetricsAsync(new[] { original }))[original];
        Assert.Equal(25_097, advanced.DamageTaken);
        Assert.Equal(16_235, advanced.SpottingDamage);
        Assert.Equal(120_222, Assert.Single(await migrated.GetDamageBreakdownsAsync(original), x => x.Direction == DamageDirection.Dealt).Damage);
        Assert.Equal(25_097, Assert.Single(await migrated.GetDamageBreakdownsAsync(original), x => x.Direction == DamageDirection.Received).Damage);
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT(*) FROM ReplayFiles WHERE BattleId=" + original));
        Assert.Contains("saved note", (await migrated.GetBattleReviewAsync(original))!.Note);
        Assert.Contains("second note", (await migrated.GetBattleReviewAsync(original))!.Note);
        Assert.Equal("draft note", (await migrated.GetReviewDraftAsync(original))!.Note);
        Assert.Null(await ScalarAsync("PRAGMA foreign_key_check"));
    }

    private static Task<long> CreateBattleAsync(SqliteHistoryRepository repository) => repository.UpsertDraftAsync(new BattleRecord
    {
        BattleKey = "battle", StartedAt = Started, Server = "ASIA", AccountId = "1", AccountName = "Tester",
        Mode = "pvp", ShipId = "101", ShipName = "Test ship", MapName = "Map"
    }, new[] { new BattlePlayerRecord { PlayerKey = "ASIA:1", AccountId = "1", AccountName = "Tester", ShipId = "101" } }, null);

    private static ReplayParseResult Replay(string hash, bool exact = true, ReplayParseStatus status = ReplayParseStatus.Parsed,
        long damage = 120_222, long? taken = 25_097, long spotting = 16_235) => new()
    {
        FileHash = hash, ParserVersion = "test/aperadar-5", GameVersion = "15.8.0", Status = status,
        StartedAt = Started, Server = "ASIA", AccountId = "1", AccountName = "Tester", ShipId = "101", MapName = "Map",
        RosterSignature = "tester:101", Source = exact ? BattleMetricSource.ReplayExact : BattleMetricSource.ReplayDerived,
        Damage = damage, Frags = 1, Result = BattleResult.Win,
        AdvancedMetrics = new BattleAdvancedMetrics
        {
            DamageTaken = taken, DamageTakenAvailability = taken.HasValue ? MetricAvailability.Experimental : MetricAvailability.Unavailable,
            SpottingDamage = spotting, SpottingDamageAvailability = MetricAvailability.Stable,
            PotentialDamage = 1_438_164, PotentialDamageAvailability = MetricAvailability.Stable, ParserSchemaVersion = "5"
        },
        DamageBreakdowns = taken.HasValue ? new[] { Breakdown(DamageDirection.Dealt, damage), Breakdown(DamageDirection.Received, taken.Value) }
            : new[] { Breakdown(DamageDirection.Dealt, damage) }
    };

    private static BattleDamageBreakdown Breakdown(DamageDirection direction, long damage) => new()
    {
        Direction = direction, RawTypeCode = 2, Category = DamageCategory.Unknown, Damage = damage, Availability = MetricAvailability.Experimental
    };

    private static ShipStatSnapshot Snapshot(int count) => new()
    {
        AccountId = "1", ShipId = "101", CapturedAt = Started.AddMinutes(count - 100), Provider = "test",
        Battles = count, Wins = 50 + count - 100, Losses = 50, Damage = 6_000_000 + (count - 100) * 120_000, Frags = count
    };

    private static async Task SaveReviewsAsync(SqliteHistoryRepository repository, long id)
    {
        await repository.SaveBattleReviewAsync(new BattleReview { BattleId = id, IsFavorite = true, Note = "saved note", Tags = new() { "saved" } });
        await repository.SaveReviewDraftAsync(new BattleReview { BattleId = id, IsFavorite = true, Note = "draft note", Tags = new() { "draft" } });
    }

    private static async Task AssertReviewsPreservedAsync(SqliteHistoryRepository repository, long id)
    {
        Assert.True((await repository.GetBattleReviewAsync(id))!.IsFavorite);
        Assert.Equal("saved note", (await repository.GetBattleReviewAsync(id))!.Note);
        Assert.Equal("draft note", (await repository.GetReviewDraftAsync(id))!.Note);
        Assert.Single(await repository.GetBattlePlayersAsync(id));
    }

    private async Task<long> InsertDuplicateAsync(long sourceId)
    {
        await using SqliteConnection connection = new($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Battles(SessionId,BattleKey,StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,
                ShipType,RosterSignature,Result,BattleCount,Source,Completeness,UpdatedAt)
            SELECT SessionId,'duplicate',StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,
                ShipType,RosterSignature,$unknown,1,$metadata,$pending,UpdatedAt FROM Battles WHERE Id=$id RETURNING Id;
            """;
        command.Parameters.AddWithValue("$id", sourceId);
        command.Parameters.AddWithValue("$unknown", (int)BattleResult.Unknown);
        command.Parameters.AddWithValue("$metadata", (int)BattleMetricSource.MetadataOnly);
        command.Parameters.AddWithValue("$pending", (int)BattleCompleteness.Pending);
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

    private async Task<object?> ScalarAsync(string sql)
    {
        await using SqliteConnection connection = new($"Data Source={DatabasePath};Mode=ReadOnly;Pooling=False");
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
