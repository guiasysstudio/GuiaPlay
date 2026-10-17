using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class YouTubePlaylistIntegrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "GuiaPlay-GP-I09", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LinkIsStoredAsYouTubeKindAndReloadedWithoutFilesystemPathRewriting()
    {
        Directory.CreateDirectory(_folder);
        var video = Assert.IsType<YouTubeVideoReference>(
            YouTubeVideoReferenceParser.TryParse(
                "https://youtu.be/M7lc1UVf-VE?t=16", out var link) ? link : null);
        var groupId = Guid.NewGuid();
        var catalog = new PlaylistCatalog(new PlaylistDocument(
            PlaylistDocument.CurrentSchemaVersion,
            [new PlaylistGroup(groupId, "YouTube", 0, [])]));
        var item = catalog.AddYouTubeItem(groupId, video, "Apresentação");

        Assert.Equal(MediaKind.YouTube, item.Kind);
        Assert.Equal("Apresentação", item.DisplayName);
        Assert.Equal(video.WatchUri.AbsoluteUri, item.OriginalPath);
        Assert.Throws<ArgumentException>(() => catalog.AddYouTubeItem(groupId, video));

        var store = new PlaylistStore(Path.Combine(_folder, "playlist.json"));
        Assert.True(store.Save(catalog.Snapshot).Succeeded);
        var loaded = store.Load();
        Assert.Null(loaded.Warning);
        var roundTrip = Assert.Single(Assert.Single(loaded.Playlist.Groups).Items);
        Assert.Equal(item.Id, roundTrip.Id);
        Assert.Equal(MediaKind.YouTube, roundTrip.Kind);
        Assert.Equal(video.WatchUri.AbsoluteUri, roundTrip.OriginalPath);
    }

    [Fact]
    public void MixedLocalAndYouTubeMediaSurviveSaveAndPresetReload()
    {
        Directory.CreateDirectory(_folder);
        var originalFile = Path.Combine(_folder, "local.mp4");
        File.WriteAllBytes(originalFile, [1, 2, 3]);
        var local = new PlaylistItem(Guid.NewGuid(), "Local", originalFile, MediaKind.Video, 0);
        var remote = new PlaylistItem(Guid.NewGuid(), "YouTube",
            "https://www.youtube.com/watch?v=M7lc1UVf-VE", MediaKind.YouTube, 1);
        var document = new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion,
            [new PlaylistGroup(Guid.NewGuid(), "Sessão", 0, [local, remote])]);
        var presets = new PlaylistPresetStore(Path.Combine(_folder, "presets"));

        var saved = presets.Save("Culto", document);
        Assert.True(saved.Succeeded);
        var reloaded = presets.Load(saved.Preset!.Id);
        Assert.True(reloaded.Succeeded);
        var items = Assert.Single(reloaded.Preset!.Playlist.Groups).Items;
        Assert.Equal(2, items.Count);
        Assert.Equal(originalFile, items[0].OriginalPath);
        Assert.Equal(remote.OriginalPath, items[1].OriginalPath);
        Assert.True(File.Exists(originalFile));
    }

    [Fact]
    public void UpgradingOldPlaylistCreatesNonDestructiveV1Backup()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "playlist.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"groups\":[]}");
        var store = new PlaylistStore(path);

        Assert.True(store.Save(PlaylistDocument.Empty).Succeeded);

        Assert.True(File.Exists(path + ".pre-youtube-v1.bak"));
        Assert.Contains("\"schemaVersion\":1", File.ReadAllText(path + ".pre-youtube-v1.bak"));
        Assert.Equal(PlaylistDocument.CurrentSchemaVersion, store.Load().Playlist.SchemaVersion);
    }

    [Fact]
    public void MalformedYouTubeLinkCannotBeAddedToWorkingDocument()
    {
        var invalid = new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion,
            [new PlaylistGroup(Guid.NewGuid(), "Links", 0,
            [
                new PlaylistItem(Guid.NewGuid(), "Inválido",
                    "https://youtube.com.evil.example/video",
                    MediaKind.YouTube, 0)
            ])]);
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        Assert.Throws<ArgumentException>(() => catalog.ApplyPersistedChanges(invalid));
        Assert.Empty(catalog.Snapshot.Groups);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
