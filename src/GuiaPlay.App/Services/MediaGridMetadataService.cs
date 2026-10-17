using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GuiaPlay.App.Models;
using GuiaPlay.Core;
using LibVLCSharp.Shared;

namespace GuiaPlay.App.Services;

/// <summary>
/// Bounded, non-destructive local-media metadata reader.
/// ParseLocal explicitly avoids network metadata/artwork retrieval.
/// The shell supplies a thumbnail only when Windows offers a real one.
/// </summary>
internal static class MediaGridMetadataService
{
    public const int MaximumInspectedRows = 150;
    private const int ParseTimeoutMs = 1500;

    public static Task InspectAsync(
        IReadOnlyList<MediaGridRow> rows,
        Func<MediaGridRow, MediaGridMetadata, Task> onResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(onResult);
        return Task.Run(() => InspectCoreAsync(rows, onResult, cancellationToken), cancellationToken);
    }

    private static async Task InspectCoreAsync(
        IReadOnlyList<MediaGridRow> rows,
        Func<MediaGridRow, MediaGridMetadata, Task> onResult,
        CancellationToken cancellationToken)
    {
        // LibVLC is isolated from the application's active playback engine.
        LibVLC? parser = null;
        try
        {
            parser = new LibVLC("--no-video-title-show");
        }
        catch (Exception)
        {
            // Date and shell thumbnails remain available if metadata decoding fails.
        }

        try
        {
            foreach (var row in rows.Take(MaximumInspectedRows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                MediaGridMetadata metadata;
                try
                {
                    metadata = await InspectOneAsync(row.FilePath, parser, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    metadata = new MediaGridMetadata("—", "—", "—", null);
                }

                cancellationToken.ThrowIfCancellationRequested();
                await onResult(row, metadata).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A new playlist invalidates any previous metadata batch.
        }
        finally
        {
            parser?.Dispose();
        }
    }

    private static async Task<MediaGridMetadata> InspectOneAsync(
        string path,
        LibVLC? parser,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(async () =>
        {
            // Do not probe UNC paths automatically. Remote media may be valid to play
            // through the existing bounded probe service, but should never block UI metadata.
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return new MediaGridMetadata("—", "—", "—", null);
            }

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new MediaGridMetadata("—", "—", "—", null);
            }

            var date = info.LastWriteTime.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
            string duration = "—";
            string resolution = "—";

            if (parser is not null && MediaTypeDetector.Detect(path) != GuiaPlay.Core.MediaKind.Unknown)
            {
                try
                {
                    using var media = new Media(parser, new Uri(info.FullName));
                    await media.Parse(MediaParseOptions.ParseLocal, ParseTimeoutMs, cancellationToken)
                        .ConfigureAwait(false);
                    if (media.Duration > 0)
                    {
                        var total = TimeSpan.FromMilliseconds(media.Duration);
                        duration = total.TotalHours >= 1
                            ? total.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
                            : total.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
                    }

                    if (MediaTypeDetector.Detect(path) == GuiaPlay.Core.MediaKind.Video)
                    {
                        var video = media.Tracks.FirstOrDefault(track => track.TrackType == TrackType.Video);
                        if (video.Data.Video.Width > 0 && video.Data.Video.Height > 0)
                        {
                            resolution = $"{video.Data.Video.Width} × {video.Data.Video.Height}";
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Partial properties do not prevent an available file from appearing.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var image = MediaTypeDetector.Detect(path) == GuiaPlay.Core.MediaKind.Video
                ? TryGetWindowsThumbnail(info.FullName)
                : null;
            return new MediaGridMetadata(duration, resolution, date, image);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static BitmapSource? TryGetWindowsThumbnail(string path)
    {
        IShellItemImageFactory? factory = null;
        nint hBitmap = 0;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(path, 0, ref iid, out factory);
            if (hr < 0 || factory is null)
            {
                return null;
            }

            // THUMBNAILONLY: never substitute a generic Windows file icon.
            hr = factory.GetImage(new NativeSize(96, 54), ShellImageFlags.ThumbnailOnly, out hBitmap);
            if (hr < 0 || hBitmap == 0)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, 0, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(96, 54));
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (hBitmap != 0)
            {
                _ = DeleteObject(hBitmap);
            }

            if (factory is not null)
            {
                _ = Marshal.ReleaseComObject(factory);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [Flags]
    private enum ShellImageFlags : uint
    {
        ThumbnailOnly = 0x8
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, ShellImageFlags flags, out nint hBitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path, nint bindContext, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);
}
