using System.Text.Json.Nodes;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class AppearanceSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.Tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void MissingOrInvalidSettingFallsBackToSystem()
    {
        var store = new AppearanceSettings(SettingsPath);
        Assert.Equal(AppearancePreference.System, store.Load());

        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ inválido");
        Assert.Equal(AppearancePreference.System, store.Load());

        File.WriteAllText(SettingsPath, "{\"appearance\":\"Solarized\"}");
        Assert.Equal(AppearancePreference.System, store.Load());

        File.WriteAllText(SettingsPath, "{\"appearance\":42}");
        Assert.Equal(AppearancePreference.System, store.Load());
    }

    [Theory]
    [InlineData(AppearancePreference.System)]
    [InlineData(AppearancePreference.Light)]
    [InlineData(AppearancePreference.Dark)]
    public void PreferenceSurvivesSaveAndReload(AppearancePreference preference)
    {
        var store = new AppearanceSettings(SettingsPath);

        store.Save(preference);

        Assert.Equal(preference, new AppearanceSettings(SettingsPath).Load());
    }

    [Fact]
    public void SavePreservesUnknownFutureSettings()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"futureSetting\":42}");
        var store = new AppearanceSettings(SettingsPath);

        store.Save(AppearancePreference.Dark);

        var saved = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal(42, saved["futureSetting"]!.GetValue<int>());
        Assert.Equal("Dark", saved["appearance"]!.GetValue<string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
