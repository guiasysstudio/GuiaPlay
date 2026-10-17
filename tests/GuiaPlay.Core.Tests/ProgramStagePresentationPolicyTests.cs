using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class ProgramStagePresentationPolicyTests
{
    [Fact]
    public void EmptyPlaybackNeverClaimsPublicTransmission()
    {
        var state = ProgramStagePresentationPolicy.Resolve(
            PlaybackStatus.Empty, MediaKind.Unknown, 0, 0, frameReady: false);

        Assert.Equal("SEM TRANSMISSÃO", state.Badge);
        Assert.False(state.ShowFrame);
        Assert.Equal("AGUARDANDO", state.OperatorBadge);
    }

    [Fact]
    public void SelectedMonitorIsNotAnActiveProgramWindow()
    {
        var state = ProgramStagePresentationPolicy.Resolve(
            PlaybackStatus.Ready, MediaKind.Video, selectedPublicOutputs: 2,
            activePublicOutputs: 0, frameReady: true);

        Assert.False(state.ShowFrame);
        Assert.Equal("SEM TRANSMISSÃO", state.Badge);
        Assert.Contains("selecionada", state.Hint);
    }

    [Fact]
    public void ProgramWaitsForFirstDeliveredVideoFrame()
    {
        var state = ProgramStagePresentationPolicy.Resolve(
            PlaybackStatus.Preparing, MediaKind.Video, 1, 1, frameReady: false);

        Assert.False(state.ShowFrame);
        Assert.Equal("PREPARANDO", state.Badge);
        Assert.Equal("CARREGANDO", state.OperatorBadge);
    }

    [Theory]
    [InlineData(PlaybackStatus.Playing, "AO VIVO", "REPRODUZINDO")]
    [InlineData(PlaybackStatus.Paused, "PAUSADO", "PAUSADO")]
    public void RealPublicWindowAndFrameUseSharedPreview(
        PlaybackStatus status, string expectedBadge, string expectedOperator)
    {
        var state = ProgramStagePresentationPolicy.Resolve(
            status, MediaKind.Video, 1, 2, frameReady: true);

        Assert.True(state.ShowFrame);
        Assert.Equal(expectedBadge, state.Badge);
        Assert.Equal(expectedOperator, state.OperatorBadge);
        Assert.Contains("2 saída(s)", state.Hint);
    }

    [Fact]
    public void AudioOnlyNeverPretendsToDisplayVideoOnPublicMonitor()
    {
        var state = ProgramStagePresentationPolicy.Resolve(
            PlaybackStatus.Playing, MediaKind.Audio, 2, 0, frameReady: false);

        Assert.False(state.ShowFrame);
        Assert.Equal("SOMENTE ÁUDIO", state.Badge);
        Assert.Equal("REPRODUZINDO", state.OperatorBadge);
    }
}
