using System.Text.Json.Nodes;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class StartupUpdateTests
{
    [Fact]
    public async Task StartupQueriesDespiteRecentUpToDateCacheAndFindsNewVersion()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = AppSettings.Default with
        {
            LastUpdateCheckUtc = now,
            LastUpdateCheckProductVersion = "0.9.0-prototipo",
            LastKnownUpdateVersion = "0.9.0-prototipo",
            LastKnownUpdatePublishedAt = now,
            LastKnownUpdateStatus = UpdateCheckStatus.UpToDate
        };
        Assert.NotNull(PersistedUpdateCache.Restore(settings, ProductVersion.Parse("0.9.0-prototipo"), now));
        Assert.True(UpdateSchedule.ShouldCheckAtStartup(settings.CheckUpdatesAutomatically));
        var calls = 0;
        var coordinator = new StartupUpdateCheckCoordinator();

        var result = await coordinator.Start(true, () =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                new PublishedRelease(
                    ProductVersion.Parse("0.10.0-prototipo"),
                    "v0.10.0-prototipo",
                    now,
                    null,
                    true,
                    [])));
        })!;

        Assert.Equal(1, calls);
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.10.0-prototipo", result.Release!.Version.ToString());
        Assert.True(UpdateIndicatorState.IsVisible(result.Status));
    }

    [Fact]
    public void DisabledAutomaticUpdateDoesNotStartQuery()
    {
        var calls = 0;
        var task = new StartupUpdateCheckCoordinator().Start(false, () =>
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));
        });

        Assert.Null(task);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ConcurrentStartupCallsShareOneRealQuery()
    {
        var calls = 0;
        var response = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new StartupUpdateCheckCoordinator();
        Task<UpdateCheckResult> Query()
        {
            Interlocked.Increment(ref calls);
            return response.Task;
        }

        var first = coordinator.Start(true, Query)!;
        var second = coordinator.Start(true, Query)!;
        Assert.Same(first, second);
        response.SetResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));

        Assert.Equal(UpdateCheckStatus.UpToDate, (await first).Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CachedUpdateCanBeShownWhileStartupNetworkQueryIsPending()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = AppSettings.Default with
        {
            LastUpdateCheckUtc = now,
            LastUpdateCheckProductVersion = ProductInfo.Version,
            LastKnownUpdateVersion = "9.0.0-prototipo",
            LastKnownUpdatePublishedAt = now,
            LastKnownUpdateStatus = UpdateCheckStatus.UpdateAvailable
        };
        var cached = PersistedUpdateCache.Restore(settings, ProductVersion.Parse(ProductInfo.Version), now);
        var response = new TaskCompletionSource<UpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new StartupUpdateCheckCoordinator().Start(true, () => response.Task)!;

        Assert.True(UpdateIndicatorState.IsVisible(cached!.Status));
        Assert.False(pending.IsCompleted);
        response.SetResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await pending).Status);
    }

    [Fact]
    public void OfflineStartupKeepsPreviouslyKnownUpdateVisible()
    {
        var previous = new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            new PublishedRelease(
                ProductVersion.Parse("0.10.0-prototipo"),
                "v0.10.0-prototipo",
                DateTimeOffset.UtcNow,
                null,
                true,
                []));
        var offline = UpdateCheckResult.Failure("offline");

        var visible = UpdateResultRetention.SelectVisibleResult(previous, offline);

        Assert.Same(previous, visible);
        Assert.True(UpdateIndicatorState.IsVisible(visible.Status));
    }
}

public sealed class AppearancePaletteTests
{
    [Theory]
    [InlineData(AppearancePreference.Light, false, ResolvedAppearanceMode.Light)]
    [InlineData(AppearancePreference.Dark, false, ResolvedAppearanceMode.Dark)]
    [InlineData(AppearancePreference.System, false, ResolvedAppearanceMode.Light)]
    [InlineData(AppearancePreference.System, true, ResolvedAppearanceMode.Dark)]
    public void ResolvesBaseMode(
        AppearancePreference preference,
        bool windowsDark,
        ResolvedAppearanceMode expected) =>
        Assert.Equal(expected, AppearancePaletteResolver.ResolveMode(preference, windowsDark, highContrast: false));

    [Fact]
    public void HighContrastOverridesModeAndCustomAccent()
    {
        var mode = AppearancePaletteResolver.ResolveMode(AppearancePreference.Dark, true, highContrast: true);
        var palette = AppearancePaletteResolver.Resolve(AccentColorPreference.Pink, mode);

        Assert.Equal(ResolvedAppearanceMode.HighContrast, mode);
        Assert.True(palette.UsesSystemColors);
    }

