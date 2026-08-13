using PlaylistBridge.Core;
using PlaylistBridge.Providers;

if (args.Length == 0 || args.Contains("--help"))
{
    PrintHelp();
    return 0;
}

var source = ValueAfter("--spotify-playlist", args);
if (string.IsNullOrWhiteSpace(source))
{
    Console.Error.WriteLine("Missing --spotify-playlist <id-or-url>.");
    return 2;
}

var playlistId = ExtractSpotifyPlaylistId(source);
var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

try
{
    using var spotifyHttp = new HttpClient();
    using var appleHttp = new HttpClient();
    var spotify = new SpotifyProvider(spotifyHttp, SpotifyOptions.FromEnvironment());
    var apple = new AppleMusicProvider(appleHttp, AppleMusicOptions.FromEnvironment());
    var service = new PlaylistTransferService(new TrackMatcher());

    Console.WriteLine($"Reading Spotify playlist {playlistId}...");
    var result = await service.TransferAsync(spotify, apple, playlistId, dryRun);

    Console.WriteLine($"{result.Source.Name}: {result.Source.Tracks.Count} tracks");
    Console.WriteLine($"Matched: {result.MatchedCount}; needs review: {result.UnmatchedCount}");
    if (result.DestinationPlaylistId is not null)
        Console.WriteLine($"Created Apple Music playlist: {result.DestinationPlaylistId}");
    else
        Console.WriteLine("Preview complete; Apple Music was not changed.");

    foreach (var match in result.Matches.Where(x => !x.IsAccepted))
        Console.WriteLine($"REVIEW: {match.Source.Artist} - {match.Source.Title} (best score {match.Score:0})");
    return result.UnmatchedCount == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"PlaylistBridge failed: {exception.Message}");
    return 1;
}

static string? ValueAfter(string option, string[] values)
{
    var index = Array.FindIndex(values, value => value.Equals(option, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

static string ExtractSpotifyPlaylistId(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
    var segments = uri.AbsolutePath.Trim('/').Split('/');
    var playlistIndex = Array.FindIndex(segments, x => x.Equals("playlist", StringComparison.OrdinalIgnoreCase));
    return playlistIndex >= 0 && playlistIndex + 1 < segments.Length ? segments[playlistIndex + 1] : value;
}

static void PrintHelp()
{
    Console.WriteLine("PlaylistBridge - transfer a Spotify playlist to Apple Music");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/PlaylistBridge.Cli -- --spotify-playlist <id-or-url> [--dry-run]");
    Console.WriteLine();
    Console.WriteLine("Use --dry-run to match and report without creating an Apple Music playlist.");
}
