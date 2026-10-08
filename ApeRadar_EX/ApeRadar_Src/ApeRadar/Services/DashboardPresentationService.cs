using ApeRadar.Models;
using ApeRadar.Utils.Sorters;
using ApeRadar.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ApeRadar.Services
{
    internal sealed record DashboardPresentationSnapshot(
        IReadOnlyList<DashboardPlayerRowViewModel> Allies,
        IReadOnlyList<DashboardPlayerRowViewModel> Enemies,
        DashboardSummary Summary);

    internal interface IDashboardPresentationService
    {
        DashboardPresentationSnapshot Create(
            Battlefield battlefield,
            DashboardRosterContext context,
            bool loadCompleted,
            int sortMode);
    }

    internal sealed class DashboardPresentationService : IDashboardPresentationService
    {
        private readonly IRosterPresentationService rosterPresentationService;

        public DashboardPresentationService(IRosterPresentationService? rosterPresentationService = null)
        {
            this.rosterPresentationService = rosterPresentationService ?? new RosterPresentationService();
        }

        public DashboardPresentationSnapshot Create(
            Battlefield battlefield,
            DashboardRosterContext context,
            bool loadCompleted,
            int sortMode)
        {
            RosterPresentationOptions options = RosterPresentationOptions.FromCurrentSettings();
            IReadOnlyList<DashboardPlayerRowViewModel> allies = CreateRows(battlefield.Allies, options, context, loadCompleted, sortMode);
            IReadOnlyList<DashboardPlayerRowViewModel> enemies = CreateRows(battlefield.Enemies, options, context, loadCompleted, sortMode);
            return new(allies, enemies, BuildSummary(allies, enemies, context));
        }

        private IReadOnlyList<DashboardPlayerRowViewModel> CreateRows(
            IEnumerable<Player> players,
            RosterPresentationOptions options,
            DashboardRosterContext context,
            bool loadCompleted,
            int sortMode)
        {
            List<PlayerRosterRowViewModel> baseRows = rosterPresentationService.CreateRows(players, options).ToList();
            PlayerRosterRowComparer comparer = new(CreateComparer(sortMode));
            baseRows.Sort((left, right) => comparer.Compare(left, right));
            return baseRows.Select((row, index) => new DashboardPlayerRowViewModel(index + 1, row, context, loadCompleted)).ToArray();
        }

        private static IComparer CreateComparer(int sortMode) => sortMode switch
        {
            0 => new CustomSorterByShipTypeAndShipTierAndWinrateDescending(),
            1 => new CustomSorterByShipTierAndShipTypeAndWinrateDescending(),
            2 => new CustomSorterByShipTypeAndWinrateDescending(),
            3 => new CustomSorterByShipTierAndWinrateDescending(),
            4 => new CustomSorterByWinrateDescending(),
            5 => new CustomSorterByWinrateAscending(),
            6 => new CustomSorterByBattlesDescending(),
            7 => new CustomSorterByBattlesAscending(),
            _ => new CustomSorterByShipTypeAndShipTierAndWinrateDescending()
        };

        private static DashboardSummary BuildSummary(
            IReadOnlyList<DashboardPlayerRowViewModel> allies,
            IReadOnlyList<DashboardPlayerRowViewModel> enemies,
            DashboardRosterContext context)
        {
            DashboardTeamSummary ally = BuildTeamSummary(allies, context);
            DashboardTeamSummary enemy = BuildTeamSummary(enemies, context);
            return new()
            {
                Ally = ally,
                Enemy = enemy,
                ContextWinrate = DashboardComparisonMetric.Percentage(ally.ContextWinrate, enemy.ContextWinrate,
                    context == DashboardRosterContext.Account ? Text("DashboardMetricAccountWinrate", "Account win rate") : Text("DashboardMetricTierWinrate", "Tier win rate"))
                    .WithCoverage(ally.ContextValidCount, ally.TeamSize, enemy.ContextValidCount, enemy.TeamSize),
                ContextPr = DashboardComparisonMetric.Integer(ally.ContextPr, enemy.ContextPr,
                    context == DashboardRosterContext.Account ? Text("DashboardMetricAccountPr", "Account PR") : Text("DashboardMetricTierPr", "Tier PR"))
                    .WithCoverage(ally.ContextPrValidCount, ally.TeamSize, enemy.ContextPrValidCount, enemy.TeamSize),
                ShipWinrate = DashboardComparisonMetric.Percentage(ally.ShipWinrate, enemy.ShipWinrate, Text("DashboardMetricShipWinrate", "Ship win rate"))
                    .WithCoverage(ally.ShipValidCount, ally.TeamSize, enemy.ShipValidCount, enemy.TeamSize),
                ShipPr = DashboardComparisonMetric.Integer(ally.ShipPr, enemy.ShipPr, Text("DashboardMetricShipPr", "Ship PR"))
                    .WithCoverage(ally.ShipPrValidCount, ally.TeamSize, enemy.ShipPrValidCount, enemy.TeamSize),
                Carrier = BuildShipClass(Text("DashboardShipClassCarrier", "Carrier"), "AirCarrier", allies, enemies),
                Destroyer = BuildShipClass(Text("DashboardShipClassDestroyer", "Destroyer"), "Destroyer", allies, enemies)
            };
        }

        private static DashboardTeamSummary BuildTeamSummary(IReadOnlyList<DashboardPlayerRowViewModel> rows, DashboardRosterContext context)
        {
            DashboardPlayerRowViewModel[] contextValid = rows.Where(row => row.HasContextWinrate).ToArray();
            DashboardPlayerRowViewModel[] contextPrValid = rows.Where(row => row.HasContextPr).ToArray();
            DashboardPlayerRowViewModel[] shipValid = rows.Where(row => row.HasValidShip).ToArray();
            DashboardPlayerRowViewModel[] shipPrValid = rows.Where(row => row.HasShipPr).ToArray();
            return new()
            {
                TeamSize = rows.Count,
                HiddenPlayerCount = rows.Count(row => row.Player.IsHidden),
                ContextValidCount = contextValid.Length,
                ShipValidCount = shipValid.Length,
                ContextPrValidCount = contextPrValid.Length,
                ShipPrValidCount = shipPrValid.Length,
                ShipLowSampleCount = rows.Count(row => row.IsShipLowSample),
                AnomalyCount = rows.Count(row => row.IsAnomaly),
                ContextWinrate = Average(contextValid, row => context == DashboardRosterContext.Account ? row.Player.AccountWinrate : row.Player.TierWinrate),
                ContextPr = Average(contextPrValid, row => context == DashboardRosterContext.Account ? row.Player.PR : row.Player.TierPR),
                ShipWinrate = Average(shipValid, row => row.Player.ShipWinrate),
                ShipPr = Average(shipPrValid, row => row.Player.ShipPR)
            };
        }

        private static DashboardShipClassMatchup BuildShipClass(
            string name,
            string shipType,
            IReadOnlyList<DashboardPlayerRowViewModel> allies,
            IReadOnlyList<DashboardPlayerRowViewModel> enemies)
        {
            DashboardPlayerRowViewModel[] allyClass = allies.Where(row => string.Equals(row.Player.ShipType, shipType, StringComparison.OrdinalIgnoreCase)).ToArray();
            DashboardPlayerRowViewModel[] enemyClass = enemies.Where(row => string.Equals(row.Player.ShipType, shipType, StringComparison.OrdinalIgnoreCase)).ToArray();
            DashboardPlayerRowViewModel[] allyValid = allyClass.Where(row => row.HasValidShip).ToArray();
            DashboardPlayerRowViewModel[] enemyValid = enemyClass.Where(row => row.HasValidShip).ToArray();
            return new()
            {
                Name = name,
                AllyCount = allyClass.Length,
                EnemyCount = enemyClass.Length,
                Winrate = DashboardComparisonMetric.Percentage(
                    Average(allyValid, row => row.Player.ShipWinrate),
                    Average(enemyValid, row => row.Player.ShipWinrate),
                    Text("DashboardMetricShipWinrate", "Ship win rate")).WithCoverage(allyValid.Length, allyClass.Length, enemyValid.Length, enemyClass.Length),
                Pr = DashboardComparisonMetric.Integer(
                    Average(allyClass.Where(row => row.HasShipPr).ToArray(), row => row.Player.ShipPR),
                    Average(enemyClass.Where(row => row.HasShipPr).ToArray(), row => row.Player.ShipPR),
                    Text("DashboardMetricShipPr", "Ship PR")).WithCoverage(allyClass.Count(row => row.HasShipPr), allyClass.Length, enemyClass.Count(row => row.HasShipPr), enemyClass.Length)
            };
        }

        private static double? Average<T>(IReadOnlyCollection<T> values, Func<T, double> selector) =>
            values.Count == 0 ? null : values.Average(selector);

        private static string Text(string resourceKey, string fallback) =>
            Application.Current?.TryFindResource(resourceKey) as string ?? fallback;
    }
}
