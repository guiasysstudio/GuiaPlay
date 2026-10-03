using System.Collections.Concurrent;

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

public sealed record MediaProbeResult(string Path, MediaProbeStatus Status);

public static class MediaFileProbe
{
    private static readonly BoundedFileProbeExecutor Executor = new(
        workerCount: Math.Clamp(Environment.ProcessorCount, 1, 4),
        queueCapacity: 64,
        File.Exists);

    public static async Task<MediaProbeStatus> ProbeAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaProbeStatus.Unsupported;
        }

        try
        {
            if (MediaTypeDetector.Detect(path) == MediaKind.Unknown)
            {
                return MediaProbeStatus.Unsupported;
            }

            var fullPath = Path.GetFullPath(path);
            return await Executor.ExistsAsync(fullPath, timeout, cancellationToken).ConfigureAwait(false)
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

    /// <summary>
    /// Probes a collection without creating one worker task per path. The native/synchronous
    /// filesystem calls are also executed by a separate bounded worker pool, so an unavailable
    /// SMB server cannot consume the process thread pool indefinitely.
    /// </summary>
    public static async Task<IReadOnlyList<MediaProbeResult>> ProbeManyAsync(
        IEnumerable<string> paths,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        var indexedPaths = paths.Select((path, index) => (Path: path ?? string.Empty, Index: index)).ToArray();
        var results = new MediaProbeResult[indexedPaths.Length];
        await Parallel.ForEachAsync(
            indexedPaths,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4),
                CancellationToken = cancellationToken
            },
            async (entry, token) =>
            {
                var status = await ProbeAsync(entry.Path, timeout, token).ConfigureAwait(false);
                results[entry.Index] = new MediaProbeResult(entry.Path, status);
            }).ConfigureAwait(false);
        return results;
    }
}

/// <summary>
/// Runs potentially blocking filesystem probes on a small set of dedicated background threads.
/// A timed-out request is abandoned, but the underlying synchronous OS call is allowed to finish
/// without occupying a thread-pool thread. Queue and worker counts are both bounded.
/// </summary>
internal sealed class BoundedFileProbeExecutor : IDisposable
{
    private readonly BlockingCollection<Request> _requests;
    private readonly Func<string, bool> _exists;
    private readonly Thread[] _workers;
    private int _disposed;

    public BoundedFileProbeExecutor(int workerCount, int queueCapacity, Func<string, bool> exists)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);
        ArgumentNullException.ThrowIfNull(exists);
        _requests = new BlockingCollection<Request>(queueCapacity);
        _exists = exists;
        _workers = Enumerable.Range(0, workerCount)
            .Select(index => new Thread(Consume)
            {
                IsBackground = true,
                Name = $"GuiaPlay file probe {index + 1}"
            })
            .ToArray();
        foreach (var worker in _workers)
        {
            worker.Start();
        }
    }

    internal int WorkerCount => _workers.Length;
    internal int QueueCapacity => _requests.BoundedCapacity;

    public async Task<bool> ExistsAsync(
        string fullPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var request = new Request(fullPath);
        try
        {
            if (!_requests.TryAdd(request))
            {
                throw new TimeoutException("A fila limitada de verificaÃ§Ã£o de arquivos estÃ¡ ocupada.");
            }
        }
        catch (InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(BoundedFileProbeExecutor));
        }

        try
        {
            return await request.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.Abandon();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _requests.CompleteAdding();
        foreach (var worker in _workers)
        {
            _ = worker.Join(TimeSpan.FromMilliseconds(250));
        }
    }

    private void Consume()
    {
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            if (request.IsAbandoned)
            {
                continue;
            }

            try
            {
                var exists = _exists(request.Path);
                request.TrySetResult(exists);
            }
            catch (Exception exception)
            {
                request.TrySetException(exception);
            }
        }
    }

    private sealed class Request(string path)
    {
        private int _abandoned;

        public string Path { get; } = path;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAbandoned => Volatile.Read(ref _abandoned) != 0;

        public void Abandon() => Interlocked.Exchange(ref _abandoned, 1);

        public void TrySetResult(bool value)
        {
            if (!IsAbandoned)
            {
                Completion.TrySetResult(value);
            }
        }

        public void TrySetException(Exception exception)
        {
            if (!IsAbandoned)
            {
                Completion.TrySetException(exception);
            }
        }
    }
}
