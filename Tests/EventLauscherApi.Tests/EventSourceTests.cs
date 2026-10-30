using System.Security.Claims;
using System.Text.Json;
using EventLauscherApi.Contracts.Requests;
using EventLauscherApi.Controllers;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventLauscherApi.Tests;

public sealed class EventSourceTests
{
    private const string InstagramUrl = "https://www.instagram.com/p/original/?a=1&b=2";
    private const string FacebookUrl = "https://www.facebook.com/events/123456789/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("instagram", InstagramUrl)]
    [InlineData("facebook", FacebookUrl)]
    [InlineData(null, InstagramUrl)]
    [InlineData("facebook", null)]
    public async Task CreatePersistsSourceAndPublicDetailReturnsIt(string? platform, string? url)
    {
        using var fixture = new Fixture();
        var result = await fixture.Events("Organizer").CreateEvent(new CreateEventRequest
        {
            Title = "Testevent", SourceUrl = url, SourcePlatform = platform
        });
        var created = Assert.IsType<Event>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);

        // A fresh context verifies persisted values, rather than a tracked instance.
        await using var reload = fixture.Reload();
        var detail = await new EventsController(reload).GetEvent(created.Id);
        Assert.Equal(EventStatus.Published, detail.Value!.Status);
        AssertSource(ToJson(detail.Value), url, platform);
        var feed = await new EventsController(reload).GetEvents();
        AssertSource(ToJson(feed.Value)[0], url, platform);
    }

    [Theory]
    [InlineData("{\"title\":\"Legacy\"}")]
    [InlineData("{\"title\":\"Legacy\",\"sourceUrl\":null,\"sourcePlatform\":null}")]
    [InlineData("{\"title\":\"Legacy\",\"sourceUrl\":\"\",\"sourcePlatform\":\"  \"}")]
    public async Task LegacyAndEmptyCreateRequestsRemainValidDrafts(string json)
    {
        using var fixture = new Fixture();
        var result = await fixture.Events().CreateEvent(JsonSerializer.Deserialize<CreateEventRequest>(json, JsonOptions)!);
        var created = Assert.IsType<Event>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);

        await using var reload = fixture.Reload();
        var stored = await reload.Events.SingleAsync();
        Assert.Null(stored.SourceUrl);
        Assert.Null(stored.SourcePlatform);
        Assert.Equal(EventStatus.Draft, stored.Status);
        Assert.Null(stored.PublishedAt);
        Assert.IsType<NotFoundResult>((await new EventsController(reload).GetEvent(created.Id)).Result);
        var my = fixture.Events();
        AssertSource(ToJson(Assert.IsType<OkObjectResult>(await my.GetMyUploads(default)).Value)[0], null, null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Organizer")]
    [InlineData("Reviewer")]
    [InlineData("Admin")]
    public async Task SourceDoesNotChangePublicationRules(string? role)
    {
        using var fixture = new Fixture();
        var result = await fixture.Events(role).CreateEvent(new CreateEventRequest
        {
            Title = "Event", SourceUrl = InstagramUrl, SourcePlatform = "instagram"
        });
        var created = Assert.IsType<Event>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(role is null ? EventStatus.Draft : EventStatus.Published, created.Status);
        Assert.Equal(role is not null, created.PublishedAt.HasValue);
    }

    [Theory]
    [InlineData("{\"title\":\"Geändert\"}")]
    [InlineData("{\"title\":\"Geändert\",\"sourceUrl\":null,\"sourcePlatform\":null}")]
    public async Task LegacyReviewAndExplicitNullKeepExistingSource(string json)
    {
        using var fixture = new Fixture();
        var original = await fixture.SeedEvent();
        var request = JsonSerializer.Deserialize<ReviewerEventsController.ReviewUpdate>(json, JsonOptions)!;
        Assert.IsType<NoContentResult>(await fixture.Reviewer().UpdateForReview(original.Id, request, default));

        await using var reload = fixture.Reload();
        var stored = await reload.Events.SingleAsync();
        Assert.Equal("Geändert", stored.Title);
        Assert.Equal(InstagramUrl, stored.SourceUrl);
        Assert.Equal("instagram", stored.SourcePlatform);
        Assert.Equal(original.Status, stored.Status);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "\t")]
    [InlineData(FacebookUrl, "facebook")]
    [InlineData("  " + FacebookUrl + "  ", " facebook ")]
    public async Task ReviewReplacesOrExplicitlyClearsSource(string url, string platform)
    {
        using var fixture = new Fixture();
        var original = await fixture.SeedEvent();
        var result = await fixture.Reviewer().UpdateForReview(original.Id, new()
        {
            SourceUrl = url, SourcePlatform = platform
        }, default);
        Assert.IsType<NoContentResult>(result);

        await using var reload = fixture.Reload();
        var stored = await reload.Events.SingleAsync();
        Assert.Equal(string.IsNullOrWhiteSpace(url) ? null : url.Trim(), stored.SourceUrl);
        Assert.Equal(string.IsNullOrWhiteSpace(platform) ? null : platform.Trim(), stored.SourcePlatform);
    }

    [Fact]
    public async Task ReviewCanChangeOneFieldWhileKeepingTheOther()
    {
        using var fixture = new Fixture();
        var original = await fixture.SeedEvent();
        var result = await fixture.Reviewer().UpdateForReview(original.Id, new() { SourceUrl = FacebookUrl }, default);
        Assert.IsType<NoContentResult>(result);
        await using var reload = fixture.Reload();
        var stored = await reload.Events.SingleAsync();
        Assert.Equal(FacebookUrl, stored.SourceUrl);
        Assert.Equal("instagram", stored.SourcePlatform);
    }

    public static IEnumerable<object?[]> InvalidSources()
    {
        foreach (var url in new[] { "/p/relative/", "www.instagram.com/p/123/", "ftp://example.com/file", "javascript:alert(1)", "file:///tmp/image", "https://", "//example.com/post", "not a url", "https://example.com/" + new string('a', 2000) })
            yield return new object?[] { url, "instagram", "sourceUrl" };
        foreach (var platform in new[] { "Instagram", "FACEBOOK", "tiktok", "42" })
            yield return new object?[] { InstagramUrl, platform, "sourcePlatform" };
    }

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public async Task InvalidCreateReturnsBadRequestWithoutSaving(string url, string platform, string field)
    {
        using var fixture = new Fixture();
        var result = await fixture.Events().CreateEvent(new CreateEventRequest
        {
            Title = "Event", SourceUrl = url, SourcePlatform = platform
        });
        var error = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(field, Assert.IsType<string>(error.Value));
        Assert.Empty(await fixture.Db.Events.ToListAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidSources))]
    public async Task InvalidReviewReturnsBadRequestWithoutChangingEvent(string url, string platform, string field)
    {
        using var fixture = new Fixture();
        var original = await fixture.SeedEvent();
        var result = await fixture.Reviewer().UpdateForReview(original.Id, new()
        {
            Title = "Should not be saved", SourceUrl = url, SourcePlatform = platform
        }, default);
        Assert.Contains(field, Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result).Value));
        Assert.Equal("Original", original.Title);
        Assert.Equal(InstagramUrl, original.SourceUrl);
        Assert.Null(original.ReviewedAt);
        await using var reload = fixture.Reload();
        Assert.Equal(InstagramUrl, (await reload.Events.SingleAsync()).SourceUrl);
    }

    [Theory]
    [InlineData("http://example.com/post")]
    [InlineData("HTTPS://www.instagram.com/reel/123/")]
    [InlineData("  " + InstagramUrl + "  ")]
    public async Task AbsoluteHttpUrlsAreAcceptedAndTrimmed(string url)
    {
        using var fixture = new Fixture();
        var result = await fixture.Events().CreateEvent(new CreateEventRequest { Title = "Event", SourceUrl = url });
        var created = Assert.IsType<Event>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(url.Trim(), created.SourceUrl);
    }

    [Fact]
    public async Task EventsSharingDeduplicatedMediaKeepIndependentSources()
    {
        using var fixture = new Fixture();
        var media = new MediaFile
        {
            FileName = "shared.jpg", Hash = "same-hash", Data = new byte[] { 1, 2, 3 },
            SourceURL = "https://legacy.example/media", SourcePlattform = "legacy"
        };
        fixture.Db.MediaFiles.Add(media);
        await fixture.Db.SaveChangesAsync();
        var first = await fixture.SeedEvent(media.Id);
        var result = await fixture.Events().CreateEvent(new CreateEventRequest
        {
            Title = "Second", MediaId = media.Id, SourceUrl = FacebookUrl, SourcePlatform = "facebook"
        });
        var second = Assert.IsType<Event>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        await using (var beforeEdit = fixture.Reload())
        {
            var stored = await beforeEdit.Events.OrderBy(e => e.Id).ToListAsync();
            Assert.Equal(InstagramUrl, stored[0].SourceUrl);
            Assert.Equal(FacebookUrl, stored[1].SourceUrl);
            Assert.Equal("facebook", stored[1].SourcePlatform);
        }
        Assert.IsType<NoContentResult>(await fixture.Reviewer().UpdateForReview(second.Id, new() { SourceUrl = "", SourcePlatform = "" }, default));

        await using var reload = fixture.Reload();
        var events = await reload.Events.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(InstagramUrl, events[0].SourceUrl);
        Assert.Equal("instagram", events[0].SourcePlatform);
        Assert.Null(events[1].SourceUrl);
        Assert.Equal(first.MediaId, events[1].MediaId);
        var storedMedia = await reload.MediaFiles.SingleAsync();
        Assert.Equal("https://legacy.example/media", storedMedia.SourceURL);
        Assert.Equal("legacy", storedMedia.SourcePlattform);
    }

    [Fact]
    public async Task AllEventProjectionsIncludeSourceWithExactJsonNames()
    {
        using var fixture = new Fixture();
        var original = await fixture.SeedEvent();
        var reviewer = fixture.Reviewer();
        var drafts = ToJson(Assert.IsType<OkObjectResult>(await reviewer.GetUnpublished(default)).Value);
        AssertSource(drafts[0], InstagramUrl, "instagram");
        var detail = ToJson(Assert.IsType<OkObjectResult>(await reviewer.GetDetail(original.Id, default)).Value);
        AssertSource(detail.GetProperty("event"), InstagramUrl, "instagram");
        var events = fixture.Events();
        AssertSource(ToJson(Assert.IsType<OkObjectResult>(await events.GetMyUploads(default)).Value)[0], InstagramUrl, "instagram");
        AssertSource(ToJson(Assert.IsType<OkObjectResult>(await events.GetDupCorpus(default)).Value)[0], InstagramUrl, "instagram");

        Assert.IsType<NoContentResult>(await reviewer.Publish(original.Id, default));
        Assert.IsType<NoContentResult>(await events.SaveEvent(original.Id, default));
        AssertSource(ToJson(Assert.IsType<OkObjectResult>(await events.GetSavedEvents(default)).Value)[0], InstagramUrl, "instagram");
        var publicController = new PublicController(fixture.Db, fixture.UserManager);
        AssertSource(ToJson(Assert.IsType<OkObjectResult>(await publicController.GetPublicFavorites("tester", default)).Value)[0], InstagramUrl, "instagram");
        AssertSource(ToJson((await events.GetEvent(original.Id)).Value), InstagramUrl, "instagram");

        Assert.IsType<NoContentResult>(await reviewer.Unpublish(original.Id, default));
        Assert.Equal(InstagramUrl, original.SourceUrl);
        Assert.Equal(EventStatus.Draft, original.Status);
    }

    [Fact]
    public async Task LegacyEventResponsesContainNullSourceFields()
    {
        using var fixture = new Fixture();
        var original = await fixture.SeedEvent();
        original.SourceUrl = null;
        original.SourcePlatform = null;
        original.Status = EventStatus.Published;
        await fixture.Db.SaveChangesAsync();
        await using var reload = fixture.Reload();
        AssertSource(ToJson((await new EventsController(reload).GetEvent(original.Id)).Value), null, null);
    }

    [Fact]
    public void PostgreSqlMigrationOnlyAddsTwoNullableEventColumnsAndSnapshotMatches()
    {
        // Only generate SQL/model metadata. Never connect to a database.
        using var db = new EventContext(new DbContextOptionsBuilder<EventContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused").Options);
        var migrations = db.GetService<IMigrationsAssembly>();
        var latest = migrations.Migrations.Last();
        Assert.EndsWith("_AddEventSourceLinks", latest.Key);
        var migration = migrations.CreateMigration(latest.Value, db.Database.ProviderName!);
        Assert.Equal(2, migration.UpOperations.Count);
        foreach (var operation in migration.UpOperations)
        {
            var column = Assert.IsType<AddColumnOperation>(operation);
            Assert.Equal("Events", column.Table);
            Assert.Contains(column.Name, new[] { "SourceUrl", "SourcePlatform" });
            Assert.True(column.IsNullable);
            Assert.Null(column.DefaultValue);
        }
        Assert.Equal(2, migration.DownOperations.Count);
        Assert.All(migration.DownOperations, operation => Assert.IsType<DropColumnOperation>(operation));
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript(migrations.Migrations.Keys.Reverse().Skip(1).First(), latest.Key);
        Assert.Contains("ALTER TABLE \"Events\" ADD \"SourceUrl\" character varying(2000)", sql);
        Assert.Contains("ALTER TABLE \"Events\" ADD \"SourcePlatform\" character varying(20)", sql);
        Assert.DoesNotContain("UPDATE \"MediaFiles\"", sql);
    }

    private static JsonElement ToJson(object? value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    private static void AssertSource(JsonElement json, string? url, string? platform)
    {
        Assert.Equal(url, json.GetProperty("sourceUrl").GetString());
        Assert.Equal(platform, json.GetProperty("sourcePlatform").GetString());
        Assert.False(json.TryGetProperty("sourceURL", out _));
        Assert.False(json.TryGetProperty("sourcePlattform", out _));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly Guid _userId = Guid.NewGuid();
        private readonly DbContextOptions<EventContext> _options;
        public EventContext Db { get; }
        public UserManager<AppUser> UserManager => _services.GetRequiredService<UserManager<AppUser>>();

        public Fixture()
        {
            var databaseName = Guid.NewGuid().ToString();
            _options = new DbContextOptionsBuilder<EventContext>().UseInMemoryDatabase(databaseName).Options;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<EventContext>(options => options.UseInMemoryDatabase(databaseName));
            services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<EventContext>();
            _services = services.BuildServiceProvider();
            Db = _services.GetRequiredService<EventContext>();
            Db.Users.Add(new AppUser
            {
                Id = _userId, UserName = "tester", NormalizedUserName = "TESTER", Email = "tester@example.com"
            });
            Db.SaveChanges();
        }

        public EventContext Reload() => new(_options);

        public EventsController Events(string? role = null) => new(Db) { ControllerContext = Context(role) };
        public ReviewerEventsController Reviewer() => new(Db, UserManager) { ControllerContext = Context("Reviewer") };

        private ControllerContext Context(string? role)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, _userId.ToString()) };
            if (role is not null) claims.Add(new(ClaimTypes.Role, role));
            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
            };
        }

        public async Task<Event> SeedEvent(int? mediaId = null)
        {
            var ev = new Event
            {
                Title = "Original", UploadUserId = _userId, MediaId = mediaId,
                SourceUrl = InstagramUrl, SourcePlatform = "instagram"
            };
            Db.Events.Add(ev);
            await Db.SaveChangesAsync();
            return ev;
        }

        public void Dispose() => _services.Dispose();
    }
}
