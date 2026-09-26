using Microsoft.Win32;

namespace GuiaPlay.Core;

public interface IRegistryStore
{
    void SetString(string subKey, string? valueName, string value);
    void SetMarker(string subKey, string valueName);
    bool HasString(string subKey, string? valueName, string expectedValue);
    bool HasValue(string subKey, string valueName);
    void DeleteValue(string subKey, string valueName);
    void DeleteKeyTree(string subKey);
    void DeleteKeyIfEmpty(string subKey);
}

public sealed class WindowsRegistryStore(string? sandboxPrefix = null) : IRegistryStore
{
    private readonly string? _sandboxPrefix = NormalizeSandboxPrefix(sandboxPrefix);

    public void SetString(string subKey, string? valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Map(subKey), writable: true);
        key.SetValue(valueName ?? string.Empty, value, RegistryValueKind.String);
    }

    public void SetMarker(string subKey, string valueName)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Map(subKey), writable: true);
        key.SetValue(valueName, Array.Empty<byte>(), RegistryValueKind.None);
    }

    public bool HasString(string subKey, string? valueName, string expectedValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Map(subKey), writable: false);
        return key?.GetValue(valueName ?? string.Empty) is string actual &&
               string.Equals(actual, expectedValue, StringComparison.Ordinal);
    }

    public bool HasValue(string subKey, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Map(subKey), writable: false);
        return key?.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase) == true;
    }

    public void DeleteValue(string subKey, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Map(subKey), writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteKeyTree(string subKey) =>
        Registry.CurrentUser.DeleteSubKeyTree(Map(subKey), throwOnMissingSubKey: false);

    public void DeleteKeyIfEmpty(string subKey)
    {
        var mapped = Map(subKey);
        using var key = Registry.CurrentUser.OpenSubKey(mapped, writable: false);
        if (key is null || key.SubKeyCount != 0 || key.ValueCount != 0)
        {
            return;
        }

        key.Close();
        Registry.CurrentUser.DeleteSubKey(mapped, throwOnMissingSubKey: false);
    }

    private string Map(string subKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        return _sandboxPrefix is null ? subKey : $"{_sandboxPrefix}\\{subKey}";
    }

    private static string? NormalizeSandboxPrefix(string? value)
    {
        var normalized = value?.Trim().Trim('\\');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public sealed record WindowsIntegrationState(bool AssociationsRegistered, bool ContextMenuRegistered)
{
    public bool ExplorerIntegrationEnabled => AssociationsRegistered || ContextMenuRegistered;
}

public sealed class WindowsIntegrationService
{
    public const string RemoveCommandLineSwitch = "--remove-windows-integration";
    public const string VideoProgId = "GuiaPlay.Video";
    public const string AudioProgId = "GuiaPlay.Audio";
    public const string ContextVerbName = "GuiaPlay.Open";
    public const string RegisteredApplicationName = "GuiaPlay";
    public const string RegisteredApplicationsKey = @"Software\RegisteredApplications";
    public const string CapabilitiesKey = @"Software\Clients\Media\GuiaPlay\Capabilities";
    public const string CapabilitiesReference = @"Software\Clients\Media\GuiaPlay\Capabilities";

    private const string ClassesRoot = @"Software\Classes";
    private const string ApplicationKey = $@"{ClassesRoot}\Applications\GuiaPlay.exe";
    private const string AppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\GuiaPlay.exe";
    private const string ApplicationDescription = "Reprodutor de mídia para operação em múltiplas telas.";
    private readonly IRegistryStore _registry;
    private readonly string _executablePath;
    private readonly string _openCommand;
    private readonly string _iconReference;
    private readonly Action _associationsChanged;

    public WindowsIntegrationService(IRegistryStore registry, string executablePath, Action? associationsChanged = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("O caminho do executável contém aspas inválidas.", nameof(executablePath));
        }

        _executablePath = Path.GetFullPath(executablePath);
        _openCommand = BuildOpenCommand(_executablePath);
        _iconReference = $"\"{_executablePath}\",0";
        _associationsChanged = associationsChanged ?? (() => { });
    }

    public static string BuildOpenCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("O caminho do executável contém aspas inválidas.", nameof(executablePath));
        }

        return $"\"{Path.GetFullPath(executablePath)}\" \"%1\"";
    }

    public WindowsIntegrationState GetState() => new(
        AssociationsRegistered: AreAssociationsRegistered(),
        ContextMenuRegistered: IsContextMenuRegistered());

    public WindowsIntegrationState Configure(bool registerAssociations, bool registerContextMenu)
    {
        if (registerAssociations)
        {
            RegisterAssociations();
        }
        else
        {
            RemoveAssociations();
        }

        if (registerContextMenu)
        {
            RegisterContextMenu();
        }
        else
        {
            RemoveContextMenu();
        }

        _associationsChanged();
        return GetState();
    }

    public void RemoveAll()
    {
        RemoveContextMenu();
        RemoveAssociations();
        _associationsChanged();
    }

    private void RegisterAssociations()
    {
        _registry.SetString(ApplicationKey, "FriendlyAppName", "GuiaPlay");
        _registry.SetString(ApplicationKey, "ApplicationCompany", "GuiaSys Studio");
        _registry.SetString(ApplicationKey, "ApplicationDescription", ApplicationDescription);
        _registry.SetString($@"{ApplicationKey}\DefaultIcon", null, _iconReference);
        _registry.SetString($@"{ApplicationKey}\shell\open\command", null, _openCommand);
        foreach (var extension in MediaTypeDetector.SupportedExtensions)
        {
            _registry.SetMarker($@"{ApplicationKey}\SupportedTypes", extension);
        }

        RegisterProgId(VideoProgId, "Mídia de vídeo do GuiaPlay");
        RegisterProgId(AudioProgId, "Mídia de áudio do GuiaPlay");

        _registry.SetString(CapabilitiesKey, "ApplicationName", "GuiaPlay");
        _registry.SetString(CapabilitiesKey, "ApplicationDescription", ApplicationDescription);
        _registry.SetString(CapabilitiesKey, "ApplicationIcon", _iconReference);
        foreach (var extension in MediaTypeDetector.SupportedVideoExtensions)
        {
            RegisterAssociation(extension, VideoProgId);
        }

        foreach (var extension in MediaTypeDetector.SupportedAudioExtensions)
        {
            RegisterAssociation(extension, AudioProgId);
        }

        _registry.SetString(RegisteredApplicationsKey, RegisteredApplicationName, CapabilitiesReference);
        _registry.SetString(AppPathsKey, null, _executablePath);
        _registry.SetString(AppPathsKey, "Path", Path.GetDirectoryName(_executablePath)!);
    }

    private void RegisterProgId(string progId, string friendlyTypeName)
    {
        var key = $@"{ClassesRoot}\{progId}";
        _registry.SetString(key, null, friendlyTypeName);
        _registry.SetString(key, "FriendlyTypeName", friendlyTypeName);
        _registry.SetString($@"{key}\DefaultIcon", null, _iconReference);
        _registry.SetString($@"{key}\shell\open\command", null, _openCommand);
    }

    private void RegisterAssociation(string extension, string progId)
    {
        _registry.SetMarker(OpenWithKey(extension), progId);
        _registry.SetString($@"{CapabilitiesKey}\FileAssociations", extension, progId);
    }

    private void RegisterContextMenu()
    {
        foreach (var extension in MediaTypeDetector.SupportedExtensions)
        {
            var verb = ContextVerbKey(extension);
            _registry.SetString(verb, "MUIVerb", "Abrir com GuiaPlay");
            _registry.SetString(verb, "Icon", _iconReference);
            _registry.SetString($@"{verb}\command", null, _openCommand);
        }
    }

    private bool AreAssociationsRegistered()
    {
        if (!_registry.HasString(ApplicationKey, "FriendlyAppName", "GuiaPlay") ||
            !_registry.HasString($@"{ApplicationKey}\shell\open\command", null, _openCommand) ||
            !_registry.HasString($@"{ClassesRoot}\{VideoProgId}\shell\open\command", null, _openCommand) ||
            !_registry.HasString($@"{ClassesRoot}\{AudioProgId}\shell\open\command", null, _openCommand) ||
            !_registry.HasString(RegisteredApplicationsKey, RegisteredApplicationName, CapabilitiesReference))
        {
            return false;
        }

        return MediaTypeDetector.SupportedVideoExtensions.All(extension =>
                   _registry.HasValue(OpenWithKey(extension), VideoProgId) &&
                   _registry.HasString($@"{CapabilitiesKey}\FileAssociations", extension, VideoProgId)) &&
               MediaTypeDetector.SupportedAudioExtensions.All(extension =>
                   _registry.HasValue(OpenWithKey(extension), AudioProgId) &&
                   _registry.HasString($@"{CapabilitiesKey}\FileAssociations", extension, AudioProgId));
    }

    private bool IsContextMenuRegistered() => MediaTypeDetector.SupportedExtensions.All(extension =>
    {
        var verb = ContextVerbKey(extension);
        return _registry.HasString(verb, "MUIVerb", "Abrir com GuiaPlay") &&
               _registry.HasString($@"{verb}\command", null, _openCommand);
    });

    private void RemoveAssociations()
    {
        foreach (var extension in MediaTypeDetector.SupportedExtensions)
        {
            var openWith = OpenWithKey(extension);
            _registry.DeleteValue(openWith, ProgIdFor(extension));
            _registry.DeleteKeyIfEmpty(openWith);
        }

        _registry.DeleteValue(RegisteredApplicationsKey, RegisteredApplicationName);
        _registry.DeleteKeyTree(CapabilitiesKey[..CapabilitiesKey.LastIndexOf('\\')]);
        _registry.DeleteKeyTree($@"{ClassesRoot}\{VideoProgId}");
        _registry.DeleteKeyTree($@"{ClassesRoot}\{AudioProgId}");
        _registry.DeleteKeyTree(ApplicationKey);
        _registry.DeleteKeyTree(AppPathsKey);
    }

    private void RemoveContextMenu()
    {
        foreach (var extension in MediaTypeDetector.SupportedExtensions)
        {
            var verb = ContextVerbKey(extension);
            _registry.DeleteKeyTree(verb);
            _registry.DeleteKeyIfEmpty($@"{ClassesRoot}\SystemFileAssociations\{extension}\shell");
            _registry.DeleteKeyIfEmpty($@"{ClassesRoot}\SystemFileAssociations\{extension}");
        }
    }

    private static string OpenWithKey(string extension) => $@"{ClassesRoot}\{extension}\OpenWithProgids";
    private static string ContextVerbKey(string extension) =>
        $@"{ClassesRoot}\SystemFileAssociations\{extension}\shell\{ContextVerbName}";
    private static string ProgIdFor(string extension) =>
        MediaTypeDetector.SupportedVideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? VideoProgId
            : AudioProgId;
}
