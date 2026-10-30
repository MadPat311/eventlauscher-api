using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventLauscherApi.Tests.Integration;

public sealed class InputValidationTests
{
    public static IEnumerable<object[]> TextLimits()
    {
        yield return new object[] { "title", 200 };
        yield return new object[] { "description", 4000 };
        yield return new object[] { "location", 400 };
        yield return new object[] { "date", 32 };
        yield return new object[] { "time", 16 };
    }

    [Theory]
    [MemberData(nameof(TextLimits))]
    public async Task CreateRejectsOverlongFieldsWithoutSaving(string field, int maximum)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await ApiTestUsers.SignIn(factory, client, "uploader", "User");
        var body = new Dictionary<string, object?> { ["title"] = "Valid", [field] = new string('x', maximum + 1) };
        using var response = await client.PostAsJsonAsync("/api/events", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.SeedAsync(async sp => Assert.Empty(await sp.GetRequiredService<EventContext>().Events.ToListAsync()));
    }

    [Theory]
    [MemberData(nameof(TextLimits))]
    public async Task ReviewRejectsOverlongFieldsWithoutChangingEvent(string field, int maximum)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var owner = await ApiTestUsers.SignIn(factory, client, "reviewer", "Reviewer");
        var id = await SeedEvent(factory, owner);
        using var response = await client.PutAsJsonAsync($"/api/reviewer/events/{id}/review", new Dictionary<string, object?>
        {
            ["title"] = "Must not be saved", [field] = new string('x', maximum + 1)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.SeedAsync(async sp =>
        {
            var stored = await sp.GetRequiredService<EventContext>().Events.SingleAsync();
            Assert.Equal("Original", stored.Title);
            Assert.Null(stored.ReviewedAt);
        });
    }

    [Theory]
    [MemberData(nameof(TextLimits))]
    public async Task CreateAndReviewAcceptFieldsExactlyAtDatabaseLimit(string field, int maximum)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await ApiTestUsers.SignIn(factory, client, "reviewer", "Reviewer");
        var body = new Dictionary<string, object?> { ["title"] = "Valid", [field] = new string('x', maximum) };
        using var created = await client.PostAsJsonAsync("/api/events", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        using var reviewed = await client.PutAsJsonAsync($"/api/reviewer/events/{id}/review", body);
        Assert.Equal(HttpStatusCode.NoContent, reviewed.StatusCode);
    }

    [Theory]
    [InlineData("latitude", -90.01)]
    [InlineData("latitude", 90.01)]
    [InlineData("longitude", -180.01)]
    [InlineData("longitude", 180.01)]
    public async Task CreateAndReviewRejectOutOfRangeCoordinates(string field, double coordinate)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var owner = await ApiTestUsers.SignIn(factory, client, "reviewer", "Reviewer");
        var id = await SeedEvent(factory, owner);
        var body = new Dictionary<string, object?> { ["title"] = "Event", [field] = coordinate };
        using var create = await client.PostAsJsonAsync("/api/events", body);
        using var review = await client.PutAsJsonAsync($"/api/reviewer/events/{id}/review", body);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, review.StatusCode);
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    public async Task CoordinateBoundariesIncludingZeroAreAccepted(double latitude, double longitude)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await ApiTestUsers.SignIn(factory, client, "reviewer", "Reviewer");
        var body = new { title = "Coordinates", latitude, longitude };
        using var response = await client.PostAsJsonAsync("/api/events", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = payload.GetProperty("id").GetInt32();
        Assert.Equal(latitude, payload.GetProperty("latitude").GetDouble());
        Assert.Equal(longitude, payload.GetProperty("longitude").GetDouble());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/reviewer/events/{id}/review", body)).StatusCode);
    }

    [Theory]
    [InlineData(2000, HttpStatusCode.OK)]
    [InlineData(2001, HttpStatusCode.BadRequest)]
    public async Task ReportReasonRespectsDatabaseLimit(int length, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var owner = await ApiTestUsers.SignIn(factory, client, "uploader", "User");
        var id = await SeedEvent(factory, owner);
        using var response = await client.PostAsJsonAsync("/api/eventreports", new { eventId = id, reason = new string('x', length) });
        Assert.Equal(expected, response.StatusCode);
        await factory.SeedAsync(async sp => Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0,
            await sp.GetRequiredService<EventContext>().EventReports.CountAsync()));
    }

    private static async Task<int> SeedEvent(ApiFactory factory, Guid owner)
    {
        var id = 0;
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<EventContext>();
            var ev = new Event { Title = "Original", UploadUserId = owner };
            db.Events.Add(ev);
            await db.SaveChangesAsync();
            id = ev.Id;
        });
        return id;
    }
}
