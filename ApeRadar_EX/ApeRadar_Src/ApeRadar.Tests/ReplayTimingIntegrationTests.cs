using ApeRadar.History;
using Microsoft.Extensions.Logging.Abstractions;
using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using Nodsoft.WowsReplaysUnpack.Core.Entities;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using Nodsoft.WowsReplaysUnpack.Core.Network.Packets;
using Nodsoft.WowsReplaysUnpack.Services;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ApeRadar.Tests;

public sealed class ReplayTimingIntegrationTests
{
    [Theory]
    [InlineData("15.7.0")]
    [InlineData("15.8.1")]
    [InlineData("15.10.0")]
    [InlineData("15.9")]
    public void UncalibratedVersion_PropertyFramesRemainUnwrapped(string version)
    {
        using MemoryStream stream = PropertyFrames((31.5f, (byte)0));
        using BattleResultsReplayDataParser parser = CreateParser();
        NetworkPacketBase packet = Assert.Single(parser.ParseNetworkPackets(stream, new(), Version.Parse(version)));
        EntityPropertyPacket property = Assert.IsType<EntityPropertyPacket>(packet);
        Assert.Equal(42u, property.EntityId);
        Assert.Equal(new byte[] { 0 }, property.Data);
    }

    [Fact]
    public void InterleavedEnumerations_KeepFrameTimesAndPayloadsOwnedByTheirReplay()
    {
        using BattleResultsReplayDataParser parser = CreateParser();
        using MemoryStream firstStream = PropertyFrames((30, (byte)0), (630, (byte)3));
        using MemoryStream secondStream = PropertyFrames((45, (byte)0), (945, (byte)3));
        using IEnumerator<NetworkPacketBase> first = parser.ParseNetworkPackets(firstStream, new(), new Version(15, 9, 0)).GetEnumerator();
        using IEnumerator<NetworkPacketBase> second = parser.ParseNetworkPackets(secondStream, new(), new Version(15, 9, 0)).GetEnumerator();
        Assert.True(first.MoveNext());
        ReplayTimedPacket firstStart = Assert.IsType<ReplayTimedPacket>(first.Current);
        Assert.True(second.MoveNext());
        ReplayTimedPacket secondStart = Assert.IsType<ReplayTimedPacket>(second.Current);
        Assert.True(first.MoveNext());
        Assert.Equal(630, Assert.IsType<ReplayTimedPacket>(first.Current).PacketTime);
        Assert.True(second.MoveNext());
        Assert.Equal(945, Assert.IsType<ReplayTimedPacket>(second.Current).PacketTime);
        Assert.Equal(30, firstStart.PacketTime);
        Assert.Equal(45, secondStart.PacketTime);
        Assert.Equal(new byte[] { 0 }, Assert.IsType<EntityPropertyPacket>(firstStart.Packet).Data);
        Assert.Equal(new byte[] { 0 }, Assert.IsType<EntityPropertyPacket>(secondStart.Packet).Data);
        Assert.False(first.MoveNext());
        Assert.False(second.MoveNext());
    }

    [Fact]
    public void ResultWithMissingTiming_DoesNotSilentlySubstituteEventTiming()
    {
        BattleHistoryReplay replay = WithOwnShip();
        ObserveCombat(replay, 30, 630);
        replay.BattleEnded = true;
        ReplayBattleResults result = AddAndValidateResult(replay, ReplayResultsFixture.Create((_, own, common) =>
        {
            common[8] = null;
            own[22] = null;
        }));

        BattleAdvancedMetrics advanced = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay, result);

