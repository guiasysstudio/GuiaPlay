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
