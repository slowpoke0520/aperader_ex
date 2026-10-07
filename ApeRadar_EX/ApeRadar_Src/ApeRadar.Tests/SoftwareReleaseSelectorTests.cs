using ApeRadar.Utils;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ApeRadar.Tests;

public sealed class SoftwareReleaseSelectorTests
{
    [Fact]
    public void StableChannel_IgnoresPrereleases()
    {
        JArray releases = new(
            Release("v2.1.1-ex.11-dev.2", prerelease: true),
            Release("v2.1.1-ex.10", prerelease: false));

        JObject selected = SoftwareReleaseSelector.SelectLatestRelease(releases, SoftwareUpdateChannel.Stable, "ApeRadar-win-x64.zip");

        Assert.Equal("v2.1.1-ex.10", selected["tag_name"]?.Value<string>());
    }

    [Fact]
    public void DevelopmentChannel_SelectsNewerPrerelease()
    {
        JArray releases = new(
            Release("v2.1.1-ex.10", prerelease: false),
            Release("v2.1.1-ex.11-dev.1", prerelease: true),
            Release("v2.1.1-ex.11-dev.2", prerelease: true));

        JObject selected = SoftwareReleaseSelector.SelectLatestRelease(releases, SoftwareUpdateChannel.Development, "ApeRadar-win-x64.zip");

        Assert.Equal("v2.1.1-ex.11-dev.2", selected["tag_name"]?.Value<string>());
    }

    [Fact]
    public void DevelopmentChannel_PrefersStableReleaseOfSameRevision()
    {
        JArray releases = new(
            Release("v2.1.1-ex.11-dev.8", prerelease: true),
            Release("v2.1.1-ex.11", prerelease: false));

        JObject selected = SoftwareReleaseSelector.SelectLatestRelease(releases, SoftwareUpdateChannel.Development, "ApeRadar-win-x64.zip");

        Assert.Equal("v2.1.1-ex.11", selected["tag_name"]?.Value<string>());
    }

    [Theory]
    [InlineData("2.1.1-ex.11-dev.2", "2.1.1-ex.10", true)]
    [InlineData("2.1.1-ex.11", "2.1.1-ex.11-rc.3", true)]
    [InlineData("2.1.1-ex.11-beta.2", "2.1.1-ex.11-beta.1", true)]
    [InlineData("2.1.1-ex.10", "2.1.1-ex.11-dev.1", false)]
    public void VersionComparison_HandlesDevelopmentStages(string latest, string current, bool expected)
    {
        Assert.Equal(expected, SoftwareReleaseSelector.IsNewer(latest, current));
    }

    [Fact]
    public void InvalidChannelSetting_DefaultsToStable()
    {
        Assert.Equal(SoftwareUpdateChannel.Stable, SoftwareReleaseSelector.ParseChannel("unexpected"));
        Assert.Equal(SoftwareReleaseSelector.StableSettingValue, SoftwareReleaseSelector.NormalizeChannelSetting(null));
    }

    [Fact]
    public void PublishedAt_AcceptsNewtonsoftDateTokenReturnedByGitHub()
    {
        JObject release = JObject.Parse("""{"published_at":"2026-09-14T07:40:29Z"}""");

        DateTimeOffset? publishedAt = SoftwareUpdateUtils.ParsePublishedAt(release["published_at"]);

        Assert.Equal(JTokenType.Date, release["published_at"]?.Type);
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T07:40:29Z"), publishedAt);
    }

    [Fact]
    public void PublishedAt_AcceptsStringTokenAndMissingValue()
    {
        JValue value = new("2026-09-14T07:40:29Z");

        Assert.Equal(DateTimeOffset.Parse("2026-09-14T07:40:29Z"), SoftwareUpdateUtils.ParsePublishedAt(value));
        Assert.Null(SoftwareUpdateUtils.ParsePublishedAt(null));
    }

    private static JObject Release(string tag, bool prerelease) => new()
    {
        ["tag_name"] = tag,
        ["draft"] = false,
        ["prerelease"] = prerelease,
        ["published_at"] = "2026-09-11T08:00:00Z",
        ["assets"] = new JArray(new JObject { ["name"] = "ApeRadar-win-x64.zip" })
    };
}
