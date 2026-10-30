using EventLauscherApi.Controllers;
using EventLauscherApi.Models;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EventLauscherApi.Tests.Infrastructure.TestEnvironment;

namespace EventLauscherApi.Tests.Controllers;

public sealed class ModerationAndPublicTests
{
    [Fact]
    public async Task ReviewUpdatesAllFieldsAndPreservesFirstReviewerOnSubsequentEdits()
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent();
        var reviewer = env.Reviewer();
        Assert.IsType<NoContentResult>(await reviewer.UpdateForReview(ev.Id, new()
        {
            Title = "Changed", Description = "Description", Location = "Berlin", Date = "2026-12-01",
            Time = "20:00", Latitude = 52.5, Longitude = 13.4
        }, default));
        var reviewedAt = ev.ReviewedAt;
        Assert.NotNull(reviewedAt);
        Assert.IsType<NoContentResult>(await env.Reviewer(Guid.NewGuid()).UpdateForReview(ev.Id, new(), default));
        await using var reload = env.Reload();
        var stored = await reload.Events.SingleAsync();
        Assert.Equal("Changed", stored.Title);
        Assert.Equal("Description", stored.Description);
        Assert.Equal("Berlin", stored.Location);
        Assert.Equal("2026-12-01", stored.Date);
        Assert.Equal("20:00", stored.Time);
        Assert.Equal(52.5, stored.Latitude);
        Assert.Equal(13.4, stored.Longitude);
        Assert.Equal(EventStatus.Draft, stored.Status);
        Assert.Equal(env.UserId, stored.ReviewedByUserId);
        Assert.Equal(reviewedAt, stored.ReviewedAt);
    }

    [Fact]
    public async Task PublishUnpublishAndRejectPersistWorkflowMetadata()
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent();
        var reviewer = env.Reviewer();
        Assert.IsType<NoContentResult>(await reviewer.Publish(ev.Id, default));
        Assert.Equal(EventStatus.Published, ev.Status);
        Assert.NotNull(ev.PublishedAt);
        var reviewedAt = ev.ReviewedAt;
        Assert.NotNull(reviewedAt);
        Assert.Equal(env.UserId, ev.ReviewedByUserId);
        var second = Guid.NewGuid();
        Assert.IsType<NoContentResult>(await env.Reviewer(second).Publish(ev.Id, default));
        Assert.Equal(reviewedAt, ev.ReviewedAt);
        Assert.Equal(env.UserId, ev.ReviewedByUserId);
        Assert.IsType<NoContentResult>(await env.Reviewer(second).Unpublish(ev.Id, default));
        Assert.Equal(EventStatus.Draft, ev.Status);
        Assert.Null(ev.PublishedAt);
        Assert.Equal(second, ev.ReviewedByUserId);
        Assert.IsType<ConflictObjectResult>(await reviewer.Unpublish(ev.Id, default));
        Assert.IsType<OkObjectResult>(await reviewer.RejectEvent(ev.Id));
        await using var reload = env.Reload();
        var stored = await reload.Events.SingleAsync();
        Assert.Equal(EventStatus.Rejected, stored.Status);
        Assert.Equal(env.UserId, stored.ReviewedByUserId);
        Assert.NotNull(stored.ReviewedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PublishRequiresNonEmptyTitle(string title)
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent();
        ev.Title = title;
        await env.Db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await env.Reviewer().Publish(ev.Id, default));
        Assert.Equal(EventStatus.Draft, ev.Status);
        Assert.Null(ev.PublishedAt);
    }

    [Fact]
    public async Task ModerationReturnsNotFoundForUnknownResources()
    {
        using var env = new TestEnvironment();
        var controller = env.Reviewer();
        Assert.IsType<NotFoundResult>(await controller.GetDetail(123, default));
        Assert.IsType<NotFoundResult>(await controller.UpdateForReview(123, new(), default));
        Assert.IsType<NotFoundResult>(await controller.Publish(123, default));
        Assert.IsType<NotFoundResult>(await controller.Unpublish(123, default));
        Assert.IsType<NotFoundResult>(await controller.BanUploader(123, default));
        Assert.IsType<NotFoundResult>(await controller.Unban(Guid.NewGuid()));
        Assert.Equal("EVENT_NOT_FOUND", Json(Assert.IsType<NotFoundObjectResult>(await controller.RejectEvent(123)).Value).GetProperty("error").GetString());
    }

    [Fact]
    public async Task ReviewerQueueIncludesOnlyDraftsAndDetailIncludesUploaderAndReviewer()
    {
        using var env = new TestEnvironment();
        var later = await env.AddEvent(date: "2026-12-01");
        var earlier = await env.AddEvent(date: "2026-11-01");
        await env.AddEvent(EventStatus.Published);
        await env.AddEvent(EventStatus.Rejected);
        var controller = env.Reviewer();
        var queue = Ok(await controller.GetUnpublished(default));
        Assert.Equal(new[] { earlier.Id, later.Id }, queue.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
        Assert.Equal("tester@example.com", queue[0].GetProperty("uploaderEmail").GetString());
        var before = Ok(await controller.GetDetail(earlier.Id, default));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, before.GetProperty("reviewer").ValueKind);
        await controller.Publish(earlier.Id, default);
        var detail = Ok(await controller.GetDetail(earlier.Id, default));
        Assert.Equal(earlier.Id, detail.GetProperty("event").GetProperty("id").GetInt32());
        Assert.Equal("tester@example.com", detail.GetProperty("uploader").GetProperty("email").GetString());
        Assert.Equal(env.UserId, detail.GetProperty("reviewer").GetProperty("reviewedByUserId").GetGuid());
    }

    [Fact]
    public async Task BanAndUnbanChangeIdentityLockout()
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent();
        var reviewer = env.Reviewer();
        Assert.IsType<NoContentResult>(await reviewer.BanUploader(ev.Id, default));
        var uploader = (await env.Users.FindByIdAsync(env.UserId.ToString()))!;
        Assert.True(uploader.LockoutEnabled);
        Assert.True(await env.Users.IsLockedOutAsync(uploader));
        Assert.IsType<NoContentResult>(await reviewer.Unban(env.UserId));
        Assert.False(await env.Users.IsLockedOutAsync(uploader));
        Assert.Null(uploader.LockoutEnd);
    }

    [Fact]
    public async Task BanReturnsNotFoundWhenUploaderWasRemoved()
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(owner: Guid.NewGuid());
        Assert.IsType<NotFoundObjectResult>(await env.Reviewer().BanUploader(ev.Id, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PublicProfilesRequireUsername(string username)
    {
        using var env = new TestEnvironment();
        Assert.IsType<BadRequestObjectResult>(await env.Public().GetPublicFavorites(username, default));
        Assert.IsType<BadRequestObjectResult>(await env.Public().GetPublicUserHeader(username, default));
        Assert.IsType<NotFoundResult>(await env.Public().ShareUserFavorites(username, default));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tester@example.com")]
    public async Task PublicProfilesUseOnlyUsernameAndDoNotResolveEmail(string username)
    {
        using var env = new TestEnvironment();
        Assert.IsType<NotFoundResult>(await env.Public().GetPublicFavorites(username, default));
        Assert.IsType<NotFoundResult>(await env.Public().GetPublicUserHeader(username, default));
        Assert.IsType<NotFoundResult>(await env.Public().ShareUserFavorites(username, default));
    }

    [Fact]
    public async Task PublicProfileCountsAndFavoritesOnlyExposePublishedEvents()
    {
        using var env = new TestEnvironment();
        var published = await env.AddEvent(EventStatus.Published);
        var draft = await env.AddEvent();
        await env.AddEvent(EventStatus.Rejected);
        var other = await env.AddEvent(EventStatus.Published, Guid.NewGuid());
        env.Db.SavedEvents.AddRange(
            new SavedEvent { UserId = env.UserId, EventId = published.Id },
            new SavedEvent { UserId = env.UserId, EventId = draft.Id },
            new SavedEvent { UserId = Guid.NewGuid(), EventId = other.Id });
        await env.Db.SaveChangesAsync();
        var header = Ok(await env.Public().GetPublicUserHeader("TESTER", default));
        Assert.Equal("tester", header.GetProperty("username").GetString());
        Assert.Equal(1, header.GetProperty("favoritesCount").GetInt32());
        Assert.Equal(1, header.GetProperty("uploadsCount").GetInt32());
        Assert.False(header.TryGetProperty("email", out _));
        var favorites = Ok(await env.Public().GetPublicFavorites("TeStEr", default));
        Assert.Equal(published.Id, Assert.Single(favorites.EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UserSharePageContainsCanonicalLinkAndRedirect()
    {
        using var env = new TestEnvironment();
        var controller = env.Public();
        var html = Assert.IsType<ContentResult>(await controller.ShareUserFavorites(" TESTER ", default));
        Assert.Contains("https://api.example.test/s/u/tester", html.Content);
        Assert.Contains("https://api.example.test/u/tester", html.Content);
        Assert.Contains("/assets/share-default.jpg", html.Content);
        Assert.Equal("text/html; charset=utf-8", html.ContentType);
        Assert.Equal("public,max-age=300", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task ReportsCanBeAnonymousOrAuthenticatedAndAreListedNewestFirstAndDeleted()
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(EventStatus.Published);
        var controller = env.Attach(new EventReportsController(env.Db));
        controller.HttpContext.User = new System.Security.Claims.ClaimsPrincipal();
        var first = Assert.IsType<EventReportsController.EventReportDto>(Assert.IsType<OkObjectResult>(
            (await controller.Create(new(ev.Id, "  Wrong date  "), default)).Result).Value);
        Assert.Null(first.ReporterUserId);
        Assert.Equal("Wrong date", first.Reason);
        env.Db.EventReports.Single().CreatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await env.Db.SaveChangesAsync();
        env.Attach(controller);
        var second = Assert.IsType<EventReportsController.EventReportDto>(Assert.IsType<OkObjectResult>(
            (await controller.Create(new(ev.Id, "Spam"), default)).Result).Value);
        Assert.Equal(env.UserId, second.ReporterUserId);
        var list = Assert.IsType<EventReportsController.EventReportDto[]>(Assert.IsType<OkObjectResult>((await controller.List(default)).Result).Value);
        Assert.Equal(new[] { second.Id, first.Id }, list.Select(x => x.Id));
        Assert.IsType<NoContentResult>(await controller.Delete(first.Id, default));
        Assert.IsType<NotFoundResult>(await controller.Delete(first.Id, default));
        await using var reload = env.Reload();
        Assert.Equal(second.Id, (await reload.EventReports.SingleAsync()).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReportsRejectMissingReason(string reason)
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent();
        var controller = env.Attach(new EventReportsController(env.Db));
        Assert.IsType<BadRequestObjectResult>((await controller.Create(new(ev.Id, reason), default)).Result);
        Assert.Empty(env.Db.EventReports);
    }

    [Fact]
    public async Task ReportsRejectMissingEvent()
    {
        using var env = new TestEnvironment();
        var controller = env.Attach(new EventReportsController(env.Db));
        Assert.IsType<NotFoundObjectResult>((await controller.Create(new(123, "Spam"), default)).Result);
        Assert.Empty(env.Db.EventReports);
    }
}
