using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PlaylistBridge.Core;

namespace PlaylistBridge.Providers;

public sealed class AppleMusicProvider : IMusicProvider
{
    private readonly HttpClient _http;
    private readonly AppleMusicOptions _options;

    public AppleMusicProvider(HttpClient http, AppleMusicOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DeveloperToken) || string.IsNullOrWhiteSpace(options.MusicUserToken))
            throw new ArgumentException("Set both Apple Music token environment variables.", nameof(options));
        _options = options;
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.music.apple.com/v1/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.DeveloperToken);
        _http.DefaultRequestHeaders.Remove("Music-User-Token");
        _http.DefaultRequestHeaders.Add("Music-User-Token", options.MusicUserToken);
    }

    public string Name => "Apple Music";

    public Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Apple Music source support is planned but not implemented in this first slice.");

    public async Task<IReadOnlyList<Track>> SearchTracksAsync(Track source, int limit = 10, CancellationToken cancellationToken = default)
    {
        var term = Uri.EscapeDataString($"{source.Title} {source.Artist}");
        using var response = await _http.GetAsync(
            $"catalog/{Uri.EscapeDataString(_options.Storefront)}/search?types=songs&limit={Math.Clamp(limit, 1, 25)}&term={term}",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("results", out var results) ||
            !results.TryGetProperty("songs", out var songs) ||
            !songs.TryGetProperty("data", out var data)) return [];

        return data.EnumerateArray().Select(ToTrack).ToArray();
    }

    public async Task<string> CreatePlaylistAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        var body = new { attributes = new { name, description = description ?? string.Empty } };
        using var response = await _http.PostAsJsonAsync("me/library/playlists", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return json.RootElement.GetProperty("data")[0].GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Apple Music did not return a playlist id.");
    }

    public async Task AddTracksAsync(string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default)
    {
        if (trackIds.Count == 0) return;
        var body = new { data = trackIds.Select(id => new { id, type = "songs" }).ToArray() };
        using var response = await _http.PostAsJsonAsync(
            $"me/library/playlists/{Uri.EscapeDataString(playlistId)}/tracks", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Track ToTrack(JsonElement item)
    {
        var attributes = item.GetProperty("attributes");
        return new Track(
            item.GetProperty("id").GetString() ?? string.Empty,
            attributes.GetProperty("name").GetString() ?? string.Empty,
            attributes.GetProperty("artistName").GetString() ?? string.Empty,
            attributes.TryGetProperty("albumName", out var album) ? album.GetString() : null,
            attributes.TryGetProperty("durationInMillis", out var duration) ? TimeSpan.FromMilliseconds(duration.GetInt32()) : null,
            attributes.TryGetProperty("isrc", out var isrc) ? isrc.GetString() : null,
            attributes.TryGetProperty("contentRating", out var rating) ? rating.GetString() == "explicit" : null);
    }
}
