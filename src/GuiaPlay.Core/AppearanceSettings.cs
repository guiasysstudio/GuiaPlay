namespace GuiaPlay.Core;

public enum AppearancePreference
{
    System,
    Light,
    Dark
}

public sealed class AppearanceSettings(string filePath)
{
    private readonly AppSettingsStore _store = new(filePath);

    public AppearancePreference Load() => _store.Load().Settings.Appearance;

    public void Save(AppearancePreference preference)
    {
        var current = _store.Load().Settings;
        var result = _store.Save(current with { Appearance = preference });
        if (!result.Succeeded)
        {
            throw new IOException(result.Error);
        }
    }
}
