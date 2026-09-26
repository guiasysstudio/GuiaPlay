using Microsoft.Win32;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class WindowsIntegrationTests
{
    private const string ExecutablePath = @"C:\Program Files\GuiaSys João\GuiaPlay.exe";

    [Fact]
    public void RegistersVideoAndAudioProgIdsOpenWithAndCapabilities()
    {
        var registry = new InMemoryRegistryStore();
        var service = new WindowsIntegrationService(registry, ExecutablePath);

        var state = service.Configure(registerAssociations: true, registerContextMenu: false);

        Assert.True(state.AssociationsRegistered);
        Assert.False(state.ContextMenuRegistered);
        Assert.True(registry.HasValue(@"Software\Classes\.mp4\OpenWithProgids", WindowsIntegrationService.VideoProgId));
        Assert.True(registry.HasValue(@"Software\Classes\.mp3\OpenWithProgids", WindowsIntegrationService.AudioProgId));
        Assert.True(registry.HasString(
            @"Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations",
            ".mkv",
            WindowsIntegrationService.VideoProgId));
        Assert.True(registry.HasString(
            @"Software\Clients\Media\GuiaPlay\Capabilities\FileAssociations",
            ".flac",
            WindowsIntegrationService.AudioProgId));
        Assert.True(registry.HasString(
            WindowsIntegrationService.RegisteredApplicationsKey,
            WindowsIntegrationService.RegisteredApplicationName,
            WindowsIntegrationService.CapabilitiesReference));
    }

    [Fact]
    public void RegistersOnlyExtensionsRecognizedByMediaClassifier()
    {
        var registry = new InMemoryRegistryStore();
        new WindowsIntegrationService(registry, ExecutablePath).Configure(true, false);

        Assert.Equal(23, MediaTypeDetector.SupportedExtensions.Count);
        Assert.All(MediaTypeDetector.SupportedVideoExtensions, extension =>
            Assert.Equal(MediaKind.Video, MediaTypeDetector.Detect("media" + extension)));
        Assert.All(MediaTypeDetector.SupportedAudioExtensions, extension =>
            Assert.Equal(MediaKind.Audio, MediaTypeDetector.Detect("media" + extension)));
        Assert.DoesNotContain(registry.Keys, key => key.Contains(".exe", StringComparison.OrdinalIgnoreCase) &&
            key.Contains("OpenWithProgids", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShellCommandQuotesExecutableAndUsesSafePlaceholder()
    {
        var command = WindowsIntegrationService.BuildOpenCommand(ExecutablePath);

        Assert.Equal("\"C:\\Program Files\\GuiaSys João\\GuiaPlay.exe\" \"%1\"", command);
        Assert.DoesNotContain("arquivo", command, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => WindowsIntegrationService.BuildOpenCommand("bad\"path.exe"));
    }

    [Fact]
    public void RegistersContextVerbForEverySupportedExtension()
    {
        var registry = new InMemoryRegistryStore();
        var service = new WindowsIntegrationService(registry, ExecutablePath);

        var state = service.Configure(registerAssociations: false, registerContextMenu: true);

        Assert.False(state.AssociationsRegistered);
        Assert.True(state.ContextMenuRegistered);
        foreach (var extension in MediaTypeDetector.SupportedExtensions)
        {
            var verb = $@"Software\Classes\SystemFileAssociations\{extension}\shell\GuiaPlay.Open";
            Assert.True(registry.HasString(verb, "MUIVerb", "Abrir com GuiaPlay"));
            Assert.True(registry.HasString($@"{verb}\command", null, WindowsIntegrationService.BuildOpenCommand(ExecutablePath)));
        }
    }

    [Fact]
    public void RegistrationAndRemovalAreIdempotentAndPreserveThirdPartyValues()
    {
        var registry = new InMemoryRegistryStore();
        registry.SetMarker(@"Software\Classes\.mp4\OpenWithProgids", "Other.Player");
        registry.SetString(@"Software\RegisteredApplications", "Other Player", @"Software\Other\Capabilities");
        var service = new WindowsIntegrationService(registry, ExecutablePath);

        service.Configure(true, true);
        var countAfterFirstRegistration = registry.ValueCount;
        service.Configure(true, true);
        Assert.Equal(countAfterFirstRegistration, registry.ValueCount);

        service.RemoveAll();
        service.RemoveAll();

        Assert.False(service.GetState().ExplorerIntegrationEnabled);
        Assert.True(registry.HasValue(@"Software\Classes\.mp4\OpenWithProgids", "Other.Player"));
        Assert.True(registry.HasString(
            @"Software\RegisteredApplications",
            "Other Player",
            @"Software\Other\Capabilities"));
        Assert.DoesNotContain(registry.Keys, key => key.Contains("UserChoice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ControlledRegistryRoundTripUsesOnlyTemporaryCurrentUserSubkey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sandbox = $@"Software\GuiaSys\GuiaPlay\Tests\{Guid.NewGuid():N}";
        try
        {
            var service = new WindowsIntegrationService(new WindowsRegistryStore(sandbox), ExecutablePath);
            Assert.True(service.Configure(true, true).ExplorerIntegrationEnabled);
            service.RemoveAll();
            Assert.False(service.GetState().ExplorerIntegrationEnabled);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(sandbox, throwOnMissingSubKey: false);
        }
    }

    private sealed class InMemoryRegistryStore : IRegistryStore
    {
        private readonly Dictionary<string, Dictionary<string, StoredValue>> _keys =
            new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Keys => _keys.Keys;
        public int ValueCount => _keys.Values.Sum(values => values.Count);

        public void SetString(string subKey, string? valueName, string value) =>
            Values(subKey)[valueName ?? string.Empty] = new StoredValue(value, IsMarker: false);

        public void SetMarker(string subKey, string valueName) =>
            Values(subKey)[valueName] = new StoredValue(null, IsMarker: true);

        public bool HasString(string subKey, string? valueName, string expectedValue) =>
            _keys.TryGetValue(subKey, out var values) &&
            values.TryGetValue(valueName ?? string.Empty, out var value) &&
            !value.IsMarker &&
            string.Equals(value.Data, expectedValue, StringComparison.Ordinal);

        public bool HasValue(string subKey, string valueName) =>
            _keys.TryGetValue(subKey, out var values) && values.ContainsKey(valueName);

        public void DeleteValue(string subKey, string valueName)
        {
            if (_keys.TryGetValue(subKey, out var values))
            {
                values.Remove(valueName);
            }
        }

        public void DeleteKeyTree(string subKey)
        {
            foreach (var key in _keys.Keys.Where(key =>
                         string.Equals(key, subKey, StringComparison.OrdinalIgnoreCase) ||
                         key.StartsWith(subKey + "\\", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _keys.Remove(key);
            }
        }

        public void DeleteKeyIfEmpty(string subKey)
        {
            if (_keys.TryGetValue(subKey, out var values) && values.Count == 0 &&
                !_keys.Keys.Any(key => key.StartsWith(subKey + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                _keys.Remove(subKey);
            }
        }

        private Dictionary<string, StoredValue> Values(string subKey)
        {
            if (!_keys.TryGetValue(subKey, out var values))
            {
                values = new Dictionary<string, StoredValue>(StringComparer.OrdinalIgnoreCase);
                _keys.Add(subKey, values);
            }

            return values;
        }

        private sealed record StoredValue(string? Data, bool IsMarker);
    }
}
