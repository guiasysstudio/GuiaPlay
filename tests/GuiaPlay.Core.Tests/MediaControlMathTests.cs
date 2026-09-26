using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class MediaControlMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 50_000)]
    [InlineData(100, 100_000)]
    [InlineData(-20, 0)]
    [InlineData(120, 100_000)]
    public void TimelineClickIsProportionalAndClamped(double coordinate, long expected) =>
        Assert.Equal(expected, MediaControlBehavior.PositionFromPoint(coordinate, 100, 100_000));

    [Fact]
    public void ZeroDurationDoesNotDivideByZero() =>
        Assert.Equal(0, MediaControlBehavior.PositionFromPoint(50, 100, 0));

    [Theory]
    [InlineData(20_000, 120, 25_000)]
    [InlineData(20_000, -120, 15_000)]
    [InlineData(98_000, 120, 100_000)]
    [InlineData(2_000, -120, 0)]
    public void TimelineWheelMovesFiveSecondsAndClamps(long current, int delta, long expected) =>
        Assert.Equal(expected, MediaControlBehavior.AdjustPosition(current, 100_000, delta));

    [Theory]
    [InlineData(50, 120, 55)]
    [InlineData(50, -120, 45)]
    [InlineData(99, 120, 100)]
    [InlineData(1, -120, 0)]
    public void VolumeWheelUsesConsistentStepAndClamps(int current, int delta, int expected) =>
        Assert.Equal(expected, MediaControlBehavior.AdjustVolume(current, delta));

    [Fact]
    public void VolumeCalculationDoesNotChangeMuteState()
    {
        var result = MediaControlBehavior.AdjustVolume(50, muted: true, wheelDelta: 120);

        Assert.Equal(55, result.Volume);
        Assert.True(result.Muted);
    }
}
