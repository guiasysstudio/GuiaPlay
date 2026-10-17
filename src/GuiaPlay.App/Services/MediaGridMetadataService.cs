using System.Globalization;
using System.IO;
using GuiaPlay.App.Models;
using GuiaPlay.Core;

namespace GuiaPlay.App.Services;

/// <summary>
/// Bounded file-system metadata reader for the playlist grid.
/// This code must never load native multimedia libraries or shell thumbnail COM
/// on an untrusted background thread: the old implementation spawned a hidden
/// LibVLC parser for every UI startup and released shell COM/GDI handles on a
/// thread-pool thread, independent of the lazily created playback engine.
/// An access violation in native code would terminate the ENTIRE GUI process.
/// </summary>
internal static class MediaGridMetadataService
{
    public const int MaximumInspectedRows = 150;

    public static Task InspectAsync(
        IReadOnlyList<MediaGridRow> rows,
        Func<MediaGridRow, MediaGridMetadata, Task> onResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(onResult);

        // UI refresh and file IO run independently. The only metadata accepted
        // automatically are verified file-system properties. Never invent a
        // duration/resolution/thumbnail when they have not been decoded safely.
        return Task.Run(async () =>
        {
            foreach (var row in rows.Take(MaximumInspectedRows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = InspectManagedFileInfo(row);
                await onResult(row, metadata).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    private static MediaGridMetadata InspectManagedFileInfo(MediaGridRow row)
    {
        if (row.Node.Item.Kind == MediaKind.YouTube)
        {
            return new MediaGridMetadata("—", "Web", "—", null);
        }

        var path = row.FilePath;
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // Do not access UNC shares automatically while rebuilding the UI.
            return new MediaGridMetadata("—", "—", "—", null);
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new MediaGridMetadata("—", "—", "—", null);
            }

            var date = info.LastWriteTime.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
            return new MediaGridMetadata("—", "—", date, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return new MediaGridMetadata("—", "—", "—", null);
        }
    }
}
