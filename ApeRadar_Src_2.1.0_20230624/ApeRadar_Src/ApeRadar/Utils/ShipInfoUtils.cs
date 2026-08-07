using ApeRadar.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace ApeRadar.Utils
{
    static internal class ShipInfoUtils
    {
        public static JObject? ShipInfo;

        public static void ReadShipInfoFile(string filename)
        {
            using FileStream fs = new(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader sr = new(fs);
            string ShipInfoStr = sr.ReadToEnd();
            ShipInfo = JsonUtils.Parse(ShipInfoStr);
        }

        public static string GetShipInfoVersion()
        {
            return ShipInfo!["version"]!.Value<string>()!;
        }

        public static string GetShipInfoDate()
        {
            return ShipInfo!["date"]!.Value<string>()!;
        }

        public static string GetShipNameByID(string ID, Language language)
        {
            string? strUnknownShip = Application.Current.FindResource("StringUnknownShip") as string;
            if (((JObject)ShipInfo!["ships"]!).ContainsKey(ID))
            {
                return language switch
                {
                    Language.AUTO => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
                    {
                        "zh" => ShipInfo["ships"]![ID]!["name_zh-cn"]!.Value<string>()!,
                        _ => ShipInfo["ships"]![ID]!["name_en-us"]!.Value<string>()!,
                    },
                    Language.EN_US => ShipInfo["ships"]![ID]!["name_en-us"]!.Value<string>()!,
                    Language.ZH_CN => ShipInfo["ships"]![ID]!["name_zh-cn"]!.Value<string>()!,
                    _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
                    {
                        "zh" => ShipInfo["ships"]![ID]!["name_zh-cn"]!.Value<string>()!,
                        _ => ShipInfo["ships"]![ID]!["name_en-us"]!.Value<string>()!,
                    },
                };
            }
            else
            {
                return strUnknownShip!;
            }
        }

        public static string GetShipTypeByID(string ID)
        {
            if (((JObject)ShipInfo!["ships"]!).ContainsKey(ID))
            {
                return ShipInfo["ships"]![ID]!["type"]!.Value<string>()!;
            }
            else
            {
                return "Unknown";
            }
        }

        public static int GetShipTierByID(string ID)
        {
            if (((JObject)ShipInfo!["ships"]!).ContainsKey(ID))
            {
                return Convert.ToInt32(ShipInfo["ships"]![ID]!["tier"]!);
            }
            else
            {
                return 0;
            }
        }
    }
}
