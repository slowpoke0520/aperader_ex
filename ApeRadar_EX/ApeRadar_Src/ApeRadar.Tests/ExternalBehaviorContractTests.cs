using System.Reflection;
using System.Text;
using ApeRadar.Models;
using ApeRadar.Services;
using ApeRadar.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ApeRadar.Tests;

public sealed class ExternalBehaviorContractTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"ApeRadar.ContractTests.{Guid.NewGuid():N}");

    public ExternalBehaviorContractTests() => Directory.CreateDirectory(root);

    [Fact]
    public void ArenaInfo_PlainJsonPreservesCurrentAndUnknownFields()
    {
        string path = Path.Combine(root, "tempArenaInfo.json");
        File.WriteAllText(path, """
            {
              "arenaUniqueId": "18446744073709551615",
              "dateTime": "28.09.2026 21:45:00",
              "matchGroup": "random",
              "futureField": { "enabled": true },
              "vehicles": [
                { "id": 101, "name": "Tester", "relation": 0, "shipId": "4179601392" }
              ]
            }
            """, new UTF8Encoding(false));

        JObject arena = FileUtils.ReadTempArenaInfoFile(path);

        Assert.Equal("18446744073709551615", arena["arenaUniqueId"]?.Value<string>());
        Assert.Equal("random", arena["matchGroup"]?.Value<string>());
        Assert.True(arena["futureField"]?["enabled"]?.Value<bool>());
        Assert.Equal("Tester", arena["vehicles"]?[0]?["name"]?.Value<string>());
    }

    [Fact]
    public void ArenaInfo_LegacyWrappedPayloadRemainsReadable()
    {
        const string json = "{\"matchGroup\":\"random\",\"vehicles\":[{\"id\":101,\"name\":\"Tester\",\"relation\":0,\"shipId\":\"101\"}]}";
        string path = Path.Combine(root, "tempArenaInfo-wrapped.json");
        using (FileStream stream = File.Create(path))
        using (BinaryWriter writer = new(stream, Encoding.UTF8))
        {
            writer.Write(new byte[] { 0x12, 0x32, 0x34, 0x11 });
            writer.Write(0);
            writer.Write(json.Length);
            writer.Write(Encoding.UTF8.GetBytes(json));
        }

        JObject arena = FileUtils.ReadTempArenaInfoFile(path);

        Assert.Equal("random", arena["matchGroup"]?.Value<string>());
        Assert.Equal("Tester", Assert.Single(arena["vehicles"]!).Value<string>("name"));
    }

    [Fact]
    public void ArenaInfo_MetadataFirstFlowReturnsTheCompleteRosterBeforeStatisticsArrive()
    {
        string path = Path.Combine(root, "tempArenaInfo-normal-flow.json");
        File.WriteAllText(path, """
            {
              "matchGroup": "random",
              "vehicles": [
                { "id": 101, "name": "Self", "relation": 0, "shipId": "101" },
                { "id": 102, "name": "Ally", "relation": 1, "shipId": "102" },
                { "id": 103, "name": "Enemy", "relation": 2, "shipId": "103" },
                { "id": 2, "name": ":Bot:", "relation": 2, "shipId": "2" }
              ]
            }
            """, new UTF8Encoding(false));
        JObject arena = FileUtils.ReadTempArenaInfoFile(path);
        BattleRosterRequest request = new(arena, arena["vehicles"]!.Count(), Server.ASIA, Server.EU, true,
            APIType.VORTEX, false, null, null);

        IReadOnlyList<Player> metadata = new BattleRosterCoordinator().CreateMetadataRoster(request);

        Assert.Equal(new[] { "Self", "Ally", "Enemy" }, metadata.Select(player => player.Name));
        Assert.DoesNotContain(metadata, player => player.Name == ":Bot:");
        Assert.All(metadata, player =>
        {
            Assert.Equal("-1", player.ID);
            Assert.Equal(-1, player.AccountWinrate);
            Assert.False(player.IsDataFetchFailed);
        });
        Assert.Equal(Server.EU, metadata.Single(player => player.Name == "Enemy").Server);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{broken-json")]
    public void ArenaInfo_InvalidOrMalformedInputUsesTheStableFileError(string content)
    {
        string path = Path.Combine(root, $"invalid-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content, new UTF8Encoding(false));

        FileFormatException error = Assert.Throws<FileFormatException>(() => FileUtils.ReadTempArenaInfoFile(path));

        Assert.Equal("FileFormatIncorrect", error.Message);
    }

    [Fact]
    public void CachedStatistics_RoundTripPreservesVisibleValuesButNotUserAnnotationsOrIdentity()
    {
        Player source = CreateStatisticsSource();
        PlayerDataSnapshot snapshot = PlayerDataSnapshot.FromPlayer(source);
        string persisted = JsonConvert.SerializeObject(snapshot);
        PlayerDataSnapshot restored = Assert.IsType<PlayerDataSnapshot>(JsonConvert.DeserializeObject<PlayerDataSnapshot>(persisted));
        Player target = new("Local identity", Server.EU, "1", "local-ship")
        {
            ID = "local-id",
            ShipName = "Local ship",
            ShipType = "Cruiser",
            ShipTier = 8,
            Note = "keep this note",
            WatchStatus = WatchStatus.POSITIVE,
            IsCustomMarked = true,
            RecentEncounterCount = 3,
            IsFixedTeammate = true
        };

        restored.ApplyTo(target);

        Assert.Equal("Local identity", target.Name);
        Assert.Equal("local-id", target.ID);
        Assert.Equal(Server.EU, target.Server);
        Assert.Equal("1", target.Relation);
        Assert.Equal("local-ship", target.ShipID);
        Assert.Equal("Local ship", target.ShipName);
        Assert.Equal("Cruiser", target.ShipType);
        Assert.Equal(8, target.ShipTier);
        Assert.Equal("keep this note", target.Note);
        Assert.Equal(WatchStatus.POSITIVE, target.WatchStatus);
        Assert.True(target.IsCustomMarked);
        Assert.Equal(3, target.RecentEncounterCount);
        Assert.True(target.IsFixedTeammate);
        Assert.Equal(source.Battles, target.Battles);
        Assert.Equal(source.AccountWinrate, target.AccountWinrate);
        Assert.Equal(source.PR, target.PR);
        Assert.Equal(source.TierBattles, target.TierBattles);
        Assert.Equal(source.TierPR, target.TierPR);
        Assert.Equal(source.ShipBattles, target.ShipBattles);
        Assert.Equal(source.ShipAvgDmgPerBattle, target.ShipAvgDmgPerBattle);
        Assert.Equal(source.ShipPR, target.ShipPR);
        Assert.Equal(source.WeightedWinrate, target.WeightedWinrate);
    }

    [Fact]
    public void LegacyCachedStatisticsWithoutTierFieldsRemainUnavailableInsteadOfBecomingZero()
    {
        const string legacyJson = """
            {
              "FetchedAt": "2026-09-28T12:00:00+00:00",
              "Battles": 4321,
              "AccountWinrate": 0.543,
              "PR": 1450,
              "ShipBattles": 87,
              "ShipWinrate": 0.517,
              "ShipPR": 1320
            }
            """;

        PlayerDataSnapshot snapshot = Assert.IsType<PlayerDataSnapshot>(JsonConvert.DeserializeObject<PlayerDataSnapshot>(legacyJson));
        Player player = new("Legacy", Server.ASIA, "1", "101");
        snapshot.ApplyTo(player);

        Assert.Equal(4321, player.Battles);
        Assert.Equal(87, player.ShipBattles);
        Assert.Equal(-1, player.TierBattles);
        Assert.Equal(-1, player.TierWinrate);
        Assert.Equal(-1, player.TierPR);
        Assert.False(player.HasTierReference);
    }

    [Fact]
    public void CachedStatistics_KeepTheExistingJsonShapeAndDoNotPersistLocalState()
    {
        Player player = CreateStatisticsSource();
        JObject json = JObject.Parse(JsonConvert.SerializeObject(PlayerDataSnapshot.FromPlayer(player)));

        string[] expectedFields = """
            FetchedAt LastAccessedAt Wins Wins_Solo Wins_Div2 Wins_Div3 Battles Battles_Solo
            Battles_Div2 Battles_Div3 TotalExp TotalExp_Solo TotalExp_Div2 TotalExp_Div3 AvgExpPerBattle AvgExpPerBattle_Solo
            AvgExpPerBattle_Div2 AvgExpPerBattle_Div3 AccountWinrate AccountWinrate_Solo AccountWinrate_Div2 AccountWinrate_Div3 ClanID ClanTag
            IsHidden Karma PR ShipPR TierWins TierBattles TierWinrate TierPR
            IsTierSampleSmall TierReferenceMin TierReferenceMax TierReferenceBattles TierReferenceWinrate TierReferencePR HasTierReference MostPlayedTier
            MostPlayedTierBattles MostPlayedTierShare IsLowTierBiased ShipWins ShipWins_Solo ShipWins_Div2 ShipWins_Div3 ShipBattles
            ShipBattles_Solo ShipBattles_Div2 ShipBattles_Div3 ShipTotalDmg ShipTotalDmg_Solo ShipTotalDmg_Div2 ShipTotalDmg_Div3 ShipAvgDmgPerBattle
            ShipAvgDmgPerBattle_Solo ShipAvgDmgPerBattle_Div2 ShipAvgDmgPerBattle_Div3 ShipTotalExp ShipTotalExp_Solo ShipTotalExp_Div2 ShipTotalExp_Div3 ShipAvgExpPerBattle
            ShipAvgExpPerBattle_Solo ShipAvgExpPerBattle_Div2 ShipAvgExpPerBattle_Div3 ShipWinrate ShipWinrate_Solo ShipWinrate_Div2 ShipWinrate_Div3 WeightedWinrate
            """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expectedFields.OrderBy(field => field),
            json.Properties().Select(property => property.Name).OrderBy(field => field));
        Assert.Equal(player.Battles, json.Value<double>("Battles"));
        Assert.Equal(player.TierBattles, json.Value<double>("TierBattles"));
        Assert.Equal(player.ShipBattles, json.Value<double>("ShipBattles"));
        Assert.Equal(player.WeightedWinrate, json.Value<double>("WeightedWinrate"));
        Assert.DoesNotContain("Name", json.Properties().Select(property => property.Name));
        Assert.DoesNotContain("ID", json.Properties().Select(property => property.Name));
        Assert.DoesNotContain("Note", json.Properties().Select(property => property.Name));
        Assert.DoesNotContain("WatchStatus", json.Properties().Select(property => property.Name));
        Assert.DoesNotContain("IsDataStale", json.Properties().Select(property => property.Name));
        Assert.DoesNotContain("IsDataFetchFailed", json.Properties().Select(property => property.Name));
        Assert.DoesNotContain("LowTierBattles", json.Properties().Select(property => property.Name));
    }

    [Fact]
    public void BackgroundRefreshUpdatesStatisticsButPreservesLocalPlayerState()
    {
        Player current = new("Current", Server.ASIA, "0", "101")
        {
            ID = "100",
            ShipName = "Current ship",
            ShipType = "Battleship",
            ShipTier = 10,
            Note = "replay later",
            WatchStatus = WatchStatus.NEGTIVE,
            IsCustomMarked = true,
            RecentEncounterCount = 2,
            IsFixedTeammate = true
        };
        Player refreshed = CreateStatisticsSource();

        current.CopyFrom(refreshed);

        Assert.Equal("Current", current.Name);
        Assert.Equal("100", current.ID);
        Assert.Equal(Server.ASIA, current.Server);
        Assert.Equal("0", current.Relation);
        Assert.Equal("101", current.ShipID);
        Assert.Equal("Current ship", current.ShipName);
        Assert.Equal("Battleship", current.ShipType);
        Assert.Equal(10, current.ShipTier);
        Assert.Equal("replay later", current.Note);
        Assert.Equal(WatchStatus.NEGTIVE, current.WatchStatus);
        Assert.True(current.IsCustomMarked);
        Assert.Equal(2, current.RecentEncounterCount);
        Assert.True(current.IsFixedTeammate);
        Assert.Equal(refreshed.Battles, current.Battles);
        Assert.Equal(refreshed.PR, current.PR);
        Assert.Equal(refreshed.ShipBattles, current.ShipBattles);
        Assert.Equal(refreshed.ShipPR, current.ShipPR);
        Assert.True(current.IsDataStale);
        Assert.True(current.IsDataFetchFailed);
    }

    [Fact]
    public void PublishedDesktopEntryPointsKeepPublicParameterlessConstructors()
    {
        Type[] contractTypes =
        {
            typeof(ApeRadar.App),
            typeof(ApeRadar.HistoryWindow),
            typeof(ApeRadar.Controls.DashboardMainView)
        };

        Assert.All(contractTypes, type =>
        {
            Assert.True(type.IsPublic, $"{type.FullName} is no longer public.");
            ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);
            Assert.NotNull(constructor);
            Assert.True(constructor!.IsPublic);
        });
    }

    private static Player CreateStatisticsSource() => new("Remote identity", Server.NA, "2", "remote-ship")
    {
        ID = "remote-id",
        ShipName = "Remote ship",
        ShipType = "Destroyer",
        ShipTier = 9,
        ClanID = "77",
        ClanTag = "[TAG]",
        IsHidden = false,
        Wins = 2400,
        Battles = 4321,
        AccountWinrate = 0.5554,
        Karma = 27,
        PR = 1678,
        TierWins = 400,
        TierBattles = 720,
        TierWinrate = 0.5556,
        TierPR = 1601,
        IsTierSampleSmall = false,
        TierReferenceMin = 8,
        TierReferenceMax = 10,
        TierReferenceBattles = 1200,
        TierReferenceWinrate = 0.551,
        TierReferencePR = 1588,
        HasTierReference = true,
        MostPlayedTier = 10,
        MostPlayedTierBattles = 500,
        MostPlayedTierShare = 0.42,
        IsLowTierBiased = true,
        ShipWins = 70,
        ShipBattles = 123,
        ShipWinrate = 0.5691,
        ShipTotalDmg = 12_300_000,
        ShipAvgDmgPerBattle = 100_000,
        ShipPR = 1750,
        WeightedWinrate = 0.561,
        IsDataStale = true,
        IsDataFetchFailed = true,
        Note = "remote note",
        WatchStatus = WatchStatus.CHEATER,
        IsCustomMarked = false,
        RecentEncounterCount = 9,
        IsFixedTeammate = false
    };

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
