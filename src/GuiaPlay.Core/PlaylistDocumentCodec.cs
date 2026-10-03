using System.Text.Json;

namespace GuiaPlay.Core;

internal sealed record PlaylistDocumentReadResult(
    PlaylistDocument Playlist,
    IReadOnlyList<string> Warnings);

internal static class PlaylistDocumentCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static PlaylistDocument Normalize(PlaylistDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var groups = (document.Groups ?? [])
            .Where(group => group.Id != Guid.Empty && PlaylistGroupName.IsValid(group.Name))
            .OrderBy(group => group.Order)
            .Select((group, groupOrder) => new PlaylistGroup(
                group.Id,
                group.Name.Trim(),
                groupOrder,
                (group.Items ?? [])
                    .Where(item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.OriginalPath))
                    .OrderBy(item => item.Order)
                    .Select((item, itemOrder) =>
                    {
                        var fullPath = Path.GetFullPath(item.OriginalPath);
                        var kind = item.Kind == MediaKind.Unknown
                            ? MediaTypeDetector.Detect(fullPath)
                            : item.Kind;
                        return item with
                        {
                            DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                                ? Path.GetFileName(fullPath)
                                : item.DisplayName.Trim(),
                            OriginalPath = fullPath,
                            Kind = kind,
                            Order = itemOrder
                        };
                    })
                    .ToArray()))
            .ToArray();
        return new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion, groups);
    }

    public static PlaylistDocumentReadResult Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    public static PlaylistDocumentReadResult Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A raiz da playlist não é um objeto JSON.");
        }

        var warnings = new List<string>();
        if (!TryGetProperty(root, "groups", out var groupsElement) || groupsElement.ValueKind == JsonValueKind.Null)
        {
            return new PlaylistDocumentReadResult(PlaylistDocument.Empty, warnings);
        }

        if (groupsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A propriedade groups da playlist não é uma lista.");
        }

        var groups = new List<PlaylistGroup>();
        var groupIds = new HashSet<Guid>();
        var groupIndex = 0;
        foreach (var groupElement in groupsElement.EnumerateArray())
        {
            if (!TryReadGroup(groupElement, groupIndex, out var group, out var groupWarnings))
            {
                warnings.Add($"O grupo {groupIndex + 1} foi ignorado: {groupWarnings[0]}");
                groupIndex++;
                continue;
            }

            if (!groupIds.Add(group.Id))
            {
                warnings.Add($"O grupo {groupIndex + 1} foi ignorado porque repete um identificador.");
                groupIndex++;
                continue;
            }

            warnings.AddRange(groupWarnings);
            groups.Add(group);
            groupIndex++;
        }

        var normalized = groups
            .OrderBy(group => group.Order)
            .Select((group, order) => group with
            {
                Order = order,
                Items = group.Items
                    .OrderBy(item => item.Order)
                    .Select((item, itemOrder) => item with { Order = itemOrder })
                    .ToArray()
            })
            .ToArray();
        return new PlaylistDocumentReadResult(
            new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion, normalized),
            warnings);
    }

    private static bool TryReadGroup(
        JsonElement element,
        int groupIndex,
        out PlaylistGroup group,
        out List<string> warnings)
    {
        group = null!;
        warnings = [];
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("o valor não é um objeto.");
            return false;
        }

        if (!TryReadGuid(element, "id", out var id) || id == Guid.Empty)
        {
            warnings.Add("o identificador é inválido.");
            return false;
        }

        if (!TryReadString(element, "name", out var name) || !PlaylistGroupName.IsValid(name))
        {
            warnings.Add("o nome é inválido.");
            return false;
        }

        var order = TryReadInt32(element, "order", out var savedOrder) ? savedOrder : groupIndex;
        var items = new List<PlaylistItem>();
        var itemIds = new HashSet<Guid>();
        if (TryGetProperty(element, "items", out var itemsElement) && itemsElement.ValueKind != JsonValueKind.Null)
        {
            if (itemsElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add($"Os itens do grupo ‘{name.Trim()}’ foram ignorados porque não formam uma lista.");
            }
            else
            {
                var itemIndex = 0;
                foreach (var itemElement in itemsElement.EnumerateArray())
                {
                    if (!TryReadItem(itemElement, itemIndex, out var item, out var reason))
                    {
                        warnings.Add($"O item {itemIndex + 1} do grupo ‘{name.Trim()}’ foi ignorado: {reason}");
                        itemIndex++;
                        continue;
                    }

                    if (!itemIds.Add(item.Id))
                    {
                        warnings.Add($"O item {itemIndex + 1} do grupo ‘{name.Trim()}’ foi ignorado porque repete um identificador.");
                        itemIndex++;
                        continue;
                    }

                    items.Add(item);
                    itemIndex++;
                }
            }
        }

        group = new PlaylistGroup(id, name.Trim(), order, items);
        return true;
    }

    private static bool TryReadItem(
        JsonElement element,
        int itemIndex,
        out PlaylistItem item,
        out string reason)
    {
        item = null!;
        reason = string.Empty;
        if (element.ValueKind != JsonValueKind.Object)
        {
            reason = "o valor não é um objeto.";
            return false;
        }

        if (!TryReadGuid(element, "id", out var id) || id == Guid.Empty)
        {
            reason = "o identificador é inválido.";
            return false;
        }

        if (!TryReadString(element, "originalPath", out var originalPath) || string.IsNullOrWhiteSpace(originalPath))
        {
            reason = "o caminho original está vazio.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(originalPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "o caminho original é inválido.";
            return false;
        }

        var kind = MediaKind.Unknown;
        if (TryGetProperty(element, "kind", out var kindElement))
        {
            try
            {
                kind = kindElement.Deserialize<MediaKind>(JsonOptions);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                reason = "o tipo de mídia é inválido.";
                return false;
            }

            if (!Enum.IsDefined(kind))
            {
                reason = "o tipo de mídia é inválido.";
                return false;
            }
        }

        if (kind == MediaKind.Unknown)
        {
            kind = MediaTypeDetector.Detect(fullPath);
        }

        if (kind == MediaKind.Unknown)
        {
            reason = "o formato de mídia não é reconhecido.";
            return false;
        }

        _ = TryReadString(element, "displayName", out var displayName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Path.GetFileName(fullPath);
        }

        var order = TryReadInt32(element, "order", out var savedOrder) ? savedOrder : itemIndex;
        item = new PlaylistItem(id, displayName.Trim(), fullPath, kind, order);
        return true;
    }

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
}
