using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ApeRadar.Services
{
    // Offline package check: actual apphost, bundled JSON, native windows and theme.
    // It deliberately uses the existing UI-only composition path so CI does not
    // migrate user settings, import a real replay directory or contact providers.
    internal static class StartupVerifier
    {
        internal static void Run(App app)
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string original = Properties.Settings.Default.MainInterfaceStyle;
            string directory = AppContext.BaseDirectory;
            try
            {
                foreach (string file in new[] { "ships.json", "expected_values.json", "release_data.json" })
                {
                    string path = Path.Combine(directory, "Resources", "Json", file);
                    using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
                }
                if (app.TryFindResource("SurfaceBrush") is not Brush) throw new InvalidOperationException("Shared theme missing.");
                foreach (string style in new[] { "Dashboard", "Legacy" })
                {
                    Properties.Settings.Default.MainInterfaceStyle = style;
                    MainWindow window = new(app.HistoryServices, initializeRuntime: false)
                    { ShowInTaskbar = false, WindowState = WindowState.Normal, Width = 1280, Height = 720 };
                    app.MainWindow = window;
                    window.Show();
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    RenderTargetBitmap bitmap = new((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (FileStream stream = File.Create(Path.Combine(directory, $"startup-{style.ToLowerInvariant()}.png"))) encoder.Save(stream);
                    window.Close();
                }
                File.WriteAllText(Path.Combine(directory, "startup-verification.json"), JsonSerializer.Serialize(new
                { success = true, runtime = Environment.Version.ToString(), styles = new[] { "Dashboard", "Legacy" }, mode = "offline-ui", verifiedAt = DateTimeOffset.UtcNow }));
                app.Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(directory, "startup-verification.json"), JsonSerializer.Serialize(new
                { success = false, error = exception.ToString(), mode = "offline-ui" }));
                app.Shutdown(1);
            }
            finally { Properties.Settings.Default.MainInterfaceStyle = original; }
        }
    }
}
