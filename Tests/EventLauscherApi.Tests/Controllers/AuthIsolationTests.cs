using EventLauscherApi.Controllers;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventLauscherApi.Tests.Controllers;

public sealed class AuthIsolationTests
{
    [Fact]
    public async Task ConfirmationTokenCannotConfirmAnotherUser()
    {
        using var env = new TestEnvironment();
        var alice = await env.AddUser("alice", false);
        var bob = await env.AddUser("bob", false);
        var token = await env.Users.GenerateEmailConfirmationTokenAsync(alice);
        var auth = env.Auth();
        Assert.IsType<BadRequestObjectResult>(await auth.ConfirmEmail(new ConfirmEmailDto(bob.Id, token)));
        Assert.False(bob.EmailConfirmed);
        Assert.IsType<OkObjectResult>(await auth.ConfirmEmail(new ConfirmEmailDto(alice.Id, token)));
        Assert.True(alice.EmailConfirmed);
    }

    [Fact]
    public async Task ResetTokenCannotChangeAnotherUsersPassword()
    {
        using var env = new TestEnvironment();
        var alice = await env.AddUser("alice");
        var bob = await env.AddUser("bob");
        var token = await env.Users.GeneratePasswordResetTokenAsync(alice);
        var auth = env.Auth();
        Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(new(bob.Id, token, "changedpass123")));
        Assert.True(await env.Users.CheckPasswordAsync(bob, TestEnvironment.Password));
        Assert.IsType<OkResult>(await auth.ResetPassword(new(alice.Id, token, "changedpass123")));
        Assert.True(await env.Users.CheckPasswordAsync(alice, "changedpass123"));
    }

    [Fact]
    public async Task ResetAndConfirmationTokensCannotBeUsedForEachOthersPurpose()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser(confirmed: false);
        var reset = await env.Users.GeneratePasswordResetTokenAsync(user);
        var confirm = await env.Users.GenerateEmailConfirmationTokenAsync(user);
        var auth = env.Auth();
        Assert.IsType<BadRequestObjectResult>(await auth.ConfirmEmail(new ConfirmEmailDto(user.Id, reset)));
        Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(new(user.Id, confirm, "changedpass123")));
        Assert.False(user.EmailConfirmed);
        Assert.True(await env.Users.CheckPasswordAsync(user, TestEnvironment.Password));
    }

    [Fact]
    public async Task ChangingPasswordInvalidatesAllPreviouslyIssuedResetTokens()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser();
        var first = await env.Users.GeneratePasswordResetTokenAsync(user);
        var second = await env.Users.GeneratePasswordResetTokenAsync(user);
        var auth = env.Auth();
        Assert.IsType<OkResult>(await auth.ResetPassword(new(user.Id, first, "changedpass123")));
        Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(new(user.Id, second, "anotherpass123")));
        Assert.True(await env.Users.CheckPasswordAsync(user, "changedpass123"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshRejectsLockedOrUnconfirmedUsersWithoutIssuingReplacement(bool locked)
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser(confirmed: locked);
        var token = await env.Jwt.CreateAndStoreRefreshToken(user.Id, default);
        if (locked)
            Assert.True((await env.Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddDays(1))).Succeeded);
        Assert.IsType<UnauthorizedResult>(await env.Auth().Refresh(new(token)));
        Assert.Equal(1, await env.Db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task SuccessfulLoginResetsFailedAttemptCount()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser();
        var auth = env.Auth();
        for (var i = 0; i < 4; i++)
            Assert.IsType<UnauthorizedResult>(await auth.Login(new(user.Email!, "wrongpass")));
        Assert.IsType<OkObjectResult>(await auth.Login(new(user.Email!, TestEnvironment.Password)));
        Assert.Equal(0, await env.Users.GetAccessFailedCountAsync(user));
        Assert.IsType<UnauthorizedResult>(await auth.Login(new(user.Email!, "wrongpass")));
        Assert.False(await env.Users.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task MeReturnsUnauthorizedAfterAccountWasDeleted()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser();
        var auth = env.Attach(env.Auth(), user.Id);
        Assert.True((await env.Users.DeleteAsync(user)).Succeeded);
        Assert.IsType<UnauthorizedResult>(await auth.Me());
    }
}
