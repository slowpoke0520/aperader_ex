using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace ApeRadar.Utils
{
    static internal class UpdateInstaller
    {
        private const string ApplyArgument = "--apply-update";
        private static readonly string[] PreservedNames =
        {
            "WatchList.json", "PlayerDataCache.json", "PlayerIDCache.json",
            "EncounterHistory.json", "placement.config", "Log", "Screenshot"
        };

        public static void Start(string archivePath)
        {
            string currentExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable");
            string updaterPath = Path.Combine(Path.GetTempPath(), $"ApeRadar.Updater.{Guid.NewGuid():N}.exe");
            File.Copy(currentExecutable, updaterPath, true);

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
                ArgumentList =
                {
                    ApplyArgument,
                    Process.GetCurrentProcess().Id.ToString(),
                    Path.GetFullPath(archivePath),
                    Path.GetFullPath(AppContext.BaseDirectory),
                    updaterPath
                }
            });
        }

        public static bool TryApplyFromCommandLine(string[] args)
        {
            if (args.Length != 5 || args[0] != ApplyArgument || !int.TryParse(args[1], out int processId))
            {
                return false;
            }

            string archivePath = Path.GetFullPath(args[2]);
            string installDirectory = Path.GetFullPath(args[3]);
            string updaterPath = Path.GetFullPath(args[4]);

            WaitForProcess(processId);
            string stagingDirectory = Path.Combine(Path.GetTempPath(), $"ApeRadar.Update.{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                ExtractSafely(archivePath, stagingDirectory);
                string sourceDirectory = Path.Combine(stagingDirectory, "ApeRadar");
                if (!File.Exists(Path.Combine(sourceDirectory, "ApeRadar.exe")))
                {
                    throw new InvalidDataException("The update package does not contain ApeRadar/ApeRadar.exe");
                }

                CopyDirectory(sourceDirectory, installDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(installDirectory, "ApeRadar.exe"),
                    WorkingDirectory = installDirectory,
                    UseShellExecute = true
                });
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
                TryDeleteFile(archivePath);
                string? downloadDirectory = Path.GetDirectoryName(archivePath);
                if (downloadDirectory != null && !Directory.EnumerateFileSystemEntries(downloadDirectory).Any())
                {
                    TryDeleteDirectory(downloadDirectory);
                }
                ScheduleSelfDelete(updaterPath);
            }
            return true;
        }

        private static void WaitForProcess(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                process.WaitForExit(30000);
            }
            catch (ArgumentException)
            {
            }
        }

        private static void ExtractSafely(string archivePath, string destinationDirectory)
        {
            string destinationRoot = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Unsafe update archive path");
                }
            }
            ZipFile.ExtractToDirectory(archivePath, destinationDirectory, true);
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, directory);
                if (!IsPreserved(relativePath))
                {
                    Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
                }
            }
            foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, file);
                if (!IsPreserved(relativePath))
                {
                    string destinationPath = Path.Combine(destinationDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(file, destinationPath, true);
                }
            }
        }

        private static bool IsPreserved(string relativePath)
        {
            string firstPart = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            return PreservedNames.Contains(firstPart, StringComparer.OrdinalIgnoreCase);
        }

        private static void ScheduleSelfDelete(string updaterPath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"/c ping 127.0.0.1 -n 2 > nul & del /f /q \"{updaterPath}\""
            });
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
