using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ApeRadar.History
{
    /// <summary>
    /// Converts Replay/internal map identifiers to the names published by the
    /// World of Warships Asia API. Keeping this in the presentation path also
    /// fixes map names already stored in existing history databases.
    /// </summary>
    internal static class HistoryMapNameLocalizer
    {
        private sealed record MapNames(string Code, string English, string Chinese);

        // Snapshot of the Asia API battlearenas list on 2026-09-06.
        private static readonly MapNames[] Maps =
        {
            new("00_CO_ocean", "Ocean", "无尽之海"),
            new("01_solomon_islands", "Solomon Islands", "狂鲨怒湾"),
            new("04_Archipelago", "Archipelago", "深渊之锁"),
            new("05_Ring", "Ring", "连环之岛"),
            new("08_NE_passage", "Strait", "风暴眼"),
            new("10_NE_big_race", "Big Race", "狩猎峡湾"),
            new("13_OC_new_dawn", "New Dawn", "乱礁靶场"),
            new("14_Atlantic", "The Atlantic", "阿特拉斯海"),
            new("15_NE_north", "North", "刺刀峡湾"),
            new("16_OC_bees_to_honey", "Hotspot", "怒海新星"),
            new("17_NA_fault_line", "Fault Line", "海神之击"),
            new("18_NE_ice_islands", "Islands of Ice", "冰海追击"),
            new("19_OC_prey", "Trap", "克拉肯之口"),
            new("20_NE_two_brothers", "Two Brothers", "双峰海峡"),
            new("22_tierra_del_fuego", "Land of Fire", "火海炼狱"),
            new("23_Shards", "Shards", "破碎之地"),
            new("25_sea_hope", "Sea of Fortune", "命运之海"),
            new("28_naval_mission", "Tears of the Desert", "沙漠之泪"),
            new("33_new_tierra", "Polar", "铁血冰原"),
            new("34_OC_islands", "Islands", "深渊之锁"),
            new("35_NE_north_winter", "Northern Lights", "北极之光"),
            new("37_Ridge", "Mountain Range", "山脉锁链"),
            new("38_Canada", "Shatter", "碎钻群岛"),
            new("40_Okinawa", "Okinawa", "巨舰之殇"),
            new("41_Conquest", "Trident", "三叉戟"),
            new("42_Neighbors", "Neighbors", "隔海相望"),
            new("44_Path_warrior", "Warrior's Path", "勇士之路"),
            new("45_Zigzag", "Loop", "群岛之环"),
            new("46_Estuary", "Estuary", "河口之争"),
            new("47_Sleeping_Giant", "Sleeping Giant", "沉睡的巨人"),
            new("50_Gold_harbor", "Haven", "多崖之港"),
            new("51_Greece", "Greece", "希腊"),
            new("52_Britain", "Crash Zone Alpha", "阿尔法碰撞区"),
            new("53_Shoreside", "Northern Waters", "北方水域"),
            new("54_Faroe", "The Faroe Islands", "世界尽头"),
            new("55_Seychelles", "Seychelles", "绿意群岛"),
            new("56_AngelWings", "Sunset Isles", "鬼门关"),
            new("58_RidgeNew", "Mountain Range", "山脉锁链"),
            new("r01_military_navigation", "Riposte", "还击"),
            new("s01_NavalBase", "Killer Whale", "杀人鲸"),
            new("s02_Naval_Defense", "Naval Station Newport", "“纽波特”海军基地"),
            new("s03_Labyrinth", "Labyrinth", "迷宫"),
            new("s06_Atoll", "Rouen Atoll", "鲁昂环礁"),
            new("s07_Advance", "Sunda Islands", "巽他群岛"),
            new("s09_LePVE", "Hermes", "赫尔墨斯"),
            new("s10_USS_CL", "Empress Augusta Bay", "奥古斯塔皇后湾"),
            new("s11_D_day", "Normandy Coast", "诺曼底海岸"),
            new("s11_D_day_2", "Normandy Coast", "诺曼底海岸"),
            new("s12_WW2_OP1", "Barents Sea", "白海"),
            new("s13_WW2_OP2", "The Solomons", "所罗门群岛"),
            new("s14_WW2_OP3", "The Atolls", "环礁"),
            new("e08_PostApocalypse", "Flooded City", "被淹没的城市"),
            new("50_VanHellsing", "Saving Transylvania", "拯救特兰西瓦尼亚"),
            new("e02_Halloween_2017", "Dragons Bay", "巨龙湾"),
            new("e11_FreshGameplay_2021", "Polygon", "多角之域"),
            new("e01_Jacuzzi", "Hot Tub", "热水浴缸"),
            new("e12_April_2024_bees_to_honey", "Colorful Islands", "多彩群岛"),
            new("e13_Space_1_Defence", "Planet Vulcan", "瓦肯星"),
            new("e13_Space_2_Offence", "Planet Vulcan", "瓦肯星"),
            new("e13_Space_3_BossFight", "Planet Vulcan", "瓦肯星"),
            new("e14_ModernEra", "Polar Waters", "极地水域")
        };

        private static readonly Dictionary<string, MapNames> Lookup = BuildLookup();

        public static string GetDisplayName(string? rawName)
        {
            bool useChinese = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
            return GetDisplayName(rawName, useChinese);
        }

        internal static string GetDisplayName(string? rawName, bool useChinese)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";

            string original = rawName.Trim().Trim('"');
            foreach (string candidate in GetCandidates(original))
            {
                if (Lookup.TryGetValue(candidate, out MapNames? names))
                {
                    return useChinese ? names.Chinese : names.English;
                }
            }
            return original;
        }

        private static IEnumerable<string> GetCandidates(string value)
        {
            yield return value;

            string candidate = value.Replace('\\', '/').TrimEnd('/');
            int queryIndex = candidate.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0) candidate = candidate[..queryIndex];
            int slashIndex = candidate.LastIndexOf('/');
            if (slashIndex >= 0) candidate = candidate[(slashIndex + 1)..];
            candidate = Path.GetFileNameWithoutExtension(candidate);

            int minimapIndex = candidate.IndexOf("_minimap", StringComparison.OrdinalIgnoreCase);
            if (minimapIndex >= 0) candidate = candidate[..minimapIndex];
            if (candidate.StartsWith("IDS_MAP_", StringComparison.OrdinalIgnoreCase)) candidate = candidate[8..];
            yield return candidate;
        }

        private static Dictionary<string, MapNames> BuildLookup()
        {
            Dictionary<string, MapNames> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (MapNames names in Maps)
            {
                result[names.Code] = names;
                result[names.English] = names;
                result[names.Chinese] = names;
            }
            return result;
        }
    }
}
