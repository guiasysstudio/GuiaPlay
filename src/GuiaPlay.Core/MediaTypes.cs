namespace GuiaPlay.Core;

public enum MediaKind
{
    Unknown,
    Video,
    Audio
}

public static class MediaTypeDetector
{
    public static IReadOnlyList<string> SupportedVideoExtensions { get; } =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".3gp", ".ogv"];

    public static IReadOnlyList<string> SupportedAudioExtensions { get; } =
        [".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".opus", ".aiff", ".alac"];

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [.. SupportedVideoExtensions, .. SupportedAudioExtensions];

    private static readonly HashSet<string> VideoExtensions = new(SupportedVideoExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AudioExtensions = new(SupportedAudioExtensions, StringComparer.OrdinalIgnoreCase);

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

        return AudioExtensions.Contains(extension) ? MediaKind.Audio : MediaKind.Unknown;
    }
}

public static class PlaybackRouting
{
    public static bool UsesVideoOutputs(MediaKind mediaKind) => mediaKind == MediaKind.Video;
}
