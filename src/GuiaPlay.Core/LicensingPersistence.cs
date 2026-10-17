using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace GuiaPlay.Core;

internal sealed record PersistedLicensingState(
    int SchemaVersion,
    string? LicenseKey,
    LicensingAccessKind AccessKind,
    string? EntitlementToken,
    LicensingServerView? ServerView,
    ProjectLicensingConfiguration? ProjectConfig,
    DateTimeOffset? LastOnlineValidationUtc,
    DateTimeOffset? LastObservedUtc,
    bool TrialConsumed = false)
{
    public const int CurrentSchemaVersion = 1;

    public static PersistedLicensingState Empty { get; } = new(
        CurrentSchemaVersion,
        null,
        LicensingAccessKind.None,
        null,
        null,
        null,
        null,
        null,
        false);
}

internal sealed class LicensingStateStore(string filePath)
{
    private readonly object _gate = new();
    private readonly string _filePath = filePath;

    public PersistedLicensingState Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                return PersistedLicensingState.Empty;
            }

            try
            {
                var encrypted = File.ReadAllBytes(_filePath);
                var json = WindowsDataProtection.Unprotect(encrypted);
                var state = JsonSerializer.Deserialize<PersistedLicensingState>(json);
                return state is { SchemaVersion: PersistedLicensingState.CurrentSchemaVersion }
                    ? state
                    : PersistedLicensingState.Empty;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                CryptographicException or
                Win32Exception)
            {
                return PersistedLicensingState.Empty;
            }
        }
    }

    public bool Save(PersistedLicensingState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_gate)
        {
            var temporaryPath = _filePath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                var directory = Path.GetDirectoryName(_filePath)
                    ?? throw new InvalidOperationException("O caminho do estado de licença não possui diretório.");
                Directory.CreateDirectory(directory);

                var json = JsonSerializer.SerializeToUtf8Bytes(state);
                var encrypted = WindowsDataProtection.Protect(json);
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(encrypted);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _filePath, overwrite: true);
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                CryptographicException or
                Win32Exception)
            {
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A sobra temporária nunca é utilizada como fonte do estado de licença.
                }
            }
        }
    }
}

internal sealed class WindowsDeviceIdentityStore(string fallbackFilePath)
{
    private const string RegistryPath = @"Software\GuiaSys\GuiaPlay\Licensing";
    private const string RegistryValueName = "DeviceId";
    private readonly object _gate = new();
    private readonly string _fallbackFilePath = fallbackFilePath;

    public string GetOrCreate()
    {
        lock (_gate)
        {
            var registryValue = TryReadRegistry();
            var fileValue = TryReadFile();
            var value = Normalize(registryValue) ?? Normalize(fileValue) ?? $"gpd_{Guid.NewGuid():N}";

            TryWriteRegistry(value);
            TryWriteFile(value);
            return value;
        }
    }

    private static string? TryReadRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            return key?.GetValue(RegistryValueName) as string;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
            System.Security.SecurityException or
            IOException)
        {
            return null;
        }
    }

    private string? TryReadFile()
    {
        try
        {
            return File.Exists(_fallbackFilePath) ? File.ReadAllText(_fallbackFilePath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryWriteRegistry(string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            key?.SetValue(RegistryValueName, value, RegistryValueKind.String);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
            System.Security.SecurityException or
            IOException)
        {
            // O arquivo local continua sendo a redundância da identidade.
        }
    }

    private void TryWriteFile(string value)
    {
        try
        {
            var directory = Path.GetDirectoryName(_fallbackFilePath);
            if (directory is null)
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = _fallbackFilePath + $".tmp-{Guid.NewGuid():N}";
            File.WriteAllText(temporaryPath, value);
            File.Move(temporaryPath, _fallbackFilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A identidade do Registry continua válida mesmo sem a cópia em arquivo.
        }
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length is < 8 or > 128)
        {
            return null;
        }

        return trimmed.Any(char.IsWhiteSpace) ? null : trimmed;
    }
}

internal static class WindowsDataProtection
{
    // The original implementation hand-marshaled Win32 DATA_BLOB values and
    // owned unmanaged buffers. Use the supported Windows DPAPI wrapper instead:
    // it performs the same per-user encryption with no optional entropy, so
    // existing licensing.dat blobs remain decryptable. Never persist plaintext.
    public static byte[] Protect(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureWindows();
        return ProtectedData.Protect(value, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    public static byte[] Unprotect(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureWindows();
        return ProtectedData.Unprotect(value, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("A proteção local de licença do GuiaPlay exige Windows.");
        }
    }
}
