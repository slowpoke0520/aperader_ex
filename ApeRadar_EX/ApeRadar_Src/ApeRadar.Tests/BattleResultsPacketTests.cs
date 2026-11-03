using ApeRadar.History;
using Microsoft.Extensions.Logging.Abstractions;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using Nodsoft.WowsReplaysUnpack.Core.Network.Packets;
using Nodsoft.WowsReplaysUnpack.Services;
using System.Text;
using Xunit;

namespace ApeRadar.Tests;

public sealed class BattleResultsPacketTests
{
    private static readonly Version SupportedVersion = new(15, 8, 0);

    [Fact]
    public void ResultsReplacement_PreservesOtherPacketsIndicesAndOrder()
    {
        using MemoryStream stream = Frames(
            (0x77, 1f, new byte[] { 4, 3, 2, 1 }),
            (0x22, 12.5f, Results("{\"playersPublicInfo\":{},\"marker\":7}")),
            (0x0a, 20f, Position()));
        using BattleResultsReplayDataParser parser = CreateParser();

        NetworkPacketBase[] packets = parser.ParseNetworkPackets(stream, new ReplayUnpackerOptions(), SupportedVersion).ToArray();

        Assert.Equal(new[] { 0, 1, 2 }, packets.Select(p => p.PacketIndex));
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, Assert.IsType<UnknownPacket>(packets[0]).Data);
        BattleResultsPacket results = Assert.IsType<BattleResultsPacket>(packets[1]);
        Assert.Equal(12.5f, results.PacketTime);
        Assert.Empty(results.Error);
        Assert.Equal(7, results.Results!.Value.GetProperty("marker").GetInt32());
        PositionPacket position = Assert.IsType<PositionPacket>(packets[2]);
        Assert.Equal(42u, position.EntityId);
        Assert.Equal(1f, position.Position.Position.X);
        Assert.Equal(3f, position.Position.Position.Z);
    }

    [Theory]
    [InlineData("15.8.1")]
    [InlineData("15.10.0")]
    [InlineData("16.0.0")]
    public void UncalibratedVersion_DoesNotInterpretUnknownPacketAsResults(string version)
    {
        byte[] payload = Results("{\"marker\":7}");
        using MemoryStream stream = Frames((0x22, 4f, payload));
        using BattleResultsReplayDataParser parser = CreateParser();
        UnknownPacket packet = Assert.IsType<UnknownPacket>(Assert.Single(parser.ParseNetworkPackets(stream, new(), Version.Parse(version))));
        Assert.Equal(payload, packet.Data);
    }

    [Theory]
    [InlineData("15.8.0.12345")]
    [InlineData("15.9.0.13357625")]
    public void BuildRevision_UsesTheCalibratedMajorMinorPatchVersion(string version)
    {
        using MemoryStream stream = Frames((0x22, 4f, Results("{}")));
        using BattleResultsReplayDataParser parser = CreateParser();
        BattleResultsPacket packet = Assert.IsType<BattleResultsPacket>(Assert.Single(parser.ParseNetworkPackets(stream, new(), Version.Parse(version))));
        Assert.Empty(packet.Error);
    }

    [Theory]
    [InlineData("missingLength", "ResultsLengthInvalid")]
    [InlineData("wrongLength", "ResultsLengthInvalid")]
    [InlineData("negativeLength", "ResultsLengthInvalid")]
    [InlineData("badUtf8", "ResultsUtf8Invalid")]
    [InlineData("badJson", "ResultsJsonInvalid")]
    [InlineData("array", "ResultsJsonNotObject")]
    [InlineData("null", "ResultsJsonNotObject")]
    [InlineData("tooDeep", "ResultsJsonInvalid")]
    [InlineData("tooLarge", "ResultsJsonTooLarge")]
    public void BadResultBody_ReportsFailureAndContinuesFollowingPackets(string input, string error)
    {
        byte[] body = input switch
        {
            "missingLength" => new byte[] { 1, 2 },
            "wrongLength" => new byte[] { 3, 0, 0, 0, (byte)'{', (byte)'}' },
            "negativeLength" => new byte[] { 255, 255, 255, 255 },
            "badUtf8" => new byte[] { 2, 0, 0, 0, 0xc3, 0x28 },
            "badJson" => Results("{broken"),
            "array" => Results("[]"),
            "null" => Results("null"),
            "tooDeep" => Results(new string('[', 66) + "0" + new string(']', 66)),
            "tooLarge" => new byte[BattleResultsReplayDataParser.MaximumResultJsonBytes + 5],
            _ => throw new ArgumentException(input)
        };
        using MemoryStream stream = Frames((0x22, 7f, body), (0x77, 8f, new byte[] { 99 }));
        using BattleResultsReplayDataParser parser = CreateParser();
        NetworkPacketBase[] packets = parser.ParseNetworkPackets(stream, new(), SupportedVersion).ToArray();
        BattleResultsPacket result = Assert.IsType<BattleResultsPacket>(packets[0]);
        Assert.Null(result.Results);
        Assert.Equal(error, result.Error);
        Assert.Equal(new byte[] { 99 }, Assert.IsType<UnknownPacket>(packets[1]).Data);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void NonfiniteResultTime_IsNotAccepted(float time)
    {
        using MemoryStream stream = Frames((0x22, time, Results("{}")));
        using BattleResultsReplayDataParser parser = CreateParser();
        BattleResultsPacket packet = Assert.IsType<BattleResultsPacket>(Assert.Single(parser.ParseNetworkPackets(stream, new(), SupportedVersion)));
        Assert.Equal("ResultsPacketTimeInvalid", packet.Error);
        Assert.Null(packet.Results);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void TruncatedOuterHeader_ThrowsBeforeUpstreamCanReadPaddedData(int length)
    {
        using MemoryStream stream = new(new byte[length]);
        using BattleResultsReplayDataParser parser = CreateParser();
        Assert.Throws<InvalidDataException>(() => parser.ParseNetworkPackets(stream, new(), SupportedVersion).ToArray());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(uint.MaxValue)]
    public void InvalidOuterSize_ThrowsBeforePayloadAllocation(uint size)
    {
        using MemoryStream stream = Frames((0x22, 1f, Results("{}")));
        stream.Position = 0;
        using (BinaryWriter writer = new(stream, Encoding.UTF8, true)) writer.Write(size);
        stream.Position = 0;
        using BattleResultsReplayDataParser parser = CreateParser();
        Assert.Throws<InvalidDataException>(() => parser.ParseNetworkPackets(stream, new(), SupportedVersion).ToArray());
    }

    [Fact]
    public async Task ConcurrentEnumerationsOnSameParser_KeepOwnedJsonSeparate()
    {
        using BattleResultsReplayDataParser parser = CreateParser();
        Task<BattleResultsPacket[]>[] tasks = Enumerable.Range(1, 12).Select(id => Task.Run(() =>
        {
            using MemoryStream stream = Frames(
                (0x22, id, Results($"{{\"marker\":{id}}}")),
                (0x22, id + 1, Results($"{{\"marker\":{id + 100}}}")));
            return parser.ParseNetworkPackets(stream, new(), SupportedVersion).Cast<BattleResultsPacket>().ToArray();
        })).ToArray();

        BattleResultsPacket[][] parsed = await Task.WhenAll(tasks);
        for (int i = 0; i < parsed.Length; i++)
        {
            Assert.Equal(i + 1, parsed[i][0].Results!.Value.GetProperty("marker").GetInt32());
            Assert.Equal(i + 101, parsed[i][1].Results!.Value.GetProperty("marker").GetInt32());
            Assert.Equal(new[] { 0, 1 }, parsed[i].Select(p => p.PacketIndex));
        }
    }

    private static BattleResultsReplayDataParser CreateParser() => new(NullLogger<DefaultReplayDataParser>.Instance);

    private static byte[] Results(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return BitConverter.GetBytes(bytes.Length).Concat(bytes).ToArray();
    }

    private static byte[] Position()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, true);
        writer.Write(42u);
        writer.Write(0);
        foreach (float value in new[] { 1f, 2f, 3f, 0f, 0f, 0f, 0.5f, 0f, 0f }) writer.Write(value);
        writer.Write(false);
        return stream.ToArray();
    }

    private static MemoryStream Frames(params (uint Type, float Time, byte[] Payload)[] frames)
    {
        MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, true))
            foreach (var frame in frames)
            {
                writer.Write((uint)frame.Payload.Length);
                writer.Write(frame.Type);
                writer.Write(frame.Time);
                writer.Write(frame.Payload);
            }
        stream.Position = 0;
        return stream;
    }
}
