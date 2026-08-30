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
using System.Text.RegularExpressions;

namespace ApeRadar.Utils
{
    static internal class SoftwareUpdateUtils
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/slowpoke0520/aperader_ex/releases/latest";
        private const string ReleaseAssetName = "ApeRadar-win-x64.zip";
        static string softwareLatestVersion = "";
        static string softwareLatestDate = "";
        static string softwareLatestUrl = "";
        static string softwareLatestFileName = "";
        static string softwareLasestSHA256 = "";
        static readonly string downloadDirectory = @".\Download";
        static readonly string[] occupiedFileList = { @".\ApeRadar.exe", @".\libSkiaSharp.dll" };
        private static bool updateInstallerStarted;

        public static async Task<bool> CheckForSoftwareUpdates()
        {
            try
            {
                JObject release = JsonUtils.Parse(await NetworkUtils.HttpGet(LatestReleaseApiUrl));
                string tagName = release["tag_name"]?.Value<string>() ?? throw new FileFormatException("FileFormatIncorrect");
                softwareLatestVersion = tagName.TrimStart('v', 'V');
                (Version latestVersion, int latestExRevision) = ParseReleaseVersion(softwareLatestVersion);
                (Version currentVersion, int currentExRevision) = ParseReleaseVersion(Properties.Settings.Default.SoftwareVersion);

                JObject? softwareAsset = release["assets"]?
                    .OfType<JObject>()
                    .FirstOrDefault(asset => asset["name"]?.Value<string>() == ReleaseAssetName);
                if (softwareAsset == null)
                {
                    throw new FileFormatException("FileFormatIncorrect");
                }
                softwareLatestUrl = GetSecureDownloadUrl(softwareAsset, "browser_download_url");
                softwareLatestFileName = ReleaseAssetName;
                string digest = softwareAsset["digest"]?.Value<string>() ?? "";
                softwareLasestSHA256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? digest[7..]
                    : "";
                string publishedAt = release["published_at"]?.ToString() ?? "";
                softwareLatestDate = DateTimeOffset.TryParse(publishedAt, out DateTimeOffset publishedDate)
                    ? publishedDate.ToString("yyyyMMdd")
                    : "";

                int versionComparison = latestVersion.CompareTo(currentVersion);
                if (versionComparison < 0 || versionComparison == 0 && latestExRevision <= currentExRevision)
                {
                    return false;
                }

                if (MessageBox.Show($"{Application.Current.FindResource("MsgBoxSoftwareUpdateFound") as string}\n{Application.Current.FindResource("MsgBoxCurrentVersion") as string} {Properties.Settings.Default.SoftwareVersion}\n{Application.Current.FindResource("MsgBoxLatestVersion") as string} {softwareLatestVersion} ({softwareLatestDate})\n{Application.Current.FindResource("MsgBoxUpdateComfirm") as string}", Application.Current.FindResource("MsgBoxUpdate") as string, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    NotificationMessageUtils.CreateMessage(MessageType.INFO, Application.Current.FindResource("NotificationMessageSoftwareUpdateDownloading") as string);
                    Directory.CreateDirectory(downloadDirectory);
                    string softwareArchive = Path.GetFullPath(Path.Combine(downloadDirectory, softwareLatestFileName));
                    await NetworkUtils.HttpDownloadFile(softwareLatestUrl, softwareArchive);
                    if (string.IsNullOrWhiteSpace(softwareLasestSHA256))
                    {
                        throw new FileFormatException("FileHashInvalid");
                    }
                    using (SHA256 sha = SHA256.Create())
                    using (FileStream fs = File.OpenRead(softwareArchive))
                    {
                        string actualHash = Convert.ToHexString(sha.ComputeHash(fs));
                        if (!actualHash.Equals(softwareLasestSHA256, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new FileFormatException("FileHashInvalid");
                        }
                    }

                    UpdateInstaller.Start(softwareArchive);
                    updateInstallerStarted = true;
                    Application.Current.Shutdown();
                }
                return true;
            }
            catch (Exception ex)
            {
                LogUtils.WriteError("", ex);
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
                if (!updateInstallerStarted && Directory.Exists(downloadDirectory))
                {
                    Directory.Delete(downloadDirectory, true);
                }
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
                if (Directory.Exists(downloadDirectory))
                {
                    Directory.Delete(downloadDirectory, true);
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

        private static (Version Core, int ExRevision) ParseReleaseVersion(string value)
        {
            Match match = Regex.Match(value, @"^(?<core>\d+\.\d+\.\d+)(?:-ex\.(?<revision>\d+))?$");
            if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out Version? core))
            {
                throw new FileFormatException("FileFormatIncorrect");
            }
            int revision = match.Groups["revision"].Success
                ? int.Parse(match.Groups["revision"].Value)
                : 0;
            return (core, revision);
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
