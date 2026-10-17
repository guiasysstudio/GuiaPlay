using System.Globalization;
using GuiaPlay.Core;

namespace GuiaPlay.App.Models;

/// <summary>
/// Presentation-only metadata from the existing safe playlist preset store.
/// The displayed counters are computed from the persisted snapshot.
/// </summary>
internal sealed record SavedPlaylistEntry(PlaylistPreset Preset, bool IsActive, bool IsDirty)
{
    public Guid Id => Preset.Id;

    public string Name => Preset.Name;

    public int GroupCount => Preset.Playlist.Groups.Count;

    public int MediaCount => Preset.Playlist.Groups.Sum(group => group.Items.Count);

    public string Details =>
        $"{GroupCount} grupo(s) · {MediaCount} mídia(s) · {Preset.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.CurrentCulture)}";

    public string ActiveLabel => IsActive ? IsDirty ? "EDITANDO" : "ATIVA" : string.Empty;
}
