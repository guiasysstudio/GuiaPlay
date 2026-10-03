using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using GuiaPlay.App.Models;
using GuiaPlay.App.Services;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.App.Tests;

public sealed class AudioOutputRuntimePolicyTests
{
    private static readonly AudioOutputPreference Saved =
        new(AudioOutputMode.Explicit, "mmdevice", "endpoint-1", "Mesa USB");

    [Fact]
    public void SameAppliedRouteDoesNotRequirePlayerRecreation()
    {
        var sameRouteWithNewLabel = Saved with { Module = "MMDEVICE", DisplayName = "Novo nome" };

        Assert.False(AudioOutputRuntimePolicy.RequiresPlayerRecreation(Saved, sameRouteWithNewLabel));
        Assert.True(AudioOutputRuntimePolicy.RequiresPlayerRecreation(Saved, AudioOutputPreference.Default));
        Assert.True(AudioOutputRuntimePolicy.RequiresPlayerRecreation(
            Saved,
            Saved with { DeviceId = "endpoint-2" }));
    }

    [Fact]
    public void UnavailableSavedDeviceCanBePreservedButNotChangedToAnotherUnavailableDevice()
    {
        Assert.True(AudioOutputRuntimePolicy.CanSaveSelection(Saved, Saved, selectedIsAvailable: false));
        Assert.False(AudioOutputRuntimePolicy.CanSaveSelection(
            Saved,
            Saved with { DeviceId = "endpoint-ausente" },
            selectedIsAvailable: false));
        Assert.True(AudioOutputRuntimePolicy.CanSaveSelection(
            Saved,
            AudioOutputPreference.Default,
            selectedIsAvailable: true));
    }

    [Theory]
    [InlineData(AudioPreferenceAvailability.WindowsDefault, true)]
    [InlineData(AudioPreferenceAvailability.Available, true)]
    [InlineData(AudioPreferenceAvailability.Unknown, false)]
    [InlineData(AudioPreferenceAvailability.Missing, false)]
    public void OnlyConfirmedRoutesCanBeAppliedWithoutSilentFallback(
        AudioPreferenceAvailability availability,
        bool expected) =>
        Assert.Equal(expected, AudioOutputRuntimePolicy.CanApplySelection(availability));
}

public sealed class SettingsAccessPolicyTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ScreenChangesAreAllowedOnlyWhenIdleOrRecoveringLostOperator(
        bool playbackActive,
        bool operatorRecoveryRequired,
        bool expected) =>
        Assert.Equal(expected, SettingsAccessPolicy.CanConfigureScreens(playbackActive, operatorRecoveryRequired));

    [Fact]
    public void BusyControlsRemainLockedEvenDuringOperatorRecovery() =>
        Assert.False(SettingsAccessPolicy.CanOpenSettings(controlsBusy: true));

    [Fact]
    public void SettingsRemainOpenableDuringPlaybackWhileDangerousPagesStayLocked()
    {
        Assert.True(SettingsAccessPolicy.CanOpenSettings(controlsBusy: false));
        Assert.False(SettingsAccessPolicy.CanConfigureScreens(
            playbackActive: true,
            operatorRecoveryRequired: false));
    }
}

public sealed class MonitorTopologyReconcilerTests
{
    [Fact]
    public void PersistentMonitorSurvivesSessionIdGeometryDpiAndNegativeCoordinateChanges()
    {
        var previous = Monitor("old-session", "display-path-a", left: 1920, top: 0, width: 1920, height: 1080, dpi: 96);
        var current = Monitor("new-session", "DISPLAY-PATH-A", left: -1440, top: -200, width: 1440, height: 2560, dpi: 144);

        var reconciliation = Assert.Single(MonitorTopologyReconciler.Reconcile([previous], [current]));

        Assert.True(reconciliation.IsConnected);
        Assert.True(reconciliation.NeedsReposition);
        Assert.Same(current, reconciliation.Current);
    }

    [Fact]
    public void ReconciliationRemovesOnlyDisconnectedOutputsAndKeepsUnaffectedOnes()
    {
        var kept = Monitor("session-a", "path-a", 0, 0, 1920, 1080, 96);
        var removed = Monitor("session-b", "path-b", 1920, 0, 1920, 1080, 96);
        var keptCurrent = kept with { Id = "session-a2" };

        var result = MonitorTopologyReconciler.Reconcile([kept, removed], [keptCurrent]);

        Assert.True(result[0].IsConnected);
        Assert.False(result[1].IsConnected);
    }

    [Fact]
    public void AmbiguousPersistentIdentityIsNotMatchedArbitrarily()
    {
        var previous = Monitor("old", "path-a", 0, 0, 1, 1, 96);
        var first = Monitor("first", "path-a", 0, 0, 1, 1, 96);
        var second = Monitor("second", "path-a", 1, 0, 1, 1, 96);

        Assert.Null(MonitorTopologyReconciler.FindCurrent(previous, [first, second]));
    }

    [Fact]
    public void SwappedSessionIdsStillMapEachOutputToItsPhysicalMonitor()
    {
        var previousA = Monitor("session-1", "path-a", 0, 0, 1920, 1080, 96);
        var previousB = Monitor("session-2", "path-b", 1920, 0, 1920, 1080, 96);
        var currentA = Monitor("session-2", "path-a", -1920, 0, 1920, 1080, 96);
        var currentB = Monitor("session-1", "path-b", 0, 0, 1920, 1080, 96);

        var result = MonitorTopologyReconciler.Reconcile([previousA, previousB], [currentA, currentB]);

        Assert.Same(currentA, result[0].Current);
        Assert.Same(currentB, result[1].Current);
        Assert.NotEqual(result[0].Current!.Id, result[1].Current!.Id);
    }

    [Fact]
    public void SafetyPollingIsSpacedBecauseDisplayChangeIsPrimarySignal() =>
        Assert.True(MonitorRefreshPolicy.SafetyPollInterval >= TimeSpan.FromSeconds(10));

    private static MonitorInfo Monitor(
        string id,
        string? persistentId,
        int left,
        int top,
        int width,
        int height,
        uint dpi) =>
        new(
            nint.Zero,
            id,
            persistentId,
            null,
            persistentId is not null,
            left,
            top,
            width,
            height,
            left,
            top,
            width,
            height,
            false,
            dpi,
            dpi);
}

public sealed class SafeProcessLauncherTests
{
    [Fact]
    public void ShellFailureIsReturnedInsteadOfEscapingToDispatcher()
    {
        var result = SafeProcessLauncher.TryStart(
            new ProcessStartInfo("https://example.invalid") { UseShellExecute = true },
            _ => throw new Win32Exception("sem navegador"));

        Assert.False(result.Succeeded);
        Assert.Contains("sem navegador", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullProcessIsReportedAsFailure()
    {
        var result = SafeProcessLauncher.TryStart(new ProcessStartInfo("anything"), _ => null);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [MemberData(nameof(RecoverableLaunchFailures))]
    public void RecoverableShellFailuresNeverEscape(Exception exception)
    {
        var result = SafeProcessLauncher.TryStart(
            new ProcessStartInfo("anything"),
            _ => throw exception);

        Assert.False(result.Succeeded);
        Assert.Equal(exception.Message, result.Error);
    }

    public static TheoryData<Exception> RecoverableLaunchFailures => new()
    {
        new ArgumentException("destino inv\u00e1lido"),
        new SecurityException("execu\u00e7\u00e3o bloqueada")
    };
}