    [Fact]
    public void EveryAccentHasDistinctLightAndDarkVariants()
    {
        foreach (var accent in Enum.GetValues<AccentColorPreference>())
        {
            var light = AppearancePaletteResolver.Resolve(accent, ResolvedAppearanceMode.Light);
            var dark = AppearancePaletteResolver.Resolve(accent, ResolvedAppearanceMode.Dark);
            Assert.NotEqual(light.AccentHex, dark.AccentHex);
            Assert.StartsWith("#", light.AccentHex);
            Assert.StartsWith("#", dark.AccentHex);
        }
    }
}

public sealed class EqualizerConfigurationTests
{
    [Fact]
    public void DisabledIsDefaultAndNormalizationUsesRequestedBandCount()
    {
        var normalized = EqualizerConfigurationBehavior.Normalize(EqualizerConfiguration.Default, 10);

        Assert.False(normalized.Enabled);
        Assert.Equal("Flat", normalized.PresetName);
        Assert.Equal(10, normalized.BandGains.Count);
        Assert.All(normalized.BandGains, gain => Assert.Equal(0f, gain));
    }

    [Fact]
    public void ClampRejectsNonFiniteAndOutOfRangeValues()
    {
        var normalized = EqualizerConfigurationBehavior.Normalize(
            new EqualizerConfiguration(true, "Custom", float.PositiveInfinity, [float.NaN, -50f, 50f]),
            3);

        Assert.Equal(0f, normalized.Preamp);
        Assert.Equal([0f, -20f, 20f], normalized.BandGains);
    }

    [Fact]
    public void PresetLoadsRealValuesAndBandChangeBecomesCustom()
    {
        var preset = new EqualizerPresetDefinition(3, "Dance", 5f, [9.6f, 7.2f]);
        var selected = EqualizerConfigurationBehavior.FromPreset(preset, enabled: true);
        var customized = EqualizerConfigurationBehavior.WithBandGain(selected, 1, 3f, 2);

        Assert.True(selected.Enabled);
        Assert.Equal("Dance", selected.PresetName);
        Assert.Equal(EqualizerConfiguration.CustomPresetName, customized.PresetName);
        Assert.Equal(3f, customized.BandGains[1]);
    }

    [Fact]
    public void PreampChangeBecomesCustomAndClamps()
    {
        var changed = EqualizerConfigurationBehavior.WithPreamp(
            new EqualizerConfiguration(true, "Rock", 5f, [1f]),
            99f,
            1);

        Assert.Equal(EqualizerConfiguration.CustomPresetName, changed.PresetName);
        Assert.Equal(20f, changed.Preamp);
    }
}

public sealed class M10SettingsMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.M10.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void SchemaFiveMigratesToBlueAccentAndDisabledEqualizer()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"schemaVersion":5,"appearance":"Dark","audio":{"volume":80}}""");
        var store = new AppSettingsStore(SettingsPath);

        var settings = store.Load().Settings;
        Assert.Equal(AccentColorPreference.GuiaPlayBlue, settings.AccentColor);
        Assert.False(settings.Equalizer.Enabled);
        Assert.True(store.Save(settings).Succeeded);
        var json = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal(6, json["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void AccentAndCustomEqualizerSurviveRestart()
    {
        var settings = AppSettings.Default with
        {
            Appearance = AppearancePreference.Dark,
            AccentColor = AccentColorPreference.Orange,
            Equalizer = new EqualizerConfiguration(true, EqualizerConfiguration.CustomPresetName, 2.5f, [1f, -2f, 3f])
        };
        var store = new AppSettingsStore(SettingsPath);
        _ = store.Load();
        Assert.True(store.Save(settings).Succeeded);

        var reloaded = new AppSettingsStore(SettingsPath).Load().Settings;
        Assert.Equal(AccentColorPreference.Orange, reloaded.AccentColor);
        Assert.True(reloaded.Equalizer.Enabled);
        Assert.Equal(2.5f, reloaded.Equalizer.Preamp);
        Assert.Equal([1f, -2f, 3f], reloaded.Equalizer.BandGains);
    }

    [Fact]
    public void InvalidAccentFallsBackToGuiaPlayBlue()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"accentColor":"Infrared"}""");

        Assert.Equal(AccentColorPreference.GuiaPlayBlue, new AppSettingsStore(SettingsPath).Load().Settings.AccentColor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
