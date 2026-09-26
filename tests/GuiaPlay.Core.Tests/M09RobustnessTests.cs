using System.Diagnostics;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class PlaybackRobustnessTests
{
    public static TheoryData<PlaybackCallbackKind> CallbackKinds => new(Enum.GetValues<PlaybackCallbackKind>());

    [Theory]
    [MemberData(nameof(CallbackKinds))]
    public void EveryCallbackFromAnOldGenerationIsRejected(PlaybackCallbackKind callback) =>
        Assert.False(PlaybackCallbackPolicy.ShouldAccept(callback, 10, 11, PlaybackStatus.Playing));

    [Fact]
    public void LateCallbacksCannotChangeStateAfterRapidSwitch()
    {
        var coordinator = new PlaybackCoordinator();
        coordinator.TryLoad(@"C:\media-a.mp4", MediaKind.Video, true);
        var oldGeneration = coordinator.BeginPlayback();
        Assert.True(coordinator.MarkPlaying(oldGeneration));

        coordinator.TryLoad(@"C:\media-b.mp4", MediaKind.Video, true);
        var currentGeneration = coordinator.BeginPlayback();
        Assert.True(coordinator.MarkPlaying(currentGeneration));

        Assert.False(coordinator.NaturalEnd(oldGeneration).Accepted);
        Assert.False(coordinator.PlaybackFailed(oldGeneration).Accepted);
        Assert.False(coordinator.AcceptsCallback(PlaybackCallbackKind.TimeChanged, oldGeneration));
        Assert.False(coordinator.AcceptsCallback(PlaybackCallbackKind.LengthChanged, oldGeneration));
        Assert.False(coordinator.AcceptsCallback(PlaybackCallbackKind.FrameReady, oldGeneration));
        Assert.False(coordinator.AcceptsCallback(PlaybackCallbackKind.AudioDeviceChanged, oldGeneration));
        Assert.Equal(PlaybackStatus.Playing, coordinator.Status);
        Assert.Equal(@"C:\media-b.mp4", coordinator.MediaPath);
    }

    [Fact]
    public void HundredLogicalMediaSwitchesRemainDeterministic()
    {
        var coordinator = new PlaybackCoordinator();
        for (var index = 0; index < 100; index++)
        {
            Assert.Equal(
                LoadDecision.Loaded,
                coordinator.TryLoad($@"C:\media-{index}.mp4", MediaKind.Video, replacementConfirmed: true));
            var generation = coordinator.BeginPlayback();
            Assert.True(coordinator.MarkPlaying(generation));
            Assert.True(coordinator.Stop().Accepted);
        }

        Assert.Equal(100, coordinator.Generation);
        Assert.Equal(PlaybackStatus.Stopped, coordinator.Status);
        Assert.Equal(@"C:\media-99.mp4", coordinator.MediaPath);
    }

    [Fact]
    public void LateErrorAfterStopDoesNotReplaceStoppedState()
    {
        var coordinator = new PlaybackCoordinator();
        coordinator.TryLoad(@"C:\video.mp4", MediaKind.Video, true);
        var generation = coordinator.BeginPlayback();
        coordinator.MarkPlaying(generation);
        coordinator.Stop();

        Assert.False(coordinator.PlaybackFailed(generation).Accepted);
        Assert.Equal(PlaybackStatus.Stopped, coordinator.Status);
    }

    [Fact]
    public void PlaybackErrorKeepsApplicationLogicReusableForAnotherMedia()
    {
        var coordinator = new PlaybackCoordinator();
        coordinator.TryLoad(@"C:\conteudo-invalido.mp4", MediaKind.Video, true);

        var failure = coordinator.PlaybackFailed(coordinator.Generation);

        Assert.True(failure.Accepted);
        Assert.True(failure.CloseOutputs);
        Assert.Equal(PlaybackStatus.Error, coordinator.Status);
        Assert.Equal(LoadDecision.Loaded, coordinator.TryLoad(@"C:\audio-valido.mp3", MediaKind.Audio, true));
        Assert.Equal(PlaybackStatus.Ready, coordinator.Status);
        Assert.True(coordinator.CanPlay(0));
    }

    [Fact]
    public void VolumeChangeWhileMutedIsPreservedWhenUnmuted()
    {
        var state = MediaControlBehavior.SetVolume(80, muted: false);
        state = MediaControlBehavior.ToggleMute(state.Volume, state.Muted);
        state = MediaControlBehavior.SetVolume(50, state.Muted);
        state = MediaControlBehavior.ToggleMute(state.Volume, state.Muted);

        Assert.Equal(new VolumeState(50, false), state);
        Assert.Equal(0, MediaControlBehavior.SetVolume(-1, false).Volume);
        Assert.Equal(100, MediaControlBehavior.SetVolume(101, false).Volume);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(999, 500)]
    public void ShortAndUnknownTimelineValuesAreAlwaysClamped(long duration, long expected)
    {
        Assert.Equal(expected, MediaControlBehavior.PositionFromPoint(50, 100, duration));
        Assert.InRange(MediaControlBehavior.AdjustPosition(long.MaxValue, duration, 120), 0, Math.Max(0, duration));
    }
}

