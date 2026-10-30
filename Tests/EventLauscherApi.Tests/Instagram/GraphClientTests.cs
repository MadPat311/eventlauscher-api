using System.Net;
using EventLauscherApi.Services.Instagram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventLauscherApi.Tests.Instagram;

public sealed class GraphClientTests
{
    [Theory]
    [InlineData(false, "mentioned_media.media_id(media-1){id,caption,media_type,media_url,permalink,timestamp,username}")]
    [InlineData(true, "mentioned_comment.comment_id(comment-1){id,text,timestamp,username}")]
    public async Task RequestUsesEncodedFieldsBearerHeaderAndConfiguredVersion(bool comment, string fields)
    {
        using var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var graph = Create(http, version: "v24.0");
        var result = comment ? await graph.GetMentionedCommentRawAsync("comment-1") : await graph.GetMentionedMediaRawAsync("media-1");
        Assert.Equal("{\"ok\":true}", result);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://graph.facebook.com/v24.0/ig-user?fields=" + Uri.EscapeDataString(fields), handler.Uri!.AbsoluteUri);
        Assert.Equal("Bearer test-access-token", handler.Authorization);
        Assert.DoesNotContain("test-access-token", handler.Uri.ToString());
    }

    [Fact]
    public async Task MissingVersionUsesDefault()
    {
        using var handler = new StubHandler();
        using var http = new HttpClient(handler);
        await Create(http).GetMentionedMediaRawAsync("m1");
        Assert.Equal("/v25.0/ig-user", handler.Uri!.AbsolutePath);
    }

    [Theory]
    [InlineData(null, "token", "Instagram:IgUserId")]
    [InlineData(" ", "token", "Instagram:IgUserId")]
    [InlineData("ig-user", null, "Instagram:AccessToken")]
    [InlineData("ig-user", " ", "Instagram:AccessToken")]
    public async Task MissingCredentialsFailBeforeAnyHttpRequest(string? userId, string? accessToken, string setting)
    {
        using var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(http, userId, accessToken).GetMentionedMediaRawAsync("m1"));
        Assert.Contains(setting, error.Message);
        Assert.Null(handler.Uri);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FailedApiResponseIncludesStatusAndResponseBody(HttpStatusCode status)
    {
        using var handler = new StubHandler { Status = status, Body = "{\"error\":\"failure\"}" };
        using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(http).GetMentionedMediaRawAsync("m1"));
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.Contains(handler.Body, error.Message);
    }

    [Fact]
    public async Task TransportErrorsAndCancellationPropagateToCaller()
    {
        using var handler = new StubHandler { Failure = new HttpRequestException("offline") };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => Create(http).GetMentionedMediaRawAsync("m1"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(http).GetMentionedMediaRawAsync("m1", cancellation.Token));
    }

    private static InstagramGraphClient Create(HttpClient client, string? user = "ig-user", string? token = "test-access-token", string? version = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Instagram:IgUserId"] = user, ["Instagram:AccessToken"] = token, ["Instagram:GraphApiVersion"] = version
        }).Build();
        return new InstagramGraphClient(new StubFactory(client), config, NullLogger<InstagramGraphClient>.Instance);
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Body { get; init; } = "{\"ok\":true}";
        public Exception? Failure { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri = request.RequestUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            if (Failure is not null) throw Failure;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
}
