namespace GuiaPlay.Core;

public enum MediaKind
{
    Unknown,
    Video,
    Audio,
    YouTube = 3,
    Image = 4
}

public static class MediaTypeDetector
{
    public static IReadOnlyList<string> SupportedVideoExtensions { get; } =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp", ".ogv"];

    public static IReadOnlyList<string> SupportedAudioExtensions { get; } =
        [".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".opus", ".aiff", ".alac"];

    public static IReadOnlyList<string> SupportedImageExtensions { get; } =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [.. SupportedVideoExtensions, .. SupportedAudioExtensions, .. SupportedImageExtensions];

    private static readonly HashSet<string> VideoExtensions = new(SupportedVideoExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AudioExtensions = new(SupportedAudioExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ImageExtensions = new(SupportedImageExtensions, StringComparer.OrdinalIgnoreCase);

    public static MediaKind Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaKind.Unknown;
        }

        var extension = Path.GetExtension(path);
        if (VideoExtensions.Contains(extension))
        {
            return MediaKind.Video;
        }

        if (AudioExtensions.Contains(extension)) return MediaKind.Audio;
        return ImageExtensions.Contains(extension) ? MediaKind.Image : MediaKind.Unknown;
    }
}

public static class PlaybackRouting
{
    public static bool UsesVideoOutputs(MediaKind mediaKind) => mediaKind is MediaKind.Video or MediaKind.Image;
}
