using GuiaPlay.Core;

namespace GuiaPlay.App.Services;

internal static class AudioOutputRuntimePolicy
{
    public static bool IsSameRoute(AudioOutputPreference left, AudioOutputPreference right)
    {
        if (left.Mode != right.Mode)
        {
            return false;
        }

        return left.Mode == AudioOutputMode.WindowsDefault ||
               string.Equals(left.Module, right.Module, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.DeviceId, right.DeviceId, StringComparison.Ordinal);
    }

    public static bool RequiresPlayerRecreation(
        AudioOutputPreference currentlyApplied,
        AudioOutputPreference requested) =>
        !IsSameRoute(currentlyApplied, requested);

    public static bool CanSaveSelection(
        AudioOutputPreference saved,
        AudioOutputPreference selected,
        bool selectedIsAvailable) =>
        selectedIsAvailable || IsSameRoute(saved, selected);

    public static bool CanApplySelection(AudioPreferenceAvailability availability) =>
        availability is AudioPreferenceAvailability.WindowsDefault or AudioPreferenceAvailability.Available;
}

internal static class SettingsAccessPolicy
{
    public static bool CanConfigureScreens(bool playbackActive, bool operatorRecoveryRequired) =>
        !playbackActive || operatorRecoveryRequired;

    public static bool CanOpenSettings(bool controlsBusy) => !controlsBusy;
}

internal static class MonitorRefreshPolicy
{
    // WM_DISPLAYCHANGE is the primary signal. Polling is only a safety net for drivers that do
    // not deliver it reliably, so hardware enumeration need not run every couple of seconds.
    public static TimeSpan SafetyPollInterval { get; } = TimeSpan.FromSeconds(15);
    public static TimeSpan DisplayChangeDebounceInterval { get; } = TimeSpan.FromMilliseconds(500);
}
