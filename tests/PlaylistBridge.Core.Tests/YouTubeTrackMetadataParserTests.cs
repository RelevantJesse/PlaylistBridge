using PlaylistBridge.Providers;

namespace PlaylistBridge.Core.Tests;

public sealed class YouTubeTrackMetadataParserTests
{
    [Theory]
    [InlineData("Foo Fighters - Everlong (Official Music Video)", "Foo Fighters", "Everlong")]
    [InlineData("Beyoncé - CUFF IT (Official Lyric Video)", "Beyoncé", "CUFF IT")]
    public void SplitsCommonMusicVideoTitles(string raw, string expectedArtist, string expectedTitle)
    {
        var result = YouTubeTrackMetadataParser.Parse(raw, "Some Channel");

        Assert.Equal(expectedArtist, result.Artist);
        Assert.Equal(expectedTitle, result.Title);
    }

    [Theory]
    [InlineData("Everlong", "Foo Fighters - Topic", "Foo Fighters")]
    [InlineData("Halo (Official Audio)", "BeyonceVEVO", "Beyonce")]
    public void UsesNormalizedChannelAsArtist(string title, string channel, string expectedArtist)
    {
        var result = YouTubeTrackMetadataParser.Parse(title, channel);

        Assert.Equal(expectedArtist, result.Artist);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabc123&feature=shared", "PLabc123")]
    [InlineData("https://music.youtube.com/playlist?list=PLmusic987", "PLmusic987")]
    [InlineData("PLraw456", "PLraw456")]
    public void ExtractsPlaylistId(string input, string expected)
    {
        Assert.Equal(expected, YouTubeProvider.ExtractPlaylistId(input));
    }
}
