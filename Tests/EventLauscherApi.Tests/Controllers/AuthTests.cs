using System.Text.Json;
using EventLauscherApi.Controllers;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EventLauscherApi.Tests.Infrastructure.TestEnvironment;

namespace EventLauscherApi.Tests.Controllers;

public sealed class AuthTests
{
    [Theory]
    [InlineData("", "validuser", "EMAIL_REQUIRED")]
    [InlineData(" ", "validuser", "EMAIL_REQUIRED")]
    [InlineData("alice@example.com", null, "USERNAME_REQUIRED")]
    [InlineData("alice@example.com", " ", "USERNAME_REQUIRED")]
    [InlineData("alice@example.com", "alice@example.com", "USERNAME_MUST_NOT_BE_EMAIL")]
    [InlineData("alice@example.com", "ab", "USERNAME_INVALID")]
    [InlineData("alice@example.com", "bad name", "USERNAME_INVALID")]
    [InlineData("alice@example.com", "<script>", "USERNAME_INVALID")]
    [InlineData("alice@example.com", "abcdefghijklmnopqrstuvwxyz123456789", "USERNAME_INVALID")]
    public async Task RegistrationRejectsInvalidInputWithoutCreatingUserOrSendingMail(string email, string? username, string error)
    {
        using var env = new TestEnvironment();
        var result = Assert.IsType<BadRequestObjectResult>(await env.Auth().Register(new(email, Password, username)));
        Assert.Equal(error, Json(result.Value).GetProperty("error").GetString());
        Assert.Equal(1, await env.Db.Users.CountAsync());
        Assert.Empty(env.Mail.Messages);
    }

    [Fact]
    public async Task RegistrationTrimsFieldsAssignsUserRoleHashesPasswordAndSendsConfirmationLink()
    {
        using var env = new TestEnvironment();
        Assert.IsType<OkObjectResult>(await env.Auth().Register(new(" alice@example.com ", Password, " alice ")));
        var user = (await env.Users.FindByNameAsync("ALICE"))!;
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("alice", user.UserName);
        Assert.False(user.EmailConfirmed);
        Assert.True(await env.Users.CheckPasswordAsync(user, Password));
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.True(await env.Users.IsInRoleAsync(user, "User"));
        var mail = Assert.Single(env.Mail.Messages);
        Assert.Equal(user.Email, mail.To);
        Assert.Contains($"https://api.example.test/auth/confirm-email?uid={user.Id}&token=", mail.Html);
    }

    [Theory]
    [InlineData("TESTER", "new@example.com", Password)]
    [InlineData("alice", "tester@example.com", Password)]
    [InlineData("alice", "alice@example.com", "short")]
    public async Task RegistrationRejectsDuplicateUsernameDuplicateEmailAndWeakPassword(string username, string email, string password)
    {
        using var env = new TestEnvironment();
        Assert.IsType<BadRequestObjectResult>(await env.Auth().Register(new(email, password, username)));
        Assert.Equal(1, await env.Db.Users.CountAsync());
        Assert.Empty(env.Mail.Messages);
    }

    [Fact]
    public async Task LoginReturnsTokensUserAndRolesAndMeMatchesCurrentUser()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser(roles: new[] { "Reviewer", "User" });
        var auth = env.Auth();
        var result = Ok(await auth.Login(new(user.Email!, Password)));
        Assert.False(string.IsNullOrEmpty(result.GetProperty("accessToken").GetString()));
        var refresh = result.GetProperty("refreshToken").GetString()!;
        Assert.Equal((true, user.Id), await env.Jwt.ValidateRefreshToken(refresh, default));
        var responseUser = result.GetProperty("user");
        Assert.Equal(user.Id, responseUser.GetProperty("id").GetGuid());
        Assert.Equal("alice", responseUser.GetProperty("userName").GetString());
        Assert.True(responseUser.GetProperty("emailConfirmed").GetBoolean());
        Assert.Contains("Reviewer", responseUser.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        env.Attach(auth, user.Id);
        Assert.Equal(user.Id, Ok(await auth.Me()).GetProperty("id").GetGuid());
        Assert.True(Ok(auth.ReviewerPing()).GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("unknown@example.com", Password)]
    [InlineData("alice@example.com", "wrongpassword")]
    public async Task LoginRejectsUnknownUserOrWrongPasswordWithoutIssuingTokens(string email, string password)
    {
        using var env = new TestEnvironment();
        await env.AddUser();
        Assert.IsType<UnauthorizedResult>(await env.Auth().Login(new(email, password)));
        Assert.Empty(env.Db.RefreshTokens);
    }

    [Fact]
    public async Task LoginRejectsUnconfirmedAndLockedUsersAndLocksAfterFiveFailures()
    {
        using var env = new TestEnvironment();
        var unconfirmed = await env.AddUser("unconfirmed", false);
        var auth = env.Auth();
        // RequireConfirmedEmail is checked by SignInManager before password validation.
        Assert.IsType<UnauthorizedResult>(await auth.Login(new(unconfirmed.Email!, Password)));
        var user = await env.AddUser();
        for (var i = 0; i < 5; i++)
            Assert.IsType<UnauthorizedResult>(await auth.Login(new(user.Email!, "wrongpassword")));
        Assert.True(await env.Users.IsLockedOutAsync(user));
        Assert.IsType<UnauthorizedResult>(await auth.Login(new(user.Email!, Password)));
        Assert.Empty(env.Db.RefreshTokens);
    }

    [Fact]
    public async Task RefreshRotatesTokenRejectsReplayAndLogoutRevokesReplacement()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser();
        var old = await env.Jwt.CreateAndStoreRefreshToken(user.Id, default);
        var auth = env.Auth();
        var response = Ok(await auth.Refresh(new(old)));
        var replacement = response.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(old, replacement);
        Assert.False(string.IsNullOrEmpty(response.GetProperty("accessToken").GetString()));
        Assert.Equal((false, Guid.Empty), await env.Jwt.ValidateRefreshToken(old, default));
        Assert.Equal((true, user.Id), await env.Jwt.ValidateRefreshToken(replacement, default));
        Assert.Equal(replacement, (await env.Db.RefreshTokens.SingleAsync(x => x.Token == old)).ReplacedByToken);
        Assert.IsType<UnauthorizedResult>(await auth.Refresh(new(old)));
        Assert.IsType<UnauthorizedResult>(await auth.Refresh(new("unknown")));
        Assert.IsType<OkResult>(await auth.Logout(new(replacement)));
        Assert.IsType<OkResult>(await auth.Logout(new("unknown")));
        Assert.Equal((false, Guid.Empty), await env.Jwt.ValidateRefreshToken(replacement, default));
    }

