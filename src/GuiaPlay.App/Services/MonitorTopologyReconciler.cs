using GuiaPlay.App.Models;

namespace GuiaPlay.App.Services;

internal sealed record MonitorReconciliation(MonitorInfo Previous, MonitorInfo? Current)
{
    public bool IsConnected => Current is not null;
    public bool NeedsReposition => Current is not null &&
        (Previous.Left != Current.Left ||
         Previous.Top != Current.Top ||
         Previous.Width != Current.Width ||
         Previous.Height != Current.Height ||
         Previous.DpiX != Current.DpiX ||
         Previous.DpiY != Current.DpiY);
}

internal static class MonitorTopologyReconciler
{
    public static MonitorInfo? FindCurrent(MonitorInfo previous, IReadOnlyList<MonitorInfo> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.PersistenceKey is { } persistentId)
        {
            var matches = current.Where(monitor =>
                    string.Equals(monitor.PersistenceKey, persistentId, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        return current.FirstOrDefault(monitor =>
            string.Equals(monitor.Id, previous.Id, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<MonitorReconciliation> Reconcile(
        IEnumerable<MonitorInfo> previous,
        IReadOnlyList<MonitorInfo> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        return previous.Select(monitor =>
                new MonitorReconciliation(monitor, FindCurrent(monitor, current)))
            .ToArray();
    }
}
