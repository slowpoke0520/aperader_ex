using System.Windows;
using Xunit;

namespace ApeRadar.Tests;

public sealed class HistoryWindowSmokeTests
{
    [Fact]
    public void HistoryWindow_ConstructsWithApplicationResources()
    {
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                Application app = new();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/ApeRadar;component/Resources/Localization/en-us.xaml", UriKind.Relative)
                });
                HistoryWindow window = new(initializeOnLoaded: false);
                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The history window constructor did not complete in time.");
        Assert.Null(error);
    }
}
