using System;
using System.Linq;

namespace ApeRadar.History
{
    internal static class HistoryBattleIdentity
    {
        internal static readonly TimeSpan ReconnectWindow = TimeSpan.FromMinutes(20);

        internal static string NormalizeMap(string value) => value.Trim().Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant() ?? "";

        internal static string? ArenaKey(string key) => key.StartsWith("arena:", StringComparison.Ordinal)
            ? key.Split('|', 2)[0] : null;

        internal static string PlayerBattleKey(BattleRecord battle)
        {
            string? arena = ArenaKey(battle.BattleKey);
            if (arena == null) return battle.BattleKey;
            string owner = KnownAccount(battle.AccountId) ? battle.AccountId : $"name:{battle.AccountName.ToLowerInvariant()}";
            return $"{arena}|{battle.Server.ToUpperInvariant()}|{owner}";
        }

        // Older databases may have attached another player's file hash to this row.
        // A hash-only lookup is still valid, but supplied identities must agree.
        internal static bool CompatiblePlayer(BattleRecord left, BattleRecord right)
        {
            if (KnownServer(left.Server) && KnownServer(right.Server) &&
                !left.Server.Equals(right.Server, StringComparison.OrdinalIgnoreCase)) return false;
            if (left.ShipId != "" && right.ShipId != "" && left.ShipId != right.ShipId) return false;
            if (KnownAccount(left.AccountId) && KnownAccount(right.AccountId) && left.AccountId != right.AccountId) return false;
            return left.AccountName == "" || right.AccountName == "" ||
                left.AccountName.Equals(right.AccountName, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool SameBattle(BattleRecord left, BattleRecord right)
        {
            if (ArenaKey(left.BattleKey) is string leftArena && ArenaKey(right.BattleKey) is string rightArena &&
                leftArena != rightArena) return false;
            if (string.IsNullOrWhiteSpace(left.AccountName) || string.IsNullOrWhiteSpace(right.AccountName) ||
                string.IsNullOrWhiteSpace(left.ShipId) || !left.ShipId.Equals(right.ShipId, StringComparison.Ordinal) ||
                !CompatiblePlayer(left, right)) return false;

            string leftMap = NormalizeMap(left.MapName), rightMap = NormalizeMap(right.MapName);
            if (leftMap != "" && rightMap != "" && leftMap != rightMap) return false;
            TimeSpan distance = (left.StartedAt - right.StartedAt).Duration();
            if (distance > ReconnectWindow) return false;
            if (left.RosterSignature != "" && right.RosterSignature != "")
                return left.RosterSignature.Equals(right.RosterSignature, StringComparison.OrdinalIgnoreCase);

            // Without both rosters, a nearby battle on the same ship is insufficient.
            return distance <= TimeSpan.FromSeconds(2) && leftMap != "" && leftMap == rightMap;
        }

        internal static bool KnownServer(string value) => !string.IsNullOrWhiteSpace(value) &&
            !value.Equals("AUTO", StringComparison.OrdinalIgnoreCase);
        internal static bool KnownAccount(string value) => !string.IsNullOrWhiteSpace(value) && value != "-1" &&
            !value.StartsWith("name:", StringComparison.OrdinalIgnoreCase);
    }
}
