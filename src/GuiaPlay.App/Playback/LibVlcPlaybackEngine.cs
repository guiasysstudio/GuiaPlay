using System.Runtime.InteropServices;
using System.IO;
using GuiaPlay.Core;
using LibVLCSharp.Shared;

namespace GuiaPlay.App.Playback;

internal sealed class LibVlcPlaybackEngine : IAsyncDisposable
{
    private readonly LibVLC _libVlc;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Session? _current;
    private bool _disposed;

    public LibVlcPlaybackEngine()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--no-osd", "--quiet");
    }

    public event Action<long>? FrameReady;
    public event Action<long>? Playing;
    public event Action<long>? Paused;
    public event Action<long>? EndReached;
    public event Action<long, string>? Error;
    public event Action<long, long>? TimeChanged;
    public event Action<long, long>? LengthChanged;
    public event Action<long, string>? AudioDeviceChanged;

    public long Time => _current?.Player.Time ?? 0;
    public long Length => _current?.Player.Length ?? 0;

    public int Volume
    {
        get => _current?.Player.Volume ?? 100;
        set
        {
            if (_current is { } session)
            {
                session.Player.Volume = Math.Clamp(value, 0, 100);
            }
        }
    }

    public bool Muted
    {
        get => _current?.Player.Mute ?? false;
        set
        {
            if (_current is { } session)
            {
                session.Player.Mute = value;
            }
        }
    }

    public IReadOnlyList<AudioDeviceIdentity> EnumerateAudioDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    public async Task LoadAsync(string path, MediaKind mediaKind, long generation, AudioOutputPreference audioOutput)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("O arquivo selecionado não existe.", path);
        }

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var previous = Interlocked.Exchange(ref _current, null);
            if (previous is not null)
            {
                await previous.StopAndDisposeAsync().ConfigureAwait(false);
            }

            var effectiveAudioOutput = AudioPreferenceResolver.Resolve(audioOutput, EnumerateAudioDevices()) == AudioPreferenceAvailability.Available
                ? audioOutput
                : AudioOutputPreference.Default;
            var session = new Session(_libVlc, path, mediaKind, generation, effectiveAudioOutput, this);
            _current = session;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task ApplyAudioOutputAsync(AudioOutputPreference audioOutput)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var previous = Interlocked.Exchange(ref _current, null);
            if (previous is null)
            {
                return;
            }

            var path = previous.Path;
            var mediaKind = previous.MediaKind;
            var generation = previous.Generation;
            await previous.StopAndDisposeAsync().ConfigureAwait(false);
            _current = new Session(_libVlc, path, mediaKind, generation, audioOutput, this);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public bool Play()
    {
        var session = _current ?? throw new InvalidOperationException("Nenhuma mídia foi carregada.");
        if (session.Player.Time >= Math.Max(0, session.Player.Length - 250))
        {
            session.Player.Time = 0;
        }

        return session.Player.Play();
    }

    public void Pause() => _current?.Player.SetPause(true);

    public void Resume() => _current?.Player.SetPause(false);

    public void Seek(long milliseconds)
    {
        if (_current is { } session && session.Player.IsSeekable)
        {
            session.Player.Time = Math.Clamp(milliseconds, 0, Math.Max(0, session.Player.Length));
        }
    }

    public async Task StopAsync()
    {
        var session = _current;
        if (session is null)
        {
            return;
        }

        await Task.Run(() =>
        {
            session.Player.Stop();
            session.Player.Time = 0;
        }).ConfigureAwait(false);
    }

    public bool TryAcquireLatestFrame(long generation, out FrameBufferRing.FrameLease lease)
    {
        var session = _current;
        if (session is null || session.Generation != generation)
        {
            lease = default;
            return false;
        }

        return session.Buffers.TryAcquireLatest(out lease);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var session = Interlocked.Exchange(ref _current, null);
            if (session is not null)
            {
                await session.StopAndDisposeAsync().ConfigureAwait(false);
            }

            _libVlc.Dispose();
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }

    private sealed class Session
    {
        private readonly LibVlcPlaybackEngine _owner;
        private readonly MediaPlayer.LibVLCVideoLockCb? _lockCallback;
        private readonly MediaPlayer.LibVLCVideoDisplayCb? _displayCallback;
        private readonly MediaPlayer.LibVLCVideoFormatCb? _formatCallback;
        private readonly MediaPlayer.LibVLCVideoCleanupCb? _cleanupCallback;
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
            Buffers = new FrameBufferRing();
            Media = new Media(libVlc, new Uri(path));
            if (mediaKind == MediaKind.Audio)
            {
                Media.AddOption(":no-video");
            }

            Player = new MediaPlayer(libVlc) { Media = Media };
            if (audioOutput.IsExplicit)
            {
                if (!Player.SetAudioOutput(audioOutput.Module!))
                {
                    throw new InvalidOperationException($"O LibVLC não aceitou o módulo de áudio '{audioOutput.Module}'.");
                }

                Player.SetOutputDevice(audioOutput.DeviceId!, audioOutput.Module!);
            }

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

            Player.Playing += (_, _) => _owner.Playing?.Invoke(Generation);
            Player.Paused += (_, _) => _owner.Paused?.Invoke(Generation);
            Player.EndReached += (_, _) => _owner.EndReached?.Invoke(Generation);
            Player.EncounteredError += (_, _) => _owner.Error?.Invoke(Generation, "A mídia não pôde ser reproduzida.");
            Player.TimeChanged += (_, args) => _owner.TimeChanged?.Invoke(Generation, args.Time);
            Player.LengthChanged += (_, args) => _owner.LengthChanged?.Invoke(Generation, args.Length);
            Player.AudioDevice += (_, args) => _owner.AudioDeviceChanged?.Invoke(Generation, args.AudioDevice);
        }

        public string Path { get; }
        public MediaKind MediaKind { get; }
        public long Generation { get; }
        public Media Media { get; }
        public MediaPlayer Player { get; }
        public FrameBufferRing Buffers { get; }

        public async Task StopAndDisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await Task.Run(Player.Stop).ConfigureAwait(false);
            Player.Dispose();
            Media.Dispose();
            Buffers.Dispose();
        }

        private nint LockVideo(nint opaque, nint planes) => Buffers.AcquireWrite(planes);

        private void DisplayVideo(nint opaque, nint picture)
        {
            Buffers.Commit(picture);
            _owner.FrameReady?.Invoke(Generation);
        }

        private uint FormatVideo(
            ref nint opaque,
            nint chroma,
            ref uint width,
            ref uint height,
            ref uint pitches,
            ref uint lines)
        {
            Marshal.WriteByte(chroma, 0, (byte)'R');
            Marshal.WriteByte(chroma, 1, (byte)'V');
            Marshal.WriteByte(chroma, 2, (byte)'3');
            Marshal.WriteByte(chroma, 3, (byte)'2');
            pitches = Align32(checked(width * 4));
            lines = Align32(height);
            Buffers.Configure(width, height, pitches, lines);
            return 1;
        }

        private void CleanupVideo(ref nint opaque) => Buffers.Reset();

        private static uint Align32(uint value) => checked((value + 31u) & ~31u);
    }
}
