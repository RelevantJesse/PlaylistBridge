using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PlaylistBridge.Web;

public sealed class ServiceCredentialStore(
    IHttpContextAccessor httpContextAccessor,
    IHttpClientFactory httpClientFactory,
    SpotifyOAuthSessionStore spotifySessions,
    EncryptedConnectionStore persistentStore)
{
    private readonly SemaphoreSlim _youTubeRefreshLock = new(1, 1);
    private readonly string? _spotifySessionId =
        httpContextAccessor.HttpContext?.Request.Cookies[SpotifyOAuthEndpoints.SessionCookie];
    private string? _spotifyToken;
    private StoredServiceCredentials _stored = persistentStore.ServiceCredentials;

    public string SpotifyToken => ValueOrEnvironment(_spotifyToken, "PLAYLISTBRIDGE_SPOTIFY_TOKEN");
    public string AppleDeveloperToken => ValueOrEnvironment(
        _stored.AppleDeveloperToken, "PLAYLISTBRIDGE_APPLE_DEVELOPER_TOKEN");
    public string AppleUserToken => ValueOrEnvironment(
        _stored.AppleUserToken, "PLAYLISTBRIDGE_APPLE_USER_TOKEN");
    public string Storefront => ValueOrEnvironment(
        _stored.Storefront, "PLAYLISTBRIDGE_APPLE_STOREFRONT", "us");

    public bool HasSpotifyToken =>
        !string.IsNullOrWhiteSpace(SpotifyToken) || spotifySessions.Contains(_spotifySessionId);
    public string? SpotifyDisplayName => spotifySessions.DisplayName(_spotifySessionId);
    public bool HasAppleDeveloperToken => !string.IsNullOrWhiteSpace(AppleDeveloperToken);
    public bool HasAppleUserToken => !string.IsNullOrWhiteSpace(AppleUserToken);
    public bool HasYouTubeToken => !string.IsNullOrWhiteSpace(CurrentYouTubeAccessToken) || HasYouTubeRefreshConfiguration;
    public bool HasYouTubeRefreshToken => HasYouTubeRefreshConfiguration;
    public bool HasAnyCredential => HasSpotifyToken || HasAppleDeveloperToken || HasAppleUserToken || HasYouTubeToken;

    public void Update(
        string? spotifyToken,
        string? appleDeveloperToken,
        string? appleUserToken,
        string? youTubeAccessToken,
        string? youTubeClientId,
        string? youTubeClientSecret,
        string? youTubeRefreshToken,
        string? storefront)
    {
        if (!string.IsNullOrWhiteSpace(spotifyToken)) _spotifyToken = spotifyToken.Trim();
        var refreshConfigurationChanged =
            !string.IsNullOrWhiteSpace(youTubeClientId) ||
            !string.IsNullOrWhiteSpace(youTubeClientSecret) ||
            !string.IsNullOrWhiteSpace(youTubeRefreshToken);
        _stored = _stored with
        {
            AppleDeveloperToken = Updated(_stored.AppleDeveloperToken, appleDeveloperToken),
            AppleUserToken = Updated(_stored.AppleUserToken, appleUserToken),
            Storefront = string.IsNullOrWhiteSpace(storefront) ? _stored.Storefront : storefront.Trim().ToLowerInvariant(),
            YouTubeAccessToken = Updated(_stored.YouTubeAccessToken, youTubeAccessToken),
            YouTubeAccessTokenExpiresAt = refreshConfigurationChanged ? null : _stored.YouTubeAccessTokenExpiresAt,
            YouTubeClientId = Updated(_stored.YouTubeClientId, youTubeClientId),
            YouTubeClientSecret = Updated(_stored.YouTubeClientSecret, youTubeClientSecret),
            YouTubeRefreshToken = Updated(_stored.YouTubeRefreshToken, youTubeRefreshToken)
        };
        persistentStore.SaveServiceCredentials(_stored);
    }

    public void ClearSavedValues()
    {
        _spotifyToken = null;
        _stored = new();
        persistentStore.SaveServiceCredentials(_stored);
    }

    public async Task<string> GetSpotifyTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(SpotifyToken)) return SpotifyToken;
        return await spotifySessions.GetAccessTokenAsync(_spotifySessionId, cancellationToken)
            ?? throw new InvalidOperationException("Connect Spotify on the Connections page.");
    }

    public async Task<string> GetYouTubeTokenAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = CurrentYouTubeAccessToken;
        if (!HasYouTubeRefreshConfiguration)
            return accessToken;

        if (!string.IsNullOrWhiteSpace(accessToken) &&
            _stored.YouTubeAccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return accessToken;

        await _youTubeRefreshLock.WaitAsync(cancellationToken);
        try
        {
            accessToken = CurrentYouTubeAccessToken;
            if (!string.IsNullOrWhiteSpace(accessToken) &&
                _stored.YouTubeAccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return accessToken;

            using var http = httpClientFactory.CreateClient();
            using var response = await http.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = YouTubeClientId,
                    ["client_secret"] = YouTubeClientSecret,
                    ["refresh_token"] = YouTubeRefreshToken,
                    ["grant_type"] = "refresh_token"
                }),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Google could not refresh the YouTube access token (HTTP {(int)response.StatusCode}): {detail}");
            }

            var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Google returned an empty token refresh response.");
            _stored = _stored with
            {
                YouTubeAccessToken = token.AccessToken,
                YouTubeAccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)
            };
            persistentStore.SaveServiceCredentials(_stored);
            return token.AccessToken;
        }
        finally { _youTubeRefreshLock.Release(); }
    }

    private string CurrentYouTubeAccessToken => ValueOrEnvironment(
        _stored.YouTubeAccessToken, "PLAYLISTBRIDGE_YOUTUBE_TOKEN");
    private string YouTubeClientId => ValueOrEnvironment(
        _stored.YouTubeClientId, "PLAYLISTBRIDGE_YOUTUBE_CLIENT_ID");
    private string YouTubeClientSecret => ValueOrEnvironment(
        _stored.YouTubeClientSecret, "PLAYLISTBRIDGE_YOUTUBE_CLIENT_SECRET");
    private string YouTubeRefreshToken => ValueOrEnvironment(
        _stored.YouTubeRefreshToken, "PLAYLISTBRIDGE_YOUTUBE_REFRESH_TOKEN");
    private bool HasYouTubeRefreshConfiguration =>
        !string.IsNullOrWhiteSpace(YouTubeClientId) &&
        !string.IsNullOrWhiteSpace(YouTubeClientSecret) &&
        !string.IsNullOrWhiteSpace(YouTubeRefreshToken);

    private static string? Updated(string? current, string? submitted) =>
        string.IsNullOrWhiteSpace(submitted) ? current : submitted.Trim();
    private static string ValueOrEnvironment(string? storedValue, string environmentName, string fallback = "") =>
        storedValue ?? Environment.GetEnvironmentVariable(environmentName) ?? fallback;

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
