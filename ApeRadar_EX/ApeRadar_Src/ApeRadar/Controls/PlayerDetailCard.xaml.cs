using System;
using System.Windows;
using System.Windows.Controls;

namespace ApeRadar.Controls
{
    public partial class PlayerDetailCard : UserControl
    {
        public PlayerDetailCard()
        {
            InitializeComponent();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            // Measure the complete formatted values before deciding whether both tables fit.
            Size unconstrained = new(double.PositiveInfinity, double.PositiveInfinity);
            AccountSection.Measure(unconstrained);
            ShipSection.Measure(unconstrained);
            double columnWidth = Math.Max(AccountSection.DesiredSize.Width, ShipSection.DesiredSize.Width);
            bool stacked = constraint.Width < columnWidth * 2 + 8;
            Grid.SetColumn(ShipSection, stacked ? 0 : 2);
            Grid.SetRow(ShipSection, stacked ? 1 : 0);
            StatisticsGrid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 8);
            StatisticsGrid.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            return base.MeasureOverride(constraint);
        }
    }
}