public sealed class DiagnosticsTests
{
    [Fact]
    public void DiagnosticReportContainsMetricsAndRedactsFullMediaPath()
    {
        var snapshot = new DiagnosticSnapshot(
            "0.9.0-prototipo",
            "Windows Test",
            TimeSpan.FromHours(2),
            new ProcessResourceSnapshot(100 * 1024 * 1024, 80 * 1024 * 1024, 12 * 1024 * 1024, 3, 2, 1, 4.25),
            "Playing",
            @"C:\Users\Andrew\Vídeos\abertura.mp4",
            MediaKind.Video,
            2,
            120,
            100,
            20,
            0.321,
            7,
            "UpToDate");

        var report = DiagnosticReportFormatter.Format(snapshot);

        Assert.Contains("GuiaPlay 0.9.0-prototipo", report);
        Assert.Contains("Media: abertura.mp4", report);
        Assert.Contains("Frames replaced: 20", report);
        Assert.Contains("Update state: UpToDate", report);
        Assert.DoesNotContain(@"C:\Users\Andrew\Vídeos", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPU:", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionDiagnosticsKeepCumulativeCountersAndOnlyFileName()
    {
        var diagnostics = new SessionDiagnostics();
        diagnostics.RecordMediaChanged();
        diagnostics.RecordMediaChanged();
        diagnostics.RecordFrameReceived();
        diagnostics.RecordFrameReceived();
        diagnostics.RecordFrameRendered(TimeSpan.FromMilliseconds(2));

        var snapshot = diagnostics.Capture(
            new ProcessResourceSnapshot(1, 2, 3, 4, 5, 6, null),
            PlaybackStatus.Playing,
            @"C:\Users\Pessoa\Culto\Abertura João.mp4",
            MediaKind.Video,
            1,
            "UpdateAvailable");

        Assert.Equal("Abertura João.mp4", snapshot.MediaName);
        Assert.Equal(2, snapshot.MediaSwitches);
        Assert.Equal(2, snapshot.FramesReceived);
        Assert.Equal(1, snapshot.FramesRendered);
        Assert.Equal(1, snapshot.FramesReplaced);
        Assert.Equal(2, snapshot.AverageFrameCopyMilliseconds, 3);
    }

    [Fact]
    public void ProcessMetricsSamplerReturnsNonNegativeLightweightSnapshot()
    {
        var sampler = new ProcessMetricsSampler();
        var first = sampler.Sample();
        var second = sampler.Sample();

        Assert.True(first.WorkingSetBytes > 0);
        Assert.True(first.PrivateMemoryBytes > 0);
        Assert.True(first.ManagedMemoryBytes >= 0);
        Assert.InRange(second.CpuPercent ?? 0, 0, 100);
    }
}

public sealed class BoundedTextLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.M09.LogTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RotationKeepsBoundedNumberOfFiles()
    {
        var path = Path.Combine(_directory, "GuiaPlay.log");
        var log = new BoundedTextLog(path, 1024, maximumFiles: 3);
        for (var index = 0; index < 7; index++)
        {
            log.Append(new string((char)('a' + index), 1100));
        }

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.True(Directory.GetFiles(_directory, "GuiaPlay-*.log").Length <= 2);
        Assert.True(Directory.GetFiles(_directory, "*.log").Length <= 3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public sealed class NotificationDebouncerTests
{
    [Fact]
    public void BurstNotificationsAreCollapsedAndLaterNotificationIsAccepted()
    {
        var time = new ManualTimeProvider();
        var debouncer = new NotificationDebouncer(TimeSpan.FromMilliseconds(500), time);

        Assert.True(debouncer.TryAccept());
        Assert.False(debouncer.TryAccept());
        time.Advance(TimeSpan.FromMilliseconds(501));
        Assert.True(debouncer.TryAccept());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan value) => _timestamp += value.Ticks;
    }
}

public sealed class MediaFileProbeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.M09.ProbeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidMissingAndUnsupportedFilesAreClassifiedWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        var valid = Path.Combine(_directory, "válido.mp4");
        await File.WriteAllTextAsync(valid, "fixture");

        Assert.Equal(MediaProbeStatus.Available, await MediaFileProbe.ProbeAsync(valid, TimeSpan.FromSeconds(1)));
        Assert.Equal(MediaProbeStatus.Missing, await MediaFileProbe.ProbeAsync(Path.Combine(_directory, "ausente.mp4"), TimeSpan.FromSeconds(1)));
        Assert.Equal(MediaProbeStatus.Unsupported, await MediaFileProbe.ProbeAsync(Path.Combine(_directory, "script.ps1"), TimeSpan.FromSeconds(1)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
