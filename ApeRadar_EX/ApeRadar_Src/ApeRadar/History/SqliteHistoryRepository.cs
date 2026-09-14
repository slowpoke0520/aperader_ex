using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApeRadar.Utils;

namespace ApeRadar.History
{
    internal sealed class SqliteHistoryRepository : IHistoryRepository
    {
        private const int CurrentSchemaVersion = 4;
        private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(45);
        private const int ReconnectMergeWindowSeconds = 20 * 60;
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
                    await BackupBeforeMigrationAsync(cancellationToken);
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
                    SessionId INTEGER NULL,
                    BattleKey TEXT NOT NULL UNIQUE,
                    StartedAt TEXT NOT NULL,
                    Server TEXT NOT NULL,
                    Mode TEXT NOT NULL,
                    MapName TEXT NOT NULL,
                    AccountId TEXT NOT NULL,
                    AccountName TEXT NOT NULL,
                    ShipId TEXT NOT NULL,
                    ShipName TEXT NOT NULL,
                    ShipType TEXT NOT NULL DEFAULT '',
                    RosterSignature TEXT NOT NULL DEFAULT '',
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
                CREATE INDEX IF NOT EXISTS IX_Battles_Filter ON Battles(Server, AccountId, ShipId, StartedAt DESC);
                CREATE INDEX IF NOT EXISTS IX_Battles_AccountTime ON Battles(Server, AccountId, StartedAt DESC);
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

            if (!await ColumnExistsAsync(connection, "Battles", "SessionId", cancellationToken))
                await ExecuteAsync(connection, null, "ALTER TABLE Battles ADD COLUMN SessionId INTEGER NULL;", cancellationToken);

            const string v2Sql = """
                CREATE TABLE IF NOT EXISTS BattleSessions(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Server TEXT NOT NULL,
                    AccountId TEXT NOT NULL,
                    AccountName TEXT NOT NULL,
                    StartedAt TEXT NOT NULL,
                    EndedAt TEXT NOT NULL,
                    IsManual INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_PendingResultChecks_NextAttempt ON PendingResultChecks(NextAttemptAt);
                CREATE INDEX IF NOT EXISTS IX_BattleSessions_AccountTime ON BattleSessions(Server, AccountId, StartedAt DESC);
                CREATE INDEX IF NOT EXISTS IX_Battles_Session ON Battles(SessionId, StartedAt);
                CREATE TABLE IF NOT EXISTS BattleAdvancedMetrics(
                    BattleId INTEGER PRIMARY KEY,
                    BattleDurationSeconds REAL NULL,
                    Survived INTEGER NULL,
                    SurvivalSeconds REAL NULL,
                    PotentialDamage INTEGER NULL,
                    DamageTaken INTEGER NULL,
                    SurvivalAvailability INTEGER NOT NULL DEFAULT 0,
                    PotentialDamageAvailability INTEGER NOT NULL DEFAULT 0,
                    DamageTakenAvailability INTEGER NOT NULL DEFAULT 0,
                    ParserSchemaVersion TEXT NOT NULL DEFAULT '',
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS BattleDamageBreakdowns(
                    BattleId INTEGER NOT NULL,
                    Direction INTEGER NOT NULL,
                    RawTypeCode INTEGER NOT NULL,
                    Category INTEGER NOT NULL,
                    Damage INTEGER NOT NULL,
                    Availability INTEGER NOT NULL,
                    PRIMARY KEY(BattleId, Direction, RawTypeCode),
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS BattleReviews(
                    BattleId INTEGER PRIMARY KEY,
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    TagsJson TEXT NOT NULL DEFAULT '[]',
                    Note TEXT NOT NULL DEFAULT '',
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE CASCADE
                );
                INSERT OR IGNORE INTO SchemaMigrations(Version, AppliedAt) VALUES(2, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """;
            await ExecuteAsync(connection, null, v2Sql, cancellationToken);
            if (!await ColumnExistsAsync(connection, "Battles", "RosterSignature", cancellationToken))
                await ExecuteAsync(connection, null, "ALTER TABLE Battles ADD COLUMN RosterSignature TEXT NOT NULL DEFAULT '';", cancellationToken);
            await ExecuteAsync(connection, null, """
                UPDATE Battles SET RosterSignature=COALESCE((
                    SELECT group_concat(RosterEntry,'|') FROM (
                        SELECT lower(AccountName) || ':' || ShipId AS RosterEntry
                        FROM BattlePlayers WHERE BattleId=Battles.Id
                        ORDER BY lower(AccountName),ShipId
                    )
                ),'') WHERE RosterSignature='';
                INSERT OR IGNORE INTO SchemaMigrations(Version, AppliedAt) VALUES(3, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """, cancellationToken);
            if (!await ColumnExistsAsync(connection, "Battles", "ShipType", cancellationToken))
                await ExecuteAsync(connection, null, "ALTER TABLE Battles ADD COLUMN ShipType TEXT NOT NULL DEFAULT '';", cancellationToken);
            await ExecuteAsync(connection, null, """
                UPDATE Battles SET ShipType=COALESCE((
                    SELECT ShipType FROM BattlePlayers
                    WHERE BattlePlayers.BattleId=Battles.Id
                      AND BattlePlayers.Relation='0'
                      AND ShipType<>''
                    LIMIT 1
                ),'') WHERE ShipType='';
                """, cancellationToken);
            await BackfillShipTypesFromCatalogAsync(connection, cancellationToken);
            await ExecuteAsync(connection, null, "INSERT OR IGNORE INTO SchemaMigrations(Version, AppliedAt) VALUES(4, strftime('%Y-%m-%dT%H:%M:%fZ','now'));", cancellationToken);
            await BackfillSessionsAsync(connection, cancellationToken);
        }

        private static async Task BackfillShipTypesFromCatalogAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            List<(long Id, string ShipId)> missing = new();
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Id,ShipId FROM Battles WHERE ShipType=''";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) missing.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            foreach ((long id, string shipId) in missing)
            {
                string shipType = ShipInfoUtils.TryGetShipTypeByID(shipId);
                if (!string.IsNullOrWhiteSpace(shipType))
                    await ExecuteAsync(connection, null, "UPDATE Battles SET ShipType=$type WHERE Id=$id AND ShipType=''", cancellationToken, ("$type", shipType), ("$id", id));
            }
        }

        private async Task BackupBeforeMigrationAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(DatabasePath)) return;
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT COALESCE(MAX(Version),0) FROM SchemaMigrations";
                int version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (version >= CurrentSchemaVersion) return;
                await ExecuteAsync(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
                await connection.CloseAsync();
                SqliteConnection.ClearAllPools();
                string backup = $"{DatabasePath}.pre-v{CurrentSchemaVersion}-{DateTimeOffset.Now:yyyyMMddHHmmss}.bak";
                File.Copy(DatabasePath, backup, false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                // A database created before SchemaMigrations existed will be upgraded normally.
            }
        }

        private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table})";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                if (reader.GetString(reader.GetOrdinal("name")).Equals(column, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public async Task<long> UpsertDraftAsync(BattleRecord battle, IReadOnlyCollection<BattlePlayerRecord> players, ShipStatSnapshot? snapshot, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            string rosterSignature = CreateRosterSignature(players);
            if (!string.IsNullOrWhiteSpace(rosterSignature)) battle.RosterSignature = rosterSignature;
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                BattleRecord? reconnect = await FindReconnectCandidateAsync(connection, transaction, battle, cancellationToken);
                if (reconnect != null)
                {
                    battle.BattleKey = reconnect.BattleKey;
                    battle.StartedAt = reconnect.StartedAt;
                    battle.SessionId = reconnect.SessionId;
                }
                const string upsert = """
                    INSERT INTO Battles(SessionId,BattleKey,StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,ShipType,RosterSignature,Result,WinCount,Damage,Frags,BattleCount,Source,Completeness,ReplayHash,ReplayVersion,StatusMessage,UpdatedAt)
                    VALUES($session,$key,$started,$server,$mode,$map,$accountId,$accountName,$shipId,$shipName,$shipType,$roster,$result,$wins,$damage,$frags,$count,$source,$complete,$hash,$version,$status,$updated)
                    ON CONFLICT(BattleKey) DO UPDATE SET
                        Server=excluded.Server, Mode=excluded.Mode, MapName=excluded.MapName,
                        AccountId=excluded.AccountId, AccountName=excluded.AccountName,
                        ShipId=excluded.ShipId, ShipName=excluded.ShipName,
                        ShipType=CASE WHEN excluded.ShipType<>'' THEN excluded.ShipType ELSE Battles.ShipType END,
                        RosterSignature=CASE WHEN excluded.RosterSignature<>'' THEN excluded.RosterSignature ELSE Battles.RosterSignature END,
                        UpdatedAt=excluded.UpdatedAt
                    RETURNING Id;
                    """;
                await using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = upsert;
                AddBattleParameters(command, battle);
                long battleId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (!battle.SessionId.HasValue)
                    battle.SessionId = await AssignSessionAsync(connection, transaction, battleId, battle, cancellationToken);

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
                WHERE ReplayHash=$hash OR BattleKey=$key OR (
                    (Completeness=$pending OR EXISTS(
                        SELECT 1 FROM ReplayFiles
                        WHERE BattleId=Battles.Id AND ErrorCode IN ('BattleNotFinished','BattleExitedAfterDeath')
                    ))
                    AND Source IN ($metadata,$derived)
                    AND ($account='' OR lower(AccountName)=lower($account))
                    AND ($ship='' OR ShipId=$ship)
                    AND ($map='' OR MapName='' OR lower(MapName)=lower($map))
                    AND ($roster='' OR RosterSignature='' OR RosterSignature=$roster)
                    AND ($started='' OR abs(strftime('%s',StartedAt)-strftime('%s',$started)) <= $mergeWindow)
                )
                ORDER BY CASE WHEN ReplayHash=$hash THEN 0 WHEN BattleKey=$key THEN 1 WHEN ReplayHash IS NOT NULL THEN 2 ELSE 3 END,
                         StartedAt LIMIT 1
                """;
            command.Parameters.AddWithValue("$hash", replay.FileHash);
            command.Parameters.AddWithValue("$key", replay.BattleKey);
            command.Parameters.AddWithValue("$pending", (int)BattleCompleteness.Pending);
            command.Parameters.AddWithValue("$metadata", (int)BattleMetricSource.MetadataOnly);
            command.Parameters.AddWithValue("$derived", (int)BattleMetricSource.ReplayDerived);
            command.Parameters.AddWithValue("$account", replay.AccountName);
            command.Parameters.AddWithValue("$ship", replay.ShipId);
            command.Parameters.AddWithValue("$map", replay.MapName);
            command.Parameters.AddWithValue("$roster", replay.RosterSignature);
            command.Parameters.AddWithValue("$started", replay.StartedAt?.UtcDateTime.ToString("O") ?? "");
            command.Parameters.AddWithValue("$mergeWindow", ReconnectMergeWindowSeconds);
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
                        RosterSignature=CASE WHEN $roster<>'' THEN $roster ELSE RosterSignature END,
                        ReplayHash=$hash,ReplayVersion=$version,StatusMessage=$message,UpdatedAt=$updated WHERE Id=$id
                    """, cancellationToken,
                    ("$result", (int)replay.Result), ("$wins", replay.Result == BattleResult.Win ? 1 : replay.Result is BattleResult.Loss or BattleResult.Draw or BattleResult.UnknownNonWin ? 0 : null),
                    ("$damage", replay.Damage), ("$frags", replay.Frags),
                    ("$source", (int)replay.Source), ("$complete", (int)completeness), ("$hash", replay.FileHash),
                    ("$roster", replay.RosterSignature), ("$version", replay.GameVersion), ("$message", replay.ErrorMessage),
                    ("$updated", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")), ("$id", battleId));
                await UpsertReplayAsync(connection, transaction, battleId, replay, replayPath, cancellationToken);
                await DeleteReconnectDraftsAsync(connection, transaction, battleId, cancellationToken);
                replay.AdvancedMetrics.BattleId = battleId;
                await UpsertAdvancedMetricsAsync(connection, transaction, replay.AdvancedMetrics, cancellationToken);
                await ExecuteAsync(connection, transaction, "DELETE FROM BattleDamageBreakdowns WHERE BattleId=$id", cancellationToken, ("$id", battleId));
                foreach (BattleDamageBreakdown breakdown in replay.DamageBreakdowns)
                {
                    breakdown.BattleId = battleId;
                    await InsertDamageBreakdownAsync(connection, transaction, breakdown, cancellationToken);
                }
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

        public async Task<bool> HasReplayAsync(string replayHash, string parserVersion, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM ReplayFiles WHERE FileHash=$hash AND ParserVersion=$parser AND ParseStatus IN ($parsed,$partial) LIMIT 1";
            command.Parameters.AddWithValue("$hash", replayHash);
            command.Parameters.AddWithValue("$parser", parserVersion);
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
            List<string> predicates = new();
            if (!string.IsNullOrWhiteSpace(query.Server))
            {
                predicates.Add("Server=$server");
                command.Parameters.AddWithValue("$server", query.Server);
            }
            if (!string.IsNullOrWhiteSpace(query.AccountId))
            {
                predicates.Add("AccountId=$account");
                command.Parameters.AddWithValue("$account", query.AccountId);
            }
            if (!string.IsNullOrWhiteSpace(query.ShipId))
            {
                predicates.Add("ShipId=$ship");
                command.Parameters.AddWithValue("$ship", query.ShipId);
            }
            if (query.From.HasValue)
            {
                predicates.Add("StartedAt >= $from");
                command.Parameters.AddWithValue("$from", query.From.Value.UtcDateTime.ToString("O"));
            }
            if (query.To.HasValue)
            {
                predicates.Add("StartedAt < $to");
                command.Parameters.AddWithValue("$to", query.To.Value.UtcDateTime.ToString("O"));
            }
            StringBuilder sql = new("SELECT * FROM Battles");
            if (predicates.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", predicates));
            sql.Append(query.Descending ? " ORDER BY StartedAt DESC" : " ORDER BY StartedAt");
            if (query.Limit is > 0)
            {
                sql.Append(" LIMIT $limit OFFSET $offset");
                command.Parameters.AddWithValue("$limit", query.Limit.Value);
                command.Parameters.AddWithValue("$offset", Math.Max(0, query.Offset));
            }
            command.CommandText = sql.ToString();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(ReadBattle(reader));
            return result;
        }

        public Task<IReadOnlyList<HistoryFilterOption>> GetServersAsync(CancellationToken cancellationToken = default) =>
            GetOptionsAsync("SELECT DISTINCT Server,Server FROM Battles ORDER BY Server", Array.Empty<(string, object?)>(), cancellationToken);

        public Task<IReadOnlyList<HistoryFilterOption>> GetAccountsAsync(string? server, CancellationToken cancellationToken = default) =>
            GetOptionsAsync("SELECT DISTINCT AccountId,AccountName FROM Battles WHERE ($server='' OR Server=$server) ORDER BY AccountName", new[] { ("$server", (object?)(server ?? "")) }, cancellationToken);

        public Task<IReadOnlyList<HistoryFilterOption>> GetShipsAsync(string? server, string? accountId, CancellationToken cancellationToken = default) =>
            GetOptionsAsync("SELECT DISTINCT ShipId,ShipName,ShipType FROM Battles WHERE ($server='' OR Server=$server) AND ($account='' OR AccountId=$account) ORDER BY ShipName", new[] { ("$server", (object?)(server ?? "")), ("$account", (object?)(accountId ?? "")) }, cancellationToken);

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

        public async Task<IReadOnlyList<BattleSession>> GetSessionsAsync(string? server = null, string? accountId = null, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            List<BattleSession> sessions = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT s.Id,s.Server,s.AccountId,s.AccountName,s.StartedAt,s.EndedAt,s.IsManual,
                       COALESCE(SUM(MAX(1,b.BattleCount)),0) AS BattleCount
                FROM BattleSessions s
                LEFT JOIN Battles b ON b.SessionId=s.Id
                WHERE ($server='' OR s.Server=$server) AND ($account='' OR s.AccountId=$account)
                GROUP BY s.Id
                HAVING COUNT(b.Id)>0
                ORDER BY s.StartedAt DESC
                """;
            command.Parameters.AddWithValue("$server", server ?? "");
            command.Parameters.AddWithValue("$account", accountId ?? "");
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) sessions.Add(ReadSession(reader));
            return sessions;
        }

        public async Task<BattleSession?> GetLatestSessionAsync(string? server = null, string? accountId = null, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            List<string> predicates = new();
            if (!string.IsNullOrWhiteSpace(server))
            {
                predicates.Add("s.Server=$server");
                command.Parameters.AddWithValue("$server", server);
            }
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                predicates.Add("s.AccountId=$account");
                command.Parameters.AddWithValue("$account", accountId);
            }
            string where = predicates.Count == 0 ? "" : "WHERE " + string.Join(" AND ", predicates);
            command.CommandText = $"""
                SELECT s.Id,s.Server,s.AccountId,s.AccountName,s.StartedAt,s.EndedAt,s.IsManual,
                       COALESCE(SUM(MAX(1,b.BattleCount)),0) AS BattleCount
                FROM BattleSessions s
                INNER JOIN Battles b ON b.SessionId=s.Id
                {where}
                GROUP BY s.Id
                ORDER BY s.StartedAt DESC
                LIMIT 1
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
        }

        public async Task<IReadOnlyList<BattleRecord>> GetSessionBattlesAsync(long sessionId, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            List<BattleRecord> battles = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Battles WHERE SessionId=$id ORDER BY StartedAt";
            command.Parameters.AddWithValue("$id", sessionId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) battles.Add(ReadBattle(reader));
            return battles;
        }

        public async Task<IReadOnlyDictionary<long, BattleAdvancedMetrics>> GetAdvancedMetricsAsync(IEnumerable<long> battleIds, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            Dictionary<long, BattleAdvancedMetrics> result = new();
            long[] ids = battleIds.Distinct().ToArray();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            foreach (long[] chunk in ids.Chunk(500))
            {
                await using SqliteCommand command = connection.CreateCommand();
                List<string> names = new();
                for (int i = 0; i < chunk.Length; i++)
                {
                    string name = $"$id{i}";
                    names.Add(name);
                    command.Parameters.AddWithValue(name, chunk[i]);
                }
                command.CommandText = $"SELECT * FROM BattleAdvancedMetrics WHERE BattleId IN ({string.Join(',', names)})";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    BattleAdvancedMetrics metric = ReadAdvancedMetrics(reader);
                    result[metric.BattleId] = metric;
                }
            }
            return result;
        }

        public async Task<IReadOnlyList<BattleDamageBreakdown>> GetDamageBreakdownsAsync(long battleId, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            List<BattleDamageBreakdown> result = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT BattleId,Direction,RawTypeCode,Category,Damage,Availability FROM BattleDamageBreakdowns WHERE BattleId=$id ORDER BY Direction,Damage DESC";
            command.Parameters.AddWithValue("$id", battleId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(new BattleDamageBreakdown
            {
                BattleId = reader.GetInt64(0), Direction = (DamageDirection)reader.GetInt32(1), RawTypeCode = reader.GetInt32(2),
                Category = (DamageCategory)reader.GetInt32(3), Damage = reader.GetInt64(4), Availability = (MetricAvailability)reader.GetInt32(5)
            });
            return result;
        }

        public async Task<IReadOnlyList<BattlePlayerRecord>> GetBattlePlayersAsync(long battleId, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            List<BattlePlayerRecord> result = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM BattlePlayers WHERE BattleId=$id ORDER BY Relation,ShipTier DESC,AccountName";
            command.Parameters.AddWithValue("$id", battleId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(ReadBattlePlayer(reader));
            return result;
        }

        public async Task<BattleReview?> GetBattleReviewAsync(long battleId, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT BattleId,IsFavorite,TagsJson,Note,UpdatedAt FROM BattleReviews WHERE BattleId=$id";
            command.Parameters.AddWithValue("$id", battleId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return new BattleReview
            {
                BattleId = reader.GetInt64(0), IsFavorite = reader.GetInt32(1) != 0,
                Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? new List<string>(),
                Note = reader.GetString(3), UpdatedAt = ParseDate(reader.GetString(4))
            };
        }

        public Task SaveBattleReviewAsync(BattleReview review, CancellationToken cancellationToken = default)
        {
            review.Note = (review.Note ?? "").Trim();
            if (review.Note.Length > 4000) review.Note = review.Note[..4000];
            review.Tags = review.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).Take(8).ToList();
            review.UpdatedAt = DateTimeOffset.UtcNow;
            return WriteAsync("""
                INSERT INTO BattleReviews(BattleId,IsFavorite,TagsJson,Note,UpdatedAt) VALUES($id,$favorite,$tags,$note,$updated)
                ON CONFLICT(BattleId) DO UPDATE SET IsFavorite=excluded.IsFavorite,TagsJson=excluded.TagsJson,Note=excluded.Note,UpdatedAt=excluded.UpdatedAt
                """, cancellationToken, ("$id", review.BattleId), ("$favorite", review.IsFavorite ? 1 : 0),
                ("$tags", JsonSerializer.Serialize(review.Tags)), ("$note", review.Note), ("$updated", review.UpdatedAt.UtcDateTime.ToString("O")));
        }

        public async Task MergeSessionsAsync(IReadOnlyCollection<long> sessionIds, CancellationToken cancellationToken = default)
        {
            long[] ids = sessionIds.Distinct().Order().ToArray();
            if (ids.Length < 2) throw new ArgumentException("At least two sessions are required.", nameof(sessionIds));
            await EnsureInitialized(cancellationToken);
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                List<BattleSession> sessions = await ReadSessionsByIdsAsync(connection, transaction, ids, cancellationToken);
                if (sessions.Count != ids.Length || sessions.Select(x => (x.Server, x.AccountId)).Distinct().Count() != 1)
                    throw new InvalidOperationException("Only sessions for the same server and account can be merged.");
                BattleSession target = sessions.OrderBy(x => x.StartedAt).First();
                foreach (long id in ids.Where(x => x != target.Id))
                    await ExecuteAsync(connection, transaction, "UPDATE Battles SET SessionId=$target WHERE SessionId=$source; DELETE FROM BattleSessions WHERE Id=$source;", cancellationToken, ("$target", target.Id), ("$source", id));
                await RefreshSessionBoundsAsync(connection, transaction, target.Id, true, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally { writeLock.Release(); }
        }

        public async Task<long> SplitSessionAsync(long sessionId, long firstBattleIdOfNewSession, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection connection = new(ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                BattleSession? source = (await ReadSessionsByIdsAsync(connection, transaction, new[] { sessionId }, cancellationToken)).SingleOrDefault();
                BattleRecord? splitAt = await ReadBattleInSessionAsync(connection, transaction, sessionId, firstBattleIdOfNewSession, cancellationToken);
                if (source == null || splitAt == null || splitAt.StartedAt <= source.StartedAt)
                    throw new InvalidOperationException("The selected battle cannot be used to split this session.");
                long newId = await InsertSessionAsync(connection, transaction, source.Server, source.AccountId, source.AccountName, splitAt.StartedAt, source.EndedAt, true, cancellationToken);
                await ExecuteAsync(connection, transaction, "UPDATE Battles SET SessionId=$new WHERE SessionId=$old AND StartedAt >= $started", cancellationToken,
                    ("$new", newId), ("$old", sessionId), ("$started", splitAt.StartedAt.UtcDateTime.ToString("O")));
                await RefreshSessionBoundsAsync(connection, transaction, sessionId, true, cancellationToken);
                await RefreshSessionBoundsAsync(connection, transaction, newId, true, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return newId;
            }
            finally { writeLock.Release(); }
        }

        public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
        {
            await WriteAsync("DELETE FROM Battles; DELETE FROM ReplayFiles; DELETE FROM PendingResultChecks; DELETE FROM BattleSessions;", cancellationToken);
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
            while (await reader.ReadAsync(cancellationToken)) result.Add(new HistoryFilterOption
            {
                Value = reader.GetString(0),
                Display = reader.GetString(1),
                ShipType = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetString(2) : ""
            });
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

        private static async Task UpsertAdvancedMetricsAsync(SqliteConnection connection, SqliteTransaction transaction, BattleAdvancedMetrics metrics, CancellationToken cancellationToken)
        {
            bool hasAnyMetric = metrics.BattleDurationSeconds.HasValue || metrics.Survived.HasValue || metrics.SurvivalSeconds.HasValue ||
                                metrics.PotentialDamage.HasValue || metrics.DamageTaken.HasValue;
            if (!hasAnyMetric) return;
            await ExecuteAsync(connection, transaction, """
                INSERT INTO BattleAdvancedMetrics(BattleId,BattleDurationSeconds,Survived,SurvivalSeconds,PotentialDamage,DamageTaken,
                    SurvivalAvailability,PotentialDamageAvailability,DamageTakenAvailability,ParserSchemaVersion)
                VALUES($id,$duration,$survived,$survival,$potential,$taken,$survivalAvailability,$potentialAvailability,$takenAvailability,$schema)
                ON CONFLICT(BattleId) DO UPDATE SET
                    BattleDurationSeconds=excluded.BattleDurationSeconds,Survived=excluded.Survived,SurvivalSeconds=excluded.SurvivalSeconds,
                    PotentialDamage=excluded.PotentialDamage,DamageTaken=excluded.DamageTaken,
                    SurvivalAvailability=excluded.SurvivalAvailability,PotentialDamageAvailability=excluded.PotentialDamageAvailability,
                    DamageTakenAvailability=excluded.DamageTakenAvailability,ParserSchemaVersion=excluded.ParserSchemaVersion
                """, cancellationToken,
                ("$id", metrics.BattleId), ("$duration", metrics.BattleDurationSeconds),
                ("$survived", metrics.Survived.HasValue ? metrics.Survived.Value ? 1 : 0 : null),
                ("$survival", metrics.SurvivalSeconds), ("$potential", metrics.PotentialDamage), ("$taken", metrics.DamageTaken),
                ("$survivalAvailability", (int)metrics.SurvivalAvailability), ("$potentialAvailability", (int)metrics.PotentialDamageAvailability),
                ("$takenAvailability", (int)metrics.DamageTakenAvailability), ("$schema", metrics.ParserSchemaVersion));
        }

        private static Task InsertDamageBreakdownAsync(SqliteConnection connection, SqliteTransaction transaction, BattleDamageBreakdown breakdown, CancellationToken cancellationToken) =>
            ExecuteAsync(connection, transaction, """
                INSERT INTO BattleDamageBreakdowns(BattleId,Direction,RawTypeCode,Category,Damage,Availability)
                VALUES($battle,$direction,$raw,$category,$damage,$availability)
                """, cancellationToken, ("$battle", breakdown.BattleId), ("$direction", (int)breakdown.Direction),
                ("$raw", breakdown.RawTypeCode), ("$category", (int)breakdown.Category), ("$damage", breakdown.Damage), ("$availability", (int)breakdown.Availability));

        private static async Task<long> AssignSessionAsync(SqliteConnection connection, SqliteTransaction transaction, long battleId, BattleRecord battle, CancellationToken cancellationToken)
        {
            await using SqliteCommand find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = """
                SELECT Id,EndedAt,IsManual FROM BattleSessions
                WHERE Server=$server AND AccountId=$account AND EndedAt <= $started
                ORDER BY EndedAt DESC LIMIT 1
                """;
            find.Parameters.AddWithValue("$server", battle.Server);
            find.Parameters.AddWithValue("$account", battle.AccountId);
            find.Parameters.AddWithValue("$started", battle.StartedAt.UtcDateTime.ToString("O"));
            long? sessionId = null;
            DateTimeOffset? endedAt = null;
            await using (SqliteDataReader reader = await find.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    sessionId = reader.GetInt64(0);
                    endedAt = ParseDate(reader.GetString(1));
                }
            }
            if (!sessionId.HasValue || !endedAt.HasValue || battle.StartedAt - endedAt.Value > SessionGap)
                sessionId = await InsertSessionAsync(connection, transaction, battle.Server, battle.AccountId, battle.AccountName, battle.StartedAt, battle.StartedAt, false, cancellationToken);
            else
                await ExecuteAsync(connection, transaction, "UPDATE BattleSessions SET EndedAt=$ended,AccountName=$name WHERE Id=$id", cancellationToken,
                    ("$ended", battle.StartedAt.UtcDateTime.ToString("O")), ("$name", battle.AccountName), ("$id", sessionId.Value));
            await ExecuteAsync(connection, transaction, "UPDATE Battles SET SessionId=$session WHERE Id=$battle", cancellationToken, ("$session", sessionId.Value), ("$battle", battleId));
            return sessionId.Value;
        }

        private static async Task<long> InsertSessionAsync(SqliteConnection connection, SqliteTransaction transaction, string server, string accountId, string accountName, DateTimeOffset startedAt, DateTimeOffset endedAt, bool manual, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO BattleSessions(Server,AccountId,AccountName,StartedAt,EndedAt,IsManual)
                VALUES($server,$account,$name,$started,$ended,$manual) RETURNING Id
                """;
            command.Parameters.AddWithValue("$server", server);
            command.Parameters.AddWithValue("$account", accountId);
            command.Parameters.AddWithValue("$name", accountName);
            command.Parameters.AddWithValue("$started", startedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$ended", endedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$manual", manual ? 1 : 0);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        private static async Task BackfillSessionsAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            List<BattleRecord> unassigned = new();
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM Battles WHERE SessionId IS NULL ORDER BY Server,AccountId,StartedAt";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) unassigned.Add(ReadBattle(reader));
            }
            if (unassigned.Count == 0) return;
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            foreach (BattleRecord battle in unassigned)
                battle.SessionId = await AssignSessionAsync(connection, transaction, battle.Id, battle, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private static async Task<List<BattleSession>> ReadSessionsByIdsAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<long> ids, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            List<string> names = new();
            for (int i = 0; i < ids.Count; i++)
            {
                string name = $"$id{i}";
                names.Add(name);
                command.Parameters.AddWithValue(name, ids[i]);
            }
            command.CommandText = $"""
                SELECT s.Id,s.Server,s.AccountId,s.AccountName,s.StartedAt,s.EndedAt,s.IsManual,
                       COALESCE(SUM(MAX(1,b.BattleCount)),0) AS BattleCount
                FROM BattleSessions s LEFT JOIN Battles b ON b.SessionId=s.Id
                WHERE s.Id IN ({string.Join(',', names)}) GROUP BY s.Id
                """;
            List<BattleSession> result = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(ReadSession(reader));
            return result;
        }

        private static async Task<BattleRecord?> ReadBattleInSessionAsync(SqliteConnection connection, SqliteTransaction transaction, long sessionId, long battleId, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT * FROM Battles WHERE Id=$battle AND SessionId=$session";
            command.Parameters.AddWithValue("$battle", battleId);
            command.Parameters.AddWithValue("$session", sessionId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadBattle(reader) : null;
        }

        private static Task RefreshSessionBoundsAsync(SqliteConnection connection, SqliteTransaction transaction, long sessionId, bool manual, CancellationToken cancellationToken) =>
            ExecuteAsync(connection, transaction, """
                UPDATE BattleSessions SET
                    StartedAt=(SELECT MIN(StartedAt) FROM Battles WHERE SessionId=$id),
                    EndedAt=(SELECT MAX(StartedAt) FROM Battles WHERE SessionId=$id),
                    IsManual=$manual
                WHERE Id=$id
                """, cancellationToken, ("$id", sessionId), ("$manual", manual ? 1 : 0));

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
            command.Parameters.AddWithValue("$session", battle.SessionId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$key", battle.BattleKey);
            command.Parameters.AddWithValue("$started", battle.StartedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$server", battle.Server);
            command.Parameters.AddWithValue("$mode", battle.Mode);
            command.Parameters.AddWithValue("$map", battle.MapName);
            command.Parameters.AddWithValue("$accountId", battle.AccountId);
            command.Parameters.AddWithValue("$accountName", battle.AccountName);
            command.Parameters.AddWithValue("$shipId", battle.ShipId);
            command.Parameters.AddWithValue("$shipName", battle.ShipName);
            command.Parameters.AddWithValue("$shipType", battle.ShipType);
            command.Parameters.AddWithValue("$roster", battle.RosterSignature);
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
            SessionId = reader.IsDBNull(reader.GetOrdinal("SessionId")) ? null : reader.GetInt64(reader.GetOrdinal("SessionId")),
            BattleKey = reader.GetString(reader.GetOrdinal("BattleKey")),
            StartedAt = ParseDate(reader.GetString(reader.GetOrdinal("StartedAt"))),
            Server = reader.GetString(reader.GetOrdinal("Server")), Mode = reader.GetString(reader.GetOrdinal("Mode")),
            MapName = reader.GetString(reader.GetOrdinal("MapName")), AccountId = reader.GetString(reader.GetOrdinal("AccountId")),
            AccountName = reader.GetString(reader.GetOrdinal("AccountName")), ShipId = reader.GetString(reader.GetOrdinal("ShipId")),
            ShipName = reader.GetString(reader.GetOrdinal("ShipName")), ShipType = reader.GetString(reader.GetOrdinal("ShipType")), RosterSignature = reader.GetString(reader.GetOrdinal("RosterSignature")),
            Result = (BattleResult)reader.GetInt32(reader.GetOrdinal("Result")),
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

        private static BattleSession ReadSession(SqliteDataReader reader) => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")), Server = reader.GetString(reader.GetOrdinal("Server")),
            AccountId = reader.GetString(reader.GetOrdinal("AccountId")), AccountName = reader.GetString(reader.GetOrdinal("AccountName")),
            StartedAt = ParseDate(reader.GetString(reader.GetOrdinal("StartedAt"))), EndedAt = ParseDate(reader.GetString(reader.GetOrdinal("EndedAt"))),
            IsManual = reader.GetInt32(reader.GetOrdinal("IsManual")) != 0,
            BattleCount = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("BattleCount")), CultureInfo.InvariantCulture)
        };

        private static BattleAdvancedMetrics ReadAdvancedMetrics(SqliteDataReader reader) => new()
        {
            BattleId = reader.GetInt64(reader.GetOrdinal("BattleId")),
            BattleDurationSeconds = reader.IsDBNull(reader.GetOrdinal("BattleDurationSeconds")) ? null : reader.GetDouble(reader.GetOrdinal("BattleDurationSeconds")),
            Survived = reader.IsDBNull(reader.GetOrdinal("Survived")) ? null : reader.GetInt32(reader.GetOrdinal("Survived")) != 0,
            SurvivalSeconds = reader.IsDBNull(reader.GetOrdinal("SurvivalSeconds")) ? null : reader.GetDouble(reader.GetOrdinal("SurvivalSeconds")),
            PotentialDamage = reader.IsDBNull(reader.GetOrdinal("PotentialDamage")) ? null : reader.GetInt64(reader.GetOrdinal("PotentialDamage")),
            DamageTaken = reader.IsDBNull(reader.GetOrdinal("DamageTaken")) ? null : reader.GetInt64(reader.GetOrdinal("DamageTaken")),
            SurvivalAvailability = (MetricAvailability)reader.GetInt32(reader.GetOrdinal("SurvivalAvailability")),
            PotentialDamageAvailability = (MetricAvailability)reader.GetInt32(reader.GetOrdinal("PotentialDamageAvailability")),
            DamageTakenAvailability = (MetricAvailability)reader.GetInt32(reader.GetOrdinal("DamageTakenAvailability")),
            ParserSchemaVersion = reader.GetString(reader.GetOrdinal("ParserSchemaVersion"))
        };

        private static BattlePlayerRecord ReadBattlePlayer(SqliteDataReader reader) => new()
        {
            PlayerKey = reader.GetString(reader.GetOrdinal("PlayerKey")), AccountId = reader.GetString(reader.GetOrdinal("AccountId")),
            AccountName = reader.GetString(reader.GetOrdinal("AccountName")), Relation = reader.GetString(reader.GetOrdinal("Relation")),
            ShipId = reader.GetString(reader.GetOrdinal("ShipId")), ShipName = reader.GetString(reader.GetOrdinal("ShipName")),
            ShipType = reader.GetString(reader.GetOrdinal("ShipType")), ShipTier = reader.GetInt32(reader.GetOrdinal("ShipTier")),
            IsHidden = reader.GetInt32(reader.GetOrdinal("IsHidden")) != 0, IsDataStale = reader.GetInt32(reader.GetOrdinal("IsDataStale")) != 0,
            AccountBattles = GetNullableDouble(reader, "AccountBattles"), AccountWinrate = GetNullableDouble(reader, "AccountWinrate"),
            AccountPr = GetNullableDouble(reader, "AccountPr"), ShipBattles = GetNullableDouble(reader, "ShipBattles"),
            ShipWinrate = GetNullableDouble(reader, "ShipWinrate"), ShipPr = GetNullableDouble(reader, "ShipPr")
        };

        private static double? GetNullableDouble(SqliteDataReader reader, string name)
        {
            int ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
        }

        private static async Task<BattleRecord?> FindReconnectCandidateAsync(SqliteConnection connection, SqliteTransaction transaction, BattleRecord battle, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(battle.RosterSignature)) return null;
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT * FROM Battles
                WHERE BattleKey<>$key
                  AND Completeness IN ($pending,$partial,$unsupported,$failed)
                  AND Source IN ($metadata,$derived)
                  AND EXISTS(
                      SELECT 1 FROM ReplayFiles
                      WHERE BattleId=Battles.Id AND ErrorCode IN ('BattleNotFinished','BattleExitedAfterDeath')
                  )
                  AND (Server=$server OR Server='AUTO' OR $server='AUTO')
                  AND lower(AccountName)=lower($account)
                  AND ShipId=$ship
                  AND ($map='' OR MapName='' OR lower(MapName)=lower($map))
                  AND RosterSignature=$roster
                  AND strftime('%s',$started)-strftime('%s',StartedAt) BETWEEN 0 AND $mergeWindow
                ORDER BY CASE WHEN ReplayHash IS NOT NULL THEN 0 ELSE 1 END, StartedAt
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$key", battle.BattleKey);
            command.Parameters.AddWithValue("$pending", (int)BattleCompleteness.Pending);
            command.Parameters.AddWithValue("$partial", (int)BattleCompleteness.Partial);
            command.Parameters.AddWithValue("$unsupported", (int)BattleCompleteness.Unsupported);
            command.Parameters.AddWithValue("$failed", (int)BattleCompleteness.Failed);
            command.Parameters.AddWithValue("$metadata", (int)BattleMetricSource.MetadataOnly);
            command.Parameters.AddWithValue("$derived", (int)BattleMetricSource.ReplayDerived);
            command.Parameters.AddWithValue("$server", battle.Server);
            command.Parameters.AddWithValue("$account", battle.AccountName);
            command.Parameters.AddWithValue("$ship", battle.ShipId);
            command.Parameters.AddWithValue("$map", battle.MapName);
            command.Parameters.AddWithValue("$roster", battle.RosterSignature);
            command.Parameters.AddWithValue("$started", battle.StartedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$mergeWindow", ReconnectMergeWindowSeconds);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadBattle(reader) : null;
        }

        private static async Task DeleteReconnectDraftsAsync(SqliteConnection connection, SqliteTransaction transaction, long battleId, CancellationToken cancellationToken)
        {
            await ExecuteAsync(connection, transaction, """
                DELETE FROM Battles
                WHERE Id<>$id
                  AND ReplayHash IS NULL
                  AND Source=$metadata
                  AND Completeness=$pending
                  AND StatusMessage='WaitingForReplay'
                  AND RosterSignature<>''
                  AND RosterSignature=(SELECT RosterSignature FROM Battles WHERE Id=$id)
                  AND lower(AccountName)=lower((SELECT AccountName FROM Battles WHERE Id=$id))
                  AND ShipId=(SELECT ShipId FROM Battles WHERE Id=$id)
                  AND (MapName='' OR (SELECT MapName FROM Battles WHERE Id=$id)='' OR lower(MapName)=lower((SELECT MapName FROM Battles WHERE Id=$id)))
                  AND abs(strftime('%s',StartedAt)-strftime('%s',(SELECT StartedAt FROM Battles WHERE Id=$id))) <= $mergeWindow
                  AND EXISTS(
                      SELECT 1 FROM ReplayFiles
                      WHERE BattleId=$id AND ErrorCode IN ('BattleNotFinished','BattleExitedAfterDeath')
                  );
                DELETE FROM BattleSessions WHERE NOT EXISTS(SELECT 1 FROM Battles WHERE Battles.SessionId=BattleSessions.Id);
                UPDATE BattleSessions SET
                    StartedAt=(SELECT MIN(StartedAt) FROM Battles WHERE SessionId=BattleSessions.Id),
                    EndedAt=(SELECT MAX(StartedAt) FROM Battles WHERE SessionId=BattleSessions.Id)
                WHERE EXISTS(SELECT 1 FROM Battles WHERE Battles.SessionId=BattleSessions.Id);
                """, cancellationToken,
                ("$id", battleId), ("$metadata", (int)BattleMetricSource.MetadataOnly),
                ("$pending", (int)BattleCompleteness.Pending), ("$mergeWindow", ReconnectMergeWindowSeconds));
        }

        private static ShipStatSnapshot ReadSnapshot(SqliteDataReader reader) => new()
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")), BattleId = reader.GetInt64(reader.GetOrdinal("BattleId")),
            CapturedAt = ParseDate(reader.GetString(reader.GetOrdinal("CapturedAt"))), Provider = reader.GetString(reader.GetOrdinal("Provider")),
            AccountId = reader.GetString(reader.GetOrdinal("AccountId")), ShipId = reader.GetString(reader.GetOrdinal("ShipId")),
            Battles = reader.GetDouble(reader.GetOrdinal("Battles")), Wins = reader.GetDouble(reader.GetOrdinal("Wins")),
            Losses = reader.IsDBNull(reader.GetOrdinal("Losses")) ? null : reader.GetDouble(reader.GetOrdinal("Losses")),
            Damage = reader.GetDouble(reader.GetOrdinal("Damage")), Frags = reader.GetDouble(reader.GetOrdinal("Frags"))
        };

        private static string CreateRosterSignature(IEnumerable<BattlePlayerRecord> players) => string.Join("|", players
            .Where(x => !string.IsNullOrWhiteSpace(x.AccountName) && !string.IsNullOrWhiteSpace(x.ShipId))
            .Select(x => $"{x.AccountName.Trim().ToLowerInvariant()}:{x.ShipId.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal));

        private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        private static void TryMoveSidecar(string source, string destination)
        {
            if (File.Exists(source)) File.Move(source, destination, true);
        }
        private Task EnsureInitialized(CancellationToken cancellationToken) => initialized ? Task.CompletedTask : InitializeAsync(cancellationToken);
    }
}
