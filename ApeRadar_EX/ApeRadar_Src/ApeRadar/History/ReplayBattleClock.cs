using System;
using Nodsoft.WowsReplaysUnpack.Core.Network.Packets;

namespace ApeRadar.History
{
    // Property/create packets in Nodsoft discard their frame timestamps. Carry
    // that timestamp with the packet itself, so parallel replays share no clock.
    internal sealed class ReplayTimedPacket : NetworkPacketBase
    {
        internal ReplayTimedPacket(NetworkPacketBase packet, float packetTime) : base(packet.PacketIndex)
            => (Packet, PacketTime) = (packet, packetTime);
        internal NetworkPacketBase Packet { get; }
        internal float PacketTime { get; }
    }

    internal sealed class ReplayBattleClock
    {
        private double? lastStageChangeTime;
        internal int? Stage { get; private set; }
        internal double? StartedAt { get; private set; }
        internal double? EndedAt { get; private set; }
        internal bool Invalid { get; private set; }
        internal bool HasEnded => !Invalid && EndedAt.HasValue;
        internal double? DurationSeconds => !Invalid && StartedAt.HasValue && EndedAt.HasValue
            ? ValidElapsed(EndedAt.Value - StartedAt.Value, allowZero: false) : null;

        // 15.8/15.9: 2=initializing, 1=preparation, 0=in battle, 3=ended.
        // A recording that starts in stage 0 has no observed battle origin.
        // Countdown snapshots are quantized and may be replayed on reconnect;
        // they cannot establish an exact origin for such a fragment.
        internal void ObserveStage(double time, int stage)
        {
            if (Invalid) return;
            if (!double.IsFinite(time) || time < 0 || stage is < 0 or > 3)
            {
                Invalidate();
                return;
            }
            // EOF cleanup can repeat the already-ended stage with frame time 0.
            // Such a snapshot must not move or invalidate confirmed boundaries.
            if (Stage == stage) return;
            if (lastStageChangeTime.HasValue && time < lastStageChangeTime.Value)
            {
                Invalidate();
                return;
            }
            if (Stage == 3 || Stage == 0 && stage is 1 or 2 || Stage == 1 && stage == 2)
            {
                Invalidate();
                return;
            }
            if (Stage == 1 && stage == 0) StartedAt = time;
            if (Stage == 0 && stage == 3)
            {
                if (StartedAt.HasValue && !ValidElapsed(time - StartedAt.Value, allowZero: false).HasValue)
                {
                    Invalidate();
                    return;
                }
                EndedAt = time;
            }
            Stage = stage;
            lastStageChangeTime = time;
        }

        internal double? SurvivalUntil(double deathTime)
        {
            if (Invalid || !StartedAt.HasValue || !double.IsFinite(deathTime) || deathTime < StartedAt.Value) return null;
            // A ship can be destroyed during post-battle cleanup. Preserve the
            // death fact, but do not count the wait after combat in its lifetime.
            double endpoint = EndedAt.HasValue ? Math.Min(deathTime, EndedAt.Value) : deathTime;
            return ValidElapsed(endpoint - StartedAt.Value, allowZero: true);
        }

        private static double? ValidElapsed(double value, bool allowZero) =>
            double.IsFinite(value) && value >= 0 && (allowZero || value > 0) && value <= 3600 ? value : null;

        private void Invalidate()
        {
            Invalid = true;
            StartedAt = null;
            EndedAt = null;
        }
    }
}
