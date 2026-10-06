namespace PlaylistBridge.Providers;

public sealed record YouTubeOptions(string AccessToken)
{
    public static YouTubeOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("PLAYLISTBRIDGE_YOUTUBE_TOKEN") ?? string.Empty);
}
