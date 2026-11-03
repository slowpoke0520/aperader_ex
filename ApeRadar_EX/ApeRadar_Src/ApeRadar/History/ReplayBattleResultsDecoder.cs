using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace ApeRadar.History
{
    internal sealed record ReplayBattleResults(
        long Damage,
        double Frags,
        BattleResult Result,
        long? SpottingDamage,
        long? DamageTaken,
        string? DamageTakenError,
        double? BattleDurationSeconds,
        bool? Survived,
        double? SurvivalSeconds,
        string AccountId,
        string ArenaId);

    // This is a small, version-bound map, not the entire upstream result schema.
    // Field meanings: toalba/wows-replay-parser battle_results.py at
    // b67f694f0b808dec8d1438d80334aa656e8cac03. The 538/18 layouts and active
    // received-damage families were checked against eleven local 15.8.0 and
    // thirty 15.9.0 results. The latter also cover mixed aircraft subtypes.
    // They must not silently be reused after a game-version or layout change.
    internal static class ReplayBattleResultsDecoder
    {
        internal static IReadOnlyList<Version> SupportedVersions { get; } =
            Array.AsReadOnly(new[] { new Version(15, 8, 0), new Version(15, 9, 0) });
        internal const int PublicRowLength = 538;
        internal const int CommonRowLength = 18;
        private static readonly HashSet<int> ReceivedSingles = new() { 199, 200, 201, 202, 203, 204, 216, 219, 220, 221 };
        private static readonly int[] ReceivedFamilyParents = { 205, 209, 228, 232 };

        internal static bool IsSupportedVersion(Version version) =>
            version.Revision is -1 or 0 && SupportedVersions.Any(supported =>
                version.Major == supported.Major && version.Minor == supported.Minor && version.Build == supported.Build);

        internal static bool TryDecode(JsonElement root, Version gameVersion, string accountName, string shipId,
            out ReplayBattleResults? result, out string error)
        {
            result = null;
            error = "BattleResultsVersionUnsupported";
            if (!IsSupportedVersion(gameVersion)) return false;

            error = "BattleResultsSchemaInvalid";
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
                !root.TryGetProperty("playersPublicInfo", out JsonElement players) || players.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("commonList", out JsonElement common) || common.ValueKind != JsonValueKind.Array ||
                common.GetArrayLength() != CommonRowLength || common[14].ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("accountDBID", out JsonElement account) || !TryPositiveId(account, out long accountId) ||
                !root.TryGetProperty("arenaUniqueID", out JsonElement arena) || !TryPositiveId(arena, out long arenaId) ||
                !TryPositiveId(common[0], out long commonArenaId) || arenaId != commonArenaId ||
                !TryInteger(common[12], out long mode) ||
                !(mode is 7 or 11 || gameVersion.Minor == 9 && mode == 15))
                return false;

            error = "BattleResultsIdentityMismatch";
            if (string.IsNullOrWhiteSpace(accountName) ||
                !uint.TryParse(shipId, NumberStyles.None, CultureInfo.InvariantCulture, out uint expectedShipId) || expectedShipId == 0 ||
                HasDuplicateProperties(players) ||
                !players.TryGetProperty(accountId.ToString(CultureInfo.InvariantCulture), out JsonElement own) ||
                own.ValueKind != JsonValueKind.Array || own.GetArrayLength() != PublicRowLength ||
                !TryPositiveId(own[0], out long rowAccountId) || rowAccountId != accountId ||
                own[1].ValueKind != JsonValueKind.String || !string.Equals(own[1].GetString(), accountName, StringComparison.OrdinalIgnoreCase) ||
                !TryPositiveId(own[7], out long rowShipId) || rowShipId != expectedShipId)
                return false;

            int nameMatches = players.EnumerateObject().Count(p => p.Value.ValueKind == JsonValueKind.Array &&
                p.Value.GetArrayLength() > 1 && p.Value[1].ValueKind == JsonValueKind.String &&
                string.Equals(p.Value[1].GetString(), accountName, StringComparison.OrdinalIgnoreCase));
            if (nameMatches != 1) return false;

            // Anchors of different JSON types catch common equal-length shifted layouts.
            // Private economy data has a separate, uncalibrated layout and is not read.
            error = "BattleResultsSchemaInvalid";
            if (own[10].ValueKind != JsonValueKind.Array || own[405].ValueKind != JsonValueKind.Object ||
                own[406].ValueKind != JsonValueKind.Object || own[407].ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;

            error = "BattleResultsMetricsInvalid";
            if (!TryNonNegativeInteger(own[426], out long damage) ||
                !TryNonNegativeInteger(own[32], out long frags) || frags > 24 ||
                !TryInteger(own[6], out long team) || team is not (0 or 1) ||
                !TryInteger(common[3], out long winner) || winner is not (0 or 1 or 255))
                return false;

            BattleResult battleResult = winner == 255 ? BattleResult.Draw : winner == team ? BattleResult.Win : BattleResult.Loss;
            long? spotting = TryNonNegativeInteger(own[412], out long spottingValue) ? spottingValue : null;
            long? received = TryReceivedDamage(own, out long receivedValue, out string? receivedError) ? receivedValue : null;
            double? duration = TrySeconds(common[8], out double durationValue) && durationValue > 0 ? durationValue : null;
            bool? survived = own[21].ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
            if (!TryNonNegativeInteger(own[20], out long hp) || survived == true && hp == 0 || survived == false && hp != 0)
                survived = null;
            double? survival = survived.HasValue && TrySeconds(own[22], out double life) &&
                (!duration.HasValue || life <= duration.Value + 1) ? life : null;

            result = new ReplayBattleResults(damage, frags, battleResult, spotting, received, receivedError,
                duration, survived, survival, accountId.ToString(CultureInfo.InvariantCulture), arenaId.ToString(CultureInfo.InvariantCulture));
            error = "";
            return true;
        }

        private static bool TryReceivedDamage(JsonElement own, out long received, out string? error)
        {
            received = 0;
            error = "BattleResultsReceivedDamageInvalid";
            // These are public integerized result totals, not health changes or spotting.
            long[] values = new long[61];
            for (int index = 199; index <= 259; index++)
                if (!TryNonNegativeInteger(own[index], out values[index - 199])) return false;

            for (int index = 199; index <= 259; index++)
            {
                if (ReceivedSingles.Contains(index) || index is 236 or 237 ||
                    ReceivedFamilyParents.Any(parent => index >= parent && index <= parent + 3))
                    continue;
                if (values[index - 199] != 0)
                {
                    // Other weapons/building damage have not been calibrated with a
                    // nonzero local sample. A partial sum would understate damage taken.
                    error = "BattleResultsReceivedDamageCategoryUncalibrated";
                    return false;
                }
            }

            try
            {
                foreach (int index in ReceivedSingles) received = checked(received + values[index - 199]);
                // Anti-submarine airstrike damage has two equal representations:
                // dbomb_airsupport (236) and adbomb (237). Across the calibrated
                // results both match the same incoming target interactions. Count
                // the damage once, and reject any new unequal representation.
                if (values[236 - 199] != values[237 - 199])
                {
                    error = "BattleResultsReceivedDamageAliasMismatch";
                    return false;
                }
                received = checked(received + values[236 - 199]);
                foreach (int parent in ReceivedFamilyParents)
                {
                    long familyTotal = values[parent - 199];
                    long children = checked(values[parent + 1 - 199] + values[parent + 2 - 199] + values[parent + 3 - 199]);
                    if (children > familyTotal)
                    {
                        error = "BattleResultsReceivedDamageFamilyMismatch";
                        return false;
                    }
                    // Never add both the family aggregate and its subtype values.
                    received = checked(received + familyTotal);
                }
            }
            catch (OverflowException) { return false; }
            error = null;
            return true;
        }

        private static bool TryPositiveId(JsonElement value, out long number) =>
            TryInteger(value, out number) && number > 0;

        private static bool TryNonNegativeInteger(JsonElement value, out long number) =>
            TryInteger(value, out number) && number >= 0;

        private static bool TryInteger(JsonElement value, out long number)
        {
            number = 0;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number);
        }

        private static bool TrySeconds(JsonElement value, out double seconds)
        {
            seconds = 0;
            return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out seconds) &&
                double.IsFinite(seconds) && seconds >= 0 && seconds <= 3600;
        }

        private static bool HasDuplicateProperties(JsonElement value)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            return value.EnumerateObject().Any(property => !names.Add(property.Name));
        }
    }
}
