using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class OperatorShortcutPolicyTests
{
    [Theory]
    [InlineData(OperatorShortcutKey.F1, OperatorShortcutModifiers.None, OperatorShortcutAction.Help)]
    [InlineData(OperatorShortcutKey.Space, OperatorShortcutModifiers.None, OperatorShortcutAction.PlayPause)]
    [InlineData(OperatorShortcutKey.F5, OperatorShortcutModifiers.None, OperatorShortcutAction.Play)]
    [InlineData(OperatorShortcutKey.F6, OperatorShortcutModifiers.None, OperatorShortcutAction.Pause)]
    [InlineData(OperatorShortcutKey.F7, OperatorShortcutModifiers.None, OperatorShortcutAction.Stop)]
    [InlineData(OperatorShortcutKey.F8, OperatorShortcutModifiers.None, OperatorShortcutAction.Previous)]
    [InlineData(OperatorShortcutKey.F9, OperatorShortcutModifiers.None, OperatorShortcutAction.Next)]
    [InlineData(OperatorShortcutKey.F11, OperatorShortcutModifiers.None, OperatorShortcutAction.ToggleFullscreenPreview)]
    [InlineData(OperatorShortcutKey.Left, OperatorShortcutModifiers.Control, OperatorShortcutAction.SeekBackwardFiveSeconds)]
    [InlineData(OperatorShortcutKey.Right, OperatorShortcutModifiers.Control, OperatorShortcutAction.SeekForwardFiveSeconds)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Control, OperatorShortcutAction.OpenMedia)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Control | OperatorShortcutModifiers.Shift, OperatorShortcutAction.ImportFolder)]
    [InlineData(OperatorShortcutKey.S, OperatorShortcutModifiers.Control, OperatorShortcutAction.SavePlaylist)]
    [InlineData(OperatorShortcutKey.N, OperatorShortcutModifiers.Control, OperatorShortcutAction.NewPlaylist)]
    [InlineData(OperatorShortcutKey.F, OperatorShortcutModifiers.Control, OperatorShortcutAction.Search)]
    [InlineData(OperatorShortcutKey.L, OperatorShortcutModifiers.Control, OperatorShortcutAction.FocusPlaylists)]
    [InlineData(OperatorShortcutKey.M, OperatorShortcutModifiers.Control, OperatorShortcutAction.ToggleMute)]
    public void RecognizesOnlyPublishedBindings(
        OperatorShortcutKey key, OperatorShortcutModifiers modifiers, OperatorShortcutAction expected)
    {
        Assert.Equal(expected, OperatorShortcutPolicy.Resolve(
            key, modifiers, textEntryFocused: false, spaceHandledByControl: false, isRepeated: false));
    }

    [Fact]
    public void EveryDocumentedGestureIsUniqueAndHasFunctionalAction()
    {
        var bindings = OperatorShortcutPolicy.Bindings;
        Assert.Equal(bindings.Count, bindings.Select(binding => (binding.Key, binding.Modifiers)).Distinct().Count());
        Assert.All(bindings, binding =>
        {
            Assert.NotEqual(OperatorShortcutAction.None, binding.Action);
            Assert.False(string.IsNullOrWhiteSpace(binding.Gesture));
            Assert.False(string.IsNullOrWhiteSpace(binding.Description));
        });
        Assert.Equal(bindings.Count,
            Enum.GetValues<OperatorShortcutAction>().Count(action => action != OperatorShortcutAction.None));
    }

    [Theory]
    [InlineData(OperatorShortcutKey.Space, OperatorShortcutModifiers.None)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Control)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Control | OperatorShortcutModifiers.Shift)]
    [InlineData(OperatorShortcutKey.N, OperatorShortcutModifiers.Control)]
    [InlineData(OperatorShortcutKey.F, OperatorShortcutModifiers.Control)]
    [InlineData(OperatorShortcutKey.F5, OperatorShortcutModifiers.None)]
    [InlineData(OperatorShortcutKey.F11, OperatorShortcutModifiers.None)]
    public void TextEditorsKeepTheirOwnInput(
        OperatorShortcutKey key, OperatorShortcutModifiers modifiers)
    {
        Assert.Equal(OperatorShortcutAction.None,
            OperatorShortcutPolicy.Resolve(key, modifiers,
                textEntryFocused: true, spaceHandledByControl: false, isRepeated: false));
    }

    [Fact]
    public void F1StillOffersHelpWhileEditing()
    {
        Assert.Equal(OperatorShortcutAction.Help,
            OperatorShortcutPolicy.Resolve(
                OperatorShortcutKey.F1, OperatorShortcutModifiers.None,
                textEntryFocused: true, spaceHandledByControl: true, isRepeated: false));
    }

    [Fact]
    public void SpaceOnFocusedControlsIsNeverRepurposedToPlayback()
    {
        Assert.Equal(OperatorShortcutAction.None,
            OperatorShortcutPolicy.Resolve(
                OperatorShortcutKey.Space, OperatorShortcutModifiers.None,
                textEntryFocused: false, spaceHandledByControl: true, isRepeated: false));
        Assert.Equal(OperatorShortcutAction.PlayPause,
            OperatorShortcutPolicy.Resolve(
                OperatorShortcutKey.Space, OperatorShortcutModifiers.None,
                textEntryFocused: false, spaceHandledByControl: false, isRepeated: false));
    }

    [Theory]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Alt)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Windows)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Control | OperatorShortcutModifiers.Alt)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Control | OperatorShortcutModifiers.Windows)]
    [InlineData(OperatorShortcutKey.O, OperatorShortcutModifiers.Control | OperatorShortcutModifiers.Shift | OperatorShortcutModifiers.Alt)]
    [InlineData(OperatorShortcutKey.Space, OperatorShortcutModifiers.Shift)]
    [InlineData(OperatorShortcutKey.F5, OperatorShortcutModifiers.Control)]
    [InlineData(OperatorShortcutKey.F1, OperatorShortcutModifiers.Alt)]
    public void UnexpectedModifiersNeverHijackNativeOrSystemShortcuts(
        OperatorShortcutKey key, OperatorShortcutModifiers modifiers)
    {
        Assert.Equal(OperatorShortcutAction.None, OperatorShortcutPolicy.Resolve(
            key, modifiers, textEntryFocused: false, spaceHandledByControl: false, isRepeated: false));
    }

    [Fact]
    public void KeyRepeatsDoNotRepeatDestructiveOrTransportActions()
    {
        foreach (var binding in OperatorShortcutPolicy.Bindings)
        {
            Assert.Equal(OperatorShortcutAction.None, OperatorShortcutPolicy.Resolve(
                binding.Key, binding.Modifiers,
                textEntryFocused: false, spaceHandledByControl: false, isRepeated: true));
        }
    }

    [Fact]
    public void CustomBindingsReplaceOldDefaultsWithoutAddingHiddenDuplicates()
    {
        OperatorShortcutAssignment[] customized =
        [
            new(OperatorShortcutAction.PlayPause, OperatorShortcutKey.F5, OperatorShortcutModifiers.Control),
            new(OperatorShortcutAction.Play, OperatorShortcutKey.F5, OperatorShortcutModifiers.None)
        ];
        Assert.True(OperatorShortcutPolicy.TryValidateAssignments(customized, out var error), error);
        Assert.Equal(OperatorShortcutAction.PlayPause,
            OperatorShortcutPolicy.Resolve(OperatorShortcutKey.F5, OperatorShortcutModifiers.Control,
                false, false, false, customized));
        Assert.Equal(OperatorShortcutAction.None,
            OperatorShortcutPolicy.Resolve(OperatorShortcutKey.Space, OperatorShortcutModifiers.None,
                false, false, false, customized));
        Assert.Equal(OperatorShortcutAction.Play,
            OperatorShortcutPolicy.Resolve(OperatorShortcutKey.F5, OperatorShortcutModifiers.None,
                false, false, false, customized));
    }

    [Fact]
    public void DuplicateOrSystemWideShortcutsAreRejected()
    {
        OperatorShortcutAssignment[] duplicates =
        [
            new(OperatorShortcutAction.PlayPause, OperatorShortcutKey.F5, OperatorShortcutModifiers.None)
        ];
        Assert.False(OperatorShortcutPolicy.TryValidateAssignments(duplicates, out var err));
        Assert.False(string.IsNullOrWhiteSpace(err));

        OperatorShortcutAssignment[] invalid =
        [
            new(OperatorShortcutAction.Help, OperatorShortcutKey.O, OperatorShortcutModifiers.Alt)
        ];
        Assert.False(OperatorShortcutPolicy.TryValidateAssignments(invalid, out _));
        Assert.True(OperatorShortcutPolicy.TryValidateAssignments([], out _));
    }

    [Fact]
    public void ACommandCanBeDisabledWithoutChangingOtherDefaults()
    {
        OperatorShortcutAssignment[] customized =
        [
            new(OperatorShortcutAction.ToggleMute, OperatorShortcutKey.None, OperatorShortcutModifiers.None)
        ];
        Assert.True(OperatorShortcutPolicy.TryValidateAssignments(customized, out _));
        Assert.Equal(OperatorShortcutAction.None,
            OperatorShortcutPolicy.Resolve(OperatorShortcutKey.M, OperatorShortcutModifiers.Control,
                false, false, false, customized));
        Assert.Equal(OperatorShortcutAction.SavePlaylist,
            OperatorShortcutPolicy.Resolve(OperatorShortcutKey.S, OperatorShortcutModifiers.Control,
                false, false, false, customized));
    }

    [Fact]
    public void UnknownOrUnassignedKeysDoNothing()
    {
        Assert.Equal(OperatorShortcutAction.None,
            OperatorShortcutPolicy.Resolve(
                OperatorShortcutKey.None, OperatorShortcutModifiers.None,
                textEntryFocused: false, spaceHandledByControl: false, isRepeated: false));
        Assert.Equal(OperatorShortcutAction.None,
            OperatorShortcutPolicy.Resolve(
                OperatorShortcutKey.M, OperatorShortcutModifiers.None,
                textEntryFocused: false, spaceHandledByControl: false, isRepeated: false));
    }
}
