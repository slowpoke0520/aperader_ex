using System.Collections;
using ApeRadar.Models;

namespace ApeRadar.Utils.Sorters
{
    internal class CustomSorterByBattlesAscending : IComparer
    {
        public int Compare(object? x, object? y)
        {
            Player? a = x as Player;
            Player? b = y as Player;
            return a!.Battles > b!.Battles ? 1 : -1;
        }
    }
}
