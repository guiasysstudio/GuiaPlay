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
    private bool? _isAvailable;

    public Guid GroupId { get; } = groupId;
    public PlaylistItem Item { get; } = item;
    public string Name => Item.DisplayName;
    public string KindLabel => Item.Kind == MediaKind.Audio ? "Áudio" : "Vídeo";
    public bool IsAvailable => _isAvailable != false;
    public string AvailabilityLabel => IsAvailable ? KindLabel : $"{KindLabel} · arquivo não encontrado";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetAvailability(bool available)
    {
        if (_isAvailable == available)
        {
            return;
        }

        _isAvailable = available;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(AvailabilityLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
