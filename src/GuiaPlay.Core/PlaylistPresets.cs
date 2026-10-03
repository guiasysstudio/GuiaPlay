using System.Text.Json;

namespace GuiaPlay.Core;

public static class PlaylistPresetName
{
    public const int MaximumLength = 80;

    public static bool IsValid(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaximumLength;

    public static string Normalize(string name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException("O nome da playlist deve ter entre 1 e 80 caracteres.", nameof(name));
        }

        return name.Trim();
    }
}

public sealed record PlaylistPreset(
    int Schema,
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    PlaylistDocument Playlist)
{
    public const int CurrentSchema = 1;
}

public sealed record PlaylistPresetListResult(
    IReadOnlyList<PlaylistPreset> Presets,
    IReadOnlyList<string> Warnings);

public sealed record PlaylistPresetLoadResult(PlaylistPreset? Preset, string? Error)
{
    public bool Succeeded => Preset is not null;
}

public enum PlaylistPresetSaveStatus
{
    Saved,
    NameConflict,
    InvalidName,
    Failed
}

public sealed record PlaylistPresetSaveResult(
    PlaylistPresetSaveStatus Status,
    PlaylistPreset? Preset,
    PlaylistPreset? ConflictingPreset,
    string? Error)
{
    public bool Succeeded => Status == PlaylistPresetSaveStatus.Saved;
}

public enum PlaylistPresetDeleteStatus
{
    Deleted,
    NotFound,
    Failed
}

public sealed record PlaylistPresetDeleteResult(PlaylistPresetDeleteStatus Status, string? Error)
{
    public bool Succeeded => Status == PlaylistPresetDeleteStatus.Deleted;
}

