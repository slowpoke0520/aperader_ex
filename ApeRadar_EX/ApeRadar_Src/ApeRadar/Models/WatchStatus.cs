using System;

namespace ApeRadar.Models
{
    public enum WatchStatus
    {
        NONE,
        POSITIVE,
        NEGTIVE,
        CHEATER
    }

    public static class WatchStatusExt
    {
        public static string GetNameByStatus(WatchStatus status)
        {
            return status switch
            {
                WatchStatus.NONE => "None",
                WatchStatus.POSITIVE => "Positive",
                WatchStatus.NEGTIVE => "Negtive",
                WatchStatus.CHEATER => "Cheater",
                _ => "None",
            };
        }

        public static WatchStatus GetStatusByName(string name)
        {
            return name switch
            {
                "None" => WatchStatus.NONE,
                "Positive" => WatchStatus.POSITIVE,
                "Negtive" => WatchStatus.NEGTIVE,
                "Cheater" => WatchStatus.CHEATER,
                _ => WatchStatus.NONE,
            };
        }
    }
}
