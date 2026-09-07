using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

internal sealed record UpdateRequest(
    int ProcessId,
    string DownloadUrl,
    string ExpectedSha256,
    string InstallDirectory,
    string UpdaterPath,
    string ExpectedVersion,
    string Language)
{
    public static bool TryParse(string[] args, out UpdateRequest? request)
    {
        request = null;
        if (args.Length != 8 || args[0] != "--apply-update" || !int.TryParse(args[1], out int processId) || processId <= 0 ||
            !Uri.TryCreate(args[2], UriKind.Absolute, out Uri? downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps ||
            args[3].Length != 64 || args[3].Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        try
        {
            string updaterPath = Path.GetFullPath(args[5]);
            string? currentExecutable = Environment.ProcessPath;
            if (currentExecutable == null ||
                !Path.GetFullPath(currentExecutable).Equals(updaterPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            request = new UpdateRequest(
                processId,
                downloadUri.AbsoluteUri,
                args[3],
                Path.GetFullPath(args[4]),
                updaterPath,
                args[6],
                args[7]);
            return Directory.Exists(request.InstallDirectory) &&
                   !string.IsNullOrWhiteSpace(request.ExpectedVersion);
        }
        catch
        {
            return false;
        }
    }
}

internal readonly record struct UpdateProgressInfo(int Percent, string Status, string Detail = "", bool Indeterminate = false);

internal static class UpdateEngine
{
    public static void Apply(UpdateRequest request, UpdateStrings text, IProgress<UpdateProgressInfo>? progress = null, CancellationToken cancellationToken = default)
    {
        string workDirectory = Path.Combine(Path.GetTempPath(), $"ApeRadar.Update.{Guid.NewGuid():N}");
        string archivePath = Path.Combine(workDirectory, "ApeRadar-win-x64.zip");
        string extractDirectory = Path.Combine(workDirectory, "Package");
        string backupDirectory = Path.Combine(workDirectory, "Backup");
        bool preserveRecoveryDirectory = false;

        try
        {
            Report(progress, 3, text.WaitingForApp, text.WaitingDetail(0), true);
            EnsureMainProcessExited(request.ProcessId, request.InstallDirectory, text, progress, cancellationToken);

            Directory.CreateDirectory(workDirectory);
            Report(progress, 11, text.Downloading, "", true);
            DownloadPackage(request.DownloadUrl, archivePath, text, progress, cancellationToken);

            Report(progress, 30, text.CheckingDownload, "", true);
            VerifyHash(archivePath, request.ExpectedSha256, cancellationToken);

            Directory.CreateDirectory(extractDirectory);
            Report(progress, 32, text.Extracting, "", true);
            UpdatePackage.ExtractSafely(archivePath, extractDirectory, (done, total, file) =>
                Report(progress, 32 + Percent(done, total, 15), text.Extracting, text.FileDetail(done, total, file)), cancellationToken);

            string sourceDirectory = Path.Combine(extractDirectory, "ApeRadar");
            string sourceExecutable = Path.Combine(sourceDirectory, "ApeRadar.exe");
            if (!File.Exists(sourceExecutable))
            {
                throw new InvalidDataException("The update package does not contain ApeRadar/ApeRadar.exe.");
            }
            ValidateVersion(sourceExecutable, request.ExpectedVersion);

            using UpdateFileTransaction transaction = new(sourceDirectory, request.InstallDirectory, backupDirectory, text, progress);
            try
            {
                transaction.Apply(cancellationToken);
                string installedExecutable = Path.Combine(request.InstallDirectory, "ApeRadar.exe");
                Report(progress, 94, text.Verifying, request.ExpectedVersion, true);
                ValidateVersion(installedExecutable, request.ExpectedVersion);

                Report(progress, 98, text.Starting, installedExecutable, true);
                using Process? started = Process.Start(new ProcessStartInfo
                {
                    FileName = installedExecutable,
                    WorkingDirectory = request.InstallDirectory,
                    UseShellExecute = true
                });
                if (started == null)
                {
                    throw new InvalidOperationException("The updated ApeRadar process could not be started.");
                }
                if (started.WaitForExit(2000))
                {
                    throw new InvalidOperationException($"The updated ApeRadar process exited immediately with code {started.ExitCode}.");
                }

                transaction.Commit();
                Report(progress, 100, text.Completed, request.ExpectedVersion);
            }
            catch (Exception updateError)
            {
                Report(progress, 92, text.RollingBack, "", true);
                transaction.Rollback();
                if (!transaction.RollbackSucceeded)
                {
                    preserveRecoveryDirectory = true;
                    throw new AggregateException(
                        $"The update failed and automatic rollback was incomplete. Recovery files were preserved at {backupDirectory}.",
                        updateError,
                        transaction.RollbackError!);
                }
                throw;
            }
        }
        finally
        {
            if (!preserveRecoveryDirectory) TryDeleteDirectory(workDirectory);
        }
    }

    private static void DownloadPackage(string url, string destination, UpdateStrings text, IProgress<UpdateProgressInfo>? progress, CancellationToken cancellationToken)
    {
        using HttpClientHandler handler = new() { AutomaticDecompression = DecompressionMethods.All };
        using HttpClient client = new(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ApeRadar-Updater/2.0");
        using HttpRequestMessage message = new(HttpMethod.Get, url);
        using HttpResponseMessage response = client.Send(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        using Stream input = response.Content.ReadAsStream(cancellationToken);
        using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[128 * 1024];
        long downloaded = 0;
        while (true)
        {
            int read = input.ReadAsync(buffer, cancellationToken).AsTask().GetAwaiter().GetResult();
            if (read == 0) break;
            output.Write(buffer, 0, read);
            downloaded += read;
            int percent = total > 0 ? Math.Clamp((int)(downloaded * 100 / total.Value), 0, 100) : 0;
            Report(progress, 11 + Percent(percent, 100, 18), text.Downloading, text.DownloadDetail(downloaded, total), !total.HasValue);
        }
        output.Flush(true);
    }

    private static void VerifyHash(string archivePath, string expectedSha256, CancellationToken cancellationToken)
    {
        using FileStream stream = File.OpenRead(archivePath);
        byte[] digest = SHA256.HashDataAsync(stream, cancellationToken).AsTask().GetAwaiter().GetResult();
        if (!Convert.ToHexString(digest).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded update package failed SHA-256 verification.");
        }
    }

    public static string WriteErrorLog(Exception exception)
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApeRadar EX", "Update");
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "UpdateError.log");
            File.WriteAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
            return path;
        }
        catch
        {
            string path = Path.Combine(Path.GetTempPath(), "ApeRadar.UpdateError.log");
            try { File.WriteAllText(path, exception.ToString()); } catch { }
            return path;
        }
    }

    public static bool TryRestartExisting(UpdateRequest request)
    {
        try
        {
            using Process process = Process.GetProcessById(request.ProcessId);
            if (!process.HasExited && !process.WaitForExit(5000)) return true;
        }
        catch (ArgumentException) { }

        string executable = Path.Combine(request.InstallDirectory, "ApeRadar.exe");
        if (!File.Exists(executable)) return false;
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = request.InstallDirectory,
            UseShellExecute = true
        }) != null;
    }

    public static void ScheduleSelfDelete(string updaterPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{updaterPath}\""
            });
        }
        catch { }
    }

    private static void EnsureMainProcessExited(int processId, string installDirectory, UpdateStrings text, IProgress<UpdateProgressInfo>? progress, CancellationToken cancellationToken)
    {
        Process? process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; }
        using (process)
        {
            for (int elapsed = 0; elapsed < 30; elapsed++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.WaitForExit(1000)) return;
                Report(progress, 3 + elapsed / 4, text.WaitingForApp, text.WaitingDetail(elapsed + 1), true);
            }

            string expectedExecutable = Path.GetFullPath(Path.Combine(installDirectory, "ApeRadar.exe"));
            string? actualExecutable = null;
            try { actualExecutable = process.MainModule?.FileName; } catch { }
            if (actualExecutable == null || !Path.GetFullPath(actualExecutable).Equals(expectedExecutable, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The process that failed to exit is not the expected ApeRadar process; it will not be terminated.");
            }

            Report(progress, 10, text.ForcingAppClose, expectedExecutable, true);
            try { process.Kill(true); } catch (InvalidOperationException) { return; }
            if (!process.WaitForExit(15_000))
            {
                throw new TimeoutException("ApeRadar did not exit and could not be terminated safely.");
            }
        }
    }

    private static void ValidateVersion(string executable, string expectedVersion)
    {
        string actual = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "";
        string normalized = actual.Split('+', 2)[0];
        if (!normalized.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Update package version mismatch. Expected {expectedVersion}, found {actual}.");
        }
    }

    private static int Percent(int completed, int total, int range) => total <= 0 ? range : Math.Clamp(completed * range / total, 0, range);
    private static void Report(IProgress<UpdateProgressInfo>? progress, int percent, string status, string detail = "", bool indeterminate = false) =>
        progress?.Report(new UpdateProgressInfo(Math.Clamp(percent, 0, 100), status, detail, indeterminate));

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}

