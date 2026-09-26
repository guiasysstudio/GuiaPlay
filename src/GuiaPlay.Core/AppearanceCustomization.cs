namespace GuiaPlay.Core;

public enum AccentColorPreference
{
    GuiaPlayBlue,
    Cyan,
    Purple,
    Green,
    Orange,
    Pink
}

public enum ResolvedAppearanceMode
{
    Light,
    Dark,
    HighContrast
}

public sealed record AccentPalette(
    AccentColorPreference Preference,
    string AccentHex,
    string ForegroundHex,
    string SubtleHex,
    bool UsesSystemColors = false);

public static class AppearancePaletteResolver
{
    public static ResolvedAppearanceMode ResolveMode(
        AppearancePreference preference,
        bool windowsUsesDarkMode,
        bool highContrast) =>
        highContrast
            ? ResolvedAppearanceMode.HighContrast
            : preference switch
            {
                AppearancePreference.Light => ResolvedAppearanceMode.Light,
                AppearancePreference.Dark => ResolvedAppearanceMode.Dark,
                _ => windowsUsesDarkMode ? ResolvedAppearanceMode.Dark : ResolvedAppearanceMode.Light
            };

    public static AccentPalette Resolve(
        AccentColorPreference preference,
        ResolvedAppearanceMode mode)
    {
        if (mode == ResolvedAppearanceMode.HighContrast)
        {
            return new AccentPalette(preference, "#000000", "#FFFFFF", "#000000", UsesSystemColors: true);
        }

        var dark = mode == ResolvedAppearanceMode.Dark;
        return preference switch
        {
            AccentColorPreference.Cyan => Palette(preference, dark, "#00666D", "#58D6E2", "#1900666D", "#2858D6E2"),
            AccentColorPreference.Purple => Palette(preference, dark, "#6741B8", "#B99CFF", "#196741B8", "#28B99CFF"),
            AccentColorPreference.Green => Palette(preference, dark, "#087F5B", "#5DD39E", "#19087F5B", "#285DD39E"),
            AccentColorPreference.Orange => Palette(preference, dark, "#A44900", "#FF9D57", "#19A44900", "#28FF9D57"),
            AccentColorPreference.Pink => Palette(preference, dark, "#A61E5C", "#F17CB0", "#19A61E5C", "#28F17CB0"),
            _ => Palette(AccentColorPreference.GuiaPlayBlue, dark, "#005FB8", "#60A5FA", "#19005FB8", "#2860A5FA")
        };
    }

    private static AccentPalette Palette(
        AccentColorPreference preference,
        bool dark,
        string lightAccent,
        string darkAccent,
        string lightSubtle,
        string darkSubtle) =>
        new(
            preference,
            dark ? darkAccent : lightAccent,
            dark ? "#FF101010" : "#FFFFFFFF",
            dark ? darkSubtle : lightSubtle);
}
