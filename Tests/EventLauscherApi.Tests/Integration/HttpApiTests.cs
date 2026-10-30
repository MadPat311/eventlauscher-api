using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using EventLauscherApi.Services;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EventLauscherApi.Tests.Integration;

public sealed class HttpApiTests
{
    [Theory]
    [InlineData("GET", "/auth/me")]
    [InlineData("GET", "/auth/reviewer/ping")]
    [InlineData("POST", "/api/events")]
    [InlineData("GET", "/api/events/my")]
    [InlineData("GET", "/api/events/saved")]
    [InlineData("GET", "/api/events/dup-corpus")]
    [InlineData("POST", "/api/events/1/save")]
    [InlineData("DELETE", "/api/events/1/save")]
    [InlineData("GET", "/api/events/1/saved")]
    [InlineData("POST", "/api/mediafiles")]
    [InlineData("GET", "/api/reviewer/events/unpublished")]
    [InlineData("GET", "/api/reviewer/events/1")]
    [InlineData("PUT", "/api/reviewer/events/1/review")]
    [InlineData("POST", "/api/reviewer/events/1/publish")]
    [InlineData("POST", "/api/reviewer/events/1/unpublish")]
    [InlineData("POST", "/api/reviewer/events/1/reject")]
    [InlineData("POST", "/api/reviewer/events/1/ban-uploader")]
    [InlineData("POST", "/api/reviewer/events/users/11111111-1111-1111-1111-111111111111/unban")]
    [InlineData("GET", "/api/eventreports")]
    [InlineData("DELETE", "/api/eventreports/1")]
    public async Task ProtectedEndpointsRequireAuthentication(string method, string path)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/reviewer/events/unpublished")]
    [InlineData("GET", "/auth/reviewer/ping")]
    [InlineData("POST", "/api/reviewer/events/1/publish")]
    [InlineData("PUT", "/api/reviewer/events/1/review")]
    [InlineData("POST", "/api/reviewer/events/1/reject")]
    [InlineData("GET", "/api/eventreports")]
    [InlineData("DELETE", "/api/eventreports/1")]
    public async Task NormalUserCannotAccessModeration(string method, string path)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await Authenticate(factory, client, "User");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Reviewer")]
    [InlineData("Admin")]
    public async Task ReviewerAndAdminCanAccessReviewerPolicyAndQueue(string role)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await Authenticate(factory, client, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/reviewer/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/reviewer/events/unpublished")).StatusCode);
    }

