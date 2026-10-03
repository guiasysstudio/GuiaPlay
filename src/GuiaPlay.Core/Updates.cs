using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GuiaPlay.Core;

public enum UpdateChannel
{
    Prototype,
    ReleaseCandidate,
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
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftTokens = TokenizePrerelease(left);
        var rightTokens = TokenizePrerelease(right);
        for (var index = 0; index < Math.Min(leftTokens.Count, rightTokens.Count); index++)
        {
            var leftToken = leftTokens[index];
            var rightToken = rightTokens[index];
            int comparison;
            if (leftToken.Numeric && rightToken.Numeric)
            {
                comparison = CompareNumericIdentifier(leftToken.Value, rightToken.Value);
            }
            else if (leftToken.Numeric != rightToken.Numeric)
            {
                comparison = leftToken.Numeric ? -1 : 1;
            }
            else
            {
                comparison = string.Compare(leftToken.Value, rightToken.Value, StringComparison.OrdinalIgnoreCase);
            }

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftTokens.Count.CompareTo(rightTokens.Count);
    }

    private static List<PrereleaseToken> TokenizePrerelease(string value)
    {
        var tokens = new List<PrereleaseToken>();
        for (var index = 0; index < value.Length;)
        {
            if (value[index] is '.' or '-')
            {
                index++;
                continue;
            }

            var numeric = char.IsAsciiDigit(value[index]);
            var start = index++;
            while (index < value.Length &&
                   value[index] is not '.' and not '-' &&
                   char.IsAsciiDigit(value[index]) == numeric)
            {
                index++;
            }

            tokens.Add(new PrereleaseToken(value[start..index], numeric));
        }

        return tokens;
    }

    private static int CompareNumericIdentifier(string left, string right)
    {
        var normalizedLeft = left.TrimStart('0');
        var normalizedRight = right.TrimStart('0');
        normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
        normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;
        var lengthComparison = normalizedLeft.Length.CompareTo(normalizedRight.Length);
        return lengthComparison != 0
            ? lengthComparison
            : string.CompareOrdinal(normalizedLeft, normalizedRight);
    }

    private readonly record struct PrereleaseToken(string Value, bool Numeric);

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

public static partial class ReleaseSelector
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

