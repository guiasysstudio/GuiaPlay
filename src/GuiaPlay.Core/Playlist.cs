using System.Text.Json;

namespace GuiaPlay.Core;

public sealed record PlaylistItem(
    Guid Id,
    string DisplayName,
    string OriginalPath,
    MediaKind Kind,
    int Order);

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

public sealed record PlaylistProbedImportResult(
    PlaylistGroup? TargetGroup,
    IReadOnlyList<PlaylistItem> Added,
    IReadOnlyList<string> Rejected);

public enum PlaylistItemAvailability
{
    Unknown,
    Available,
    Missing,
    DecodeFailed,
    TimedOut,
    InvalidPath,
    Unsupported
}

public static class PlaylistItemAvailabilityPolicy
{
    public static PlaylistItemAvailability FromProbe(MediaProbeStatus status) => status switch
    {
        MediaProbeStatus.Available => PlaylistItemAvailability.Available,
        MediaProbeStatus.Missing => PlaylistItemAvailability.Missing,
        MediaProbeStatus.Unsupported => PlaylistItemAvailability.Unsupported,
        MediaProbeStatus.TimedOut => PlaylistItemAvailability.TimedOut,
        MediaProbeStatus.InvalidPath => PlaylistItemAvailability.InvalidPath,
        _ => PlaylistItemAvailability.Unknown
    };

    public static bool CanActivate(PlaylistItemAvailability availability) =>
        availability == PlaylistItemAvailability.Available;
}

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
                var parsed = PlaylistDocumentCodec.Read(File.ReadAllText(_filePath));
                var warning = parsed.Warnings.Count == 0
                    ? null
                    : string.Join(" ", parsed.Warnings);
                return new PlaylistLoadResult(parsed.Playlist, warning);
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
                var normalized = PlaylistDocumentCodec.Normalize(document);
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

}

public sealed class PlaylistCatalog(PlaylistDocument document)
{
    private readonly List<PlaylistGroup> _groups = PlaylistDocumentCodec.Normalize(document).Groups.ToList();

    public bool IsDirty { get; private set; }

