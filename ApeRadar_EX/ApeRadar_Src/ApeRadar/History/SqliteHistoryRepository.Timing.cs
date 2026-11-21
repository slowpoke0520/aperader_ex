using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed partial class SqliteHistoryRepository
    {
        private static async Task MigrateReplayTimingAsync(SqliteConnection connection, CancellationToken token)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
            await using (SqliteCommand check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version=8";
                if (Convert.ToInt64(await check.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0) return;
            }
            if (!await ColumnExistsAsync(connection, "BattleAdvancedMetrics", "BattleDurationSource", token, transaction))
                await ExecuteAsync(connection, transaction,
                    "ALTER TABLE BattleAdvancedMetrics ADD COLUMN BattleDurationSource INTEGER NOT NULL DEFAULT 0", token);
            if (!await ColumnExistsAsync(connection, "BattleAdvancedMetrics", "SurvivalTimeSource", token, transaction))
                await ExecuteAsync(connection, transaction,
                    "ALTER TABLE BattleAdvancedMetrics ADD COLUMN SurvivalTimeSource INTEGER NOT NULL DEFAULT 0", token);

            // Schemas 5/6 used these fields exclusively from validated settlement
            // data when the battle is ReplayExact. Earlier/event-derived seconds
            // are packet-clock values with an unproven origin. The pre-v8 backup
            // keeps those originals; do not relabel them as elapsed battle time.
            await ExecuteAsync(connection, transaction, """
                UPDATE BattleAdvancedMetrics SET
                    BattleDurationSource=CASE WHEN BattleDurationSeconds>0 AND BattleDurationSeconds<=3600
                        AND ParserSchemaVersion IN ('5','6') AND EXISTS(
                            SELECT 1 FROM Battles b WHERE b.Id=BattleId AND b.Source=0 AND b.Completeness=0
                                AND b.BattleCount=1 AND b.Damage>=0 AND b.Frags>=0 AND b.Result IN (0,1,2))
                        THEN 1 ELSE 0 END,
                    SurvivalTimeSource=CASE WHEN SurvivalSeconds>=0 AND SurvivalSeconds<=3600
                        AND Survived IN (0,1) AND SurvivalAvailability IN (1,2)
                        AND (BattleDurationSeconds IS NULL OR
                            (BattleDurationSeconds>0 AND BattleDurationSeconds<=3600 AND SurvivalSeconds<=BattleDurationSeconds+1))
                        AND ParserSchemaVersion IN ('5','6') AND EXISTS(
                            SELECT 1 FROM Battles b WHERE b.Id=BattleId AND b.Source=0 AND b.Completeness=0
                                AND b.BattleCount=1 AND b.Damage>=0 AND b.Frags>=0 AND b.Result IN (0,1,2))
                        THEN 1 ELSE 0 END;

                UPDATE BattleAdvancedMetrics SET
                    BattleDurationSeconds=CASE WHEN BattleDurationSource=1 THEN BattleDurationSeconds ELSE NULL END,
                    SurvivalSeconds=CASE WHEN SurvivalTimeSource=1 THEN SurvivalSeconds ELSE NULL END,
                    SurvivalAvailability=CASE WHEN Survived=0 OR (ParserSchemaVersion IN ('5','6') AND EXISTS(
                            SELECT 1 FROM Battles b WHERE b.Id=BattleId AND b.Source=0 AND b.Completeness=0
                                AND b.BattleCount=1 AND b.Damage>=0 AND b.Frags>=0 AND b.Result IN (0,1,2)))
                        THEN SurvivalAvailability ELSE 0 END,
                    Survived=CASE WHEN Survived=0 OR (ParserSchemaVersion IN ('5','6') AND EXISTS(
                            SELECT 1 FROM Battles b WHERE b.Id=BattleId AND b.Source=0 AND b.Completeness=0
                                AND b.BattleCount=1 AND b.Damage>=0 AND b.Frags>=0 AND b.Result IN (0,1,2)))
                        THEN Survived ELSE NULL END;

                INSERT INTO SchemaMigrations(Version,AppliedAt) VALUES(8,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """, token);
            await transaction.CommitAsync(token);
        }

        private static async Task MigrateReplayOwnershipAsync(SqliteConnection connection, CancellationToken token)
        {
            await using SqliteTransaction transaction = connection.BeginTransaction();
            await using SqliteCommand check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM SchemaMigrations WHERE Version=9";
            if (await check.ExecuteScalarAsync(token) != null) return;
            if (!await ColumnExistsAsync(connection, "BattleAdvancedMetrics", "TimingReplayHash", token, transaction))
                await ExecuteAsync(connection, transaction,
                    "ALTER TABLE BattleAdvancedMetrics ADD COLUMN TimingReplayHash TEXT NOT NULL DEFAULT ''", token);

            // A single linked file is unambiguous. With several files the old DB
            // did not record which one supplied the timing, so leave ownership open.
            await ExecuteAsync(connection, transaction, """
                UPDATE BattleAdvancedMetrics AS m SET TimingReplayHash=r.FileHash
                FROM (SELECT BattleId, MIN(FileHash) AS FileHash FROM ReplayFiles
                      GROUP BY BattleId HAVING COUNT(*)=1) AS r
                WHERE m.BattleId=r.BattleId AND m.TimingReplayHash='';
                UPDATE Battles SET Completeness=$partial
                WHERE Source=$derived AND Completeness=$complete;
                INSERT INTO SchemaMigrations(Version,AppliedAt) VALUES(9,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """, token, ("$partial", (int)BattleCompleteness.Partial), ("$derived", (int)BattleMetricSource.ReplayDerived),
                ("$complete", (int)BattleCompleteness.Complete));
            await transaction.CommitAsync(token);
        }

        private static async Task<(bool Replace, string Schema)> SelectTimingSnapshotAsync(SqliteConnection connection,
            SqliteTransaction transaction, long targetId, long otherId, CancellationToken token)
        {
            BattleAdvancedMetrics? target = null, other = null;
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT * FROM BattleAdvancedMetrics WHERE BattleId IN ($target,$other)";
            command.Parameters.AddWithValue("$target", targetId);
            command.Parameters.AddWithValue("$other", otherId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                BattleAdvancedMetrics metrics = ReadAdvancedMetrics(reader);
                if (metrics.BattleId == targetId) target = metrics; else other = metrics;
            }
            int targetSchema = NumericSchema(target?.ParserSchemaVersion);
            int otherSchema = NumericSchema(other?.ParserSchemaVersion);
            string schema = otherSchema > targetSchema || string.IsNullOrWhiteSpace(target?.ParserSchemaVersion)
                ? other?.ParserSchemaVersion ?? "" : target!.ParserSchemaVersion;
            // A schema-7 invalidation must never be refilled from an uncalibrated
            // legacy recording. Within one generation prefer a complete timing
            // snapshot from the stronger source; never splice its individual fields.
            bool replace = target == null || (other != null && PreferIncomingTiming(target, other));
            return (replace, schema);
        }

        private static bool PreferIncomingTiming(BattleAdvancedMetrics stored, BattleAdvancedMetrics incoming)
        {
            int storedSchema = NumericSchema(stored.ParserSchemaVersion), incomingSchema = NumericSchema(incoming.ParserSchemaVersion);
            if ((incomingSchema >= 7) != (storedSchema >= 7)) return incomingSchema >= 7;
            return TimingRank(incoming) > TimingRank(stored);
        }

        private static int NumericSchema(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int schema) ? schema : 0;

        private static int TimingRank(BattleAdvancedMetrics metrics)
        {
            static int SourceRank(ReplayTimingSource source) => source switch
            {
                ReplayTimingSource.BattleResults => 20,
                ReplayTimingSource.BattleClock => 10,
                _ => 0
            };
            int rank = Math.Max(SourceRank(metrics.BattleDurationSource), SourceRank(metrics.SurvivalTimeSource));
            return rank * 10 + (metrics.BattleDurationSeconds.HasValue ? 2 : 0) +
                (metrics.SurvivalSeconds.HasValue ? 2 : 0) + (metrics.Survived.HasValue ? 1 : 0);
        }
    }
}
