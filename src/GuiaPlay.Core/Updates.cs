using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GuiaPlay.Core;

public enum UpdateChannel
{
    Prototype,
    Stable
}

public readonly partial record struct ProductVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<ProductVersion>
{
    [GeneratedRegex("^v?(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z][0-9A-Za-z.-]*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static ProductVersion Parse(string value) =>
        TryParse(value, out var version) ? version : throw new FormatException($"Versão inválida: '{value}'.");

    public static bool TryParse(string? value, out ProductVersion version)
    {
        var match = VersionPattern().Match(value?.Trim() ?? string.Empty);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, CultureInfo.InvariantCulture, out var patch))
        {
            version = default;
            return false;
        }

        version = new ProductVersion(major, minor, patch, match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
        return true;
    }

    public int CompareTo(ProductVersion other)
    {
        var numeric = Major.CompareTo(other.Major);
        if (numeric == 0) numeric = Minor.CompareTo(other.Minor);
        if (numeric == 0) numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0) return numeric;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        return string.Compare(Prerelease, other.Prerelease, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Prerelease is null ? string.Empty : $"-{Prerelease}")}";
    public static bool operator >(ProductVersion left, ProductVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ProductVersion left, ProductVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(ProductVersion left, ProductVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(ProductVersion left, ProductVersion right) => left.CompareTo(right) <= 0;
}

public sealed record ReleaseAsset(string Name, Uri DownloadUrl);

public sealed record PublishedRelease(
    ProductVersion Version,
    string Tag,
    DateTimeOffset PublishedAt,
    string? Notes,
    bool Prerelease,
    IReadOnlyList<ReleaseAsset> Assets);

public static class ReleaseSelector
{
    public static PublishedRelease? SelectLatest(IEnumerable<GitHubRelease> releases, UpdateChannel channel)
    {
        ArgumentNullException.ThrowIfNull(releases);
        return releases
            .Where(release => !release.Draft && ProductVersion.TryParse(release.TagName, out _))
            .Select(release => (Release: release, Version: ProductVersion.Parse(release.TagName)))
            .Where(candidate => IsCompatible(candidate.Version, candidate.Release.Prerelease, channel))
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => new PublishedRelease(
                candidate.Version,
                candidate.Release.TagName,
                candidate.Release.PublishedAt,
                candidate.Release.Body,
                candidate.Release.Prerelease,
                candidate.Release.Assets.Select(asset => new ReleaseAsset(asset.Name, asset.BrowserDownloadUrl)).ToArray()))
            .FirstOrDefault();
    }

    private static bool IsCompatible(ProductVersion version, bool prereleaseFlag, UpdateChannel channel) => channel switch
    {
        UpdateChannel.Stable => !prereleaseFlag && version.Prerelease is null,
        UpdateChannel.Prototype => version.Prerelease is null ||
                                   (prereleaseFlag && string.Equals(version.Prerelease, "prototipo", StringComparison.OrdinalIgnoreCase)),
        _ => false
    };
}

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoPublishedVersion,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    PublishedRelease? Release = null,
    UpdateManifest? Manifest = null,
    string? Error = null)
{
    public static UpdateCheckResult Failure(string error) => new(UpdateCheckStatus.Failed, Error: error);
}

