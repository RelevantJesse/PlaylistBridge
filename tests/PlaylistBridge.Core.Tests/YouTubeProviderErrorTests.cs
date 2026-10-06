using System.Net;
using System.Text;
using PlaylistBridge.Core;
using PlaylistBridge.Providers;

namespace PlaylistBridge.Core.Tests;

public sealed class YouTubeProviderErrorTests
{
    [Fact]
    public async Task CreatePlaylistAsync_SurfacesGoogleErrorDetails()
    {
        var handler = new QueueMessageHandler(Response(
            HttpStatusCode.Unauthorized,
            """{"error":{"code":401,"message":"A YouTube channel is required.","errors":[{"reason":"youtubeSignupRequired"}]}}"""));
        var provider = new YouTubeProvider(new HttpClient(handler), new YouTubeOptions("test-token"));

        var exception = await Assert.ThrowsAsync<ProviderApiException>(
            () => provider.CreatePlaylistAsync("Test", null));

        Assert.Equal("YouTube", exception.ProviderName);
        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("youtubeSignupRequired", exception.ErrorCode);
        Assert.Contains("A YouTube channel is required", exception.Message);
    }

    [Fact]
    public async Task AddTracksAsync_ReportsConfirmedProgressBeforeFailure()
    {
        var handler = new QueueMessageHandler(
            Response(HttpStatusCode.OK, "{}"),
            Response(HttpStatusCode.Unauthorized,
                """{"error":{"code":401,"message":"Invalid Credentials","errors":[{"reason":"authError"}]}}"""));
        var provider = new YouTubeProvider(new HttpClient(handler), new YouTubeOptions("test-token"));

        var exception = await Assert.ThrowsAsync<TrackWriteException>(
            () => provider.AddTracksAsync("playlist", ["video-1", "video-2", "video-3"]));

        Assert.Equal(1, exception.AddedTrackCount);
        var providerError = Assert.IsType<ProviderApiException>(exception.InnerException);
        Assert.Equal("authError", providerError.ErrorCode);
    }

    [Fact]
    public async Task AddTracksAsync_RetriesTransientAbortedOperation()
    {
        var handler = new QueueMessageHandler(
            Response(HttpStatusCode.Conflict,
                """{"error":{"code":409,"message":"The operation was aborted.","status":"SERVICE_UNAVAILABLE"}}"""),
            Response(HttpStatusCode.OK, "{}"));
        var provider = new YouTubeProvider(new HttpClient(handler), new YouTubeOptions("test-token"));

        await provider.AddTracksAsync("playlist", ["video-1"]);

        Assert.Equal(2, handler.RequestCount);
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class QueueMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
