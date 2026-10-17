using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using GuiaPlay.Core;

namespace GuiaPlay.App.Models;

/// <summary>
/// Row in playback order, not a separately sortable copy of the playlist.
/// All mutations go back through PlaylistCatalog and its atomic store.
/// </summary>
internal sealed class MediaGridRow : INotifyPropertyChanged
{
    private string _duration = "—";
    private string _resolution = "—";
    private string _date = "—";
    private ImageSource? _thumbnail;

    public MediaGridRow(int order, string groupName, PlaylistItemNode node)
    {
        Order = order;
        GroupName = groupName;
        Node = node;
        Node.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AvailabilityLabel));
    }

    public int Order { get; }
    public string OrderText => Order.ToString("D2", CultureInfo.CurrentCulture);
    public PlaylistItemNode Node { get; }
    public Guid GroupId => Node.GroupId;
    public Guid ItemId => Node.Item.Id;
    public string GroupName { get; }
    public string Name => Node.Name;
    public string FilePath => Node.Item.OriginalPath;
    public string Kind => Node.KindLabel;
    public string Glyph => Node.Item.Kind switch
    {
        MediaKind.Audio => "♫",
        MediaKind.YouTube => "▶",
        _ => "▶"
    };
    public string AvailabilityLabel => Node.AvailabilityLabel;
    public string Duration => _duration;
    public string Resolution => _resolution;
    public string Date => _date;
    public ImageSource? Thumbnail => _thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyMetadata(MediaGridMetadata metadata)
    {
        _duration = metadata.Duration;
        _resolution = metadata.Resolution;
        _date = metadata.Date;
        _thumbnail = metadata.Thumbnail;
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(Thumbnail));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed record MediaGridMetadata(
    string Duration,
    string Resolution,
    string Date,
    ImageSource? Thumbnail);
