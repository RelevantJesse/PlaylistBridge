using System.Net;
using System.Text.RegularExpressions;

namespace PlaylistBridge.Providers;

public sealed record YouTubeTrackMetadata(string Title, string Artist);

public static partial class YouTubeTrackMetadataParser
{
    public static YouTubeTrackMetadata Parse(string? rawTitle, string? rawChannel)
    {
        var title = WebUtility.HtmlDecode(rawTitle ?? string.Empty).Trim();
        var channel = WebUtility.HtmlDecode(rawChannel ?? string.Empty).Trim();
        var artist = TopicSuffix().Replace(channel, string.Empty).Trim();
        artist = VevoSuffix().Replace(artist, string.Empty).Trim();

        var separator = title.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0 && separator < title.Length - 3)
        {
            artist = title[..separator].Trim();
            title = title[(separator + 3)..].Trim();
        }

        title = VideoLabel().Replace(title, string.Empty).Trim(' ', '-', '–', '—');
        return new YouTubeTrackMetadata(title, artist);
    }

    [GeneratedRegex(@"\s*-\s*Topic$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopicSuffix();

    [GeneratedRegex(@"VEVO$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VevoSuffix();

    [GeneratedRegex(@"\s*[\(\[]\s*(official\s+)?(music\s+)?(video|audio|lyric\s+video|lyrics)\s*[\)\]]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VideoLabel();
}
