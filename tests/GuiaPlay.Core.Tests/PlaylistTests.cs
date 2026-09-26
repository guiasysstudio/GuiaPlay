using System.Text.Json.Nodes;
using System.Diagnostics;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class PlaylistTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GuiaPlay.M03.Tests", Guid.NewGuid().ToString("N"));
    private string PlaylistPath => Path.Combine(_directory, "playlist.json");

    [Fact]
    public void CreatesAndRenamesVirtualGroup()
    {
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);

        var group = catalog.CreateGroup("Entrada");
        var renamed = catalog.RenameGroup(group.Id, "Louvor");

        Assert.True(renamed);
        Assert.Equal("Louvor", Assert.Single(catalog.Snapshot.Groups).Name);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Entrada", true)]
    public void GroupDialogValidationAcceptsOnlyValidNames(string? name, bool expected)
    {
        Assert.Equal(expected, PlaylistGroupName.IsValid(name));
    }

    [Fact]
    public void PersistsGroupsAndOnlyLightweightFileReference()
    {
        Directory.CreateDirectory(_directory);
        var mediaPath = Path.Combine(_directory, "grande-video.mkv");
        File.WriteAllBytes(mediaPath, [1, 2, 3, 4, 5]);
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var group = catalog.CreateGroup("Entrada");
        catalog.AddItem(group.Id, mediaPath);
        var store = new PlaylistStore(PlaylistPath);

        Assert.True(store.Save(catalog.Snapshot).Succeeded);
        var loaded = store.Load().Playlist;
        var json = File.ReadAllText(PlaylistPath);

        Assert.Equal("Entrada", Assert.Single(loaded.Groups).Name);
        var item = Assert.Single(Assert.Single(loaded.Groups).Items);
        Assert.Equal(Path.GetFullPath(mediaPath), item.OriginalPath);
        Assert.Equal(MediaKind.Video, item.Kind);
        Assert.DoesNotContain(Convert.ToBase64String(File.ReadAllBytes(mediaPath)), json, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingItemNeverDeletesOriginalFile()
    {
        Directory.CreateDirectory(_directory);
        var mediaPath = Path.Combine(_directory, "hino.mp3");
        File.WriteAllText(mediaPath, "conteúdo original");
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var group = catalog.CreateGroup("Louvor");
        var item = catalog.AddItem(group.Id, mediaPath);

        Assert.True(catalog.RemoveItem(group.Id, item.Id));

        Assert.True(File.Exists(mediaPath));
        Assert.Empty(Assert.Single(catalog.Snapshot.Groups).Items);
    }

    [Fact]
    public void DragImportAcceptsMultipleValidFilesRejectsInvalidAndDoesNotCopyMedia()
    {
        Directory.CreateDirectory(_directory);
        var video = Path.Combine(_directory, "abertura.mp4");
        var audio = Path.Combine(_directory, "hino.ogg");
        var invalid = Path.Combine(_directory, "leia-me.txt");
        File.WriteAllBytes(video, [10, 20, 30]);
        File.WriteAllBytes(audio, [40, 50]);
        File.WriteAllText(invalid, "não é mídia");
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var group = catalog.CreateGroup("Arrastados");

        var result = catalog.AddItems(group.Id, [video, invalid, audio]);

        Assert.Equal(2, result.Added.Count);
        Assert.Equal(invalid, Assert.Single(result.Rejected));
        Assert.Equal([10, 20, 30], File.ReadAllBytes(video));
        Assert.Equal([40, 50], File.ReadAllBytes(audio));
        Assert.Equal(3, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void ReordersGroups()
    {
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var first = catalog.CreateGroup("Primeiro");
        catalog.CreateGroup("Segundo");

        Assert.True(catalog.MoveGroup(first.Id, 1));

        Assert.Equal(["Segundo", "Primeiro"], catalog.Snapshot.Groups.Select(group => group.Name));
    }

    [Fact]
    public void ReordersItemsInsideGroup()
    {
        Directory.CreateDirectory(_directory);
        var firstPath = Path.Combine(_directory, "primeiro.mp3");
        var secondPath = Path.Combine(_directory, "segundo.mp3");
        File.WriteAllText(firstPath, "1");
        File.WriteAllText(secondPath, "2");
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var group = catalog.CreateGroup("Grupo");
        var first = catalog.AddItem(group.Id, firstPath);
        catalog.AddItem(group.Id, secondPath);

        Assert.True(catalog.MoveItem(group.Id, first.Id, group.Id, 1));

        Assert.Equal(["segundo.mp3", "primeiro.mp3"], catalog.Snapshot.Groups.Single().Items.Select(item => item.DisplayName));
    }

    [Fact]
    public void MovesItemBetweenGroupsAndPersistsNewOrder()
    {
        Directory.CreateDirectory(_directory);
        var mediaPath = Path.Combine(_directory, "mover.flac");
        File.WriteAllText(mediaPath, "audio");
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var source = catalog.CreateGroup("Origem");
        var target = catalog.CreateGroup("Destino");
        var item = catalog.AddItem(source.Id, mediaPath);

        Assert.True(catalog.MoveItem(source.Id, item.Id, target.Id, 0));
        var store = new PlaylistStore(PlaylistPath);
        Assert.True(store.Save(catalog.Snapshot).Succeeded);
        var loaded = store.Load().Playlist;

        Assert.Empty(loaded.Groups[0].Items);
        Assert.Equal(item.Id, Assert.Single(loaded.Groups[1].Items).Id);
        Assert.Equal(0, loaded.Groups[1].Items[0].Order);
    }

    [Fact]
    public void MissingOriginalRemainsInPlaylistAndIsUnavailable()
    {
        var missing = Path.Combine(_directory, "nao-existe.webm");
        var group = new PlaylistGroup(Guid.NewGuid(), "Sermão", 0,
        [new PlaylistItem(Guid.NewGuid(), "Ilustração.webm", missing, MediaKind.Video, 0)]);
        var store = new PlaylistStore(PlaylistPath);

        Assert.True(store.Save(new PlaylistDocument(1, [group])).Succeeded);
        var item = Assert.Single(Assert.Single(store.Load().Playlist.Groups).Items);

        Assert.False(item.IsAvailable);
        Assert.Equal(Path.GetFullPath(missing), item.OriginalPath);
    }

    [Fact]
    public void GroupAndItemOrderAreNormalizedAndPersisted()
    {
        var later = new PlaylistGroup(Guid.NewGuid(), "Depois", 20, []);
        var earlier = new PlaylistGroup(Guid.NewGuid(), "Antes", 10, []);
        var store = new PlaylistStore(PlaylistPath);

        Assert.True(store.Save(new PlaylistDocument(1, [later, earlier])).Succeeded);
        var loaded = store.Load().Playlist;

        Assert.Equal(["Antes", "Depois"], loaded.Groups.Select(group => group.Name));
        Assert.Equal([0, 1], loaded.Groups.Select(group => group.Order));
    }

    [Theory]
    [InlineData("arquivo.mp4", MediaKind.Video)]
    [InlineData("arquivo.mkv", MediaKind.Video)]
    [InlineData("arquivo.webm", MediaKind.Video)]
    [InlineData("arquivo.mp3", MediaKind.Audio)]
    [InlineData("arquivo.ogg", MediaKind.Audio)]
    [InlineData("arquivo.flac", MediaKind.Audio)]
    public void RecognizesCommonLibVlcMediaExtensions(string path, MediaKind expected)
    {
        Assert.Equal(expected, MediaTypeDetector.Detect(path));
    }

    [Fact]
    public void PlaylistJsonContainsPathButNoMediaPayloadProperty()
    {
        var item = new PlaylistItem(Guid.NewGuid(), "Saída.mp4", @"C:\Midias\Saida.mp4", MediaKind.Video, 0);
        var group = new PlaylistGroup(Guid.NewGuid(), "Encerramento", 0, [item]);
        var store = new PlaylistStore(PlaylistPath);

        Assert.True(store.Save(new PlaylistDocument(1, [group])).Succeeded);
        var root = JsonNode.Parse(File.ReadAllText(PlaylistPath))!.AsObject();
        var savedItem = root["groups"]![0]!["items"]![0]!.AsObject();

        Assert.NotNull(savedItem["originalPath"]);
        Assert.Null(savedItem["bytes"]);
        Assert.Null(savedItem["content"]);
        Assert.Null(savedItem["data"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":1,\"groups\":[")]
    public void EmptyOrTruncatedPlaylistFallsBackWithoutCrashing(string invalidJson)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PlaylistPath, invalidJson);

        var result = new PlaylistStore(PlaylistPath).Load();

        Assert.Empty(result.Playlist.Groups);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void FailedSavePreservesPreviouslyValidPlaylist()
    {
        Directory.CreateDirectory(_directory);
        var original = new PlaylistDocument(1, [new PlaylistGroup(Guid.NewGuid(), "Original", 0, [])]);
        var store = new PlaylistStore(PlaylistPath);
        Assert.True(store.Save(original).Succeeded);
        var previousBytes = File.ReadAllBytes(PlaylistPath);
        using var lockStream = new FileStream(PlaylistPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = store.Save(new PlaylistDocument(1, [new PlaylistGroup(Guid.NewGuid(), "Nova", 0, [])]));

        Assert.False(result.Succeeded);
        Assert.Equal(previousBytes, File.ReadAllBytes(PlaylistPath));
    }

    [Fact]
    public void ThousandItemCatalogReordersSerializesAndLoadsWithinGenerousThreshold()
    {
        var stopwatch = Stopwatch.StartNew();
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var firstGroup = catalog.CreateGroup("Catálogo A");
        var secondGroup = catalog.CreateGroup("Catálogo B");
        for (var index = 0; index < 1000; index++)
        {
            catalog.AddItem(firstGroup.Id, Path.Combine(_directory, $"referência-{index:0000}.mp4"));
        }

        var first = catalog.Snapshot.Groups[0].Items[0];
        Assert.True(catalog.MoveItem(firstGroup.Id, first.Id, secondGroup.Id, 0));
        var store = new PlaylistStore(PlaylistPath);
        Assert.True(store.Save(catalog.Snapshot).Succeeded);
        var loaded = store.Load().Playlist;
        stopwatch.Stop();

        Assert.Equal(1000, loaded.Groups.Sum(group => group.Items.Count));
        Assert.Equal(first.Id, Assert.Single(loaded.Groups[1].Items).Id);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Operação levou {stopwatch.Elapsed}.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
