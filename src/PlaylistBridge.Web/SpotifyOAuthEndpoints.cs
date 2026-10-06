using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace PlaylistBridge.Web;

public static class SpotifyOAuthEndpoints
{
    public const string SessionCookie = "PlaylistBridge.Spotify.Session";
    private const string DefaultRedirectUri = "https://127.0.0.1:7075/auth/spotify/callback";

    public static IEndpointRouteBuilder MapSpotifyOAuth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/spotify/connect", ConnectAsync).DisableAntiforgery();
        endpoints.MapGet("/auth/spotify/callback", CallbackAsync);
        endpoints.MapPost("/auth/spotify/disconnect", Disconnect).DisableAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ConnectAsync(
        HttpContext context,
        SpotifyOAuthSessionStore sessions,
        IConfiguration configuration)
    {
        var form = await context.Request.ReadFormAsync();
        var clientId = form["clientId"].ToString().Trim();
        var clientSecret = form["clientSecret"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return Results.Redirect("/connections?spotify=missing_credentials");

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var redirectUri = configuration["Spotify:RedirectUri"] ?? DefaultRedirectUri;
        var handshake = new SpotifyHandshake(clientId, clientSecret, state, redirectUri);
        sessions.AddHandshake(handshake);

        const string scopes = "playlist-read-private playlist-read-collaborative user-read-private";
        var authorizationUrl = "https://accounts.spotify.com/authorize?" + BuildQuery(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["scope"] = scopes,
            ["redirect_uri"] = redirectUri,
            ["state"] = state
        });
        return Results.Redirect(authorizationUrl);
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        SpotifyOAuthSessionStore sessions,
        string? code,
        string? state,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Results.Redirect($"/connections?spotify={Uri.EscapeDataString(error)}");
        if (string.IsNullOrWhiteSpace(state))
            return Results.Redirect("/connections?spotify=state_mismatch");
        var handshake = sessions.TakeHandshake(state);
        if (handshake is null) return Results.Redirect("/connections?spotify=session_expired");
        if (string.IsNullOrWhiteSpace(code) ||
            !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(state),
                System.Text.Encoding.UTF8.GetBytes(handshake.State)))
            return Results.Redirect("/connections?spotify=state_mismatch");

        using var http = httpClientFactory.CreateClient();
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        tokenRequest.Headers.Authorization = SpotifyOAuthSessionStore.Basic(handshake.ClientId, handshake.ClientSecret);
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = handshake.RedirectUri
        });
        using var tokenResponse = await http.SendAsync(tokenRequest, cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
            return Results.Redirect($"/connections?spotify=token_exchange_{(int)tokenResponse.StatusCode}");

        var token = await tokenResponse.Content.ReadFromJsonAsync<SpotifyTokenResponse>(cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.RefreshToken))
            return Results.Redirect("/connections?spotify=no_refresh_token");

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var profileResponse = await http.SendAsync(profileRequest, cancellationToken);
        if (!profileResponse.IsSuccessStatusCode)
            return Results.Redirect($"/connections?spotify=profile_{(int)profileResponse.StatusCode}");
        var profile = await profileResponse.Content.ReadFromJsonAsync<SpotifyProfile>(cancellationToken);

        var sessionId = sessions.Add(new SpotifyOAuthSession(
            handshake.ClientId,
            handshake.ClientSecret,
            token.AccessToken,
            token.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
            profile?.DisplayName ?? "Spotify account"));
        context.Response.Cookies.Append(SessionCookie, sessionId, SecureCookie(context, TimeSpan.FromDays(180)));
        return Results.Redirect("/connections?spotify=connected");
    }

    private static IResult Disconnect(HttpContext context, SpotifyOAuthSessionStore sessions)
    {
        context.Request.Cookies.TryGetValue(SessionCookie, out var sessionId);
        sessions.Remove(sessionId);
        context.Response.Cookies.Delete(SessionCookie);
        return Results.Redirect("/connections?spotify=disconnected");
    }

    private static CookieOptions SecureCookie(HttpContext context, TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        MaxAge = lifetime
    };

    private static string BuildQuery(IReadOnlyDictionary<string, string> values) =>
        string.Join('&', values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private sealed record SpotifyProfile([property: JsonPropertyName("display_name")] string? DisplayName);
}
