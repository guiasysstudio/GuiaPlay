using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GuiaPlay.Core;

namespace GuiaPlay.App.Models;

internal sealed class PlaylistGroupNode(PlaylistGroup group)
{
    public PlaylistGroup Group { get; } = group;
    public Guid Id => Group.Id;
    public string Name => Group.Name;
    public ObservableCollection<PlaylistItemNode> Items { get; } =
        new(group.Items.OrderBy(item => item.Order).Select(item => new PlaylistItemNode(group.Id, item)));
}

internal sealed class PlaylistItemNode(Guid groupId, PlaylistItem item) : INotifyPropertyChanged
{
    private PlaylistItemAvailability _availability;

    public Guid GroupId { get; } = groupId;
    public PlaylistItem Item { get; } = item;
    public string Name => Item.DisplayName;
    public string KindLabel => Item.Kind == MediaKind.Audio ? "Áudio" : "Vídeo";
    public bool IsAvailable => _availability is PlaylistItemAvailability.Unknown or PlaylistItemAvailability.Available;
    public string AvailabilityLabel => _availability switch
    {
        PlaylistItemAvailability.Missing => $"{KindLabel} · arquivo não encontrado",
        PlaylistItemAvailability.DecodeFailed => $"{KindLabel} · arquivo existe, mas não pôde ser decodificado",
        PlaylistItemAvailability.TimedOut => $"{KindLabel} · disponibilidade não confirmada",
        PlaylistItemAvailability.InvalidPath => $"{KindLabel} · caminho inválido",
        PlaylistItemAvailability.Unsupported => $"{KindLabel} · formato não suportado",
        _ => KindLabel
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetAvailability(MediaProbeStatus status)
    {
        SetAvailability(PlaylistItemAvailabilityPolicy.FromProbe(status));
    }

    public void SetDecodeFailure() => SetAvailability(PlaylistItemAvailability.DecodeFailed);

    private void SetAvailability(PlaylistItemAvailability availability)
    {
        if (_availability == availability)
        {
            return;
        }

        _availability = availability;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(AvailabilityLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
