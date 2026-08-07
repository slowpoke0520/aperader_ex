using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace ApeRadar.Utils
{
    static internal class PRUtils
    {
        //expected values mirror hosted on GitHub, original data from WoWS Numbers
        public const string ExpectedValuesDownloadUrl = "https://raw.githubusercontent.com/wowsinfo/WoWs-Info-Seven/API/json/personal_rating.json";

        // Personal Rating formula is provided by WoWS Numbers.
        // https://wows-numbers.com/personal/rating
        // rDmg = actualDmg / expectedDmg
        // rWins = actualWins / expectedWins
        // rFrags = actualFrags / expectedFrags
        // nDmg = max(0, (rDmg - 0.4) / (1 - 0.4))
        // nFrags = max(0, (rFrags - 0.1) / (1 - 0.1))
        // nWins = max(0, (rWins - 0.7) / (1 - 0.7))
        // PR = 700 * nDmg + 300 * nFrags + 150 * nWins

        private static JObject? ExpectedValues;
        private static long ExpectedValuesTime = 0;

        public static void LoadExpectedValues(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    LogUtils.WriteInfo($"ExpectedValues file not found: {filename}");
                    ExpectedValues = null;
                    ExpectedValuesTime = 0;
                    return;
                }
                JObject JObjectExpectedValues = JsonUtils.Parse(File.ReadAllText(filename));
                ExpectedValues = JObjectExpectedValues["data"] as JObject;
                ExpectedValuesTime = JObjectExpectedValues["time"]?.Value<long>() ?? 0;
                LogUtils.WriteInfo($"ExpectedValues loaded: {ExpectedValues?.Count} ships, time={ExpectedValuesTime}");
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Failed to load expected values", ex);
                ExpectedValues = null;
                ExpectedValuesTime = 0;
            }
        }

        public static long GetExpectedValuesTime()
        {
            return ExpectedValuesTime;
        }

        public static string GetExpectedValuesDateString()
        {
            if (ExpectedValuesTime <= 0)
            {
                return "";
            }
            return DateTimeOffset.FromUnixTimeSeconds(ExpectedValuesTime).ToLocalTime().ToString("yyyy-MM-dd");
        }

        public static bool TryGetExpectedValues(string shipId, out double expectedDmg, out double expectedFrags, out double expectedWinratePercent)
        {
            expectedDmg = 0;
            expectedFrags = 0;
            expectedWinratePercent = 0;
            if (ExpectedValues == null || shipId == "-1")
            {
                return false;
            }
            JToken? token = ExpectedValues[shipId];
            if (token == null || token.Type != JTokenType.Object)
            {
                return false;
            }
            expectedDmg = token["average_damage_dealt"]?.Value<double>() ?? 0;
            expectedFrags = token["average_frags"]?.Value<double>() ?? 0;
            expectedWinratePercent = token["win_rate"]?.Value<double>() ?? 0;
            return expectedDmg > 0 && expectedFrags > 0 && expectedWinratePercent > 0;
        }

        public static double CalculatePR(double actualDmg, double expectedDmg, double actualWins, double expectedWins, double actualFrags, double expectedFrags)
        {
            if (expectedDmg <= 0 || expectedWins <= 0 || expectedFrags <= 0)
            {
                return -1;
            }
            double rDmg = actualDmg / expectedDmg;
            double rWins = actualWins / expectedWins;
            double rFrags = actualFrags / expectedFrags;

            double nDmg = Math.Max(0, (rDmg - 0.4) / (1.0 - 0.4));
            double nFrags = Math.Max(0, (rFrags - 0.1) / (1.0 - 0.1));
            double nWins = Math.Max(0, (rWins - 0.7) / (1.0 - 0.7));

            double pr = 700 * nDmg + 300 * nFrags + 150 * nWins;
            return Math.Min(pr, 9999);
        }

        public static double CalculateAccountPR(IEnumerable<(string shipId, double battles, double damageDealt, double frags, double wins)> ships)
        {
            double actualDmg = 0;
            double expectedDmg = 0;
            double actualWins = 0;
            double expectedWins = 0;
            double actualFrags = 0;
            double expectedFrags = 0;

            foreach ((string shipId, double battles, double damageDealt, double frags, double wins) in ships)
            {
                if (battles <= 0)
                {
                    continue;
                }
                if (!TryGetExpectedValues(shipId, out double eDmg, out double eFrags, out double eWin))
                {
                    continue;
                }
                expectedDmg += eDmg * battles;
                expectedFrags += eFrags * battles;
                expectedWins += (eWin / 100.0) * battles;
                actualDmg += damageDealt;
                actualFrags += frags;
                actualWins += wins;
            }

            return CalculatePR(actualDmg, expectedDmg, actualWins, expectedWins, actualFrags, expectedFrags);
        }

        //PR of a single ship, based on that ship's own battle record and expected values
        public static double CalculateShipPR(string shipId, double battles, double damageDealt, double frags, double wins)
        {
            if (battles <= 0)
            {
                return -1;
            }
            if (!TryGetExpectedValues(shipId, out double expectedDmg, out double expectedFrags, out double expectedWinratePercent))
            {
                return -1;
            }
            return CalculatePR(damageDealt, expectedDmg * battles, wins, expectedWinratePercent / 100.0 * battles, frags, expectedFrags * battles);
        }
    }
}
