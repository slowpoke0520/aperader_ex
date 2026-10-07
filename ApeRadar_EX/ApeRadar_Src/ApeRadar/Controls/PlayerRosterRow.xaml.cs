using System.Windows;
using System.Windows.Controls;

namespace ApeRadar.Controls
{
    public partial class PlayerRosterRow : UserControl
    {
        public static readonly DependencyProperty IsMirroredProperty = DependencyProperty.Register(
            nameof(IsMirrored), typeof(bool), typeof(PlayerRosterRow), new PropertyMetadata(false));

        public bool IsMirrored
        {
            get => (bool)GetValue(IsMirroredProperty);
            set => SetValue(IsMirroredProperty, value);
        }

        public PlayerRosterRow()
        {
            InitializeComponent();
        }
    }
}
