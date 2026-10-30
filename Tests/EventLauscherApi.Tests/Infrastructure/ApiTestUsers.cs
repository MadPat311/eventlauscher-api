using System.Net.Http.Headers;
using EventLauscherApi.Models;
using EventLauscherApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EventLauscherApi.Tests.Infrastructure;

internal static class ApiTestUsers
{
    public static async Task<Guid> SignIn(ApiFactory factory, HttpClient client, string username, params string[] roles)
    {
        Guid id = default;
        await factory.SeedAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<AppUser>>();
            var manager = services.GetRequiredService<RoleManager<AppRole>>();
            var user = new AppUser { UserName = username, Email = $"{username}@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestEnvironment.Password)).Succeeded);
            foreach (var role in roles)
            {
                if (!await manager.RoleExistsAsync(role))
                    Assert.True((await manager.CreateAsync(new AppRole { Name = role })).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            }
            id = user.Id;
            var (token, _) = services.GetRequiredService<IJwtService>().CreateAccessToken(user, roles);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        });
        return id;
    }
}
