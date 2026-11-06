using System.Text;
using System.Text.Json;
using EventLauscherApi.Controllers;
using EventLauscherApi.Models.Instagram;
using EventLauscherApi.Services.Instagram;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventLauscherApi.Tests.Instagram;

public sealed class WebhookTests
{
    [Theory]
    [InlineData("subscribe", "test-verify-token", "challenge", true)]
    [InlineData("subscribe", "wrong", "challenge", false)]
    [InlineData("other", "test-verify-token", "challenge", false)]
    [InlineData("subscribe", "test-verify-token", "", false)]
    [InlineData("subscribe", "", "challenge", false)]
    public void VerificationRequiresMatchingTokenModeAndChallenge(string mode, string token, string challenge, bool valid)
    {
        using var env = new TestEnvironment();
        var controller = Controller(env);
        controller.Request.QueryString = QueryString.Create(new Dictionary<string, string?>
        {
            ["hub.mode"] = mode, ["hub.verify_token"] = token, ["hub.challenge"] = challenge
        });
        var result = controller.VerifyWebhook();
        if (valid)
        {
            var content = Assert.IsType<ContentResult>(result);
            Assert.Equal(challenge, content.Content);
            Assert.Equal("text/plain", content.ContentType);
        }
        else Assert.IsType<UnauthorizedResult>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n ")]
    public async Task ReceiverRejectsEmptyBody(string payload)
    {
        using var env = new TestEnvironment();
        var controller = Controller(env);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        Assert.IsType<BadRequestObjectResult>(await controller.ReceiveWebhook());
        Assert.Empty(env.Db.InstagramWebhookInboxEntries);
    }

    [Fact]
    public async Task ReceiverStoresRawPayloadForAsynchronousProcessing()
    {
        using var env = new TestEnvironment();
        var payload = MentionPayload("media-1", "comment-1");
        var controller = Controller(env);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        Assert.IsType<OkResult>(await controller.ReceiveWebhook());
        await using var reload = env.Reload();
        var entry = await reload.InstagramWebhookInboxEntries.SingleAsync();
        Assert.Equal(payload, entry.PayloadJson);
        Assert.Equal("Instagram", entry.Provider);
        Assert.Equal("Received", entry.Status);
        Assert.Null(entry.ProcessedAt);
    }

    [Theory]
    [InlineData("m1", null, "MediaCaption", "media:m1")]
    [InlineData("m1", "c1", "Comment", "media:m1")]
    [InlineData(null, "c1", "Comment", "comment:c1")]
    [InlineData(null, null, "Unknown", null)]
    public async Task ProcessorExtractsMentionKindAndSourceKey(string? media, string? comment, string kind, string? key)
    {
        using var env = new TestEnvironment();
        var entry = await AddInbox(env, MentionPayload(media, comment));
        Assert.Equal(1, await Processor(env).ProcessReceivedAsync());
        await using var reload = env.Reload();
        var source = await reload.InstagramSources.SingleAsync();
        Assert.Equal(entry.Id, source.WebhookInboxEntryId);
        Assert.Equal("ig-account", source.InstagramEntryId);
        Assert.Equal(media, source.MediaId);
        Assert.Equal(comment, source.FirstCommentId);
        Assert.Equal(comment, source.LastCommentId);
        Assert.Equal(kind, source.MentionType);
        if (key is null) Assert.StartsWith("unknown:", source.SourceKey);
        else Assert.Equal(key, source.SourceKey);
        Assert.Equal(1, source.MentionCount);
        Assert.Equal("PendingFetch", source.Status);
        Assert.NotNull(source.RawValueJson);
        var inbox = await reload.InstagramWebhookInboxEntries.SingleAsync();
        Assert.Equal("MentionDetected", inbox.Status);
        Assert.NotNull(inbox.ProcessedAt);
        Assert.Equal(0, await Processor(env).ProcessReceivedAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"entry\":{}}")]
    [InlineData("{\"entry\":[]}")]
    [InlineData("{\"entry\":[{}]}")]
    [InlineData("{\"entry\":[{\"changes\":{}}]}")]
    [InlineData("{\"entry\":[{\"changes\":[{}]}]}")]
    [InlineData("{\"entry\":[{\"changes\":[{\"field\":\"comments\",\"value\":{\"media_id\":\"1\"}}]}]}")]
    public async Task ProcessorIgnoresPayloadsWithoutMentions(string payload)
    {
        using var env = new TestEnvironment();
        var entry = await AddInbox(env, payload);
        Assert.Equal(1, await Processor(env).ProcessReceivedAsync());
        Assert.Equal("Ignored", entry.Status);
        Assert.NotNull(entry.ProcessedAt);
        Assert.Empty(env.Db.InstagramSources);
    }

    [Fact]
    public async Task InvalidPayloadFailsWithoutBlockingNextEntry()
    {
        using var env = new TestEnvironment();
        var failed = await AddInbox(env, "not json");
        var valid = await AddInbox(env, MentionPayload("m1"));
        Assert.Equal(1, await Processor(env).ProcessReceivedAsync());
        Assert.Equal("Failed", failed.Status);
        Assert.NotNull(failed.ErrorMessage);
        Assert.NotNull(failed.ProcessedAt);
        Assert.Equal("MentionDetected", valid.Status);
        Assert.Single(env.Db.InstagramSources);
    }

    [Fact]
    public async Task RepeatedMentionUpdatesExistingSourceWithoutLosingFirstCommentOrFetchStatus()
    {
        using var env = new TestEnvironment();
        var first = await AddInbox(env, MentionPayload("m1", "c1"));
        await Processor(env).ProcessReceivedAsync();
        var source = await env.Db.InstagramSources.SingleAsync();
        source.Status = "Fetched";
        await env.Db.SaveChangesAsync();
        await AddInbox(env, MentionPayload("m1", "c2"));
        Assert.Equal(1, await Processor(env).ProcessReceivedAsync());
        await using var reload = env.Reload();
        var stored = await reload.InstagramSources.SingleAsync();
        Assert.Equal(2, stored.MentionCount);
        Assert.Equal(first.Id, stored.WebhookInboxEntryId);
        Assert.Equal("c1", stored.FirstCommentId);
        Assert.Equal("c2", stored.LastCommentId);
        Assert.Equal("Fetched", stored.Status);
        Assert.Contains("c2", stored.RawValueJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedMentionsWithinOneBatchCreateOnlyOneSource(bool samePayload)
    {
        using var env = new TestEnvironment();
        if (samePayload)
        {
            var payload = JsonSerializer.Serialize(new
            {
                entry = new[] { new { id = "ig-account", changes = new[]
                {
                    new { field = "mentions", value = new { media_id = "m1", comment_id = "c1" } },
                    new { field = "mentions", value = new { media_id = "m1", comment_id = "c2" } }
                } } }
            });
            await AddInbox(env, payload);
        }
        else
        {
            await AddInbox(env, MentionPayload("m1", "c1"));
            await AddInbox(env, MentionPayload("m1", "c2"));
        }
        Assert.Equal(samePayload ? 1 : 2, await Processor(env).ProcessReceivedAsync());
        await using var reload = env.Reload();
        var source = await reload.InstagramSources.SingleAsync();
        Assert.Equal(2, source.MentionCount);
        Assert.Equal("c1", source.FirstCommentId);
        Assert.Equal("c2", source.LastCommentId);
    }

    [Fact]
    public async Task ProcessorTakesTwentyOldestReceivedEntriesAndLeavesOtherStatusesAlone()
    {
        using var env = new TestEnvironment();
        for (var i = 0; i < 21; i++)
            env.Db.InstagramWebhookInboxEntries.Add(new WebhookInboxEntry { PayloadJson = "{}", ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-i) });
        env.Db.InstagramWebhookInboxEntries.Add(new WebhookInboxEntry { PayloadJson = "{}", Status = "Failed" });
        await env.Db.SaveChangesAsync();
        Assert.Equal(20, await Processor(env).ProcessReceivedAsync());
        Assert.Equal(20, await env.Db.InstagramWebhookInboxEntries.CountAsync(x => x.Status == "Ignored"));
        Assert.Equal(1, (await env.Db.InstagramWebhookInboxEntries.SingleAsync(x => x.Status == "Received")).Id);
        Assert.Equal(1, await env.Db.InstagramWebhookInboxEntries.CountAsync(x => x.Status == "Failed"));
    }

    [Fact]
    public async Task ProcessorHonorsCancellation()
    {
        using var env = new TestEnvironment();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(env).ProcessReceivedAsync(cancellation.Token));
    }

    internal static string MentionPayload(string? media, string? comment = null) => JsonSerializer.Serialize(new
    {
        entry = new[] { new { id = "ig-account", changes = new[] { new { field = "mentions", value = new { media_id = media, comment_id = comment } } } } }
    });

    private static async Task<WebhookInboxEntry> AddInbox(TestEnvironment env, string payload)
    {
        var entry = new WebhookInboxEntry { PayloadJson = payload };
        env.Db.InstagramWebhookInboxEntries.Add(entry);
        await env.Db.SaveChangesAsync();
        return entry;
    }
    private static InstagramWebhookController Controller(TestEnvironment env) => env.Attach(new InstagramWebhookController(env.Db, env.Configuration, NullLogger<InstagramWebhookController>.Instance));
    private static InstagramWebhookInboxProcessor Processor(TestEnvironment env) => new(env.Db, NullLogger<InstagramWebhookInboxProcessor>.Instance);
}
