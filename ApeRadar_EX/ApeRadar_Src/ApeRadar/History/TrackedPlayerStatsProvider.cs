using ApeRadar.Models;
using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.History
{
    internal sealed class TrackedPlayerStatsProvider : ITrackedPlayerStatsProvider
    {
        private const string BuiltInApplicationId = "447ec579e994976e39dec0e7d0bac644";

        public async Task<ShipStatSnapshot?> GetCurrentShipStatsAsync(BattleRecord battle, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(battle.AccountId) || battle.AccountId.StartsWith("name:", StringComparison.OrdinalIgnoreCase) || battle.Server == "AUTO") return null;
            Server server = ServerExt.GetServerByName(battle.Server);
            APIType apiType = APITypeExt.GetAPITypeByName(Properties.Settings.Default.APITypeSelection);
            return apiType == APIType.VORTEX || server is Server.RU or Server.CN
                ? await GetVortexAsync(battle, server)
                : await GetWgAsync(battle, server, apiType == APIType.WG_PUBLIC_WITH_YUYUKO_PROXY);
        }

        private static async Task<ShipStatSnapshot?> GetVortexAsync(BattleRecord battle, Server server)
        {
            string domain = ServerExt.GetFullUrlStringByServer(server);
            JObject response = JsonUtils.Parse(await NetworkUtils.HttpGet($"https://vortex.{domain}/api/accounts/{battle.AccountId}/ships/pvp/"));
            JToken? pvp = response["data"]?[battle.AccountId]?["statistics"]?[battle.ShipId]?["pvp"];
            if (response["status"]?.Value<string>() != "ok" || pvp?.HasValues != true) return null;
            return CreateSnapshot(battle, "VORTEX", pvp, "battles_count");
        }

        private static async Task<ShipStatSnapshot?> GetWgAsync(BattleRecord battle, Server server, bool proxy)
        {
            string domain = ServerExt.GetFullUrlStringByServer(server);
            string serverName = battle.Server.ToLowerInvariant();
            string url = proxy
                ? $"https://dev-proxy.wows.shinoaki.com:7700/dev/wows/ships/stats/{serverName}/?fields=ship_id%2Cpvp.wins%2Cpvp.losses%2Cpvp.battles%2Cpvp.damage_dealt%2Cpvp.frags&account_id={battle.AccountId}&ship_id={battle.ShipId}"
                : $"https://api.{domain}/wows/ships/stats/?application_id={GetApplicationId()}&fields=ship_id%2Cpvp.wins%2Cpvp.losses%2Cpvp.battles%2Cpvp.damage_dealt%2Cpvp.frags&account_id={battle.AccountId}&ship_id={battle.ShipId}";
            JObject response = JsonUtils.Parse(await NetworkUtils.HttpGet(url));
            JArray? ships = response["data"]?[battle.AccountId] as JArray;
            JToken? pvp = ships?.FirstOrDefault(x => x["ship_id"]?.Value<string>() == battle.ShipId)?["pvp"];
            if (response["status"]?.Value<string>() != "ok" || pvp?.HasValues != true) return null;
            return CreateSnapshot(battle, proxy ? "WG_PROXY" : "WG_PUBLIC", pvp, "battles");
        }

        private static string GetApplicationId()
        {
            string configured = Properties.Settings.Default.WgApplicationId?.Trim() ?? "";
            return string.IsNullOrWhiteSpace(configured) ? BuiltInApplicationId : configured;
        }

        private static ShipStatSnapshot CreateSnapshot(BattleRecord battle, string provider, JToken pvp, string battlesField) => new()
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Provider = provider,
            AccountId = battle.AccountId,
            ShipId = battle.ShipId,
            Battles = pvp[battlesField]?.Value<double>() ?? 0,
            Wins = pvp["wins"]?.Value<double>() ?? 0,
            Losses = pvp["losses"]?.Value<double>(),
            Damage = pvp["damage_dealt"]?.Value<double>() ?? 0,
            Frags = pvp["frags"]?.Value<double>() ?? 0
        };
    }
}
