namespace PlaylistBridge.Core;

public sealed record Track(
    string Id,
    string Title,
    string Artist,
    string? Album = null,
    TimeSpan? Duration = null,
    string? Isrc = null,
    bool? IsExplicit = null);

public sealed record Playlist(string Id, string Name, string? Description, IReadOnlyList<Track> Tracks);

public enum MatchConfidence { None, Low, Medium, High, Exact }

public sealed record TrackMatch(
    Track Source,
    Track? Destination,
    double Score,
    MatchConfidence Confidence,
    IReadOnlyList<string> Reasons)
{
    public bool IsAccepted => Destination is not null && Confidence >= MatchConfidence.Medium;
}

public sealed record TransferResult(
    Playlist Source,
    string? DestinationPlaylistId,
    IReadOnlyList<TrackMatch> Matches,
    int AddedTrackCount = 0)
{
    public int MatchedCount => Matches.Count(x => x.IsAccepted);
    public int UnmatchedCount => Matches.Count - MatchedCount;
    public bool HasStarted => DestinationPlaylistId is not null;
    public bool IsComplete => HasStarted && AddedTrackCount >= MatchedCount;
}
