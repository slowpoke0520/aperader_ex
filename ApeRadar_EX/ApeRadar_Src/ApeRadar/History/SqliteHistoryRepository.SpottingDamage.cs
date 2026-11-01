using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed partial class SqliteHistoryRepository
    {
        private static async Task MigrateSpottingDamageAsync(SqliteConnection connection, CancellationToken token)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
            await using (SqliteCommand check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version=7";
                if (Convert.ToInt64(await check.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0)
                    return;
            }

            if (!await ColumnExistsAsync(connection, "BattleAdvancedMetrics", "SpottingDamage", token, transaction))
                await ExecuteAsync(connection, transaction, "ALTER TABLE BattleAdvancedMetrics ADD COLUMN SpottingDamage INTEGER NULL", token);
            if (!await ColumnExistsAsync(connection, "BattleAdvancedMetrics", "SpottingDamageAvailability", token, transaction))
                await ExecuteAsync(connection, transaction,
                    "ALTER TABLE BattleAdvancedMetrics ADD COLUMN SpottingDamageAvailability INTEGER NOT NULL DEFAULT 0", token);

            // Parser schemas 2 and 3 stored receiveDamageStat category 2 (spotting)
            // as damage taken. Preserve their valid observed values under the right
            // meaning; unknown provenance cannot be relabelled safely. The pre-v7
            // database backup retains every original value and breakdown.
            await ExecuteAsync(connection, transaction, """
                UPDATE BattleAdvancedMetrics
                SET SpottingDamage=DamageTaken, SpottingDamageAvailability=1
                WHERE ParserSchemaVersion IN ('2','3') AND SpottingDamage IS NULL
                    AND DamageTaken IS NOT NULL AND DamageTaken>=0 AND DamageTakenAvailability IN (1,2);

                INSERT OR IGNORE INTO BattleDamageBreakdowns(BattleId,Direction,RawTypeCode,Category,Damage,Availability)
                SELECT b.BattleId,2,b.RawTypeCode,b.Category,b.Damage,b.Availability
                FROM BattleDamageBreakdowns b JOIN BattleAdvancedMetrics m ON m.BattleId=b.BattleId
                WHERE b.Direction=1 AND m.ParserSchemaVersion IN ('2','3')
                    AND b.Damage>=0 AND b.Availability IN (1,2)
                    AND m.DamageTaken IS NOT NULL AND m.DamageTaken>=0 AND m.DamageTakenAvailability IN (1,2);

                DELETE FROM BattleDamageBreakdowns
                WHERE Direction=1 AND NOT EXISTS(
                    SELECT 1 FROM BattleAdvancedMetrics m WHERE m.BattleId=BattleDamageBreakdowns.BattleId
                        AND m.ParserSchemaVersion<>'' AND m.ParserSchemaVersion NOT GLOB '*[^0-9]*'
                        AND CAST(m.ParserSchemaVersion AS INTEGER)>=4);

                UPDATE BattleAdvancedMetrics SET DamageTaken=NULL,DamageTakenAvailability=0
                WHERE NOT (ParserSchemaVersion<>'' AND ParserSchemaVersion NOT GLOB '*[^0-9]*'
                    AND CAST(ParserSchemaVersion AS INTEGER)>=4);

                INSERT INTO SchemaMigrations(Version,AppliedAt) VALUES(7,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                """, token);
            await transaction.CommitAsync(token);
        }
    }
}