    [Fact]
    public async Task RefreshRejectsTokenForDeletedUser()
    {
        using var env = new TestEnvironment();
        var token = await env.Jwt.CreateAndStoreRefreshToken(Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(await env.Auth().Refresh(new(token)));
    }

    [Fact]
    public async Task JsonEmailConfirmationValidatesTokensAndIsIdempotent()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser(confirmed: false);
        var auth = env.Auth();
        Assert.IsType<NotFoundObjectResult>(await auth.ConfirmEmail(new ConfirmEmailDto(Guid.NewGuid(), "invalid")));
        Assert.IsType<BadRequestObjectResult>(await auth.ConfirmEmail(new ConfirmEmailDto(user.Id, "invalid")));
        Assert.False(user.EmailConfirmed);
        var token = await env.Users.GenerateEmailConfirmationTokenAsync(user);
        Assert.True(Ok(await auth.ConfirmEmail(new ConfirmEmailDto(user.Id, token))).GetProperty("success").GetBoolean());
        Assert.True(user.EmailConfirmed);
        Assert.True(Ok(await auth.ConfirmEmail(new ConfirmEmailDto(user.Id, "already used"))).GetProperty("alreadyConfirmed").GetBoolean());
    }

    [Fact]
    public async Task HtmlEmailConfirmationReturnsCorrectStatusAndNoReferrerHeader()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser(confirmed: false);
        var auth = env.Auth();
        Assert.Equal(404, Assert.IsType<ContentResult>(await auth.ConfirmEmail(Guid.NewGuid(), "invalid")).StatusCode);
        Assert.Equal(400, Assert.IsType<ContentResult>(await auth.ConfirmEmail(user.Id, "invalid")).StatusCode);
        var token = await env.Users.GenerateEmailConfirmationTokenAsync(user);
        var success = Assert.IsType<ContentResult>(await auth.ConfirmEmail(user.Id, token));
        Assert.Equal(200, success.StatusCode);
        Assert.Equal("text/html; charset=utf-8", success.ContentType);
        Assert.Contains(user.Email!, success.Content);
        Assert.Equal("no-referrer", auth.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal(200, Assert.IsType<ContentResult>(await auth.ConfirmEmail(user.Id, "already used")).StatusCode);
    }

    [Fact]
    public async Task ForgotPasswordDoesNotRevealUnknownEmailAndSendsResetLinkForExistingUser()
    {
        using var env = new TestEnvironment();
        var auth = env.Auth();
        Assert.IsType<OkResult>(await auth.ForgotPassword(new("unknown@example.com")));
        Assert.Empty(env.Mail.Messages);
        var user = await env.AddUser();
        Assert.IsType<OkResult>(await auth.ForgotPassword(new(user.Email!)));
        var mail = Assert.Single(env.Mail.Messages);
        Assert.Equal(user.Email, mail.To);
        Assert.Contains($"https://app.example.test/reset?uid={user.Id}&token=", mail.Html);
    }

    [Fact]
    public async Task ResetPasswordRequiresValidSingleUseTokenAndChangesLoginCredentials()
    {
        using var env = new TestEnvironment();
        var user = await env.AddUser();
        var auth = env.Auth();
        Assert.IsType<NotFoundResult>(await auth.ResetPassword(new(Guid.NewGuid(), "invalid", "newpassword123")));
        Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(new(user.Id, "invalid", "newpassword123")));
        var token = await env.Users.GeneratePasswordResetTokenAsync(user);
        Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(new(user.Id, token, "short")));
        Assert.IsType<OkResult>(await auth.ResetPassword(new(user.Id, token, "newpassword123")));
        Assert.True(await env.Users.CheckPasswordAsync(user, "newpassword123"));
        Assert.False(await env.Users.CheckPasswordAsync(user, Password));
        Assert.IsType<BadRequestObjectResult>(await auth.ResetPassword(new(user.Id, token, "anotherpass123")));
        var preview = Ok(auth.ResetPasswordPreview(user.Id, "test-token"));
        Assert.Equal(user.Id, preview.GetProperty("uid").GetGuid());
        Assert.Equal("test-token", preview.GetProperty("token").GetString());
    }
}
