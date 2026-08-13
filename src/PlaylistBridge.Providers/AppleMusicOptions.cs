namespace PlaylistBridge.Providers;

public sealed record AppleMusicOptions(string DeveloperToken, string MusicUserToken, string Storefront = "us")
{
    public static AppleMusicOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("PLAYLISTBRIDGE_APPLE_DEVELOPER_TOKEN") ?? string.Empty,
        Environment.GetEnvironmentVariable("PLAYLISTBRIDGE_APPLE_USER_TOKEN") ?? string.Empty,
        Environment.GetEnvironmentVariable("PLAYLISTBRIDGE_APPLE_STOREFRONT") ?? "us");
}
