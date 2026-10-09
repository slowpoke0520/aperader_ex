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
        private static async Task<BattleRecord?> ReadBattleByIdAsync(SqliteConnection connection, SqliteTransaction? transaction,
            long id, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT * FROM Battles WHERE Id=$id";
            command.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadBattle(reader) : null;
        }

        private static async Task<BattleRecord?> FindIdentityMatchAsync(SqliteConnection connection, SqliteTransaction? transaction,
            BattleRecord identity, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(identity.ReplayHash))
            {
                // All reconnect segments keep their association when reparsed, even
                // if only the newest segment is recorded in Battles.ReplayHash.
                await using SqliteCommand linked = connection.CreateCommand();
                linked.Transaction = transaction;
                linked.CommandText = "SELECT Battles.* FROM Battles JOIN ReplayFiles ON ReplayFiles.BattleId=Battles.Id WHERE ReplayFiles.FileHash=$hash";
                linked.Parameters.AddWithValue("$hash", identity.ReplayHash);
                await using SqliteDataReader linkedReader = await linked.ExecuteReaderAsync(cancellationToken);
                if (await linkedReader.ReadAsync(cancellationToken)) return ReadBattle(linkedReader);
            }
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT * FROM Battles
                WHERE ($hash<>'' AND ReplayHash=$hash) OR ($key<>'' AND BattleKey=$key) OR (
                    ShipId=$ship AND AccountName=$account COLLATE NOCASE
                    AND abs(strftime('%s',StartedAt)-strftime('%s',$started)) <= $window
                ) ORDER BY StartedAt,Id
                """;
            command.Parameters.AddWithValue("$hash", identity.ReplayHash ?? "");
            command.Parameters.AddWithValue("$key", identity.BattleKey);
            command.Parameters.AddWithValue("$ship", identity.ShipId);
            command.Parameters.AddWithValue("$account", identity.AccountName);
            command.Parameters.AddWithValue("$started", identity.StartedAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$window", (int)HistoryBattleIdentity.ReconnectWindow.TotalSeconds);
            List<BattleRecord> candidates = new();
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) candidates.Add(ReadBattle(reader));
            BattleRecord? exact = candidates.FirstOrDefault(x => identity.ReplayHash is { Length: > 0 } && x.ReplayHash == identity.ReplayHash)
                ?? candidates.FirstOrDefault(x => identity.BattleKey != "" && x.BattleKey == identity.BattleKey);
            if (exact != null) return exact;
            BattleRecord[] matches = candidates.Where(x => HistoryBattleIdentity.SameBattle(x, identity)).ToArray();
            // Conflicting identities are left separate rather than guessed from proximity.
            return matches.Length > 0 && matches.All(x => HistoryBattleIdentity.SameBattle(matches[0], x)) ? matches[0] : null;
        }

        private static int MetricPriority(BattleRecord battle)
        {
            bool completeSingle = battle.BattleCount == 1 && battle.Completeness == BattleCompleteness.Complete &&
                battle.Damage.HasValue && battle.Frags.HasValue && battle.Result != BattleResult.Unknown;
            if (completeSingle) return 100 + (battle.Source switch
            {
                BattleMetricSource.ReplayExact => 50,
                BattleMetricSource.ApiExact => 40,
                BattleMetricSource.ReplayDerived => 30,
                _ => 0
            });
            int knownFields = (battle.Damage.HasValue ? 4 : 0) + (battle.Frags.HasValue ? 2 : 0) +
                (battle.Result != BattleResult.Unknown ? 1 : 0);
            // Even a partial replay identifies one battle. A multi-battle API interval
            // must not be attributed to it or counted again alongside other recordings.
            if (battle.BattleCount == 1 && battle.Source is BattleMetricSource.ReplayExact or BattleMetricSource.ReplayDerived)
                return 30 + knownFields;
            if (battle.Source == BattleMetricSource.ApiMerged) return 20;
            return knownFields;
        }

        private static void MergeBattleValues(BattleRecord target, BattleRecord other)
        {
            if (other.StartedAt < target.StartedAt) target.StartedAt = other.StartedAt;
            if (target.Server is "" or "AUTO" && other.Server is not ("" or "AUTO")) target.Server = other.Server;
            if ((target.AccountId == "" || target.AccountId.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) &&
                other.AccountId != "" && !other.AccountId.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) target.AccountId = other.AccountId;
            if (target.MapName == "") target.MapName = other.MapName;
            if (target.ShipName == "" || target.ShipName == target.ShipId) target.ShipName = other.ShipName;
            if (target.ShipType == "") target.ShipType = other.ShipType;
            if (target.RosterSignature == "") target.RosterSignature = other.RosterSignature;
            target.SessionId ??= other.SessionId;
            if (MetricPriority(other) >= MetricPriority(target))
            {
                target.Result = other.Result;
                target.WinCount = other.WinCount;
                target.Damage = other.Damage;
                target.Frags = other.Frags;
                target.BattleCount = other.BattleCount;
                target.Source = other.Source;
                target.Completeness = other.Completeness;
                target.StatusMessage = other.StatusMessage;
            }
            if (!string.IsNullOrWhiteSpace(other.ReplayHash))
            {
                target.ReplayHash = other.ReplayHash;
                target.ReplayVersion = other.ReplayVersion;
            }
            if (other.UpdatedAt > target.UpdatedAt) target.UpdatedAt = other.UpdatedAt;
        }

        private static async Task UpdateReconciledBattleAsync(SqliteConnection connection, SqliteTransaction transaction,
            BattleRecord battle, CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Battles SET SessionId=$session,StartedAt=$started,Server=$server,MapName=$map,
                    AccountId=$accountId,ShipName=$shipName,ShipType=$shipType,RosterSignature=$roster,
                    Result=$result,WinCount=$wins,Damage=$damage,Frags=$frags,BattleCount=$count,
                    Source=$source,Completeness=$complete,ReplayHash=$hash,ReplayVersion=$version,
                    StatusMessage=$status,UpdatedAt=$updated WHERE Id=$id
                """;
            AddBattleParameters(command, battle);
            command.Parameters.AddWithValue("$id", battle.Id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task ReconcileLegacyBattlesAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using (SqliteCommand version = connection.CreateCommand())
            {
                version.CommandText = "SELECT 1 FROM SchemaMigrations WHERE Version=5";
                if (await version.ExecuteScalarAsync(cancellationToken) != null) return;
            }
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            List<BattleRecord> battles = new();
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT * FROM Battles ORDER BY StartedAt,Id";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) battles.Add(ReadBattle(reader));
            }
            Dictionary<(string Account, string Ship), List<BattleRecord>> groups = new();
            foreach (BattleRecord battle in battles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = (battle.AccountName.ToLowerInvariant(), battle.ShipId);
                if (!groups.TryGetValue(key, out List<BattleRecord>? recent)) groups[key] = recent = new();
                recent.RemoveAll(x => battle.StartedAt - x.StartedAt > HistoryBattleIdentity.ReconnectWindow);
                BattleRecord[] matches = recent.Where(x => HistoryBattleIdentity.SameBattle(x, battle)).ToArray();
                if (matches.Length != 1) { recent.Add(battle); continue; }
                await MergeDuplicateBattleAsync(connection, transaction, matches[0], battle, cancellationToken);
            }
            await ExecuteAsync(connection, transaction, """
                DELETE FROM BattleSessions WHERE IsManual=0 AND NOT EXISTS(SELECT 1 FROM Battles WHERE SessionId=BattleSessions.Id);
                UPDATE BattleSessions SET
                    StartedAt=(SELECT MIN(StartedAt) FROM Battles WHERE SessionId=BattleSessions.Id),
                    EndedAt=(SELECT MAX(StartedAt) FROM Battles WHERE SessionId=BattleSessions.Id)
                WHERE EXISTS(SELECT 1 FROM Battles WHERE SessionId=BattleSessions.Id);
                INSERT INTO SchemaMigrations(Version,AppliedAt) VALUES(5,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private static async Task MergeDuplicateBattleAsync(SqliteConnection connection, SqliteTransaction transaction,
            BattleRecord target, BattleRecord duplicate, CancellationToken cancellationToken)
        {
            await using (SqliteCommand manual = connection.CreateCommand())
            {
                manual.Transaction = transaction;
                manual.CommandText = "SELECT Id FROM BattleSessions WHERE IsManual=1 AND Id IN ($target,$other) ORDER BY Id LIMIT 1";
                manual.Parameters.AddWithValue("$target", target.SessionId ?? (object)DBNull.Value);
                manual.Parameters.AddWithValue("$other", duplicate.SessionId ?? (object)DBNull.Value);
                object? session = await manual.ExecuteScalarAsync(cancellationToken);
                if (session != null) target.SessionId = Convert.ToInt64(session, CultureInfo.InvariantCulture);
            }
            MergeBattleValues(target, duplicate);
            await UpdateReconciledBattleAsync(connection, transaction, target, cancellationToken);
            await ExecuteAsync(connection, transaction, """
                INSERT OR IGNORE INTO BattlePlayers
                SELECT $keep,PlayerKey,AccountId,AccountName,Relation,ShipId,ShipName,ShipType,ShipTier,IsHidden,IsDataStale,
                    AccountBattles,AccountWinrate,AccountPr,ShipBattles,ShipWinrate,ShipPr FROM BattlePlayers WHERE BattleId=$remove;
                UPDATE ShipSnapshots SET BattleId=$keep WHERE BattleId=$remove;
                UPDATE ReplayFiles SET BattleId=$keep WHERE BattleId=$remove;
                INSERT INTO BattleAdvancedMetrics
                SELECT $keep,BattleDurationSeconds,Survived,SurvivalSeconds,PotentialDamage,DamageTaken,
                    SurvivalAvailability,PotentialDamageAvailability,DamageTakenAvailability,ParserSchemaVersion
                FROM BattleAdvancedMetrics WHERE BattleId=$remove
                ON CONFLICT(BattleId) DO UPDATE SET
                    BattleDurationSeconds=COALESCE(BattleAdvancedMetrics.BattleDurationSeconds,excluded.BattleDurationSeconds),
                    Survived=COALESCE(BattleAdvancedMetrics.Survived,excluded.Survived),
                    SurvivalSeconds=COALESCE(BattleAdvancedMetrics.SurvivalSeconds,excluded.SurvivalSeconds),
                    SurvivalAvailability=CASE WHEN BattleAdvancedMetrics.Survived IS NULL AND BattleAdvancedMetrics.SurvivalSeconds IS NULL
                        THEN excluded.SurvivalAvailability ELSE BattleAdvancedMetrics.SurvivalAvailability END,
                    PotentialDamageAvailability=CASE WHEN BattleAdvancedMetrics.PotentialDamage IS NULL
                        THEN excluded.PotentialDamageAvailability ELSE BattleAdvancedMetrics.PotentialDamageAvailability END,
                    DamageTakenAvailability=CASE WHEN BattleAdvancedMetrics.DamageTaken IS NULL
                        THEN excluded.DamageTakenAvailability ELSE BattleAdvancedMetrics.DamageTakenAvailability END,
                    PotentialDamage=COALESCE(BattleAdvancedMetrics.PotentialDamage,excluded.PotentialDamage),
                    DamageTaken=COALESCE(BattleAdvancedMetrics.DamageTaken,excluded.DamageTaken),
                    ParserSchemaVersion=CASE WHEN BattleAdvancedMetrics.ParserSchemaVersion=''
                        THEN excluded.ParserSchemaVersion ELSE BattleAdvancedMetrics.ParserSchemaVersion END;
                INSERT OR IGNORE INTO BattleDamageBreakdowns
                SELECT $keep,Direction,RawTypeCode,Category,Damage,Availability FROM BattleDamageBreakdowns WHERE BattleId=$remove;
                INSERT INTO PendingResultChecks
                SELECT $keep,Attempt,NextAttemptAt,LastError FROM PendingResultChecks WHERE BattleId=$remove
                ON CONFLICT(BattleId) DO UPDATE SET NextAttemptAt=MIN(PendingResultChecks.NextAttemptAt,excluded.NextAttemptAt);
                """, cancellationToken, ("$keep", target.Id), ("$remove", duplicate.Id));
            await MergeReviewsAsync(connection, transaction, target.Id, duplicate.Id, cancellationToken);
            await MergeReviewsAsync(connection, transaction, target.Id, duplicate.Id, cancellationToken, "BattleReviewDrafts");
            await ExecuteAsync(connection, transaction, "DELETE FROM Battles WHERE Id=$remove", cancellationToken, ("$remove", duplicate.Id));
            if (target.Completeness == BattleCompleteness.Complete)
                await ExecuteAsync(connection, transaction, "DELETE FROM PendingResultChecks WHERE BattleId=$id", cancellationToken, ("$id", target.Id));
        }

        private static async Task MergeReviewsAsync(SqliteConnection connection, SqliteTransaction transaction,
            long target, long other, CancellationToken cancellationToken, string tableName = "BattleReviews")
        {
            List<BattleReview> reviews = new();
            await using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"SELECT * FROM {tableName} WHERE BattleId IN ($target,$other) ORDER BY BattleId=$target DESC";
                command.Parameters.AddWithValue("$target", target);
                command.Parameters.AddWithValue("$other", other);
                await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) reviews.Add(new BattleReview
                {
                    IsFavorite = reader.GetInt32(reader.GetOrdinal("IsFavorite")) != 0,
                    Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("TagsJson"))) ?? new(),
                    Note = reader.GetString(reader.GetOrdinal("Note")), UpdatedAt = ParseDate(reader.GetString(reader.GetOrdinal("UpdatedAt")))
                });
            }
            if (reviews.Count == 0) return;
            await ExecuteAsync(connection, transaction, $"""
                INSERT INTO {tableName}(BattleId,IsFavorite,TagsJson,Note,UpdatedAt) VALUES($id,$favorite,$tags,$note,$updated)
                ON CONFLICT(BattleId) DO UPDATE SET IsFavorite=excluded.IsFavorite,TagsJson=excluded.TagsJson,
                    Note=excluded.Note,UpdatedAt=excluded.UpdatedAt;
                """, cancellationToken, ("$id", target), ("$favorite", reviews.Any(x => x.IsFavorite) ? 1 : 0),
                ("$tags", JsonSerializer.Serialize(reviews.SelectMany(x => x.Tags).Distinct(StringComparer.Ordinal))),
                ("$note", string.Join(Environment.NewLine, reviews.Select(x => x.Note).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))),
                ("$updated", reviews.Max(x => x.UpdatedAt).UtcDateTime.ToString("O")));
        }
    }
}
