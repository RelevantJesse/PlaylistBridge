namespace PlaylistBridge.Core;

public sealed class TrackMatcher
{
    public TrackMatch FindBest(Track source, IEnumerable<Track> candidates)
    {
        var ranked = candidates.Select(candidate => Score(source, candidate))
            .OrderByDescending(match => match.Score)
            .ToArray();

        if (ranked.Length == 0)
            return new(source, null, 0, MatchConfidence.None, ["No catalog candidates"]);

        var best = ranked[0];
        if (best.Confidence == MatchConfidence.Exact)
            return best;

        if (best.Score < 65)
            return best with { Confidence = MatchConfidence.Low };

        if (ranked.Length > 1 && best.Score < 100 && best.Score - ranked[1].Score < 8)
            return best with
            {
                Confidence = MatchConfidence.Low,
                Reasons = [..best.Reasons, "Top candidates are too close to choose safely"]
            };

        return best with { Confidence = best.Score >= 90 ? MatchConfidence.High : MatchConfidence.Medium };
    }

    public TrackMatch Score(Track source, Track candidate)
    {
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.Isrc) &&
            string.Equals(source.Isrc, candidate.Isrc, StringComparison.OrdinalIgnoreCase))
            return new(source, candidate, 100, MatchConfidence.Exact, ["Exact ISRC"]);

        double score = 0;
        AddTextScore(source.Title, candidate.Title, 40, "title", reasons, ref score);
        AddTextScore(source.Artist, candidate.Artist, 30, "artist", reasons, ref score);
        AddTextScore(source.Album, candidate.Album, 10, "album", reasons, ref score);

        if (source.Duration is not null && candidate.Duration is not null)
        {
            var delta = Math.Abs((source.Duration.Value - candidate.Duration.Value).TotalSeconds);
            if (delta <= 2) { score += 15; reasons.Add("Duration within 2 seconds"); }
            else if (delta <= 5) { score += 8; reasons.Add("Duration within 5 seconds"); }
        }

        if (source.IsExplicit is not null && candidate.IsExplicit is not null)
        {
            if (source.IsExplicit == candidate.IsExplicit) { score += 5; reasons.Add("Explicit flag matches"); }
            else { score -= 10; reasons.Add("Explicit flag differs"); }
        }

        foreach (var marker in new[] { "live", "remaster", "acoustic", "instrumental" })
        {
            if (TrackNormalizer.HasVersionMarker(source.Title, marker) != TrackNormalizer.HasVersionMarker(candidate.Title, marker))
            {
                score -= 20;
                reasons.Add($"{marker} version mismatch");
            }
        }

        return new(source, candidate, Math.Clamp(score, 0, 99), MatchConfidence.Low, reasons);
    }

    private static void AddTextScore(string? left, string? right, double weight, string label,
        ICollection<string> reasons, ref double score)
    {
        var a = TrackNormalizer.Normalize(left);
        var b = TrackNormalizer.Normalize(right);
        if (a.Length > 0 && a == b)
        {
            score += weight;
            reasons.Add($"Normalized {label} matches");
        }
    }
}
