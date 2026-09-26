using System.Text.Json;
using System.Text.Json.Nodes;

namespace GuiaPlay.Core;

public enum AudioOutputMode
{
    WindowsDefault,
    Explicit
}

public sealed record AudioOutputPreference(
    AudioOutputMode Mode,
    string? Module,
    string? DeviceId,
    string? DisplayName)
{
    public static AudioOutputPreference Default { get; } = new(AudioOutputMode.WindowsDefault, null, null, null);

    public bool IsExplicit => Mode == AudioOutputMode.Explicit;
}

public sealed record AppSettings(
    AppearancePreference Appearance,
    string? OperatorMonitorId,
    IReadOnlyDictionary<string, string> MonitorNames,
    IReadOnlySet<string> SelectedOutputIds,
    AudioOutputPreference AudioOutput,
    int Volume,
    bool Muted)
{
    public const int CurrentSchemaVersion = 3;

    public static AppSettings Default { get; } = new(
        AppearancePreference.System,
        null,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        AudioOutputPreference.Default,
        100,
        false);
}

public sealed record SettingsLoadResult(AppSettings Settings, bool RecoveredFromInvalidFile, string? Warning);

public sealed record SettingsSaveResult(bool Succeeded, string? Error)
{
    public static SettingsSaveResult Success { get; } = new(true, null);
}

/// <summary>
/// Versioned settings store. Unknown JSON properties are retained so newer files can safely
/// pass through an older build. Writes are replaced atomically on the same volume.
/// </summary>
public sealed class AppSettingsStore(string filePath, int invalidBackupRetention = 3)
{
    private readonly object _gate = new();
    private readonly string _filePath = filePath;
    private readonly int _invalidBackupRetention = Math.Max(1, invalidBackupRetention);
    private JsonObject _root = new();
    private bool _loaded;

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                _root = new JsonObject();
                _loaded = true;
                return new SettingsLoadResult(AppSettings.Default, false, null);
            }

            try
            {
                _root = JsonNode.Parse(File.ReadAllText(_filePath)) as JsonObject
                    ?? throw new JsonException("A raiz das configurações não é um objeto JSON.");
                _loaded = true;
                return new SettingsLoadResult(Parse(_root), false, null);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                var warning = $"As configurações não puderam ser lidas: {exception.Message}";
                var backedUp = TryBackupInvalidFile();
                _root = new JsonObject();
                _loaded = true;
                return new SettingsLoadResult(
                    AppSettings.Default,
                    backedUp,
                    backedUp ? warning + " Uma cópia foi preservada." : warning);
            }
        }
    }

    public SettingsSaveResult Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            if (!_loaded)
            {
                _ = Load();
            }

            var temporaryPath = _filePath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                UpdateRoot(settings);
                var directory = Path.GetDirectoryName(_filePath)
                    ?? throw new InvalidOperationException("O caminho das configurações não possui diretório.");
                Directory.CreateDirectory(directory);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(_root, new JsonSerializerOptions { WriteIndented = true });
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

                File.Move(temporaryPath, _filePath, overwrite: true);
                return SettingsSaveResult.Success;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return new SettingsSaveResult(false, exception.Message);
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
                    // A leftover temporary file is harmless and never used as a settings source.
                }
            }
        }
    }

    private static AppSettings Parse(JsonObject root)
    {
        var appearance = ParseEnum(root["appearance"], AppearancePreference.System);
        var screens = root["screens"] as JsonObject;
        var operatorId = ReadString(screens?["operatorId"]);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (screens?["names"] is JsonObject namesNode)
        {
            foreach (var pair in namesNode)
            {
                var value = NormalizeMonitorName(ReadString(pair.Value));
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(value))
                {
                    names[pair.Key] = value;
                }
            }
        }

        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (screens?["selectedOutputIds"] is JsonArray outputArray)
        {
            foreach (var node in outputArray)
            {
                var id = ReadString(node);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    outputs.Add(id);
                }
            }
        }

        var audio = root["audio"] as JsonObject;
        var mode = ParseEnum(audio?["mode"], AudioOutputMode.WindowsDefault);
        var module = ReadString(audio?["module"]);
        var deviceId = ReadString(audio?["deviceId"]);
        var displayName = ReadString(audio?["displayName"]);
        if (mode == AudioOutputMode.Explicit && (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(deviceId)))
        {
            mode = AudioOutputMode.WindowsDefault;
            module = null;
            deviceId = null;
        }

        var volume = ReadInt(audio?["volume"], 100);
        var muted = ReadBool(audio?["muted"], false);
        return new AppSettings(
            appearance,
            operatorId,
            names,
            outputs,
            new AudioOutputPreference(mode, module, deviceId, displayName),
            Math.Clamp(volume, 0, 100),
            muted);
    }

    private void UpdateRoot(AppSettings settings)
    {
        _root["schemaVersion"] = AppSettings.CurrentSchemaVersion;
        _root["appearance"] = settings.Appearance.ToString();

        var screens = _root["screens"] as JsonObject ?? new JsonObject();
        _root["screens"] = screens;
        screens["operatorId"] = settings.OperatorMonitorId;
        var outputIds = new JsonArray();
        foreach (var id in settings.SelectedOutputIds.Order(StringComparer.OrdinalIgnoreCase))
        {
            outputIds.Add(id);
        }

        screens["selectedOutputIds"] = outputIds;
        var names = new JsonObject();
        foreach (var pair in settings.MonitorNames.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                names[pair.Key] = NormalizeMonitorName(pair.Value);
            }
        }

        screens["names"] = names;

        var audio = _root["audio"] as JsonObject ?? new JsonObject();
        _root["audio"] = audio;
        audio["mode"] = settings.AudioOutput.Mode.ToString();
        audio["module"] = settings.AudioOutput.Module;
        audio["deviceId"] = settings.AudioOutput.DeviceId;
        audio["displayName"] = settings.AudioOutput.DisplayName;
        audio["volume"] = Math.Clamp(settings.Volume, 0, 100);
        audio["muted"] = settings.Muted;
    }

    private bool TryBackupInvalidFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (directory is null || !File.Exists(_filePath))
            {
                return false;
            }

            var stem = Path.GetFileNameWithoutExtension(_filePath);
            var backupPath = Path.Combine(directory, $"{stem}.invalid-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
            File.Copy(_filePath, backupPath, overwrite: false);

            foreach (var obsolete in Directory.GetFiles(directory, $"{stem}.invalid-*.json")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(_invalidBackupRetention))
            {
                File.Delete(obsolete);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;

    private static int ReadInt(JsonNode? node, int fallback) =>
        node is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;

    private static bool ReadBool(JsonNode? node, bool fallback) =>
        node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static string? NormalizeMonitorName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return trimmed.Length <= 40 ? trimmed : trimmed[..40];
    }

    private static T ParseEnum<T>(JsonNode? node, T fallback) where T : struct, Enum =>
        Enum.TryParse(ReadString(node), ignoreCase: true, out T parsed) ? parsed : fallback;
}
