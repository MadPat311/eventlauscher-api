using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EventLauscherApi.Data;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventLauscherApi.Tests.Integration;

[Trait("Category", "PostgreSQL")]
public sealed class PostgreSqlHttpTests
{
    [PostgreSqlFact]
    public async Task DraftReviewPublicationFavoritesAndWithdrawalWorkAcrossUsersAndFreshRequests()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        using var factory = new ApiFactory(database.ConnectionString);
        using var uploader = factory.Client();
        using var reviewer = factory.Client();
        using var visitor = factory.Client();
        using var anonymous = factory.Client();
        await ApiTestUsers.SignIn(factory, uploader, "uploader", "User");
        await ApiTestUsers.SignIn(factory, reviewer, "reviewer", "Reviewer");
        await ApiTestUsers.SignIn(factory, visitor, "visitor", "User");

        using var created = await uploader.PostAsJsonAsync("/api/events", new
        {
            title = "Original", date = "2026-11-15", time = "19:30", sourceUrl = "https://example.test/post", sourcePlatform = "instagram"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.PostAsync($"/api/events/{id}/save", null)).StatusCode);
        Assert.Single((await reviewer.GetFromJsonAsync<JsonElement>("/api/reviewer/events/unpublished")).EnumerateArray());
        var detail = await reviewer.GetFromJsonAsync<JsonElement>($"/api/reviewer/events/{id}");
        Assert.Equal("uploader@example.test", detail.GetProperty("uploader").GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PutAsJsonAsync($"/api/reviewer/events/{id}/review", new { title = "Reviewed", latitude = 0, longitude = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsync($"/api/reviewer/events/{id}/publish", null)).StatusCode);
        Assert.Empty((await reviewer.GetFromJsonAsync<JsonElement>("/api/reviewer/events/unpublished")).EnumerateArray());
        var published = await anonymous.GetFromJsonAsync<JsonElement>($"/api/events/{id}");
        Assert.Equal("Reviewed", published.GetProperty("title").GetString());
        Assert.Equal("https://example.test/post", published.GetProperty("sourceUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/s/e/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/events/{id}/calendar")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await visitor.PostAsync($"/api/events/{id}/save", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await visitor.PostAsync($"/api/events/{id}/save", null)).StatusCode);
        Assert.Single((await visitor.GetFromJsonAsync<JsonElement>("/api/events/saved")).EnumerateArray());
        Assert.Empty((await uploader.GetFromJsonAsync<JsonElement>("/api/events/saved")).EnumerateArray());
        Assert.Single((await anonymous.GetFromJsonAsync<JsonElement>("/api/public/users/VISITOR/favorites")).EnumerateArray());
        var profile = await anonymous.GetFromJsonAsync<JsonElement>("/api/public/users/visitor");
        Assert.Equal(1, profile.GetProperty("favoritesCount").GetInt32());
        Assert.Equal(0, profile.GetProperty("uploadsCount").GetInt32());
        Assert.Equal(1, (await anonymous.GetFromJsonAsync<JsonElement>("/api/public/users/uploader")).GetProperty("uploadsCount").GetInt32());

        using var report = await visitor.PostAsJsonAsync("/api/eventreports", new { eventId = id, reason = "Wrong date" });
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Single((await reviewer.GetFromJsonAsync<JsonElement>("/api/eventreports")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsync($"/api/reviewer/events/{id}/unpublish", null)).StatusCode);
        Assert.Empty((await visitor.GetFromJsonAsync<JsonElement>("/api/events/saved")).EnumerateArray());
        Assert.Empty((await anonymous.GetFromJsonAsync<JsonElement>("/api/public/users/visitor/favorites")).EnumerateArray());
        Assert.Equal(0, (await anonymous.GetFromJsonAsync<JsonElement>("/api/public/users/visitor")).GetProperty("favoritesCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/s/e/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/events/{id}/calendar")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reviewer.PostAsync($"/api/reviewer/events/{id}/publish", null)).StatusCode);
        Assert.Single((await visitor.GetFromJsonAsync<JsonElement>("/api/events/saved")).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsync($"/api/reviewer/events/{id}/reject", null)).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<JsonElement>("/api/events")).EnumerateArray());
        Assert.Empty((await visitor.GetFromJsonAsync<JsonElement>("/api/events/saved")).EnumerateArray());
    }

    [PostgreSqlFact]
    public async Task ActualConfirmationAndPasswordResetLinksFromMailWorkThroughHttp()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        using var factory = new ApiFactory(database.ConnectionString);
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/register", new
        {
            email = "alice@example.test", userName = "alice", password = TestEnvironment.Password,
            roles = new[] { "Admin" }, emailConfirmed = true
        })).StatusCode);
        var confirmation = Link(Assert.Single(factory.Mail.Messages).Html);
        Assert.Equal("/auth/confirm-email", confirmation.AbsolutePath);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(TestEnvironment.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(confirmation.PathAndQuery)).StatusCode);
        using var login = await Login(TestEnvironment.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "User" }, payload.GetProperty("user").GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/forgot-password", new { email = "alice@example.test" })).StatusCode);
        var reset = Link(factory.Mail.Messages[1].Html);
        Assert.Equal("/reset", reset.AbsolutePath);
        var query = QueryHelpers.ParseQuery(reset.Query);
        var request = new { userId = Guid.Parse(query["uid"].ToString()), token = query["token"].ToString(), newPassword = "newpassword123" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/reset-password", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/reset-password", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(TestEnvironment.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login("newpassword123")).StatusCode);

        Task<HttpResponseMessage> Login(string password) => client.PostAsJsonAsync("/auth/login", new { email = "alice@example.test", password });
    }

    [PostgreSqlFact]
    public async Task InvalidMediaReferenceAndOverlongInputReturnBadRequestWithoutDatabaseWrites()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        using var factory = new ApiFactory(database.ConnectionString);
        using var client = factory.Client();
        await ApiTestUsers.SignIn(factory, client, "uploader", "User");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/events", new { title = "Event", mediaId = int.MaxValue })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/events", new { title = new string('x', 201) })).StatusCode);
        await using var reload = database.Context();
        Assert.Empty(await reload.Events.ToListAsync());
    }

    private static Uri Link(string html) => new(WebUtility.HtmlDecode(Regex.Match(html, "href=\"([^\"]+)\"").Groups[1].Value));
}
