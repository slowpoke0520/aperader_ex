using Microsoft.Data.Sqlite;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed class HistoryDatabaseMaintenance
    {
        private const int CurrentSchemaVersion = 4;
        private readonly string databasePath;
        private readonly string connectionString;

        internal HistoryDatabaseMaintenance(string databasePath, string connectionString)
        {
            this.databasePath = databasePath;
            this.connectionString = connectionString;
        }

        internal async Task BackupBeforeMigrationAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(databasePath)) return;
            try
            {
                await using SqliteConnection connection = new(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT COALESCE(MAX(Version),0) FROM SchemaMigrations";
                int version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (version >= CurrentSchemaVersion) return;
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await command.ExecuteNonQueryAsync(cancellationToken);
                await connection.CloseAsync();
                SqliteConnection.ClearAllPools();
                string backup = $"{databasePath}.pre-v{CurrentSchemaVersion}-{DateTimeOffset.Now:yyyyMMddHHmmss}.bak";
                File.Copy(databasePath, backup, false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                // A database created before SchemaMigrations existed will be upgraded normally.
            }
        }

        internal void IsolateCorruptDatabase()
        {
            SqliteConnection.ClearAllPools();
            if (!File.Exists(databasePath)) return;
            string backup = $"{databasePath}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmss}";
            File.Move(databasePath, backup, true);
            TryMoveSidecar(databasePath + "-wal", backup + "-wal");
            TryMoveSidecar(databasePath + "-shm", backup + "-shm");
        }

        private static void TryMoveSidecar(string source, string destination)
        {
            if (File.Exists(source)) File.Move(source, destination, true);
        }
    }
}
