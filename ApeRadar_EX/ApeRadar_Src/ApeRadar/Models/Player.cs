using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ApeRadar.Models
{
    internal class Player : PlayerStatistics, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public string Name { get; set; }
        public string ID { get; set; }
        public Server Server { get; set; }
        public string Relation { get; set; }
        public string ShipID { get; set; }
        public string ShipName { get; set; }
        public string ShipType { get; set; }
        public int ShipTier { get; set; }
        public string TierSampleMarker => IsTierSampleSmall && TierBattles >= 0 ? " ⚠" : "";
        public bool HasRecognizedTierData => MostPlayedTier > 0;
        public double LowTierBattles { get; set; }
        public double HighTierBattles { get; set; }
        public double LowTierWinrate { get; set; }
        public double HighTierWinrate { get; set; }
        public double LowTierPR { get; set; }
        public double HighTierPR { get; set; }
        public string Note { get; set; }
        public bool IsDataStale { get; set; }
        public bool IsDataFetchFailed { get; set; }

        private bool isCustomMarked;
        public bool IsCustomMarked
        {
            get => isCustomMarked;
            set
            {
                isCustomMarked = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(CustomMarkerIcon));
            }
        }

        public string CustomMarkerIcon => IsCustomMarked ? " ★" : "";
        public string RecentEncounterDetails { get; set; } = "";

        private int recentEncounterCount;
        public int RecentEncounterCount
        {
            get => recentEncounterCount;
            set
            {
                recentEncounterCount = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(RecentEncounterIcon));
                NotifyPropertyChanged(nameof(RecentEncounterDetails));
            }
        }

        private bool isFixedTeammate;
        public bool IsFixedTeammate
        {
            get => isFixedTeammate;
            set
            {
                isFixedTeammate = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(RecentEncounterIcon));
                NotifyPropertyChanged(nameof(CanBeFixedTeammate));
            }
        }

        public string RecentEncounterIcon => !IsFixedTeammate && RecentEncounterCount > 0 ? $" 🔁{RecentEncounterCount}" : "";
        internal PlayerIdentity Identity => new(Name, ID, Server, Relation, ShipID);
        internal PlayerAvailability Availability => new(Identity.HasAccountId, IsHidden, IsDataStale, IsDataFetchFailed);
        public bool CanRefreshData => Identity.HasAccountId;
        public bool CanBeFixedTeammate => Identity.HasAccountId && (Relation == "1" || IsFixedTeammate);

        private WatchStatus watchStatus;
        public WatchStatus WatchStatus
        {
            get
            {
                return watchStatus;
            }
            set
            {
                watchStatus = value;
                NotifyPropertyChanged();
            }
        }
        public double PlotXPosition { get; set; }

        public Player(string Name, Server Server, string Relation, string ShipID)
        {
            this.Name = Name;
            this.Server = Server;
            ID = "-1";
            this.Relation = Relation;
            this.ShipID = ShipID;
            ShipName = "";
            ShipType = "";
            ShipTier = 0;
            ClanID = "-1";
            ClanTag = "";
            IsHidden = false;
            Wins = -1;
            Wins_Solo = -1;
            Wins_Div2 = -1;
            Wins_Div3 = -1;
            Battles = -1;
            Battles_Solo = -1;
            Battles_Div2 = -1;
            Battles_Div3 = -1;
            TotalExp = -1;
            TotalExp_Solo = -1;
            TotalExp_Div2 = -1;
            TotalExp_Div3 = -1;
            AvgExpPerBattle = -1;
            AvgExpPerBattle_Solo = -1;
            AvgExpPerBattle_Div2 = -1;
            AvgExpPerBattle_Div3 = -1;
            AccountWinrate = -1;
            AccountWinrate_Solo = -1;
            AccountWinrate_Div2 = -1;
            AccountWinrate_Div3 = -1;
            ShipWins = -1;
            ShipWins_Solo = -1;
            ShipWins_Div2 = -1;
            ShipWins_Div3 = -1;
            ShipBattles = -1;
            ShipBattles_Solo = -1;
            ShipBattles_Div2 = -1;
            ShipBattles_Div3 = -1;
            ShipTotalDmg = -1;
            ShipTotalDmg_Solo = -1;
            ShipTotalDmg_Div2 = -1;
            ShipTotalDmg_Div3 = -1;
            ShipAvgDmgPerBattle = -1;
            ShipAvgDmgPerBattle_Solo = -1;
            ShipAvgDmgPerBattle_Div2 = -1;
            ShipAvgDmgPerBattle_Div3 = -1;
            ShipTotalExp = -1;
            ShipTotalExp_Solo = -1;
            ShipTotalExp_Div2 = -1;
            ShipTotalExp_Div3 = -1;
            ShipAvgExpPerBattle = -1;
            ShipAvgExpPerBattle_Solo = -1;
            ShipAvgExpPerBattle_Div2 = -1;
            ShipAvgExpPerBattle_Div3 = -1;
            ShipWinrate = -1;
            ShipWinrate_Solo = -1;
            ShipWinrate_Div2 = -1;
            ShipWinrate_Div3 = -1;
            WeightedWinrate = -1;
            Karma = -1;
            PR = -1;
            ShipPR = -1;
            ResetTierPerformance();
            Note = "";
            IsCustomMarked = false;
            RecentEncounterCount = 0;
            IsFixedTeammate = false;
            WatchStatus = WatchStatus.NONE;
            PlotXPosition = -1;
        }

        public Player(string Name, string ID, Server Server, WatchStatus WatchStatus)
        {
            this.Name = Name;
            this.Server = Server;
            this.ID = ID;
            Relation = "-1";
            ShipID = "-1";
            ShipName = "";
            ShipType = "";
            ShipTier = 0;
            ClanID = "-1";
            ClanTag = "";
            IsHidden = false;
            Wins = -1;
            Wins_Solo = -1;
            Wins_Div2 = -1;
            Wins_Div3 = -1;
            Battles = -1;
            Battles_Solo = -1;
            Battles_Div2 = -1;
            Battles_Div3 = -1;
            TotalExp = -1;
            TotalExp_Solo = -1;
            TotalExp_Div2 = -1;
            TotalExp_Div3 = -1;
            AvgExpPerBattle = -1;
            AvgExpPerBattle_Solo = -1;
            AvgExpPerBattle_Div2 = -1;
            AvgExpPerBattle_Div3 = -1;
            AccountWinrate = -1;
            AccountWinrate_Solo = -1;
            AccountWinrate_Div2 = -1;
            AccountWinrate_Div3 = -1;
            ShipWins = -1;
            ShipWins_Solo = -1;
            ShipWins_Div2 = -1;
            ShipWins_Div3 = -1;
            ShipBattles = -1;
            ShipBattles_Solo = -1;
            ShipBattles_Div2 = -1;
            ShipBattles_Div3 = -1;
            ShipTotalDmg = -1;
            ShipTotalDmg_Solo = -1;
            ShipTotalDmg_Div2 = -1;
            ShipTotalDmg_Div3 = -1;
            ShipAvgDmgPerBattle = -1;
            ShipAvgDmgPerBattle_Solo = -1;
            ShipAvgDmgPerBattle_Div2 = -1;
            ShipAvgDmgPerBattle_Div3 = -1;
            ShipTotalExp = -1;
            ShipTotalExp_Solo = -1;
            ShipTotalExp_Div2 = -1;
            ShipTotalExp_Div3 = -1;
            ShipAvgExpPerBattle = -1;
            ShipAvgExpPerBattle_Solo = -1;
            ShipAvgExpPerBattle_Div2 = -1;
            ShipAvgExpPerBattle_Div3 = -1;
            ShipWinrate = -1;
            ShipWinrate_Solo = -1;
            ShipWinrate_Div2 = -1;
            ShipWinrate_Div3 = -1;
            WeightedWinrate = -1;
            Karma = -1;
            PR = -1;
            ShipPR = -1;
            ResetTierPerformance();
            Note = "";
            IsCustomMarked = false;
            RecentEncounterCount = 0;
            IsFixedTeammate = false;
            this.WatchStatus = WatchStatus;
            PlotXPosition = -1;
        }

        public string GetPlayerInfoStrForYuyukoApiPush()
        {
            return $@"{{""server"": ""{ServerExt.GetNameByServer(Server).ToLower()}"", ""accountId"": {ID}, ""userName"": ""{Name}"", ""shipId"": {ShipID}, ""hidden"": {IsHidden.ToString().ToLower()}, ""clanId"": {ClanID}, ""tag"": ""{ClanTag}"", ""relation"": {Relation}}}";
        }

        //copy statistics only, keep watchlist status / note / identity untouched
        public void CopyFrom(Player other)
        {
            CopyStatisticsFrom(other);
            LowTierBattles = other.LowTierBattles;
            HighTierBattles = other.HighTierBattles;
            LowTierWinrate = other.LowTierWinrate;
            HighTierWinrate = other.HighTierWinrate;
            LowTierPR = other.LowTierPR;
            HighTierPR = other.HighTierPR;
            IsDataStale = other.IsDataStale;
            IsDataFetchFailed = other.IsDataFetchFailed;
        }

        private void ResetTierPerformance()
        {
            TierWins = -1;
            TierBattles = -1;
            TierWinrate = -1;
            TierPR = -1;
            IsTierSampleSmall = false;
            TierReferenceMin = 0;
            TierReferenceMax = 0;
            TierReferenceBattles = -1;
            TierReferenceWinrate = -1;
            TierReferencePR = -1;
            HasTierReference = false;
            MostPlayedTier = 0;
            MostPlayedTierBattles = -1;
            MostPlayedTierShare = -1;
            IsLowTierBiased = false;
            LowTierBattles = -1;
            HighTierBattles = -1;
            LowTierWinrate = -1;
            HighTierWinrate = -1;
            LowTierPR = -1;
            HighTierPR = -1;
        }

        override public string ToString()
        {
            return $"Name={Name}, ID={ID}, Server={ServerExt.GetNameByServer(Server)}, Relation={Relation}, ShipID={ShipID}, ShipName={ShipName}, ShipType={ShipType}, ShipTier={ShipTier}, ClanID={ClanID}, ClanTag={ClanTag}, IsHidden={IsHidden}, IsDataFetchFailed={IsDataFetchFailed}, Wins={Wins}, Wins_Solo={Wins_Solo}, Wins_Div2={Wins_Div2}, Wins_Div3={Wins_Div3}, Battles={Battles}, Battles_Solo={Battles_Solo}, Battles_Div2={Battles_Div2}, Battles_Div3={Battles_Div3}, TotalExp={TotalExp}, TotalExp_Solo={TotalExp_Solo}, TotalExp_Div2={TotalExp_Div2}, TotalExp_Div3={TotalExp_Div3}, AvgExpPerBattle={AvgExpPerBattle}, AvgExpPerBattle_Solo={AvgExpPerBattle_Solo}, AvgExpPerBattle_Div2={AvgExpPerBattle_Div2}, AvgExpPerBattle_Div3={AvgExpPerBattle_Div3}, AccountWinrate={AccountWinrate}, AccountWinrate_Solo={AccountWinrate_Solo}, AccountWinrate_Div2={AccountWinrate_Div2}, AccountWinrate_Div3={AccountWinrate_Div3}, ShipWins={ShipWins}, ShipWins_Solo={ShipWins_Solo}, ShipWins_Div2={ShipWins_Div2}, ShipWins_Div3={ShipWins_Div3}, ShipBattles={ShipBattles}, ShipBattles_Solo={ShipBattles_Solo}, ShipBattles_Div2={ShipBattles_Div2}, ShipBattles_Div3={ShipBattles_Div3}, ShipTotalDmg={ShipTotalDmg}, ShipTotalDmg_Solo={ShipTotalDmg_Solo}, ShipTotalDmg_Div2={ShipTotalDmg_Div2}, ShipTotalDmg_Div3={ShipTotalDmg_Div3}, ShipAvgDmgPerBattle={ShipAvgDmgPerBattle}, ShipAvgDmgPerBattle_Solo={ShipAvgDmgPerBattle_Solo}, ShipAvgDmgPerBattle_Div2={ShipAvgDmgPerBattle_Div2}, ShipAvgDmgPerBattle_Div3={ShipAvgDmgPerBattle_Div3}, ShipTotalExp={ShipTotalExp}, ShipTotalExp_Solo={ShipTotalExp_Solo}, ShipTotalExp_Div2={ShipTotalExp_Div2}, ShipTotalExp_Div3={ShipTotalExp_Div3}, ShipAvgExpPerBattle={ShipAvgExpPerBattle}, ShipAvgExpPerBattle_Solo={ShipAvgExpPerBattle_Solo}, ShipAvgExpPerBattle_Div2={ShipAvgExpPerBattle_Div2}, ShipAvgExpPerBattle_Div3={ShipAvgExpPerBattle_Div3}, ShipWinrate={ShipWinrate}, ShipWinrate_Solo={ShipWinrate_Solo}, ShipWinrate_Div2={ShipWinrate_Div2}, ShipWinrate_Div3={ShipWinrate_Div3}, TierBattles={TierBattles}, TierWinrate={TierWinrate}, MostPlayedTier={MostPlayedTier}, WeightedWinrate={WeightedWinrate}, Karma={Karma}, PR={PR}, Note={Note}, WatchStatus={WatchStatusExt.GetNameByStatus(WatchStatus)}";
        }
    }
}
