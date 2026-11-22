using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed partial class SqliteHistoryRepository
    {
        // Resolve missing IDs only for display and filtering. A later import can
        // reveal a second real account with this name, so keep the recorded IDs.
        private static string FilterAccountId(string table) => $"""
            (CASE WHEN {table}.AccountId NOT IN ('','-1') AND {table}.AccountId NOT LIKE 'name:%'
                THEN {table}.AccountId
                ELSE COALESCE((
                    SELECT MIN(known.AccountId) FROM Battles known
                    WHERE known.Server={table}.Server AND known.Server NOT IN ('','AUTO')
                      AND known.AccountName={table}.AccountName COLLATE NOCASE AND known.AccountName<>''
                      AND known.AccountId NOT IN ('','-1') AND known.AccountId NOT LIKE 'name:%'
                    HAVING MIN(known.AccountId)=MAX(known.AccountId)
                ), 'unresolved:' || {table}.Server || ':' || lower({table}.AccountName)) END)
            """;

        public async Task<int> CountBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            StringBuilder sql = new("SELECT COUNT(*) FROM Battles");
            AppendBattleFilters(command, sql, query);
            command.CommandText = sql.ToString();
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        public async Task<IReadOnlyList<BattleRecord>> GetBattlesAsync(HistoryQuery query, CancellationToken cancellationToken = default)
        {
            await EnsureInitialized(cancellationToken);
            List<BattleRecord> result = new();
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            StringBuilder sql = new("SELECT * FROM Battles");
            AppendBattleFilters(command, sql, query);
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

        private static string ShipCategoryPredicate(SqliteCommand command, IReadOnlyList<string>? ids)
        {
            if (ids == null) return "1";
            command.Parameters.AddWithValue("$matchingShips", JsonSerializer.Serialize(ids));
            return "ShipId IN (SELECT value FROM json_each($matchingShips))";
        }

        private static void AppendBattleFilters(SqliteCommand command, StringBuilder sql, HistoryQuery query)
        {
            List<string> predicates = new();
            if (query.MatchingShipIds != null) predicates.Add(ShipCategoryPredicate(command, query.MatchingShipIds));
            if (query.SingleBattlesOnly) predicates.Add("BattleCount=1 AND Source<>3");
            if (query.FavoritesOnly) predicates.Add("EXISTS(SELECT 1 FROM BattleReviews r WHERE r.BattleId=Battles.Id AND r.IsFavorite=1)");
            if (!string.IsNullOrWhiteSpace(query.Server))
            {
                predicates.Add("Server=$server");
                command.Parameters.AddWithValue("$server", query.Server);
            }
            if (!string.IsNullOrWhiteSpace(query.AccountId))
            {
                predicates.Add($"{FilterAccountId("Battles")}=$account");
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
            if (predicates.Count > 0) sql.Append(" WHERE ").Append(string.Join(" AND ", predicates));
        }
    }
}
