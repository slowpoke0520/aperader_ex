using ApeRadar.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace ApeRadar.Utils
{
    static internal class WatchListUtils
    {
        public static void CreateNewWatchList(string filename)
        {
            JObject JObjectWatchList = JsonUtils.Parse("{\"RU\":{},\"EU\":{},\"NA\":{},\"ASIA\":{},\"CN\":{}}");
            WriteWatchList(filename, JObjectWatchList);
        }

        public static JObject ReadWatchList(string filename)
        {
            if (!File.Exists(filename))
            {
                CreateNewWatchList(filename);
            }
            using FileStream fs = new(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader sr = new(fs);
            string strWatchList = sr.ReadToEnd();
            return JsonUtils.Parse(strWatchList);
        }

        public static void SaveWatchList(Player p, string filename)
        {
            JObject JObjectWatchList = ReadWatchList(filename);

            if (JObjectWatchList[ServerExt.GetNameByServer(p.Server)]!.SelectToken(p.ID) != null)
            {
                if (p.WatchStatus == WatchStatus.NONE && string.IsNullOrEmpty(p.Note))
                {
                    JObject? JObjectToUpdate = JObjectWatchList[ServerExt.GetNameByServer(p.Server)] as JObject;
                    JObjectToUpdate!.Remove(p.ID);
                }
                else
                {
                    JObjectWatchList[ServerExt.GetNameByServer(p.Server)]![p.ID]!["status"] = WatchStatusExt.GetNameByStatus(p.WatchStatus);
                    if (string.IsNullOrEmpty(p.Note))
                    {
                        (JObjectWatchList[ServerExt.GetNameByServer(p.Server)]![p.ID] as JObject)!.Remove("note");
                    }
                    else
                    {
                        JObjectWatchList[ServerExt.GetNameByServer(p.Server)]![p.ID]!["note"] = p.Note;
                    }
                }
            }
            else
            {
                if (p.WatchStatus != WatchStatus.NONE || !string.IsNullOrEmpty(p.Note))
                {
                    JObject JObjectPlayer = new()
                    {
                        ["name"] = p.Name,
                        ["status"] = WatchStatusExt.GetNameByStatus(p.WatchStatus)
                    };
                    if (!string.IsNullOrEmpty(p.Note))
                    {
                        JObjectPlayer["note"] = p.Note;
                    }
                    JObject? JObjectToUpdate = JObjectWatchList[ServerExt.GetNameByServer(p.Server)] as JObject;
                    JObjectToUpdate!.Add(p.ID, JObjectPlayer);
                }
            }
            WriteWatchList(filename, JObjectWatchList);
        }

        public static void SaveWatchListNote(Player p, string filename)
        {
            JObject JObjectWatchList = ReadWatchList(filename);
            JObject? JObjectServer = JObjectWatchList[ServerExt.GetNameByServer(p.Server)] as JObject;
            JToken? JObjectPlayer = JObjectServer!.SelectToken(p.ID);

            if (string.IsNullOrEmpty(p.Note))
            {
                if (JObjectPlayer != null)
                {
                    (JObjectPlayer as JObject)!.Remove("note");
                    if (JObjectPlayer["status"]?.Value<string>() == WatchStatusExt.GetNameByStatus(WatchStatus.NONE))
                    {
                        JObjectServer.Remove(p.ID);
                    }
                }
            }
            else
            {
                if (JObjectPlayer != null)
                {
                    JObjectPlayer["note"] = p.Note;
                }
                else
                {
                    JObject JObjectNewPlayer = new()
                    {
                        ["name"] = p.Name,
                        ["status"] = WatchStatusExt.GetNameByStatus(WatchStatus.NONE)
                    };
                    JObjectNewPlayer["note"] = p.Note;
                    JObjectServer.Add(p.ID, JObjectNewPlayer);
                }
            }

            WriteWatchList(filename, JObjectWatchList);
        }

        public static string GetPlayerNote(JObject JObjectWatchList, Server server, string playerID)
        {
            JToken? JObjectPlayer = JObjectWatchList[ServerExt.GetNameByServer(server)]!.SelectToken(playerID);
            if (JObjectPlayer == null)
            {
                return "";
            }
            return JObjectPlayer["note"]?.Value<string>() ?? "";
        }

        private static void WriteWatchList(string filename, JObject watchList)
        {
            AtomicFileUtils.WriteAllText(filename, JsonConvert.SerializeObject(watchList, Formatting.Indented));
        }
    }
}
