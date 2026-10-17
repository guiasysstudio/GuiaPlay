using GuiaPlay.App.Models;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.App.Tests;

public sealed class MediaGridRowTests
{
    [Fact]
    public void GridRowReportsTrueGroupIdentityAndPlaybackOrder()
    {
        var group = Guid.NewGuid();
        var item = new PlaylistItem(Guid.NewGuid(), "Abertura", @"C:\Midias\abertura.mp4", MediaKind.Video, 0);
        var row = new MediaGridRow(3, "Culto Domingo", new PlaylistItemNode(group, item));

        Assert.Equal("03", row.OrderText);
        Assert.Equal("Culto Domingo", row.GroupName);
        Assert.Equal(group, row.GroupId);
        Assert.Equal(item.Id, row.ItemId);
        Assert.Equal(item.OriginalPath, row.FilePath);
        Assert.Equal("Vídeo", row.Kind);
        Assert.Equal("—", row.Resolution);
        Assert.Equal("—", row.Duration);
    }

    [Fact]
    public void MetadataAndFileAvailabilityNotifyTheGrid()
    {
        var item = new PlaylistItem(Guid.NewGuid(), "Hino", @"C:\Midias\hino.mp3", MediaKind.Audio, 0);
        var node = new PlaylistItemNode(Guid.NewGuid(), item);
        var row = new MediaGridRow(1, "Louvor", node);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.ApplyMetadata(new MediaGridMetadata("03:16", "—", "16/10/2026", null));
        node.SetAvailability(MediaProbeStatus.Missing);

        Assert.Equal("03:16", row.Duration);
        Assert.Equal("16/10/2026", row.Date);
        Assert.Null(row.Thumbnail);
        Assert.Contains(nameof(MediaGridRow.Duration), changed);
        Assert.Contains(nameof(MediaGridRow.Resolution), changed);
        Assert.Contains(nameof(MediaGridRow.Date), changed);
        Assert.Contains(nameof(MediaGridRow.AvailabilityLabel), changed);
        Assert.Contains("não encontrado", row.AvailabilityLabel);
    }
}
