using System.Globalization;
using System.Security.Claims;
using System.Text;
using EventLauscherApi.Contracts.Requests;
using EventLauscherApi.Models;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EventLauscherApi.Tests.Infrastructure.TestEnvironment;

namespace EventLauscherApi.Tests.Controllers;

public sealed class EventWorkflowTests
{
    [Fact]
    public async Task PublicFeedOnlyIncludesPublishedEventsInDateOrder()
    {
        using var env = new TestEnvironment();
        await env.AddEvent();
        await env.AddEvent(EventStatus.Rejected);
        var later = await env.AddEvent(EventStatus.Published, date: "2026-12-01");
        var earlier = await env.AddEvent(EventStatus.Published, date: "2026-11-01");
        var result = await env.Events().GetEvents();
        Assert.Equal(new[] { earlier.Id, later.Id }, result.Value!.Select(e => e.Id));
        Assert.IsType<NotFoundResult>((await env.Events().GetEvent(int.MaxValue)).Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateRequiresTitleAndDoesNotPersistInvalidRequest(string? title)
    {
        using var env = new TestEnvironment();
        Assert.IsType<BadRequestObjectResult>((await env.Events().CreateEvent(new() { Title = title! })).Result);
        Assert.Empty(await env.Db.Events.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-id")]
    public async Task CreateForbidsMissingOrMalformedIdentity(string? id)
    {
        using var env = new TestEnvironment();
        var controller = env.Events();
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            id is null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));
        Assert.IsType<ForbidResult>((await controller.CreateEvent(new() { Title = "Event" })).Result);
        Assert.Empty(env.Db.Events);
    }

    [Fact]
    public async Task CreateCopiesAllEditableFieldsAndAcceptsSubjectClaim()
    {
        using var env = new TestEnvironment();
        var controller = env.Events();
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", env.UserId.ToString()) }, "test"));
        var request = new CreateEventRequest
        {
            Title = "  Concert  ", Description = "Live music", Location = "Berlin", Date = "2026-11-15",
            Time = "19:30", Latitude = 52.5, Longitude = 13.4, MediaId = 42
        };
        var result = Assert.IsType<CreatedAtActionResult>((await controller.CreateEvent(request)).Result);
        var created = Assert.IsType<Event>(result.Value);
        await using var reload = env.Reload();
        var stored = await reload.Events.SingleAsync();
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal("GetEvent", result.ActionName);
        Assert.Equal("Concert", stored.Title);
        Assert.Equal(request.Description, stored.Description);
        Assert.Equal(request.Location, stored.Location);
        Assert.Equal(request.Date, stored.Date);
        Assert.Equal(request.Time, stored.Time);
        Assert.Equal(request.Latitude, stored.Latitude);
        Assert.Equal(request.Longitude, stored.Longitude);
        Assert.Equal(request.MediaId, stored.MediaId);
        Assert.Equal(env.UserId, stored.UploadUserId);
        Assert.Equal(EventStatus.Draft, stored.Status);
        Assert.Null(stored.ReviewedAt);
        Assert.Null(stored.ReviewedByUserId);
    }

    [Fact]
    public async Task OwnUploadsArePrivateAndOrderedByPublicationReviewThenId()
    {
        using var env = new TestEnvironment();
        var draft = await env.AddEvent();
        var reviewed = await env.AddEvent(EventStatus.Rejected);
        reviewed.ReviewedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var published = await env.AddEvent(EventStatus.Published);
        published.PublishedAt = DateTimeOffset.UtcNow;
        await env.AddEvent(owner: Guid.NewGuid());
        await env.Db.SaveChangesAsync();
        var list = Ok(await env.Events().GetMyUploads(default));
        Assert.Equal(new[] { published.Id, reviewed.Id, draft.Id }, list.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task SaveAndUnsaveAreIdempotentAndIsolatedPerUser()
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(EventStatus.Published);
        var other = Guid.NewGuid();
        Assert.IsType<NoContentResult>(await env.Events().SaveEvent(ev.Id, default));
        Assert.IsType<NoContentResult>(await env.Events().SaveEvent(ev.Id, default));
        Assert.Single(env.Db.SavedEvents);
        Assert.True(Ok(await env.Events().IsSaved(ev.Id, default)).GetProperty("saved").GetBoolean());
        Assert.False(Ok(await env.Events(other).IsSaved(ev.Id, default)).GetProperty("saved").GetBoolean());
        Assert.IsType<NoContentResult>(await env.Events(other).UnsaveEvent(ev.Id, default));
        Assert.Single(env.Db.SavedEvents);
        Assert.IsType<NoContentResult>(await env.Events().UnsaveEvent(ev.Id, default));
        Assert.IsType<NoContentResult>(await env.Events().UnsaveEvent(ev.Id, default));
        Assert.False(Ok(await env.Events().IsSaved(ev.Id, default)).GetProperty("saved").GetBoolean());
        await using var reload = env.Reload();
        Assert.Empty(reload.SavedEvents);
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.Rejected)]
    public async Task UnpublishedAndMissingEventsCannotBeSaved(EventStatus status)
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(status);
        Assert.IsType<NotFoundResult>(await env.Events().SaveEvent(ev.Id, default));
        Assert.IsType<NotFoundResult>(await env.Events().SaveEvent(int.MaxValue, default));
        Assert.Empty(env.Db.SavedEvents);
    }

    [Fact]
    public async Task SavedListHidesUnpublishedEventsAndOtherUsersAndOrdersNewestFirst()
    {
        using var env = new TestEnvironment();
        var older = await env.AddEvent(EventStatus.Published);
        var newer = await env.AddEvent(EventStatus.Published);
        var hidden = await env.AddEvent();
        var foreign = await env.AddEvent(EventStatus.Published);
        env.Db.SavedEvents.AddRange(
            new SavedEvent { UserId = env.UserId, EventId = older.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new SavedEvent { UserId = env.UserId, EventId = newer.Id },
            new SavedEvent { UserId = env.UserId, EventId = hidden.Id },
            new SavedEvent { UserId = Guid.NewGuid(), EventId = foreign.Id });
        await env.Db.SaveChangesAsync();
        var list = Ok(await env.Events().GetSavedEvents(default));
        Assert.Equal(new[] { newer.Id, older.Id }, list.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task DuplicateCorpusIncludesEveryStatusAndCoordinatesInDescendingIdOrder()
    {
        using var env = new TestEnvironment();
        var draft = await env.AddEvent();
        var published = await env.AddEvent(EventStatus.Published);
        var rejected = await env.AddEvent(EventStatus.Rejected);
        rejected.Latitude = 52.5;
        rejected.Longitude = 13.4;
        await env.Db.SaveChangesAsync();
        var list = Ok(await env.Events().GetDupCorpus(default));
        Assert.Equal(new[] { rejected.Id, published.Id, draft.Id }, list.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()));
        Assert.Equal(52.5, list[0].GetProperty("latitude").GetDouble());
        Assert.Equal(13.4, list[0].GetProperty("longitude").GetDouble());
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.Rejected)]
    public async Task SharingAndCalendarHideUnpublishedEvents(EventStatus status)
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(status);
        Assert.IsType<NotFoundResult>(await env.Events().ShareEvent(ev.Id, default));
        Assert.IsType<NotFoundResult>(await env.Events().DownloadCalendarEntry(ev.Id, default));
        Assert.IsType<NotFoundResult>(await env.Events().ShareEvent(int.MaxValue, default));
        Assert.IsType<NotFoundResult>(await env.Events().DownloadCalendarEntry(int.MaxValue, default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    public async Task SharePageEncodesUserTextAndIncludesLinksAndImage(int? mediaId)
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(EventStatus.Published);
        ev.Title = "<script>alert(1)</script>";
        ev.Location = "A & B";
        ev.Time = "19:00";
        ev.MediaId = mediaId;
        await env.Db.SaveChangesAsync();
        var controller = env.Events();
        var html = Assert.IsType<ContentResult>(await controller.ShareEvent(ev.Id, default));
        Assert.Equal("text/html; charset=utf-8", html.ContentType);
        Assert.Contains("&lt;script&gt;", html.Content);
        Assert.DoesNotContain("<script>", html.Content);
        Assert.Contains("A &amp; B", html.Content);
        Assert.Contains($"https://api.example.test/events/{ev.Id}", html.Content);
        Assert.Contains($"https://api.example.test/s/e/{ev.Id}", html.Content);
        Assert.Contains(mediaId.HasValue ? "/api/MediaFiles/42" : "/assets/share-default.jpg", html.Content);
        Assert.Equal("public,max-age=300", controller.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("2026-11-15", "19:30", 19, 30)]
    [InlineData("15.11.2026", "09:05", 9, 5)]
    [InlineData("5.1.2026", "23:59", 23, 59)]
    [InlineData("2026-11-15", null, 9, 0)]
    [InlineData("2026-11-15", "invalid", 9, 0)]
    [InlineData("2026-11-15", "25:60", 9, 0)]
    public async Task CalendarParsesSupportedDatesAndDefaultsInvalidTime(string date, string? time, int hour, int minute)
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(EventStatus.Published, date: date);
        ev.Time = time;
        await env.Db.SaveChangesAsync();
        var file = Assert.IsType<FileContentResult>(await env.Events().DownloadCalendarEntry(ev.Id, default));
        var ics = Encoding.UTF8.GetString(file.FileContents);
        var parsed = DateTime.ParseExact(date, new[] { "yyyy-MM-dd", "d.M.yyyy", "dd.MM.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
        var start = new DateTime(parsed.Year, parsed.Month, parsed.Day, hour, minute, 0, DateTimeKind.Local);
        Assert.Contains($"DTSTART:{start.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}", ics);
        Assert.Contains($"DTEND:{start.AddHours(2).ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}", ics);
        Assert.Contains($"UID:{ev.Id}@eventlauscher.de", ics);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR", ics);
        Assert.Equal("text/calendar; charset=utf-8", file.ContentType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("2026-02-30")]
    [InlineData("11/15/2026")]
    public async Task CalendarRejectsInvalidDates(string? date)
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(EventStatus.Published, date: date);
        Assert.IsType<BadRequestObjectResult>(await env.Events().DownloadCalendarEntry(ev.Id, default));
    }

    [Fact]
    public async Task CalendarEscapesTextAndProducesSafeDownloadName()
    {
        using var env = new TestEnvironment();
        var ev = await env.AddEvent(EventStatus.Published);
        ev.Title = "A/B; C,D";
        ev.Description = "First\\part\r\nSecond;line,ok";
        ev.Location = "Hall; 1, Berlin";
        await env.Db.SaveChangesAsync();
        var file = Assert.IsType<FileContentResult>(await env.Events().DownloadCalendarEntry(ev.Id, default));
        var ics = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("SUMMARY:A/B\\; C\\,D", ics);
        Assert.Contains("LOCATION:Hall\\; 1\\, Berlin", ics);
        Assert.Contains("First\\\\part\\nSecond\\;line\\,ok\\n\\nMehr Infos: https://api.example.test/events/", ics);
        Assert.DoesNotContain('/', file.FileDownloadName);
        Assert.DoesNotContain(' ', file.FileDownloadName);
        Assert.EndsWith(".ics", file.FileDownloadName);
    }
}
