namespace GuiaPlay.Core;

public enum PlaybackCallbackKind
{
    Playing,
    Paused,
    EndReached,
    Error,
    TimeChanged,
    LengthChanged,
    FrameReady,
    AudioDeviceChanged
}

public static class PlaybackCallbackPolicy
{
    public static bool ShouldAccept(
        PlaybackCallbackKind callback,
        long callbackGeneration,
        long currentGeneration,
        PlaybackStatus status)
    {
        if (callbackGeneration != currentGeneration)
        {
            return false;
        }

        return callback switch
        {
            PlaybackCallbackKind.Playing => status is PlaybackStatus.Preparing or PlaybackStatus.Playing,
            PlaybackCallbackKind.Paused => status == PlaybackStatus.Paused,
            PlaybackCallbackKind.EndReached => status is PlaybackStatus.Preparing or PlaybackStatus.Playing or PlaybackStatus.Paused,
            PlaybackCallbackKind.Error => status is PlaybackStatus.Ready or PlaybackStatus.Preparing or PlaybackStatus.Playing or PlaybackStatus.Paused,
            PlaybackCallbackKind.TimeChanged or PlaybackCallbackKind.LengthChanged or PlaybackCallbackKind.FrameReady =>
                status is PlaybackStatus.Ready or PlaybackStatus.Preparing or PlaybackStatus.Playing or PlaybackStatus.Paused,
            PlaybackCallbackKind.AudioDeviceChanged =>
                status is PlaybackStatus.Ready or PlaybackStatus.Preparing or PlaybackStatus.Playing or PlaybackStatus.Paused,
            _ => false
        };
    }
}

public sealed class NotificationDebouncer(TimeSpan interval, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private long? _lastAcceptedTimestamp;

    public bool TryAccept()
    {
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            if (_lastAcceptedTimestamp is { } previous && _timeProvider.GetElapsedTime(previous, now) < interval)
            {
                return false;
            }

            _lastAcceptedTimestamp = now;
            return true;
        }
    }
}

public enum MediaProbeStatus
{
    Available,
    Missing,
    Unsupported,
    TimedOut,
    InvalidPath
}

public static class MediaFileProbe
{
    public static async Task<MediaProbeStatus> ProbeAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || MediaTypeDetector.Detect(path) == MediaKind.Unknown)
        {
            return MediaProbeStatus.Unsupported;
        }

        try
        {
            var existsTask = Task.Run(() => File.Exists(Path.GetFullPath(path)), CancellationToken.None);
            return await existsTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false)
                ? MediaProbeStatus.Available
                : MediaProbeStatus.Missing;
        }
        catch (TimeoutException)
        {
            return MediaProbeStatus.TimedOut;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return MediaProbeStatus.InvalidPath;
        }
    }
}
