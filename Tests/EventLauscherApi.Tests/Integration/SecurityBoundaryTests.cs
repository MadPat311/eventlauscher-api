using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EventLauscherApi.Data;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventLauscherApi.Tests.Integration;

public sealed class SecurityBoundaryTests
{
    [Theory]
    [InlineData("/auth/login", 10, HttpStatusCode.Unauthorized)]
    [InlineData("/auth/forgot-password", 5, HttpStatusCode.OK)]
    public async Task LimitsArePerConnectionIpAndIgnoreForgedForwardingHeaders(
        string path, int permits, HttpStatusCode allowedStatus)
    {
        using var factory = new ApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<IStartupFilter, RemoteAddressFilter>()));
        using var client = configured.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "203.0.113.10");
        for (var i = 0; i < permits; i++)
        {
            using var response = await client.PostAsJsonAsync(path, new { email = "unknown@example.test", password = "invalid" });
            Assert.Equal(allowedStatus, response.StatusCode);
        }
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");
        using var rejected = await client.PostAsJsonAsync(path, new { email = "unknown@example.test", password = "invalid" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Remote-IP");
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "203.0.113.11");
        using var otherIp = await client.PostAsJsonAsync(path, new { email = "unknown@example.test", password = "invalid" });
        Assert.Equal(allowedStatus, otherIp.StatusCode);
    }

    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("missing", HttpStatusCode.Unauthorized)]
    [InlineData("tampered", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-secret", HttpStatusCode.Unauthorized)]
    public async Task WebhookPersistsOnlyAuthenticRawBodies(string scenario, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client();
        // Whitespace and non-ASCII text must be included exactly as received.
        const string original = "{ \"message\": \"Grüße 🎉\" }\n";
        var body = scenario == "tampered" ? original + " " : original;
        var secret = scenario == "wrong-secret" ? "another-secret" : "test-only-meta-app-secret";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/instagram")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };
        if (scenario != "missing")
            request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(original))).ToLowerInvariant());
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        await factory.SeedAsync(async sp =>
        {
            var entries = await sp.GetRequiredService<EventContext>().InstagramWebhookInboxEntries.ToListAsync();
            if (scenario == "valid") Assert.Equal(original, Assert.Single(entries).PayloadJson);
            else Assert.Empty(entries);
        });
    }

    // Only installed in this test host; production never accepts this header.
    private sealed class RemoteAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Remote-IP"].ToString());
                await continuation(context);
            });
            next(app);
        };
    }
}
