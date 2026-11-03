using Microsoft.Extensions.Logging;
using Nodsoft.WowsReplaysUnpack;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using Nodsoft.WowsReplaysUnpack.Core.Network.Packets;
using Nodsoft.WowsReplaysUnpack.Services;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ApeRadar.History
{
    // Extend the upstream parser without replacing its normal packet decoders. The
    // Calibrated 15.8.0/15.9.0 samples identify 0x22 as Int32 JSON-byte-length + UTF-8 JSON.
    // Earlier clients use this number for other packets, so the version gate matters.
    internal sealed class BattleResultsReplayDataParser : IReplayDataParser
    {
        internal const int MaximumResultJsonBytes = 4 * 1024 * 1024;
        internal const int MaximumNetworkPacketBytes = 16 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly ILogger<DefaultReplayDataParser> logger;

        public BattleResultsReplayDataParser(ILogger<DefaultReplayDataParser> logger) => this.logger = logger;

        internal static bool SupportsResultsPacket(Version version) =>
            version.Build >= 0 && ReplayBattleResultsDecoder.IsSupportedVersion(new Version(version.Major, version.Minor, version.Build));

        public IEnumerable<NetworkPacketBase> ParseNetworkPackets(MemoryStream replayDataStream,
            ReplayUnpackerOptions options, Version gameVersion)
        {
            // Every enumeration owns an upstream instance. In particular, two
            // concurrent replays must not share the upstream mutable packet buffer.
            using DefaultReplayDataParser parser = new(logger);
            using IEnumerator<NetworkPacketBase> packets = parser.ParseNetworkPackets(replayDataStream, options, gameVersion).GetEnumerator();
            byte[] header = new byte[12];
            int packetIndex = 0;
            while (replayDataStream.Position < replayDataStream.Length)
            {
                long frameStart = replayDataStream.Position;
                if (replayDataStream.Length - frameStart < 12)
                    throw new InvalidDataException("Replay packet header is truncated.");

                replayDataStream.ReadExactly(header);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(header);
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                float time = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8)));
                if (size > MaximumNetworkPacketBytes || size > replayDataStream.Length - replayDataStream.Position)
                    throw new InvalidDataException("Replay packet size is invalid or its payload is truncated.");

                BattleResultsPacket? results = null;
                if (type == 0x22 && SupportsResultsPacket(gameVersion))
                {
                    // A malformed result body does not invalidate unrelated replay
                    // events. A malformed outer frame does: its next boundary is unknown.
                    if (size > MaximumResultJsonBytes + 4)
                        results = new(packetIndex, time, gameVersion, null, "ResultsJsonTooLarge");
                    else
                    {
                        byte[] payload = new byte[(int)size];
                        replayDataStream.ReadExactly(payload);
                        results = ParseResults(packetIndex, time, gameVersion, payload);
                    }
                }

                replayDataStream.Position = frameStart;
                if (!packets.MoveNext())
                    throw new InvalidDataException("Replay packet decoder ended before the validated frame.");
                NetworkPacketBase packet = results ?? packets.Current;
                if (SupportsResultsPacket(gameVersion) && packet is (EntityCreatePacket or EntityPropertyPacket or
                    NestedPropertyPacket or BasePlayerCreatePacket or CellPlayerCreatePacket))
                    packet = new ReplayTimedPacket(packet, time);
                yield return packet;
                packetIndex = checked(packetIndex + 1);
            }
        }

        private static BattleResultsPacket ParseResults(int packetIndex, float time, Version version, byte[] payload)
        {
            if (!float.IsFinite(time)) return Invalid("ResultsPacketTimeInvalid");
            if (payload.Length < 4) return Invalid("ResultsLengthInvalid");
            int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(payload);
            if (jsonLength <= 0 || jsonLength != payload.Length - 4) return Invalid("ResultsLengthInvalid");

            try
            {
                string json = StrictUtf8.GetString(payload, 4, jsonLength);
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) return Invalid("ResultsJsonNotObject");
                return new(packetIndex, time, version, document.RootElement.Clone(), "");
            }
            catch (DecoderFallbackException) { return Invalid("ResultsUtf8Invalid"); }
            catch (JsonException) { return Invalid("ResultsJsonInvalid"); }

            BattleResultsPacket Invalid(string error) => new(packetIndex, time, version, null, error);
        }

        // There is no cross-enumeration state to dispose; enumeration disposes its own resources.
        public void Dispose() { }
    }
}
