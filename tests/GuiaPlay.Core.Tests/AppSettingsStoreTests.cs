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
        Assert.Equal(3, json["schemaVersion"]!.GetValue<int>());
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
            true);
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
