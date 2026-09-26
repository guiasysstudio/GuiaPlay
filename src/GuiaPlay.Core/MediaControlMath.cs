namespace GuiaPlay.Core;

public readonly record struct VolumeWheelResult(int Volume, bool Muted);

public static class MediaControlBehavior
{
    public const int VolumeWheelStep = 5;
    public const long TimelineWheelStepMilliseconds = 5_000;

    public static int AdjustVolume(int current, int wheelDelta) =>
        Math.Clamp(current + WheelDirection(wheelDelta) * VolumeWheelStep, 0, 100);

    public static VolumeWheelResult AdjustVolume(int current, bool muted, int wheelDelta) =>
        new(AdjustVolume(current, wheelDelta), muted);

    public static long PositionFromPoint(double coordinate, double controlWidth, long durationMilliseconds)
    {
        if (controlWidth <= 0 || durationMilliseconds <= 0 || double.IsNaN(coordinate))
        {
            return 0;
        }

        var ratio = Math.Clamp(coordinate / controlWidth, 0d, 1d);
        return (long)Math.Round(durationMilliseconds * ratio, MidpointRounding.AwayFromZero);
    }

    public static long AdjustPosition(long currentMilliseconds, long durationMilliseconds, int wheelDelta)
    {
        if (durationMilliseconds <= 0 || wheelDelta == 0)
        {
            return Math.Clamp(currentMilliseconds, 0, Math.Max(0, durationMilliseconds));
        }

        return Math.Clamp(
            currentMilliseconds + WheelDirection(wheelDelta) * TimelineWheelStepMilliseconds,
            0,
            durationMilliseconds);
    }

    private static int WheelDirection(int wheelDelta) => Math.Sign(wheelDelta);
}
