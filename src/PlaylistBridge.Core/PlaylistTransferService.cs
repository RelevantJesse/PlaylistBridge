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
        var preview = await PreviewAsync(
            sourceProvider, destinationProvider, sourcePlaylistId, cancellationToken);

        return dryRun
            ? preview
            : await CommitAsync(destinationProvider, preview, sourceProvider.Name, cancellationToken);
    }

    public async Task<TransferResult> PreviewAsync(
        IMusicProvider sourceProvider,
        IMusicProvider destinationProvider,
        string sourcePlaylistId,
        CancellationToken cancellationToken = default)
    {
        var playlist = await sourceProvider.GetPlaylistAsync(sourcePlaylistId, cancellationToken);
        var matches = new List<TrackMatch>(playlist.Tracks.Count);

        foreach (var track in playlist.Tracks)
        {
            var candidates = await destinationProvider.SearchTracksAsync(track, 10, cancellationToken);
            matches.Add(matcher.FindBest(track, candidates));
        }

        return new(playlist, null, matches);
    }

    public async Task<TransferResult> CommitAsync(
        IMusicProvider destinationProvider,
        TransferResult preview,
        string sourceProviderName,
        CancellationToken cancellationToken = default)
    {
        var ids = preview.Matches.Where(x => x.IsAccepted).Select(x => x.Destination!.Id).ToArray();
        var destinationId = preview.DestinationPlaylistId ?? await destinationProvider.CreatePlaylistAsync(
            preview.Source.Name,
            $"Transferred from {sourceProviderName}. {preview.Source.Description}",
            cancellationToken);

        var addedTrackCount = Math.Clamp(preview.AddedTrackCount, 0, ids.Length);
        foreach (var batch in ids.Skip(addedTrackCount).Chunk(100))
        {
            try
            {
                await destinationProvider.AddTracksAsync(destinationId, batch, cancellationToken);
                addedTrackCount += batch.Length;
            }
            catch (TrackWriteException exception)
            {
                addedTrackCount += exception.AddedTrackCount;
                throw new PlaylistTransferException(
                    preview with { DestinationPlaylistId = destinationId, AddedTrackCount = addedTrackCount },
                    exception.InnerException ?? exception);
            }
            catch (Exception exception)
            {
                throw new PlaylistTransferException(
                    preview with { DestinationPlaylistId = destinationId, AddedTrackCount = addedTrackCount },
                    exception);
            }
        }

        return preview with { DestinationPlaylistId = destinationId, AddedTrackCount = addedTrackCount };
    }
}
