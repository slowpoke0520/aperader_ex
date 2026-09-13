using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ApeRadar.ViewModels
{
    public enum RosterLoadState
    {
        Idle,
        Metadata,
        Cached,
        Refreshing,
        Complete,
        Partial,
        Failed
    }

    public sealed class RosterStatusViewModel : INotifyPropertyChanged
    {
        private RosterLoadState state;
        private string text = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public RosterLoadState State
        {
            get => state;
            private set
            {
                if (state == value) return;
                state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(Accent));
                OnPropertyChanged(nameof(Background));
            }
        }

        public string Text
        {
            get => text;
            private set
            {
                if (text == value) return;
                text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        public bool IsBusy => State is RosterLoadState.Metadata or RosterLoadState.Refreshing;
        public bool IsVisible => !string.IsNullOrWhiteSpace(Text);

        public Brush Accent => State switch
        {
            RosterLoadState.Complete => new SolidColorBrush(Color.FromRgb(38, 115, 77)),
            RosterLoadState.Partial => new SolidColorBrush(Color.FromRgb(148, 98, 0)),
            RosterLoadState.Failed => new SolidColorBrush(Color.FromRgb(179, 38, 30)),
            _ => new SolidColorBrush(Color.FromRgb(36, 107, 206))
        };

        public Brush Background => State switch
        {
            RosterLoadState.Complete => new SolidColorBrush(Color.FromRgb(235, 247, 240)),
            RosterLoadState.Partial => new SolidColorBrush(Color.FromRgb(255, 247, 226)),
            RosterLoadState.Failed => new SolidColorBrush(Color.FromRgb(253, 238, 237)),
            _ => new SolidColorBrush(Color.FromRgb(234, 242, 253))
        };

        public void Set(RosterLoadState newState, string message)
        {
            State = newState;
            Text = message ?? "";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
