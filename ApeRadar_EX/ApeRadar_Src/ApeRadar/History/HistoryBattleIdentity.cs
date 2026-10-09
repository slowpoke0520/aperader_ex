using System;
using System.Linq;

namespace ApeRadar.History
{
    internal static class HistoryBattleIdentity
    {
        internal static readonly TimeSpan ReconnectWindow = TimeSpan.FromMinutes(20);

        internal static string NormalizeMap(string value) => value.Trim().Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant() ?? "";

        internal static bool SameBattle(BattleRecord left, BattleRecord right)
        {
            if (string.IsNullOrWhiteSpace(left.AccountName) || string.IsNullOrWhiteSpace(right.AccountName) ||
                string.IsNullOrWhiteSpace(left.ShipId) || !left.ShipId.Equals(right.ShipId, StringComparison.Ordinal) ||
                !left.AccountName.Equals(right.AccountName, StringComparison.OrdinalIgnoreCase)) return false;
            if (KnownServer(left.Server) && KnownServer(right.Server) &&
                !left.Server.Equals(right.Server, StringComparison.OrdinalIgnoreCase)) return false;
            if (KnownAccount(left.AccountId) && KnownAccount(right.AccountId) && left.AccountId != right.AccountId) return false;

            string leftMap = NormalizeMap(left.MapName), rightMap = NormalizeMap(right.MapName);
            if (leftMap != "" && rightMap != "" && leftMap != rightMap) return false;
            TimeSpan distance = (left.StartedAt - right.StartedAt).Duration();
            if (distance > ReconnectWindow) return false;
            if (left.RosterSignature != "" && right.RosterSignature != "")
                return left.RosterSignature.Equals(right.RosterSignature, StringComparison.OrdinalIgnoreCase);

            // Without both rosters, a nearby battle on the same ship is insufficient.
            return distance <= TimeSpan.FromSeconds(2) && leftMap != "" && leftMap == rightMap;
        }

        private static bool KnownServer(string value) => !string.IsNullOrWhiteSpace(value) &&
            !value.Equals("AUTO", StringComparison.OrdinalIgnoreCase);
        private static bool KnownAccount(string value) => !string.IsNullOrWhiteSpace(value) && value != "-1" &&
            !value.StartsWith("name:", StringComparison.OrdinalIgnoreCase);
    }
}
