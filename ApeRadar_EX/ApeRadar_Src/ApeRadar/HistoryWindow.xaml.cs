using ApeRadar.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace ApeRadar
{
    public partial class HistoryWindow : Window
    {
        private readonly HistoryViewModel viewModel;
        private bool ready;

        public HistoryWindow()
        {
            InitializeComponent();
            viewModel = new HistoryViewModel(History.HistoryServices.Repository, History.HistoryServices.Analysis, History.HistoryServices.Coordinator);
            DataContext = viewModel;
            Loaded += HistoryWindow_Loaded;
            Closed += (_, _) => viewModel.Dispose();
        }

        private async void HistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await viewModel.InitializeAsync();
            ready = true;
        }

        private async void Server_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.RefreshDependentFiltersAsync(true, false);
        }

        private async void Account_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.RefreshDependentFiltersAsync(false, true);
        }

        private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.ReloadAsync();
        }

        private async void Date_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ready) await viewModel.ReloadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.ReloadAsync();
        private async void RetryReplay_Click(object sender, RoutedEventArgs e) => await viewModel.RetryFailedReplaysAsync();
        private void CancelImport_Click(object sender, RoutedEventArgs e) => viewModel.CancelReplayImport();
        private async void RetryApi_Click(object sender, RoutedEventArgs e) => await viewModel.RetryPendingAsync();
        private void OpenData_Click(object sender, RoutedEventArgs e) => viewModel.OpenDataDirectory();

        private async void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(FindResource("HistoryClearConfirmation") as string, FindResource("HistoryClear") as string,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                await viewModel.ClearAsync();
        }
    }
}
