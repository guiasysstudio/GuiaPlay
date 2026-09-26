using System.Text.Json.Nodes;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.M02.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void MigratesM02SchemaWithoutLosingPreferences()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"schemaVersion":2,"appearance":"Dark","screens":{"operatorId":"operator","selectedOutputIds":["public"]},"audio":{"volume":42,"muted":true}}""");
        var store = new AppSettingsStore(SettingsPath);

        var loaded = store.Load().Settings;
        Assert.True(store.Save(loaded).Succeeded);
        var json = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();

        Assert.Equal(AppearancePreference.Dark, loaded.Appearance);
        Assert.Equal("operator", loaded.OperatorMonitorId);
        Assert.Contains("public", loaded.SelectedOutputIds);
        Assert.Equal(42, loaded.Volume);
        Assert.True(loaded.Muted);
        Assert.Equal(5, json["schemaVersion"]!.GetValue<int>());
        Assert.True(loaded.CheckUpdatesAutomatically);
        Assert.False(loaded.InstallUpdatesAutomatically);
        Assert.Null(loaded.LastUpdateCheckUtc);
    }

    [Fact]
    public void MigratesAppearanceOnlyFileAndPreservesUnknownProperties()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"appearance":"Dark","future":{"enabled":true}}""");
        var store = new AppSettingsStore(SettingsPath);

        var loaded = store.Load();
        var saved = store.Save(loaded.Settings with { Volume = 37 });

        Assert.Equal(AppearancePreference.Dark, loaded.Settings.Appearance);
        Assert.Equal(AudioOutputMode.WindowsDefault, loaded.Settings.AudioOutput.Mode);
        Assert.True(saved.Succeeded);
        var json = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.True(json["future"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(AppSettings.CurrentSchemaVersion, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal(37, json["audio"]!["volume"]!.GetValue<int>());
    }

    [Fact]
    public void RoundTripPreservesAllM02PreferencesAndNestedUnknownProperties()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"screens":{"futureScreenKey":7},"audio":{"futureAudioKey":"ok"}}""");
        var settings = new AppSettings(
            AppearancePreference.Light,
            "monitor-path-a",
            new Dictionary<string, string> { ["monitor-path-a"] = "Operador", ["monitor-path-b"] = "Público" },
            new HashSet<string> { "monitor-path-b" },
            new AudioOutputPreference(AudioOutputMode.Explicit, "mmdevice", "endpoint-1", "Mesa USB"),
            64,
            true,
            false,
            true,
            new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
            "0.6.0-prototipo",
            "0.7.0-prototipo",
            new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero),
            UpdateCheckStatus.UpdateAvailable);
        var store = new AppSettingsStore(SettingsPath);
        _ = store.Load();

        Assert.True(store.Save(settings).Succeeded);
        var reloaded = new AppSettingsStore(SettingsPath).Load().Settings;
        var json = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();

        Assert.Equal(settings.Appearance, reloaded.Appearance);
        Assert.Equal(settings.OperatorMonitorId, reloaded.OperatorMonitorId);
        Assert.Equal("Público", reloaded.MonitorNames["monitor-path-b"]);
        Assert.Contains("monitor-path-b", reloaded.SelectedOutputIds);
        Assert.Equal(settings.AudioOutput, reloaded.AudioOutput);
        Assert.Equal(64, reloaded.Volume);
        Assert.True(reloaded.Muted);
        Assert.False(reloaded.CheckUpdatesAutomatically);
        Assert.True(reloaded.InstallUpdatesAutomatically);
        Assert.Equal(settings.LastUpdateCheckUtc, reloaded.LastUpdateCheckUtc);
        Assert.Equal(settings.LastUpdateCheckProductVersion, reloaded.LastUpdateCheckProductVersion);
        Assert.Equal(settings.LastKnownUpdateVersion, reloaded.LastKnownUpdateVersion);
        Assert.Equal(settings.LastKnownUpdatePublishedAt, reloaded.LastKnownUpdatePublishedAt);
        Assert.Equal(settings.LastKnownUpdateStatus, reloaded.LastKnownUpdateStatus);
        Assert.Equal(7, json["screens"]!["futureScreenKey"]!.GetValue<int>());
        Assert.Equal("ok", json["audio"]!["futureAudioKey"]!.GetValue<string>());
    }

    [Fact]
    public void InvalidJsonIsBackedUpWithBoundedRetention()
    {
        Directory.CreateDirectory(_directory);
        for (var index = 0; index < 5; index++)
        {
            File.WriteAllText(SettingsPath, "{ invalid " + index);
            var result = new AppSettingsStore(SettingsPath, invalidBackupRetention: 2).Load();
            Assert.Equal(AppSettings.Default, result.Settings);
            Assert.True(result.RecoveredFromInvalidFile);
        }

        Assert.Equal(2, Directory.GetFiles(_directory, "settings.invalid-*.json").Length);
    }

    [Fact]
    public void InvalidValuesFallBackAndVolumeIsClamped()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{"appearance":"Unknown","audio":{"mode":"Explicit","volume":999,"muted":"yes"}}""");

        var settings = new AppSettingsStore(SettingsPath).Load().Settings;

        Assert.Equal(AppearancePreference.System, settings.Appearance);
        Assert.Equal(AudioOutputMode.WindowsDefault, settings.AudioOutput.Mode);
        Assert.Equal(100, settings.Volume);
        Assert.False(settings.Muted);
    }

    [Fact]
    public void WriteFailureIsReportedAndDoesNotEscape()
    {
        Directory.CreateDirectory(_directory);
        var fileWhereDirectoryIsExpected = Path.Combine(_directory, "occupied");
        File.WriteAllText(fileWhereDirectoryIsExpected, "x");
        var impossiblePath = Path.Combine(fileWhereDirectoryIsExpected, "settings.json");

        var result = new AppSettingsStore(impossiblePath).Save(AppSettings.Default);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void PersistedUpdateResultSurvivesSimulatedRestart()
    {
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var currentVersion = ProductVersion.Parse("0.6.0-prototipo");
        var availableVersion = ProductVersion.Parse("0.7.0-prototipo");
        var result = new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            new PublishedRelease(availableVersion, "v0.7.0-prototipo", now, null, true, []));
        var store = new AppSettingsStore(SettingsPath);
        _ = store.Load();
        Assert.True(store.Save(PersistedUpdateCache.Record(AppSettings.Default, currentVersion, result, now)).Succeeded);

        var reloaded = new AppSettingsStore(SettingsPath).Load().Settings;
        var restored = PersistedUpdateCache.Restore(reloaded, currentVersion, now.AddMinutes(1));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, restored?.Status);
        Assert.Equal(availableVersion, restored?.Release?.Version);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
