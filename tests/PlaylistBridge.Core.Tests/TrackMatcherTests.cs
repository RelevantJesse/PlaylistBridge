using PlaylistBridge.Core;

namespace PlaylistBridge.Core.Tests;

public sealed class TrackMatcherTests
{
    private readonly TrackMatcher _matcher = new();

    [Fact]
    public void ExactIsrcWinsEvenWhenMetadataDiffers()
    {
        var source = Song("1", "Song", "Artist", isrc: "US-AAA-24-00001");
        var exact = Song("2", "Different", "Someone", isrc: "us-aaa-24-00001");

        var result = _matcher.FindBest(source, [exact]);

        Assert.Equal(100, result.Score);
        Assert.Equal(MatchConfidence.Exact, result.Confidence);
        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void NormalizationIgnoresCasePunctuationAndDiacritics()
    {
        var source = Song("1", "Déjà Vu!", "Beyoncé");
        var candidate = Song("2", "DEJA-VU", "Beyonce");

        var result = _matcher.Score(source, candidate);

        Assert.Contains("Normalized title matches", result.Reasons);
        Assert.Contains("Normalized artist matches", result.Reasons);
    }

    [Fact]
    public void MetadataAndDurationProduceAcceptedMatch()
    {
        var source = Song("1", "Everlong", "Foo Fighters", "The Colour and the Shape", 250);
        var candidate = Song("2", "Everlong", "Foo Fighters", "The Colour and the Shape", 251);

        var result = _matcher.FindBest(source, [candidate]);

        Assert.True(result.IsAccepted);
        Assert.Equal(MatchConfidence.High, result.Confidence);
    }

    [Theory]
    [InlineData("Song (Live)", "Song")]
    [InlineData("Song", "Song - Remaster")]
    [InlineData("Song (Acoustic)", "Song")]
    [InlineData("Song", "Song Instrumental")]
    public void VersionMismatchIsPenalized(string sourceTitle, string candidateTitle)
    {
        var result = _matcher.Score(Song("1", sourceTitle, "Artist"), Song("2", candidateTitle, "Artist"));

        Assert.Contains(result.Reasons, reason => reason.EndsWith("version mismatch"));
        Assert.True(result.Score < 70);
    }

    [Fact]
    public void ExplicitMismatchIsPenalized()
    {
        var result = _matcher.Score(
            Song("1", "Song", "Artist", explicitValue: true),
            Song("2", "Song", "Artist", explicitValue: false));

        Assert.Contains("Explicit flag differs", result.Reasons);
        Assert.Equal(60, result.Score);
    }

    [Fact]
    public void CloseCandidatesAreMarkedForReview()
    {
        var source = Song("1", "Song", "Artist", "Album", 200);
        var a = Song("2", "Song", "Artist", "Album", 204);
        var b = Song("3", "Song", "Artist", "Album", 205);

        var result = _matcher.FindBest(source, [a, b]);

        Assert.False(result.IsAccepted);
        Assert.Contains(result.Reasons, reason => reason.Contains("too close"));
    }

    [Fact]
    public void EmptyCatalogReturnsNoMatch()
    {
        var result = _matcher.FindBest(Song("1", "Song", "Artist"), []);

        Assert.Null(result.Destination);
        Assert.Equal(MatchConfidence.None, result.Confidence);
    }

    private static Track Song(string id, string title, string artist, string? album = null,
        double? seconds = null, string? isrc = null, bool? explicitValue = null) =>
        new(id, title, artist, album, seconds is null ? null : TimeSpan.FromSeconds(seconds.Value), isrc, explicitValue);
}