internal static class UpdatePackage
{
    public static void ExtractSafely(string archivePath, string destinationDirectory, Action<int, int, string>? progress = null, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        List<(ZipArchiveEntry Entry, string Destination)> entries = new();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.IsPathRooted(entry.FullName)) throw new InvalidDataException("Unsafe update archive path.");
            string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe update archive path.");
            entries.Add((entry, destination));
        }

        int completed = 0;
        foreach ((ZipArchiveEntry entry, string destination) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
            progress?.Invoke(++completed, entries.Count, entry.FullName);
        }
    }
}

internal sealed class UpdateFileTransaction : IDisposable
{
    private static readonly HashSet<string> PreservedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "WatchList.json", "PlayerDataCache.json", "PlayerIDCache.json", "EncounterHistory.json", "placement.config",
        Path.Combine("Resources", "Json", "ships.json"),
        Path.Combine("Resources", "Json", "expected_values.json")
    };
    private static readonly string[] PreservedDirectories = { "Log", "Screenshot" };

    private readonly string installDirectory;
    private readonly string backupDirectory;
    private readonly UpdateStrings text;
    private readonly IProgress<UpdateProgressInfo>? progress;
    private readonly Action<string>? beforeCopy;
    private readonly List<FilePlan> files;
    private bool applyStarted;
    private bool committed;
    private bool rolledBack;

    public bool RollbackSucceeded => rolledBack && RollbackError == null;
    public Exception? RollbackError { get; private set; }

    public UpdateFileTransaction(string sourceDirectory, string installDirectory, string backupDirectory, UpdateStrings text, IProgress<UpdateProgressInfo>? progress = null, Action<string>? beforeCopy = null)
    {
        this.installDirectory = Path.GetFullPath(installDirectory);
        this.backupDirectory = Path.GetFullPath(backupDirectory);
        this.text = text;
        this.progress = progress;
        this.beforeCopy = beforeCopy;
        files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .Select(source =>
            {
                string relative = Path.GetRelativePath(sourceDirectory, source);
                return new FilePlan(source, relative, Path.Combine(this.installDirectory, relative), Path.Combine(this.backupDirectory, relative));
            })
            .Where(file => !IsPreservedExisting(file.RelativePath, file.DestinationPath))
            .ToList();

        BackupExistingFiles();
    }

    public void Apply(CancellationToken cancellationToken = default)
    {
        applyStarted = true;
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FilePlan file = files[i];
                progress?.Report(new UpdateProgressInfo(56 + (files.Count == 0 ? 36 : i * 36 / files.Count), text.Installing, text.FileDetail(i + 1, files.Count, file.RelativePath)));
                beforeCopy?.Invoke(file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(file.DestinationPath)!);
                CopyWithRetry(file.SourcePath, file.DestinationPath);
            }
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public void Commit()
    {
        committed = true;
        TryDeleteDirectory(backupDirectory);
    }

    public void Rollback()
    {
        if (!applyStarted || committed || rolledBack) return;
        List<Exception> errors = new();
        foreach (FilePlan file in files.Where(file => !file.ExistedBefore).Reverse())
        {
            try { if (File.Exists(file.DestinationPath)) File.Delete(file.DestinationPath); }
            catch (Exception ex) { errors.Add(ex); }
        }
        foreach (FilePlan file in files.Where(file => file.ExistedBefore).Reverse())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.DestinationPath)!);
                CopyWithRetry(file.BackupPath, file.DestinationPath);
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        rolledBack = true;
        if (errors.Count > 0) RollbackError = new AggregateException(errors);
    }

    public void Dispose()
    {
        if (!committed && !rolledBack)
        {
            try { Rollback(); } catch { }
        }
        if (committed || RollbackSucceeded) TryDeleteDirectory(backupDirectory);
    }

    private void BackupExistingFiles()
    {
        int existingCount = files.Count(file => File.Exists(file.DestinationPath));
        int completed = 0;
        foreach (FilePlan file in files)
        {
            file.ExistedBefore = File.Exists(file.DestinationPath);
            if (!file.ExistedBefore) continue;
            progress?.Report(new UpdateProgressInfo(48 + (existingCount == 0 ? 7 : completed * 7 / existingCount), text.BackingUp, text.FileDetail(++completed, existingCount, file.RelativePath)));
            Directory.CreateDirectory(Path.GetDirectoryName(file.BackupPath)!);
            CopyWithRetry(file.DestinationPath, file.BackupPath);
        }
    }

    private static bool IsPreservedExisting(string relativePath, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return false;
        string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (PreservedFiles.Contains(normalized)) return true;
        return PreservedDirectories.Any(directory =>
            normalized.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyWithRetry(string source, string destination)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

    private sealed class FilePlan
    {
        public FilePlan(string sourcePath, string relativePath, string destinationPath, string backupPath)
        {
            SourcePath = sourcePath;
            RelativePath = relativePath;
            DestinationPath = destinationPath;
            BackupPath = backupPath;
        }

        public string SourcePath { get; }
        public string RelativePath { get; }
        public string DestinationPath { get; }
        public string BackupPath { get; }
        public bool ExistedBefore { get; set; }
    }
}
