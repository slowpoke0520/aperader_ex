using ApeRadar.Services;
using Xunit;

namespace ApeRadar.Tests;

public class SettingsDraftTests
{
    [Fact]
    public void Save_AppliesStagedValuesUsingExistingSettingNames()
    {
        ApeRadar.Properties.Settings settings = new();
        string originalServer = settings.Server;
        bool originalIcon = settings.ShowShipTypeIcon;
        SettingsDraft draft = new(settings);
        draft.Set(nameof(settings.Server), "NA");
        draft.Set(nameof(settings.ShowShipTypeIcon), !originalIcon);

        Assert.Equal(originalServer, settings.Server);
        Assert.Equal(originalIcon, settings.ShowShipTypeIcon);

        int saveCalls = 0;
        draft.Commit(() => saveCalls++);

        Assert.Equal("NA", settings.Server);
        Assert.Equal(!originalIcon, settings.ShowShipTypeIcon);
        Assert.Equal(1, saveCalls);
    }

    [Fact]
    public void Cancel_DiscardsChangesWithoutPersisting()
    {
        ApeRadar.Properties.Settings settings = new();
        string originalServer = settings.Server;
        SettingsDraft draft = new(settings);
        draft.Set(nameof(settings.Server), "NA");

        draft.Cancel();
        draft.Commit(() => { });

        Assert.Equal(originalServer, settings.Server);
    }

    [Fact]
    public void FailedSave_RestoresAllChangedSettings()
    {
        ApeRadar.Properties.Settings settings = new();
        string originalServer = settings.Server;
        bool originalIcon = settings.ShowShipTypeIcon;
        SettingsDraft draft = new(settings);
        draft.Set(nameof(settings.Server), "NA");
        draft.Set(nameof(settings.ShowShipTypeIcon), !originalIcon);

        Assert.Throws<InvalidOperationException>(() => draft.Commit(() => throw new InvalidOperationException("save failed")));

        Assert.Equal(originalServer, settings.Server);
        Assert.Equal(originalIcon, settings.ShowShipTypeIcon);
    }

    [Fact]
    public void Defaults_UseExistingSettingsMetadataWithoutChangingLiveValues()
    {
        ApeRadar.Properties.Settings settings = new();
        settings.RosterDisplayDensity = "Comfortable";
        SettingsDraft draft = new(settings);

        Assert.Equal("Standard", draft.Default<string>(nameof(settings.RosterDisplayDensity)));
        Assert.True(draft.Default<bool>(nameof(settings.ShowShipTypeIcon)));
        Assert.Equal("Comfortable", settings.RosterDisplayDensity);
    }
}
