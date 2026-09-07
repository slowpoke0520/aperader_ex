using System;
using System.Diagnostics;
using System.IO;

namespace ApeRadar.Utils
{
    static internal class UpdateInstaller
    {
        private const string ApplyArgument = "--apply-update";

        public static void Start(string downloadUrl, string expectedSha256, string expectedVersion, string language)
        {
            string bundledUpdater = Path.Combine(AppContext.BaseDirectory, "ApeRadar.Updater.exe");
            if (!File.Exists(bundledUpdater))
            {
                throw new FileNotFoundException("The bundled ApeRadar updater is missing.", bundledUpdater);
            }

            string updaterPath = Path.Combine(Path.GetTempPath(), $"ApeRadar.Updater.{Guid.NewGuid():N}.exe");
            File.Copy(bundledUpdater, updaterPath, true);

            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
                ArgumentList =
                {
                    ApplyArgument,
                    Process.GetCurrentProcess().Id.ToString(),
                    downloadUrl,
                    expectedSha256,
                    Path.GetFullPath(AppContext.BaseDirectory),
                    updaterPath,
                    expectedVersion,
                    language
                }
            });
            if (process == null)
            {
                throw new InvalidOperationException("Unable to start the ApeRadar updater.");
            }
        }
    }
}
