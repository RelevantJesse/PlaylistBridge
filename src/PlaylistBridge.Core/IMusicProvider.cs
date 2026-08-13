namespace PlaylistBridge.Core;

public interface IMusicProvider
{
    string Name { get; }
    Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Track>> SearchTracksAsync(Track source, int limit = 10, CancellationToken cancellationToken = default);
    Task<string> CreatePlaylistAsync(string name, string? description, CancellationToken cancellationToken = default);
    Task AddTracksAsync(string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default);
}
