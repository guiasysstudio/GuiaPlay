using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GuiaPlay.Core;

public sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public int? Percentage => TotalBytes is > 0
        ? (int)Math.Clamp(BytesReceived * 100L / TotalBytes.Value, 0, 100)
        : null;
}

public static class PackageIntegrity
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<bool> VerifySha256Async(string filePath, string expected, CancellationToken cancellationToken = default) =>
        string.Equals(await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false), expected, StringComparison.OrdinalIgnoreCase);
}

public sealed class UpdatePackageDownloader(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<string> DownloadAsync(
        Uri source,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        try
        {
            using var response = await _httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            progress?.Report(new UpdateDownloadProgress(0, totalBytes));
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            long received = 0;
            var lastReport = Stopwatch.GetTimestamp();
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                var now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(lastReport, now) >= TimeSpan.FromMilliseconds(100))
                {
                    progress?.Report(new UpdateDownloadProgress(received, totalBytes));
                    lastReport = now;
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(new UpdateDownloadProgress(received, totalBytes));
            return destinationPath;
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
    }

    public async Task<string> DownloadAndVerifyAsync(
        Uri source,
        string destinationPath,
        string expectedSha256,
        CancellationToken cancellationToken = default,
        IProgress<UpdateDownloadProgress>? progress = null)
    {
        try
        {
            await DownloadAsync(source, destinationPath, progress, cancellationToken).ConfigureAwait(false);

            if (!await PackageIntegrity.VerifySha256Async(destinationPath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException("O SHA-256 do pacote baixado não confere com o manifesto.");
            }

            return destinationPath;
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed record ZipExtractionLimits(int MaximumEntries, long MaximumUncompressedBytes)
{
    public static ZipExtractionLimits Default { get; } = new(25_000, 4L * 1024 * 1024 * 1024);
}

public static class SafeZipExtractor
{
    public static void Extract(string archivePath, string destinationDirectory) =>
        Extract(archivePath, destinationDirectory, ZipExtractionLimits.Default);

    public static void Extract(string archivePath, string destinationDirectory, ZipExtractionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaximumEntries <= 0 || limits.MaximumUncompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits));
        }

        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > limits.MaximumEntries)
        {
            throw new InvalidDataException($"O pacote excede o limite de {limits.MaximumEntries} entradas.");
        }

        long declaredTotal = 0;
        try
        {
            foreach (var entry in archive.Entries)
            {
                declaredTotal = checked(declaredTotal + entry.Length);
                if (declaredTotal > limits.MaximumUncompressedBytes)
                {
                    throw new InvalidDataException("O pacote excede o limite de tamanho descompactado.");
                }
            }
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("O tamanho descompactado declarado pelo pacote é inválido.", exception);
        }

        Directory.CreateDirectory(destinationRoot);
        long extractedTotal = 0;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Entrada ZIP insegura rejeitada: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                extractedTotal = checked(extractedTotal + read);
                if (extractedTotal > limits.MaximumUncompressedBytes)
                {
                    throw new InvalidDataException("O pacote excede o limite de tamanho descompactado.");
                }

                output.Write(buffer, 0, read);
            }
        }
    }
}

public sealed record ManagedInstallation(string RootDirectory, string MarkerPath, string Version, string RuntimeIdentifier);

