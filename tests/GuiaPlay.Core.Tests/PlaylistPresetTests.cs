using System.Text.Json.Nodes;
using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class PlaylistPresetTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "GuiaPlay.M10.3.Preset.Tests",
        Guid.NewGuid().ToString("N"));

    private string PresetDirectory => Path.Combine(_root, "playlist-presets");

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Culto de oração", true)]
    public void PresetNameRequiresVisibleText(string? name, bool expected)
    {
        Assert.Equal(expected, PlaylistPresetName.IsValid(name));
    }

    [Fact]
    public void PresetNameAcceptsEightyCharactersAndRejectsEightyOne()
    {
        Assert.True(PlaylistPresetName.IsValid(new string('a', 80)));
        Assert.False(PlaylistPresetName.IsValid(new string('a', 81)));
    }

    [Fact]
    public void SaveCreatesOneGuidJsonWithSchemaUnicodeAndCompleteOrderedSnapshot()
    {
        Directory.CreateDirectory(_root);
        var mediaPath = Path.Combine(_root, "Hino João 🎵.mp3");
        var originalBytes = new byte[] { 1, 3, 5, 7, 9 };
        File.WriteAllBytes(mediaPath, originalBytes);
        var firstItem = new PlaylistItem(Guid.NewGuid(), "Hino João 🎵", mediaPath, MediaKind.Audio, 20);
        var secondItem = new PlaylistItem(Guid.NewGuid(), "Abertura", Path.Combine(_root, "Abertura.mp4"), MediaKind.Video, 10);
        var laterGroup = new PlaylistGroup(Guid.NewGuid(), "Louvor ágil", 9, [firstItem]);
        var earlierGroup = new PlaylistGroup(Guid.NewGuid(), "Vídeos", 2, [secondItem]);
        var store = new PlaylistPresetStore(PresetDirectory);

        var result = store.Save(
            "  Culto Domingo — manhã 日本語  ",
            new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion, [laterGroup, earlierGroup]));

        Assert.True(result.Succeeded, result.Error);
        var preset = Assert.IsType<PlaylistPreset>(result.Preset);
        Assert.Equal("Culto Domingo — manhã 日本語", preset.Name);
        Assert.NotEqual(Guid.Empty, preset.Id);
        Assert.Equal(["Vídeos", "Louvor ágil"], preset.Playlist.Groups.Select(group => group.Name));
        Assert.Equal([0, 1], preset.Playlist.Groups.Select(group => group.Order));
        Assert.Equal(Path.GetFullPath(mediaPath), preset.Playlist.Groups[1].Items[0].OriginalPath);
        Assert.Equal(MediaKind.Audio, preset.Playlist.Groups[1].Items[0].Kind);

        var jsonPath = Assert.Single(Directory.GetFiles(PresetDirectory, "*.json"));
        Assert.Equal($"{preset.Id:D}.json", Path.GetFileName(jsonPath), ignoreCase: true);
        var root = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();
        Assert.Equal(PlaylistPreset.CurrentSchema, root["schema"]!.GetValue<int>());
        Assert.Equal(preset.Id, root["id"]!.GetValue<Guid>());
        Assert.Equal(preset.Name, root["name"]!.GetValue<string>());
        Assert.NotNull(root["createdAt"]);
        Assert.NotNull(root["updatedAt"]);
        Assert.NotNull(root["playlist"]);
        Assert.Equal(originalBytes, File.ReadAllBytes(mediaPath));
        Assert.DoesNotContain(
            Directory.GetFiles(PresetDirectory),
            path => string.Equals(Path.GetFileName(path), Path.GetFileName(mediaPath), StringComparison.Ordinal));
    }

    [Fact]
    public void RestartLoadsMultiplePresetsIndependently()
    {
        var store = new PlaylistPresetStore(PresetDirectory);
        var morning = store.Save("Culto manhã", Document("Manhã"));
        var evening = store.Save("Culto noite", Document("Noite"));
        Assert.True(morning.Succeeded, morning.Error);
        Assert.True(evening.Succeeded, evening.Error);

        var afterRestart = new PlaylistPresetStore(PresetDirectory).LoadAll();

        Assert.Empty(afterRestart.Warnings);
        Assert.Equal(["Culto manhã", "Culto noite"], afterRestart.Presets.Select(preset => preset.Name));
        Assert.Equal(2, Directory.GetFiles(PresetDirectory, "*.json").Length);
    }

    [Fact]
    public void CaseInsensitiveConflictCancelsWithoutChangingExistingPreset()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new PlaylistPresetStore(PresetDirectory, time);
        var first = store.Save("Santa Ceia", Document("Original"));
        var firstPreset = Assert.IsType<PlaylistPreset>(first.Preset);
        var originalBytes = File.ReadAllBytes(Path.Combine(PresetDirectory, $"{firstPreset.Id:D}.json"));
        time.Advance(TimeSpan.FromHours(1));

        var conflict = store.Save("  SANTA CEIA  ", Document("Não deve salvar"));

        Assert.Equal(PlaylistPresetSaveStatus.NameConflict, conflict.Status);
        Assert.Equal(firstPreset.Id, conflict.ConflictingPreset?.Id);
        Assert.Equal(originalBytes, File.ReadAllBytes(Path.Combine(PresetDirectory, $"{firstPreset.Id:D}.json")));
        Assert.Equal("Original", Assert.Single(store.Load(firstPreset.Id).Preset!.Playlist.Groups).Name);
    }

    [Fact]
    public void ConfirmedOverwritePreservesIdAndCreationDateAndUpdatesSnapshot()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var store = new PlaylistPresetStore(PresetDirectory, time);
        var created = Assert.IsType<PlaylistPreset>(store.Save("JA", Document("Inicial")).Preset);
        time.Advance(TimeSpan.FromMinutes(15));

        var overwrite = store.Save("ja", Document("Atualizada"), overwriteExisting: true);

        var updated = Assert.IsType<PlaylistPreset>(overwrite.Preset);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(time.GetUtcNow(), updated.UpdatedAt);
        Assert.Equal("Atualizada", Assert.Single(updated.Playlist.Groups).Name);
        Assert.Single(Directory.GetFiles(PresetDirectory, "*.json"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidNameDoesNotCreateDirectoryOrFile(string? name)
    {
        var result = new PlaylistPresetStore(PresetDirectory).Save(name, Document("Grupo"));

        Assert.Equal(PlaylistPresetSaveStatus.InvalidName, result.Status);
        Assert.False(Directory.Exists(PresetDirectory));
    }

    [Fact]
    public void CorruptPresetIsIsolatedFromValidPresets()
    {
        var store = new PlaylistPresetStore(PresetDirectory);
        var valid = Assert.IsType<PlaylistPreset>(store.Save("Válido", Document("Preservado")).Preset);
        File.WriteAllText(Path.Combine(PresetDirectory, $"{Guid.NewGuid():D}.json"), "{ conteúdo quebrado");
        File.WriteAllText(Path.Combine(PresetDirectory, "nao-e-guid.json"), "{}");

        var result = new PlaylistPresetStore(PresetDirectory).LoadAll();

        Assert.Equal(valid.Id, Assert.Single(result.Presets).Id);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void InvalidItemInsidePresetIsSkippedButValidGroupsAndItemsRemain()
    {
        var store = new PlaylistPresetStore(PresetDirectory);
        var saved = Assert.IsType<PlaylistPreset>(store.Save("Parcial", DocumentWithItem("Grupo", "válido.mp4")).Preset);
        var path = Path.Combine(PresetDirectory, $"{saved.Id:D}.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var items = root["playlist"]!["groups"]![0]!["items"]!.AsArray();
        items.Add(new JsonObject
        {
            ["id"] = Guid.NewGuid(),
            ["displayName"] = "Inválido",
            ["originalPath"] = null,
            ["kind"] = (int)MediaKind.Video,
            ["order"] = 1
        });
        File.WriteAllText(path, root.ToJsonString());

        var loaded = new PlaylistPresetStore(PresetDirectory).Load(saved.Id);

        Assert.True(loaded.Succeeded, loaded.Error);
        Assert.NotNull(loaded.Error);
        var group = Assert.Single(loaded.Preset!.Playlist.Groups);
        Assert.Equal("Grupo", group.Name);
        Assert.Equal("válido.mp4", Assert.Single(group.Items).DisplayName);
    }

    [Fact]
    public void MissingMediaReferenceSurvivesPresetRoundTrip()
    {
        var missing = Path.Combine(_root, "arquivo removido.webm");
        var document = new PlaylistDocument(
            PlaylistDocument.CurrentSchemaVersion,
            [new PlaylistGroup(
                Guid.NewGuid(),
                "Sermão",
                0,
                [new PlaylistItem(Guid.NewGuid(), "arquivo removido.webm", missing, MediaKind.Video, 0)])]);
        var store = new PlaylistPresetStore(PresetDirectory);
        var saved = Assert.IsType<PlaylistPreset>(store.Save("Com ausente", document).Preset);

        var loaded = store.Load(saved.Id);

        Assert.True(loaded.Succeeded, loaded.Error);
        Assert.Equal(Path.GetFullPath(missing), Assert.Single(Assert.Single(loaded.Preset!.Playlist.Groups).Items).OriginalPath);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void DeleteRemovesOnlyPresetJsonAndNeverTouchesMediaOrOtherFiles()
    {
        Directory.CreateDirectory(_root);
        var media = Path.Combine(_root, "original.flac");
        File.WriteAllText(media, "conteúdo");
        var store = new PlaylistPresetStore(PresetDirectory);
        var preset = Assert.IsType<PlaylistPreset>(store.Save("Excluir", DocumentWithItem("Grupo", media)).Preset);
        var unrelated = Path.Combine(PresetDirectory, "leia-me.txt");
        File.WriteAllText(unrelated, "manter");

        var result = store.Delete(preset.Id);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(File.Exists(Path.Combine(PresetDirectory, $"{preset.Id:D}.json")));
        Assert.True(File.Exists(media));
        Assert.True(File.Exists(unrelated));
        Assert.Equal(PlaylistPresetDeleteStatus.NotFound, store.Delete(preset.Id).Status);
    }

    [Fact]
    public void FailedAtomicOverwritePreservesPreviousValidJson()
    {
        var store = new PlaylistPresetStore(PresetDirectory);
        var preset = Assert.IsType<PlaylistPreset>(store.Save("Atômica", Document("Original")).Preset);
        var path = Path.Combine(PresetDirectory, $"{preset.Id:D}.json");
        var previousBytes = File.ReadAllBytes(path);
        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = store.Save("Atômica", Document("Nova"), overwriteExisting: true);

        Assert.Equal(PlaylistPresetSaveStatus.Failed, result.Status);
        Assert.Equal(previousBytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(PresetDirectory, "*.tmp-*"));
    }

    [Fact]
    public void CatalogTracksEveryPlaylistMutationAndCanAcceptBaseline()
    {
        var catalog = new PlaylistCatalog(PlaylistDocument.Empty);
        Assert.False(catalog.IsDirty);

        var firstGroup = catalog.CreateGroup("Primeiro");
        AssertDirtyThenAccept(catalog);
        Assert.True(catalog.RenameGroup(firstGroup.Id, "Renomeado"));
        AssertDirtyThenAccept(catalog);
        var firstItem = catalog.AddItem(firstGroup.Id, Path.Combine(_root, "primeiro.mp3"));
        AssertDirtyThenAccept(catalog);
        var secondItem = catalog.AddItem(firstGroup.Id, Path.Combine(_root, "segundo.mp3"));
        AssertDirtyThenAccept(catalog);
        Assert.True(catalog.MoveItem(firstGroup.Id, firstItem.Id, firstGroup.Id, 1));
        AssertDirtyThenAccept(catalog);
        var secondGroup = catalog.CreateGroup("Segundo");
        AssertDirtyThenAccept(catalog);
        Assert.True(catalog.MoveItem(firstGroup.Id, secondItem.Id, secondGroup.Id, 0));
        AssertDirtyThenAccept(catalog);
        Assert.True(catalog.MoveGroup(secondGroup.Id, 0));
        AssertDirtyThenAccept(catalog);
        Assert.True(catalog.RemoveItem(secondGroup.Id, secondItem.Id));
        AssertDirtyThenAccept(catalog);
        Assert.True(catalog.RemoveGroup(firstGroup.Id));
        Assert.True(catalog.IsDirty);
    }

    [Fact]
    public void WorkspaceNeverReplacesDirtyPlaylistWithoutExplicitDiscard()
    {
        var workspace = new PlaylistWorkspace(Document("Em edição"));
        workspace.Catalog.CreateGroup("Alteração não salva");
        var target = new PlaylistPreset(
            PlaylistPreset.CurrentSchema,
            Guid.NewGuid(),
            "Destino",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Document("Carregada"));

        var blocked = workspace.LoadPreset(target);

        Assert.Equal(PlaylistWorkspaceLoadStatus.UnsavedChanges, blocked.Status);
        Assert.Equal(["Em edição", "Alteração não salva"], workspace.Catalog.Snapshot.Groups.Select(group => group.Name));
        var loaded = workspace.LoadPreset(target, PlaylistReplacementMode.DiscardUnsavedChanges);
        Assert.True(loaded.Succeeded);
        Assert.False(workspace.IsDirty);
        Assert.Equal(target.Id, workspace.ActivePresetId);
        Assert.Equal("Destino", workspace.ActivePresetName);
        Assert.Equal("Carregada", Assert.Single(workspace.Catalog.Snapshot.Groups).Name);
    }

    [Fact]
    public void SavingWorkspaceSetsActivePresetAndClearsDirtyState()
    {
        var workspace = new PlaylistWorkspace(PlaylistDocument.Empty);
        workspace.Catalog.CreateGroup("Culto");
        Assert.True(workspace.IsDirty);

        var result = workspace.SavePreset(new PlaylistPresetStore(PresetDirectory), "Culto especial");

        Assert.True(result.Succeeded, result.Error);
        Assert.False(workspace.IsDirty);
        Assert.Equal(result.Preset!.Id, workspace.ActivePresetId);
        Assert.Equal("Culto especial", workspace.ActivePresetName);
    }

    [Fact]
    public void PresetWithOneThousandReferencesRoundTripsInExactOrder()
    {
        var groups = Enumerable.Range(0, 10)
            .Select(groupIndex => new PlaylistGroup(
                Guid.NewGuid(),
                $"Grupo {groupIndex:D2}",
                groupIndex,
                Enumerable.Range(0, 100)
                    .Select(itemIndex =>
                    {
                        var path = Path.Combine(_root, $"grupo-{groupIndex:D2}", $"midia-{itemIndex:D3}.mp4");
                        return new PlaylistItem(
                            Guid.NewGuid(),
                            Path.GetFileName(path),
                            path,
                            MediaKind.Video,
                            itemIndex);
                    })
                    .ToArray()))
            .ToArray();
        var expectedPaths = groups.SelectMany(group => group.Items).Select(item => item.OriginalPath).ToArray();
        var store = new PlaylistPresetStore(PresetDirectory);

        var saved = Assert.IsType<PlaylistPreset>(store.Save(
            "Evento com mil referencias",
            new PlaylistDocument(PlaylistDocument.CurrentSchemaVersion, groups)).Preset);
        var loaded = store.Load(saved.Id);

        Assert.True(loaded.Succeeded, loaded.Error);
        Assert.Equal(10, loaded.Preset!.Playlist.Groups.Count);
        Assert.Equal(1_000, loaded.Preset.Playlist.Groups.Sum(group => group.Items.Count));
        Assert.Equal(expectedPaths, loaded.Preset.Playlist.Groups.SelectMany(group => group.Items).Select(item => item.OriginalPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static PlaylistDocument Document(string groupName) => new(
        PlaylistDocument.CurrentSchemaVersion,
        [new PlaylistGroup(Guid.NewGuid(), groupName, 0, [])]);

    private static PlaylistDocument DocumentWithItem(string groupName, string path) => new(
        PlaylistDocument.CurrentSchemaVersion,
        [new PlaylistGroup(
            Guid.NewGuid(),
            groupName,
            0,
            [new PlaylistItem(Guid.NewGuid(), Path.GetFileName(path), path, MediaTypeDetector.Detect(path), 0)])]);

    private static void AssertDirtyThenAccept(PlaylistCatalog catalog)
    {
        Assert.True(catalog.IsDirty);
        catalog.AcceptChanges();
        Assert.False(catalog.IsDirty);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan interval) => _utcNow += interval;
    }
}
