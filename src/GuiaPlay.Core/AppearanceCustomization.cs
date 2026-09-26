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
    ResolvedAppearanceMode Mode,
    string AccentHex,
    string AccentForegroundHex,
    string AccentSubtleHex,
    string AccentBorderHex,
    string WindowBackgroundHex,
    string SurfacePrimaryHex,
    string SurfaceSecondaryHex,
    string SurfaceElevatedHex,
    string SidebarBackgroundHex,
    string ControlBackgroundHex,
    string ControlHoverHex,
    string SelectionBackgroundHex,
    string SelectionForegroundHex,
    string DividerHex,
    string TextPrimaryHex,
    string TextSecondaryHex,
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
            return SystemPalette(preference);
        }

        return (preference, mode) switch
        {
            (AccentColorPreference.Cyan, ResolvedAppearanceMode.Light) => new(
                preference, mode, "#00666D", "#FFFFFFFF", "#FFD9F0F1", "#FF73B8BC",
                "#FFF1F9F9", "#FFFBFEFE", "#FFE7F4F5", "#FFF8FCFC", "#FFE3F2F3",
                "#FFF5FAFA", "#FFDEEFF0", "#FFCFEAEC", "#FF123F43", "#FFAFCCCE", "#FF142728", "#FF4E6668"),
            (AccentColorPreference.Purple, ResolvedAppearanceMode.Light) => new(
                preference, mode, "#6741B8", "#FFFFFFFF", "#FFE9E0F7", "#FFAA91D6",
                "#FFF7F4FC", "#FFFDFBFF", "#FFF0EAF9", "#FFFCFAFE", "#FFECE5F6",
                "#FFF9F6FC", "#FFECE4F6", "#FFE2D7F2", "#FF35235D", "#FFC7B8DF", "#FF211A2D", "#FF62566F"),
            (AccentColorPreference.Green, ResolvedAppearanceMode.Light) => new(
                preference, mode, "#087F5B", "#FFFFFFFF", "#FFDCEFE7", "#FF7EB7A3",
                "#FFF3F9F6", "#FFFBFEFC", "#FFE8F4EE", "#FFFAFDFB", "#FFE4F1EA",
                "#FFF6FBF8", "#FFE1F0E8", "#FFD4EBE0", "#FF174837", "#FFB7D4C6", "#FF15261F", "#FF52675E"),
            (AccentColorPreference.Orange, ResolvedAppearanceMode.Light) => new(
                preference, mode, "#A44900", "#FFFFFFFF", "#FFF5E4D6", "#FFD39A6E",
                "#FFFCF6F1", "#FFFFFCFA", "#FFF7ECE3", "#FFFFFBF8", "#FFF4E7DC",
                "#FFFCF8F4", "#FFF2E3D6", "#FFF2DDCC", "#FF5D2F0D", "#FFDEC2AC", "#FF2B1E15", "#FF735F50"),
            (AccentColorPreference.Pink, ResolvedAppearanceMode.Light) => new(
                preference, mode, "#A61E5C", "#FFFFFFFF", "#FFF5DCE8", "#FFD489AA",
                "#FFFCF4F8", "#FFFFFBFD", "#FFF7E8EF", "#FFFFFAFC", "#FFF4E3EB",
                "#FFFCF7FA", "#FFF2DFE8", "#FFF0D5E2", "#FF65163A", "#FFDFB5C9", "#FF2C1922", "#FF735766"),
            (AccentColorPreference.GuiaPlayBlue, ResolvedAppearanceMode.Light) => new(
                AccentColorPreference.GuiaPlayBlue, mode, "#005FB8", "#FFFFFFFF", "#FFDCEAF7", "#FF7BA9D3",
                "#FFF3F7FC", "#FFFCFDFF", "#FFEAF1F8", "#FFFAFCFE", "#FFE7F0FA",
                "#FFF6FAFE", "#FFE2EDF8", "#FFD6E8F9", "#FF123B61", "#FFB7CBE0", "#FF151F2A", "#FF526274"),

            (AccentColorPreference.Cyan, ResolvedAppearanceMode.Dark) => new(
                preference, mode, "#FF58D6E2", "#FF071719", "#FF173B3F", "#FF3A7479",
                "#FF091516", "#FF102224", "#FF152D30", "#FF19363A", "#FF0D1C1E",
                "#FF173033", "#FF1F4044", "#FF285158", "#FFF4F8F8", "#FF2D5357", "#FFF2F7F7", "#FFB7C9CA"),
            (AccentColorPreference.Purple, ResolvedAppearanceMode.Dark) => new(
                preference, mode, "#FFB99CFF", "#FF160E26", "#FF33264B", "#FF695493",
                "#FF130F1A", "#FF1D1728", "#FF261E35", "#FF2B2240", "#FF181322",
                "#FF2A2138", "#FF382B4D", "#FF473663", "#FFF8F5FC", "#FF493A5D", "#FFF7F4FB", "#FFC9C0D4"),
            (AccentColorPreference.Green, ResolvedAppearanceMode.Dark) => new(
                preference, mode, "#FF5DD39E", "#FF071B13", "#FF183C2E", "#FF39765C",
                "#FF0B1612", "#FF12231C", "#FF182E25", "#FF1C382C", "#FF0F1D18",
                "#FF193126", "#FF224233", "#FF2B5440", "#FFF3F9F6", "#FF315541", "#FFF2F7F4", "#FFBBCBC3"),
            (AccentColorPreference.Orange, ResolvedAppearanceMode.Dark) => new(
                preference, mode, "#FFFF9D57", "#FF241205", "#FF4A2D1A", "#FF915F3B",
                "#FF18110D", "#FF261A13", "#FF322219", "#FF3C291D", "#FF20160F",
                "#FF352419", "#FF493022", "#FF5D3D2A", "#FFFFF6EF", "#FF60422F", "#FFFCF5F0", "#FFD4C2B6"),
            (AccentColorPreference.Pink, ResolvedAppearanceMode.Dark) => new(
                preference, mode, "#FFF17CB0", "#FF260A17", "#FF4A2436", "#FF92506D",
                "#FF180E14", "#FF251720", "#FF311E2A", "#FF3B2431", "#FF1E1219",
                "#FF34202B", "#FF472B3B", "#FF5B364B", "#FFFFF5FA", "#FF60384C", "#FFFCF4F8", "#FFD2BEC8"),
            _ => new AccentPalette(
                AccentColorPreference.GuiaPlayBlue, mode, "#FF60A5FA", "#FF081624", "#FF24364E", "#FF4F78A8",
                "#FF0C121B", "#FF121C29", "#FF172536", "#FF1B2C40", "#FF0F1A28",
                "#FF19283A", "#FF223852", "#FF29486A", "#FFF5F9FD", "#FF2A405A", "#FFF4F7FB", "#FFB9C6D6")
        };
    }

    private static AccentPalette SystemPalette(AccentColorPreference preference) => new(
        preference,
        ResolvedAppearanceMode.HighContrast,
        "#FF000000",
        "#FFFFFFFF",
        "#FF000000",
        "#FFFFFFFF",
        "#FF000000",
        "#FF000000",
        "#FF000000",
        "#FF000000",
        "#FF000000",
        "#FF000000",
        "#FF000000",
        "#FF000000",
        "#FFFFFFFF",
        "#FFFFFFFF",
        "#FFFFFFFF",
        "#FF000000",
        UsesSystemColors: true);
}
