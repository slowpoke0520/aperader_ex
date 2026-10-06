using System;

namespace ApeRadar.Models
{
    // Shared account and ship statistics. Player keeps the WPF-facing properties;
    // PlayerDataSnapshot keeps the existing flat JSON cache shape.
    internal class PlayerStatistics
    {
        public double Wins { get; set; }
        public double Wins_Solo { get; set; }
        public double Wins_Div2 { get; set; }
        public double Wins_Div3 { get; set; }
        public double Battles { get; set; }
        public double Battles_Solo { get; set; }
        public double Battles_Div2 { get; set; }
        public double Battles_Div3 { get; set; }
        public double TotalExp { get; set; }
        public double TotalExp_Solo { get; set; }
        public double TotalExp_Div2 { get; set; }
        public double TotalExp_Div3 { get; set; }
        public double AvgExpPerBattle { get; set; }
        public double AvgExpPerBattle_Solo { get; set; }
        public double AvgExpPerBattle_Div2 { get; set; }
        public double AvgExpPerBattle_Div3 { get; set; }
        public double AccountWinrate { get; set; }
        public double AccountWinrate_Solo { get; set; }
        public double AccountWinrate_Div2 { get; set; }
        public double AccountWinrate_Div3 { get; set; }
        public string ClanID { get; set; } = "-1";
        public string ClanTag { get; set; } = "";
        public bool IsHidden { get; set; }
        public double Karma { get; set; }
        public double PR { get; set; }
        public double ShipPR { get; set; }
        public double TierWins { get; set; } = -1;
        public double TierBattles { get; set; } = -1;
        public double TierWinrate { get; set; } = -1;
        public double TierPR { get; set; } = -1;
        public bool IsTierSampleSmall { get; set; }
        public int TierReferenceMin { get; set; }
        public int TierReferenceMax { get; set; }
        public double TierReferenceBattles { get; set; } = -1;
        public double TierReferenceWinrate { get; set; } = -1;
        public double TierReferencePR { get; set; } = -1;
        public bool HasTierReference { get; set; }
        public int MostPlayedTier { get; set; }
        public double MostPlayedTierBattles { get; set; } = -1;
        public double MostPlayedTierShare { get; set; } = -1;
        public bool IsLowTierBiased { get; set; }

        //current ship data
        public double ShipWins { get; set; }
        public double ShipWins_Solo { get; set; }
        public double ShipWins_Div2 { get; set; }
        public double ShipWins_Div3 { get; set; }
        public double ShipBattles { get; set; }
        public double ShipBattles_Solo { get; set; }
        public double ShipBattles_Div2 { get; set; }
        public double ShipBattles_Div3 { get; set; }
        public double ShipTotalDmg { get; set; }
        public double ShipTotalDmg_Solo { get; set; }
        public double ShipTotalDmg_Div2 { get; set; }
        public double ShipTotalDmg_Div3 { get; set; }
        public double ShipAvgDmgPerBattle { get; set; }
        public double ShipAvgDmgPerBattle_Solo { get; set; }
        public double ShipAvgDmgPerBattle_Div2 { get; set; }
        public double ShipAvgDmgPerBattle_Div3 { get; set; }
        public double ShipTotalExp { get; set; }
        public double ShipTotalExp_Solo { get; set; }
        public double ShipTotalExp_Div2 { get; set; }
        public double ShipTotalExp_Div3 { get; set; }
        public double ShipAvgExpPerBattle { get; set; }
        public double ShipAvgExpPerBattle_Solo { get; set; }
        public double ShipAvgExpPerBattle_Div2 { get; set; }
        public double ShipAvgExpPerBattle_Div3 { get; set; }
        public double ShipWinrate { get; set; }
        public double ShipWinrate_Solo { get; set; }
        public double ShipWinrate_Div2 { get; set; }
        public double ShipWinrate_Div3 { get; set; }
        public double WeightedWinrate { get; set; }


