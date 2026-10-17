namespace GuiaPlay.Core;

[Flags]
public enum OperatorShortcutModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8
}

public enum OperatorShortcutKey
{
    None,
    Space,
    F1,
    F5,
    F6,
    F7,
    F8,
    F9,
    F11,
    Left,
    Right,
    O,
    S,
    N,
    F,
    L,
    M
}

public enum OperatorShortcutAction
{
    None,
    Help,
    PlayPause,
    Play,
    Pause,
    Stop,
    Previous,
    Next,
    SeekBackwardFiveSeconds,
    SeekForwardFiveSeconds,
    ToggleFullscreenPreview,
    OpenMedia,
    ImportFolder,
    SavePlaylist,
    NewPlaylist,
    Search,
    FocusPlaylists,
    ToggleMute
}

public sealed record OperatorShortcutBinding(
    OperatorShortcutKey Key,
    OperatorShortcutModifiers Modifiers,
    OperatorShortcutAction Action,
    string Gesture,
    string Description);


/// <summary>One user override; defaults remain intact for commands not customized.</summary>
public sealed record OperatorShortcutAssignment(
    OperatorShortcutAction Action,
    OperatorShortcutKey Key,
    OperatorShortcutModifiers Modifiers);

/// <summary>
/// Window-scoped operator shortcuts. No global hooks or input simulation.
/// Keystrokes inside editors remain untouched; Space on native controls stays native.
/// </summary>
public static class OperatorShortcutPolicy
{
    public static IReadOnlyList<OperatorShortcutBinding> Bindings { get; } =
        Array.AsReadOnly(new[]
        {
            new OperatorShortcutBinding(OperatorShortcutKey.F1, OperatorShortcutModifiers.None,
                OperatorShortcutAction.Help, "F1", "Mostrar esta lista de atalhos"),
            new OperatorShortcutBinding(OperatorShortcutKey.Space, OperatorShortcutModifiers.None,
                OperatorShortcutAction.PlayPause, "Espaço", "Reproduzir / pausar a mídia"),
            new OperatorShortcutBinding(OperatorShortcutKey.F5, OperatorShortcutModifiers.None,
                OperatorShortcutAction.Play, "F5", "Reproduzir ou continuar"),
            new OperatorShortcutBinding(OperatorShortcutKey.F6, OperatorShortcutModifiers.None,
                OperatorShortcutAction.Pause, "F6", "Pausar reprodução"),
            new OperatorShortcutBinding(OperatorShortcutKey.F7, OperatorShortcutModifiers.None,
                OperatorShortcutAction.Stop, "F7", "Parar reprodução"),
            new OperatorShortcutBinding(OperatorShortcutKey.F8, OperatorShortcutModifiers.None,
                OperatorShortcutAction.Previous, "F8", "Carregar mídia anterior (não reproduz automaticamente)"),
            new OperatorShortcutBinding(OperatorShortcutKey.F9, OperatorShortcutModifiers.None,
                OperatorShortcutAction.Next, "F9", "Carregar próxima mídia (não reproduz automaticamente)"),
            new OperatorShortcutBinding(OperatorShortcutKey.Left, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.SeekBackwardFiveSeconds, "Ctrl + ←", "Retroceder 5 segundos"),
            new OperatorShortcutBinding(OperatorShortcutKey.Right, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.SeekForwardFiveSeconds, "Ctrl + →", "Avançar 5 segundos"),
            new OperatorShortcutBinding(OperatorShortcutKey.F11, OperatorShortcutModifiers.None,
                OperatorShortcutAction.ToggleFullscreenPreview, "F11", "Prévia do operador em tela cheia"),
            new OperatorShortcutBinding(OperatorShortcutKey.O, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.OpenMedia, "Ctrl + O", "Abrir vídeo ou áudio local"),
            new OperatorShortcutBinding(OperatorShortcutKey.O,
                OperatorShortcutModifiers.Control | OperatorShortcutModifiers.Shift,
                OperatorShortcutAction.ImportFolder, "Ctrl + Shift + O", "Importar pasta e subpastas"),
            new OperatorShortcutBinding(OperatorShortcutKey.S, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.SavePlaylist, "Ctrl + S", "Salvar playlist"),
            new OperatorShortcutBinding(OperatorShortcutKey.N, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.NewPlaylist, "Ctrl + N", "Nova playlist (com confirmação)"),
            new OperatorShortcutBinding(OperatorShortcutKey.F, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.Search, "Ctrl + F", "Pesquisar mídias na playlist"),
            new OperatorShortcutBinding(OperatorShortcutKey.L, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.FocusPlaylists, "Ctrl + L", "Selecionar biblioteca de playlists"),
            new OperatorShortcutBinding(OperatorShortcutKey.M, OperatorShortcutModifiers.Control,
                OperatorShortcutAction.ToggleMute, "Ctrl + M", "Silenciar / ativar áudio")
        });

    public static OperatorShortcutAction Resolve(
        OperatorShortcutKey key,
        OperatorShortcutModifiers modifiers,
        bool textEntryFocused,
        bool spaceHandledByControl,
        bool isRepeated,
        IReadOnlyList<OperatorShortcutAssignment>? assignments = null)
    {
        if (key == OperatorShortcutKey.None || isRepeated ||
            (modifiers & (OperatorShortcutModifiers.Windows | OperatorShortcutModifiers.Alt)) != 0)
        {
            return OperatorShortcutAction.None;
        }

        if (textEntryFocused && key != OperatorShortcutKey.F1)
        {
            return OperatorShortcutAction.None;
        }

        if (spaceHandledByControl && key == OperatorShortcutKey.Space)
        {
            return OperatorShortcutAction.None;
        }

        // Override by action (rather than adding shortcuts): moving an action
        // must never leave the old key active as a hidden duplicate.
        foreach (var shortcut in Bindings)
        {
            var configured = assignments?.FirstOrDefault(a => a.Action == shortcut.Action);
            var gestureKey = configured?.Key ?? shortcut.Key;
            var gestureModifiers = configured?.Modifiers ?? shortcut.Modifiers;
            if (gestureKey == key && gestureModifiers == modifiers)
            {
                return shortcut.Action;
            }
        }

        return OperatorShortcutAction.None;
    }

    public static bool TryValidateAssignments(
        IReadOnlyList<OperatorShortcutAssignment>? overrides,
        out string? error)
    {
        error = null;
        if (overrides is null || overrides.Count == 0) return true;
        var actions = Bindings.Select(b => b.Action).ToHashSet();
        var usedActions = new HashSet<OperatorShortcutAction>();
        var usedKeys = new HashSet<(OperatorShortcutKey, OperatorShortcutModifiers)>();
        foreach (var item in overrides)
        {
            if (!actions.Contains(item.Action) || !usedActions.Add(item.Action) ||
                !Enum.IsDefined(item.Key) ||
                (item.Modifiers & ~(OperatorShortcutModifiers.Control | OperatorShortcutModifiers.Shift)) != 0)
            {
                error = "Um atalho possui ação, tecla ou modificador inválido.";
                return false;
            }
        }

        foreach (var binding in Bindings)
        {
            var custom = overrides.FirstOrDefault(o => o.Action == binding.Action);
            var gestureKey = custom?.Key ?? binding.Key;
            var gestureMods = custom?.Modifiers ?? binding.Modifiers;
            if (gestureKey == OperatorShortcutKey.None) continue; // Disable this command's keyboard gesture.
            if (!usedKeys.Add((gestureKey, gestureMods)))
            {
                error = $"A combinação {gestureMods} + {gestureKey} está atribuída a mais de uma ação.";
                return false;
            }
        }

        return true;
    }
}
