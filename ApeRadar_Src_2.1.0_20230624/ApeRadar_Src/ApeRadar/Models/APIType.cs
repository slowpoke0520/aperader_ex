using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ApeRadar.Models
{
    public enum APIType
    {
        VORTEX,
        WG_PUBLIC,
        WG_PUBLIC_WITH_YUYUKO_PROXY
    }

    public static class APITypeExt
    {
        public static APIType GetAPITypeByName(string name)
        {
            return name switch
            {
                "VORTEX" => APIType.VORTEX,
                "WG_PUBLIC" => APIType.WG_PUBLIC,
                "WG_PUBLIC_WITH_YUYUKO_PROXY" => APIType.WG_PUBLIC_WITH_YUYUKO_PROXY,
                _ => throw new ArgumentException(),
            };
        }
        public static string GetNameByAPIType(APIType type)
        {
            return type switch
            {
                APIType.VORTEX => "VORTEX",
                APIType.WG_PUBLIC => "WG_PUBLIC",
                APIType.WG_PUBLIC_WITH_YUYUKO_PROXY => "WG_PUBLIC_WITH_YUYUKO_PROXY",
                _ => throw new ArgumentException(),
            };
        }
    }
}
