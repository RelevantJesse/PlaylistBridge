namespace PlaylistBridge.Web;

public enum MusicService
{
    Spotify,
    AppleMusic,
    YouTube
}

public sealed record MusicServiceOption(
    MusicService Id,
    string Name,
    bool CanReadPlaylists,
    bool CanCreatePlaylists,
    string PlaylistPlaceholder);
