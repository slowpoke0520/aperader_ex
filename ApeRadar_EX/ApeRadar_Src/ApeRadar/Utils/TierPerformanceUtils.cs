using ApeRadar.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ApeRadar.Utils
{
    internal sealed record TierShipStatistics(
        string ShipId,
        int Tier,
        double Battles,
        double Wins,
        double DamageDealt,
        double Frags);

    internal sealed record TierPerformanceSummary(
        int CurrentTier,
        double Battles,
        double Wins,
        double Winrate,
        double Pr,
        bool IsSmallSample,
        int ReferenceMinTier,
        int ReferenceMaxTier,
        double ReferenceBattles,
        double ReferenceWinrate,
        double ReferencePr,
        bool HasReference,
        int MostPlayedTier,
        double MostPlayedBattles,
        double MostPlayedShare,
        bool IsLowTierBiased);

    internal static class TierPerformanceUtils
    {
        internal const int MinimumReliableBattles = 50;

        internal static TierPerformanceSummary Calculate(int currentTier, IEnumerable<TierShipStatistics> shipStatistics)
        {
            List<TierShipStatistics> valid = shipStatistics
                .Where(x => x.Tier is >= 1 and <= 11 && x.Battles > 0)
                .ToList();

            if (currentTier is < 1 or > 11)
            {
                return new TierPerformanceSummary(
                    currentTier,
                    -1,
                    -1,
                    -1,
                    -1,
                    false,
                    0,
                    0,
                    -1,
                    -1,
                    -1,
                    false,
                    0,
                    -1,
                    -1,
                    false);
            }

            List<TierShipStatistics> exact = valid.Where(x => x.Tier == currentTier).ToList();
            double battles = exact.Sum(x => x.Battles);
            double wins = exact.Sum(x => x.Wins);
            double winrate = battles > 0 ? wins / battles : -1;
            double pr = exact.Count > 0
                ? PRUtils.CalculateAccountPR(exact.Select(x => (x.ShipId, x.Battles, x.DamageDealt, x.Frags, x.Wins)))
                : -1;

            (int referenceMinTier, int referenceMaxTier) = GetReferenceRange(currentTier);
            List<TierShipStatistics> reference = valid
                .Where(x => x.Tier >= referenceMinTier && x.Tier <= referenceMaxTier)
                .ToList();
            double referenceBattles = reference.Sum(x => x.Battles);
            double referenceWins = reference.Sum(x => x.Wins);
            double referenceWinrate = referenceBattles > 0 ? referenceWins / referenceBattles : -1;
            double referencePr = reference.Count > 0
                ? PRUtils.CalculateAccountPR(reference.Select(x => (x.ShipId, x.Battles, x.DamageDealt, x.Frags, x.Wins)))
                : -1;
            bool hasReference = battles < MinimumReliableBattles && referenceBattles > battles;

            IGrouping<int, TierShipStatistics>? mostPlayed = valid
                .GroupBy(x => x.Tier)
                .OrderByDescending(x => x.Sum(y => y.Battles))
                .ThenByDescending(x => x.Key)
                .FirstOrDefault();
            double recognizedBattles = valid.Sum(x => x.Battles);
            double mostPlayedBattles = mostPlayed?.Sum(x => x.Battles) ?? 0;
            int mostPlayedTier = mostPlayed?.Key ?? 0;
            double mostPlayedShare = recognizedBattles > 0 ? mostPlayedBattles / recognizedBattles : 0;
            double lowTierBattles = valid.Where(x => x.Tier <= 5).Sum(x => x.Battles);
            bool isLowTierBiased = currentTier >= 8 && recognizedBattles >= 500 && lowTierBattles / recognizedBattles >= 0.6;

            return new TierPerformanceSummary(
                currentTier,
                battles,
                wins,
                winrate,
                pr,
                battles < MinimumReliableBattles,
                referenceMinTier,
                referenceMaxTier,
                referenceBattles,
                referenceWinrate,
                referencePr,
                hasReference,
                mostPlayedTier,
                mostPlayedBattles,
                mostPlayedShare,
                isLowTierBiased);
        }

        internal static void ApplyTo(Player player, IEnumerable<TierShipStatistics> shipStatistics)
        {
            int currentTier = player.ShipTier > 0 ? player.ShipTier : ShipInfoUtils.GetShipTierByID(player.ShipID);
            TierPerformanceSummary summary = Calculate(currentTier, shipStatistics);
            player.TierBattles = summary.Battles;
            player.TierWins = summary.Wins;
            player.TierWinrate = summary.Winrate;
            player.TierPR = summary.Pr;
            player.IsTierSampleSmall = summary.IsSmallSample;
            player.TierReferenceMin = summary.ReferenceMinTier;
            player.TierReferenceMax = summary.ReferenceMaxTier;
            player.TierReferenceBattles = summary.ReferenceBattles;
            player.TierReferenceWinrate = summary.ReferenceWinrate;
            player.TierReferencePR = summary.ReferencePr;
            player.HasTierReference = summary.HasReference;
            player.MostPlayedTier = summary.MostPlayedTier;
            player.MostPlayedTierBattles = summary.MostPlayedBattles;
            player.MostPlayedTierShare = summary.MostPlayedShare;
            player.IsLowTierBiased = summary.IsLowTierBiased;
        }

        private static (int minTier, int maxTier) GetReferenceRange(int currentTier)
        {
            if (currentTier <= 1) return (1, 2);
            if (currentTier >= 11) return (10, 11);
            return (currentTier - 1, Math.Min(11, currentTier + 1));
        }
    }
}
