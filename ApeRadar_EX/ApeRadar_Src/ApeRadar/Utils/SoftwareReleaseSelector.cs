using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ApeRadar.Utils
{
    internal enum SoftwareUpdateChannel
    {
        Stable,
        Development
    }

    internal readonly record struct SoftwareReleaseVersion(
        Version Core,
        int ExRevision,
        bool IsPrerelease,
        int StageRank,
        int StageRevision) : IComparable<SoftwareReleaseVersion>
    {
        public int CompareTo(SoftwareReleaseVersion other)
        {
            int comparison = Core.CompareTo(other.Core);
            if (comparison != 0) return comparison;
            comparison = ExRevision.CompareTo(other.ExRevision);
            if (comparison != 0) return comparison;
            if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1;
            comparison = StageRank.CompareTo(other.StageRank);
            return comparison != 0 ? comparison : StageRevision.CompareTo(other.StageRevision);
        }

        public SoftwareReleaseVersion AsPrerelease() => this with { IsPrerelease = true, StageRank = 0 };
    }

    internal static class SoftwareReleaseSelector
    {
        public const string StableSettingValue = "STABLE";
        public const string DevelopmentSettingValue = "DEVELOPMENT";

        private static readonly Regex VersionPattern = new(
            @"^v?(?<core>\d+\.\d+\.\d+)(?:-ex\.(?<revision>\d+))?(?:-(?<stage>dev|alpha|beta|rc)(?:[.-](?<stageRevision>\d+))?)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string NormalizeChannelSetting(string? value) =>
            value?.Equals(DevelopmentSettingValue, StringComparison.OrdinalIgnoreCase) == true
                ? DevelopmentSettingValue
                : StableSettingValue;

        public static SoftwareUpdateChannel ParseChannel(string? value) =>
            NormalizeChannelSetting(value) == DevelopmentSettingValue
                ? SoftwareUpdateChannel.Development
                : SoftwareUpdateChannel.Stable;

        public static JObject SelectLatestRelease(JArray releases, SoftwareUpdateChannel channel, string assetName)
        {
            List<(JObject Release, SoftwareReleaseVersion Version, DateTimeOffset PublishedAt)> candidates = new();
            foreach (JObject release in releases.OfType<JObject>())
            {
                if (release["draft"]?.Value<bool>() == true) continue;
                bool isPrerelease = release["prerelease"]?.Value<bool>() == true;
                if (channel == SoftwareUpdateChannel.Stable && isPrerelease) continue;
                if (release["assets"] is not JArray assets ||
                    !assets.OfType<JObject>().Any(asset => asset["name"]?.Value<string>() == assetName)) continue;

                string tagName = release["tag_name"]?.Value<string>() ?? "";
                if (!TryParseVersion(tagName, out SoftwareReleaseVersion version)) continue;
                if (isPrerelease && !version.IsPrerelease) version = version.AsPrerelease();
                DateTimeOffset publishedAt = DateTimeOffset.TryParse(release["published_at"]?.Value<string>(), out DateTimeOffset value)
                    ? value
                    : DateTimeOffset.MinValue;
                candidates.Add((release, version, publishedAt));
            }

            return candidates
                .OrderByDescending(candidate => candidate.Version)
                .ThenByDescending(candidate => candidate.PublishedAt)
                .Select(candidate => candidate.Release)
                .FirstOrDefault()
                ?? throw new FileFormatException("FileFormatIncorrect");
        }

        public static bool IsNewer(string latest, string current)
        {
            if (!TryParseVersion(latest, out SoftwareReleaseVersion latestVersion) ||
                !TryParseVersion(current, out SoftwareReleaseVersion currentVersion))
            {
                throw new FileFormatException("FileFormatIncorrect");
            }
            return latestVersion.CompareTo(currentVersion) > 0;
        }

        public static bool TryParseVersion(string? value, out SoftwareReleaseVersion version)
        {
            version = default;
            Match match = VersionPattern.Match(value?.Trim() ?? "");
            if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out Version? core)) return false;
            if (!int.TryParse(match.Groups["revision"].Success ? match.Groups["revision"].Value : "0", out int exRevision)) return false;
            if (!int.TryParse(match.Groups["stageRevision"].Success ? match.Groups["stageRevision"].Value : "0", out int stageRevision)) return false;

            string stage = match.Groups["stage"].Value.ToLowerInvariant();
            int stageRank = stage switch
            {
                "dev" => 0,
                "alpha" => 1,
                "beta" => 2,
                "rc" => 3,
                _ => 4
            };
            version = new SoftwareReleaseVersion(core, exRevision, match.Groups["stage"].Success, stageRank, stageRevision);
            return true;
        }
    }
}