/// <summary>
/// Stores one metadata-only JSON file per preset. It never reads, copies, moves or deletes media.
/// </summary>
public sealed class PlaylistPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;

    public PlaylistPresetStore(string directoryPath, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("O diretório de presets não pode ficar vazio.", nameof(directoryPath));
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string DirectoryPath { get; }

    public static string DefaultDirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GuiaSys",
        "GuiaPlay",
        "playlist-presets");

    public PlaylistPresetListResult LoadAll()
    {
        lock (_gate)
        {
            return LoadAllCore();
        }
    }

    public PlaylistPresetLoadResult Load(Guid id)
    {
        if (id == Guid.Empty)
        {
            return new PlaylistPresetLoadResult(null, "O identificador do preset é inválido.");
        }

        lock (_gate)
        {
            var path = GetPresetPath(id);
            try
            {
                if (!File.Exists(path))
                {
                    return new PlaylistPresetLoadResult(null, "A playlist salva não foi encontrada.");
                }

                return ReadPreset(path, id);
            }
            catch (Exception exception) when (IsFileException(exception))
            {
                return new PlaylistPresetLoadResult(null, $"A playlist salva não pôde ser lida: {exception.Message}");
            }
        }
    }

    public PlaylistPresetSaveResult Save(
        string? name,
        PlaylistDocument playlist,
        bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (!PlaylistPresetName.IsValid(name))
        {
            return new PlaylistPresetSaveResult(
                PlaylistPresetSaveStatus.InvalidName,
                null,
                null,
                "O nome da playlist deve ter entre 1 e 80 caracteres.");
        }

        var normalizedName = PlaylistPresetName.Normalize(name!);
        lock (_gate)
        {
            var existing = LoadAllCore().Presets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !overwriteExisting)
            {
                return new PlaylistPresetSaveResult(
                    PlaylistPresetSaveStatus.NameConflict,
                    null,
                    existing,
                    "Já existe uma playlist com esse nome.");
            }

            try
            {
                var normalizedPlaylist = PlaylistDocumentCodec.Normalize(playlist);
                var now = _timeProvider.GetUtcNow();
                var preset = existing is null
                    ? new PlaylistPreset(
                        PlaylistPreset.CurrentSchema,
                        Guid.NewGuid(),
                        normalizedName,
                        now,
                        now,
                        normalizedPlaylist)
                    : existing with
                    {
                        Name = normalizedName,
                        UpdatedAt = now < existing.CreatedAt ? existing.CreatedAt : now,
                        Playlist = normalizedPlaylist
                    };
                WriteAtomically(preset);
                return new PlaylistPresetSaveResult(PlaylistPresetSaveStatus.Saved, preset, null, null);
            }
            catch (Exception exception) when (IsFileException(exception))
            {
                return new PlaylistPresetSaveResult(
                    PlaylistPresetSaveStatus.Failed,
                    null,
                    null,
                    exception.Message);
            }
        }
    }

    public PlaylistPresetDeleteResult Delete(Guid id)
    {
        if (id == Guid.Empty)
        {
            return new PlaylistPresetDeleteResult(
                PlaylistPresetDeleteStatus.NotFound,
                "A playlist salva não foi encontrada.");
        }

        lock (_gate)
        {
            var path = GetPresetPath(id);
            try
            {
                if (!File.Exists(path))
                {
                    return new PlaylistPresetDeleteResult(
                        PlaylistPresetDeleteStatus.NotFound,
                        "A playlist salva não foi encontrada.");
                }

                File.Delete(path);
                return new PlaylistPresetDeleteResult(PlaylistPresetDeleteStatus.Deleted, null);
            }
            catch (Exception exception) when (IsFileException(exception))
            {
                return new PlaylistPresetDeleteResult(PlaylistPresetDeleteStatus.Failed, exception.Message);
            }
        }
    }

    private PlaylistPresetListResult LoadAllCore()
    {
        var presets = new List<PlaylistPreset>();
        var warnings = new List<string>();
        if (!Directory.Exists(DirectoryPath))
        {
            return new PlaylistPresetListResult(presets, warnings);
        }

        string[] paths;
        try
        {
            paths = Directory.GetFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (IsFileException(exception))
        {
            warnings.Add($"Os presets de playlist não puderam ser enumerados: {exception.Message}");
            return new PlaylistPresetListResult(presets, warnings);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var expectedId) || expectedId == Guid.Empty)
            {
                warnings.Add($"O arquivo de preset ‘{fileName}’ foi ignorado porque o nome não é um GUID.");
                continue;
            }

            var result = ReadPreset(path, expectedId);
            if (result.Preset is not { } preset)
            {
                warnings.Add($"O preset ‘{fileName}’ foi ignorado: {result.Error}");
                continue;
            }

            if (!names.Add(preset.Name))
            {
                warnings.Add($"O preset ‘{fileName}’ foi ignorado porque repete o nome de outro preset.");
                continue;
            }

            presets.Add(preset);
            if (result.Error is not null)
            {
                warnings.Add($"O preset ‘{fileName}’ foi carregado parcialmente: {result.Error}");
            }
        }

        return new PlaylistPresetListResult(
            presets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings);
    }

    private static PlaylistPresetLoadResult ReadPreset(string path, Guid expectedId)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("a raiz não é um objeto JSON.");
            }

            if (!TryReadInt32(root, "schema", out var schema) || schema != PlaylistPreset.CurrentSchema)
            {
                throw new JsonException("o schema é ausente ou incompatível.");
            }

            if (!TryReadGuid(root, "id", out var id) || id == Guid.Empty || id != expectedId)
            {
                throw new JsonException("o identificador não corresponde ao nome do arquivo.");
            }

            if (!TryReadString(root, "name", out var name) || !PlaylistPresetName.IsValid(name))
            {
                throw new JsonException("o nome é inválido.");
            }

            if (!TryReadDateTimeOffset(root, "createdAt", out var createdAt) ||
                !TryReadDateTimeOffset(root, "updatedAt", out var updatedAt) ||
                updatedAt < createdAt)
            {
                throw new JsonException("as datas são inválidas.");
            }

            if (!TryGetProperty(root, "playlist", out var playlistElement))
            {
                throw new JsonException("a playlist está ausente.");
            }

            var parsedPlaylist = PlaylistDocumentCodec.Read(playlistElement);
            var warning = parsedPlaylist.Warnings.Count == 0
                ? null
                : string.Join(" ", parsedPlaylist.Warnings);
            return new PlaylistPresetLoadResult(
                new PlaylistPreset(
                    PlaylistPreset.CurrentSchema,
                    id,
                    PlaylistPresetName.Normalize(name),
                    createdAt,
                    updatedAt,
                    parsedPlaylist.Playlist),
                warning);
        }
        catch (Exception exception) when (IsFileException(exception))
        {
            return new PlaylistPresetLoadResult(null, exception.Message);
        }
    }

    private void WriteAtomically(PlaylistPreset preset)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = GetPresetPath(preset.Id);
        var temporaryPath = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(preset, JsonOptions);
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

            File.Move(temporaryPath, path, overwrite: true);
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
                // Temporary files are never treated as presets because they do not end in .json.
            }
        }
    }

    private string GetPresetPath(Guid id) => Path.Combine(DirectoryPath, $"{id:D}.json");

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadGuid(JsonElement element, string name, out Guid value)
    {
        value = default;
        return TryGetProperty(element, name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               property.TryGetGuid(out value);
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadInt32(JsonElement element, string name, out int value)
    {
        value = default;
        return TryGetProperty(element, name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryReadDateTimeOffset(JsonElement element, string name, out DateTimeOffset value)
    {
        value = default;
        return TryGetProperty(element, name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out value);
    }

    private static bool IsFileException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or PathTooLongException or JsonException;
}

public enum PlaylistReplacementMode
{
    RequireCleanWorkspace,
    DiscardUnsavedChanges
}

public enum PlaylistWorkspaceLoadStatus
{
    Loaded,
    UnsavedChanges
}

public sealed record PlaylistWorkspaceLoadResult(PlaylistWorkspaceLoadStatus Status)
{
    public bool Succeeded => Status == PlaylistWorkspaceLoadStatus.Loaded;
}

/// <summary>
/// Keeps the editable playlist and its preset baseline together. Loading only changes playlist data;
/// playback and application settings are deliberately outside this service.
/// </summary>
public sealed class PlaylistWorkspace
{
    public PlaylistWorkspace(PlaylistDocument initialPlaylist)
    {
        Catalog = new PlaylistCatalog(initialPlaylist);
    }

    public PlaylistCatalog Catalog { get; }
    public Guid? ActivePresetId { get; private set; }
    public string? ActivePresetName { get; private set; }
    public bool IsDirty => Catalog.IsDirty;

    public PlaylistWorkspaceLoadResult LoadPreset(
        PlaylistPreset preset,
        PlaylistReplacementMode replacementMode = PlaylistReplacementMode.RequireCleanWorkspace)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (IsDirty && replacementMode != PlaylistReplacementMode.DiscardUnsavedChanges)
        {
            return new PlaylistWorkspaceLoadResult(PlaylistWorkspaceLoadStatus.UnsavedChanges);
        }

        Catalog.Replace(preset.Playlist);
        ActivePresetId = preset.Id;
        ActivePresetName = preset.Name;
        return new PlaylistWorkspaceLoadResult(PlaylistWorkspaceLoadStatus.Loaded);
    }

    public PlaylistPresetSaveResult SavePreset(
        PlaylistPresetStore store,
        string? name,
        bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        var result = store.Save(name, Catalog.Snapshot, overwriteExisting);
        if (result.Preset is not { } preset)
        {
            return result;
        }

        AcceptSavedPreset(preset);
        return result;
    }

    public void AcceptSavedPreset(PlaylistPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ActivePresetId = preset.Id;
        ActivePresetName = preset.Name;
        Catalog.AcceptChanges();
    }

    public void DetachPreset(Guid presetId)
    {
        if (ActivePresetId != presetId)
        {
            return;
        }

        ActivePresetId = null;
        ActivePresetName = null;
    }
}
