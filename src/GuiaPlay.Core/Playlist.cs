using System.Text.Json;

namespace GuiaPlay.Core;

public sealed record PlaylistItem(
    Guid Id,
    string DisplayName,
    string OriginalPath,
    MediaKind Kind,
    int Order)
{
    public bool IsAvailable => File.Exists(OriginalPath);
}

public sealed record PlaylistGroup(
    Guid Id,
    string Name,
    int Order,
    IReadOnlyList<PlaylistItem> Items);

public sealed record PlaylistDocument(int SchemaVersion, IReadOnlyList<PlaylistGroup> Groups)
{
    public const int CurrentSchemaVersion = 1;
    public static PlaylistDocument Empty { get; } = new(CurrentSchemaVersion, []);
}

public sealed record PlaylistLoadResult(PlaylistDocument Playlist, string? Warning);

public sealed record PlaylistSaveResult(bool Succeeded, string? Error)
{
    public static PlaylistSaveResult Success { get; } = new(true, null);
}

public sealed record PlaylistImportResult(
    IReadOnlyList<PlaylistItem> Added,
    IReadOnlyList<string> Rejected);

public static class PlaylistGroupName
{
    public static bool IsValid(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 80;
}

/// <summary>
/// Stores only lightweight playlist metadata. Media bytes are never read, copied or serialized.
/// </summary>
public sealed class PlaylistStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _filePath = filePath;

    public PlaylistLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                return new PlaylistLoadResult(PlaylistDocument.Empty, null);
            }

            try
            {
                var document = JsonSerializer.Deserialize<PlaylistDocument>(File.ReadAllText(_filePath), JsonOptions)
                    ?? PlaylistDocument.Empty;
                return new PlaylistLoadResult(Normalize(document), null);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return new PlaylistLoadResult(PlaylistDocument.Empty, $"A playlist não pôde ser lida: {exception.Message}");
            }
        }
    }

    public PlaylistSaveResult Save(PlaylistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            var temporaryPath = _filePath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                var directory = Path.GetDirectoryName(_filePath)
                    ?? throw new InvalidOperationException("O caminho da playlist não possui diretório.");
                Directory.CreateDirectory(directory);
                var normalized = Normalize(document);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions);
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
                return PlaylistSaveResult.Success;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                return new PlaylistSaveResult(false, exception.Message);
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
                    // A temporary file is never used as a playlist source.
                }
            }
        }
    }

    private static PlaylistDocument Normalize(PlaylistDocument document)
    {
        var groups = (document.Groups ?? [])
            .Where(group => group.Id != Guid.Empty && !string.IsNullOrWhiteSpace(group.Name))
            .OrderBy(group => group.Order)
            .Select((group, groupOrder) => new PlaylistGroup(
                group.Id,
                group.Name.Trim(),
                groupOrder,
                (group.Items ?? [])
                    .Where(item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.OriginalPath))
                    .OrderBy(item => item.Order)
                    .Select((item, itemOrder) => item with
                    {
                        DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                            ? Path.GetFileName(item.OriginalPath)
                            : item.DisplayName.Trim(),
                        OriginalPath = Path.GetFullPath(item.OriginalPath),
                        Kind = item.Kind == MediaKind.Unknown ? MediaTypeDetector.Detect(item.OriginalPath) : item.Kind,
                        Order = itemOrder
                    })
                    .ToArray()))
            .ToArray();
        return new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion, groups);
    }
}

public sealed class PlaylistCatalog(PlaylistDocument document)
{
    private readonly List<PlaylistGroup> _groups = document.Groups.OrderBy(group => group.Order).ToList();

    public PlaylistDocument Snapshot => new(
        PlaylistDocument.CurrentSchemaVersion,
        _groups.Select((group, groupOrder) => group with
        {
            Order = groupOrder,
            Items = group.Items.Select((item, itemOrder) => item with { Order = itemOrder }).ToArray()
        }).ToArray());

    public PlaylistGroup CreateGroup(string name)
    {
        var normalized = NormalizeName(name);
        var group = new PlaylistGroup(Guid.NewGuid(), normalized, _groups.Count, []);
        _groups.Add(group);
        return group;
    }

