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
        Assert.Equal("0.10.3-prototipo", ProductInfo.Version);
        Assert.Equal(new DateOnly(2026, 10, 2), ProductInfo.ReleaseDate);
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
    [InlineData("0.10.3-prototipo", "1.0.0-rc1")]
    [InlineData("1.0.0-rc1", "1.0.0-rc2")]
    [InlineData("1.0.0-rc2", "1.0.0-rc10")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10")]
    [InlineData("1.0.0-rc10", "1.0.0")]
    public void ComparesSemantically(string older, string newer) =>
        Assert.True(ProductVersion.Parse(older) < ProductVersion.Parse(newer));

    [Fact]
    public void EqualVersionsCompareEqual() =>
        Assert.Equal(0, ProductVersion.Parse("v0.5.0-prototipo").CompareTo(ProductVersion.Parse("0.5.0-prototipo")));

    [Fact]
    public void RcCompactAndDottedFormsHaveEquivalentPrecedence() =>
        Assert.Equal(0, ProductVersion.Parse("1.0.0-rc1").CompareTo(ProductVersion.Parse("1.0.0-rc.1")));
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

    [Fact]
    public void PrototypeAcceptsLaterPrototypeReleaseCandidateAndStable()
    {
        var releases = new[]
        {
            Release("v0.10.4-prototipo", prerelease: true),
            Release("v1.0.0-rc2", prerelease: true),
            Release("v1.0.0")
        };

        Assert.Equal("1.0.0", ReleaseSelector.SelectLatest(releases, UpdateChannel.Prototype)!.Version.ToString());
    }

    [Fact]
    public void ReleaseCandidateRejectsPrototypeButAcceptsRcAndStable()
    {
        var releases = new[]
        {
            Release("v9.0.0-prototipo", prerelease: true),
            Release("v1.0.0-rc2", prerelease: true),
            Release("v1.0.0")
        };

        Assert.Equal("1.0.0", ReleaseSelector.SelectLatest(releases, UpdateChannel.ReleaseCandidate)!.Version.ToString());
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
        Assert.Equal("package.zip", result.SelectedPackage!.AssetName);
    }

    [Theory]
    [InlineData("0.10.3-prototipo", UpdateChannel.Prototype, "1.0.0-rc1", true, "releaseCandidate")]
    [InlineData("1.0.0-rc1", UpdateChannel.ReleaseCandidate, "1.0.0-rc2", true, "releaseCandidate")]
    [InlineData("1.0.0-rc2", UpdateChannel.ReleaseCandidate, "1.0.0", false, "stable")]
    public async Task FutureChannelTransitionsAreUpdates(
        string current,
        UpdateChannel channel,
        string published,
        bool prerelease,
        string manifestChannel)
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("update-manifest.json", StringComparison.Ordinal)
            ? Json(Manifest(published, "package.zip", manifestChannel))
            : Json(Releases($"v{published}", "package.zip", prerelease)));

        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse(current), channel, runtimeIdentifier: "win-x64");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public async Task StableChannelNeverDowngradesToReleaseCandidate()
    {
        using var client = Client(_ => Json(Releases("v1.0.0-rc3", "package.zip", prerelease: true)));

        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("1.0.0"), UpdateChannel.Stable, runtimeIdentifier: "win-x64");

        Assert.NotEqual(UpdateCheckStatus.UpdateAvailable, result.Status);
    }

    [Theory]
    [InlineData("win-x64", "x64.zip")]
    [InlineData("win-x86", "x86.zip")]
    public async Task MultiArchitectureManifestSelectsExactRuntimePackage(string runtimeIdentifier, string expectedAsset)
    {
        var manifest = """
            {"schema":1,"version":"0.10.3-prototipo","channel":"prototype","publishedAt":"2026-10-02T12:00:00Z",
             "package":{"assetName":"x64.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
             "packages":{
               "win-x64":{"assetName":"x64.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
               "win-x86":{"assetName":"x86.zip","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}}
            """;
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("update-manifest.json", StringComparison.Ordinal)
            ? Json(manifest)
            : Json(ReleasesWithAssets("v0.10.3-prototipo", true, "x64.zip", "x86.zip")));

        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse("0.10.2-prototipo"), UpdateChannel.Prototype, runtimeIdentifier: runtimeIdentifier);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(expectedAsset, result.SelectedPackage!.AssetName);
    }

    [Fact]
    public async Task LegacyManifestRemainsX64Only()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("update-manifest.json", StringComparison.Ordinal)
            ? Json(Manifest("0.10.3-prototipo", "x64.zip"))
            : Json(Releases("v0.10.3-prototipo", "x64.zip")));
        var service = new GitHubUpdateService(client, "o", "r");

        var x64 = await service.CheckAsync(
            ProductVersion.Parse("0.10.2-prototipo"),
            UpdateChannel.Prototype,
            runtimeIdentifier: "win-x64");
        var x86 = await service.CheckAsync(
            ProductVersion.Parse("0.10.2-prototipo"),
            UpdateChannel.Prototype,
            runtimeIdentifier: "win-x86");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, x64.Status);
        Assert.Equal(UpdateCheckStatus.Failed, x86.Status);
        Assert.Contains("win-x86", x86.Error);
    }

    [Theory]
    [InlineData("0.6.0-prototipo", "v0.6.0-prototipo")]
    [InlineData("0.6.0-prototipo", "v0.5.0-prototipo")]
    public async Task ReportsUpToDateForEqualOrOlderPublishedVersion(string currentVersion, string publishedTag)
    {
        using var client = Client(_ => Json(Releases(publishedTag, "package.zip")));
        var result = await new GitHubUpdateService(client, "o", "r")
            .CheckAsync(ProductVersion.Parse(currentVersion), UpdateChannel.Prototype);
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

    private static string Releases(string tag, string packageName, bool prerelease = true) =>
        $"[{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":{prerelease.ToString().ToLowerInvariant()},\"published_at\":\"2026-09-25T12:00:00Z\",\"body\":\"Notas\",\"assets\":[" +
        "{\"name\":\"update-manifest.json\",\"browser_download_url\":\"https://example.test/update-manifest.json\"}," +
        $"{{\"name\":\"{packageName}\",\"browser_download_url\":\"https://example.test/{packageName}\"}}]}}]";

    private static string ReleasesWithAssets(string tag, bool prerelease, params string[] packageNames) =>
        $"[{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":{prerelease.ToString().ToLowerInvariant()},\"published_at\":\"2026-10-02T12:00:00Z\",\"body\":\"Notas\",\"assets\":[" +
        "{\"name\":\"update-manifest.json\",\"browser_download_url\":\"https://example.test/update-manifest.json\"}," +
        string.Join(',', packageNames.Select(name => $"{{\"name\":\"{name}\",\"browser_download_url\":\"https://example.test/{name}\"}}")) + "]}]";

    private static string Manifest(string version, string packageName, string channel = "prototype") =>
        $"{{\"schema\":1,\"version\":\"{version}\",\"channel\":\"{channel}\",\"publishedAt\":\"2026-09-25T12:00:00Z\"," +
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

    [Fact]
    public void StartupWithoutPreviousCheckQueries()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(UpdateSchedule.ShouldCheck(
            false,
            true,
            AppSettings.Default,
            ProductVersion.Parse("0.6.0-prototipo"),
            now));
    }

    [Fact]
    public void RecentValidAvailableCacheRestoresIndicatorWithoutQuery()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CachedSettings(now, "0.6.0-prototipo", UpdateCheckStatus.UpdateAvailable, "0.7.0-prototipo");

        var restored = PersistedUpdateCache.Restore(settings, ProductVersion.Parse("0.6.0-prototipo"), now);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, restored?.Status);
        Assert.True(UpdateIndicatorState.IsVisible(restored!.Status));
        Assert.False(UpdateSchedule.ShouldCheck(false, true, settings, ProductVersion.Parse("0.6.0-prototipo"), now));
    }

    [Fact]
    public void RecentUpToDateCacheDoesNotShowIndicator()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CachedSettings(now, "0.6.0-prototipo", UpdateCheckStatus.UpToDate, "0.6.0-prototipo");

        var restored = PersistedUpdateCache.Restore(settings, ProductVersion.Parse("0.6.0-prototipo"), now);

        Assert.Equal(UpdateCheckStatus.UpToDate, restored?.Status);
        Assert.False(UpdateIndicatorState.IsVisible(restored!.Status));
    }

    [Fact]
    public void RecentTimestampWithoutValidStateForcesQuery()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = AppSettings.Default with { LastUpdateCheckUtc = now };

        Assert.True(UpdateSchedule.ShouldCheck(false, true, settings, ProductVersion.Parse("0.6.0-prototipo"), now));
    }

    [Fact]
    public void CheckMadeByNewerProductForcesQueryInOlderProduct()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CachedSettings(now, "0.6.0-prototipo", UpdateCheckStatus.UpToDate, "0.6.0-prototipo");

        Assert.True(UpdateSchedule.ShouldCheck(false, true, settings, ProductVersion.Parse("0.5.0-prototipo"), now));
    }

    [Fact]
    public void CheckMadeBySameProductRespectsInterval()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CachedSettings(now.AddHours(-11), "0.6.0-prototipo", UpdateCheckStatus.UpToDate, "0.6.0-prototipo");

        Assert.False(UpdateSchedule.ShouldCheck(false, true, settings, ProductVersion.Parse("0.6.0-prototipo"), now));
    }

    [Fact]
    public void ManualCheckAlwaysQueriesWithValidRecentCache()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CachedSettings(now, "0.6.0-prototipo", UpdateCheckStatus.UpToDate, "0.6.0-prototipo");

        Assert.True(UpdateSchedule.ShouldCheck(true, false, settings, ProductVersion.Parse("0.6.0-prototipo"), now));
    }

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

    private static AppSettings CachedSettings(
        DateTimeOffset checkedAt,
        string checkedProductVersion,
        UpdateCheckStatus status,
        string? knownVersion) =>
        AppSettings.Default with
        {
            LastUpdateCheckUtc = checkedAt,
            LastUpdateCheckProductVersion = checkedProductVersion,
            LastKnownUpdateVersion = knownVersion,
            LastKnownUpdatePublishedAt = knownVersion is null ? null : checkedAt,
            LastKnownUpdateStatus = status
        };
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
    public async Task DownloaderReportsRealByteProgressFromContentLength()
    {
        Directory.CreateDirectory(_root);
        var bytes = new byte[200_000];
        var reports = new List<UpdateDownloadProgress>();
        using var client = new HttpClient(new BytesHandler(bytes));

        await new UpdatePackageDownloader(client).DownloadAsync(
            new Uri("https://example.test/package.zip"),
            Path.Combine(_root, "progress.zip"),
            new CollectingProgress<UpdateDownloadProgress>(reports));

        var final = Assert.IsType<UpdateDownloadProgress>(reports.Last());
        Assert.Equal(bytes.Length, final.BytesReceived);
        Assert.Equal(bytes.Length, final.TotalBytes);
        Assert.Equal(100, final.Percentage);
    }

    [Fact]
    public async Task DownloaderReportsBytesWithoutInventingPercentageWhenLengthIsUnknown()
    {
        Directory.CreateDirectory(_root);
        var bytes = new byte[12_345];
        var reports = new List<UpdateDownloadProgress>();
        using var client = new HttpClient(new UnknownLengthHandler(bytes));

        await new UpdatePackageDownloader(client).DownloadAsync(
            new Uri("https://example.test/package.zip"),
            Path.Combine(_root, "unknown-length.zip"),
            new CollectingProgress<UpdateDownloadProgress>(reports));

        var final = reports.Last();
        Assert.Equal(bytes.Length, final.BytesReceived);
        Assert.Null(final.TotalBytes);
        Assert.Null(final.Percentage);
    }

    [Fact]
    public async Task CancelledDownloadDeletesPartialPackage()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "partial.zip");
        using var client = new HttpClient(new StreamingHandler(new PartialThenBlockStream()));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UpdatePackageDownloader(client).DownloadAsync(
                new Uri("https://example.test/partial.zip"),
                path,
                cancellationToken: cancellation.Token));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void InvalidManifestIsRejected()
    {
        Assert.False(UpdateManifest.TryParse("{\"schema\":2}", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ManifestRejectsUnknownRuntimeIdentifier()
    {
        const string json = """
            {"schema":1,"version":"0.10.3-prototipo","channel":"prototype","publishedAt":"2026-10-02T12:00:00Z",
             "package":{"assetName":"x64.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
             "packages":{
               "win-x64":{"assetName":"x64.zip","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
               "linux-x64":{"assetName":"linux.zip","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}}
            """;

        Assert.False(UpdateManifest.TryParse(json, out _, out var error));
        Assert.Contains("arquitetura", error, StringComparison.OrdinalIgnoreCase);
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
    public void ZipEntryCountLimitIsEnforcedBeforeExtraction()
    {
        Directory.CreateDirectory(_root);
        var zipPath = Path.Combine(_root, "too-many.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("one.txt");
            archive.CreateEntry("two.txt");
            archive.CreateEntry("three.txt");
        }

        Assert.Throws<InvalidDataException>(() => SafeZipExtractor.Extract(
            zipPath,
            Path.Combine(_root, "entries-out"),
            new ZipExtractionLimits(2, 1024)));
        Assert.False(Directory.Exists(Path.Combine(_root, "entries-out")));
    }

    [Fact]
    public void ZipUncompressedSizeLimitIsEnforcedBeforeExtraction()
    {
        Directory.CreateDirectory(_root);
        var zipPath = Path.Combine(_root, "too-large.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("large.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("0123456789");
        }

        Assert.Throws<InvalidDataException>(() => SafeZipExtractor.Extract(
            zipPath,
            Path.Combine(_root, "size-out"),
            new ZipExtractionLimits(10, 5)));
        Assert.False(Directory.Exists(Path.Combine(_root, "size-out")));
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
        var detected = ManagedInstallationDetector.Detect(executable);
        Assert.NotNull(detected);
        Assert.Equal("win-x64", detected.RuntimeIdentifier);
    }

    [Fact]
    public void AppliesUpdateInTemporaryInstallation()
    {
        var (source, target, backup) = CreateInstallTrees();
        var result = UpdateApplicator.Apply(source, target, backup);
        Assert.True(result.Succeeded);
        Assert.Equal("B", File.ReadAllText(Path.Combine(target, "app.txt")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(backup, "app.txt")));
        Assert.True(InstallMarkerStore.TryRead(Path.Combine(target, "install.json"), out var version, out var rid));
        Assert.Equal("0.10.3-prototipo", version);
        Assert.Equal("win-x64", rid);
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
        Assert.True(InstallMarkerStore.TryRead(Path.Combine(target, "install.json"), out var version, out _));
        Assert.Equal("0.10.2-prototipo", version);
    }

    [Fact]
    public void BackupFailureNeverModifiesInstallationOrAttemptsPartialRollback()
    {
        var (source, target, backup) = CreateInstallTrees();

        var result = UpdateApplicator.Apply(
            source,
            target,
            backup,
            failBeforeCopy: null,
            failDuringBackup: copyNumber => copyNumber == 1);

        Assert.False(result.Succeeded);
        Assert.False(result.RolledBack);
        Assert.Equal("A", File.ReadAllText(Path.Combine(target, "app.txt")));
        Assert.True(File.Exists(Path.Combine(target, "obsolete.txt")));
        Assert.True(InstallMarkerStore.TryRead(Path.Combine(target, "install.json"), out var version, out _));
        Assert.Equal("0.10.2-prototipo", version);
    }

    [Fact]
    public void ApplicatorRejectsIncompletePayloadBeforeCreatingBackupOrChangingInstallation()
    {
        var (source, target, backup) = CreateInstallTrees();
        File.Delete(Path.Combine(source, "GuiaPlay.exe"));

        var error = Assert.Throws<InvalidDataException>(() => UpdateApplicator.Apply(source, target, backup));

        Assert.Contains("arquivo obrigatório", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("A", File.ReadAllText(Path.Combine(target, "app.txt")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void ApplicatorRejectsCrossArchitecturePackageBeforeChangingInstallation()
    {
        var (source, target, backup) = CreateInstallTrees();
        InstallMarkerStore.WriteAtomic(Path.Combine(source, "install.json"), "0.10.3-prototipo", "win-x86");

        var error = Assert.Throws<InvalidDataException>(() => UpdateApplicator.Apply(source, target, backup));

        Assert.Contains("arquiteturas diferentes", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("A", File.ReadAllText(Path.Combine(target, "app.txt")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void ApplicatorRejectsSameVersionOrDowngradeBeforeChangingInstallation()
    {
        var (source, target, backup) = CreateInstallTrees();
        InstallMarkerStore.WriteAtomic(Path.Combine(source, "install.json"), "0.10.2-prototipo", "win-x64");

        var error = Assert.Throws<InvalidDataException>(() => UpdateApplicator.Apply(source, target, backup));

        Assert.Contains("não é mais novo", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("A", File.ReadAllText(Path.Combine(target, "app.txt")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void InstallMarkerReplacementIsAtomicAndLeavesNoTemporaryFile()
    {
        var markerPath = Path.Combine(_root, "atomic", "install.json");
        InstallMarkerStore.WriteAtomic(markerPath, "0.10.2-prototipo", "win-x64");

        InstallMarkerStore.WriteAtomic(markerPath, "0.10.3-prototipo", "win-x64");

        Assert.True(InstallMarkerStore.TryRead(markerPath, out var version, out var rid));
        Assert.Equal("0.10.3-prototipo", version);
        Assert.Equal("win-x64", rid);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(markerPath)!, "install.json.tmp-*"));
    }

    [Fact]
    public void WorkspaceRetentionOnlyDeletesRecognizedGuiaPlayDirectories()
    {
        var temp = Path.Combine(_root, "retention");
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var staleUpdate = UpdateWorkspaceRetention.CreateDirectory(temp, UpdateWorkspaceKind.Update, "stale", now.AddDays(-3));
        var freshUpdate = UpdateWorkspaceRetention.CreateDirectory(temp, UpdateWorkspaceKind.Update, "fresh", now);
        var oldestBackup = UpdateWorkspaceRetention.CreateDirectory(temp, UpdateWorkspaceKind.Backup, "backup-old", now.AddDays(-3));
        var middleBackup = UpdateWorkspaceRetention.CreateDirectory(temp, UpdateWorkspaceKind.Backup, "backup-middle", now.AddDays(-2));
        var newestBackup = UpdateWorkspaceRetention.CreateDirectory(temp, UpdateWorkspaceKind.Backup, "backup-new", now.AddDays(-1));
        var foreign = Path.Combine(temp, "GuiaPlayUpdate", "foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "keep.txt"), "not owned by GuiaPlay");

        var result = UpdateWorkspaceRetention.Cleanup(temp, now, backupRetention: 2);

        Assert.False(Directory.Exists(staleUpdate));
        Assert.True(Directory.Exists(freshUpdate));
        Assert.False(Directory.Exists(oldestBackup));
        Assert.True(Directory.Exists(middleBackup));
        Assert.True(Directory.Exists(newestBackup));
        Assert.True(Directory.Exists(foreign));
        Assert.Equal(2, result.DeletedDirectories);
    }

    private (string Source, string Target, string Backup) CreateInstallTrees()
    {
        var source = Path.Combine(_root, Guid.NewGuid().ToString("N"), "source");
        var target = Path.Combine(_root, Guid.NewGuid().ToString("N"), "target");
        var backup = Path.Combine(_root, Guid.NewGuid().ToString("N"), "backup");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(source, "app.txt"), "B");
        File.WriteAllText(Path.Combine(source, "GuiaPlay.exe"), "app");
        File.WriteAllText(Path.Combine(source, "GuiaPlay.Updater.exe"), "updater");
        var nativeDirectory = Path.Combine(source, "libvlc", "win-x64");
        Directory.CreateDirectory(Path.Combine(nativeDirectory, "plugins"));
        File.WriteAllText(Path.Combine(nativeDirectory, "libvlc.dll"), "libvlc");
        File.WriteAllText(Path.Combine(nativeDirectory, "libvlccore.dll"), "libvlccore");
        File.WriteAllText(Path.Combine(target, "app.txt"), "A");
        File.WriteAllText(Path.Combine(target, "obsolete.txt"), "old");
        InstallMarkerStore.WriteAtomic(Path.Combine(source, "install.json"), "0.10.3-prototipo", "win-x64");
        InstallMarkerStore.WriteAtomic(Path.Combine(target, "install.json"), "0.10.2-prototipo", "win-x64");
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

    private sealed class UnknownLengthHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(bytes) });
    }

    private sealed class StreamingHandler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
    }

    private sealed class PartialThenBlockStream : Stream
    {
        private bool _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                var count = Math.Min(1024, buffer.Length);
                buffer.Span[..count].Fill(0x5A);
                return count;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CollectingProgress<T>(ICollection<T> values) : IProgress<T>
    {
        public void Report(T value) => values.Add(value);
    }
}
