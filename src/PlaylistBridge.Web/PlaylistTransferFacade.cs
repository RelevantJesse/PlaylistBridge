using PlaylistBridge.Core;
using PlaylistBridge.Providers;

namespace PlaylistBridge.Web;

public sealed class PlaylistTransferFacade(
    IHttpClientFactory httpClientFactory,
    PlaylistTransferService transferService,
    ServiceCredentialStore credentials,
    ILogger<PlaylistTransferFacade> logger)
{
    public static IReadOnlyList<MusicServiceOption> Services { get; } =
    [
        new(MusicService.Spotify, "Spotify", true, false, "https://open.spotify.com/playlist/…"),
        new(MusicService.AppleMusic, "Apple Music", false, true, "Apple Music playlist URL"),
        new(MusicService.YouTube, "YouTube", true, true, "https://www.youtube.com/playlist?list=…")
    ];

    public static IEnumerable<MusicServiceOption> Sources => Services.Where(x => x.CanReadPlaylists);
    public static IEnumerable<MusicServiceOption> Destinations => Services.Where(x => x.CanCreatePlaylists);

    public bool IsRouteConfigured(MusicService source, MusicService destination) =>
        MissingConfiguration(source, destination).Count == 0;

    public IReadOnlyList<string> MissingConfiguration(MusicService source, MusicService destination) =>
        RequiredCredentials(source).Concat(RequiredCredentials(destination)).Distinct().ToArray();

    public bool IsServiceConfigured(MusicService service) => RequiredCredentials(service).Count == 0;

    public async Task<TransferResult> PreviewAsync(
        MusicService source,
        MusicService destination,
        string playlistIdOrUrl,
        CancellationToken cancellationToken = default)
    {
        EnsureRouteConfigured(source, destination);
        logger.LogInformation("Preview started: {Source} to {Destination}", source, destination);
        try
        {
            var result = await transferService.PreviewAsync(
                await CreateProviderAsync(source, cancellationToken),
                await CreateProviderAsync(destination, cancellationToken),
                NormalizePlaylistReference(source, playlistIdOrUrl),
                cancellationToken);
            logger.LogInformation(
                "Preview completed: {Source} to {Destination}; {TrackCount} tracks, {MatchedCount} accepted matches",
                source, destination, result.Source.Tracks.Count, result.MatchedCount);
            return result;
        }
        catch (Exception exception)
        {
            LogFailure(exception, "Preview failed: {Source} to {Destination}", source, destination);
            throw;
        }
    }

    public async Task<TransferResult> CommitAsync(
        MusicService source,
        MusicService destination,
        TransferResult preview,
        CancellationToken cancellationToken = default)
    {
        EnsureRouteConfigured(source, destination);
        logger.LogInformation(
            "Transfer started: {Source} to {Destination}; destination playlist {DestinationPlaylistId}; progress {AddedCount}/{MatchedCount}",
            source, destination, preview.DestinationPlaylistId ?? "not-created", preview.AddedTrackCount, preview.MatchedCount);
        try
        {
            var result = await transferService.CommitAsync(
                await CreateProviderAsync(destination, cancellationToken), preview, DisplayName(source), cancellationToken);
            logger.LogInformation(
                "Transfer completed: {Source} to {Destination}; destination playlist {DestinationPlaylistId}; {AddedCount} tracks added",
                source, destination, result.DestinationPlaylistId, result.AddedTrackCount);
            return result;
        }
        catch (Exception exception)
        {
            LogFailure(exception, "Transfer failed: {Source} to {Destination}", source, destination);
            throw;
        }
    }

    public async Task<IReadOnlyList<ServiceConnectionResult>> TestConfiguredConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ServiceConnectionResult>();
        foreach (var service in Services.Select(x => x.Id).Distinct().Where(IsServiceConfigured))
            results.Add(await TestConnectionAsync(service, cancellationToken));
        return results;
    }

    private async Task<ServiceConnectionResult> TestConnectionAsync(
        MusicService service, CancellationToken cancellationToken)
    {
        using var http = httpClientFactory.CreateClient();
        string url;
        switch (service)
        {
            case MusicService.Spotify:
                http.DefaultRequestHeaders.Authorization = Bearer(await credentials.GetSpotifyTokenAsync(cancellationToken));
                url = "https://api.spotify.com/v1/me";
                break;
            case MusicService.AppleMusic:
                http.DefaultRequestHeaders.Authorization = Bearer(credentials.AppleDeveloperToken);
                http.DefaultRequestHeaders.Add("Music-User-Token", credentials.AppleUserToken);
                url = "https://api.music.apple.com/v1/me/library/playlists?limit=1";
                break;
            case MusicService.YouTube:
                http.DefaultRequestHeaders.Authorization = Bearer(await credentials.GetYouTubeTokenAsync(cancellationToken));
                url = "https://www.googleapis.com/youtube/v3/channels?part=id&mine=true";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(service));
        }

        using var response = await http.GetAsync(url, cancellationToken);
        return new ServiceConnectionResult(service, response.IsSuccessStatusCode, (int)response.StatusCode);
    }

    private async Task<IMusicProvider> CreateProviderAsync(
        MusicService service, CancellationToken cancellationToken) => service switch
        {
            MusicService.Spotify => new SpotifyProvider(
                httpClientFactory.CreateClient(),
                new SpotifyOptions(await credentials.GetSpotifyTokenAsync(cancellationToken))),
            MusicService.AppleMusic => new AppleMusicProvider(
                httpClientFactory.CreateClient(),
                new AppleMusicOptions(credentials.AppleDeveloperToken, credentials.AppleUserToken, credentials.Storefront)),
            MusicService.YouTube => new YouTubeProvider(
                httpClientFactory.CreateClient(),
                new YouTubeOptions(await credentials.GetYouTubeTokenAsync(cancellationToken))),
            _ => throw new ArgumentOutOfRangeException(nameof(service))
        };

    private IReadOnlyList<string> RequiredCredentials(MusicService service)
    {
        var missing = new List<string>();
        if (service == MusicService.Spotify && !credentials.HasSpotifyToken)
            missing.Add("Spotify access token");
        if (service == MusicService.AppleMusic)
        {
            if (!credentials.HasAppleDeveloperToken) missing.Add("Apple Music developer token");
            if (!credentials.HasAppleUserToken) missing.Add("Apple Music user token");
        }
        if (service == MusicService.YouTube && !credentials.HasYouTubeToken)
            missing.Add("YouTube OAuth access token");
        return missing;
    }

    private void EnsureRouteConfigured(MusicService source, MusicService destination)
    {
        var missing = MissingConfiguration(source, destination);
        if (missing.Count > 0)
            throw new InvalidOperationException($"Missing configuration: {string.Join(", ", missing)}");
    }

    private static string NormalizePlaylistReference(MusicService service, string value)
    {
        if (service == MusicService.YouTube) return YouTubeProvider.ExtractPlaylistId(value);
        if (service != MusicService.Spotify) return value.Trim();
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return trimmed;
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        var playlistIndex = Array.FindIndex(segments, x => x.Equals("playlist", StringComparison.OrdinalIgnoreCase));
        return playlistIndex >= 0 && playlistIndex + 1 < segments.Length ? segments[playlistIndex + 1] : trimmed;
    }

    private static string DisplayName(MusicService service) =>
        Services.First(x => x.Id == service).Name;

    private static System.Net.Http.Headers.AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private void LogFailure(Exception exception, string message, params object?[] arguments)
    {
        var providerError = FindProviderError(exception);
        if (providerError is not null)
        {
            logger.LogWarning(
                exception,
                message + "; provider {Provider}; operation {Operation}; HTTP {StatusCode}; code {ProviderErrorCode}",
                arguments.Concat(new object?[]
                {
                    providerError.ProviderName,
                    providerError.Operation,
                    providerError.StatusCode,
                    providerError.ErrorCode ?? "not-supplied"
                }).ToArray());
            return;
        }

        logger.LogError(exception, message, arguments);
    }

    private static ProviderApiException? FindProviderError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is ProviderApiException providerError) return providerError;
        return null;
    }
}

public sealed record ServiceConnectionResult(MusicService Service, bool Connected, int StatusCode);
