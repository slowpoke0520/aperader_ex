using ApeRadar.Models;
using ApeRadar.Utils;
using System;

namespace ApeRadar.Services
{
    internal sealed record PlayerStatisticsOptions(string WgApplicationId, double SoloMultiplier,
        double DivisionTwoMultiplier, double DivisionThreeMultiplier, double ShipMaxWeight, int ShipBattlesAtMaxWeight)
    {
        public static PlayerStatisticsOptions Capture()
        {
            var settings = Properties.Settings.Default;
            return new(settings.WgApplicationId, settings.WeightedWinrateAccountSoloWeightMultiplier,
                settings.WeightedWinrateAccountDiv2WeightMultiplier, settings.WeightedWinrateAccountDiv3WeightMultiplier,
                settings.WeightedWinrateShipMaxWeight, settings.WeightedWinrateShipBattlesAtMaxWeight);
        }
    }

    internal interface IRosterLoadSettings
    {
        RosterLoadCommand Capture(string filename, bool forceRefresh, string? playerId, Server? server);
    }

    // Global settings are read once at the application boundary. Requests, retries
    // and stale-cache refreshes share the same immutable calculation options.
    internal sealed class RosterLoadSettings : IRosterLoadSettings
    {
        public RosterLoadCommand Capture(string filename, bool forceRefresh, string? playerId, Server? server)
        {
            var settings = Properties.Settings.Default;
            return new(filename, settings.GamePath, settings.Server, settings.SecondaryServer,
                settings.SecondaryServerEnabled, settings.APITypeSelection, settings.MaximumRetryAttemptsOnError,
                forceRefresh, playerId, server, @".\WatchList.json") { StatisticsOptions = PlayerStatisticsOptions.Capture() };
        }
    }

    internal static class WeightedWinrateCalculator
    {
        public static double Calculate(PlayerStatisticsOptions options, double soloWinrate, double soloBattles,
            double divisionTwoWinrate, double divisionTwoBattles, double divisionThreeWinrate, double divisionThreeBattles,
            double shipWinrate, double shipBattles)
        {
            double soloWeight = soloBattles * options.SoloMultiplier;
            double twoWeight = divisionTwoBattles * options.DivisionTwoMultiplier;
            double threeWeight = divisionThreeBattles * options.DivisionThreeMultiplier;
            double totalWeight = soloWeight + twoWeight + threeWeight;
            if (!double.IsFinite(totalWeight) || totalWeight <= 0) return -1;
            double account = (soloWinrate * soloWeight + divisionTwoWinrate * twoWeight + divisionThreeWinrate * threeWeight) / totalWeight;
            double shipWeight = options.ShipBattlesAtMaxWeight > 0
                ? options.ShipMaxWeight * Math.Clamp(shipBattles / options.ShipBattlesAtMaxWeight, 0, 1) / 100 : 0;
            double result = shipWeight <= 0 ? account : account * (1 - shipWeight) + shipWinrate * shipWeight;
            return double.IsFinite(result) && result >= 0 && result <= 1 ? result : -1;
        }
    }
}