public sealed partial record UpdateManifest(int Schema, string Version, string Channel, DateTimeOffset PublishedAt, UpdatePackage Package)
{
    public static bool TryParse(string json, out UpdateManifest? manifest, out string? error)
    {
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
            if (manifest is null || manifest.Schema != 1 ||
                !ProductVersion.TryParse(manifest.Version, out _) ||
                !string.Equals(manifest.Channel, "prototype", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(manifest.Package?.AssetName) ||
                Path.GetFileName(manifest.Package.AssetName) != manifest.Package.AssetName ||
                !Sha256Pattern().IsMatch(manifest.Package.Sha256 ?? string.Empty))
            {
                throw new JsonException("O manifesto não contém os campos esperados.");
            }

            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            manifest = null;
            error = exception.Message;
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

public sealed record UpdatePackage(string AssetName, string Sha256);

public static class UpdateSchedule
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(12);

    public static bool ShouldCheck(bool manual, bool enabled, DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc) =>
        manual || enabled && (lastCheckUtc is null || nowUtc - lastCheckUtc.Value >= AutomaticInterval);

    public static bool ShouldCheck(
        bool manual,
        bool enabled,
        AppSettings settings,
        ProductVersion currentVersion,
        DateTimeOffset nowUtc)
    {
        if (manual)
        {
            return true;
        }

        if (!enabled)
        {
            return false;
        }

        return !PersistedUpdateCache.IsValid(settings, currentVersion, nowUtc) ||
               nowUtc - settings.LastUpdateCheckUtc!.Value >= AutomaticInterval;
    }
}

public static class PersistedUpdateCache
{
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    public static bool IsValid(AppSettings settings, ProductVersion currentVersion, DateTimeOffset nowUtc)
    {
        if (settings.LastUpdateCheckUtc is not { } checkedAt ||
            checkedAt > nowUtc + MaximumClockSkew ||
            !ProductVersion.TryParse(settings.LastUpdateCheckProductVersion, out var checkedVersion) ||
            checkedVersion != currentVersion ||
            settings.LastKnownUpdateStatus is not { } status)
        {
            return false;
        }

        return status switch
        {
            UpdateCheckStatus.UpdateAvailable =>
                ProductVersion.TryParse(settings.LastKnownUpdateVersion, out var availableVersion) &&
                availableVersion > currentVersion &&
                settings.LastKnownUpdatePublishedAt is not null,
            UpdateCheckStatus.UpToDate =>
                ProductVersion.TryParse(settings.LastKnownUpdateVersion, out var publishedVersion) &&
                publishedVersion <= currentVersion,
            UpdateCheckStatus.NoPublishedVersion => string.IsNullOrWhiteSpace(settings.LastKnownUpdateVersion),
            _ => false
        };
    }

    public static UpdateCheckResult? Restore(AppSettings settings, ProductVersion currentVersion, DateTimeOffset nowUtc)
    {
        if (!IsValid(settings, currentVersion, nowUtc) || settings.LastKnownUpdateStatus is not { } status)
        {
            return null;
        }

        PublishedRelease? release = null;
        if (ProductVersion.TryParse(settings.LastKnownUpdateVersion, out var knownVersion))
        {
            release = new PublishedRelease(
                knownVersion,
                $"v{knownVersion}",
                settings.LastKnownUpdatePublishedAt ?? settings.LastUpdateCheckUtc!.Value,
                null,
                knownVersion.Prerelease is not null,
                []);
        }

        return new UpdateCheckResult(status, release);
    }

    public static AppSettings Record(
        AppSettings settings,
        ProductVersion currentVersion,
        UpdateCheckResult result,
        DateTimeOffset checkedAt)
    {
        var successful = result.Status != UpdateCheckStatus.Failed;
        return settings with
        {
            LastUpdateCheckUtc = checkedAt,
            LastUpdateCheckProductVersion = currentVersion.ToString(),
            LastKnownUpdateVersion = successful ? result.Release?.Version.ToString() : null,
            LastKnownUpdatePublishedAt = successful ? result.Release?.PublishedAt : null,
            LastKnownUpdateStatus = result.Status
        };
    }
}

public static class UpdateInstallationPolicy
{
    public static bool CanAutoInstall(bool enabled, bool updateAvailable, bool playbackActive, bool managedInstallation) =>
        enabled && updateAvailable && !playbackActive && managedInstallation;
}

public static class UpdateIndicatorState
{
    public static bool IsVisible(UpdateCheckStatus status) => status == UpdateCheckStatus.UpdateAvailable;
}

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] Uri BrowserDownloadUrl);
