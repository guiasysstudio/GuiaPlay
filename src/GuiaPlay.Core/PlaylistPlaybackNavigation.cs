namespace GuiaPlay.Core;

public enum PlaylistPlaybackMode
{
    Manual,
    Sequential
}

public enum PlaylistRepeatMode
{
    Off,
    Current,
    All
}

/// <summary>
/// Resolves operator-controlled and end-of-media navigation in the real playlist
/// order. This does not sort by the user's search filter or create file copies.
/// </summary>
public static class PlaylistPlaybackNavigation
{
    public static Guid? Previous(PlaylistDocument playlist, Guid? current)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        var order = Flatten(playlist);
        var index = FindIndex(order, current);
        return index > 0 ? order[index - 1] : null;
    }

    public static Guid? Next(
        PlaylistDocument playlist,
        Guid? current,
        PlaylistPlaybackMode mode,
        PlaylistRepeatMode repeat,
        bool shuffle,
        bool automatic,
        int randomCandidateIndex = 0,
        IReadOnlySet<Guid>? playedInShuffleCycle = null)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (automatic && mode != PlaylistPlaybackMode.Sequential)
        {
            return null;
        }

        var order = Flatten(playlist);
        var index = FindIndex(order, current);
        if (index < 0)
        {
            return null; // Directly opened media cannot drive playlist auto-advance.
        }

        if (automatic && repeat == PlaylistRepeatMode.Current)
        {
            return order[index];
        }

        if (shuffle && order.Length > 1)
        {
            var candidates = order.Where(item => item != current &&
                (playedInShuffleCycle is null || !playedInShuffleCycle.Contains(item))).ToArray();
            if (candidates.Length == 0)
            {
                // Completing a shuffle cycle with Repeat Off means stop. An explicit
                // Next request may start another draw without starting playback.
                if (automatic && repeat != PlaylistRepeatMode.All)
                {
                    return null;
                }

                candidates = order.Where(item => item != current).ToArray();
            }

            var choice = Math.Abs((long)randomCandidateIndex) % candidates.Length;
            return candidates[choice];
        }

        if (index < order.Length - 1)
        {
            return order[index + 1];
        }

        return repeat == PlaylistRepeatMode.All ? order[0] : null;
    }

    public static bool Contains(PlaylistDocument playlist, Guid? itemId) =>
        FindIndex(Flatten(playlist), itemId) >= 0;

    private static Guid[] Flatten(PlaylistDocument playlist) =>
        playlist.Groups.OrderBy(group => group.Order)
            .SelectMany(group => group.Items.OrderBy(item => item.Order))
            .Select(item => item.Id).ToArray();

    private static int FindIndex(Guid[] order, Guid? itemId) =>
        itemId is { } id ? Array.IndexOf(order, id) : -1;
}