        public void CopyStatisticsFrom(PlayerStatistics other)
        {
            Wins = other.Wins;
            Wins_Solo = other.Wins_Solo;
            Wins_Div2 = other.Wins_Div2;
            Wins_Div3 = other.Wins_Div3;
            Battles = other.Battles;
            Battles_Solo = other.Battles_Solo;
            Battles_Div2 = other.Battles_Div2;
            Battles_Div3 = other.Battles_Div3;
            TotalExp = other.TotalExp;
            TotalExp_Solo = other.TotalExp_Solo;
            TotalExp_Div2 = other.TotalExp_Div2;
            TotalExp_Div3 = other.TotalExp_Div3;
            AvgExpPerBattle = other.AvgExpPerBattle;
            AvgExpPerBattle_Solo = other.AvgExpPerBattle_Solo;
            AvgExpPerBattle_Div2 = other.AvgExpPerBattle_Div2;
            AvgExpPerBattle_Div3 = other.AvgExpPerBattle_Div3;
            AccountWinrate = other.AccountWinrate;
            AccountWinrate_Solo = other.AccountWinrate_Solo;
            AccountWinrate_Div2 = other.AccountWinrate_Div2;
            AccountWinrate_Div3 = other.AccountWinrate_Div3;
            ClanID = other.ClanID;
            ClanTag = other.ClanTag;
            IsHidden = other.IsHidden;
            Karma = other.Karma;
            PR = other.PR;
            ShipPR = other.ShipPR;
            TierWins = other.TierWins;
            TierBattles = other.TierBattles;
            TierWinrate = other.TierWinrate;
            TierPR = other.TierPR;
            IsTierSampleSmall = other.IsTierSampleSmall;
            TierReferenceMin = other.TierReferenceMin;
            TierReferenceMax = other.TierReferenceMax;
            TierReferenceBattles = other.TierReferenceBattles;
            TierReferenceWinrate = other.TierReferenceWinrate;
            TierReferencePR = other.TierReferencePR;
            HasTierReference = other.HasTierReference;
            MostPlayedTier = other.MostPlayedTier;
            MostPlayedTierBattles = other.MostPlayedTierBattles;
            MostPlayedTierShare = other.MostPlayedTierShare;
            IsLowTierBiased = other.IsLowTierBiased;
            ShipWins = other.ShipWins;
            ShipWins_Solo = other.ShipWins_Solo;
            ShipWins_Div2 = other.ShipWins_Div2;
            ShipWins_Div3 = other.ShipWins_Div3;
            ShipBattles = other.ShipBattles;
            ShipBattles_Solo = other.ShipBattles_Solo;
            ShipBattles_Div2 = other.ShipBattles_Div2;
            ShipBattles_Div3 = other.ShipBattles_Div3;
            ShipTotalDmg = other.ShipTotalDmg;
            ShipTotalDmg_Solo = other.ShipTotalDmg_Solo;
            ShipTotalDmg_Div2 = other.ShipTotalDmg_Div2;
            ShipTotalDmg_Div3 = other.ShipTotalDmg_Div3;
            ShipAvgDmgPerBattle = other.ShipAvgDmgPerBattle;
            ShipAvgDmgPerBattle_Solo = other.ShipAvgDmgPerBattle_Solo;
            ShipAvgDmgPerBattle_Div2 = other.ShipAvgDmgPerBattle_Div2;
            ShipAvgDmgPerBattle_Div3 = other.ShipAvgDmgPerBattle_Div3;
            ShipTotalExp = other.ShipTotalExp;
            ShipTotalExp_Solo = other.ShipTotalExp_Solo;
            ShipTotalExp_Div2 = other.ShipTotalExp_Div2;
            ShipTotalExp_Div3 = other.ShipTotalExp_Div3;
            ShipAvgExpPerBattle = other.ShipAvgExpPerBattle;
            ShipAvgExpPerBattle_Solo = other.ShipAvgExpPerBattle_Solo;
            ShipAvgExpPerBattle_Div2 = other.ShipAvgExpPerBattle_Div2;
            ShipAvgExpPerBattle_Div3 = other.ShipAvgExpPerBattle_Div3;
            ShipWinrate = other.ShipWinrate;
            ShipWinrate_Solo = other.ShipWinrate_Solo;
            ShipWinrate_Div2 = other.ShipWinrate_Div2;
            ShipWinrate_Div3 = other.ShipWinrate_Div3;
            WeightedWinrate = other.WeightedWinrate;
        }
    }
}
