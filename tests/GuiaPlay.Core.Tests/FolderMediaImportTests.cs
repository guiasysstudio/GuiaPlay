using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class FolderMediaImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GuiaPlay-GP-I04", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScannerFindsSupportedMediaInDeterministicFolderOrderWithoutModifyingFiles()
    {
        var firstFolder = Path.Combine(_root, "01 - Vídeos");
        var secondFolder = Path.Combine(_root, "02 - Louvor");
        Directory.CreateDirectory(firstFolder);
        Directory.CreateDirectory(secondFolder);
        var video = Path.Combine(firstFolder, "abertura.MP4");
        var song = Path.Combine(secondFolder, "hino.Mp3");
        var ignored = Path.Combine(_root, "roteiro.txt");
        File.WriteAllBytes(video, [1, 2, 3]);
        File.WriteAllBytes(song, [4, 5]);
        File.WriteAllText(ignored, "não importar");

        var result = await FolderMediaScanner.ScanAsync(_root);

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(3, result.DirectoriesVisited);
        Assert.Equal(1, result.SkippedUnsupported);
        Assert.False(result.Limited);
        Assert.Equal(
            [Path.GetFullPath(video), Path.GetFullPath(song)],
            result.Candidates.Select(item => item.FullPath).ToArray());
        Assert.Equal(["01 - Vídeos", "02 - Louvor"], result.Candidates.Select(item => item.RelativeFolder).ToArray());
        Assert.Equal(MediaKind.Video, result.Candidates[0].Kind);
        Assert.Equal(MediaKind.Audio, result.Candidates[1].Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(video));
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(song));
    }

    [Fact]
    public async Task MergeCreatesNamedGroupsWithoutOverwritingExistingPlaylistsAndSkipsDuplicates()
    {
        var subFolder = Path.Combine(_root, "Vídeos");
        Directory.CreateDirectory(subFolder);
        var song = Path.Combine(_root, "hino.mp3");
        var video = Path.Combine(subFolder, "abertura.mp4");
        File.WriteAllText(song, "música");
        File.WriteAllText(video, "vídeo");
        var legacyGroup = new PlaylistGroup(Guid.NewGuid(), "Existente", 0,
        [
            new PlaylistItem(Guid.NewGuid(), "hino", song, MediaKind.Audio, 0)
        ]);
        var original = new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion, [legacyGroup]);
        var scan = await FolderMediaScanner.ScanAsync(_root);

        var merged = FolderMediaPlaylistMerge.Merge(original, scan);

        Assert.Equal(1, merged.Added);
        Assert.Equal(1, merged.SkippedDuplicates);
        Assert.Equal(1, merged.GroupsCreated);
        Assert.Equal(2, merged.Playlist.Groups.Count);
        Assert.Equal("Existente", merged.Playlist.Groups[0].Name);
        Assert.Equal(legacyGroup.Id, merged.Playlist.Groups[0].Id);
        Assert.Equal(Path.GetFullPath(video), Assert.Single(merged.Playlist.Groups[1].Items).OriginalPath);
        Assert.Equal($"{new DirectoryInfo(_root).Name} / Vídeos", merged.Playlist.Groups[1].Name);
        Assert.Single(original.Groups); // the input snapshot was not mutated
        Assert.True(File.Exists(song));
        Assert.True(File.Exists(video));

        var repeated = FolderMediaPlaylistMerge.Merge(merged.Playlist, scan);
        Assert.Equal(0, repeated.Added);
        Assert.Equal(2, repeated.SkippedDuplicates);
        Assert.Equal(0, repeated.GroupsCreated);
    }

    [Fact]
    public async Task TwoRootsWithSameNameGetSeparateGroupsAndRemainIdempotent()
    {
        var leftRoot = Path.Combine(_root, "A", "Evento");
        var rightRoot = Path.Combine(_root, "B", "Evento");
        Directory.CreateDirectory(leftRoot);
        Directory.CreateDirectory(rightRoot);
        File.WriteAllText(Path.Combine(leftRoot, "primeira.mp3"), "A");
        File.WriteAllText(Path.Combine(rightRoot, "segunda.mp3"), "B");

        var left = await FolderMediaScanner.ScanAsync(leftRoot);
        var right = await FolderMediaScanner.ScanAsync(rightRoot);
        var withLeft = FolderMediaPlaylistMerge.Merge(PlaylistDocument.Empty, left);
        var withBoth = FolderMediaPlaylistMerge.Merge(withLeft.Playlist, right);

        Assert.Equal(2, withBoth.Playlist.Groups.Count);
        Assert.NotEqual(withBoth.Playlist.Groups[0].Name, withBoth.Playlist.Groups[1].Name);
        Assert.True(PlaylistGroupName.IsValid(withBoth.Playlist.Groups[1].Name));
        Assert.Equal(2, withBoth.Playlist.Groups.SelectMany(group => group.Items).Count());

        var repeated = FolderMediaPlaylistMerge.Merge(withBoth.Playlist, right);
        Assert.Equal(0, repeated.Added);
        Assert.Equal(1, repeated.SkippedDuplicates);
    }

    [Fact]
    public async Task ImportPreservesNestedRelativeFoldersAsDistinctVirtualGroups()
    {
        var nested = Path.Combine(_root, "Culto", "Louvor", "Manhã");
        Directory.CreateDirectory(nested);
        var media = Path.Combine(nested, "faixa.wav");
        File.WriteAllBytes(media, [5, 6, 7]);
        var scan = await FolderMediaScanner.ScanAsync(_root);

        var merged = FolderMediaPlaylistMerge.Merge(PlaylistDocument.Empty, scan);

        Assert.Equal(1, merged.Added);
        var group = Assert.Single(merged.Playlist.Groups);
        Assert.Equal($"{new DirectoryInfo(_root).Name} / Culto / Louvor / Manhã", group.Name);
        Assert.True(PlaylistGroupName.IsValid(group.Name));
    }

    [Fact]
    public void LongNestedFolderNamesAreBoundedAndDisambiguated()
    {
        var longName = new string('x', 130);
        var first = FolderMediaPlaylistMerge.GroupName("Evento", longName + "a");
        var second = FolderMediaPlaylistMerge.GroupName("Evento", longName + "b");

        Assert.NotEqual(first, second);
        Assert.True(PlaylistGroupName.IsValid(first));
        Assert.True(PlaylistGroupName.IsValid(second));
        Assert.Equal(first, FolderMediaPlaylistMerge.GroupName("Evento", longName + "a"));
    }

    [Fact]
    public async Task CancellationIsRespectedBeforeAnyPlaylistMutation()
    {
        Directory.CreateDirectory(_root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FolderMediaScanner.ScanAsync(_root, cancellation.Token));
    }

    [Fact]
    public async Task MissingRootIsReportedWithoutCreatingTheDirectory()
    {
        var nonExisting = Path.Combine(_root, "nao-existe");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => FolderMediaScanner.ScanAsync(nonExisting));
        Assert.False(Directory.Exists(nonExisting));
    }

    [Fact]
    public void PersistedMutationKeepsPresetDirtyStateWithoutChangingOriginalMedia()
    {
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        var doc = new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion,
            [new PlaylistGroup(Guid.NewGuid(), "Importadas", 0, [])]);

        catalog.ApplyPersistedChanges(doc);

        Assert.True(catalog.IsDirty);
        Assert.Equal("Importadas", Assert.Single(catalog.Snapshot.Groups).Name);
        Assert.Empty(catalog.Snapshot.Groups[0].Items);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
