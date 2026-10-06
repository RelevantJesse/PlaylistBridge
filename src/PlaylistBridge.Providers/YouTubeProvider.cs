using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml;
using PlaylistBridge.Core;

namespace PlaylistBridge.Providers;

public sealed class YouTubeProvider : IMusicProvider
{
    private const int MaximumWriteAttempts = 4;
    private readonly HttpClient _http;

    public YouTubeProvider(HttpClient http, YouTubeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessToken))
            throw new ArgumentException("Add a YouTube OAuth access token on the Connections page.", nameof(options));
        _http = http;
        _http.BaseAddress ??= new Uri("https://www.googleapis.com/youtube/v3/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
    }

    public string Name => "YouTube";

    public async Task<Playlist> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        playlistId = ExtractPlaylistId(playlistId);
        using var playlistJson = await GetJsonAsync(
            $"playlists?part=snippet&id={Uri.EscapeDataString(playlistId)}", cancellationToken);
        var playlistItems = playlistJson.RootElement.GetProperty("items");
        if (playlistItems.GetArrayLength() == 0)
            throw new InvalidOperationException("YouTube could not find that playlist or the connected account cannot access it.");

        var playlistSnippet = playlistItems[0].GetProperty("snippet");
        var rawTracks = new List<RawVideo>();
        string? pageToken = null;
        do
        {
            var path = $"playlistItems?part=snippet,contentDetails&maxResults=50&playlistId={Uri.EscapeDataString(playlistId)}";
            if (!string.IsNullOrWhiteSpace(pageToken)) path += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            using var page = await GetJsonAsync(path, cancellationToken);
            foreach (var item in page.RootElement.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                var details = item.GetProperty("contentDetails");
                var videoId = details.GetProperty("videoId").GetString();
                if (string.IsNullOrWhiteSpace(videoId)) continue;
                rawTracks.Add(new RawVideo(
                    videoId,
                    snippet.GetProperty("title").GetString() ?? string.Empty,
                    snippet.TryGetProperty("videoOwnerChannelTitle", out var owner)
                        ? owner.GetString() ?? string.Empty
                        : snippet.GetProperty("channelTitle").GetString() ?? string.Empty));
            }
            pageToken = page.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrWhiteSpace(pageToken));

        var durations = await GetDurationsAsync(rawTracks.Select(x => x.Id), cancellationToken);
        return new Playlist(
            playlistId,
            playlistSnippet.GetProperty("title").GetString() ?? "YouTube playlist",
            playlistSnippet.TryGetProperty("description", out var description) ? description.GetString() : null,
            rawTracks.Select(video => ToTrack(video, durations.GetValueOrDefault(video.Id))).ToArray());
    }

    public async Task<IReadOnlyList<Track>> SearchTracksAsync(
        Track source, int limit = 10, CancellationToken cancellationToken = default)
    {
        var query = Uri.EscapeDataString($"{source.Artist} {source.Title}");
        var count = Math.Clamp(limit, 1, 50);
        using var search = await GetJsonAsync(
            $"search?part=snippet&type=video&videoCategoryId=10&maxResults={count}&q={query}", cancellationToken);
        var videos = search.RootElement.GetProperty("items").EnumerateArray().Select(item =>
        {
            var snippet = item.GetProperty("snippet");
            return new RawVideo(
                item.GetProperty("id").GetProperty("videoId").GetString() ?? string.Empty,
                snippet.GetProperty("title").GetString() ?? string.Empty,
                snippet.GetProperty("channelTitle").GetString() ?? string.Empty);
        }).Where(video => video.Id.Length > 0).ToArray();

        var durations = await GetDurationsAsync(videos.Select(x => x.Id), cancellationToken);
        return videos.Select(video => ToTrack(video, durations.GetValueOrDefault(video.Id))).ToArray();
    }

    public async Task<string> CreatePlaylistAsync(
        string name, string? description, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            snippet = new { title = name, description = description ?? string.Empty },
            status = new { privacyStatus = "private" }
        };
        using var response = await _http.PostAsJsonAsync("playlists?part=snippet,status", body, cancellationToken);
        await ProviderHttp.EnsureSuccessAsync(response, Name, "create the playlist", cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return json.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("YouTube did not return a playlist id.");
    }

    public async Task AddTracksAsync(
        string playlistId, IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default)
    {
        var addedTrackCount = 0;
        foreach (var trackId in trackIds)
        {
            var body = new
            {
                snippet = new
                {
                    playlistId,
                    resourceId = new { kind = "youtube#video", videoId = trackId }
                }
            };
            try
            {
                await AddTrackWithRetryAsync(body, cancellationToken);
                addedTrackCount++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new TrackWriteException(Name, addedTrackCount, trackIds.Count, exception);
            }
        }
    }

    private async Task AddTrackWithRetryAsync(object body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await _http.PostAsJsonAsync("playlistItems?part=snippet", body, cancellationToken);
            try
            {
                await ProviderHttp.EnsureSuccessAsync(response, Name, "add a track to the playlist", cancellationToken);
                return;
            }
            catch (ProviderApiException exception) when (
                attempt < MaximumWriteAttempts && IsTransientWriteFailure(exception))
            {
                var backoff = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1) + Random.Shared.Next(50, 201));
                await Task.Delay(backoff, cancellationToken);
            }
        }
    }

    private static bool IsTransientWriteFailure(ProviderApiException exception) =>
        exception.StatusCode is 408 or 429 or >= 500 ||
        exception.StatusCode == 409 && exception.ErrorCode is "SERVICE_UNAVAILABLE" or "ABORTED";

    public static string ExtractPlaylistId(string value)
    {
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return trimmed;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length == 2 && pieces[0].Equals("list", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pieces[1]);
        }
        return trimmed;
    }

    private async Task<Dictionary<string, TimeSpan?>> GetDurationsAsync(
        IEnumerable<string> videoIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, TimeSpan?>(StringComparer.Ordinal);
        foreach (var batch in videoIds.Distinct().Chunk(50))
        {
            if (batch.Length == 0) continue;
            using var json = await GetJsonAsync(
                $"videos?part=contentDetails&id={Uri.EscapeDataString(string.Join(',', batch))}", cancellationToken);
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var duration = item.GetProperty("contentDetails").GetProperty("duration").GetString();
                if (!string.IsNullOrWhiteSpace(id)) result[id] = ParseDuration(duration);
            }
        }
        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        await ProviderHttp.EnsureSuccessAsync(response, Name, "read data", cancellationToken);
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    private static Track ToTrack(RawVideo video, TimeSpan? duration)
    {
        var metadata = YouTubeTrackMetadataParser.Parse(video.Title, video.Channel);
        return new Track(video.Id, metadata.Title, metadata.Artist, Duration: duration);
    }

    private static TimeSpan? ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return null;
        try { return XmlConvert.ToTimeSpan(duration); }
        catch (FormatException) { return null; }
    }

    private sealed record RawVideo(string Id, string Title, string Channel);
}
