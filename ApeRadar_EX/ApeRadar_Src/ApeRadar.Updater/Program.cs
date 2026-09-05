using System.Diagnostics;
using System.IO.Compression;

internal static class Program
{
    private static readonly string[] PreservedNames =
    {
        "WatchList.json", "PlayerDataCache.json", "PlayerIDCache.json",
        "EncounterHistory.json", "placement.config", "Log", "Screenshot"
    };

    private static int Main(string[] args)
    {
        if (args.Length != 5 || args[0] != "--apply-update" || !int.TryParse(args[1], out int processId)) return 2;
        string archivePath = Path.GetFullPath(args[2]);
        string installDirectory = Path.GetFullPath(args[3]);
        string updaterPath = Path.GetFullPath(args[4]);
        string stagingDirectory = Path.Combine(Path.GetTempPath(), $"ApeRadar.Update.{Guid.NewGuid():N}");
        try
        {
            WaitForProcess(processId);
            Directory.CreateDirectory(stagingDirectory);
            ExtractSafely(archivePath, stagingDirectory);
            string sourceDirectory = Path.Combine(stagingDirectory, "ApeRadar");
            if (!File.Exists(Path.Combine(sourceDirectory, "ApeRadar.exe"))) throw new InvalidDataException("The update archive does not contain ApeRadar/ApeRadar.exe.");
            CopyDirectory(sourceDirectory, installDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installDirectory, "ApeRadar.exe"),
                WorkingDirectory = installDirectory,
                UseShellExecute = true
            });
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(installDirectory, "UpdateError.log"), ex.ToString()); } catch { }
            return 1;
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            TryDeleteFile(archivePath);
            ScheduleSelfDelete(updaterPath);
        }
    }

    private static void WaitForProcess(int processId)
    {
        try { using Process process = Process.GetProcessById(processId); process.WaitForExit(60_000); }
        catch (ArgumentException) { }
    }

    private static void ExtractSafely(string archivePath, string destinationDirectory)
    {
        string root = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe update archive path.");
        }
        ZipFile.ExtractToDirectory(archivePath, destinationDirectory, true);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, directory);
            if (!IsPreserved(relative)) Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }
        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            if (IsPreserved(relative)) continue;
            string destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static bool IsPreserved(string relativePath)
    {
        string first = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return PreservedNames.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    private static void ScheduleSelfDelete(string updaterPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe", UseShellExecute = false, CreateNoWindow = true,
                Arguments = $"/c ping 127.0.0.1 -n 2 > nul & del /f /q \"{updaterPath}\""
            });
        }
        catch { }
    }

    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