    [Theory]
    [InlineData("User", 0)]
    [InlineData("Organizer", 1)]
    [InlineData("Reviewer", 1)]
    [InlineData("Admin", 1)]
    public async Task RealJwtRolesControlPublicationAndPublicVisibility(string role, int expectedStatus)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var userId = await Authenticate(factory, client, role);
        using var response = await client.PostAsJsonAsync("/api/events", new
        {
            title = "  HTTP event  ", sourceUrl = "https://example.com/original", sourcePlatform = "instagram",
            status = 1, uploadUserId = Guid.NewGuid(), reviewedByUserId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal(expectedStatus, created.GetProperty("status").GetInt32());
        Assert.Equal(userId, created.GetProperty("uploadUserId").GetGuid());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("reviewedByUserId").ValueKind);
        Assert.Equal("HTTP event", created.GetProperty("title").GetString());
        Assert.EndsWith($"/api/Events/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(expectedStatus == 1 ? HttpStatusCode.OK : HttpStatusCode.NotFound, (await client.GetAsync($"/api/events/{id}")).StatusCode);
        var feed = await client.GetFromJsonAsync<JsonElement>("/api/events");
        Assert.Equal(expectedStatus, feed.GetArrayLength());
    }

    [Fact]
    public async Task HttpRegistrationConfirmationLoginRefreshLogoutRoundTrip()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/register", new
        {
            email = "http@example.com", userName = "http-user", password = TestEnvironment.Password
        })).StatusCode);
        Assert.Single(factory.Mail.Messages);
        Guid userId = default;
        string token = "";
        await factory.SeedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByNameAsync("http-user"))!;
            userId = user.Id;
            token = await users.GenerateEmailConfirmationTokenAsync(user);
        });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/confirm-email", new { userId, token })).StatusCode);
        using var login = await client.PostAsJsonAsync("/auth/login", new { email = "http@example.com", password = TestEnvironment.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.GetProperty("accessToken").GetString());
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal(userId, me.GetProperty("id").GetGuid());
        var refreshToken = payload.GetProperty("refreshToken").GetString();
        using var refreshed = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var replacement = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();
        Assert.NotEqual(refreshToken, replacement);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/logout", new { refreshToken = replacement })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = replacement })).StatusCode);
    }

    [Fact]
    public async Task ReportsRecognizeReporterFromRealJwt()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var userId = await Authenticate(factory, client, "User");
        var eventId = 0;
        await factory.SeedAsync(async sp =>
        {
            var db = sp.GetRequiredService<EventContext>();
            var ev = new Event { Title = "Reported", UploadUserId = userId, Status = EventStatus.Published };
            db.Events.Add(ev);
            await db.SaveChangesAsync();
            eventId = ev.Id;
        });
        using var response = await client.PostAsJsonAsync("/api/eventreports", new { eventId, reason = "Spam" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId, report.GetProperty("reporterUserId").GetGuid());
    }

    [Fact]
    public async Task MultipartUploadAndDownloadRoundTrip()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await Authenticate(factory, client, "User");
        using var form = new MultipartFormDataContent();
        byte[] bytes = { 1, 4, 9, 16 };
        form.Add(new ByteArrayContent(bytes), "file", "photo.png");
        using var uploaded = await client.PostAsync("/api/mediafiles", form);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var id = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        client.DefaultRequestHeaders.Authorization = null;
        using var downloaded = await client.GetAsync($"/api/mediafiles/{id}");
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("image/png", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await downloaded.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task InvalidJsonAndMissingRequiredFieldsAreRejectedByModelBinding()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        await Authenticate(factory, client, "User");
        using var malformed = new StringContent("{bad json", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/events", malformed)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/events", new { description = "missing title" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/events", new { title = "Event", sourceUrl = "javascript:alert(1)" })).StatusCode);
    }

    [Fact]
    public async Task InvalidBearerTokenIsRejected()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    public async Task JwtValidationRejectsExpiredTokensWrongIssuerAudienceOrSignature(string invalidPart)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        var key = invalidPart == "signature" ? "different-test-key-with-at-least-32-bytes-123456789" : TestEnvironment.JwtKey;
        var token = new JwtSecurityToken(
            issuer: invalidPart == "issuer" ? "other-issuer" : "eventlauscher-tests",
            audience: invalidPart == "audience" ? "other-audience" : "eventlauscher-tests",
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
            expires: DateTime.UtcNow.AddMinutes(invalidPart == "expired" ? -5 : 15),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task PasswordResetRateLimiterRejectsSixthRequest()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/auth/forgot-password", new { email = "unknown@example.com" })).StatusCode);
        using var rejected = await client.PostAsJsonAsync("/auth/forgot-password", new { email = "unknown@example.com" });
        // ASP.NET Core's default rejection status configured by the current application.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
    }

    private static async Task<Guid> Authenticate(ApiFactory factory, HttpClient client, string role)
    {
        Guid userId = default;
        await factory.SeedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var roles = sp.GetRequiredService<RoleManager<AppRole>>();
            Assert.True((await roles.CreateAsync(new AppRole { Name = role })).Succeeded);
            var user = new AppUser { UserName = "http-user", Email = "http@example.com", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestEnvironment.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            userId = user.Id;
            var (token, _) = sp.GetRequiredService<IJwtService>().CreateAccessToken(user, new[] { role });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        });
        return userId;
    }
}
