using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace PlaylistBridge.Web;

public sealed class SpotifyOAuthSessionStore(
    IHttpClientFactory httpClientFactory,
    EncryptedConnectionStore persistentStore)
{
    private readonly ConcurrentDictionary<string, SpotifyOAuthSession> _sessions =
        new(persistentStore.SpotifySessions);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new();

    public void AddHandshake(SpotifyHandshake handshake)
    {
        persistentStore.SaveSpotifyHandshake(
            handshake.State,
            new PendingSpotifyHandshake(handshake, DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    public SpotifyHandshake? TakeHandshake(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        return persistentStore.TakeSpotifyHandshake(state)?.Handshake;
    }

    public string Add(SpotifyOAuthSession session)
    {
        var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        _sessions[id] = session;
        persistentStore.SaveSpotifySession(id, session);
        return id;
    }

    public bool Contains(string? id) => id is not null && _sessions.ContainsKey(id);

    public string? DisplayName(string? id) =>
        id is not null && _sessions.TryGetValue(id, out var session) ? session.DisplayName : null;

    public void Remove(string? id)
    {
        if (id is null) return;
        _sessions.TryRemove(id, out _);
        persistentStore.RemoveSpotifySession(id);
        if (_refreshLocks.TryRemove(id, out var gate)) gate.Dispose();
    }

    public async Task<string?> GetAccessTokenAsync(string? id, CancellationToken cancellationToken = default)
    {
        if (id is null || !_sessions.TryGetValue(id, out var session)) return null;
        if (session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return session.AccessToken;

        var gate = _refreshLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!_sessions.TryGetValue(id, out session)) return null;
            if (session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return session.AccessToken;

            using var http = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            request.Headers.Authorization = Basic(session.ClientId, session.ClientSecret);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = session.RefreshToken
            });
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
                    Remove(id);
                response.EnsureSuccessStatusCode();
            }

            var token = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Spotify returned an empty refresh response.");
            session = session with
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken ?? session.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)
            };
            _sessions[id] = session;
            persistentStore.SaveSpotifySession(id, session);
            return session.AccessToken;
        }
        finally { gate.Release(); }
    }

    internal static AuthenticationHeaderValue Basic(string clientId, string clientSecret) => new(
        "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
}

public sealed record SpotifyOAuthSession(
    string ClientId,
    string ClientSecret,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string DisplayName);

public sealed record SpotifyHandshake(string ClientId, string ClientSecret, string State, string RedirectUri);
internal sealed record PendingSpotifyHandshake(SpotifyHandshake Handshake, DateTimeOffset ExpiresAt);

public sealed record SpotifyTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope);