    public event EventHandler? Changed;

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
        MarkChanged();
        return group;
    }

    public bool RenameGroup(Guid groupId, string name)
    {
        var index = _groups.FindIndex(group => group.Id == groupId);
        if (index < 0)
        {
            return false;
        }

        var normalized = NormalizeName(name);
        if (string.Equals(_groups[index].Name, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        _groups[index] = _groups[index] with { Name = normalized };
        MarkChanged();
        return true;
    }

    public bool RemoveGroup(Guid groupId)
    {
        if (_groups.RemoveAll(group => group.Id == groupId) == 0)
        {
            return false;
        }

        MarkChanged();
        return true;
    }

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
        MarkChanged();
        return item;
    }

    public PlaylistImportResult AddItems(Guid groupId, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var added = new List<PlaylistItem>();
        var rejected = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || MediaTypeDetector.Detect(path) == MediaKind.Unknown)
            {
                rejected.Add(path ?? string.Empty);
                continue;
            }

            added.Add(AddItem(groupId, path));
        }

        return new PlaylistImportResult(added, rejected);
    }

    /// <summary>
    /// Adds only paths already accepted by the bounded asynchronous probe service. This method
    /// intentionally performs no synchronous filesystem access and is safe to call on the UI thread.
    /// </summary>
    public PlaylistImportResult AddProbedItems(Guid groupId, IEnumerable<MediaProbeResult> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        var added = new List<PlaylistItem>();
        var rejected = new List<string>();
        foreach (var probe in probes)
        {
            if (probe.Status != MediaProbeStatus.Available ||
                string.IsNullOrWhiteSpace(probe.Path) ||
                MediaTypeDetector.Detect(probe.Path) == MediaKind.Unknown)
            {
                rejected.Add(probe.Path ?? string.Empty);
                continue;
            }

            added.Add(AddItem(groupId, probe.Path));
        }

        return new PlaylistImportResult(added, rejected);
    }

    /// <summary>
    /// Imports a probed batch atomically at catalog level. A fallback group is created only when
    /// at least one path is valid and available, preventing empty "ghost" groups after a rejected drop.
    /// </summary>
    public PlaylistProbedImportResult ImportProbedItems(
        Guid? targetGroupId,
        string fallbackGroupName,
        IEnumerable<MediaProbeResult> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        var accepted = new List<(string FullPath, MediaKind Kind)>();
        var rejected = new List<string>();
        foreach (var probe in probes)
        {
            var kind = MediaTypeDetector.Detect(probe.Path);
            if (probe.Status != MediaProbeStatus.Available ||
                string.IsNullOrWhiteSpace(probe.Path) ||
                kind == MediaKind.Unknown)
            {
                rejected.Add(probe.Path);
                continue;
            }

            try
            {
                accepted.Add((Path.GetFullPath(probe.Path), kind));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                rejected.Add(probe.Path);
            }
        }

        if (accepted.Count == 0)
        {
            return new PlaylistProbedImportResult(null, [], rejected);
        }

        var groupIndex = targetGroupId is { } id
            ? _groups.FindIndex(group => group.Id == id)
            : -1;
        PlaylistGroup group;
        if (groupIndex < 0)
        {
            group = new PlaylistGroup(Guid.NewGuid(), NormalizeName(fallbackGroupName), _groups.Count, []);
            groupIndex = _groups.Count;
            _groups.Add(group);
        }
        else
        {
            group = _groups[groupIndex];
        }

        var items = group.Items.ToList();
        var added = accepted.Select(entry =>
        {
            var item = new PlaylistItem(
                Guid.NewGuid(),
                Path.GetFileName(entry.FullPath),
                entry.FullPath,
                entry.Kind,
                items.Count);
            items.Add(item);
            return item;
        }).ToArray();
        group = group with { Items = items };
        _groups[groupIndex] = group;
        MarkChanged();
        return new PlaylistProbedImportResult(group, added, rejected);
    }

    public bool MoveGroup(Guid groupId, int targetIndex)
    {
        var sourceIndex = _groups.FindIndex(group => group.Id == groupId);
        if (sourceIndex < 0 || _groups.Count == 0)
        {
            return false;
        }

        var group = _groups[sourceIndex];
        var normalizedTarget = Math.Clamp(targetIndex, 0, _groups.Count - 1);
        if (sourceIndex == normalizedTarget)
        {
            return false;
        }

        _groups.RemoveAt(sourceIndex);
        _groups.Insert(Math.Clamp(targetIndex, 0, _groups.Count), group);
        MarkChanged();
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
        var normalizedTarget = sourceGroupId == targetGroupId
            ? Math.Clamp(targetIndex, 0, sourceItems.Count - 1)
            : targetIndex;
        if (sourceGroupId == targetGroupId && sourceItemIndex == normalizedTarget)
        {
            return false;
        }

        sourceItems.RemoveAt(sourceItemIndex);
        if (sourceGroupId == targetGroupId)
        {
            sourceItems.Insert(Math.Clamp(targetIndex, 0, sourceItems.Count), item);
            _groups[sourceGroupIndex] = _groups[sourceGroupIndex] with { Items = sourceItems };
            MarkChanged();
            return true;
        }

        var targetItems = _groups[targetGroupIndex].Items.ToList();
        targetItems.Insert(Math.Clamp(targetIndex, 0, targetItems.Count), item);
        _groups[sourceGroupIndex] = _groups[sourceGroupIndex] with { Items = sourceItems };
        _groups[targetGroupIndex] = _groups[targetGroupIndex] with { Items = targetItems };
        MarkChanged();
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
        MarkChanged();
        return true;
    }

    public void Replace(PlaylistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _groups.Clear();
        _groups.AddRange(PlaylistDocumentCodec.Normalize(document).Groups);
        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AcceptChanges() => IsDirty = false;

    private void MarkChanged()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
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
