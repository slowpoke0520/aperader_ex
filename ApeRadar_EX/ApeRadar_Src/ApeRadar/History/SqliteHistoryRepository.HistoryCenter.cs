using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed partial class SqliteHistoryRepository
    {
        private static Task CreateHistoryCenterSchemaAsync(SqliteConnection connection, CancellationToken token) =>
            ExecuteAsync(connection, null, """
                CREATE TABLE IF NOT EXISTS ApiBattleIntervals(
                    IntervalKey TEXT PRIMARY KEY, Server TEXT NOT NULL, AccountId TEXT NOT NULL,
                    AccountName TEXT NOT NULL, ShipId TEXT NOT NULL, ShipName TEXT NOT NULL,
                    StartedAt TEXT NOT NULL, EndedAt TEXT NOT NULL, BattleCount INTEGER NOT NULL,
                    Wins REAL NULL, Damage INTEGER NULL, Frags REAL NULL, HasCounterBounds INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ApiBattleIntervals_Filter ON ApiBattleIntervals(Server,AccountId,ShipId,EndedAt);
                CREATE TABLE IF NOT EXISTS BattleReviewDrafts(
                    BattleId INTEGER PRIMARY KEY, IsFavorite INTEGER NOT NULL, TagsJson TEXT NOT NULL,
                    Note TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY(BattleId) REFERENCES Battles(Id) ON DELETE CASCADE
                );
                INSERT OR IGNORE INTO ApiBattleIntervals
                SELECT 'legacy:' || BattleKey,Server,AccountId,AccountName,ShipId,ShipName,
                    StartedAt,UpdatedAt,BattleCount,WinCount,Damage,Frags,0 FROM Battles
                WHERE BattleCount>1 AND NOT EXISTS(SELECT 1 FROM SchemaMigrations WHERE Version=6);
                INSERT OR IGNORE INTO SchemaMigrations(Version,AppliedAt) VALUES(6,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """, token);

        private async Task StoreApiIntervalAsync(long id, ShipStatSnapshot before, ShipStatSnapshot after, CancellationToken token)
        {
            BattleRecord? battle = await GetBattleAsync(id, token);
            if (battle == null) return;
            string key = $"{battle.Server}:{battle.AccountId}:{battle.ShipId}:{before.Provider}:" +
                before.Battles.ToString("R", CultureInfo.InvariantCulture) + ":" + after.Battles.ToString("R", CultureInfo.InvariantCulture);
            DateTimeOffset from = before.CapturedAt == default ? battle.StartedAt : before.CapturedAt;
            DateTimeOffset to = after.CapturedAt == default ? DateTimeOffset.UtcNow : after.CapturedAt;
            await WriteAsync("""
                INSERT INTO ApiBattleIntervals VALUES($key,$server,$account,$name,$ship,$shipName,$from,$to,$count,$wins,$damage,$frags,1)
                ON CONFLICT(IntervalKey) DO UPDATE SET EndedAt=excluded.EndedAt,Wins=excluded.Wins,Damage=excluded.Damage,Frags=excluded.Frags;
                """, token, ("$key", key), ("$server", battle.Server), ("$account", battle.AccountId), ("$name", battle.AccountName),
                ("$ship", battle.ShipId), ("$shipName", battle.ShipName), ("$from", from.UtcDateTime.ToString("O")),
                ("$to", to.UtcDateTime.ToString("O")), ("$count", Convert.ToInt32(after.Battles-before.Battles)),
                ("$wins", Math.Max(0,after.Wins-before.Wins)), ("$damage", Math.Max(0,after.Damage-before.Damage)),
                ("$frags", Math.Max(0,after.Frags-before.Frags)));
        }

        public async Task<IReadOnlyList<ApiBattleInterval>> GetApiIntervalsAsync(HistoryQuery query, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            // Time filters select overlapping API capture periods; they do not assign battles to a day.
            command.CommandText = """
                SELECT * FROM ApiBattleIntervals WHERE ($server='' OR Server=$server)
                    AND ($account='' OR AccountId=$account) AND ($ship='' OR ShipId=$ship)
                    AND ($from='' OR EndedAt>=$from) AND ($to='' OR StartedAt<$to) ORDER BY EndedAt DESC;
                """;
            command.Parameters.AddWithValue("$server", query.Server ?? "");
            command.Parameters.AddWithValue("$account", query.AccountId ?? "");
            command.Parameters.AddWithValue("$ship", query.ShipId ?? "");
            command.Parameters.AddWithValue("$from", query.From?.UtcDateTime.ToString("O") ?? "");
            command.Parameters.AddWithValue("$to", query.To?.UtcDateTime.ToString("O") ?? "");
            List<ApiBattleInterval> rows = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new ApiBattleInterval
            {
                IntervalKey=reader.GetString(0),Server=reader.GetString(1),AccountId=reader.GetString(2),AccountName=reader.GetString(3),
                ShipId=reader.GetString(4),ShipName=reader.GetString(5),From=ParseDate(reader.GetString(6)),To=ParseDate(reader.GetString(7)),
                BattleCount=reader.GetInt32(8),Wins=reader.IsDBNull(9)?null:reader.GetDouble(9),
                Damage=reader.IsDBNull(10)?null:reader.GetInt64(10),Frags=reader.IsDBNull(11)?null:reader.GetDouble(11),
                HasCounterBounds=reader.GetInt32(12)!=0
            });
            return rows;
        }

        public async Task<BattleReview?> GetReviewDraftAsync(long battleId, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT IsFavorite,TagsJson,Note,UpdatedAt FROM BattleReviewDrafts WHERE BattleId=$id";
            command.Parameters.AddWithValue("$id", battleId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return !await reader.ReadAsync(cancellationToken) ? null : new BattleReview
            {
                BattleId=battleId,IsFavorite=reader.GetInt32(0)!=0,Tags=JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? new(),
                Note=reader.GetString(2),UpdatedAt=ParseDate(reader.GetString(3))
            };
        }

        public Task SaveReviewDraftAsync(BattleReview draft, CancellationToken cancellationToken = default) => WriteAsync("""
            INSERT INTO BattleReviewDrafts VALUES($id,$favorite,$tags,$note,$updated)
            ON CONFLICT(BattleId) DO UPDATE SET IsFavorite=excluded.IsFavorite,TagsJson=excluded.TagsJson,Note=excluded.Note,UpdatedAt=excluded.UpdatedAt;
            """, cancellationToken, ("$id", draft.BattleId), ("$favorite", draft.IsFavorite?1:0),
            ("$tags", JsonSerializer.Serialize(draft.Tags.Distinct(StringComparer.Ordinal).Take(8))),
            ("$note", draft.Note[..Math.Min(draft.Note.Length,4000)]), ("$updated", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")));

        public Task SetBattleFavoriteAsync(long battleId, bool favorite, CancellationToken cancellationToken = default) => WriteAsync("""
            INSERT INTO BattleReviews(BattleId,IsFavorite,UpdatedAt) VALUES($id,$favorite,$updated)
            ON CONFLICT(BattleId) DO UPDATE SET IsFavorite=excluded.IsFavorite,UpdatedAt=excluded.UpdatedAt;
            UPDATE BattleReviewDrafts SET IsFavorite=$favorite WHERE BattleId=$id;
            """, cancellationToken, ("$id", battleId), ("$favorite", favorite?1:0), ("$updated", DateTimeOffset.UtcNow.UtcDateTime.ToString("O")));

        public async Task<IReadOnlyDictionary<long,BattleReview>> GetReviewSummariesAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
        {
            long[] values=ids.Distinct().ToArray();
            Dictionary<long,BattleReview> result=new();
            if (values.Length==0) return result;
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection=new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command=connection.CreateCommand();
            for (int i=0;i<values.Length;i++) command.Parameters.AddWithValue($"$id{i}",values[i]);
            command.CommandText=$"SELECT BattleId,IsFavorite,TagsJson,Note FROM BattleReviews WHERE BattleId IN ({string.Join(",",values.Select((_,i)=>$"$id{i}"))})";
            await using SqliteDataReader reader=await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result[reader.GetInt64(0)]=new BattleReview
            {
                BattleId=reader.GetInt64(0),IsFavorite=reader.GetInt32(1)!=0,
                Tags=JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? new(),Note=reader.GetString(3)
            };
            return result;
        }
    }
}