public static class InstallMarkerStore
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static bool TryRead(string markerPath, out string? version, out string? runtimeIdentifier)
    {
        version = null;
        runtimeIdentifier = null;
        try
        {
            var marker = JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(markerPath), ReadOptions);
            if (marker is not { Schema: 1 } ||
                !string.Equals(marker.AppId, ManagedInstallationDetector.ApplicationId, StringComparison.Ordinal) ||
                !ProductVersion.TryParse(marker.Version, out _))
            {
                return false;
            }

            var rid = string.IsNullOrWhiteSpace(marker.Rid) ? "win-x64" : marker.Rid;
            if (!UpdateRuntimeIdentifier.IsSupported(rid))
            {
                return false;
            }

            version = marker.Version;
            runtimeIdentifier = rid.ToLowerInvariant();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static void WriteAtomic(string markerPath, string version, string runtimeIdentifier)
    {
        if (!ProductVersion.TryParse(version, out _))
        {
            throw new ArgumentException("A versão do marcador é inválida.", nameof(version));
        }

        if (!UpdateRuntimeIdentifier.IsSupported(runtimeIdentifier))
        {
            throw new ArgumentException("A arquitetura do marcador é inválida.", nameof(runtimeIdentifier));
        }

        var fullPath = Path.GetFullPath(markerPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("O marcador precisa ter um diretório.", nameof(markerPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                new InstallMarker(1, ManagedInstallationDetector.ApplicationId, version, runtimeIdentifier.ToLowerInvariant()),
                WriteOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record InstallMarker(int Schema, string AppId, string Version, string? Rid);
}

public static class ManagedInstallationDetector
{
    public const string ApplicationId = "GuiaSys.GuiaPlay";

    public static ManagedInstallation? Detect(string executablePath)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (root is null) return null;
        var markerPath = Path.Combine(root, "install.json");
        if (!File.Exists(markerPath)) return null;

        return InstallMarkerStore.TryRead(markerPath, out var version, out var runtimeIdentifier)
            ? new ManagedInstallation(root, markerPath, version!, runtimeIdentifier!)
            : null;
    }
}

public enum UpdateWorkspaceKind
{
    Update,
    Updater,
    Backup
}

public sealed record UpdateWorkspaceCleanupResult(int DeletedDirectories, int PreservedDirectories);

public static class UpdateWorkspaceRetention
{
    public const string MarkerFileName = ".guiaplay-workspace.json";
    private const string WorkspaceApplicationId = "GuiaSys.GuiaPlay.UpdateWorkspace";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string CreateDirectory(
        string temporaryDirectory,
        UpdateWorkspaceKind kind,
        string name,
        DateTimeOffset? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name is "." or "..")
        {
            throw new ArgumentException("O nome da pasta temporária é inválido.", nameof(name));
        }

        var root = GetRoot(temporaryDirectory, kind);
        Directory.CreateDirectory(root);
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!IsDirectChild(root, path))
        {
            throw new ArgumentException("A pasta temporária precisa ficar dentro da raiz esperada.", nameof(name));
        }

        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException("A pasta temporária já existe.");
        }

        Directory.CreateDirectory(path);
        var marker = new WorkspaceMarker(
            1,
            WorkspaceApplicationId,
            kind.ToString(),
            (createdAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime());
        File.WriteAllText(
            Path.Combine(path, MarkerFileName),
            JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public static UpdateWorkspaceCleanupResult Cleanup(
        string temporaryDirectory,
        DateTimeOffset nowUtc,
        int backupRetention = 2,
        int transientRetention = 3,
        TimeSpan? transientMaximumAge = null)
    {
        if (backupRetention < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(backupRetention));
        }

        if (transientRetention < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(transientRetention));
        }

        var maximumAge = transientMaximumAge ?? TimeSpan.FromDays(2);
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(transientMaximumAge));
        }

        var deleted = 0;
        var preserved = 0;
        foreach (var kind in Enum.GetValues<UpdateWorkspaceKind>())
        {
            var root = GetRoot(temporaryDirectory, kind);
            if (!Directory.Exists(root))
            {
                continue;
            }

            var recognized = Directory.EnumerateDirectories(root)
                .Select(path => TryReadMarker(path, kind, out var createdAt)
                    ? new RecognizedWorkspace(path, createdAt)
                    : null)
                .Where(item => item is not null)
                .Cast<RecognizedWorkspace>()
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToArray();
            for (var index = 0; index < recognized.Length; index++)
            {
                var workspace = recognized[index];
                var shouldDelete = kind == UpdateWorkspaceKind.Backup
                    ? index >= backupRetention
                    : index >= transientRetention || nowUtc.ToUniversalTime() - workspace.CreatedAtUtc > maximumAge;
                if (shouldDelete && TryDeleteRecognizedDirectory(workspace.Path, kind))
                {
                    deleted++;
                }
                else
                {
                    preserved++;
                }
            }
        }

        return new UpdateWorkspaceCleanupResult(deleted, preserved);
    }

    public static bool IsRecognizedDirectory(string path, UpdateWorkspaceKind kind) =>
        TryReadMarker(path, kind, out _);

    public static bool TryDeleteRecognizedDirectory(string path, UpdateWorkspaceKind kind)
    {
        if (!TryReadMarker(path, kind, out _))
        {
            return false;
        }

        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryReadMarker(string path, UpdateWorkspaceKind expectedKind, out DateTimeOffset createdAtUtc)
    {
        createdAtUtc = default;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath) ||
                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(Path.GetFileName(Path.GetDirectoryName(fullPath)), RootName(expectedKind), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var marker = JsonSerializer.Deserialize<WorkspaceMarker>(
                File.ReadAllText(Path.Combine(fullPath, MarkerFileName)),
                JsonOptions);
            if (marker is not { Schema: 1 } ||
                !string.Equals(marker.AppId, WorkspaceApplicationId, StringComparison.Ordinal) ||
                !string.Equals(marker.Kind, expectedKind.ToString(), StringComparison.Ordinal) ||
                marker.CreatedAtUtc == default)
            {
                return false;
            }

            createdAtUtc = marker.CreatedAtUtc.ToUniversalTime();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string GetRoot(string temporaryDirectory, UpdateWorkspaceKind kind) =>
        Path.GetFullPath(Path.Combine(Path.GetFullPath(temporaryDirectory), RootName(kind)));

    private static string RootName(UpdateWorkspaceKind kind) => kind switch
    {
        UpdateWorkspaceKind.Update => "GuiaPlayUpdate",
        UpdateWorkspaceKind.Updater => "GuiaPlayUpdater",
        UpdateWorkspaceKind.Backup => "GuiaPlayBackup",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool IsDirectChild(string root, string path) =>
        string.Equals(Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private sealed record WorkspaceMarker(int Schema, string AppId, string Kind, DateTimeOffset CreatedAtUtc);
    private sealed record RecognizedWorkspace(string Path, DateTimeOffset CreatedAtUtc);
}

public sealed record UpdateApplyResult(bool Succeeded, bool RolledBack, string? Error);

public static class UpdateApplicator
{
    public static UpdateApplyResult Apply(string stagingDirectory, string installationDirectory, string backupDirectory) =>
        Apply(stagingDirectory, installationDirectory, backupDirectory, null, null);

    internal static UpdateApplyResult Apply(
        string stagingDirectory,
        string installationDirectory,
        string backupDirectory,
        Func<int, bool>? failBeforeCopy,
        Func<int, bool>? failDuringBackup = null)
    {
        var sourceRoot = NormalizeExistingDirectory(stagingDirectory);
        var targetRoot = NormalizeExistingDirectory(installationDirectory);
        var backupRoot = Path.GetFullPath(backupDirectory);
        if (IsWithinOrEqual(sourceRoot, targetRoot) || IsWithinOrEqual(targetRoot, sourceRoot))
        {
            throw new ArgumentException("O staging e a instalação precisam ser diretórios separados.");
        }

        var sourceMarkerPath = Path.Combine(sourceRoot, "install.json");
        if (!InstallMarkerStore.TryRead(sourceMarkerPath, out var targetVersion, out var targetRuntimeIdentifier))
        {
            throw new InvalidDataException("O pacote não contém um install.json válido.");
        }

        var installedMarkerPath = Path.Combine(targetRoot, "install.json");
        if (!InstallMarkerStore.TryRead(installedMarkerPath, out var installedVersion, out var installedRuntimeIdentifier))
        {
            throw new InvalidDataException("A instalação não contém um install.json válido.");
        }

        if (!string.Equals(targetRuntimeIdentifier, installedRuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("O pacote e a instalação possuem arquiteturas diferentes.");
        }

        if (ProductVersion.Parse(targetVersion!) <= ProductVersion.Parse(installedVersion!))
        {
            throw new InvalidDataException("O pacote não é mais novo do que a versão instalada.");
        }

        ValidateStagingPayload(sourceRoot, targetRuntimeIdentifier!);

        if (IsWithinOrEqual(sourceRoot, backupRoot) || IsWithinOrEqual(targetRoot, backupRoot))
        {
            throw new ArgumentException("O backup deve ficar fora do staging e da instalação.", nameof(backupDirectory));
        }

        if (Directory.Exists(backupRoot))
        {
            var existingEntries = Directory.EnumerateFileSystemEntries(backupRoot)
                .Where(path => !string.Equals(Path.GetFileName(path), UpdateWorkspaceRetention.MarkerFileName, StringComparison.Ordinal))
                .Take(1)
                .Any();
            if (!UpdateWorkspaceRetention.IsRecognizedDirectory(backupRoot, UpdateWorkspaceKind.Backup) || existingEntries)
            {
                throw new IOException("A pasta de backup já existe e não é um workspace vazio do GuiaPlay.");
            }
        }
        else
        {
            Directory.CreateDirectory(backupRoot);
        }

        var installationModified = false;
        try
        {
            CopyTree(targetRoot, backupRoot, skipMarker: true, failDuringBackup);
            installationModified = true;
            var copied = 0;
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceRoot, file);
                if (string.Equals(relative, "install.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                copied++;
                if (failBeforeCopy?.Invoke(copied) == true)
                {
                    throw new IOException("Falha simulada durante a aplicação.");
                }

                var destination = Path.Combine(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }

            RemoveObsoleteFiles(sourceRoot, targetRoot);
            InstallMarkerStore.WriteAtomic(
                installedMarkerPath,
                targetVersion!,
                targetRuntimeIdentifier!);
            return new UpdateApplyResult(true, false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!installationModified)
            {
                return new UpdateApplyResult(false, false, exception.Message);
            }

            try
            {
                CopyTree(backupRoot, targetRoot, skipMarker: false, null);
                RemoveObsoleteFiles(backupRoot, targetRoot);
                return new UpdateApplyResult(false, true, exception.Message);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                return new UpdateApplyResult(false, false, $"{exception.Message} Rollback também falhou: {rollbackException.Message}");
            }
        }
    }

    private static void ValidateStagingPayload(string sourceRoot, string runtimeIdentifier)
    {
        var requiredFiles = new[]
        {
            "GuiaPlay.exe",
            "GuiaPlay.Updater.exe",
            Path.Combine("libvlc", runtimeIdentifier, "libvlc.dll"),
            Path.Combine("libvlc", runtimeIdentifier, "libvlccore.dll")
        };
        foreach (var relativePath in requiredFiles)
        {
            if (!File.Exists(Path.Combine(sourceRoot, relativePath)))
            {
                throw new InvalidDataException($"O pacote não contém o arquivo obrigatório '{relativePath}'.");
            }
        }

        if (!Directory.Exists(Path.Combine(sourceRoot, "libvlc", runtimeIdentifier, "plugins")))
        {
            throw new InvalidDataException("O pacote não contém os plugins nativos obrigatórios do LibVLC.");
        }

        var oppositeRuntimeIdentifier = string.Equals(runtimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase)
            ? "win-x86"
            : "win-x64";
        if (Directory.Exists(Path.Combine(sourceRoot, "libvlc", oppositeRuntimeIdentifier)))
        {
            throw new InvalidDataException("O pacote contém binários nativos de outra arquitetura.");
        }
    }

    private static string NormalizeExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    private static bool IsWithinOrEqual(string parentRoot, string candidate)
    {
        var parent = parentRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(parent, fullCandidate, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyTree(string sourceRoot, string destinationRoot, bool skipMarker, Func<int, bool>? failBeforeCopy)
    {
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (string.Equals(relative, UpdateWorkspaceRetention.MarkerFileName, StringComparison.Ordinal)) continue;
            if (skipMarker && string.Equals(relative, "install.json", StringComparison.OrdinalIgnoreCase)) continue;
            copied++;
            if (failBeforeCopy?.Invoke(copied) == true)
            {
                throw new IOException("Falha simulada durante a cópia de segurança.");
            }

            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void RemoveObsoleteFiles(string sourceRoot, string targetRoot)
    {
        foreach (var targetFile in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(targetRoot, targetFile);
            if (string.Equals(relative, "install.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(Path.Combine(sourceRoot, relative))) File.Delete(targetFile);
        }
    }

}
