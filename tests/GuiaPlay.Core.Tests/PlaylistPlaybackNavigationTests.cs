using GuiaPlay.Core;
using Xunit;

namespace GuiaPlay.Core.Tests;

public sealed class PlaylistPlaybackNavigationTests
{
    private readonly Guid _first = Guid.NewGuid();
    private readonly Guid _second = Guid.NewGuid();
    private readonly Guid _third = Guid.NewGuid();

    private PlaylistDocument OrderedPlaylist() => new(
        PlaylistDocument.CurrentSchemaVersion,
        [
            new PlaylistGroup(Guid.NewGuid(), "Louvor", 0,
            [
                new PlaylistItem(_first, "1.mp3", @"C:\a\1.mp3", MediaKind.Audio, 0),
                new PlaylistItem(_second, "2.mp4", @"C:\a\2.mp4", MediaKind.Video, 1)
            ]),
            new PlaylistGroup(Guid.NewGuid(), "Vídeos", 1,
            [
                new PlaylistItem(_third, "3.mp4", @"C:\b\3.mp4", MediaKind.Video, 0)
            ])
        ]);

    [Fact]
    public void ManualNeverAutoAdvancesEvenWithRepeatOrShuffle()
    {
        var playlist = OrderedPlaylist();
        Assert.Null(PlaylistPlaybackNavigation.Next(
            playlist, _first, PlaylistPlaybackMode.Manual,
            PlaylistRepeatMode.All, shuffle: true, automatic: true));
        Assert.Null(PlaylistPlaybackNavigation.Next(
            playlist, _third, PlaylistPlaybackMode.Manual,
            PlaylistRepeatMode.Current, shuffle: false, automatic: true));
    }

    [Fact]
    public void SequentialAdvancesAcrossGroupBoundaryAndStopsAtEnd()
    {
        var playlist = OrderedPlaylist();
        Assert.Equal(_second, PlaylistPlaybackNavigation.Next(
            playlist, _first, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.Off, shuffle: false, automatic: true));
        Assert.Equal(_third, PlaylistPlaybackNavigation.Next(
            playlist, _second, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.Off, shuffle: false, automatic: true));
        Assert.Null(PlaylistPlaybackNavigation.Next(
            playlist, _third, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.Off, shuffle: false, automatic: true));
    }

    [Fact]
    public void RepeatCurrentHasPriorityOverShuffleOnlyOnNaturalEnd()
    {
        var playlist = OrderedPlaylist();
        Assert.Equal(_second, PlaylistPlaybackNavigation.Next(
            playlist, _second, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.Current, shuffle: true, automatic: true));
        Assert.NotEqual(_second, PlaylistPlaybackNavigation.Next(
            playlist, _second, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.Current, shuffle: true, automatic: false));
    }

    [Fact]
    public void RepeatPlaylistWrapsButPreviousNeverWrapsUnexpectedly()
    {
        var playlist = OrderedPlaylist();
        Assert.Equal(_first, PlaylistPlaybackNavigation.Next(
            playlist, _third, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.All, shuffle: false, automatic: true));
        Assert.Equal(_first, PlaylistPlaybackNavigation.Previous(playlist, _second));
        Assert.Null(PlaylistPlaybackNavigation.Previous(playlist, _first));
    }

    [Fact]
    public void ShuffleNeverPicksCurrentWhenMultipleItemsExist()
    {
        var playlist = OrderedPlaylist();
        for (var seed = 0; seed < 10; seed++)
        {
            var next = PlaylistPlaybackNavigation.Next(
                playlist, _second, PlaylistPlaybackMode.Sequential,
                PlaylistRepeatMode.All, shuffle: true, automatic: true,
                randomCandidateIndex: seed);
            Assert.Contains(next, new Guid?[] { _first, _third });
        }
    }

    [Fact]
    public void ShuffleWithoutRepeatStopsAtEndOfUnplayedCandidates()
    {
        var playlist = OrderedPlaylist();
        var visited = new HashSet<Guid> { _first, _second, _third };

        Assert.Null(PlaylistPlaybackNavigation.Next(
            playlist, _third, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.Off, shuffle: true, automatic: true,
            playedInShuffleCycle: visited));
        Assert.NotNull(PlaylistPlaybackNavigation.Next(
            playlist, _third, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.All, shuffle: true, automatic: true,
            playedInShuffleCycle: visited));
    }

    [Fact]
    public void ShuffleOnlyChoosesNotYetPlayedItemsWithinCycle()
    {
        var playlist = OrderedPlaylist();
        var visited = new HashSet<Guid> { _first, _second };

        var next = PlaylistPlaybackNavigation.Next(
            playlist, _second, PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.Off, shuffle: true, automatic: true,
            playedInShuffleCycle: visited);

        Assert.Equal(_third, next);
    }

    [Fact]
    public void NavigationFailsClosedForMissingItemOrEmptyPlaylist()
    {
        var playlist = OrderedPlaylist();
        Assert.Null(PlaylistPlaybackNavigation.Next(
            playlist, Guid.NewGuid(), PlaylistPlaybackMode.Sequential,
            PlaylistRepeatMode.All, shuffle: false, automatic: true));
        Assert.Null(PlaylistPlaybackNavigation.Previous(PlaylistDocument.Empty, _first));
        Assert.False(PlaylistPlaybackNavigation.Contains(PlaylistDocument.Empty, _first));
        Assert.True(PlaylistPlaybackNavigation.Contains(playlist, _third));
    }
}
