using ApeRadar.History;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace ApeRadar.Tests;

public sealed class HistoryReadBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SqliteCountAndPages_MatchFiltersOnTenThousandRecords()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ApeRadar.ReadBenchmark.{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "history.db");
        try
        {
            SqliteHistoryRepository repository = new(path);
            await repository.InitializeAsync();
            await using (SqliteConnection connection = new($"Data Source={path}"))
            {
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO Battles(BattleKey,StartedAt,Server,Mode,MapName,AccountId,AccountName,ShipId,ShipName,Result,WinCount,Damage,Frags,BattleCount,Source,Completeness,UpdatedAt)
                    VALUES($key,$time,'ASIA','RandomBattle','Map','1','Tester',$ship,'Ship',0,1,90000,1,1,$source,$complete,$time)
                    """;
                var key = command.Parameters.Add("$key", SqliteType.Text);
                var time = command.Parameters.Add("$time", SqliteType.Text);
                var ship = command.Parameters.Add("$ship", SqliteType.Text);
                command.Parameters.AddWithValue("$source", (int)BattleMetricSource.ApiExact);
                command.Parameters.AddWithValue("$complete", (int)BattleCompleteness.Complete);
                for (int i = 0; i < 10_000; i++)
                {
                    key.Value = $"record-{i}";
                    time.Value = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i).UtcDateTime.ToString("O");
                    ship.Value = i % 2 == 0 ? "101" : "202";
                    await command.ExecuteNonQueryAsync();
                }
                transaction.Commit();
            }
            HistoryQuery filtered = new() { Server = "ASIA", AccountId = "1", ShipId = "101", Limit = 100, Offset = 100, Descending = true };
            Assert.Equal(5_000, await repository.CountBattlesAsync(filtered)); // limit/offset do not affect count
            Assert.Equal(0, await repository.CountBattlesAsync(new HistoryQuery { Server = "EU" }));
            Assert.Equal(50, await repository.CountBattlesAsync(new HistoryQuery
            {
                Server = "ASIA", AccountId = "1", ShipId = "101",
                From = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                To = new DateTimeOffset(2026, 1, 1, 1, 40, 0, TimeSpan.Zero)
            }));
            Stopwatch timer = Stopwatch.StartNew();
            HistoryAnalysisService analysis = new();
            HistoryAnalysisLoader loader = new(repository, analysis, new SessionAnalysisService(analysis));
            var snapshot = await loader.LoadAsync(new HistoryQuery { Server = "ASIA", AccountId = "1" }, "Damage", 20, false, default);
            long initial = timer.ElapsedMilliseconds;
            Assert.Equal(10_000, snapshot.Points.Count);
            timer.Restart();
            for (int page = 0; page < 10; page++)
            {
                var rows = await repository.GetBattlesAsync(new HistoryQuery
                { Server = "ASIA", AccountId = "1", ShipId = "101", Limit = 100, Offset = page * 100, Descending = true });
                Assert.Equal(100, rows.Count);
                Assert.All(rows, row => Assert.Equal("101", row.ShipId));
                Assert.Equal($"record-{9998 - page * 200}", rows[0].BattleKey);
            }
            output.WriteLine($"SQLite 10,000 records: full query + damage trend (window 20) {initial} ms; 10 filtered pages {timer.ElapsedMilliseconds} ms.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
