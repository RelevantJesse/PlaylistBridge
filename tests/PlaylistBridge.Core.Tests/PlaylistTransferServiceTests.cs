using PlaylistBridge.Core;

namespace PlaylistBridge.Core.Tests;

public sealed class PlaylistTransferServiceTests
{
    [Fact]
    public async Task CommitAsync_PreservesPartialProgressAndResumesExistingPlaylist()
    {
        var sourceTracks = Enumerable.Range(1, 3)
            .Select(index => new Track($"source-{index}", $"Track {index}", "Artist"))
            .ToArray();
        var matches = sourceTracks.Select((track, index) => new TrackMatch(
            track,
            new Track($"destination-{index + 1}", track.Title, track.Artist),
            100,
            MatchConfidence.High,
            [])).ToArray();
        var preview = new TransferResult(new Playlist("source", "Playlist", null, sourceTracks), null, matches);
        var provider = new RecordingProvider { FailAfterTrackCount = 2 };
        var service = new PlaylistTransferService(new TrackMatcher());

        var failure = await Assert.ThrowsAsync<PlaylistTransferException>(
            () => service.CommitAsync(provider, preview, "Spotify"));

        Assert.Equal("created-playlist", failure.PartialResult.DestinationPlaylistId);
        Assert.Equal(2, failure.PartialResult.AddedTrackCount);
        Assert.Single(provider.CreatedPlaylists);

        provider.FailAfterTrackCount = null;
        var completed = await service.CommitAsync(provider, failure.PartialResult, "Spotify");

        Assert.True(completed.IsComplete);
        Assert.Equal(3, completed.AddedTrackCount);
        Assert.Single(provider.CreatedPlaylists);
        Assert.Equal(["destination-1", "destination-2", "destination-3"], provider.AddedTrackIds);
    }

    private sealed class RecordingProvider : IMusicProvider
    {
        public string Name => "Test destination";
        public int? FailAfterTrackCount { get; set; }
        public List<string> CreatedPlaylists { get; } = [];
        public List<string> AddedTrackIds { get; } = [];

        public Task<string> CreatePlaylistAsync(string name, string? description, CancellationToken cancellationToken = default)
        {
            CreatedPlaylists.Add(name);
            return Task.FromResult("created-playlist");
        }

        public Task AddTracksAsync(string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default)
        {
            var addedThisCall = 0;
            foreach (var trackId in trackIds)
            {
                if (FailAfterTrackCount is not null && AddedTrackIds.Count >= FailAfterTrackCount)
                    throw new TrackWriteException(Name, addedThisCall, trackIds.Count,
                        new ProviderApiException(Name, "add a track", 401, "expiredToken", "Token expired"));
                AddedTrackIds.Add(trackId);
                addedThisCall++;
            }
            return Task.CompletedTask;
        }

        public Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Track>> SearchTracksAsync(
            Track source, int limit = 10, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
