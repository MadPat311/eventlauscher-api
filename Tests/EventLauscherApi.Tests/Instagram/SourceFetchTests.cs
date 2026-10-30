using System.Text.Json;
using EventLauscherApi.Models.Instagram;
using EventLauscherApi.Services.Instagram;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EventLauscherApi.Tests.Instagram;

public sealed class SourceFetchTests
{
    private const string Media = """
        {"username":"organizer","caption":"Live concert","permalink":"https://instagram.com/p/1/",
        "media_type":"IMAGE","media_url":"https://example.com/image.jpg","thumbnail_url":"https://example.com/thumb.jpg",
        "timestamp":"2026-10-01T18:00:00+00:00"}
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task FetchPopulatesMetadataFromObjectOrArrayAndOptionallyFetchesComment(bool array, bool comment)
    {
        using var env = new TestEnvironment();
        var source = await AddSource(env, comment: comment ? "c1" : null);
        source.ErrorMessage = "Previous error";
        var mediaResponse = "{\"mentioned_media\":" + (array ? "[" + Media + "]" : Media) + "}";
        var graph = new Mock<IInstagramGraphClient>(MockBehavior.Strict);
        graph.Setup(x => x.GetMentionedMediaRawAsync("m1", It.IsAny<CancellationToken>())).ReturnsAsync(mediaResponse);
        if (comment) graph.Setup(x => x.GetMentionedCommentRawAsync("c1", It.IsAny<CancellationToken>())).ReturnsAsync("{\"mentioned_comment\":{\"text\":\"hello\"}}");
        Assert.Equal(1, await Processor(env, graph.Object).ProcessPendingFetchAsync());
        await using var reload = env.Reload();
        var stored = await reload.InstagramSources.SingleAsync();
        Assert.Equal("Fetched", stored.Status);
        Assert.Equal("organizer", stored.Username);
        Assert.Equal("Live concert", stored.CaptionOrText);
        Assert.Equal("https://instagram.com/p/1/", stored.Permalink);
        Assert.Equal("IMAGE", stored.MediaType);
        Assert.Equal("https://example.com/image.jpg", stored.MediaUrl);
        Assert.Equal("https://example.com/thumb.jpg", stored.ThumbnailUrl);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T18:00:00+00:00"), stored.PostedAt);
        Assert.NotNull(stored.FetchedAt);
        Assert.Null(stored.ErrorMessage);
        if (comment)
        {
            using var json = JsonDocument.Parse(stored.RawApiJson!);
            Assert.True(json.RootElement.TryGetProperty("mentioned_media_response", out _));
            Assert.Equal("hello", json.RootElement.GetProperty("mentioned_comment_response").GetProperty("mentioned_comment").GetProperty("text").GetString());
        }
        else Assert.Equal(mediaResponse, stored.RawApiJson);
        Assert.Equal(0, await Processor(env, graph.Object).ProcessPendingFetchAsync());
        graph.VerifyAll();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"mentioned_media\":[]}")]
    [InlineData("{\"mentioned_media\":null}")]
    [InlineData("{\"mentioned_media\":[42]}")]
    [InlineData("invalid json")]
    public async Task InvalidApiResponseMarksSourceFailed(string payload)
    {
        using var env = new TestEnvironment();
        var source = await AddSource(env);
        var graph = new Mock<IInstagramGraphClient>();
        graph.Setup(x => x.GetMentionedMediaRawAsync("m1", It.IsAny<CancellationToken>())).ReturnsAsync(payload);
        Assert.Equal(1, await Processor(env, graph.Object).ProcessPendingFetchAsync());
        Assert.Equal("Failed", source.Status);
        Assert.NotNull(source.ErrorMessage);
        Assert.NotNull(source.FetchedAt);
    }

    [Fact]
    public async Task OptionalFieldsAndNonStringValuesAreHandled()
    {
        using var env = new TestEnvironment();
        var source = await AddSource(env);
        var graph = new Mock<IInstagramGraphClient>();
        graph.Setup(x => x.GetMentionedMediaRawAsync("m1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"mentioned_media\":{\"username\":123,\"timestamp\":\"invalid\"}}");
        Assert.Equal(1, await Processor(env, graph.Object).ProcessPendingFetchAsync());
        Assert.Equal("Fetched", source.Status);
        Assert.Equal("123", source.Username);
        Assert.Null(source.CaptionOrText);
        Assert.Null(source.Permalink);
        Assert.Null(source.PostedAt);
    }

    [Fact]
    public async Task MissingMediaIdFailsWithoutCallingGraphApi()
    {
        using var env = new TestEnvironment();
        var source = await AddSource(env, media: null);
        var graph = new Mock<IInstagramGraphClient>(MockBehavior.Strict);
        Assert.Equal(1, await Processor(env, graph.Object).ProcessPendingFetchAsync());
        Assert.Equal("Failed", source.Status);
        Assert.Contains("MediaId is missing", source.ErrorMessage);
        Assert.NotNull(source.FetchedAt);
        graph.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ApiFailureIsTruncatedAndDoesNotBlockNextSource()
    {
        using var env = new TestEnvironment();
        var failed = await AddSource(env);
        var next = await AddSource(env, "m2");
        var graph = new Mock<IInstagramGraphClient>(MockBehavior.Strict);
        graph.Setup(x => x.GetMentionedMediaRawAsync("m1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException(new string('x', 2500)));
        graph.Setup(x => x.GetMentionedMediaRawAsync("m2", It.IsAny<CancellationToken>())).ReturnsAsync("{\"mentioned_media\":{}}");
        Assert.Equal(2, await Processor(env, graph.Object).ProcessPendingFetchAsync());
        Assert.Equal("Failed", failed.Status);
        Assert.Equal(2000, failed.ErrorMessage!.Length);
        Assert.Equal("Fetched", next.Status);
    }

    [Fact]
    public async Task FetchTakesTenOldestPendingSourcesAndDoesNotRetryFailedOnes()
    {
        using var env = new TestEnvironment();
        for (var i = 0; i < 11; i++)
        {
            var source = await AddSource(env, $"m{i}");
            source.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i);
        }
        var failed = await AddSource(env, "failed");
        failed.Status = "Failed";
        await env.Db.SaveChangesAsync();
        var graph = new Mock<IInstagramGraphClient>();
        graph.Setup(x => x.GetMentionedMediaRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("{\"mentioned_media\":{}}");
        Assert.Equal(10, await Processor(env, graph.Object).ProcessPendingFetchAsync());
        Assert.Equal("m0", (await env.Db.InstagramSources.SingleAsync(x => x.Status == "PendingFetch")).MediaId);
        Assert.Equal("Failed", failed.Status);
        graph.Verify(x => x.GetMentionedMediaRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    [Fact]
    public async Task FetchHonorsCancellation()
    {
        using var env = new TestEnvironment();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(env, Mock.Of<IInstagramGraphClient>()).ProcessPendingFetchAsync(cancellation.Token));
    }

    private static InstagramSourceFetchProcessor Processor(TestEnvironment env, IInstagramGraphClient graph) => new(env.Db, graph, NullLogger<InstagramSourceFetchProcessor>.Instance);

    private static async Task<InstagramSource> AddSource(TestEnvironment env, string? media = "m1", string? comment = null)
    {
        var source = new InstagramSource { SourceKey = Guid.NewGuid().ToString(), MediaId = media, LastCommentId = comment };
        env.Db.InstagramSources.Add(source);
        await env.Db.SaveChangesAsync();
        return source;
    }
}
