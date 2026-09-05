using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed class SqliteHistoryRepository : IHistoryRepository
    {
        private readonly SemaphoreSlim writeLock = new(1, 1);
        private bool initialized;

        public SqliteHistoryRepository(string? databasePath = null)
        {
            DatabasePath = databasePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApeRadar EX", "History", "history.db");
        }

        public string DatabasePath { get; }

        private string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (initialized) return;
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                if (initialized) return;
                Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
                try
                {
                    await CreateSchemaAsync(cancellationToken);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
                {
                    SqliteConnection.ClearAllPools();
                    if (File.Exists(DatabasePath))
                    {
                        string backup = $"{DatabasePath}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmss}";
                        File.Move(DatabasePath, backup, true);
                        TryMoveSidecar(DatabasePath + "-wal", backup + "-wal");
                        TryMoveSidecar(DatabasePath + "-shm", backup + "-shm");
                    }
                    await CreateSchemaAsync(cancellationToken);
                }
                initialized = true;
            }
            finally { writeLock.Release(); }
        }

        private async Task CreateSchemaAsync(CancellationToken cancellationToken)
        {
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);
            await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken);
            await ExecuteAsync(connection, null, "PRAGMA busy_timeout=5000;", cancellationToken);
            const string sql = """
                CREATE TABLE IF NOT EXISTS SchemaMigrations(
                    Version INTEGER PRIMARY KEY,
                    AppliedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Battles(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BattleKey TEXT NOT NULL UNIQUE,
                    StartedAt TEXT NOT NULL,
                    Server TEXT NOT NULL,
                    Mode TEXT NOT NULL,
                    MapName TEXT NOT NULL,
                    AccountId TEXT NOT NULL,
                    AccountName TEXT NOT NULL,
                    ShipId TEXT NOT NULL,
                    ShipName TEXT NOT NULL,
                    Result INTEGER NOT NULL,
                    WinCount REAL NULL,
                    Damage INTEGER NULL,
                    Frags REAL NULL,
                    BattleCount INTEGER NOT NULL DEFAULT 1,
                    Source INTEGER NOT NULL,
                    Completeness INTEGER NOT NULL,
                    ReplayHash TEXT NULL,
                    ReplayVersion TEXT NULL,
                    StatusMessage TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_Battles_Filter ON Battles(Server, AccountId, ShipId, StartedAt);
                CREATE TABLE IF NOT EXISTS BattlePlayers(
                    BattleId INTEGER NOT NULL,
                    PlayerKey TEXT NOT NULL,
                    AccountId TEXT NOT NULL,
                    AccountName TEXT NOT NULL,
                    Relation TEXT NOT NULL,
                    ShipId TEXT NOT NULL,
                    ShipName TEXT NOT NULL,
                    ShipType TEXT NOT NULL,
                    ShipTier INTEGER NOT NULL,
                    IsHidden INTEGER NOT NULL,
                    IsDataStale INTEGER NOT NULL,
                    AccountBattles REAL NULL,
                    AccountWinrate REAL NULL,
                    AccountPr REAL NULL,
                    ShipBattles REAL NULL,
                    ShipWinrate REAL NULL,
                    ShipPr REAL NULL,
                    PRIMARY KEY(BattleId, PlayerKey),
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS ReplayFiles(
                    FileHash TEXT PRIMARY KEY,
                    BattleId INTEGER NULL,
                    FilePath TEXT NOT NULL,
                    GameVersion TEXT NOT NULL,
                    ParserVersion TEXT NOT NULL,
                    ParseStatus INTEGER NOT NULL,
                    ErrorCode TEXT NOT NULL,
                    ErrorMessage TEXT NOT NULL,
                    AttemptedAt TEXT NOT NULL,
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE SET NULL
                );
                CREATE TABLE IF NOT EXISTS ShipSnapshots(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BattleId INTEGER NOT NULL,
                    CapturedAt TEXT NOT NULL,
                    Provider TEXT NOT NULL,
                    AccountId TEXT NOT NULL,
                    ShipId TEXT NOT NULL,
                    Battles REAL NOT NULL,
                    Wins REAL NOT NULL,
                    Losses REAL NULL,
                    Damage REAL NOT NULL,
                    Frags REAL NOT NULL,
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_ShipSnapshots_Battle ON ShipSnapshots(BattleId, CapturedAt);
                CREATE TABLE IF NOT EXISTS PendingResultChecks(
                    BattleId INTEGER PRIMARY KEY,
                    Attempt INTEGER NOT NULL,
                    NextAttemptAt TEXT NOT NULL,
                    LastError TEXT NOT NULL,
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE CASCADE
                );
                INSERT OR IGNORE INTO SchemaMigrations(Version, AppliedAt) VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """;
            await ExecuteAsync(connection, null, sql, cancellationToken);
        }

        public async Task<long> UpsertDraftAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                const string upsert = """
                    INSERT INTO Battles(BattleKey,StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,Result,WinCount,Damage,Frags,BattleCount,Source,Completeness,ReplayHash,ReplayVersion,StatusMessage,UpdatedAt)
                    VALUES($key,$started,$server,$mode,$map,$accountId,$accountName,$shipId,$shipName,$result,$wins,$damage,$frags,$count,$source,$complete,$hash,$version,$status,$updated)
                    ON CONFLICT(BattleKey) DO UPDATE SET
                        Server=excluded.Server, Mode=excluded.Mode, MapName=excluded.MapName,
                        AccountId=excluded.AccountId, AccountName=excluded.AccountName,
                        ShipId=excluded.ShipId, ShipName=excluded.ShipName, UpdatedAt=excluded.UpdatedAt
                    RETURNING Id;
                    """;
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = upsert;
                AddBattleParameters(command, battle);
                long battleId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

                await ExecuteAsync(connection, transaction, "DELETE FROM BattlePlayers WHERE BattleId=$id", cancellationToken, ("$id", battleId));
                foreach (BattlePlayerRecord player in players)
                {
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO BattlePlayers(BattleId,PlayerKey,AccountId,AccountName,Relation,ShipId,ShipName,ShipType,ShipTier,IsHidden,IsDataStale,AccountBattles,AccountWinrate,AccountPr,ShipBattles,ShipWinrate,ShipPr)
                        VALUES($battle,$key,$id,$name,$relation,$shipId,$shipName,$type,$tier,$hidden,$stale,$ab,$awr,$apr,$sb,$swr,$spr)
                        """, cancellationToken,
                        ("$battle", battleId), ("$key", player.PlayerKey), ("$id", player.AccountId), ("$name", player.AccountName),
                        ("$relation", player.Relation), ("$shipId", player.ShipId), ("$shipName", player.ShipName), ("$type", player.ShipType),
                        ("$tier", player.ShipTier), ("$hidden", player.IsHidden ? 1 : 0), ("$stale", player.IsDataStale ? 1 : 0),
                        ("$ab", player.AccountBattles), ("$awr", player.AccountWinrate), ("$apr", player.AccountPr),
                        ("$sb", player.ShipBattles), ("$swr", player.ShipWinrate), ("$spr", player.ShipPr));
                }
                if (snapshot != null)
                {
                    snapshot.BattleId = battleId;
                    await InsertSnapshotAsync(connection, transaction, snapshot, cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                return battleId;
            }
            finally { writeLock.Release(); }
        }

        public async Task<BattleRecord?> FindDraftForReplayAsync(ReplayParseResult replay, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT * FROM Battles
                WHERE Completeness IN ($pending,$unsupported)
                  AND ($account='' OR lower(AccountName)=lower($account))
                  AND ($ship='' OR ShipId=$ship)
                  AND ($started='' OR abs(strftime('%s',StartedAt)-strftime('%s',$started)) <= 900)
                ORDER BY abs(strftime('%s',StartedAt)-strftime('%s',$started)) LIMIT 1
                """;
            command.Parameters.AddWithValue("$pending", (int)BattleCompleteness.Pending);
            command.Parameters.AddWithValue("$unsupported", (int)BattleCompleteness.Unsupported);
            command.Parameters.AddWithValue("$account", replay.AccountName);
            command.Parameters.AddWithValue("$ship", replay.ShipId);
            command.Parameters.AddWithValue("$started", replay.StartedAt?.UtcDateTime.ToString("O") ?? "");
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadBattle(reader) : null;
        }

        public async Task CompleteFromReplayAsync(long battleId, ReplayParseResult replay, string replayPath, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                BattleCompleteness completeness = replay.Status switch
                {
                    ReplayParseStatus.Parsed when replay.HasCompleteMetrics => BattleCompleteness.Complete,
                    ReplayParseStatus.Unsupported => BattleCompleteness.Unsupported,
                    ReplayParseStatus.Invalid => BattleCompleteness.Failed,
                    _ => BattleCompleteness.Partial
                };
                await ExecuteAsync(connection, transaction, """
                    UPDATE Battles SET Result=$result,WinCount=$wins,Damage=$damage,Frags=$frags,Source=$source,Completeness=$complete,
                        ReplayHash=$hash,ReplayVersion=$version,StatusMessage=$message,UpdatedAt=$updated WHERE Id=$id
                    """, cancellationToken,
                    ("$result", (int)replay.Result), ("$wins", replay.Result == BattleResult.Win ? 1 : replay.Result is BattleResult.Loss or BattleResult.Draw or BattleResult.UnknownNonWin ? 0 : null),
                    ("$damage", replay.Damage), ("$frags", replay.Frags),
                    ("$source", (int)replay.Source), ("$complete", (int)completeness), ("$hash", replay.FileHash),
                    ("$version", replay.GameVersion), ("$message", replay.ErrorMessage), ("$updated", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")), ("$id", battleId));
                await UpsertReplayAsync(connection, transaction, battleId, replay, replayPath, cancellationToken);
                if (replay.HasCompleteMetrics)
                    await ExecuteAsync(connection, transaction, "DELETE FROM PendingResultChecks WHERE BattleId=$id", cancellationToken, ("$id", battleId));
                await transaction.CommitAsync(cancellationToken);
            }
            finally { writeLock.Release(); }
        }

        public async Task RecordReplayFailureAsync(ReplayParseResult replay, string replayPath, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await UpsertReplayAsync(connection, null, null, replay, replayPath, cancellationToken);
            }
            finally { writeLock.Release(); }
        }

        public async Task<bool> HasReplayAsync(string replayHash, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM ReplayFiles WHERE FileHash=$hash AND ParseStatus IN ($parsed,$partial) LIMIT 1";
            command.Parameters.AddWithValue("$hash", replayHash);
            command.Parameters.AddWithValue("$parsed", (int)ReplayParseStatus.Parsed);
            command.Parameters.AddWithValue("$partial", (int)ReplayParseStatus.Partial);
            return await command.ExecuteScalarAsync(cancellationToken) != null;
        }

        public async Task<BattleRecord?> GetBattleAsync(long battleId, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Battles WHERE Id=$id";
            command.Parameters.AddWithValue("$id", battleId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadBattle(reader) : null;
        }

        public async Task<IReadOnlyList<BattleRecord>> GetBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            List<BattleRecord> result = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT * FROM Battles WHERE
                    ($server='' OR Server=$server) AND ($account='' OR AccountId=$account) AND ($ship='' OR ShipId=$ship)
                    AND ($from='' OR StartedAt >= $from) AND ($to='' OR StartedAt < $to)
                ORDER BY StartedAt
                """;
            command.Parameters.AddWithValue("$server", query.Server ?? "");
            command.Parameters.AddWithValue("$account", query.AccountId ?? "");
            command.Parameters.AddWithValue("$ship", query.ShipId ?? "");
            command.Parameters.AddWithValue("$from", query.From?.UtcDateTime.ToString("O") ?? "");
            command.Parameters.AddWithValue("$to", query.To?.UtcDateTime.ToString("O") ?? "");
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(ReadBattle(reader));
            return result;
        }

        public Task<IReadOnlyList<HistoryFilterOption>> GetServersAsync(CancellationToken cancellationToken = default) =>
            GetOptionsAsync("SELECT DISTINCT Server,Server FROM Battles ORDER BY Server", Array.Empty<(string, object?)>(), cancellationToken);

        public Task<IReadOnlyList<HistoryFilterOption>> GetAccountsAsync(string? server, CancellationToken cancellationToken = default) =>
            GetOptionsAsync("SELECT DISTINCT AccountId,AccountName FROM Battles WHERE ($server='' OR Server=$server) ORDER BY AccountName", new[] { ("$server", (object?)(server ?? "")) }, cancellationToken);

        public Task<IReadOnlyList<HistoryFilterOption>> GetShipsAsync(string? server, string? accountId, CancellationToken cancellationToken = default) =>
            GetOptionsAsync("SELECT DISTINCT ShipId,ShipName FROM Battles WHERE ($server='' OR Server=$server) AND ($account='' OR AccountId=$account) ORDER BY ShipName", new[] { ("$server", (object?)(server ?? "")), ("$account", (object?)(accountId ?? "")) }, cancellationToken);

        public async Task<ShipStatSnapshot?> GetPreBattleSnapshotAsync(long battleId, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM ShipSnapshots WHERE BattleId=$id ORDER BY CapturedAt LIMIT 1";
            command.Parameters.AddWithValue("$id", battleId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadSnapshot(reader) : null;
        }

        public async Task AddOrUpdatePendingCheckAsync(PendingResultCheck check, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await WriteAsync("""
                INSERT INTO PendingResultChecks(BattleId,Attempt,NextAttemptAt,LastError) VALUES($id,$attempt,$next,$error)
                ON CONFLICT(BattleId) DO UPDATE SET Attempt=excluded.Attempt,NextAttemptAt=excluded.NextAttemptAt,LastError=excluded.LastError
                """, cancellationToken, ("$id", check.BattleId), ("$attempt", check.Attempt), ("$next", check.NextAttemptAt.UtcDateTime.ToString("O")), ("$error", check.LastError));
        }

        public async Task<IReadOnlyList<PendingResultCheck>> GetDuePendingChecksAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            List<PendingResultCheck> checks = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT BattleId,Attempt,NextAttemptAt,LastError FROM PendingResultChecks WHERE NextAttemptAt <= $now ORDER BY NextAttemptAt";
            command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                checks.Add(new PendingResultCheck
                {
                    BattleId = reader.GetInt64(0), Attempt = reader.GetInt32(1),
                    NextAttemptAt = ParseDate(reader.GetString(2)), LastError = reader.GetString(3)
                });
            }
            return checks;
        }

        public async Task ResolveFromApiAsync(long battleId, ShipStatSnapshot before, ShipStatSnapshot after, CancellationToken cancellationToken = default)
        {
            double battleDelta = after.Battles - before.Battles;
            if (battleDelta <= 0) return;
            double winDelta = after.Wins - before.Wins;
            double? lossDelta = before.Losses.HasValue && after.Losses.HasValue ? after.Losses.Value - before.Losses.Value : null;
            BattleResult battleResult = battleDelta == 1
                ? winDelta >= 1 ? BattleResult.Win : lossDelta >= 1 ? BattleResult.Loss : BattleResult.UnknownNonWin
                : BattleResult.Unknown;
            BattleMetricSource source = battleDelta == 1 ? BattleMetricSource.ApiExact : BattleMetricSource.ApiMerged;
            await WriteAsync("""
                UPDATE Battles SET Result=$result,WinCount=$wins,Damage=$damage,Frags=$frags,BattleCount=$count,Source=$source,
                    Completeness=$complete,StatusMessage=$message,UpdatedAt=$updated WHERE Id=$id;
                DELETE FROM PendingResultChecks WHERE BattleId=$id;
                """, cancellationToken,
                ("$result", (int)battleResult), ("$wins", Math.Max(0, winDelta)), ("$damage", Math.Max(0, after.Damage - before.Damage)),
                ("$frags", Math.Max(0, after.Frags - before.Frags)), ("$count", Convert.ToInt32(battleDelta)),
                ("$source", (int)source), ("$complete", (int)(battleDelta == 1 ? BattleCompleteness.Complete : BattleCompleteness.Partial)),
                ("$message", battleDelta == 1 ? "" : $"API merged {battleDelta:0} battles"),
                ("$updated", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")), ("$id", battleId));
        }

        public async Task MarkPendingAttemptAsync(PendingResultCheck check, bool exhausted, CancellationToken cancellationToken = default)
        {
            if (exhausted)
            {
                await WriteAsync("""
                    DELETE FROM PendingResultChecks WHERE BattleId=$id;
                    UPDATE Battles SET Completeness=$complete,Source=$source,StatusMessage=$error,UpdatedAt=$updated WHERE Id=$id;
                    """, cancellationToken, ("$id", check.BattleId), ("$complete", (int)BattleCompleteness.Partial),
                    ("$source", (int)BattleMetricSource.MetadataOnly), ("$error", check.LastError), ("$updated", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")));
            }
            else await AddOrUpdatePendingCheckAsync(check, cancellationToken);
        }

        public Task MakePendingChecksDueAsync(CancellationToken cancellationToken = default) =>
            WriteAsync("UPDATE PendingResultChecks SET NextAttemptAt=$now", cancellationToken, ("$now", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")));

        public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            await WriteAsync("DELETE FROM Battles; DELETE FROM ReplayFiles; DELETE FROM PendingResultChecks;", cancellationToken);
        }

        private async Task<IReadOnlyList<HistoryFilterOption>> GetOptionsAsync(string sql, (string, object?)[] parameters, CancellationToken cancellationToken)
        {
            await EnsureInitialized(cancellationToken);
            List<HistoryFilterOption> result = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(new HistoryFilterOption { Value = reader.GetString(0), Display = reader.GetString(1) });
            return result;
        }

        private async Task WriteAsync(string sql, CancellationToken cancellationToken, params (string, object?)[] parameters)
        {
            await EnsureInitialized(cancellationToken);
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await ExecuteAsync(connection, null, sql, cancellationToken, parameters);
            }
            finally { writeLock.Release(); }
        }

        private async Task InsertSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, ShipStatSnapshot snapshot, CancellationToken cancellationToken)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO ShipSnapshots(BattleId,CapturedAt,Provider,AccountId,ShipId,Battles,Wins,Losses,Damage,Frags)
                SELECT $battle,$captured,$provider,$account,$ship,$battles,$wins,$losses,$damage,$frags
                WHERE NOT EXISTS(SELECT 1 FROM ShipSnapshots WHERE BattleId=$battle)
                """, cancellationToken,
                ("$battle", snapshot.BattleId), ("$captured", snapshot.CapturedAt.UtcDateTime.ToString("O")), ("$provider", snapshot.Provider),
                ("$account", snapshot.AccountId), ("$ship", snapshot.ShipId), ("$battles", snapshot.Battles), ("$wins", snapshot.Wins),
                ("$losses", snapshot.Losses), ("$damage", snapshot.Damage), ("$frags", snapshot.Frags));
        }

        private static async Task UpsertReplayAsync(SqliteConnection connection, SqliteTransaction? transaction, long? battleId, ReplayParseResult replay, string replayPath, CancellationToken cancellationToken)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO ReplayFiles(FileHash,BattleId,FilePath,GameVersion,ParserVersion,ParseStatus,ErrorCode,ErrorMessage,AttemptedAt)
                VALUES($hash,$battle,$path,$game,$parser,$status,$code,$message,$attempted)
                ON CONFLICT(FileHash) DO UPDATE SET BattleId=COALESCE(excluded.BattleId,ReplayFiles.BattleId),FilePath=excluded.FilePath,
                    GameVersion=excluded.GameVersion,ParserVersion=excluded.ParserVersion,ParseStatus=excluded.ParseStatus,
                    ErrorCode=excluded.ErrorCode,ErrorMessage=excluded.ErrorMessage,AttemptedAt=excluded.AttemptedAt
                """, cancellationToken,
                ("$hash", replay.FileHash), ("$battle", battleId), ("$path", replayPath), ("$game", replay.GameVersion),
                ("$parser", replay.ParserVersion), ("$status", (int)replay.Status), ("$code", replay.ErrorCode),
                ("$message", replay.ErrorMessage), ("$attempted", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")));
        }

        private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string, object?)[] parameters)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void AddBattleParameters(SqliteCommand command, BattleRecord battle)
        {
            command.Parameters.AddWithValue("$key", battle.BattleKey);
            command.Parameters.AddWithValue("$started", battle.StartedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$server", battle.Server);
            command.Parameters.AddWithValue("$mode", battle.Mode);
            command.Parameters.AddWithValue("$map", battle.MapName);
            command.Parameters.AddWithValue("$accountId", battle.AccountId);
            command.Parameters.AddWithValue("$accountName", battle.AccountName);
            command.Parameters.AddWithValue("$shipId", battle.ShipId);
            command.Parameters.AddWithValue("$shipName", battle.ShipName);
            command.Parameters.AddWithValue("$result", (int)battle.Result);
            command.Parameters.AddWithValue("$wins", battle.WinCount ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$damage", battle.Damage ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$frags", battle.Frags ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$count", battle.BattleCount);
            command.Parameters.AddWithValue("$source", (int)battle.Source);
            command.Parameters.AddWithValue("$complete", (int)battle.Completeness);
            command.Parameters.AddWithValue("$hash", battle.ReplayHash ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$version", battle.ReplayVersion ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$status", battle.StatusMessage ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$updated", battle.UpdatedAt.UtcDateTime.ToString("O"));
        }

        private static BattleRecord ReadBattle(SqliteDataReader reader) => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            BattleKey = reader.GetString(reader.GetOrdinal("BattleKey")),
            StartedAt = ParseDate(reader.GetString(reader.GetOrdinal("StartedAt"))),
            Server = reader.GetString(reader.GetOrdinal("Server")), Mode = reader.GetString(reader.GetOrdinal("Mode")),
            MapName = reader.GetString(reader.GetOrdinal("MapName")), AccountId = reader.GetString(reader.GetOrdinal("AccountId")),
            AccountName = reader.GetString(reader.GetOrdinal("AccountName")), ShipId = reader.GetString(reader.GetOrdinal("ShipId")),
            ShipName = reader.GetString(reader.GetOrdinal("ShipName")), Result = (BattleResult)reader.GetInt32(reader.GetOrdinal("Result")),
            WinCount = reader.IsDBNull(reader.GetOrdinal("WinCount")) ? null : reader.GetDouble(reader.GetOrdinal("WinCount")),
            Damage = reader.IsDBNull(reader.GetOrdinal("Damage")) ? null : reader.GetInt64(reader.GetOrdinal("Damage")),
            Frags = reader.IsDBNull(reader.GetOrdinal("Frags")) ? null : reader.GetDouble(reader.GetOrdinal("Frags")),
            BattleCount = reader.GetInt32(reader.GetOrdinal("BattleCount")), Source = (BattleMetricSource)reader.GetInt32(reader.GetOrdinal("Source")),
            Completeness = (BattleCompleteness)reader.GetInt32(reader.GetOrdinal("Completeness")),
            ReplayHash = reader.IsDBNull(reader.GetOrdinal("ReplayHash")) ? null : reader.GetString(reader.GetOrdinal("ReplayHash")),
            ReplayVersion = reader.IsDBNull(reader.GetOrdinal("ReplayVersion")) ? null : reader.GetString(reader.GetOrdinal("ReplayVersion")),
            StatusMessage = reader.IsDBNull(reader.GetOrdinal("StatusMessage")) ? null : reader.GetString(reader.GetOrdinal("StatusMessage")),
            UpdatedAt = ParseDate(reader.GetString(reader.GetOrdinal("UpdatedAt")))
        };

        private static ShipStatSnapshot ReadSnapshot(SqliteDataReader reader) => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")), BattleId = reader.GetInt64(reader.GetOrdinal("BattleId")),
            CapturedAt = ParseDate(reader.GetString(reader.GetOrdinal("CapturedAt"))), Provider = reader.GetString(reader.GetOrdinal("Provider")),
            AccountId = reader.GetString(reader.GetOrdinal("AccountId")), ShipId = reader.GetString(reader.GetOrdinal("ShipId")),
            Battles = reader.GetDouble(reader.GetOrdinal("Battles")), Wins = reader.GetDouble(reader.GetOrdinal("Wins")),
            Losses = reader.IsDBNull(reader.GetOrdinal("Losses")) ? null : reader.GetDouble(reader.GetOrdinal("Losses")),
            Damage = reader.GetDouble(reader.GetOrdinal("Damage")), Frags = reader.GetDouble(reader.GetOrdinal("Frags"))
        };

        private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        private static void TryMoveSidecar(string source, string destination)
        {
            if (File.Exists(source)) File.Move(source, destination, true);
        }
        private Task EnsureInitialized(CancellationToken cancellationToken) => initialized ? Task.CompletedTask : InitializeAsync(cancellationToken);
    }
}
