namespace PlaylistBridge.Providers;

public sealed record SpotifyOptions(string AccessToken)
{
    public static SpotifyOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("PLAYLISTBRIDGE_SPOTIFY_TOKEN") ?? string.Empty);
}