        Assert.Null(advanced.BattleDurationSeconds);
        Assert.Null(advanced.SurvivalSeconds);
        Assert.Equal(ReplayTimingSource.Unknown, advanced.BattleDurationSource);
        Assert.Equal(ReplayTimingSource.Unknown, advanced.SurvivalTimeSource);
    }

    [Fact]
    public void ArchivedPostEndDeath_RemainsDeadAndCapsFallbackLifetime_ThenPreservesResultPrecision()
    {
        // Archived 15.8 event excerpt (2026-10-07 22:04:12), not a new live run.
        BattleHistoryReplay replay = WithOwnShip();
        ObserveCombat(replay, 31.649067, 894.65234);
        replay.BattleEnded = true;
        replay.BattleEndTime = 903.7932f;
        replay.VehicleDeaths.Add(new VehicleDeathEvent(101, 202, 0, 902.8083f));
        replay.Entities[101].ClientProperties["isAlive"] = (byte)0;
        replay.Entities[101].ClientProperties["health"] = 0f;

        BattleAdvancedMetrics fallback = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay);
        Assert.False(fallback.Survived);
        Assert.Equal(863.003273, fallback.SurvivalSeconds!.Value, 6);
        Assert.Equal(fallback.BattleDurationSeconds, fallback.SurvivalSeconds);
        Assert.Equal(ReplayTimingSource.BattleClock, fallback.SurvivalTimeSource);

        ReplayBattleResults result = AddAndValidateResult(replay, ReplayResultsFixture.Create((_, own, common) =>
        {
            common[8] = 863;
            own[20] = 0;
            own[21] = false;
            own[22] = 863.1428571428696;
        }));
        BattleAdvancedMetrics exact = NodsoftReplayParserAdapter.FindAdvancedMetrics(replay, result);
        Assert.False(exact.Survived);
        Assert.Equal(863, exact.BattleDurationSeconds);
        Assert.Equal(863.1428571428696, exact.SurvivalSeconds);
        Assert.Equal(ReplayTimingSource.BattleResults, exact.SurvivalTimeSource);
    }

    private static BattleHistoryReplay WithOwnShip()
    {
        DefaultDefinitionStore store = CreateStore();
        Version version = new(15, 9, 0);
        BattleHistoryReplay replay = new() { ClientVersion = version };
        Entity avatar = new(1, "Avatar", store.GetEntityDefinition(version, "Avatar"), NullLogger<Entity>.Instance);
        avatar.ClientProperties["ownShipId"] = 101u;
        replay.Entities[1] = avatar;
        Entity ship = new(101, "Vehicle", store.GetEntityDefinition(version, "Vehicle"), NullLogger<Entity>.Instance);
        ship.ClientProperties["isAlive"] = (byte)1;
        ship.ClientProperties["health"] = 50_000f;
        replay.Entities[101] = ship;
        return replay;
    }

    private static ReplayBattleResults AddAndValidateResult(BattleHistoryReplay replay, JsonElement root, float packetTime = 1000)
    {
        BattleHistoryReplayController.ApplyBattleResults(replay, new BattleResultsPacket(1, packetTime, replay.ClientVersion, root, ""));
        ReplayBattleResults? result = NodsoftReplayParserAdapter.FindValidatedBattleResults(replay, "Self", "101", out string error);
        Assert.Empty(error);
        return Assert.IsType<ReplayBattleResults>(result);
    }

    private static void ObserveCombat(BattleHistoryReplay replay, double start, double end)
    {
        replay.BattleClock.ObserveStage(0, 1);
        replay.BattleClock.ObserveStage(start, 0);
        replay.BattleClock.ObserveStage(end, 3);
    }

    private static DefaultDefinitionStore CreateStore() =>
        new(NullLogger<DefaultDefinitionStore>.Instance, new Replay159DefinitionLoader(new AssemblyDefinitionLoader()));

    private static BattleResultsReplayDataParser CreateParser() => new(NullLogger<DefaultReplayDataParser>.Instance);

    private static MemoryStream PropertyFrames(params (float Time, byte Stage)[] frames)
    {
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, true))
        {
            foreach ((float Time, byte Stage) frame in frames)
            {
                writer.Write(13u); // Entity ID, property index, blob byte count, one UINT8 value.
                writer.Write(0x07u);
                writer.Write(frame.Time);
                writer.Write(42u);
                writer.Write(7u);
                writer.Write(1u);
                writer.Write(frame.Stage);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
