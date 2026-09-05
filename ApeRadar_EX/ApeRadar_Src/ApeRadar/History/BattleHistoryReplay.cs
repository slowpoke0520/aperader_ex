using Microsoft.Extensions.Logging;
using Nodsoft.WowsReplaysUnpack.Controllers;
using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using Nodsoft.WowsReplaysUnpack.Core.Entities;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using Razorvine.Pickle;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ApeRadar.History
{
    internal sealed class BattleHistoryReplay : UnpackedReplay
    {
        public bool BattleEnded { get; set; }
        public bool DamageStatsSeen { get; set; }
        public string DamageStatsError { get; set; } = "";
        public string VehicleDeathsError { get; set; } = "";
        public Dictionary<int, double> EnemyDamageByType { get; } = new();
        public List<VehicleDeathEvent> VehicleDeaths { get; } = new();
    }

    internal readonly record struct VehicleDeathEvent(uint VictimId, uint KillerId, uint Reason);

    internal sealed class BattleHistoryReplayController : ReplayControllerBase<BattleHistoryReplay>
    {
        public BattleHistoryReplayController(IDefinitionStore definitionStore, ILogger<Entity> entityLogger)
            : base(definitionStore, entityLogger)
        {
        }

        public override void CallSubscription(
            string hash,
            Entity entity,
            float packetTime,
            Dictionary<string, object?> arguments)
        {
            base.CallSubscription(hash, entity, packetTime, arguments);
            switch (hash)
            {
                case "Avatar_onBattleEnd":
                    Replay.BattleEnded = true;
                    break;
                case "Avatar_receiveDamageStat":
                    byte[]? payload = arguments.Values.OfType<byte[]>().FirstOrDefault();
                    if (payload != null && payload.Length > 0) ApplyDamageStats(Replay, payload);
                    break;
                case "Avatar_receiveVehicleDeath":
                    ApplyVehicleDeath(arguments);
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
                    if (statisticsType != 0) continue; // DAMAGE_STATS_ENEMY
                    double damage = Convert.ToDouble(value[1], CultureInfo.InvariantCulture);
                    if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0)
                        throw new InvalidDataException("Damage statistics entry contains an invalid value.");
                    replay.EnemyDamageByType[damageType] = damage;
                }
            }
            catch (Exception ex) when (ex is PickleException or InvalidDataException or FormatException or InvalidCastException or OverflowException)
            {
                replay.DamageStatsError = ex.Message;
            }
        }

        private void ApplyVehicleDeath(Dictionary<string, object?> arguments)
        {
            try
            {
                object?[] values = arguments.Values.ToArray();
                if (values.Length < 3) return;
                Replay.VehicleDeaths.Add(new VehicleDeathEvent(
                    Convert.ToUInt32(values[0]),
                    Convert.ToUInt32(values[1]),
                    Convert.ToUInt32(values[2])));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                Replay.VehicleDeathsError = ex.Message;
            }
        }
    }
}
