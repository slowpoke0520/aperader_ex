using System.IO.Compression;
using Xunit;

namespace ApeRadar.Tests;

public sealed class UpdaterTransactionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"ApeRadar.Updater.Tests.{Guid.NewGuid():N}");

    public UpdaterTransactionTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Apply_UpdatesManagedFilesAndPreservesUserData()
    {
        string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        string install = Directory.CreateDirectory(Path.Combine(root, "install")).FullName;
        string backup = Path.Combine(root, "backup");

        Write(source, "ApeRadar.dll", "new managed file");
        Write(install, "ApeRadar.dll", "old managed file");
        Write(source, "WatchList.json", "release default");
        Write(install, "WatchList.json", "user watch list");
        Write(source, Path.Combine("Resources", "Json", "ships.json"), "release ships");
        Write(install, Path.Combine("Resources", "Json", "ships.json"), "newer user ships");
        Write(source, Path.Combine("Resources", "Json", "expected_values.json"), "release PR data");
        Write(install, Path.Combine("Resources", "Json", "expected_values.json"), "newer user PR data");
        Write(source, "PlayerDataCache.json", "release cache default");

        using UpdateFileTransaction transaction = new(source, install, backup, new UpdateStrings("ZH_CN"));
        transaction.Apply();
        transaction.Commit();

        Assert.Equal("new managed file", Read(install, "ApeRadar.dll"));
        Assert.Equal("user watch list", Read(install, "WatchList.json"));
        Assert.Equal("newer user ships", Read(install, Path.Combine("Resources", "Json", "ships.json")));
        Assert.Equal("newer user PR data", Read(install, Path.Combine("Resources", "Json", "expected_values.json")));
        Assert.Equal("release cache default", Read(install, "PlayerDataCache.json"));
    }

    [Fact]
    public void Apply_RollsBackOverwrittenAndNewFilesWhenCopyFails()
    {
        string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        string install = Directory.CreateDirectory(Path.Combine(root, "install")).FullName;
        string backup = Path.Combine(root, "backup");
        Write(source, "a-existing.dll", "new A");
        Write(install, "a-existing.dll", "old A");
        Write(source, "b-new.dll", "new B");
        Write(source, "z-fail.dll", "new Z");
        Write(install, "z-fail.dll", "old Z");

        using UpdateFileTransaction transaction = new(source, install, backup, new UpdateStrings("ZH_CN"), beforeCopy: relative =>
        {
            if (relative.Equals("z-fail.dll", StringComparison.OrdinalIgnoreCase)) throw new IOException("simulated locked file");
        });

        Assert.Throws<IOException>(() => transaction.Apply());
        Assert.Equal("old A", Read(install, "a-existing.dll"));
        Assert.Equal("old Z", Read(install, "z-fail.dll"));
        Assert.False(File.Exists(Path.Combine(install, "b-new.dll")));
    }

    [Fact]
    public void ExtractSafely_RejectsPathTraversal()
    {
        string archivePath = Path.Combine(root, "unsafe.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
            using StreamWriter writer = new(entry.Open());
            writer.Write("unsafe");
        }

        Assert.Throws<InvalidDataException>(() => UpdatePackage.ExtractSafely(archivePath, Path.Combine(root, "extract")));
        Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
    }

    [Fact]
    public void Apply_CancellationRollsBackChanges()
    {
        string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        string install = Directory.CreateDirectory(Path.Combine(root, "install")).FullName;
        string backup = Path.Combine(root, "backup");
        Write(source, "a-existing.dll", "new A");
        Write(install, "a-existing.dll", "old A");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        using UpdateFileTransaction transaction = new(source, install, backup, new UpdateStrings("ZH_CN"));

        Assert.Throws<OperationCanceledException>(() => transaction.Apply(cancellation.Token));
        Assert.True(transaction.RollbackSucceeded);
        Assert.Equal("old A", Read(install, "a-existing.dll"));
    }

    [Fact]
    public void UpdateRequest_RequiresHttpsAndSha256()
    {
        string[] valid =
        {
            "--apply-update", "123", "https://example.test/ApeRadar.zip", new string('a', 64),
            root, Environment.ProcessPath!, "2.1.1-ex.9", "ZH_CN"
        };

        Assert.True(UpdateRequest.TryParse(valid, out UpdateRequest? request));
        Assert.Equal("2.1.1-ex.9", request!.ExpectedVersion);

        string[] insecure = (string[])valid.Clone();
        insecure[2] = "http://example.test/ApeRadar.zip";
        Assert.False(UpdateRequest.TryParse(insecure, out _));

        string[] badHash = (string[])valid.Clone();
        badHash[3] = "not-a-sha256";
        Assert.False(UpdateRequest.TryParse(badHash, out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static void Write(string directory, string relativePath, string content)
    {
        string path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string directory, string relativePath) => File.ReadAllText(Path.Combine(directory, relativePath));
}
