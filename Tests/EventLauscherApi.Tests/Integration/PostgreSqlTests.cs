using System.Security.Claims;
using EventLauscherApi.Controllers;
using EventLauscherApi.Models;
using EventLauscherApi.Models.Instagram;
using EventLauscherApi.Services.Instagram;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static EventLauscherApi.Tests.Infrastructure.TestEnvironment;

namespace EventLauscherApi.Tests.Integration;

[Trait("Category", "PostgreSQL")]
public sealed class PostgreSqlTests
{
    [PostgreSqlFact]
    public async Task MissingUploaderViolatesForeignKey()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var db = database.Context();
        db.Events.Add(new Event { Title = "Invalid", UploadUserId = Guid.NewGuid() });
        await AssertSqlState(db.SaveChangesAsync, PostgresErrorCodes.ForeignKeyViolation);
    }

    [PostgreSqlTheory]
    [InlineData("Title", 201)]
    [InlineData("Description", 4001)]
    [InlineData("Location", 401)]
    [InlineData("Date", 33)]
    [InlineData("Time", 17)]
    [InlineData("SourceUrl", 2001)]
    [InlineData("SourcePlatform", 21)]
    public async Task PostgreSqlEnforcesEventTextLimits(string field, int length)
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var user = await database.AddUser();
        await using var db = database.Context();
        var ev = new Event { Title = "Event", UploadUserId = user.Id };
        typeof(Event).GetProperty(field)!.SetValue(ev, new string('x', length));
        db.Events.Add(ev);
        await AssertSqlState(db.SaveChangesAsync, PostgresErrorCodes.StringDataRightTruncation);
    }

    [PostgreSqlFact]
    public async Task SavedEventCannotBeInsertedTwiceAcrossIndependentContexts()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var user = await database.AddUser();
        await using var first = database.Context();
        var ev = new Event { Title = "Event", UploadUserId = user.Id };
        first.Events.Add(ev);
        await first.SaveChangesAsync();
        first.SavedEvents.Add(new SavedEvent { UserId = user.Id, EventId = ev.Id });
        await first.SaveChangesAsync();
        await using var second = database.Context();
        second.SavedEvents.Add(new SavedEvent { UserId = user.Id, EventId = ev.Id });
        await AssertSqlState(second.SaveChangesAsync, PostgresErrorCodes.UniqueViolation);
        Assert.Equal(1, await first.SavedEvents.CountAsync());
    }

    [PostgreSqlFact]
    public async Task SourceKeysAndRefreshTokensMustBeUniqueAcrossContexts()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var user = await database.AddUser();
        await using var first = database.Context();
        var inbox = new WebhookInboxEntry { PayloadJson = "{}" };
        first.InstagramWebhookInboxEntries.Add(inbox);
        await first.SaveChangesAsync();
        first.InstagramSources.Add(new InstagramSource { WebhookInboxEntryId = inbox.Id, SourceKey = "media:1" });
        first.RefreshTokens.Add(new RefreshToken { UserId = user.Id, Token = "same-token", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await first.SaveChangesAsync();
        await using (var second = database.Context())
        {
            second.InstagramSources.Add(new InstagramSource { WebhookInboxEntryId = inbox.Id, SourceKey = "media:1" });
            await AssertSqlState(second.SaveChangesAsync, PostgresErrorCodes.UniqueViolation);
        }
        await using var third = database.Context();
        third.RefreshTokens.Add(new RefreshToken { UserId = user.Id, Token = "same-token", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await AssertSqlState(third.SaveChangesAsync, PostgresErrorCodes.UniqueViolation);
    }

    [PostgreSqlFact]
    public async Task EventDeletionCascadesToFavoritesAndReportsOnServer()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var user = await database.AddUser();
        await using var db = database.Context();
        var ev = new Event { Title = "Event", UploadUserId = user.Id };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        db.SavedEvents.Add(new SavedEvent { UserId = user.Id, EventId = ev.Id });
        db.EventReports.Add(new EventReport { EventId = ev.Id, Reason = "Spam", ReporterUserId = user.Id });
        await db.SaveChangesAsync();
        // ExecuteDelete bypasses EF relationship fixup and tests database enforcement.
        await db.Events.Where(x => x.Id == ev.Id).ExecuteDeleteAsync();
        await using var reload = database.Context();
        Assert.Empty(await reload.SavedEvents.ToListAsync());
        Assert.Empty(await reload.EventReports.ToListAsync());
        Assert.Single(await reload.Users.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task ReporterDeletionAnonymizesReportAndRemovesTheirFavoritesAndRefreshTokens()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var owner = await database.AddUser("owner");
        var reporter = await database.AddUser("reporter");
        await using var db = database.Context();
        var ev = new Event { Title = "Event", UploadUserId = owner.Id };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        db.EventReports.Add(new EventReport { EventId = ev.Id, ReporterUserId = reporter.Id, Reason = "Spam" });
        db.SavedEvents.Add(new SavedEvent { UserId = reporter.Id, EventId = ev.Id });
        db.RefreshTokens.Add(new RefreshToken { UserId = reporter.Id, Token = "token", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        await db.Users.Where(x => x.Id == reporter.Id).ExecuteDeleteAsync();
        await using var reload = database.Context();
        var report = await reload.EventReports.SingleAsync();
        Assert.Null(report.ReporterUserId);
        Assert.Equal("Spam", report.Reason);
        Assert.Single(await reload.Events.ToListAsync());
        Assert.Empty(await reload.SavedEvents.ToListAsync());
        Assert.Empty(await reload.RefreshTokens.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task UploaderCannotBeDeletedWhileOwningAnEvent()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var owner = await database.AddUser();
        await using var db = database.Context();
        db.Events.Add(new Event { Title = "Owned", UploadUserId = owner.Id });
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Users.Where(x => x.Id == owner.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Events.CountAsync());
    }

    [PostgreSqlFact]
    public async Task DeletingInboxCascadesToItsSources()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var db = database.Context();
        var inbox = new WebhookInboxEntry { PayloadJson = "{}" };
        db.InstagramWebhookInboxEntries.Add(inbox);
        await db.SaveChangesAsync();
        db.InstagramSources.Add(new InstagramSource { WebhookInboxEntryId = inbox.Id, SourceKey = "media:1" });
        await db.SaveChangesAsync();
        await db.InstagramWebhookInboxEntries.ExecuteDeleteAsync();
        Assert.Empty(await db.InstagramSources.AsNoTracking().ToListAsync());
    }

    [PostgreSqlFact]
    public async Task FailedSaveRollsBackAllChangesInTheBatch()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var user = await database.AddUser();
        await using var db = database.Context();
        db.MediaFiles.Add(new MediaFile { FileName = "should-rollback.png", Data = new byte[] { 1, 2, 3 } });
        db.Events.Add(new Event { Title = new string('x', 201), UploadUserId = user.Id });
        await AssertSqlState(db.SaveChangesAsync, PostgresErrorCodes.StringDataRightTruncation);
        await using var reload = database.Context();
        Assert.Empty(await reload.MediaFiles.ToListAsync());
        Assert.Empty(await reload.Events.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task OwnUploadsSortDatedEntriesBeforeDraftsWithoutReviewTimestamps()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var user = await database.AddUser();
        await using var db = database.Context();
        var draft = new Event { Title = "Draft", UploadUserId = user.Id };
        var reviewed = new Event { Title = "Reviewed", UploadUserId = user.Id, ReviewedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var published = new Event { Title = "Published", UploadUserId = user.Id, Status = EventStatus.Published, PublishedAt = DateTimeOffset.UtcNow };
        db.Events.AddRange(draft, reviewed, published);
        await db.SaveChangesAsync();
        var controller = new EventsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) }, "test"))
            } }
        };
        var items = Ok(await controller.GetMyUploads(default));
        Assert.Equal(new[] { published.Id, reviewed.Id, draft.Id }, items.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
    }

    [PostgreSqlFact]
    public async Task WebhookBatchDeduplicationSurvivesUniqueIndexAndFreshContexts()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using (var db = database.Context())
        {
            db.InstagramWebhookInboxEntries.AddRange(
                new WebhookInboxEntry { PayloadJson = Instagram.WebhookTests.MentionPayload("m1", "c1") },
                new WebhookInboxEntry { PayloadJson = Instagram.WebhookTests.MentionPayload("m1", "c2") });
            await db.SaveChangesAsync();
            Assert.Equal(2, await new InstagramWebhookInboxProcessor(db, NullLogger<InstagramWebhookInboxProcessor>.Instance).ProcessReceivedAsync());
        }
        await using var reload = database.Context();
        var source = await reload.InstagramSources.SingleAsync();
        Assert.Equal(2, source.MentionCount);
        Assert.Equal("c1", source.FirstCommentId);
        Assert.Equal("c2", source.LastCommentId);
        Assert.Equal(2, await reload.InstagramWebhookInboxEntries.CountAsync(x => x.Status == "MentionDetected"));
    }

    [PostgreSqlFact]
    public async Task SourceLinkMigrationUpDownAndUpPreservesExistingEvent()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var user = await database.AddUser();
        await using var db = database.Context();
        db.Events.Add(new Event { Title = "Existing event", UploadUserId = user.Id });
        await db.SaveChangesAsync();
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.EndsWith("_AddEventSourceLinks", migrations[^1]);
        // Reconstruct only the immediately preceding schema, keeping history explicit.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Events\" DROP COLUMN \"SourceUrl\", DROP COLUMN \"SourcePlatform\"");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL)");
        foreach (var migration in migrations[..^1])
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migration}, {"8.0.27"})");
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var ev = await db.Events.SingleAsync();
        Assert.Equal("Existing event", ev.Title);
        Assert.Null(ev.SourceUrl);
        ev.SourceUrl = "https://example.test/original";
        ev.SourcePlatform = "instagram";
        await db.SaveChangesAsync();
        await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        Assert.Equal(1, await db.Events.Select(x => x.Id).CountAsync());
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        ev = await db.Events.SingleAsync();
        Assert.Equal("Existing event", ev.Title);
        Assert.Null(ev.SourceUrl);
        Assert.Null(ev.SourcePlatform);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    private static async Task AssertSqlState(Func<CancellationToken, Task<int>> operation, string state)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => operation(default));
        Assert.Equal(state, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
