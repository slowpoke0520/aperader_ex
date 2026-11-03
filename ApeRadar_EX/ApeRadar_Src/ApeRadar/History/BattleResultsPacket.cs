using Nodsoft.WowsReplaysUnpack.Core.Network.Packets;
using System;
using System.Text.Json;

namespace ApeRadar.History
{
    // Owned JSON (Clone), never a view into a disposed JsonDocument or a shared parser buffer.
    internal sealed class BattleResultsPacket : NetworkPacketBase
    {
        internal BattleResultsPacket(int packetIndex, float packetTime, Version gameVersion,
            JsonElement? results, string error) : base(packetIndex)
        {
            PacketTime = packetTime;
            GameVersion = gameVersion;
            Results = results;
            Error = error;
        }

        public float PacketTime { get; }
        public Version GameVersion { get; }
        public JsonElement? Results { get; }
        public string Error { get; }
    }
}
