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
        bool IsLowTierBiased,
        SealClubAnalysis SealClub);

    internal sealed record SealClubAnalysis(
        double TotalBattles,
        double LowTierBattles,
        double HighTierBattles,
        double LowTierWinrate,
        double HighTierWinrate,
        double LowTierPr,
        double HighTierPr,
        double LowTierPrCoverage,
        double HighTierPrCoverage,
        bool IsMatch)
    {
        internal static readonly SealClubAnalysis Empty = new(-1, -1, -1, -1, -1, -1, -1, 0, 0, false);
    }

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
                    false,
                    SealClubAnalysis.Empty);
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
            SealClubAnalysis sealClub = CalculateSealClubAnalysis(valid);
            bool isLowTierBiased = sealClub.IsMatch;

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
                isLowTierBiased,
                sealClub);
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
            player.LowTierBattles = summary.SealClub.LowTierBattles;
            player.HighTierBattles = summary.SealClub.HighTierBattles;
            player.LowTierWinrate = summary.SealClub.LowTierWinrate;
            player.HighTierWinrate = summary.SealClub.HighTierWinrate;
            player.LowTierPR = summary.SealClub.LowTierPr;
            player.HighTierPR = summary.SealClub.HighTierPr;
        }

        internal static SealClubAnalysis EvaluateSealClub(
            double totalBattles,
            double lowTierBattles,
            double highTierBattles,
            double lowTierWinrate,
            double highTierWinrate,
            double lowTierPr,
            double highTierPr,
            double lowTierPrCoverage = 1,
            double highTierPrCoverage = 1,
            bool reachedTierEight = true)
        {
            bool match = reachedTierEight &&
                totalBattles >= 1_000 &&
                lowTierBattles >= 500 &&
                highTierBattles >= 200 &&
                lowTierBattles / totalBattles >= 0.50 &&
                lowTierWinrate >= 0.56 &&
                lowTierWinrate - highTierWinrate >= 0.06 &&
                lowTierPr >= 1_600 &&
                highTierPr > 0 &&
                lowTierPr >= highTierPr * 1.25 &&
                lowTierPrCoverage >= 0.90 &&
                highTierPrCoverage >= 0.90;
            return new(totalBattles, lowTierBattles, highTierBattles, lowTierWinrate, highTierWinrate,
                lowTierPr, highTierPr, lowTierPrCoverage, highTierPrCoverage, match);
        }

        private static SealClubAnalysis CalculateSealClubAnalysis(IReadOnlyCollection<TierShipStatistics> valid)
        {
            double totalBattles = valid.Sum(x => x.Battles);
            TierShipStatistics[] low = valid.Where(x => x.Tier <= 5).ToArray();
            TierShipStatistics[] high = valid.Where(x => x.Tier >= 8).ToArray();
            double lowBattles = low.Sum(x => x.Battles);
            double highBattles = high.Sum(x => x.Battles);
            double lowWinrate = lowBattles > 0 ? low.Sum(x => x.Wins) / lowBattles : -1;
            double highWinrate = highBattles > 0 ? high.Sum(x => x.Wins) / highBattles : -1;
            (double lowPr, double lowCoverage) = CalculatePrAndCoverage(low);
            (double highPr, double highCoverage) = CalculatePrAndCoverage(high);
            return EvaluateSealClub(totalBattles, lowBattles, highBattles, lowWinrate, highWinrate,
                lowPr, highPr, lowCoverage, highCoverage, high.Any());
        }

        private static (double pr, double coverage) CalculatePrAndCoverage(IEnumerable<TierShipStatistics> ships)
        {
            TierShipStatistics[] values = ships.ToArray();
            double battles = values.Sum(x => x.Battles);
            double covered = values.Where(x => PRUtils.TryGetExpectedValues(x.ShipId, out _, out _, out _)).Sum(x => x.Battles);
            double pr = PRUtils.CalculateAccountPR(values.Select(x => (x.ShipId, x.Battles, x.DamageDealt, x.Frags, x.Wins)));
            return (pr, battles > 0 ? covered / battles : 0);
        }

        private static (int minTier, int maxTier) GetReferenceRange(int currentTier)
        {
            if (currentTier <= 1) return (1, 2);
            if (currentTier >= 11) return (10, 11);
            return (currentTier - 1, Math.Min(11, currentTier + 1));
        }
    }
}
