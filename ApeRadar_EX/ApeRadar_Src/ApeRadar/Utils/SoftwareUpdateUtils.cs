using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.IO.Compression;
using System.Security.Cryptography;
using ApeRadar.Models;
using System.Diagnostics;
using System.Threading;
using System.Net.Http;

namespace ApeRadar.Utils
{
    internal enum SoftwareUpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        UpdateStarted,
        AlreadyRunning,
        NetworkError,
        RateLimited,
        InvalidFeed,
        MissingAsset,
        HashInvalid,
        Cancelled
    }

    internal sealed record SoftwareUpdateCheckResult(
        SoftwareUpdateCheckStatus Status,
        string CurrentVersion,
        string? AvailableVersion,
        SoftwareUpdateChannel Channel,
        DateTimeOffset CheckedAt)
    {
        public bool HasUpdate => Status is SoftwareUpdateCheckStatus.UpdateAvailable or SoftwareUpdateCheckStatus.UpdateStarted;
        public DateTimeOffset? PublishedAt { get; init; }
        public string ReleaseNotes { get; init; } = "";
    }

    static internal class SoftwareUpdateUtils
    {
        private const string LatestStableReleaseApiUrl = "https://api.github.com/repos/slowpoke0520/aperader_ex/releases/latest";
        private const string ReleasesApiUrl = "https://api.github.com/repos/slowpoke0520/aperader_ex/releases?per_page=100";
        public const string ReleaseNotesUrl = "https://github.com/slowpoke0520/aperader_ex/releases";
        private const string ReleaseAssetName = "ApeRadar-win-x64.zip";
        static readonly string[] occupiedFileList = { @".\ApeRadar.exe", @".\libSkiaSharp.dll" };
        private static readonly SemaphoreSlim SoftwareUpdateGate = new(1, 1);

        public static async Task<SoftwareUpdateCheckResult> CheckForSoftwareUpdates(bool installWhenFound = true, CancellationToken cancellationToken = default)
        {
            SoftwareUpdateChannel channel = SoftwareReleaseSelector.ParseChannel(Properties.Settings.Default.SoftwareUpdateChannel);
            string currentVersion = Properties.Settings.Default.SoftwareVersion;
            if (!await SoftwareUpdateGate.WaitAsync(0, cancellationToken))
            {
                NotificationMessageUtils.CreateMessage(MessageType.INFO, Application.Current.FindResource("NotificationMessageSoftwareUpdateAlreadyRunning") as string);
                return new(SoftwareUpdateCheckStatus.AlreadyRunning, currentVersion, null, channel, DateTimeOffset.Now);
            }

            try
            {
                JArray releases;
                try
                {
                    string releaseJson = await NetworkUtils.HttpGet(channel == SoftwareUpdateChannel.Stable
                        ? LatestStableReleaseApiUrl
                        : ReleasesApiUrl, cancellationToken);
                    JToken response = JToken.Parse(releaseJson);
                    releases = response switch
                    {
                        JObject singleRelease => new JArray(singleRelease),
                        JArray releaseList => releaseList,
                        _ => throw new FileFormatException("FileFormatIncorrect")
                    };
                }
                catch (Exception ex) when (ex is not System.Net.Http.HttpRequestException)
                {
                    throw new FileFormatException("FileFormatIncorrect", ex);
                }
                JObject release = SoftwareReleaseSelector.SelectLatestRelease(releases, channel, ReleaseAssetName);
                string tagName = release["tag_name"]?.Value<string>() ?? throw new FileFormatException("FileFormatIncorrect");
                string softwareLatestVersion = tagName.TrimStart('v', 'V');
                DateTimeOffset? publishedAt = release["published_at"]?.Value<DateTimeOffset?>();
                string releaseNotes = release["body"]?.Value<string>() ?? "";

                JObject? softwareAsset = release["assets"]?
                    .OfType<JObject>()
                    .FirstOrDefault(asset => asset["name"]?.Value<string>() == ReleaseAssetName);
                if (softwareAsset == null)
                {
                    NotificationMessageUtils.CreateMessage(MessageType.ERROR, Application.Current.FindResource("NotificationMessageUpdateAssetMissing") as string);
                    return new(SoftwareUpdateCheckStatus.MissingAsset, currentVersion, softwareLatestVersion, channel, DateTimeOffset.Now)
                    {
                        PublishedAt = publishedAt,
                        ReleaseNotes = releaseNotes
                    };
                }
                string softwareLatestUrl = GetSecureDownloadUrl(softwareAsset, "browser_download_url");
                string digest = softwareAsset["digest"]?.Value<string>() ?? "";
                string softwareLatestSHA256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest[7..]
                    : "";
                if (string.IsNullOrWhiteSpace(softwareLatestSHA256))
                {
                    throw new FileFormatException("FileHashInvalid");
                }

                if (!SoftwareReleaseSelector.IsNewer(softwareLatestVersion, currentVersion))
                {
                    return new(SoftwareUpdateCheckStatus.UpToDate, currentVersion, softwareLatestVersion, channel, DateTimeOffset.Now)
                    {
                        PublishedAt = publishedAt,
                        ReleaseNotes = releaseNotes
                    };
                }

                if (!installWhenFound)
                {
                    string channelName = Application.Current.FindResource(channel == SoftwareUpdateChannel.Development
                        ? "ComboBoxItemSoftwareUpdateChannelDevelopment"
                        : "ComboBoxItemSoftwareUpdateChannelStable") as string ?? channel.ToString();
                    string messageFormat = Application.Current.FindResource("NotificationMessageSoftwareUpdateAvailable") as string
                        ?? "Update {0} is available on the {1} channel.";
                    NotificationMessageUtils.CreateMessage(MessageType.INFO, string.Format(messageFormat, softwareLatestVersion, channelName));
                    return new(SoftwareUpdateCheckStatus.UpdateAvailable, currentVersion, softwareLatestVersion, channel, DateTimeOffset.Now)
                    {
                        PublishedAt = publishedAt,
                        ReleaseNotes = releaseNotes
                    };
                }

                NotificationMessageUtils.CreateMessage(MessageType.INFO, Application.Current.FindResource("NotificationMessageSoftwareUpdateDownloading") as string);
                UpdateInstaller.Start(softwareLatestUrl, softwareLatestSHA256, softwareLatestVersion, Properties.Settings.Default.Language);
                Application.Current.Shutdown();
                return new(SoftwareUpdateCheckStatus.UpdateStarted, currentVersion, softwareLatestVersion, channel, DateTimeOffset.Now)
                {
                    PublishedAt = publishedAt,
                    ReleaseNotes = releaseNotes
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(SoftwareUpdateCheckStatus.Cancelled, currentVersion, null, channel, DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("", ex);
                SoftwareUpdateCheckStatus status = ex switch
                {
                    NetworkRequestException network when network.FailureKind == ApiFailureKind.RateLimited => SoftwareUpdateCheckStatus.RateLimited,
                    HttpRequestException => SoftwareUpdateCheckStatus.NetworkError,
                    FileFormatException when ex.Message == "FileHashInvalid" => SoftwareUpdateCheckStatus.HashInvalid,
                    FileFormatException => SoftwareUpdateCheckStatus.InvalidFeed,
                    _ => SoftwareUpdateCheckStatus.InvalidFeed
                };
                string resourceKey = status switch
                {
                    SoftwareUpdateCheckStatus.RateLimited => "NotificationMessageUpdateRateLimited",
                    SoftwareUpdateCheckStatus.NetworkError => "NotificationMessageUpdateConnectionError",
                    SoftwareUpdateCheckStatus.HashInvalid => "NotificationMessageUpdateFileHashError",
                    SoftwareUpdateCheckStatus.InvalidFeed => "NotificationMessageUpdateFeedInvalid",
                    _ => "NotificationMessageOtherError"
                };
                NotificationMessageUtils.CreateMessage(MessageType.ERROR, Application.Current.FindResource(resourceKey) as string);
                return new(status, currentVersion, null, channel, DateTimeOffset.Now);
            }
            finally
            {
                SoftwareUpdateGate.Release();
            }
        }

        public static void CleanOldVersionFiles()
        {
            foreach (string filename in occupiedFileList)
            {
                if (File.Exists($"{filename}.bak"))
                {
                    File.Delete($"{filename}.bak");
                }
            }
        }

        public static async Task<bool> CheckForShipListUpdates()
        {
            const string updateInfoUrl = "https://lxdev.org/aperadar/updateinfo/";
            string? downloadDirectory = null;
            try
            {
                JObject updateInfo = JsonUtils.Parse(await NetworkUtils.HttpGet(updateInfoUrl));
                string latestVersion = updateInfo["shiplist_latest_version"]?.Value<string>() ?? throw new FileFormatException("FileFormatIncorrect");
                string latestDate = updateInfo["shiplist_latest_date"]?.Value<string>() ?? throw new FileFormatException("FileFormatIncorrect");
                string downloadUrl = GetSecureDownloadUrl(updateInfo, "shiplist_latest_url");
                string expectedHash = updateInfo["shiplist_latest_sha256"]?.Value<string>() ?? throw new FileFormatException("FileFormatIncorrect");

                if (!int.TryParse(latestDate, out int latestDateValue) ||
                    !int.TryParse(ShipInfoUtils.GetShipInfoDate(), out int currentDateValue))
                {
                    throw new FileFormatException("FileFormatIncorrect");
                }
                if (latestDateValue <= currentDateValue)
                {
                    return false;
                }

                if (MessageBox.Show($"{Application.Current.FindResource("MsgBoxShiplistUpdateFound") as string}\n{Application.Current.FindResource("MsgBoxCurrentVersion") as string} {ShipInfoUtils.GetShipInfoVersion()} ({ShipInfoUtils.GetShipInfoDate()})\n{Application.Current.FindResource("MsgBoxLatestVersion") as string} {latestVersion} ({latestDate})\n{Application.Current.FindResource("MsgBoxUpdateComfirm") as string}", Application.Current.FindResource("MsgBoxUpdate") as string, MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                {
                    return true;
                }

                NotificationMessageUtils.CreateMessage(MessageType.INFO, Application.Current.FindResource("NotificationMessageShiplistUpdateDownloading") as string);
                downloadDirectory = Path.Combine(Path.GetTempPath(), $"ApeRadar.ShipList.{Guid.NewGuid():N}");
                Directory.CreateDirectory(downloadDirectory);
                string archivePath = Path.GetFullPath(Path.Combine(downloadDirectory, Path.GetFileName(new Uri(downloadUrl).LocalPath)));
                await NetworkUtils.HttpDownloadFile(downloadUrl, archivePath);
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(archivePath))
                {
                    string actualHash = Convert.ToHexString(sha.ComputeHash(fs));
                    if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FileFormatException("FileHashInvalid");
                    }
                }

                const string shipListDirectory = @".\Resources\Json\";
                ValidateArchiveEntries(archivePath, shipListDirectory);
                ZipFile.ExtractToDirectory(archivePath, shipListDirectory, true);
                ShipInfoUtils.ReadShipInfoFile(Path.Combine(shipListDirectory, "ships.json"));
                NotificationMessageUtils.CreateMessage(MessageType.INFO, Application.Current.FindResource("NotificationMessageShiplistUpdateComplete") as string);
                MessageBox.Show(Application.Current.FindResource("MsgBoxShiplistUpdateComplete") as string, Application.Current.FindResource("MsgBoxUpdate") as string, MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("Ship list update failed", ex);
                _ = ex.Message switch
                {
                    "HttpRequestFailed" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, Application.Current.FindResource("NotificationMessageUpdateConnectionError") as string),
                    "FileHashInvalid" => NotificationMessageUtils.CreateMessage(MessageType.ERROR, Application.Current.FindResource("NotificationMessageUpdateFileHashError") as string),
                    _ => NotificationMessageUtils.CreateMessage(MessageType.ERROR, Application.Current.FindResource("NotificationMessageOtherError") as string),
                };
                return true;
            }
            finally
            {
                if (downloadDirectory != null && Directory.Exists(downloadDirectory))
                {
                    try { Directory.Delete(downloadDirectory, true); } catch { }
                }
            }
        }

        private static string GetSecureDownloadUrl(JObject updateInfo, string propertyName)
        {
            string value = updateInfo[propertyName]?.Value<string>() ?? throw new FileFormatException("FileFormatIncorrect");
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new FileFormatException("FileFormatIncorrect");
            }
            return uri.AbsoluteUri;
        }

        private static void ValidateArchiveEntries(string archivePath, string destinationDirectory)
        {
            string destinationRoot = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new FileFormatException("FileFormatIncorrect");
                }
            }
        }

    }
}
