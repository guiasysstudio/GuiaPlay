namespace GuiaPlay.Core;

public sealed record ConnectedDisplay(string SessionId, string? PersistentId, bool IsPrimary)
{
    public bool HasReliableIdentity => !string.IsNullOrWhiteSpace(PersistentId);
}

public enum SavedDisplayMatchKind
{
    Missing,
    Exact,
    Ambiguous
}

public sealed record SavedDisplayMatch(SavedDisplayMatchKind Kind, ConnectedDisplay? Display);

public static class DisplayPreferenceResolver
{
    public static SavedDisplayMatch Match(string? savedId, IEnumerable<ConnectedDisplay> connected)
    {
        if (string.IsNullOrWhiteSpace(savedId))
        {
            return new SavedDisplayMatch(SavedDisplayMatchKind.Missing, null);
        }

        var matches = connected
            .Where(display => display.HasReliableIdentity && string.Equals(display.PersistentId, savedId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => new SavedDisplayMatch(SavedDisplayMatchKind.Exact, matches[0]),
            > 1 => new SavedDisplayMatch(SavedDisplayMatchKind.Ambiguous, null),
            _ => new SavedDisplayMatch(SavedDisplayMatchKind.Missing, null)
        };
    }

    public static IReadOnlySet<string> ResolveStartupOutputs(
        IEnumerable<string> savedIds,
        IEnumerable<ConnectedDisplay> connected,
        string? operatorPersistentId)
    {
        var displays = connected.ToArray();
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in savedIds)
        {
            var match = Match(id, displays);
            if (match.Kind == SavedDisplayMatchKind.Exact &&
                !string.Equals(id, operatorPersistentId, StringComparison.OrdinalIgnoreCase))
            {
                resolved.Add(id);
            }
        }

        return ExcludeOperator(resolved, operatorPersistentId);
    }

    public static IReadOnlySet<string> ExcludeOperator(IEnumerable<string> selectedIds, string? operatorPersistentId)
    {
        return selectedIds
            .Where(id => !string.Equals(id, operatorPersistentId, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ReconnectAuthorization
{
    private readonly HashSet<string> _disconnected = new(StringComparer.OrdinalIgnoreCase);

    public void MarkDisconnected(string persistentId) => _disconnected.Add(persistentId);

    public bool MayRestoreSavedSelection(string persistentId) => !_disconnected.Contains(persistentId);

    public void ExplicitlyAuthorize(string persistentId) => _disconnected.Remove(persistentId);
}

public sealed record AudioDeviceIdentity(string Module, string DeviceId, string DisplayName);

public enum AudioPreferenceAvailability
{
    WindowsDefault,
    Available,
    Unknown,
    Missing
}

public static class AudioPreferenceResolver
{
    public static AudioPreferenceAvailability Resolve(
        AudioOutputPreference preference,
        IEnumerable<AudioDeviceIdentity> devices)
    {
        if (!preference.IsExplicit)
        {
            return AudioPreferenceAvailability.WindowsDefault;
        }

        var inventory = devices.ToArray();
        if (inventory.Length == 0)
        {
            return AudioPreferenceAvailability.Unknown;
        }

        return inventory.Any(device =>
            string.Equals(device.Module, preference.Module, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.DeviceId, preference.DeviceId, StringComparison.Ordinal))
            ? AudioPreferenceAvailability.Available
            : AudioPreferenceAvailability.Missing;
    }
}
