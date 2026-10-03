using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class PlaybackCoordinatorTests
{
    [Fact]
    public void VideoWithoutSelectedOutputCannotPlay()
    {
        var sut = new PlaybackCoordinator();
        sut.TryLoad(@"C:\video.mkv", MediaKind.Video, replacementConfirmed: true);

        Assert.False(sut.CanPlay(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void VideoWithOneOrMoreSelectedOutputsCanPlay(int outputCount)
    {
        var sut = new PlaybackCoordinator();
        sut.TryLoad(@"C:\video.webm", MediaKind.Video, replacementConfirmed: true);

        Assert.True(sut.CanPlay(outputCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AudioCanPlayIndependentlyFromSelectedOutputs(int outputCount)
    {
        var sut = new PlaybackCoordinator();
        sut.TryLoad(@"C:\audio.flac", MediaKind.Audio, replacementConfirmed: true);

        Assert.True(sut.CanPlay(outputCount));
        Assert.Equal(MediaKind.Audio, sut.MediaKind);
    }

    [Fact]
    public void AudioDoesNotPauseWhenVideoOutputsDisappear()
    {
        var sut = new PlaybackCoordinator();
        sut.TryLoad(@"C:\audio.ogg", MediaKind.Audio, replacementConfirmed: true);
        var generation = sut.BeginPlayback();
        sut.MarkPlaying(generation);

        var result = sut.PublicOutputsChanged(0);

        Assert.False(result.Accepted);
        Assert.False(result.PauseEngine);
        Assert.Equal(PlaybackStatus.Playing, sut.Status);
    }

    [Fact]
    public void AudioNeverUsesVideoOutputsEvenWhenScreensAreSelected()
    {
        Assert.False(PlaybackRouting.UsesVideoOutputs(MediaKind.Audio));
        Assert.True(PlaybackRouting.UsesVideoOutputs(MediaKind.Video));
    }

    [Fact]
    public void MissingMediaCannotPlay()
    {
        Assert.False(new PlaybackCoordinator().CanPlay(2));
    }

    [Fact]
    public void NaturalEndClosesOutputsButKeepsMediaLoaded()
    {
        var sut = PlayingCoordinator();

        var result = sut.NaturalEnd(sut.Generation);

        Assert.True(result.Accepted);
        Assert.True(result.CloseOutputs);
        Assert.Equal(PlaybackStatus.Ended, sut.Status);
        Assert.NotNull(sut.MediaPath);
    }

    [Fact]
    public void StopClosesOutputsAndKeepsMediaReadyForReplay()
    {
        var sut = PlayingCoordinator();

        var result = sut.Stop();

        Assert.True(result.CloseOutputs);
        Assert.Equal(PlaybackStatus.Stopped, sut.Status);
        Assert.Equal(sut.Generation, sut.BeginPlayback());
    }

    [Fact]
    public void FailedEngineStopBecomesExplicitErrorInsteadOfFalseStoppedState()
    {
        var sut = PlayingCoordinator();

        var result = sut.StopFailed();

        Assert.True(result.Accepted);
        Assert.True(result.CloseOutputs);
        Assert.Equal(PlaybackStatus.Error, sut.Status);
        Assert.NotNull(sut.MediaPath);
    }

    [Fact]
    public void PauseKeepsOutputsOpen()
    {
        var sut = PlayingCoordinator();

        var result = sut.Pause();

        Assert.True(result.Accepted);
        Assert.False(result.CloseOutputs);
        Assert.Equal(PlaybackStatus.Paused, sut.Status);
    }

    [Fact]
    public void CancelledReplacementPreservesCurrentMediaAndPlayback()
    {
        var sut = PlayingCoordinator();
        var original = sut.MediaPath;
        var generation = sut.Generation;

        var result = sut.TryLoad(@"C:\novo.mp4", replacementConfirmed: false);

        Assert.Equal(LoadDecision.Cancelled, result);
        Assert.Equal(original, sut.MediaPath);
        Assert.Equal(generation, sut.Generation);
        Assert.Equal(PlaybackStatus.Playing, sut.Status);
    }

    [Fact]
    public void OldEndEventCannotEndNewMedia()
    {
        var sut = PlayingCoordinator();
        var oldGeneration = sut.Generation;
        Assert.Equal(LoadDecision.Loaded, sut.TryLoad(@"C:\novo.mp4", replacementConfirmed: true));
        sut.BeginPlayback();
        sut.MarkPlaying(sut.Generation);

        var result = sut.NaturalEnd(oldGeneration);

        Assert.False(result.Accepted);
        Assert.Equal(PlaybackStatus.Playing, sut.Status);
    }

    [Fact]
    public void LosingEveryPublicOutputPausesButLosingOneDoesNot()
    {
        var sut = PlayingCoordinator();

        Assert.False(sut.PublicOutputsChanged(1).PauseEngine);
        var result = sut.PublicOutputsChanged(0);

        Assert.True(result.PauseEngine);
        Assert.Equal(PlaybackStatus.Paused, sut.Status);
    }

    [Fact]
    public void LosingEveryOutputWhilePreparingPausesAndLatePlayingEventIsIgnored()
    {
        var sut = new PlaybackCoordinator();
        sut.TryLoad(@"C:\video.mp4", replacementConfirmed: true);
        var generation = sut.BeginPlayback();

        var result = sut.PublicOutputsChanged(0);

        Assert.True(result.PauseEngine);
        Assert.False(sut.MarkPlaying(generation));
        Assert.Equal(PlaybackStatus.Paused, sut.Status);
    }

    [Fact]
    public void LosingRequiredAudioDevicePausesAndStaleNotificationIsIgnored()
    {
        var sut = PlayingCoordinator();
        var currentGeneration = sut.Generation;

        Assert.False(sut.RequiredDeviceLost(currentGeneration - 1).Accepted);
        Assert.Equal(PlaybackStatus.Playing, sut.Status);

        var result = sut.RequiredDeviceLost(currentGeneration);

        Assert.True(result.PauseEngine);
        Assert.Equal(PlaybackStatus.Paused, sut.Status);
    }

    [Fact]
    public void TenCriticalPlaybackStateCyclesCompleteWithoutStaleState()
    {
        var sut = new PlaybackCoordinator();

        for (var cycle = 0; cycle < 10; cycle++)
        {
            Assert.Equal(
                LoadDecision.Loaded,
                sut.TryLoad($@"C:\Midias\ciclo-{cycle}.mp4", MediaKind.Video, replacementConfirmed: true));
            var generation = sut.BeginPlayback();
            Assert.True(sut.MarkPlaying(generation));
            Assert.True(sut.Pause().Accepted);
            Assert.True(sut.Resume().Accepted);
            Assert.True(sut.Stop().Accepted);
            Assert.Equal(PlaybackStatus.Stopped, sut.Status);
        }

        Assert.Equal(10, sut.Generation);
    }

    private static PlaybackCoordinator PlayingCoordinator()
    {
        var sut = new PlaybackCoordinator();
        sut.TryLoad(@"C:\video.mp4", replacementConfirmed: true);
        var generation = sut.BeginPlayback();
        Assert.True(sut.MarkPlaying(generation));
        return sut;
    }
}
