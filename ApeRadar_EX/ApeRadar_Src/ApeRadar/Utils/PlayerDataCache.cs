using ApeRadar.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ApeRadar.Utils
{
    //snapshot of all player statistics shown in the UI, used to serve cached data instantly
    internal class PlayerDataSnapshot
    {
        public DateTimeOffset FetchedAt { get; set; }
        public DateTimeOffset LastAccessedAt { get; set; }

        //account data
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

        public bool IsExpired()
        {
            return DateTimeOffset.Now - FetchedAt > PlayerDataCache.CurrentShipTtl;
        }

        public static PlayerDataSnapshot FromPlayer(Player p)
        {
            return new PlayerDataSnapshot
            {
                FetchedAt = DateTimeOffset.Now,
                LastAccessedAt = DateTimeOffset.Now,
                Wins = p.Wins,
                Wins_Solo = p.Wins_Solo,
                Wins_Div2 = p.Wins_Div2,
                Wins_Div3 = p.Wins_Div3,
                Battles = p.Battles,
                Battles_Solo = p.Battles_Solo,
                Battles_Div2 = p.Battles_Div2,
                Battles_Div3 = p.Battles_Div3,
                TotalExp = p.TotalExp,
                TotalExp_Solo = p.TotalExp_Solo,
                TotalExp_Div2 = p.TotalExp_Div2,
                TotalExp_Div3 = p.TotalExp_Div3,
                AvgExpPerBattle = p.AvgExpPerBattle,
                AvgExpPerBattle_Solo = p.AvgExpPerBattle_Solo,
                AvgExpPerBattle_Div2 = p.AvgExpPerBattle_Div2,
                AvgExpPerBattle_Div3 = p.AvgExpPerBattle_Div3,
                AccountWinrate = p.AccountWinrate,
                AccountWinrate_Solo = p.AccountWinrate_Solo,
                AccountWinrate_Div2 = p.AccountWinrate_Div2,
                AccountWinrate_Div3 = p.AccountWinrate_Div3,
                ClanID = p.ClanID,
                ClanTag = p.ClanTag,
                IsHidden = p.IsHidden,
                Karma = p.Karma,
                PR = p.PR,
                ShipPR = p.ShipPR,
                TierWins = p.TierWins,
                TierBattles = p.TierBattles,
                TierWinrate = p.TierWinrate,
                TierPR = p.TierPR,
                IsTierSampleSmall = p.IsTierSampleSmall,
                TierReferenceMin = p.TierReferenceMin,
                TierReferenceMax = p.TierReferenceMax,
                TierReferenceBattles = p.TierReferenceBattles,
                TierReferenceWinrate = p.TierReferenceWinrate,
                TierReferencePR = p.TierReferencePR,
                HasTierReference = p.HasTierReference,
                MostPlayedTier = p.MostPlayedTier,
                MostPlayedTierBattles = p.MostPlayedTierBattles,
                MostPlayedTierShare = p.MostPlayedTierShare,
                IsLowTierBiased = p.IsLowTierBiased,
                ShipWins = p.ShipWins,
                ShipWins_Solo = p.ShipWins_Solo,
                ShipWins_Div2 = p.ShipWins_Div2,
                ShipWins_Div3 = p.ShipWins_Div3,
                ShipBattles = p.ShipBattles,
                ShipBattles_Solo = p.ShipBattles_Solo,
                ShipBattles_Div2 = p.ShipBattles_Div2,
                ShipBattles_Div3 = p.ShipBattles_Div3,
                ShipTotalDmg = p.ShipTotalDmg,
                ShipTotalDmg_Solo = p.ShipTotalDmg_Solo,
                ShipTotalDmg_Div2 = p.ShipTotalDmg_Div2,
                ShipTotalDmg_Div3 = p.ShipTotalDmg_Div3,
                ShipAvgDmgPerBattle = p.ShipAvgDmgPerBattle,
                ShipAvgDmgPerBattle_Solo = p.ShipAvgDmgPerBattle_Solo,
                ShipAvgDmgPerBattle_Div2 = p.ShipAvgDmgPerBattle_Div2,
                ShipAvgDmgPerBattle_Div3 = p.ShipAvgDmgPerBattle_Div3,
                ShipTotalExp = p.ShipTotalExp,
                ShipTotalExp_Solo = p.ShipTotalExp_Solo,
                ShipTotalExp_Div2 = p.ShipTotalExp_Div2,
                ShipTotalExp_Div3 = p.ShipTotalExp_Div3,
                ShipAvgExpPerBattle = p.ShipAvgExpPerBattle,
                ShipAvgExpPerBattle_Solo = p.ShipAvgExpPerBattle_Solo,
                ShipAvgExpPerBattle_Div2 = p.ShipAvgExpPerBattle_Div2,
                ShipAvgExpPerBattle_Div3 = p.ShipAvgExpPerBattle_Div3,
                ShipWinrate = p.ShipWinrate,
                ShipWinrate_Solo = p.ShipWinrate_Solo,
                ShipWinrate_Div2 = p.ShipWinrate_Div2,
                ShipWinrate_Div3 = p.ShipWinrate_Div3,
                WeightedWinrate = p.WeightedWinrate,
            };
        }

        public void ApplyTo(Player p)
        {
            p.Wins = Wins;
            p.Wins_Solo = Wins_Solo;
            p.Wins_Div2 = Wins_Div2;
            p.Wins_Div3 = Wins_Div3;
            p.Battles = Battles;
            p.Battles_Solo = Battles_Solo;
            p.Battles_Div2 = Battles_Div2;
            p.Battles_Div3 = Battles_Div3;
            p.TotalExp = TotalExp;
            p.TotalExp_Solo = TotalExp_Solo;
            p.TotalExp_Div2 = TotalExp_Div2;
            p.TotalExp_Div3 = TotalExp_Div3;
            p.AvgExpPerBattle = AvgExpPerBattle;
            p.AvgExpPerBattle_Solo = AvgExpPerBattle_Solo;
            p.AvgExpPerBattle_Div2 = AvgExpPerBattle_Div2;
            p.AvgExpPerBattle_Div3 = AvgExpPerBattle_Div3;
            p.AccountWinrate = AccountWinrate;
            p.AccountWinrate_Solo = AccountWinrate_Solo;
            p.AccountWinrate_Div2 = AccountWinrate_Div2;
            p.AccountWinrate_Div3 = AccountWinrate_Div3;
            p.ClanID = ClanID;
            p.ClanTag = ClanTag;
            p.IsHidden = IsHidden;
            p.Karma = Karma;
            p.PR = PR;
            p.ShipPR = ShipPR;
            p.TierWins = TierWins;
            p.TierBattles = TierBattles;
            p.TierWinrate = TierWinrate;
            p.TierPR = TierPR;
            p.IsTierSampleSmall = IsTierSampleSmall;
            p.TierReferenceMin = TierReferenceMin;
            p.TierReferenceMax = TierReferenceMax;
            p.TierReferenceBattles = TierReferenceBattles;
            p.TierReferenceWinrate = TierReferenceWinrate;
            p.TierReferencePR = TierReferencePR;
            p.HasTierReference = HasTierReference;
            p.MostPlayedTier = MostPlayedTier;
            p.MostPlayedTierBattles = MostPlayedTierBattles;
            p.MostPlayedTierShare = MostPlayedTierShare;
            p.IsLowTierBiased = IsLowTierBiased;
            p.ShipWins = ShipWins;
            p.ShipWins_Solo = ShipWins_Solo;
            p.ShipWins_Div2 = ShipWins_Div2;
            p.ShipWins_Div3 = ShipWins_Div3;
            p.ShipBattles = ShipBattles;
            p.ShipBattles_Solo = ShipBattles_Solo;
            p.ShipBattles_Div2 = ShipBattles_Div2;
            p.ShipBattles_Div3 = ShipBattles_Div3;
            p.ShipTotalDmg = ShipTotalDmg;
            p.ShipTotalDmg_Solo = ShipTotalDmg_Solo;
            p.ShipTotalDmg_Div2 = ShipTotalDmg_Div2;
            p.ShipTotalDmg_Div3 = ShipTotalDmg_Div3;
            p.ShipAvgDmgPerBattle = ShipAvgDmgPerBattle;
            p.ShipAvgDmgPerBattle_Solo = ShipAvgDmgPerBattle_Solo;
            p.ShipAvgDmgPerBattle_Div2 = ShipAvgDmgPerBattle_Div2;
            p.ShipAvgDmgPerBattle_Div3 = ShipAvgDmgPerBattle_Div3;
            p.ShipTotalExp = ShipTotalExp;
            p.ShipTotalExp_Solo = ShipTotalExp_Solo;
            p.ShipTotalExp_Div2 = ShipTotalExp_Div2;
            p.ShipTotalExp_Div3 = ShipTotalExp_Div3;
            p.ShipAvgExpPerBattle = ShipAvgExpPerBattle;
            p.ShipAvgExpPerBattle_Solo = ShipAvgExpPerBattle_Solo;
            p.ShipAvgExpPerBattle_Div2 = ShipAvgExpPerBattle_Div2;
            p.ShipAvgExpPerBattle_Div3 = ShipAvgExpPerBattle_Div3;
            p.ShipWinrate = ShipWinrate;
            p.ShipWinrate_Solo = ShipWinrate_Solo;
            p.ShipWinrate_Div2 = ShipWinrate_Div2;
            p.ShipWinrate_Div3 = ShipWinrate_Div3;
            p.WeightedWinrate = WeightedWinrate;
        }
    }

    internal interface IStatsCache
    {
        bool TryGet(Server server, string playerId, string shipId, out PlayerDataSnapshot? snapshot);
        void Set(Server server, string playerId, string shipId, PlayerDataSnapshot snapshot);
        void Save();
        int PruneStale(DateTimeOffset now);
    }

    internal sealed class PersistentStatsCache : IStatsCache
    {
        public bool TryGet(Server server, string playerId, string shipId, out PlayerDataSnapshot? snapshot) =>
            PlayerDataCache.TryGet(server, playerId, shipId, out snapshot);
        public void Set(Server server, string playerId, string shipId, PlayerDataSnapshot snapshot) =>
            PlayerDataCache.Set(server, playerId, shipId, snapshot);
        public void Save() => PlayerDataCache.Save();
        public int PruneStale(DateTimeOffset now) => PlayerDataCache.PruneStale(now);
    }

    //in-memory cache of assembled player data, keyed by server + playerID + shipID
    static internal class PlayerDataCache
    {
        public static readonly TimeSpan CurrentShipTtl = TimeSpan.FromHours(1);
        public static readonly TimeSpan AccountAndTierTtl = TimeSpan.FromHours(6);
        public static readonly TimeSpan IdentityTtl = TimeSpan.FromDays(7);
        public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
        public static readonly TimeSpan CacheTtl = CurrentShipTtl;
        private static readonly string CacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApeRadar EX", "Cache");
        private static readonly string Filename = Path.Combine(CacheDirectory, "PlayerDataCache.v2.json");
        private const string LegacyFilename = @".\PlayerDataCache.json";

        private static readonly Dictionary<(Server, string, string), PlayerDataSnapshot> cache = new();
        private static readonly object syncRoot = new();
        private static bool loaded = false;
        private static long lookupCount;
        private static long hitCount;

        private class CacheEntry
        {
            public string Server { get; set; } = "";
            public string PlayerID { get; set; } = "";
            public string ShipID { get; set; } = "";
            public PlayerDataSnapshot? Snapshot { get; set; }
        }

        private class PlayerDataCacheFile
        {
            public int SchemaVersion { get; set; } = 2;
            public List<CacheEntry> Entries { get; set; } = new();
        }

        private static void EnsureLoaded()
        {
            lock (syncRoot)
            {
                if (loaded) return;
                loaded = true;
                try
                {
                    string source = File.Exists(Filename) ? Filename : LegacyFilename;
                    if (!File.Exists(source)) return;
                    PlayerDataCacheFile? file = JsonConvert.DeserializeObject<PlayerDataCacheFile>(File.ReadAllText(source));
                    if (file?.Entries != null)
                    {
                        foreach (CacheEntry entry in file.Entries)
                        {
                            if (entry.Snapshot == null)
                            {
                                continue;
                            }
                            if (entry.Snapshot.LastAccessedAt == default) entry.Snapshot.LastAccessedAt = entry.Snapshot.FetchedAt;
                            cache[(ServerExt.GetServerByName(entry.Server), entry.PlayerID, entry.ShipID)] = entry.Snapshot;
                        }
                    }
                    PruneStaleCore(DateTimeOffset.Now);
                    if (!source.Equals(Filename, StringComparison.OrdinalIgnoreCase)) SaveCore();
                }
                catch (Exception ex)
                {
                    LogUtils.WriteError("Failed to load PlayerDataCache", ex);
                    cache.Clear();
                }
            }
        }

        public static void Save()
        {
            EnsureLoaded();
            try
            {
                lock (syncRoot)
                {
                    PruneStaleCore(DateTimeOffset.Now);
                    SaveCore();
                }
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Failed to save PlayerDataCache", ex);
            }
        }

        public static bool TryGet(Server server, string playerID, string shipID, out PlayerDataSnapshot? snapshot)
        {
            EnsureLoaded();
            Interlocked.Increment(ref lookupCount);
            lock (syncRoot)
            {
                if (!cache.TryGetValue((server, playerID, shipID), out snapshot)) return false;
                Interlocked.Increment(ref hitCount);
                snapshot.LastAccessedAt = DateTimeOffset.Now;
                return true;
            }
        }

        public static void Set(Server server, string playerID, string shipID, PlayerDataSnapshot snapshot)
        {
            EnsureLoaded();
            lock (syncRoot)
            {
                snapshot.LastAccessedAt = DateTimeOffset.Now;
                cache[(server, playerID, shipID)] = snapshot;
            }
        }

        public static void Clear()
        {
            EnsureLoaded();
            lock (syncRoot) cache.Clear();
            try
            {
                if (File.Exists(Filename))
                {
                    File.Delete(Filename);
                }
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Failed to delete PlayerDataCache file", ex);
            }
        }

        public static int PruneStale(DateTimeOffset now)
        {
            EnsureLoaded();
            lock (syncRoot) return PruneStaleCore(now);
        }

        private static int PruneStaleCore(DateTimeOffset now)
        {
            (Server, string, string)[] expired = cache
                .Where(entry => now - (entry.Value.LastAccessedAt == default ? entry.Value.FetchedAt : entry.Value.LastAccessedAt) > Retention)
                .Select(entry => entry.Key)
                .ToArray();
            foreach ((Server, string, string) key in expired) cache.Remove(key);
            return expired.Length;
        }

        private static void SaveCore()
        {
            PlayerDataCacheFile file = new();
            foreach (KeyValuePair<(Server, string, string), PlayerDataSnapshot> kv in cache)
            {
                file.Entries.Add(new CacheEntry
                {
                    Server = ServerExt.GetNameByServer(kv.Key.Item1),
                    PlayerID = kv.Key.Item2,
                    ShipID = kv.Key.Item3,
                    Snapshot = kv.Value
                });
            }
            AtomicFileUtils.WriteAllText(Filename, JsonConvert.SerializeObject(file, Formatting.Indented));
        }

        public static int Count
        {
            get
            {
                EnsureLoaded();
                lock (syncRoot) return cache.Count;
            }
        }

        public static (long Lookups, long Hits, double HitRate) GetMetricsSnapshot()
        {
            long lookups = Interlocked.Read(ref lookupCount);
            long hits = Interlocked.Read(ref hitCount);
            return (lookups, hits, lookups == 0 ? 0 : (double)hits / lookups);
        }
    }

    //persistent cache of Vortex player name -> ID, to avoid searching the same names every battle
    static internal class PlayerIDCache
    {
        private const string FILENAME = @".\PlayerIDCache.json";
        private static JObject? data;
        private static bool loaded = false;

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
                    data = JsonUtils.Parse(File.ReadAllText(FILENAME));
                }
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Failed to load PlayerIDCache", ex);
                data = null;
            }
            if (data == null)
            {
                data = JsonUtils.Parse("{\"RU\":{},\"EU\":{},\"NA\":{},\"ASIA\":{},\"CN\":{}}");
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
                LogUtils.WriteError("Failed to save PlayerIDCache", ex);
            }
        }

        public static bool TryGetID(Server server, string name, out string playerID)
        {
            EnsureLoaded();
            playerID = "";
            JToken? token = data![ServerExt.GetNameByServer(server)]?.SelectToken(name);
            if (token == null)
            {
                return false;
            }
            playerID = token.Value<string>()!;
            return playerID != "";
        }

        public static void SetID(Server server, string name, string playerID)
        {
            EnsureLoaded();
            data![ServerExt.GetNameByServer(server)]![name] = playerID;
            Save();
        }

        public static void RemoveID(Server server, string name)
        {
            EnsureLoaded();
            (data![ServerExt.GetNameByServer(server)] as JObject)?.Remove(name);
            Save();
        }

        public static void Clear()
        {
            EnsureLoaded();
            data = JsonUtils.Parse("{\"RU\":{},\"EU\":{},\"NA\":{},\"ASIA\":{},\"CN\":{}}");
            Save();
        }
    }
}
