using ApeRadar.History;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace ApeRadar.Tests;

public sealed class ReplayParserAdapterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ApeRadar.ReplayTests.{Guid.NewGuid():N}");
    private readonly ITestOutputHelper output;

    public ReplayParserAdapterTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task InvalidPayload_StillReturnsSafeHeaderMetadataForApiFallback()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "sample.wowsreplay");
        const string json = """
            {"matchGroup":"pvp","gameType":"RandomBattle","playerName":"Tester","dateTime":"03.09.2026 12:34:56","mapName":"ocean","clientVersionFromExe":"15.8.0","vehicles":[{"name":"Tester","shipId":101}]}
            """;
        byte[] header = Encoding.UTF8.GetBytes(json);
        await using (FileStream stream = File.Create(path))
        {
            await stream.WriteAsync(new byte[8]);
            await stream.WriteAsync(BitConverter.GetBytes(header.Length));
            await stream.WriteAsync(header);
            await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });
        }

        using NodsoftReplayParserAdapter parser = new();
        ReplayParseResult result = await parser.ParseAsync(path);

        Assert.Equal("pvp", result.Mode);
        Assert.NotEqual("NotRandomBattle", result.ErrorCode);
        Assert.Equal("Tester", result.AccountName);
        Assert.Equal("101", result.ShipId);
        Assert.Equal("ocean", result.MapName);
        Assert.NotEmpty(result.FileHash);
        Assert.NotEqual(ReplayParseStatus.Parsed, result.Status);
    }

    [Fact]
    public async Task ReconnectSegments_WithSameRosterUseSameFallbackBattleKey()
    {
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "first.wowsreplay");
        string second = Path.Combine(directory, "second.wowsreplay");
        const string firstHeader = """
            {"matchGroup":"pvp","playerName":"Tester","dateTime":"11.09.2026 12:00:00","mapName":"ocean","vehicles":[{"name":"Tester","shipId":101},{"name":"Other","shipId":202}]}
            """;
        const string secondHeader = """
            {"matchGroup":"pvp","playerName":"Tester","dateTime":"11.09.2026 12:08:00","mapName":"ocean","vehicles":[{"name":"Other","shipId":202},{"name":"Tester","shipId":101}]}
            """;
        await WriteReplayStubAsync(first, firstHeader);
        await WriteReplayStubAsync(second, secondHeader);

        using NodsoftReplayParserAdapter parser = new();
        ReplayParseResult firstResult = await parser.ParseAsync(first);
        ReplayParseResult secondResult = await parser.ParseAsync(second);

        Assert.NotEqual(firstResult.FileHash, secondResult.FileHash);
        Assert.Equal(firstResult.RosterSignature, secondResult.RosterSignature);
        Assert.Equal(firstResult.BattleKey, secondResult.BattleKey);
    }

    [Fact]
    public void DamageStats_KeepEnemyPotentialAndExperimentalReceivedDamageSeparate()
    {
        BattleHistoryReplay replay = new();
        byte[] enemyDamage = Convert.FromHexString("80027D71014B024B008671025D7103284B03474095A8000000000065732E");
        byte[] potentialDamage = Convert.FromHexString("80027D71014B204B038671025D7103284B054740F136400000000065732E");
        byte[] receivedDamage = Convert.FromHexString("80027D71014B204B028671025D7103284B054740F136400000000065732E");

        BattleHistoryReplayController.ApplyDamageStats(replay, enemyDamage);
        BattleHistoryReplayController.ApplyDamageStats(replay, potentialDamage);
        BattleHistoryReplayController.ApplyDamageStats(replay, receivedDamage);

        Assert.True(replay.DamageStatsSeen);
        Assert.Empty(replay.DamageStatsError);
        Assert.Equal(1386, NodsoftReplayParserAdapter.FindDamage(replay));
        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay);
        Assert.NotNull(advanced.PotentialDamage);
        Assert.NotNull(advanced.DamageTaken);
        Assert.Equal(MetricAvailability.Stable, advanced.PotentialDamageAvailability);
        Assert.Equal(MetricAvailability.Experimental, advanced.DamageTakenAvailability);
    }

    [Theory]
    [InlineData(0, 0, (int)BattleResult.Win)]
    [InlineData(0, 1, (int)BattleResult.Loss)]
    [InlineData(1, 255, (int)BattleResult.Draw)]
    public void BattleResult_UsesPlayerAndWinnerTeams(int playerTeam, int winnerTeam, int expected)
    {
        Assert.Equal((BattleResult)expected, NodsoftReplayParserAdapter.InterpretBattleResult(playerTeam, winnerTeam));
    }

    [Fact]
    public void EarlyExitAfterDeath_KeepsDamageAndFragsRecordedBeforeExit()
    {
        const uint ownShipId = 101;
        BattleHistoryReplay replay = new() { DamageStatsSeen = true };
        replay.EnemyDamageByType[1] = 75_500;
        replay.VehicleDeaths.Add(new VehicleDeathEvent(202, ownShipId, 0, 180));
        replay.VehicleDeaths.Add(new VehicleDeathEvent(ownShipId, 303, 0, 420));

        bool found = NodsoftReplayParserAdapter.TryFindEarlyExitMetrics(replay, ownShipId, out long? damage, out double? frags);

        Assert.True(found);
        Assert.Equal(75_500, damage);
        Assert.Equal(1, frags);
    }

    [Fact]
    public void DisconnectWhileAlive_DoesNotTreatPartialMetricsAsFinal()
    {
        BattleHistoryReplay replay = new() { DamageStatsSeen = true };
        replay.EnemyDamageByType[1] = 12_000;

        bool found = NodsoftReplayParserAdapter.TryFindEarlyExitMetrics(replay, 101, out long? damage, out double? frags);

        Assert.False(found);
        Assert.Null(damage);
        Assert.Null(frags);
    }

    [Fact]
    public async Task LocalReplayDirectory_WhenConfigured_ParsesWithoutLeakingFixturesIntoTheRepository()
    {
        string? replayDirectory = Environment.GetEnvironmentVariable("APERADAR_REPLAY_TEST_DIR");
        if (string.IsNullOrWhiteSpace(replayDirectory) || !Directory.Exists(replayDirectory)) return;
        string[] files = Directory.GetFiles(replayDirectory, "*.wowsreplay", SearchOption.AllDirectories).OrderBy(x => x).Take(30).ToArray();
        Assert.NotEmpty(files);
        using NodsoftReplayParserAdapter parser = new();
        List<ReplayParseResult> randomBattles = new();
        foreach (string file in files)
        {
            ReplayParseResult result = await parser.ParseAsync(file);
            string types = string.Join(',', result.DamageBreakdowns.Where(x => x.Direction == DamageDirection.Dealt).Select(x => $"{x.RawTypeCode}:{x.Damage}"));
            output.WriteLine($"{Path.GetFileName(file)} | {result.Status} | {result.ErrorCode} | damage={result.Damage} | frags={result.Frags} | survival={result.AdvancedMetrics.Survived} | potential={result.AdvancedMetrics.PotentialDamage} | taken={result.AdvancedMetrics.DamageTaken} | dealtTypes={types} | error={result.ErrorMessage}");
            if (result.ErrorCode != "NotRandomBattle") randomBattles.Add(result);
        }
        Assert.NotEmpty(randomBattles);
        Assert.Contains(randomBattles, x => x.Status is ReplayParseStatus.Parsed or ReplayParseStatus.Partial or ReplayParseStatus.Unsupported);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static async Task WriteReplayStubAsync(string path, string json)
    {
        byte[] header = Encoding.UTF8.GetBytes(json);
        await using FileStream stream = File.Create(path);
        await stream.WriteAsync(new byte[8]);
        await stream.WriteAsync(BitConverter.GetBytes(header.Length));
        await stream.WriteAsync(header);
        await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });
    }
}
