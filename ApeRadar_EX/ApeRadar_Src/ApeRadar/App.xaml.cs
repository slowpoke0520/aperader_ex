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
            if (UpdateInstaller.TryApplyFromCommandLine(Environment.GetCommandLineArgs()[1..]))
            {
                Shutdown();
                return;
            }
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { HistoryServices.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception ex) { LogUtils.WriteError("Battle history shutdown failed.", ex); }
            base.OnExit(e);
            this.WindowPlace.Save();
        }
    }
}
