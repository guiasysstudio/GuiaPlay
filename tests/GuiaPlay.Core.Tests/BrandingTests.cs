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
    public void M09DiagnosticsInfrastructureAndSoakScriptAvoidAggressiveSamplingAndFakeGpuMetrics()
    {
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));
        var soak = File.ReadAllText(PathInRepository("scripts/soak-test.ps1"));

        Assert.DoesNotContain("Header=\"Diagnóstico\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Copiar diagnóstico", settings, StringComparison.Ordinal);
        Assert.Contains("SessionDiagnostics", main, StringComparison.Ordinal);
        Assert.Contains("ProcessMetricsSampler", main, StringComparison.Ordinal);
        Assert.Contains("LogDiagnosticSnapshot", main, StringComparison.Ordinal);
        Assert.Contains("[int]$SampleSeconds = 5", soak, StringComparison.Ordinal);
        Assert.Contains("WorkingSetMB", soak, StringComparison.Ordinal);
        Assert.Contains("PrivateMemoryMB", soak, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuPercent", soak, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void LicensingTrialIsExplicitAndDoesNotStartOnApplicationInitialization()
    {
        var manager = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/LicensingManager.cs"));
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));

        Assert.Contains("public async Task<LicensingSnapshot> StartTrialAsync", manager, StringComparison.Ordinal);
        Assert.Contains("StartTrialButton", settings, StringComparison.Ordinal);
        Assert.Contains("Iniciar período grátis", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("allowTrialStart: true", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("_apiClient.StartTrialAsync(", ExtractInitializeMethod(manager), StringComparison.Ordinal);
    }

    private static string ExtractInitializeMethod(string source)
    {
        const string startMarker = "public async Task<LicensingSnapshot> InitializeAsync";
        const string endMarker = "public async Task<LicensingSnapshot> RefreshAsync";
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        return start >= 0 && end > start ? source[start..end] : source;
    }

    [Fact]
    public void LicensingStartupUsesValidProtectedCacheBeforeShowingActivationState()
    {
        var manager = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/LicensingManager.cs"));
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));

        Assert.Contains("Current = BuildCachedStartupSnapshot();", manager, StringComparison.Ordinal);
        Assert.Contains("private LicensingSnapshot BuildCachedStartupSnapshot()", manager, StringComparison.Ordinal);
        Assert.Contains("var verification = VerifyLocal(configuration, accessKind, now);", manager, StringComparison.Ordinal);
        Assert.Contains("if (!verification.IsValid)", manager, StringComparison.Ordinal);
        Assert.Contains("LicenseBlockedOverlay.Visibility = snapshot.HasAccess ? Visibility.Collapsed : Visibility.Visible;", main, StringComparison.Ordinal);
    }

    [Fact]
    public void LicensingAccountButtonTargetsPurchasesAndTrialOfferIsOneTimePerLocalState()
    {
        var contracts = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/LicensingContracts.cs"));
        var manager = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/LicensingManager.cs"));
        var persistence = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/LicensingPersistence.cs"));
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml.cs"));
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));

        Assert.Contains("https://licencas.guiasys.com.br/conta/compras", contracts, StringComparison.Ordinal);
        Assert.Contains("LicensingProtocol.AccountUri", settings, StringComparison.Ordinal);
        Assert.Contains("bool TrialConsumed = false", persistence, StringComparison.Ordinal);
        Assert.Contains("!_state.TrialConsumed", manager, StringComparison.Ordinal);
        Assert.Contains("TrialConsumed = true", manager, StringComparison.Ordinal);
        Assert.Contains("TrialConsumedSnapshot", manager, StringComparison.Ordinal);
        Assert.Contains("Período grátis já utilizado", settings, StringComparison.Ordinal);
        Assert.Contains("Período grátis já utilizado", main, StringComparison.Ordinal);
        Assert.Contains("\"trial_consumed\"", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutPageTargetsOfficialGuiaSysStudioWebsite()
    {
        var product = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/ProductInfo.cs"));
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml"));
        var settingsCode = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml.cs"));

        Assert.Contains("https://www.guiasys.com.br/", product, StringComparison.Ordinal);
        Assert.Contains("Abrir página do GuiaSys Studio", settings, StringComparison.Ordinal);
        Assert.Contains("ProductInfo.StudioPageUri.AbsoluteUri", settingsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductInfo.ProjectPageUri.AbsoluteUri", settingsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void LicensingAndStudioUrlsUseGuiasysComBr()
    {
        var contracts = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/LicensingContracts.cs"));
        var product = File.ReadAllText(PathInRepository("src/GuiaPlay.Core/ProductInfo.cs"));

        Assert.Contains("https://licencas.guiasys.com.br", contracts, StringComparison.Ordinal);
        Assert.Contains("https://licencas.guiasys.com.br/conta/compras", contracts, StringComparison.Ordinal);
        Assert.Contains("https://www.guiasys.com.br/", product, StringComparison.Ordinal);
        Assert.DoesNotContain("guiasys.online", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guiasys.online", product, StringComparison.OrdinalIgnoreCase);
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
        // Model 1 widened the workspace for three functional panes.
        Assert.Contains("MinWidth=\"980\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LicensedWorkspace\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProgramImage\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SidebarOutputsList\"", main, StringComparison.Ordinal);
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
        Assert.Contains("x:Key=\"GPPlayTransport\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CompactTransportButtonStyle\"", main, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"30\" />", main, StringComparison.Ordinal);
        // GP-I10: The approved 3-panel UI supersedes M10.3's two-column layout.
        Assert.Contains("<ColumnDefinition Width=\"255\" MinWidth=\"240\" />", main, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"345\" />", main, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"280\" MinWidth=\"240\" />", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviewProgressSlider\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MediaGrid\"", main, StringComparison.Ordinal);
        Assert.Contains("SavePlaylistPresetButton_OnClick", main, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedPresetButton_OnClick", main, StringComparison.Ordinal);
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

    [Fact]
    public void GP_I11_StartupPlaylistMetadataMustNeverStartNativeLibVlcOrShellCom()
    {
        // Crash reproduction: prior GUI startup spun up a LibVLC parser and
        // called Shell/GDI thumbnail APIs on a thread-pool thread. That worker
        // was completely independent of the deferred playback engine.
        var service = File.ReadAllText(
            PathInRepository("src/GuiaPlay.App/Services/MediaGridMetadataService.cs"));
        Assert.Contains("new FileInfo(path)", service, StringComparison.Ordinal);
        Assert.Contains("MaximumInspectedRows = 150", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new LibVLC(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("LibVLCSharp", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SHCreateItemFromParsingName", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseComObject", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBitmapSourceFromHBitmap", service, StringComparison.Ordinal);
        Assert.DoesNotContain("DllImport(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Marshal.", service, StringComparison.Ordinal);
    }

    [Fact]
    public void GP_I12_ApprovedLayoutKeepsPanoramicPreviewsAndFullWidthMediaActions()
    {
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));
        var code = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));

        Assert.Contains("Grid.RowSpan=\"2\"", main, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"49*\" MinHeight=\"190\" />", main, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"1\" Grid.Column=\"2\" Grid.ColumnSpan=\"3\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MediaRemoveSelectedButton\"", main, StringComparison.Ordinal);
        Assert.Contains("Click=\"RemoveSelectedMediaButton_OnClick\"", main, StringComparison.Ordinal);
        Assert.Contains("RemoveMediaGridRow(SelectedMediaRow())", code, StringComparison.Ordinal);
        Assert.Contains("MediaRemoveSelectedButton.IsEnabled = canMove;", code, StringComparison.Ordinal);
        Assert.Contains("Click=\"SavePlaylistPresetButton_OnClick\"", main, StringComparison.Ordinal);
        Assert.Contains("Click=\"NewPlaylistButton_OnClick\"", main, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AddMediaMenuButton\"", main, StringComparison.Ordinal);
        Assert.Equal(1, main.Split("x:Name=\"MediaGrid\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, main.Split("x:Name=\"AddMediaMenuButton\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void GP_I13_UsesNativeWpfChromeAndRealApprovedModelLayout()
    {
        var xaml = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));
        var code = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));

        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("shell:WindowChrome.WindowChrome", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LicenseStatusDot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TitleBarMaximize_OnClick", xaml, StringComparison.Ordinal);
        Assert.Contains("TitleBarClose_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TransportThumbnailImage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GPPlayTransport\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SAÍDA DE VÍDEO", xaml, StringComparison.Ordinal);
        Assert.Contains("GPVolumeDown", xaml, StringComparison.Ordinal);
        Assert.Contains("GPVolumeMute", xaml, StringComparison.Ordinal);
        Assert.Contains("SpeakerLowIcon.Visibility", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new LibVLC(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GP_I15_ResponsiveCustomWindowRespectsTaskbarAndSeparatesPages()
    {
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));
        var code = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));
        var settings = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml.cs"));
        var native = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Services/WindowWorkAreaMaximizer.cs"));
        var about = File.ReadAllText(PathInRepository("src/GuiaPlay.App/Windows/AboutWindow.xaml"));

        Assert.Contains("Viewbox Stretch=\"Uniform\" StretchDirection=\"DownOnly\"", main, StringComparison.Ordinal);
        Assert.Contains("GPVectorSymbol", main, StringComparison.Ordinal);
        Assert.Contains("GPVectorWordmark", main, StringComparison.Ordinal);
        Assert.Contains("WindowWorkAreaMaximizer.Apply(hwnd, lParam)", code, StringComparison.Ordinal);
        Assert.Contains("work.Left - bounds.Left", native, StringComparison.Ordinal);
        Assert.Contains("work.Right - work.Left", native, StringComparison.Ordinal);
        Assert.Contains("ScreensNavigationButton.Visibility = Visibility.Collapsed;", settings, StringComparison.Ordinal);
        Assert.Contains("AboutNavigationButton.Visibility = Visibility.Collapsed;", settings, StringComparison.Ordinal);
        Assert.Contains("SettingsHeadingText.Text = \"Monitores\";", settings, StringComparison.Ordinal);
        Assert.Contains("new AboutWindow", code, StringComparison.Ordinal);
        Assert.Contains("Sobre o GuiaPlay", about, StringComparison.Ordinal);
        Assert.Contains("Mídia movida na ordem de reprodução.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GP_I14_XamlIsWellFormedAndUniqueControlsSurviveLayoutCleanup()
    {
        var source = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));
        var document = XDocument.Parse(source);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var names = document.Descendants()
            .Select(element => (string?)element.Attribute(x + "Name"))
            .Where(name => name is not null)
            .ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        foreach (var control in new[]
        {
            "SidebarOutputsList", "IdentifyButton", "AudioDeviceNameText",
            "PlayButton", "PauseButton", "StopButton", "VolumeSlider",
            "ShufflePlaylistCheckBox", "RepeatModeButton", "FullscreenPreviewButton",
            "OpenButton", "PlaybackModeCombo", "HeaderSettingsButton"
        })
        {
            Assert.Contains(control, names);
        }
    }

    [Fact]
    public void GP_I14_ReferenceLayoutHasNoDuplicatedAudioVideoOrTransportControls()
    {
        var xaml = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml"));
        var code = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));

        Assert.DoesNotContain("x:Name=\"ConfigureScreensButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SideVolumeUpIcon\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlaylistOptionsMenuButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"MainPlayPauseButton_OnClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MainPauseGlyph\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"GPPlaybackToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"FullscreenPreviewButton_OnClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HeaderSettingsButton.IsEnabled", code, StringComparison.Ordinal);
        Assert.Contains("PauseButton_OnClick(sender, e);", code, StringComparison.Ordinal);
        Assert.Contains("var openDirect = new MenuItem", code, StringComparison.Ordinal);
        Assert.Contains("menu.Items.Add(sequential);", code, StringComparison.Ordinal);
        Assert.Contains("PlayButton.IsEnabled = PlayButton.IsEnabled || PauseButton.IsEnabled;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GP_I11_LicensingSettingsMustNotInitializeNativeLibVlc()
    {
        // A startup-only license prompt used to construct LibVLC because the
        // settings window requested _engine.EqualizerCatalog. This is a native
        // access-violation regression guard for code that does not need media.
        var main = File.ReadAllText(PathInRepository("src/GuiaPlay.App/MainWindow.xaml.cs"));
        var start = main.IndexOf("    private async Task OpenSettingsAsync(", StringComparison.Ordinal);
        var end = main.IndexOf("    private void UpdateManager_OnStateChanged(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var settingsHandler = main[start..end];

        Assert.Contains("_engineInstance?.EqualizerCatalog", settingsHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("_engine.EqualizerCatalog", settingsHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("await _engine.ApplyEqualizerAsync", settingsHandler, StringComparison.Ordinal);
        Assert.Contains("if (_engineInstance is { } activeEngine)", settingsHandler, StringComparison.Ordinal);
        Assert.Contains("if (_engineInstance is { } engineForSettings)", settingsHandler, StringComparison.Ordinal);

        var mute = main.IndexOf("    private void MuteButton_OnClick(", StringComparison.Ordinal);
        var muteEnd = main.IndexOf("    private void UpdateMuteVisual(", mute, StringComparison.Ordinal);
        Assert.True(mute >= 0 && muteEnd > mute);
        Assert.DoesNotContain("_engine.Muted", main[mute..muteEnd], StringComparison.Ordinal);
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
