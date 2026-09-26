using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class DeviceSelectionTests
{
    [Fact]
    public void PersistentIdentityMatchesAfterDisplayOrderChanges()
    {
        var displays = new[]
        {
            new ConnectedDisplay("DISPLAY9", "path-b", false),
            new ConnectedDisplay("DISPLAY2", "path-a", true)
        };

        var match = DisplayPreferenceResolver.Match("path-b", displays);

        Assert.Equal(SavedDisplayMatchKind.Exact, match.Kind);
        Assert.Equal("DISPLAY9", match.Display!.SessionId);
    }

    [Fact]
    public void MissingAndAmbiguousIdentityNeverAutoApply()
    {
        Assert.Equal(
            SavedDisplayMatchKind.Missing,
            DisplayPreferenceResolver.Match("gone", [new("DISPLAY1", "present", true)]).Kind);
        Assert.Equal(
            SavedDisplayMatchKind.Ambiguous,
            DisplayPreferenceResolver.Match("duplicate", [new("A", "duplicate", true), new("B", "duplicate", false)]).Kind);
    }

    [Fact]
    public void OperatorIsAlwaysExcludedFromStartupOutputs()
    {
        var resolved = DisplayPreferenceResolver.ResolveStartupOutputs(
            ["operator", "public"],
            [new("A", "operator", true), new("B", "public", false)],
            "operator");

        Assert.DoesNotContain("operator", resolved);
        Assert.Contains("public", resolved);
    }

    [Fact]
    public void ChangingOperatorRemovesItWithoutSelectingFormerOperator()
    {
        var beforeChange = new[] { "public-a", "new-operator" };

        var afterChange = DisplayPreferenceResolver.ExcludeOperator(beforeChange, "new-operator");

        Assert.Contains("public-a", afterChange);
        Assert.DoesNotContain("new-operator", afterChange);
        Assert.DoesNotContain("former-operator", afterChange);
    }

    [Fact]
    public void ReconnectedDisplayRequiresExplicitAuthorization()
    {
        var authorization = new ReconnectAuthorization();
        authorization.MarkDisconnected("public");

        Assert.False(authorization.MayRestoreSavedSelection("public"));
        authorization.ExplicitlyAuthorize("public");
        Assert.True(authorization.MayRestoreSavedSelection("public"));
    }

    [Fact]
    public void AudioPreferenceUsesStableModuleAndDeviceIdentity()
    {
        var preference = new AudioOutputPreference(AudioOutputMode.Explicit, "mmdevice", "endpoint-a", "Old name");
        var devices = new[] { new AudioDeviceIdentity("mmdevice", "endpoint-a", "Renamed") };

        Assert.Equal(AudioPreferenceAvailability.Available, AudioPreferenceResolver.Resolve(preference, devices));
        Assert.Equal(AudioPreferenceAvailability.Missing, AudioPreferenceResolver.Resolve(
            preference,
            [new AudioDeviceIdentity("mmdevice", "endpoint-b", "Other")]));
        Assert.Equal(AudioPreferenceAvailability.Unknown, AudioPreferenceResolver.Resolve(preference, []));
        Assert.Equal(AudioPreferenceAvailability.WindowsDefault, AudioPreferenceResolver.Resolve(AudioOutputPreference.Default, []));
    }
}
