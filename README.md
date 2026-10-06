# PlaylistBridge

PlaylistBridge is a .NET 10 Blazor and command-line application for moving playlists between music services. The first working path reads a Spotify playlist, finds safe matches in the Apple Music catalog, and creates an Apple Music library playlist.

The matching engine uses ISRC when both catalogs expose it. Otherwise, it scores normalized title, artist, album, duration, explicit status, and version markers such as “live” or “remaster.” Close or weak matches are left for review instead of being added automatically.

## What is included

- Provider-neutral playlist, track, match, and transfer models
- `IMusicProvider` abstraction for future source and destination services
- Spotify playlist reader using the Spotify Web API
- Apple Music catalog search and library playlist writer
- YouTube playlist reader, music-video search, and private playlist writer
- Source and destination selectors for every currently implemented route
- Preview mode that performs matching without writing to Apple Music
- Blazor Server UI with configuration status, match review, and explicit transfer confirmation
- Unit tests covering ISRC, normalization, duration, ambiguity, explicit status, and version mismatches

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
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
$env:PLAYLISTBRIDGE_SPOTIFY_TOKEN = "optional existing Spotify user access token"
$env:PLAYLISTBRIDGE_APPLE_DEVELOPER_TOKEN = "your Apple developer token"
$env:PLAYLISTBRIDGE_APPLE_USER_TOKEN = "your Apple Music user token"
$env:PLAYLISTBRIDGE_APPLE_STOREFRONT = "us"
```

For production, obtain and refresh tokens through each provider’s authorization flow rather than checking credentials into source control. See [Spotify authorization](https://developer.spotify.com/documentation/web-api/concepts/authorization) and [Apple Music user authentication](https://developer.apple.com/documentation/applemusicapi/user-authentication-for-musickit).

## Run the Blazor UI

After setting the environment variables, start the web app:

```powershell
dotnet run --project src/PlaylistBridge.Web
```

Open the local address shown in the terminal. Paste a Spotify playlist link or ID and select **Preview transfer**. Review the matches, then select **Create in Apple Music** to write only the accepted matches. Credentials are read on the server and are not stored by the application.

The **Connections** page walks through each provider's setup and can test configured connections. For Spotify, enter the app's Client ID and Client Secret and select **Connect with Spotify**. PlaylistBridge uses Spotify's server-side Authorization Code flow, validates OAuth state, keeps credentials and tokens in server memory, and refreshes one-hour access tokens automatically. Register `https://127.0.0.1:7075/auth/spotify/callback` as the exact Spotify Redirect URI when using the default HTTPS launch profile.

YouTube setup is free within Google's default API quota. The Connections page links to the YouTube Data API console and Google OAuth Playground. A YouTube OAuth token must include `https://www.googleapis.com/auth/youtube` so PlaylistBridge can read and create playlists.

For a durable YouTube connection, save the OAuth client ID, client secret, and refresh token from Google OAuth Playground. Enable **Use your own OAuth credentials** in Playground before authorizing. PlaylistBridge exchanges the refresh token for short-lived access tokens automatically. Google only issues a refresh token when offline access is requested, and it may only return one during the first consent flow.

Connections entered in the web app are encrypted with ASP.NET Core Data Protection and stored for the current Windows user under `%LOCALAPPDATA%\PlaylistBridge`. Spotify OAuth sessions, Google refresh credentials, and Apple Music credentials survive application restarts. Secrets are not logged or stored in browser storage.

## Run the CLI

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
- `PlaylistBridge.Providers` contains Spotify, Apple Music, and YouTube HTTP adapters and provider configuration.
- `PlaylistBridge.Cli` is the runnable entry point.
- `PlaylistBridge.Web` is the interactive Blazor Server front end.
- `PlaylistBridge.Core.Tests` protects matching behavior.

## Diagnostics and interrupted transfers

The web app writes structured transfer events to the Visual Studio Output window or the terminal where it is running. Logs include the source and destination services, operation name, HTTP status, provider error code, destination playlist ID, and confirmed track counts. Credentials and access tokens are never logged.

When a destination playlist is created but a later track fails, the page keeps the playlist ID and confirmed progress. The error includes the provider's actual response, and **Resume** continues with the remaining matched tracks instead of creating another playlist. This resume state currently lives in the open browser session, so do not refresh the page before retrying.

YouTube track writes automatically retry a small number of times with exponential backoff when Google reports a transient aborted operation, throttling, or server failure. Authentication, permission, and validation errors are not retried.

## Current limits and next steps

- YouTube authorization still uses Google OAuth Playground; saved refresh tokens are renewed automatically. Spotify supports the local OAuth flow.
- Apple Music-to-Spotify and additional providers can implement the same provider contract.
- Ambiguous matches are printed to the console; an interactive review workflow is a logical next feature.
- A production app should persist resumable transfers, add automatic retry/rate-limit handling, and use durable encrypted token storage.

## Security

Never commit access tokens, Apple private keys (`.p8`), or generated signing certificates. The included `.gitignore` excludes common local secret files.

## License

MIT
