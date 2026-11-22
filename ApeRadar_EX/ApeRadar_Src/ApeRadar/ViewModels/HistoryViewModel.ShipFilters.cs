using ApeRadar.History;
using ApeRadar.Utils;
using ApeRadar.Utils.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ApeRadar.ViewModels
{
    internal sealed partial class HistoryViewModel
    {
        private sealed record ClassifiedShip(HistoryFilterOption Ship, string Type, int Tier, string Nation);
        private IReadOnlyList<ClassifiedShip> accountShips = Array.Empty<ClassifiedShip>();
        private IReadOnlyList<string>? matchingShipIds;
        private int shipOptionsVersion;
        private string? selectedType, selectedTier, selectedNation;
        private bool updatingShipOptions;

        public IReadOnlyList<HistoryFilterOption> Ships { get; private set; } = Array.Empty<HistoryFilterOption>();
        public IReadOnlyList<HistoryFilterOption> ShipTypes { get; private set; } = Array.Empty<HistoryFilterOption>();
        public IReadOnlyList<HistoryFilterOption> ShipTiers { get; private set; } = Array.Empty<HistoryFilterOption>();
        public IReadOnlyList<HistoryFilterOption> ShipNations { get; private set; } = Array.Empty<HistoryFilterOption>();
        public string? SelectedType { get => selectedType; set { if (Set(ref selectedType, value)) ApplyShipFilters(); } }
        public string? SelectedTier { get => selectedTier; set { if (Set(ref selectedTier, value)) ApplyShipFilters(); } }
        public string? SelectedNation { get => selectedNation; set { if (Set(ref selectedNation, value)) ApplyShipFilters(); } }

        private async Task LoadShipFiltersAsync(bool keepShip)
        {
            int version = ++shipOptionsVersion;
            var rows = await repository.GetShipsAsync(SelectedServer?.Value, SelectedAccount?.Value);
            if (version != shipOptionsVersion) return;
            var catalog = ShipInfoUtils.ShipInfo?["ships"];
            accountShips = rows.Select(ship =>
            {
                var data = catalog?[ship.Value];
                string type = (string?)data?["type"] ?? ship.ShipType;
                return new ClassifiedShip(ship, string.IsNullOrWhiteSpace(type) || type == "Unknown" ? "?" : type,
                    (int?)data?["tier"] ?? 0, (string?)data?["nation"] ?? "?");
            }).ToArray();

            string? type = SelectedType, tier = SelectedTier, nation = SelectedNation;
            string? shipId = keepShip ? SelectedShip?.Value : null;
            updatingShipOptions = true;
            try
            {
                string unknown = Resource("HistoryResultUnknown", "Unknown");
                ShipTypes = Options(Resource("HistoryAllShipTypes", "All ship types"), accountShips.Select(s => s.Type)
                    .Distinct().Select(t => Option(t, t == "?" ? unknown : ShipTypePresentation.GetDisplayName(t))).OrderBy(o => o.Display));
                ShipTiers = Options(Resource("HistoryAllShipTiers", "All tiers"), accountShips.Select(s => s.Tier)
                    .Distinct().OrderBy(t => t).Select(t => Option(t.ToString(CultureInfo.InvariantCulture), t == 0 ? unknown : ShipTierConverter.Format(t))));
                ShipNations = Options(Resource("HistoryAllShipNations", "All nations"), accountShips.Select(s => s.Nation)
                    .Distinct().Select(n => Option(n, n == "?" ? unknown : Resource("ShipNation_" + n, n))).OrderBy(o => o.Display));
                OnPropertyChanged(nameof(ShipTypes));
                OnPropertyChanged(nameof(ShipTiers));
                OnPropertyChanged(nameof(ShipNations));
                SelectedType = ShipTypes.Any(o => o.Value == type) ? type : "";
                SelectedTier = ShipTiers.Any(o => o.Value == tier) ? tier : "";
                SelectedNation = ShipNations.Any(o => o.Value == nation) ? nation : "";
            }
            finally { updatingShipOptions = false; }
            RebuildShipList(shipId);
        }

        public void ResetShipFilters()
        {
            updatingShipOptions = true;
            try { SelectedType = ""; SelectedTier = ""; SelectedNation = ""; }
            finally { updatingShipOptions = false; }
            RebuildShipList(null);
        }

        private void ApplyShipFilters()
        {
            if (!updatingShipOptions) RebuildShipList(SelectedShip?.Value);
        }

        private void RebuildShipList(string? selectedId)
        {
            bool active = !string.IsNullOrEmpty(SelectedType) || !string.IsNullOrEmpty(SelectedTier) || !string.IsNullOrEmpty(SelectedNation);
            var ships = accountShips.Where(s =>
                (string.IsNullOrEmpty(SelectedType) || s.Type == SelectedType) &&
                (string.IsNullOrEmpty(SelectedTier) || s.Tier.ToString(CultureInfo.InvariantCulture) == SelectedTier) &&
                (string.IsNullOrEmpty(SelectedNation) || s.Nation == SelectedNation)).Select(s => s.Ship).ToArray();
            matchingShipIds = active ? ships.Select(s => s.Value).ToArray() : null;
            Ships = Options(Resource("HistoryAllShips", "All ships"), ships);
            OnPropertyChanged(nameof(Ships));
            SelectedShip = Ships.FirstOrDefault(s => s.Value == selectedId) ?? Ships[0];
        }

        private static HistoryFilterOption Option(string value, string display) => new() { Value = value, Display = display };
        private static HistoryFilterOption[] Options(string all, IEnumerable<HistoryFilterOption> values) =>
            values.Prepend(Option("", all)).ToArray();
    }
}
