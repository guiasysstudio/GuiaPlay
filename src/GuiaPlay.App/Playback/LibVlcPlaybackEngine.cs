using System.Runtime.InteropServices;
using GuiaPlay.App.Services;
using GuiaPlay.Core;
using LibVLCSharp.Shared;

namespace GuiaPlay.App.Playback;

internal sealed class LibVlcPlaybackEngine : IAsyncDisposable
{
    private readonly LibVLC _libVlc;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _nativeGate = new();
    private readonly object _callbackGate = new();
    private readonly object _disposeSync = new();
    private EqualizerConfiguration _equalizerConfiguration;
    private Session? _current;
    private Task? _disposeTask;
    private int _disposeStarted;

    public LibVlcPlaybackEngine(EqualizerConfiguration equalizerConfiguration)
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--no-osd", "--quiet");
        EqualizerCatalog = LibVlcEqualizerCatalog.Discover();
        _equalizerConfiguration = EqualizerConfigurationBehavior.Normalize(
            equalizerConfiguration,
            EqualizerCatalog.Bands.Count);
    }

    public LibVlcEqualizerCatalog EqualizerCatalog { get; }

    public event Action<long>? FrameReady;
    public event Action<long>? Playing;
    public event Action<long>? Paused;
    public event Action<long>? EndReached;
    public event Action<long, string>? Error;
    public event Action<long, long>? TimeChanged;
    public event Action<long, long>? LengthChanged;
    public event Action<long, string>? AudioDeviceChanged;
    public event Action<string>? EqualizerFailed;

    public long Time
    {
        get
        {
            lock (_nativeGate)
            {
                return Volatile.Read(ref _current) is { IsDisposed: false } session ? session.Player.Time : 0;
            }
        }
    }

    public long Length
    {
        get
        {
            lock (_nativeGate)
            {
                return Volatile.Read(ref _current) is { IsDisposed: false } session ? session.Player.Length : 0;
            }
        }
    }

    public int Volume
    {
        get
        {
            lock (_nativeGate)
            {
                return Volatile.Read(ref _current) is { IsDisposed: false } session ? session.Player.Volume : 100;
            }
        }
        set
        {
            lock (_nativeGate)
            {
                if (Volatile.Read(ref _current) is { IsDisposed: false } session)
                {
                    session.Player.Volume = Math.Clamp(value, 0, 100);
                }
            }
        }
    }

    public bool Muted
    {
        get
        {
            lock (_nativeGate)
            {
                return Volatile.Read(ref _current) is { IsDisposed: false } session && session.Player.Mute;
            }
        }
        set
        {
            lock (_nativeGate)
            {
                if (Volatile.Read(ref _current) is { IsDisposed: false } session)
                {
                    session.Player.Mute = value;
                }
            }
        }
    }

    public IReadOnlyList<AudioDeviceIdentity> EnumerateAudioDevices()
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var devices = new List<AudioDeviceIdentity>();
            foreach (var output in _libVlc.AudioOutputs)
            {
                foreach (var device in _libVlc.AudioOutputDevices(output.Name))
                {
                    if (string.IsNullOrWhiteSpace(output.Name) || string.IsNullOrWhiteSpace(device.DeviceIdentifier))
                    {
                        continue;
                    }

                    var displayName = string.IsNullOrWhiteSpace(device.Description)
                        ? device.DeviceIdentifier
                        : device.Description;
                    devices.Add(new AudioDeviceIdentity(output.Name, device.DeviceIdentifier, displayName));
                }
            }

            return devices
                .DistinctBy(device => (device.Module.ToUpperInvariant(), device.DeviceId))
                .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(device => device.Module, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public async Task LoadAsync(string path, MediaKind mediaKind, long generation, AudioOutputPreference audioOutput)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var previous = DetachCurrent();
            if (previous is not null)
            {
                await previous.StopAndDisposeAsync().ConfigureAwait(false);
            }

            var effectiveAudioOutput = AudioPreferenceResolver.Resolve(audioOutput, EnumerateAudioDevices()) == AudioPreferenceAvailability.Available
                ? audioOutput
                : AudioOutputPreference.Default;
            Session? session = null;
            try
            {
                lock (_nativeGate)
                {
                    ObjectDisposedException.ThrowIf(IsDisposed, this);
                    session = new Session(_libVlc, path, mediaKind, generation, effectiveAudioOutput, this);
                    ApplyEqualizerWhileLocked(session.Player);
                }

                AttachCurrent(session);
            }
            catch
            {
                if (session is not null)
                {
                    await session.StopAndDisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task ApplyAudioOutputAsync(AudioOutputPreference audioOutput)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var previous = Volatile.Read(ref _current);
            if (previous is null)
            {
                return;
            }

            var effectiveAudioOutput = AudioPreferenceResolver.Resolve(audioOutput, EnumerateAudioDevices()) == AudioPreferenceAvailability.Available
                ? audioOutput
                : AudioOutputPreference.Default;
            if (!AudioOutputRuntimePolicy.RequiresPlayerRecreation(previous.AppliedAudioOutput, effectiveAudioOutput))
            {
                return;
            }

            _ = DetachCurrent();

            var path = previous.Path;
            var mediaKind = previous.MediaKind;
            var generation = previous.Generation;
            await previous.StopAndDisposeAsync().ConfigureAwait(false);
            Session? session = null;
            try
            {
                lock (_nativeGate)
                {
                    ObjectDisposedException.ThrowIf(IsDisposed, this);
                    session = new Session(_libVlc, path, mediaKind, generation, effectiveAudioOutput, this);
                    ApplyEqualizerWhileLocked(session.Player);
                }

                AttachCurrent(session);
            }
            catch
            {
                if (session is not null)
                {
                    await session.StopAndDisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task ApplyEqualizerAsync(EqualizerConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _equalizerConfiguration = EqualizerConfigurationBehavior.Normalize(
                configuration,
                EqualizerCatalog.Bands.Count);
            if (Volatile.Read(ref _current) is { } session)
            {
                lock (_nativeGate)
                {
                    if (!session.IsDisposed && ReferenceEquals(Volatile.Read(ref _current), session))
                    {
                        ApplyEqualizerWhileLocked(session.Player);
                    }
                }
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public bool Play()
    {
        lock (_nativeGate)
        {
            var session = Volatile.Read(ref _current);
            if (session is null || session.IsDisposed)
            {
                throw new InvalidOperationException("Nenhuma mídia foi carregada.");
            }

            if (session.Player.Time >= Math.Max(0, session.Player.Length - 250))
            {
                session.Player.Time = 0;
            }

            return session.Player.Play();
        }
    }

    public void Pause()
    {
        lock (_nativeGate)
        {
            if (Volatile.Read(ref _current) is { IsDisposed: false } session)
            {
                session.Player.SetPause(true);
            }
        }
    }

    public void Resume()
    {
        lock (_nativeGate)
        {
            if (Volatile.Read(ref _current) is { IsDisposed: false } session)
            {
                session.Player.SetPause(false);
            }
        }
    }

    public void Seek(long milliseconds)
    {
        lock (_nativeGate)
        {
            if (Volatile.Read(ref _current) is { IsDisposed: false } session && session.Player.IsSeekable)
            {
                session.Player.Time = Math.Clamp(milliseconds, 0, Math.Max(0, session.Player.Length));
            }
        }
    }

    public async Task StopAsync()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (Volatile.Read(ref _current) is { } session)
            {
                await session.StopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public bool TryAcquireLatestFrame(long generation, out FrameBufferRing.FrameLease lease)
    {
        var session = Volatile.Read(ref _current);
        if (session is null || session.Generation != generation)
        {
            lease = default;
            return false;
        }

        return session.Buffers.TryAcquireLatest(out lease);
    }

    private bool IsDisposed => Volatile.Read(ref _disposeStarted) != 0;

    private Session? DetachCurrent()
    {
        lock (_callbackGate)
        {
            return Interlocked.Exchange(ref _current, null);
        }
    }

    private void AttachCurrent(Session session)
    {
        lock (_callbackGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Volatile.Write(ref _current, session);
        }
    }

    private void Publish(Session session, Action publish)
    {
        lock (_callbackGate)
        {
            if (IsDisposed || session.IsDisposed || !ReferenceEquals(Volatile.Read(ref _current), session))
            {
                return;
            }

            try
            {
                publish();
            }
            catch (Exception exception)
            {
                FileLogger.Error("Um consumidor de evento do motor de reprodução falhou.", exception);
            }
        }
    }

    private void ApplyEqualizerWhileLocked(MediaPlayer player)
    {
        if (!EqualizerCatalog.TryApply(player, _equalizerConfiguration, out var error) && error is not null)
        {
            try
            {
                EqualizerFailed?.Invoke(error);
            }
            catch (Exception exception)
            {
                FileLogger.Error("Um consumidor do erro de equalizador falhou.", exception);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        _ = Interlocked.Exchange(ref _disposeStarted, 1);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var session = DetachCurrent();
            try
            {
                if (session is not null)
                {
                    await session.StopAndDisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_nativeGate)
                {
                    _libVlc.Dispose();
                }
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private sealed class Session
    {
        private readonly LibVlcPlaybackEngine _owner;
        private readonly MediaPlayer.LibVLCVideoLockCb? _lockCallback;
        private readonly MediaPlayer.LibVLCVideoDisplayCb? _displayCallback;
        private readonly MediaPlayer.LibVLCVideoFormatCb? _formatCallback;
        private readonly MediaPlayer.LibVLCVideoCleanupCb? _cleanupCallback;
        private readonly object _disposeSync = new();
        private Task? _disposeTask;
        private int _disposed;

        public Session(
            LibVLC libVlc,
            string path,
            MediaKind mediaKind,
            long generation,
            AudioOutputPreference audioOutput,
            LibVlcPlaybackEngine owner)
        {
            _owner = owner;
            Path = path;
            MediaKind = mediaKind;
            Generation = generation;
            Buffers = new FrameBufferRing(message => FileLogger.Info($"FrameBuffer: {message}"));
            Media? media = null;
            MediaPlayer? player = null;
            try
            {
                media = new Media(libVlc, new Uri(path));
                Media = media;
                if (mediaKind == MediaKind.Audio)
                {
                    Media.AddOption(":no-video");
                }

                player = new MediaPlayer(libVlc) { Media = Media };
                Player = player;
                if (audioOutput.IsExplicit)
                {
                    if (!Player.SetAudioOutput(audioOutput.Module!))
                    {
                        throw new InvalidOperationException($"O LibVLC não aceitou o módulo de áudio '{audioOutput.Module}'.");
                    }

                    Player.SetOutputDevice(audioOutput.DeviceId!, audioOutput.Module!);
                }

                AppliedAudioOutput = audioOutput;

                if (mediaKind == MediaKind.Video)
                {
                    _lockCallback = LockVideo;
                    _displayCallback = DisplayVideo;
                    _formatCallback = FormatVideo;
                    _cleanupCallback = CleanupVideo;
                    Player.SetVideoCallbacks(_lockCallback, null, _displayCallback);
                    Player.SetVideoFormatCallbacks(_formatCallback, _cleanupCallback);
                }

                Player.SetVideoTitleDisplay(Position.Disable, 0);
                SubscribeEvents();
            }
            catch
            {
                Buffers.BeginShutdown();
                try
                {
                    player?.Dispose();
                }
                finally
                {
                    try
                    {
                        media?.Dispose();
                    }
                    finally
                    {
                        Buffers.CompleteShutdownAfterCallbacksStopped();
                    }
                }

                throw;
            }
        }

        public string Path { get; }
        public MediaKind MediaKind { get; }
        public long Generation { get; }
        public AudioOutputPreference AppliedAudioOutput { get; }
        public Media Media { get; }
        public MediaPlayer Player { get; }
        public FrameBufferRing Buffers { get; }
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public async Task StopAsync()
        {
            if (IsDisposed)
            {
                return;
            }

            await Task.Run(() =>
            {
                lock (_owner._nativeGate)
                {
                    if (!IsDisposed)
                    {
                        Player.Stop();
                    }
                }
            }).ConfigureAwait(false);
            Buffers.Reset();
            Buffers.ReleaseRetiredWritesAfterCallbacksStopped();
            lock (_owner._nativeGate)
            {
                if (!IsDisposed)
                {
                    Player.Time = 0;
                }
            }
        }

        public Task StopAndDisposeAsync()
        {
            lock (_disposeSync)
            {
                return _disposeTask ??= StopAndDisposeCoreAsync();
            }
        }

        private async Task StopAndDisposeCoreAsync()
        {
            _ = Interlocked.Exchange(ref _disposed, 1);
            lock (_owner._nativeGate)
            {
                UnsubscribeEvents();
            }

            Buffers.BeginShutdown();
            try
            {
                await Task.Run(() =>
                {
                    lock (_owner._nativeGate)
                    {
                        Player.Stop();
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    lock (_owner._nativeGate)
                    {
                        Player.Dispose();
                    }
                }
                finally
                {
                    try
                    {
                        Buffers.CompleteShutdownAfterCallbacksStopped();
                        lock (_owner._nativeGate)
                        {
                            Media.Dispose();
                        }
                    }
                    finally
                    {
                        Buffers.Dispose();
                    }
                }
            }
        }

        private nint LockVideo(nint opaque, nint planes) => Buffers.AcquireWrite(planes);

        private void DisplayVideo(nint opaque, nint picture)
        {
            try
            {
                if (Buffers.Commit(picture))
                {
                    _owner.Publish(this, () => _owner.FrameReady?.Invoke(Generation));
                }
            }
            catch (Exception exception)
            {
                FileLogger.Error("Falha no callback de exibição de vídeo do LibVLC.", exception);
            }
        }

        private uint FormatVideo(
            ref nint opaque,
            nint chroma,
            ref uint width,
            ref uint height,
            ref uint pitches,
            ref uint lines)
        {
            try
            {
                var alignedPitch = Align32(checked(width * 4));
                var alignedLines = Align32(height);
                if (!Buffers.TryConfigure(width, height, alignedPitch, alignedLines))
                {
                    return 0;
                }

                Marshal.WriteByte(chroma, 0, (byte)'R');
                Marshal.WriteByte(chroma, 1, (byte)'V');
                Marshal.WriteByte(chroma, 2, (byte)'3');
                Marshal.WriteByte(chroma, 3, (byte)'2');
                pitches = alignedPitch;
                lines = alignedLines;
                return FrameBufferRing.PictureBufferCount;
            }
            catch (Exception exception)
            {
                // Exceptions must never escape across the unmanaged LibVLC callback boundary.
                Buffers.Reset();
                FileLogger.Error("O formato de v\u00eddeo informado pelo LibVLC foi rejeitado.", exception);
                return 0;
            }
        }

        private void CleanupVideo(ref nint opaque)
        {
            try
            {
                Buffers.CompleteFormatCleanupAfterCallbacksStopped();
            }
            catch (Exception exception)
            {
                FileLogger.Error("Falha no callback de limpeza de vídeo do LibVLC.", exception);
            }
        }

        private void SubscribeEvents()
        {
            Player.Playing += OnPlaying;
            Player.Paused += OnPaused;
            Player.EndReached += OnEndReached;
            Player.EncounteredError += OnEncounteredError;
            Player.TimeChanged += OnTimeChanged;
            Player.LengthChanged += OnLengthChanged;
            Player.AudioDevice += OnAudioDeviceChanged;
        }

        private void UnsubscribeEvents()
        {
            Player.Playing -= OnPlaying;
            Player.Paused -= OnPaused;
            Player.EndReached -= OnEndReached;
            Player.EncounteredError -= OnEncounteredError;
            Player.TimeChanged -= OnTimeChanged;
            Player.LengthChanged -= OnLengthChanged;
            Player.AudioDevice -= OnAudioDeviceChanged;
        }

        private void OnPlaying(object? sender, EventArgs args) =>
            _owner.Publish(this, () => _owner.Playing?.Invoke(Generation));

        private void OnPaused(object? sender, EventArgs args) =>
            _owner.Publish(this, () => _owner.Paused?.Invoke(Generation));

        private void OnEndReached(object? sender, EventArgs args) =>
            _owner.Publish(this, () => _owner.EndReached?.Invoke(Generation));

        private void OnEncounteredError(object? sender, EventArgs args) =>
            _owner.Publish(this, () => _owner.Error?.Invoke(Generation, "A mídia não pôde ser reproduzida."));

        private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs args) =>
            _owner.Publish(this, () => _owner.TimeChanged?.Invoke(Generation, args.Time));

        private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs args) =>
            _owner.Publish(this, () => _owner.LengthChanged?.Invoke(Generation, args.Length));

        private void OnAudioDeviceChanged(object? sender, MediaPlayerAudioDeviceEventArgs args) =>
            _owner.Publish(this, () => _owner.AudioDeviceChanged?.Invoke(Generation, args.AudioDevice));

        private static uint Align32(uint value) => checked((value + 31u) & ~31u);
    }
}