    public bool RenameGroup(Guid groupId, string name)
    {
        var index = _groups.FindIndex(group => group.Id == groupId);
        if (index < 0)
        {
            return false;
        }

        _groups[index] = _groups[index] with { Name = NormalizeName(name) };
        return true;
    }

    public bool RemoveGroup(Guid groupId) => _groups.RemoveAll(group => group.Id == groupId) > 0;

    public PlaylistItem AddItem(Guid groupId, string path)
    {
        var index = _groups.FindIndex(group => group.Id == groupId);
        if (index < 0)
        {
            throw new ArgumentException("Grupo não encontrado.", nameof(groupId));
        }

        var fullPath = Path.GetFullPath(path);
        var kind = MediaTypeDetector.Detect(fullPath);
        if (kind == MediaKind.Unknown)
        {
            throw new ArgumentException("Formato de mídia não reconhecido.", nameof(path));
        }

        var items = _groups[index].Items.ToList();
        var item = new PlaylistItem(Guid.NewGuid(), Path.GetFileName(fullPath), fullPath, kind, items.Count);
        items.Add(item);
        _groups[index] = _groups[index] with { Items = items };
        return item;
    }

    public PlaylistImportResult AddItems(Guid groupId, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var added = new List<PlaylistItem>();
        var rejected = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || MediaTypeDetector.Detect(path) == MediaKind.Unknown)
            {
                rejected.Add(path ?? string.Empty);
                continue;
            }

            added.Add(AddItem(groupId, path));
        }

        return new PlaylistImportResult(added, rejected);
    }

    public bool MoveGroup(Guid groupId, int targetIndex)
    {
        var sourceIndex = _groups.FindIndex(group => group.Id == groupId);
        if (sourceIndex < 0 || _groups.Count == 0)
        {
            return false;
        }

        var group = _groups[sourceIndex];
        _groups.RemoveAt(sourceIndex);
        _groups.Insert(Math.Clamp(targetIndex, 0, _groups.Count), group);
        return true;
    }

    public bool MoveItem(Guid sourceGroupId, Guid itemId, Guid targetGroupId, int targetIndex)
    {
        var sourceGroupIndex = _groups.FindIndex(group => group.Id == sourceGroupId);
        var targetGroupIndex = _groups.FindIndex(group => group.Id == targetGroupId);
        if (sourceGroupIndex < 0 || targetGroupIndex < 0)
        {
            return false;
        }

        var sourceItems = _groups[sourceGroupIndex].Items.ToList();
        var sourceItemIndex = sourceItems.FindIndex(item => item.Id == itemId);
        if (sourceItemIndex < 0)
        {
            return false;
        }

        var item = sourceItems[sourceItemIndex];
        sourceItems.RemoveAt(sourceItemIndex);
        if (sourceGroupId == targetGroupId)
        {
            sourceItems.Insert(Math.Clamp(targetIndex, 0, sourceItems.Count), item);
            _groups[sourceGroupIndex] = _groups[sourceGroupIndex] with { Items = sourceItems };
            return true;
        }

        var targetItems = _groups[targetGroupIndex].Items.ToList();
        targetItems.Insert(Math.Clamp(targetIndex, 0, targetItems.Count), item);
        _groups[sourceGroupIndex] = _groups[sourceGroupIndex] with { Items = sourceItems };
        _groups[targetGroupIndex] = _groups[targetGroupIndex] with { Items = targetItems };
        return true;
    }

    public bool RemoveItem(Guid groupId, Guid itemId)
    {
        var index = _groups.FindIndex(group => group.Id == groupId);
        if (index < 0)
        {
            return false;
        }

        var items = _groups[index].Items.Where(item => item.Id != itemId).ToArray();
        if (items.Length == _groups[index].Items.Count)
        {
            return false;
        }

        _groups[index] = _groups[index] with { Items = items };
        return true;
    }

    private static string NormalizeName(string name)
    {
        if (!PlaylistGroupName.IsValid(name))
        {
            throw new ArgumentException("O nome do grupo deve ter entre 1 e 80 caracteres.", nameof(name));
        }

        var trimmed = name.Trim();
        return trimmed;
    }
}