    internal static UpdateChannel? Classify(ProductVersion version)
    {
        if (version.Prerelease is null)
        {
            return UpdateChannel.Stable;
        }

        if (string.Equals(version.Prerelease, "prototipo", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateChannel.Prototype;
        }

        return RcPattern().IsMatch(version.Prerelease)
            ? UpdateChannel.ReleaseCandidate
            : null;
    }

    private static bool IsCompatible(ProductVersion version, bool prereleaseFlag, UpdateChannel channel)
    {
        var releaseChannel = Classify(version);
        if (releaseChannel is null || prereleaseFlag != (releaseChannel != UpdateChannel.Stable))
        {
            return false;
        }

        return channel switch
        {
            UpdateChannel.Prototype => true,
            UpdateChannel.ReleaseCandidate => releaseChannel is UpdateChannel.ReleaseCandidate or UpdateChannel.Stable,
            UpdateChannel.Stable => releaseChannel == UpdateChannel.Stable,
            _ => false
        };
    }

    [GeneratedRegex("^rc(?:\\.?[0-9]+)(?:[.-][0-9A-Za-z-]+)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RcPattern();
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
    UpdatePackage? SelectedPackage = null,
    string? Error = null)
{
    public static UpdateCheckResult Failure(string error) => new(UpdateCheckStatus.Failed, Error: error);
}

public sealed partial record UpdateManifest(
    int Schema,
    string Version,
    string Channel,
    DateTimeOffset PublishedAt,
    UpdatePackage Package,
    IReadOnlyDictionary<string, UpdatePackage>? Packages = null)
{
    public static bool TryParse(string json, out UpdateManifest? manifest, out string? error)
    {
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
            if (manifest is null || manifest.Schema != 1 ||
                !ProductVersion.TryParse(manifest.Version, out var version) ||
                !TryParseChannel(manifest.Channel, out var channel) ||
                ReleaseSelector.Classify(version) != channel ||
                !IsValidPackage(manifest.Package))
            {
                throw new JsonException("O manifesto não contém os campos esperados.");
            }

            if (manifest.Packages is not null &&
                (manifest.Packages.Count == 0 ||
                 manifest.Packages.Any(pair => !IsSupportedRuntimeIdentifier(pair.Key) || !IsValidPackage(pair.Value)) ||
                 !TryGetPackage(manifest.Packages, "win-x64", out var x64Package) ||
                 x64Package != manifest.Package))
            {
                throw new JsonException("Os pacotes por arquitetura do manifesto são inválidos.");
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

    public bool TrySelectPackage(string runtimeIdentifier, out UpdatePackage? package)
    {
        package = null;
        if (!IsSupportedRuntimeIdentifier(runtimeIdentifier))
        {
            return false;
        }

        if (Packages is not null && TryGetPackage(Packages, runtimeIdentifier, out package))
        {
            return true;
        }

        if (string.Equals(runtimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase))
        {
            package = Package;
            return true;
        }

        return false;
    }

    private static bool TryParseChannel(string? value, out UpdateChannel channel)
    {
        if (string.Equals(value, "prototype", StringComparison.OrdinalIgnoreCase))
        {
            channel = UpdateChannel.Prototype;
            return true;
        }

        if (string.Equals(value, "releaseCandidate", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "rc", StringComparison.OrdinalIgnoreCase))
        {
            channel = UpdateChannel.ReleaseCandidate;
            return true;
        }

        if (string.Equals(value, "stable", StringComparison.OrdinalIgnoreCase))
        {
            channel = UpdateChannel.Stable;
            return true;
        }

        channel = default;
        return false;
    }

    private static bool IsValidPackage(UpdatePackage? package) =>
        package is not null &&
        !string.IsNullOrWhiteSpace(package.AssetName) &&
        Path.GetFileName(package.AssetName) == package.AssetName &&
        Sha256Pattern().IsMatch(package.Sha256 ?? string.Empty);

    private static bool IsSupportedRuntimeIdentifier(string? runtimeIdentifier) =>
        string.Equals(runtimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(runtimeIdentifier, "win-x86", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPackage(
        IReadOnlyDictionary<string, UpdatePackage> packages,
        string runtimeIdentifier,
        out UpdatePackage? package)
    {
        var match = packages.FirstOrDefault(pair =>
            string.Equals(pair.Key, runtimeIdentifier, StringComparison.OrdinalIgnoreCase));
        package = match.Value;
        return package is not null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

public sealed record UpdatePackage(string AssetName, string Sha256);

public static class UpdateRuntimeIdentifier
{
    public static string? Current => FromArchitecture(RuntimeInformation.ProcessArchitecture);

    public static bool IsSupported(string? runtimeIdentifier) =>
        string.Equals(runtimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(runtimeIdentifier, "win-x86", StringComparison.OrdinalIgnoreCase);

    public static string? FromArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.X86 => "win-x86",
        _ => null
    };
}

public static class UpdateSchedule
{
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(12);

    public static bool ShouldCheck(bool manual, bool enabled, DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc) =>
        manual || enabled && (lastCheckUtc is null || nowUtc - lastCheckUtc.Value >= AutomaticInterval);

    public static bool ShouldCheckAtStartup(bool enabled) => enabled;

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

public sealed class StartupUpdateCheckCoordinator
{
    private readonly object _gate = new();
    private Task<UpdateCheckResult>? _startupCheck;

    public Task<UpdateCheckResult>? Start(
        bool automaticEnabled,
        Func<Task<UpdateCheckResult>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!UpdateSchedule.ShouldCheckAtStartup(automaticEnabled))
        {
            return null;
        }

        lock (_gate)
        {
            return _startupCheck ??= InvokeQuery(query);
        }
    }

    private static async Task<UpdateCheckResult> InvokeQuery(Func<Task<UpdateCheckResult>> query) =>
        await query().ConfigureAwait(false);
}

public static class UpdateResultRetention
{
    public static UpdateCheckResult SelectVisibleResult(
        UpdateCheckResult? previous,
        UpdateCheckResult networkResult) =>
        networkResult.Status == UpdateCheckStatus.Failed && previous?.Status == UpdateCheckStatus.UpdateAvailable
            ? previous
            : networkResult;
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
