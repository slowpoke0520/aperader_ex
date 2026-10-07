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
    internal class PlayerDataSnapshot : PlayerStatistics
    {
        public DateTimeOffset FetchedAt { get; set; }
        public DateTimeOffset LastAccessedAt { get; set; }

        public bool IsExpired()
        {
            return DateTimeOffset.Now - FetchedAt > PlayerDataCache.CurrentShipTtl;
        }

        public static PlayerDataSnapshot FromPlayer(Player p)
        {
            PlayerDataSnapshot snapshot = new()
            {
                FetchedAt = DateTimeOffset.Now,
                LastAccessedAt = DateTimeOffset.Now
            };
            snapshot.CopyStatisticsFrom(p);
            return snapshot;
        }

        public void ApplyTo(Player p) => p.CopyStatisticsFrom(this);
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
