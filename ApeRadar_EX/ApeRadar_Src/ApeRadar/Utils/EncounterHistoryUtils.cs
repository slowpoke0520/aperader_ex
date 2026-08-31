using ApeRadar.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace ApeRadar.Utils
{
    //Persists a short battle history so players met in the previous few battles can be marked.
    static internal class EncounterHistoryUtils
    {
        public const int RecentBattleCount = 5;
        private const int MaximumStoredBattles = 30;
        private const string FILENAME = @".\EncounterHistory.json";
        private static readonly object syncRoot = new();
        private static EncounterHistoryFile data = new();
        private static bool loaded = false;

        private class EncounterHistoryFile
        {
            public List<BattleRecord> Battles { get; set; } = new();
            public List<FixedTeammateRecord> FixedTeammates { get; set; } = new();
        }

        private class BattleRecord
        {
            public string BattleID { get; set; } = "";
            public DateTimeOffset BattleStartTime { get; set; }
            public List<string> PlayerKeys { get; set; } = new();
            public List<PlayerEncounterRecord> Players { get; set; } = new();
        }

        private class PlayerEncounterRecord
        {
            public string PlayerKey { get; set; } = "";
            public string Relation { get; set; } = "";
            public string ShipName { get; set; } = "";
        }

        private class FixedTeammateRecord
        {
            public string PlayerKey { get; set; } = "";
            public string Name { get; set; } = "";
        }

        private static string GetPlayerKey(Player p)
        {
            string identity = p.ID != "-1" ? p.ID : $"name:{p.Name.ToLowerInvariant()}";
            return $"{ServerExt.GetNameByServer(p.Server)}:{identity}";
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            try
            {
                if (File.Exists(FILENAME))
                {
                    data = JsonConvert.DeserializeObject<EncounterHistoryFile>(File.ReadAllText(FILENAME)) ?? new EncounterHistoryFile();
                    data.Battles ??= new List<BattleRecord>();
                    data.FixedTeammates ??= new List<FixedTeammateRecord>();
                    foreach (BattleRecord battle in data.Battles)
                    {
                        battle.PlayerKeys ??= new List<string>();
                        battle.Players ??= new List<PlayerEncounterRecord>();
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Failed to load encounter history", ex);
                data = new EncounterHistoryFile();
            }
        }

        private static void Save()
        {
            try
            {
                AtomicFileUtils.WriteAllText(FILENAME, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Failed to save encounter history", ex);
            }
        }

        public static void ApplyRecentEncounterMarkers(IEnumerable<Player> players, string battleID, DateTimeOffset battleStartTime)
        {
            lock (syncRoot)
            {
                EnsureLoaded();
                List<BattleRecord> recentBattles = data.Battles
                    .Where(b => b.BattleID != battleID && b.BattleStartTime < battleStartTime)
                    .OrderByDescending(b => b.BattleStartTime)
                    .Take(RecentBattleCount)
                    .ToList();
                HashSet<string> fixedTeammates = data.FixedTeammates.Select(t => t.PlayerKey).ToHashSet();

                foreach (Player p in players)
                {
                    string key = GetPlayerKey(p);
                    p.IsFixedTeammate = fixedTeammates.Contains(key);
                    if (p.Relation == "0" || p.IsFixedTeammate)
                    {
                        p.RecentEncounterDetails = "";
                        p.RecentEncounterCount = 0;
                        continue;
                    }

                    List<BattleRecord> encounters = recentBattles
                        .Where(b => b.PlayerKeys.Contains(key) || b.Players.Any(e => e.PlayerKey == key))
                        .ToList();
                    p.RecentEncounterDetails = string.Join(Environment.NewLine, encounters.Select(b => FormatEncounter(b, key)));
                    p.RecentEncounterCount = encounters.Count;
                }
            }
        }

        private static string FormatEncounter(BattleRecord battle, string playerKey)
        {
            PlayerEncounterRecord? encounter = battle.Players.FirstOrDefault(e => e.PlayerKey == playerKey);
            string time = battle.BattleStartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            if (encounter == null)
            {
                return $"{time} · {Application.Current.FindResource("EncounterLegacyDetails")}";
            }

            string side = encounter.Relation switch
            {
                "1" => Application.Current.FindResource("EncounterAlly") as string ?? "Ally",
                "0" => Application.Current.FindResource("EncounterUnknown") as string ?? "Unknown",
                _ => Application.Current.FindResource("EncounterEnemy") as string ?? "Enemy"
            };
            return $"{time} · {side} · {encounter.ShipName}";
        }

        public static void RecordBattle(IEnumerable<Player> players, string battleID, DateTimeOffset battleStartTime)
        {
            lock (syncRoot)
            {
                EnsureLoaded();
                data.Battles.RemoveAll(b => b.BattleID == battleID);
                data.Battles.Add(new BattleRecord
                {
                    BattleID = battleID,
                    BattleStartTime = battleStartTime,
                    PlayerKeys = players.Where(p => p.Relation != "0").Select(GetPlayerKey).Distinct().ToList(),
                    Players = players
                        .Where(p => p.Relation != "0")
                        .GroupBy(GetPlayerKey)
                        .Select(group =>
                        {
                            Player p = group.First();
                            return new PlayerEncounterRecord
                            {
                                PlayerKey = group.Key,
                                Relation = p.Relation,
                                ShipName = p.ShipName
                            };
                        })
                        .ToList()
                });
                data.Battles = data.Battles
                    .OrderByDescending(b => b.BattleStartTime)
                    .Take(MaximumStoredBattles)
                    .ToList();
                Save();
            }
        }

        public static void ToggleFixedTeammate(Player p)
        {
            if (!p.CanBeFixedTeammate)
            {
                return;
            }

            lock (syncRoot)
            {
                EnsureLoaded();
                string key = GetPlayerKey(p);
                FixedTeammateRecord? existing = data.FixedTeammates.FirstOrDefault(t => t.PlayerKey == key);
                if (existing == null)
                {
                    data.FixedTeammates.Add(new FixedTeammateRecord { PlayerKey = key, Name = p.Name });
                    p.IsFixedTeammate = true;
                    p.RecentEncounterCount = 0;
                }
                else
                {
                    data.FixedTeammates.Remove(existing);
                    p.IsFixedTeammate = false;
                }
                Save();
            }
        }
    }
}
