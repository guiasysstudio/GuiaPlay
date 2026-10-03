using System.Xml.Linq;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class BrandingTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("src/GuiaPlay.App/Assets/Branding/Icons/GuiaPlay.ico")]
    [InlineData("src/GuiaPlay.App/Assets/Branding/GuiaPlay-Wordmark-UI.png")]
    [InlineData("src/GuiaPlay.App/Assets/Branding/Wordmarks/GuiaPlay-Wordmark-1024.png")]
    [InlineData("installer/Assets/GuiaPlay-Setup.ico")]
    [InlineData("installer/Assets/WizardImageFile.bmp")]
    [InlineData("installer/Assets/WizardSmallImageFile.bmp")]
    [InlineData("installer/Assets/GuiaPlay-Banner-700x200.png")]
    public void OfficialBrandingAssetExistsAndIsNotEmpty(string relativePath)
    {
        var file = new FileInfo(Path.Combine(RepositoryRoot, Normalize(relativePath)));

        Assert.True(file.Exists, $"Asset obrigatório ausente: {relativePath}");
        Assert.True(file.Length > 0, $"Asset obrigatório vazio: {relativePath}");
    }

    [Fact]
    public void ApplicationProjectUsesOfficialExecutableIcon()
    {
        var project = XDocument.Load(PathInRepository("src/GuiaPlay.App/GuiaPlay.App.csproj"));

        Assert.Equal(
            @"Assets\Branding\Icons\GuiaPlay.ico",
            project.Descendants("ApplicationIcon").Single().Value);
    }

    [Fact]
    public void ApplicationProjectEmbedsOnlyRuntimeBrandingResources()
    {
        var project = XDocument.Load(PathInRepository("src/GuiaPlay.App/GuiaPlay.App.csproj"));
        var resources = project.Descendants("Resource")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(@"Assets\Branding\Icons\GuiaPlay.ico", resources);
        Assert.Contains(@"Assets\Branding\GuiaPlay-Wordmark-UI.png", resources);
        Assert.DoesNotContain(@"Assets\Branding\Wordmarks\GuiaPlay-Wordmark-1024.png", resources);
    }

    [Fact]
    public void PrimaryWindowsUseOfficialIconAndWordmark()
    {
        var mainWindow = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));
        var settingsWindow = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));

        Assert.Contains("Assets/Branding/Icons/GuiaPlay.ico", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Assets/Branding/GuiaPlay-Wordmark-UI.png", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Assets/Branding/Icons/GuiaPlay.ico", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("Assets/Branding/GuiaPlay-Wordmark-UI.png", settingsWindow, StringComparison.Ordinal);
        Assert.Contains("Header=\"Sobre\"", settingsWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerReferencesOfficialBrandingAndExecutableShortcutIcons()
    {
        var installer = File.ReadAllText(PathInRepository("installer/GuiaPlay.iss"));

        Assert.Contains("SetupIconFile=Assets\\GuiaPlay-Setup.ico", installer, StringComparison.Ordinal);
        Assert.Contains("WizardImageFile=Assets\\WizardImageFile.bmp", installer, StringComparison.Ordinal);
        Assert.Contains("WizardSmallImageFile=Assets\\WizardSmallImageFile.bmp", installer, StringComparison.Ordinal);
        Assert.Contains("UninstallDisplayIcon={app}\\{#MyAppExeName}", installer, StringComparison.Ordinal);
        Assert.Equal(2, installer.Split("IconFilename: \"{app}\\{#MyAppExeName}\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("Tasks: desktopicon", installer, StringComparison.Ordinal);
        Assert.Contains("Flags: unchecked", installer, StringComparison.Ordinal);
        Assert.Contains("Flags: nowait postinstall skipifsilent unchecked", installer, StringComparison.Ordinal);
        Assert.Contains("Name: \"windowsintegration\"", installer, StringComparison.Ordinal);
        Assert.Contains("Name: \"contextmenu\"", installer, StringComparison.Ordinal);
        Assert.Contains(
            "Flags: unchecked",
            installer.Split('\n').Single(line => line.Contains("Name: \"windowsintegration\"", StringComparison.Ordinal)),
            StringComparison.Ordinal);
        Assert.Contains(
            "Flags: unchecked",
            installer.Split('\n').Single(line => line.Contains("Name: \"contextmenu\"", StringComparison.Ordinal)),
            StringComparison.Ordinal);
        Assert.Contains("Software\\RegisteredApplications", installer, StringComparison.Ordinal);
        Assert.Contains("GuiaPlay.Video", installer, StringComparison.Ordinal);
        Assert.Contains("GuiaPlay.Audio", installer, StringComparison.Ordinal);
        Assert.Contains("--remove-windows-integration", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("UserChoice", installer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseBuildValidatesEveryRequiredBrandingAsset()
    {
        var script = File.ReadAllText(PathInRepository("scripts/build-release.ps1"));
        var expectedNames = new[]
        {
            "GuiaPlay.ico",
            "GuiaPlay-Wordmark-UI.png",
            "GuiaPlay-Wordmark-1024.png",
            "GuiaPlay-Setup.ico",
            "WizardImageFile.bmp",
            "WizardSmallImageFile.bmp",
            "GuiaPlay-Banner-700x200.png"
        };

        Assert.All(expectedNames, name => Assert.Contains(name, script, StringComparison.Ordinal));
        Assert.Contains("Asset obrigatório de branding ausente ou vazio", script, StringComparison.Ordinal);
        Assert.Contains("Get-PeMachine", script, StringComparison.Ordinal);
        Assert.Contains("Arquitetura PE incorreta", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePublishRedownloadsEveryAssetAndComparesItsSha256()
    {
        var script = File.ReadAllText(PathInRepository("scripts/publish-release.ps1"));

        Assert.Contains("gh release download", script, StringComparison.Ordinal);
        Assert.Contains("$localHash", script, StringComparison.Ordinal);
        Assert.Contains("$downloadedHash", script, StringComparison.Ordinal);
        Assert.Contains("diverge byte a byte", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateProcessLaunchesAreValidatedAndRecoverable()
    {
        var manager = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Services/UpdateManager.cs"));
        var updater = File.ReadAllText(PathInRepository("src/GuiaPlay.Updater/Program.cs"));

        Assert.Contains("SafeProcessLauncher.TryStart(start)", manager, StringComparison.Ordinal);
        Assert.Contains("string.Equals(launch, \"GuiaPlay.exe\"", updater, StringComparison.Ordinal);
        Assert.Contains("File.Exists(launchPath)", updater, StringComparison.Ordinal);
        Assert.Contains("Logging must never hide", updater, StringComparison.Ordinal);
    }

    [Fact]
    public void M09DiagnosticsAndSoakScriptAvoidAggressiveSamplingAndFakeGpuMetrics()
    {
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));
        var soak = File.ReadAllText(PathInRepository("scripts/soak-test.ps1"));

        Assert.Contains("Header=\"Diagnóstico\"", settings, StringComparison.Ordinal);
        Assert.Contains("Copiar diagnóstico", settings, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("[int]$SampleSeconds = 5", soak, StringComparison.Ordinal);
        Assert.Contains("WorkingSetMB", soak, StringComparison.Ordinal);
        Assert.Contains("PrivateMemoryMB", soak, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuPercent", soak, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M10UsesProfessionalCardsAndKeepsPrimaryOperationsVisible()
    {
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));

        Assert.Contains("CardStyle", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdateAvailableButton\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlayButton\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PauseButton\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StopButton\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VolumeSlider\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlaylistTree\"", main, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"840\"", main, StringComparison.Ordinal);
    }

    [Fact]
    public void M10SettingsHaveSidebarSeparatedPagesAppearanceAndNativeEqualizer()
    {
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));

        Assert.Contains("x:Name=\"SettingsSidebar\"", settings, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsContentSurface\"", settings, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AppearanceTab\" Header=\"Aparência\"", settings, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AudioTab\" Header=\"Áudio\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Aparência e áudio", settings, StringComparison.Ordinal);
        Assert.Contains("Ativar equalizador", settings, StringComparison.Ordinal);
        Assert.Contains("EqualizerBandsItems", settings, StringComparison.Ordinal);
        Assert.Contains("GuiaPlay Azul", settings, StringComparison.Ordinal);
        Assert.Contains("Ciano", settings, StringComparison.Ordinal);
        Assert.Contains("Roxo", settings, StringComparison.Ordinal);
        Assert.Contains("Verde", settings, StringComparison.Ordinal);
        Assert.Contains("Laranja", settings, StringComparison.Ordinal);
        Assert.Contains("Rosa", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M10StartupUpdateAndEqualizerAreConnectedToProductRuntime()
    {
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));
        var updateManager = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Services/UpdateManager.cs"));
        var engine = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Playback/LibVlcPlaybackEngine.cs"));
        var adapter = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Playback/LibVlcEqualizerAdapter.cs"));

        Assert.Contains("manager.CheckOnStartupAsync()", main, StringComparison.Ordinal);
        Assert.Contains("UpdateManager_OnStateChanged", main, StringComparison.Ordinal);
        Assert.Contains("Update startup: scheduled", main, StringComparison.Ordinal);
        Assert.Contains("Update startup: querying GitHub", updateManager, StringComparison.Ordinal);
        Assert.Contains("Update startup: query completed", updateManager, StringComparison.Ordinal);
        Assert.Contains("ApplyEqualizerWhileLocked(session.Player)", engine, StringComparison.Ordinal);
        Assert.Contains("SetEqualizer", adapter, StringComparison.Ordinal);
        Assert.Contains("UnsetEqualizer", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void M101AppliesSemanticPaletteResourcesAcrossMainAndSettingsSurfaces()
    {
        var app = File.ReadAllText(PathInRepository("src/GuiaPlay.App/App.xaml"));
        var appCode = File.ReadAllText(PathInRepository("src/GuiaPlay.App/App.xaml.cs"));
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));
        var resourceNames = new[]
        {
            "GuiaPlayWindowBackgroundBrush",
            "GuiaPlaySurfacePrimaryBrush",
            "GuiaPlaySurfaceSecondaryBrush",
            "GuiaPlaySurfaceElevatedBrush",
            "GuiaPlaySidebarBrush",
            "GuiaPlayControlSurfaceBrush",
            "GuiaPlayControlHoverBrush",
            "GuiaPlaySelectionBrush",
            "GuiaPlayDividerBrush"
        };

        Assert.All(resourceNames, resource =>
        {
            Assert.Contains($"x:Key=\"{resource}\"", app, StringComparison.Ordinal);
            Assert.Contains($"Resources[\"{resource}\"]", appCode, StringComparison.Ordinal);
        });
        Assert.Contains("Background=\"{DynamicResource GuiaPlayWindowBackgroundBrush}\"", main, StringComparison.Ordinal);
        Assert.Contains("GuiaPlaySurfacePrimaryBrush", main, StringComparison.Ordinal);
        Assert.Contains("GuiaPlaySurfaceSecondaryBrush", main, StringComparison.Ordinal);
        Assert.Contains("GuiaPlaySelectionBrush", main, StringComparison.Ordinal);
        Assert.Contains("Background=\"Black\"", main, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource GuiaPlayWindowBackgroundBrush}\"", settings, StringComparison.Ordinal);
        Assert.Contains("GuiaPlaySidebarBrush", settings, StringComparison.Ordinal);
        Assert.Contains("GuiaPlaySelectionBrush", settings, StringComparison.Ordinal);
        Assert.Contains("AppearancePreviewSidebar", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("CardBackgroundFillColorDefaultBrush", main, StringComparison.Ordinal);
        Assert.DoesNotContain("CardBackgroundFillColorDefaultBrush", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void M101HighContrastMapsEveryCustomSurfaceBackToSystemColors()
    {
        var appCode = File.ReadAllText(PathInRepository("src/GuiaPlay.App/App.xaml.cs"));

        Assert.Contains("SystemColors.WindowBrush", appCode, StringComparison.Ordinal);
        Assert.Contains("SystemColors.ControlBrush", appCode, StringComparison.Ordinal);
        Assert.Contains("SystemColors.HighlightBrush", appCode, StringComparison.Ordinal);
        Assert.Contains("SystemColors.HighlightTextBrush", appCode, StringComparison.Ordinal);
        Assert.Contains("SystemColors.WindowTextBrush", appCode, StringComparison.Ordinal);
        Assert.Contains("SystemColors.GrayTextBrush", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void M102SettingsUseARealClippedSidebarAndHiddenTabHeaders()
    {
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));
        var settingsCode = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml.cs"));

        Assert.Contains("x:Name=\"SettingsWorkspace\"", settings, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"192\" />", settings, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"12\" />", settings, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsSidebar\" Grid.Column=\"0\"", settings, StringComparison.Ordinal);
        Assert.Contains("Padding=\"8\" ClipToBounds=\"True\"", settings, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsContentSurface\" Grid.Column=\"2\"", settings, StringComparison.Ordinal);
        Assert.Contains("<TabControl.Template>", settings, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding SelectedContent}\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("TabStripPlacement=", settings, StringComparison.Ordinal);
        Assert.Contains("SelectionIndicator", settings, StringComparison.Ordinal);
        Assert.Contains("IsKeyboardFocused", settings, StringComparison.Ordinal);
        Assert.Equal(7, settings.Split("GroupName=\"SettingsNavigation\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("SynchronizeNavigationSelection", settingsCode, StringComparison.Ordinal);
        Assert.Contains("ScreensNavigationButton.IsEnabled = _screensEditable", settingsCode, StringComparison.Ordinal);
        Assert.Contains("SettingsAccessPolicy.CanConfigureScreens", settingsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void M103CompactsOperationsAndGivesPlaylistMoreUsefulSpace()
    {
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));

        Assert.Contains("x:Name=\"OperationalControlPanel\"", main, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource GuiaPlaySurfaceSecondaryBrush}\" Padding=\"8,6\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CompactTransportButtonStyle\"", main, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"30\" />", main, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"54*\" MinWidth=\"390\" />", main, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"46*\" MinWidth=\"310\" />", main, StringComparison.Ordinal);
        Assert.Contains("SavePlaylistPresetButton_OnClick", main, StringComparison.Ordinal);
        Assert.Contains("LoadPlaylistPresetButton_OnClick", main, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"OutputChipStyle\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlaylistEmptyState\"", main, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Items.Count}\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviewEmptyState\"", main, StringComparison.Ordinal);
        Assert.Contains("Background=\"Black\"", main, StringComparison.Ordinal);
        Assert.Contains("GuiaPlaySurfacePrimaryBrush", main, StringComparison.Ordinal);
        Assert.Contains("GuiaPlaySelectionBrush", main, StringComparison.Ordinal);
    }

    [Fact]
    public void M103BrandingIsDerivedFromRequiredOfficialSvgs()
    {
        var logo = new FileInfo(PathInRepository("src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Logo.svg"));
        var wordmark = new FileInfo(PathInRepository("src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Wordmark.svg"));
        var generator = File.ReadAllText(PathInRepository("scripts/generate-branding-assets.ps1"));
        var manifest = File.ReadAllText(PathInRepository("docs/branding/brand-manifest.json"));

        Assert.True(logo.Exists && logo.Length > 0);
        Assert.True(wordmark.Exists && wordmark.Length > 0);
        Assert.Contains("GuiaPlay-Logo.svg", generator, StringComparison.Ordinal);
        Assert.Contains("GuiaPlay-Wordmark.svg", generator, StringComparison.Ordinal);
        Assert.Contains("GuiaPlay-Logo.svg", manifest, StringComparison.Ordinal);
        Assert.Contains("GuiaPlay-Wordmark.svg", manifest, StringComparison.Ordinal);
        Assert.Contains("sha256", manifest, StringComparison.OrdinalIgnoreCase);
    }

    private static string PathInRepository(string relativePath) =>
        Path.Combine(RepositoryRoot, Normalize(relativePath));

    private static string Normalize(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "installer")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório GuiaPlay.");
    }
}
