using System.Net.Http.Headers;
using System.Text.Json;
using PlaylistBridge.Core;

namespace PlaylistBridge.Providers;

public sealed class SpotifyProvider : IMusicProvider
{
    private readonly HttpClient _http;

    public SpotifyProvider(HttpClient http, SpotifyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessToken))
            throw new ArgumentException("Set PLAYLISTBRIDGE_SPOTIFY_TOKEN.", nameof(options));
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.spotify.com/v1/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
    }

    public string Name => "Spotify";

    public async Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        using var metadata = await GetJsonAsync($"playlists/{Uri.EscapeDataString(playlistId)}?fields=id,name,description", cancellationToken);
        var root = metadata.RootElement;
        var tracks = new List<Track>();
        string? next = $"playlists/{Uri.EscapeDataString(playlistId)}/items?limit=50";

        while (next is not null)
        {
            using var page = await GetJsonAsync(next, cancellationToken);
            foreach (var item in page.RootElement.GetProperty("items").EnumerateArray())
            {
                var hasTrack = item.TryGetProperty("item", out var track) || item.TryGetProperty("track", out track);
                if (!hasTrack || track.ValueKind != JsonValueKind.Object ||
                    track.TryGetProperty("type", out var type) && type.GetString() != "track") continue;
                var artists = track.GetProperty("artists").EnumerateArray()
                    .Select(x => x.GetProperty("name").GetString()).Where(x => x is not null);
                tracks.Add(new Track(
                    track.GetProperty("id").GetString() ?? string.Empty,
                    track.GetProperty("name").GetString() ?? string.Empty,
                    string.Join(", ", artists),
                    track.TryGetProperty("album", out var album) ? album.GetProperty("name").GetString() : null,
                    track.TryGetProperty("duration_ms", out var duration) ? TimeSpan.FromMilliseconds(duration.GetInt32()) : null,
                    track.TryGetProperty("external_ids", out var ids) && ids.TryGetProperty("isrc", out var isrc) ? isrc.GetString() : null,
                    track.TryGetProperty("explicit", out var explicitValue) ? explicitValue.GetBoolean() : null));
            }
            next = page.RootElement.TryGetProperty("next", out var nextElement) && nextElement.ValueKind == JsonValueKind.String
                ? nextElement.GetString()
                : null;
        }

        return new Playlist(
            root.GetProperty("id").GetString() ?? playlistId,
            root.GetProperty("name").GetString() ?? "Transferred playlist",
            root.TryGetProperty("description", out var description) ? description.GetString() : null,
            tracks);
    }

    public Task<IReadOnlyList<Track>> SearchTracksAsync(Track source, int limit = 10, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Spotify destination support is planned but not implemented in this first slice.");

    public Task<string> CreatePlaylistAsync(string name, string? description, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Spotify destination support is planned but not implemented in this first slice.");

    public Task AddTracksAsync(string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Spotify destination support is planned but not implemented in this first slice.");

    private async Task<JsonDocument> GetJsonAsync(string pathOrUrl, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(pathOrUrl, cancellationToken);
        await ProviderHttp.EnsureSuccessAsync(response, Name, "read playlist data", cancellationToken);
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
    }
}
