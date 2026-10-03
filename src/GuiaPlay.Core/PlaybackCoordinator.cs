namespace GuiaPlay.Core;

public enum PlaybackStatus
{
    Empty,
    Ready,
    Preparing,
    Playing,
    Paused,
    Stopped,
    Ended,
    Error
}

public enum LoadDecision
{
    Loaded,
    Cancelled
}

public readonly record struct PlaybackTransition(
    bool Accepted,
    bool CloseOutputs = false,
    bool PauseEngine = false);

/// <summary>
/// Deterministic lifecycle rules kept independent from WPF and LibVLC.
/// Every accepted media load gets a generation so late native events can be ignored.
/// </summary>
public sealed class PlaybackCoordinator
{
    public string? MediaPath { get; private set; }
    public MediaKind MediaKind { get; private set; }
    public long Generation { get; private set; }
    public PlaybackStatus Status { get; private set; } = PlaybackStatus.Empty;

    public bool IsActive => Status is PlaybackStatus.Preparing or PlaybackStatus.Playing or PlaybackStatus.Paused;

    public LoadDecision TryLoad(string path, bool replacementConfirmed)
        => TryLoad(path, MediaTypeDetector.Detect(path), replacementConfirmed);

    public LoadDecision TryLoad(string path, MediaKind mediaKind, bool replacementConfirmed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (mediaKind == MediaKind.Unknown)
        {
            throw new ArgumentException("O formato da mídia não é reconhecido.", nameof(mediaKind));
        }

        if (IsActive && !replacementConfirmed)
        {
            return LoadDecision.Cancelled;
        }

        Generation++;
        MediaPath = path;
        MediaKind = mediaKind;
        Status = PlaybackStatus.Ready;
        return LoadDecision.Loaded;
    }

    public bool CanPlay(int selectedVideoOutputCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(selectedVideoOutputCount);
        if (MediaPath is null)
        {
            return false;
        }

        return MediaKind switch
        {
            MediaKind.Audio => true,
            MediaKind.Video => selectedVideoOutputCount > 0,
            _ => false
        };
    }

    public long BeginPlayback()
    {
        if (MediaPath is null || Status is PlaybackStatus.Empty or PlaybackStatus.Preparing or PlaybackStatus.Playing)
        {
            throw new InvalidOperationException("Não há mídia pronta para iniciar.");
        }

        Status = PlaybackStatus.Preparing;
        return Generation;
    }

    public bool MarkPlaying(long generation)
    {
        if (!PlaybackCallbackPolicy.ShouldAccept(PlaybackCallbackKind.Playing, generation, Generation, Status) ||
            Status != PlaybackStatus.Preparing)
        {
            return false;
        }

        Status = PlaybackStatus.Playing;
        return true;
    }

    public PlaybackTransition Pause()
    {
        if (Status != PlaybackStatus.Playing)
        {
            return new(false);
        }

        Status = PlaybackStatus.Paused;
        return new(true);
    }

    public PlaybackTransition Resume()
    {
        if (Status != PlaybackStatus.Paused)
        {
            return new(false);
        }

        Status = PlaybackStatus.Playing;
        return new(true);
    }

    public PlaybackTransition Stop()
    {
        if (MediaPath is null)
        {
            return new(false);
        }

        Status = PlaybackStatus.Stopped;
        return new(true, CloseOutputs: true);
    }

    public PlaybackTransition StopFailed()
    {
        if (MediaPath is null)
        {
            return new(false);
        }

        Status = PlaybackStatus.Error;
        return new(true, CloseOutputs: true);
    }

    public PlaybackTransition NaturalEnd(long generation)
    {
        if (!AcceptsCallback(PlaybackCallbackKind.EndReached, generation))
        {
            return new(false);
        }

        Status = PlaybackStatus.Ended;
        return new(true, CloseOutputs: true);
    }

    public PlaybackTransition PlaybackFailed(long generation)
    {
        if (!AcceptsCallback(PlaybackCallbackKind.Error, generation))
        {
            return new(false);
        }

        Status = PlaybackStatus.Error;
        return new(true, CloseOutputs: true);
    }

    public PlaybackTransition PublicOutputsChanged(int remainingCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(remainingCount);

        if (MediaKind == MediaKind.Video &&
            remainingCount == 0 &&
            Status is PlaybackStatus.Playing or PlaybackStatus.Preparing)
        {
            Status = PlaybackStatus.Paused;
            return new(true, PauseEngine: true);
        }

        return new(false);
    }

    public PlaybackTransition RequiredDeviceLost(long generation)
    {
        if (!IsCurrent(generation) || Status is not PlaybackStatus.Playing and not PlaybackStatus.Preparing)
        {
            return new(false);
        }

        Status = PlaybackStatus.Paused;
        return new(true, PauseEngine: true);
    }

    public bool AcceptsCallback(PlaybackCallbackKind callback, long generation) =>
        PlaybackCallbackPolicy.ShouldAccept(callback, generation, Generation, Status);

    private bool IsCurrent(long generation) => generation == Generation;
}
