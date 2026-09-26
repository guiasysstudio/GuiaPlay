using System.IO.Compression;
using System.Net;
using System.Text;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class ProductVersionTests
{
    [Fact]
    public void ProductMetadataUsesCentralVersionAndReleaseDate()
    {
        Assert.Equal("0.5.0-prototipo", ProductInfo.Version);
        Assert.Equal(new DateOnly(2026, 9, 25), ProductInfo.ReleaseDate);
        Assert.Equal(UpdateChannel.Prototype, ProductInfo.Channel);
    }

    [Theory]
    [InlineData("0.5.0-prototipo", 0, 5, 0, "prototipo")]
    [InlineData("v1.0.1", 1, 0, 1, null)]
    public void ParsesSupportedVersions(string text, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(ProductVersion.TryParse(text, out var version));
        Assert.Equal((major, minor, patch, prerelease), (version.Major, version.Minor, version.Patch, version.Prerelease));
    }

    [Theory]
    [InlineData("0.5.0-prototipo", "0.6.0-prototipo")]
    [InlineData("0.5.1-prototipo", "0.6.0-prototipo")]
    [InlineData("0.9.9-prototipo", "1.0.0")]
    [InlineData("1.0.0-prototipo", "1.0.0")]
    public void ComparesSemantically(string older, string newer) =>
        Assert.True(ProductVersion.Parse(older) < ProductVersion.Parse(newer));

    [Fact]
    public void EqualVersionsCompareEqual() =>
        Assert.Equal(0, ProductVersion.Parse("v0.5.0-prototipo").CompareTo(ProductVersion.Parse("0.5.0-prototipo")));
}

public sealed class ReleaseSelectorTests
{
    [Fact]
    public void PrototypeIncludesPrereleaseIgnoresDraftAndInvalidAndSelectsLatest()
    {
        var releases = new[]
        {
            Release("v9.0.0-prototipo", draft: true, prerelease: true),
            Release("not-a-version", prerelease: true),
            Release("v0.5.1-prototipo", prerelease: true),
            Release("v0.6.0-prototipo", prerelease: true)
        };

        var latest = ReleaseSelector.SelectLatest(releases, UpdateChannel.Prototype);

        Assert.NotNull(latest);
        Assert.Equal("0.6.0-prototipo", latest.Version.ToString());
    }

    [Fact]
    public void StableIgnoresPrereleases()
    {
        var latest = ReleaseSelector.SelectLatest(
            [Release("v2.0.0-prototipo", prerelease: true), Release("v1.0.1")],
            UpdateChannel.Stable);

        Assert.Equal("1.0.1", latest!.Version.ToString());
    }

    private static GitHubRelease Release(string tag, bool draft = false, bool prerelease = false) =>
        new(tag, draft, prerelease, DateTimeOffset.Parse("2026-09-25T12:00:00Z"), "notes", []);
}

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task ReportsUpdateAvailableWithValidManifest()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("update-manifest.json", StringComparison.Ordinal)
            ? Json(Manifest("0.6.0-prototipo", "package.zip"))
            : Json(Releases("v0.6.0-prototipo", "package.zip")));
        var service = new GitHubUpdateService(client, "guiasysstudio", "GuiaPlay");

        var result = await service.CheckAsync(ProductVersion.Parse("0.5.0-prototipo"), UpdateChannel.Prototype);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.6.0-prototipo", result.Manifest!.Version);
    }

    [Fact]
    public async Task DoesNotOfferDowngradeOrEqualVersion()
    {
        using var client = Client(_ => Json(Releases("v0.5.0-prototipo", "package.zip")));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.6.0-prototipo"), UpdateChannel.Prototype);
        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task ReportsNoPublishedVersionWhenOnlyDraftExists()
    {
        const string json = "[{\"tag_name\":\"v0.6.0-prototipo\",\"draft\":true,\"prerelease\":true,\"published_at\":\"2026-09-25T12:00:00Z\",\"body\":\"\",\"assets\":[]}]";
        using var client = Client(_ => Json(json));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.5.0-prototipo"), UpdateChannel.Prototype);
        Assert.Equal(UpdateCheckStatus.NoPublishedVersion, result.Status);
    }

    [Fact]
    public async Task ReportsHttpError()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.5.0-prototipo"), UpdateChannel.Prototype);
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("503", result.Error);
    }

    [Fact]
    public async Task ReportsTimeout()
    {
        using var client = new HttpClient(new DelegateHandler(_ => throw new TaskCanceledException()));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.5.0-prototipo"), UpdateChannel.Prototype);
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("tempo limite", result.Error);
    }

    [Fact]
    public async Task ReportsInvalidJson()
    {
        using var client = Client(_ => Json("{ invalid"));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.5.0-prototipo"), UpdateChannel.Prototype);
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ReportsMissingManifestAsset()
    {
        const string json = "[{\"tag_name\":\"v0.6.0-prototipo\",\"draft\":false,\"prerelease\":true,\"published_at\":\"2026-09-25T12:00:00Z\",\"body\":\"\",\"assets\":[]}]";
        using var client = Client(_ => Json(json));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.5.0-prototipo"), UpdateChannel.Prototype);
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("manifest", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportsInvalidManifest()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("update-manifest.json", StringComparison.Ordinal)
            ? Json("{}")
            : Json(Releases("v0.6.0-prototipo", "package.zip")));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.5.0-prototipo"), UpdateChannel.Prototype);
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("inválido", result.Error);
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new DelegateHandler(response));
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string Releases(string tag, string packageName) =>
        $"[{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":true,\"published_at\":\"2026-09-25T12:00:00Z\",\"body\":\"Notas\",\"assets\":[" +
        "{\"name\":\"update-manifest.json\",\"browser_download_url\":\"https://example.test/update-manifest.json\"}," +
        $"{{\"name\":\"{packageName}\",\"browser_download_url\":\"https://example.test/{packageName}\"}}]}}]";

    private static string Manifest(string version, string packageName) =>
        $"{{\"schema\":1,\"version\":\"{version}\",\"channel\":\"prototype\",\"publishedAt\":\"2026-09-25T12:00:00Z\"," +
        $"\"package\":{{\"assetName\":\"{packageName}\",\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}}}";

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}

