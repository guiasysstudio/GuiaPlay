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

public static class SafeZipExtractor
{
    public static void Extract(string archivePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);
        using var archive = ZipFile.OpenRead(archivePath);
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
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}

public sealed record ManagedInstallation(string RootDirectory, string MarkerPath, string Version);

public static class ManagedInstallationDetector
{
    public const string ApplicationId = "GuiaSys.GuiaPlay";

    public static ManagedInstallation? Detect(string executablePath)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (root is null) return null;
        var markerPath = Path.Combine(root, "install.json");
        if (!File.Exists(markerPath)) return null;

        try
        {
            var marker = JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(markerPath), JsonOptions);
            return marker is { Schema: 1 } && string.Equals(marker.AppId, ApplicationId, StringComparison.Ordinal) &&
                   ProductVersion.TryParse(marker.Version, out _)
                ? new ManagedInstallation(root, markerPath, marker.Version)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record InstallMarker(int Schema, string AppId, string Version);
}

public sealed record UpdateApplyResult(bool Succeeded, bool RolledBack, string? Error);

public static class UpdateApplicator
{
    public static UpdateApplyResult Apply(string stagingDirectory, string installationDirectory, string backupDirectory) =>
        Apply(stagingDirectory, installationDirectory, backupDirectory, null);

    internal static UpdateApplyResult Apply(
        string stagingDirectory,
        string installationDirectory,
        string backupDirectory,
        Func<int, bool>? failBeforeCopy)
    {
        var sourceRoot = NormalizeExistingDirectory(stagingDirectory);
        var targetRoot = NormalizeExistingDirectory(installationDirectory);
        var backupRoot = Path.GetFullPath(backupDirectory);
        if (backupRoot.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
            backupRoot.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("O backup deve ficar fora do staging e da instalação.", nameof(backupDirectory));
        }

        if (Directory.Exists(backupRoot))
        {
            throw new IOException("A pasta de backup já existe.");
        }

        Directory.CreateDirectory(backupRoot);
        try
        {
            CopyTree(targetRoot, backupRoot, skipMarker: true, null);
            var copied = 0;
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                copied++;
                if (failBeforeCopy?.Invoke(copied) == true)
                {
                    throw new IOException("Falha simulada durante a aplicação.");
                }

                var relative = Path.GetRelativePath(sourceRoot, file);
                var destination = Path.Combine(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }

            RemoveObsoleteFiles(sourceRoot, targetRoot);
            return new UpdateApplyResult(true, false, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                ClearInstallFiles(targetRoot);
                CopyTree(backupRoot, targetRoot, skipMarker: false, null);
                return new UpdateApplyResult(false, true, exception.Message);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                return new UpdateApplyResult(false, false, $"{exception.Message} Rollback também falhou: {rollbackException.Message}");
            }
        }
    }

    private static string NormalizeExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        return fullPath;
    }

    private static void CopyTree(string sourceRoot, string destinationRoot, bool skipMarker, Func<int, bool>? unused)
    {
        _ = unused;
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (skipMarker && string.Equals(relative, "install.json", StringComparison.OrdinalIgnoreCase)) continue;
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

    private static void ClearInstallFiles(string targetRoot)
    {
        foreach (var file in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetRelativePath(targetRoot, file), "install.json", StringComparison.OrdinalIgnoreCase)) File.Delete(file);
        }
    }
}
