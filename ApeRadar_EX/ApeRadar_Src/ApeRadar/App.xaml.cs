using System.Windows;
using RestoreWindowPlace;
using ApeRadar.Utils;
using System;
using ApeRadar.History;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ApeRadar
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private int crashDialogShown;
        public WindowPlace WindowPlace { get; }

        public App()
        {
            this.WindowPlace = new WindowPlace("placement.config");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string report = WriteCrashReport("UI", e.Exception);
            e.Handled = true;
            if (Interlocked.Exchange(ref crashDialogShown, 1) == 0)
            {
                MessageBox.Show(
                    $"程序遇到无法恢复的错误，将安全退出。\n版本：{ApeRadar.Properties.Settings.Default.SoftwareVersion} Build {ApeRadar.Properties.Settings.Default.SoftwareDate}\n阶段：UI\n诊断报告：{report}\n\nApeRadar encountered an unrecoverable error and will close.",
                    "ApeRadar EX", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(-1);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown fatal error");
            WriteCrashReport("AppDomain", exception);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteCrashReport("BackgroundTask", e.Exception);
            e.SetObserved();
        }

        internal static string WriteCrashReport(string stage, Exception exception)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApeRadar EX", "Diagnostics");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log");
                StringBuilder text = new();
                text.AppendLine($"Time: {DateTimeOffset.Now:O}");
                text.AppendLine($"Stage: {stage}");
                text.AppendLine($"Version: {ApeRadar.Properties.Settings.Default.SoftwareVersion} Build {ApeRadar.Properties.Settings.Default.SoftwareDate}");
                text.AppendLine($"OS: {Environment.OSVersion.VersionString}");
                text.AppendLine($"Runtime: {Environment.Version}");
                text.AppendLine();
                text.AppendLine(exception.ToString());
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
                LogUtils.WriteError($"Unhandled exception at {stage}; report: {path}", exception);
                return path;
            }
            catch
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApeRadar EX", "Diagnostics");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
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
