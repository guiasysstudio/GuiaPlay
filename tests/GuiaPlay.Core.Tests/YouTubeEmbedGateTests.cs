using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class YouTubeEmbedGateTests
{
    [Theory]
    [InlineData(199, 270)]
    [InlineData(480, 199)]
    [InlineData(180, 180)]
    public void RejectsEmbedSmallerThanTwoHundredPixels(int width, int height)
    {
        var assessment = YouTubeEmbedGate.Assess(
            width, height, hasAppIdentification: true, isActuallyVisible: true,
            automaticPlayback: false, visibleViewportFraction: 1, otherAutoPlayingYouTubePlayersOnSameScreen: 0);
        Assert.False(assessment.Allowed);
        Assert.Contains("200", assessment.Reason);
    }

    [Fact]
    public void RequiresAppIdentityEvenWhenViewportLarge()
    {
        var assessment = YouTubeEmbedGate.Assess(640, 360,
            hasAppIdentification: false, isActuallyVisible: true,
            automaticPlayback: false, visibleViewportFraction: 1, otherAutoPlayingYouTubePlayersOnSameScreen: 0);
        Assert.False(assessment.Allowed);
        Assert.Contains("Referer", assessment.Reason);
    }

    [Theory]
    [InlineData(false, 1.0, 0)]
    [InlineData(true, 0.5, 0)]
    [InlineData(true, 0.1, 0)]
    [InlineData(true, 1.0, 1)]
    public void BlocksHiddenOrInvalidAutomaticPlayer(bool visible, double fraction, int another)
    {
        var assessment = YouTubeEmbedGate.Assess(640, 360,
            hasAppIdentification: true, isActuallyVisible: visible,
            automaticPlayback: true, visibleViewportFraction: fraction,
            otherAutoPlayingYouTubePlayersOnSameScreen: another);
        Assert.False(assessment.Allowed);
    }

    [Fact]
    public void VisibleSinglePlayerWithoutAutoPlayIsEligibleForBrowserValidation()
    {
        var assessment = YouTubeEmbedGate.Assess(480, 270,
            hasAppIdentification: true, isActuallyVisible: true,
            automaticPlayback: false, visibleViewportFraction: 1,
            otherAutoPlayingYouTubePlayersOnSameScreen: 0);
        Assert.True(assessment.Allowed);
        Assert.Null(assessment.Reason);
    }

    [Fact]
    public void EligibleGeometryDoesNotClaimFrameMirroringOrMultidisplaySync()
    {
        Assert.False(YouTubeEmbedGate.CanMirrorAsWpfFrames);
        Assert.False(YouTubeEmbedGate.GuaranteesMultipleOutputSynchronization);
    }
}
