using System.Windows;

namespace ApeRadar.Services
{
    internal sealed record DashboardLayout(bool CompactSidebar, bool CompactHeight, double SidebarWidth, Thickness PageMargin,
        double RowHeight, bool FitsFullRoster, double RosterHeight)
    {
        public const double MinimumRowHeight = 46;
        public const double PreferredRowHeight = 56;
        public const double TeamHeaderHeight = 36;
        public const double ColumnHeaderHeight = 28;
        public const double TopBarHeight = 40;
        public const double HeadingHeight = 36;
        public const double SummaryHeight = 70;
        public const double ToolbarHeight = 36;
    }

    internal static class DashboardLayoutCalculator
    {
        public static DashboardLayout Calculate(double width, double height)
        {
            bool compact = height < 800;
            Thickness margin = compact ? new Thickness(12, 6, 12, 6) : new Thickness(18, 8, 18, 8);
            double chrome = (compact ? 36 : DashboardLayout.TopBarHeight)
                + (compact ? 32 : DashboardLayout.HeadingHeight)
                + DashboardLayout.SummaryHeight + DashboardLayout.ToolbarHeight
                + margin.Top + margin.Bottom + 4;
            // Reserve native scrollbar space too, so the twelfth row stays whole
            // when a narrow table also needs horizontal scrolling.
            double gridChrome = DashboardLayout.TeamHeaderHeight + DashboardLayout.ColumnHeaderHeight + 2
                + SystemParameters.HorizontalScrollBarHeight + 2;
            double rowBudget = System.Math.Floor((height - chrome - gridChrome) / 12);
            double rowHeight = System.Math.Clamp(rowBudget, DashboardLayout.MinimumRowHeight, DashboardLayout.PreferredRowHeight);
            double rosterHeight = 12 * rowHeight + gridChrome;
            return new(width < 1400, compact, width < 1400 ? 64 : 208, margin, rowHeight,
                height - chrome >= rosterHeight, rosterHeight);
        }
    }
}
