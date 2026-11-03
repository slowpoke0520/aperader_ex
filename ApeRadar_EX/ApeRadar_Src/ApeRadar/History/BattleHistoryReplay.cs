using Microsoft.Extensions.Logging;
using Nodsoft.WowsReplaysUnpack.Controllers;
using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using Nodsoft.WowsReplaysUnpack.Core.Entities;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using Nodsoft.WowsReplaysUnpack.Core.Network.Packets;
using Razorvine.Pickle;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ApeRadar.History
{
    internal sealed class BattleHistoryReplay : UnpackedReplay
    {
        public bool BattleEnded { get; set; }
        public bool DamageStatsSeen { get; set; }
        public string DamageStatsError { get; set; } = "";
        public string VehicleDeathsError { get; set; } = "";
        public Dictionary<int, double> EnemyDamageByType { get; } = new();
        public Dictionary<int, double> PotentialDamageByType { get; } = new();
        public Dictionary<int, double> SpottingDamageByType { get; } = new();
        public HashSet<int> DamageStatisticTypesSeen { get; } = new();
        public List<VehicleDeathEvent> VehicleDeaths { get; } = new();
        public float LastPacketTime { get; set; }
        public float? BattleEndTime { get; set; }
        public List<JsonElement> BattleResults { get; } = new();
        public string BattleResultsError { get; set; } = "";
        public string EntityParsingError { get; set; } = "";
        public ReplayBattleClock BattleClock { get; } = new();
        public bool HasBattleEnded => BattleEnded || BattleClock.HasEnded;
    }

    internal readonly record struct VehicleDeathEvent(uint VictimId, uint KillerId, uint Reason, float PacketTime);

    internal sealed class BattleHistoryReplayController : ReplayControllerBase<BattleHistoryReplay>
    {
        public BattleHistoryReplayController(IDefinitionStore definitionStore, ILogger<Entity> entityLogger)
            : base(definitionStore, entityLogger)
        {
        }

        public override void HandleNetworkPacket(NetworkPacketBase packet, ReplayUnpackerOptions options)
        {
            float? packetTime = null;
            if (packet is ReplayTimedPacket timed)
            {
                packetTime = timed.PacketTime;
                packet = timed.Packet;
            }
            if (packet is BattleResultsPacket results)
            {
                ApplyBattleResults(Replay, results);
                return;
            }
            // Result packets are independent of entity layouts. If a version-specific
            // entity cannot be decoded, stop using process metrics but keep reading
            // frames so a later validated result can still be imported.
            if (!string.IsNullOrEmpty(Replay.EntityParsingError)) return;
            try
            {
                base.HandleNetworkPacket(packet, options);
                if (packetTime.HasValue) ObserveBattleClock(packetTime.Value);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or KeyNotFoundException or
                                       InvalidCastException or FormatException or ArgumentException or OverflowException)
            {
                Replay.EntityParsingError = ex.GetType().Name;
            }
        }

        private void ObserveBattleClock(float packetTime)
        {
            Entity? logic = Replay.Entities.Values.FirstOrDefault(entity => entity.Name == "BattleLogic");
            object? raw = logic?.ClientProperties.GetValueOrDefault("battleStage");
            int? stage = raw switch { byte value => value, sbyte value => value, int value => value, _ => null };
            if (stage.HasValue) Replay.BattleClock.ObserveStage(packetTime, stage.Value);
        }

        internal static void ApplyBattleResults(BattleHistoryReplay replay, BattleResultsPacket packet)
        {
            if (replay.BattleResultsError == "BattleResultsTooManyPackets") return;
            if (packet.Results is not { } root)
            {
                replay.BattleResultsError = packet.Error;
                return;
            }
            if (replay.BattleResults.Count >= 8)
            {
                replay.BattleResults.Clear();
                replay.BattleResultsError = "BattleResultsTooManyPackets";
                return;
            }
            replay.BattleResults.Add(root);
        }

        public override void CallSubscription(
            string hash,
            Entity entity,
            float packetTime,
            Dictionary<string, object?> arguments)
        {
            base.CallSubscription(hash, entity, packetTime, arguments);
            Replay.LastPacketTime = Math.Max(Replay.LastPacketTime, packetTime);
            switch (hash)
            {
                case "Avatar_onBattleEnd":
                    Replay.BattleEnded = true;
                    Replay.BattleEndTime = packetTime;
                    break;
                case "Avatar_receiveDamageStat":
                    byte[]? payload = arguments.Values.OfType<byte[]>().FirstOrDefault();
                    if (payload != null && payload.Length > 0) ApplyDamageStats(Replay, payload);
                    break;
                case "Avatar_receiveVehicleDeath":
                    ApplyVehicleDeath(arguments, packetTime);
                    break;
            }
        }

        internal static void ApplyDamageStats(BattleHistoryReplay replay, byte[] payload)
        {
            try
            {
                using Unpickler unpickler = new();
                using MemoryStream stream = new(payload, writable: false);
                if (unpickler.load(stream) is not IDictionary statistics)
                    throw new InvalidDataException("Damage statistics payload is not a dictionary.");

                replay.DamageStatsSeen = true;
                foreach (DictionaryEntry entry in statistics)
                {
                    if (entry.Key is not IList key || key.Count < 2 || entry.Value is not IList value || value.Count < 2)
                        throw new InvalidDataException("Damage statistics entry has an unexpected shape.");
                    int damageType = Convert.ToInt32(key[0], CultureInfo.InvariantCulture);
                    int statisticsType = Convert.ToInt32(key[1], CultureInfo.InvariantCulture);
                    double damage = Convert.ToDouble(value[1], CultureInfo.InvariantCulture);
                    if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0)
                        throw new InvalidDataException("Damage statistics entry contains an invalid value.");
                    replay.DamageStatisticTypesSeen.Add(statisticsType);
                    Dictionary<int, double>? target = statisticsType switch
                    {
                        0 => replay.EnemyDamageByType,
                        // Category 2 is damage allies deal to targets we spot, not damage taken.
                        2 => replay.SpottingDamageByType,
                        3 => replay.PotentialDamageByType,
                        _ => null
                    };
                    if (target != null) target[damageType] = damage;
                }
            }
            catch (Exception ex) when (ex is PickleException or InvalidDataException or FormatException or InvalidCastException or OverflowException)
            {
                replay.DamageStatsError = ex.Message;
            }
        }

        private void ApplyVehicleDeath(Dictionary<string, object?> arguments, float packetTime)
        {
            try
            {
                object?[] values = arguments.Values.ToArray();
                if (values.Length < 3) return;
                Replay.VehicleDeaths.Add(new VehicleDeathEvent(
                    Convert.ToUInt32(values[0]),
                    Convert.ToUInt32(values[1]),
                    Convert.ToUInt32(values[2]),
                    packetTime));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                Replay.VehicleDeathsError = ex.Message;
            }
        }
    }
}
