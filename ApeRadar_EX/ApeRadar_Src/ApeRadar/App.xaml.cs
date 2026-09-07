using System.Windows;
using RestoreWindowPlace;
using ApeRadar.Utils;
using System;
using ApeRadar.History;

namespace ApeRadar
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public WindowPlace WindowPlace { get; }

        public App()
        {
            this.WindowPlace = new WindowPlace("placement.config");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var disposeTask = HistoryServices.DisposeAsync().AsTask();
                if (!disposeTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    LogUtils.WriteInfo("Battle history shutdown timed out; process exit will release remaining resources.");
                }
                else
                {
                    disposeTask.GetAwaiter().GetResult();
                }
            }
            catch (Exception ex) { LogUtils.WriteError("Battle history shutdown failed.", ex); }
            base.OnExit(e);
            this.WindowPlace.Save();
        }
    }
}
