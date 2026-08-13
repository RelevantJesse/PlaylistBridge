# PlaylistBridge

PlaylistBridge is a .NET 8 command-line application for moving playlists between music services. The first working path reads a Spotify playlist, finds safe matches in the Apple Music catalog, and creates an Apple Music library playlist.

The matching engine uses ISRC when both catalogs expose it. Otherwise, it scores normalized title, artist, album, duration, explicit status, and version markers such as “live” or “remaster.” Close or weak matches are left for review instead of being added automatically.

## What is included

- Provider-neutral playlist, track, match, and transfer models
- `IMusicProvider` abstraction for future source and destination services
- Spotify playlist reader using the Spotify Web API
- Apple Music catalog search and library playlist writer
- Preview mode that performs matching without writing to Apple Music
- Unit tests covering ISRC, normalization, duration, ambiguity, explicit status, and version mismatches

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or a newer compatible SDK
- A Spotify user access token able to read the source playlist
- An Apple Music developer token and Music User Token
- An active Apple Music subscription for the destination user

The implementation follows Spotify’s [Get Playlist](https://developer.spotify.com/documentation/web-api/reference/get-playlist) and [Get Playlist Items](https://developer.spotify.com/documentation/web-api/reference/get-playlists-items) endpoints, plus Apple’s [catalog search](https://developer.apple.com/documentation/applemusicapi/search-for-catalog-resources), [playlist creation](https://developer.apple.com/documentation/applemusicapi/create-a-new-library-playlist), and [playlist track](https://developer.apple.com/documentation/applemusicapi/add-tracks-to-a-library-playlist) endpoints.

## Setup

Clone the repository and restore it:

```powershell
dotnet restore
dotnet build
dotnet test
```

Set credentials in your current shell. The names are also documented in `.env.example`; the application does not automatically load `.env` files.

```powershell
$env:PLAYLISTBRIDGE_SPOTIFY_TOKEN = "your Spotify user access token"
$env:PLAYLISTBRIDGE_APPLE_DEVELOPER_TOKEN = "your Apple developer token"
$env:PLAYLISTBRIDGE_APPLE_USER_TOKEN = "your Apple Music user token"
$env:PLAYLISTBRIDGE_APPLE_STOREFRONT = "us"
```

For production, obtain and refresh tokens through each provider’s authorization flow rather than checking credentials into source control. See [Spotify authorization](https://developer.spotify.com/documentation/web-api/concepts/authorization) and [Apple Music user authentication](https://developer.apple.com/documentation/applemusicapi/user-authentication-for-musickit).

## Run

Preview a transfer without changing Apple Music:

```powershell
dotnet run --project src/PlaylistBridge.Cli -- --spotify-playlist "https://open.spotify.com/playlist/PLAYLIST_ID" --dry-run
```

Create the Apple Music playlist and add accepted matches:

```powershell
dotnet run --project src/PlaylistBridge.Cli -- --spotify-playlist PLAYLIST_ID
```

The process exits with code `1` when one or more tracks need review, even if the accepted tracks were transferred. This makes partial transfers visible in scripts and CI.

## Architecture

```text
SpotifyProvider ──reads──> PlaylistTransferService ──searches/writes──> AppleMusicProvider
                                  │
                                  └── TrackMatcher
                                      ├── exact ISRC
                                      └── normalized metadata score
```

- `PlaylistBridge.Core` contains models, provider contracts, matching, and orchestration.
- `PlaylistBridge.Providers` contains HTTP adapters and provider configuration.
- `PlaylistBridge.Cli` is the runnable entry point.
- `PlaylistBridge.Core.Tests` protects matching behavior.

## Current limits and next steps

- Token acquisition and refresh are deliberately external placeholders for now.
- Apple Music-to-Spotify and additional providers can implement the same provider contract.
- Ambiguous matches are printed to the console; an interactive review workflow is a logical next feature.
- A production app should add retry/rate-limit handling, secure token storage, structured logging, and resumable transfers.

## Security

Never commit access tokens, Apple private keys (`.p8`), or generated signing certificates. The included `.gitignore` excludes common local secret files.

## License

MIT
