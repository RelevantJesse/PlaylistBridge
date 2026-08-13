namespace PlaylistBridge.Core;

public sealed class PlaylistTransferService(TrackMatcher matcher)
{
    public async Task<TransferResult> TransferAsync(
        IMusicProvider sourceProvider,
        IMusicProvider destinationProvider,
        string sourcePlaylistId,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var playlist = await sourceProvider.GetPlaylistAsync(sourcePlaylistId, cancellationToken);
        var matches = new List<TrackMatch>(playlist.Tracks.Count);

        foreach (var track in playlist.Tracks)
        {
            var candidates = await destinationProvider.SearchTracksAsync(track, 10, cancellationToken);
            matches.Add(matcher.FindBest(track, candidates));
        }

        string? destinationId = null;
        if (!dryRun)
        {
            destinationId = await destinationProvider.CreatePlaylistAsync(
                playlist.Name, $"Transferred from {sourceProvider.Name}. {playlist.Description}", cancellationToken);
            var ids = matches.Where(x => x.IsAccepted).Select(x => x.Destination!.Id).ToArray();
            foreach (var batch in ids.Chunk(100))
                await destinationProvider.AddTracksAsync(destinationId, batch, cancellationToken);
        }

        return new(playlist, destinationId, matches);
    }
}