public sealed class UpdatePolicyTests
{
    [Fact]
    public void AutomaticCheckRespectsTwelveHours()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(UpdateSchedule.ShouldCheck(false, true, now.AddHours(-11), now));
        Assert.True(UpdateSchedule.ShouldCheck(false, true, now.AddHours(-12), now));
    }

    [Fact]
    public void ManualCheckIgnoresDisabledSettingAndInterval() =>
        Assert.True(UpdateSchedule.ShouldCheck(true, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData(UpdateCheckStatus.UpdateAvailable, true)]
    [InlineData(UpdateCheckStatus.UpToDate, false)]
    [InlineData(UpdateCheckStatus.Failed, false)]
    public void IndicatorVisibilityFollowsStatus(UpdateCheckStatus status, bool expected) =>
        Assert.Equal(expected, UpdateIndicatorState.IsVisible(status));

    [Fact]
    public void PlaybackPreventsAutomaticInstall()
    {
        Assert.False(UpdateInstallationPolicy.CanAutoInstall(true, true, true, true));
        Assert.True(UpdateInstallationPolicy.CanAutoInstall(true, true, false, true));
    }
}

public sealed class PackageSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GuiaPlay.M05.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidAndInvalidShaAreDetected()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "package.bin");
        await File.WriteAllTextAsync(path, "GuiaPlay");
        var hash = await PackageIntegrity.ComputeSha256Async(path);
        Assert.True(await PackageIntegrity.VerifySha256Async(path, hash));
        Assert.False(await PackageIntegrity.VerifySha256Async(path, new string('0', 64)));
    }

    [Fact]
    public async Task DownloaderDeletesPackageWhenShaIsInvalid()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "download.zip");
        using var client = new HttpClient(new BytesHandler("package bytes"u8.ToArray()));
        var downloader = new UpdatePackageDownloader(client);

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAndVerifyAsync(
            new Uri("https://example.test/package.zip"),
            path,
            new string('0', 64)));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void InvalidManifestIsRejected()
    {
        Assert.False(UpdateManifest.TryParse("{\"schema\":2}", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ZipSlipIsRejected()
    {
        Directory.CreateDirectory(_root);
        var zipPath = Path.Combine(_root, "bad.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("unsafe");
        }

        Assert.Throws<InvalidDataException>(() => SafeZipExtractor.Extract(zipPath, Path.Combine(_root, "out")));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public void DevelopmentModeWithoutMarkerIsRejectedAndManagedInstallIsAccepted()
    {
        var install = Path.Combine(_root, "install");
        Directory.CreateDirectory(install);
        var executable = Path.Combine(install, "GuiaPlay.exe");
        File.WriteAllText(executable, string.Empty);
        Assert.Null(ManagedInstallationDetector.Detect(executable));

        File.WriteAllText(Path.Combine(install, "install.json"), "{\"schema\":1,\"appId\":\"GuiaSys.GuiaPlay\",\"version\":\"0.5.0-prototipo\"}");
        Assert.NotNull(ManagedInstallationDetector.Detect(executable));
    }

    [Fact]
    public void AppliesUpdateInTemporaryInstallation()
    {
        var (source, target, backup) = CreateInstallTrees();
        var result = UpdateApplicator.Apply(source, target, backup);
        Assert.True(result.Succeeded);
        Assert.Equal("B", File.ReadAllText(Path.Combine(target, "app.txt")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(backup, "app.txt")));
        Assert.True(File.Exists(Path.Combine(target, "install.json")));
    }

    [Fact]
    public void FailureRollsBackTemporaryInstallation()
    {
        var (source, target, backup) = CreateInstallTrees();
        File.WriteAllText(Path.Combine(source, "second.txt"), "new");
        var result = UpdateApplicator.Apply(source, target, backup, copyNumber => copyNumber == 2);
        Assert.False(result.Succeeded);
        Assert.True(result.RolledBack);
        Assert.Equal("A", File.ReadAllText(Path.Combine(target, "app.txt")));
        Assert.False(File.Exists(Path.Combine(target, "second.txt")));
        Assert.True(File.Exists(Path.Combine(target, "install.json")));
    }

    private (string Source, string Target, string Backup) CreateInstallTrees()
    {
        var source = Path.Combine(_root, Guid.NewGuid().ToString("N"), "source");
        var target = Path.Combine(_root, Guid.NewGuid().ToString("N"), "target");
        var backup = Path.Combine(_root, Guid.NewGuid().ToString("N"), "backup");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "app.txt"), "B");
        File.WriteAllText(Path.Combine(target, "app.txt"), "A");
        File.WriteAllText(Path.Combine(target, "obsolete.txt"), "old");
        File.WriteAllText(Path.Combine(target, "install.json"), "marker");
        return (source, target, backup);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
