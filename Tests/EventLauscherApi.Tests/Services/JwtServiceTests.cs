using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EventLauscherApi.Models;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EventLauscherApi.Tests.Services;

public sealed class JwtServiceTests
{
    [Fact]
    public void AccessTokenHasValidSignatureIdentityRolesIssuerAudienceAndExpiry()
    {
        using var env = new TestEnvironment();
        var before = DateTimeOffset.UtcNow;
        var user = new AppUser { Id = env.UserId, UserName = "tester", Email = "tester@example.com" };
        var (token, expires) = env.Jwt.CreateAccessToken(user, new[] { "User", "Reviewer" });
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestEnvironment.JwtKey)),
            ValidateIssuer = true, ValidIssuer = "eventlauscher-tests", ValidateAudience = true, ValidAudience = "eventlauscher-tests",
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        }, out var validated);
        Assert.Equal(SecurityAlgorithms.HmacSha256, Assert.IsType<JwtSecurityToken>(validated).Header.Alg);
        Assert.Equal(env.UserId.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("tester@example.com", principal.FindFirstValue(ClaimTypes.Email));
        Assert.Equal("tester", principal.FindFirstValue("preferred_username"));
        Assert.True(principal.IsInRole("User"));
        Assert.True(principal.IsInRole("Reviewer"));
        Assert.False(principal.IsInRole("Admin"));
        Assert.InRange(expires, before.AddMinutes(15), DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.InRange((expires.UtcDateTime - validated.ValidTo).TotalSeconds, 0, 1);
    }

    [Fact]
    public void AccessTokenHandlesMissingOptionalUserFieldsAndNoRoles()
    {
        using var env = new TestEnvironment();
        var (token, _) = env.Jwt.CreateAccessToken(new AppUser { Id = env.UserId }, Array.Empty<string>());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("", jwt.Claims.Single(x => x.Type == "preferred_username").Value);
        Assert.Equal("", jwt.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == ClaimTypes.Role);
    }

    [Fact]
    public async Task RefreshTokensAreUniqueRandomPersistedAndExpireAccordingToConfiguration()
    {
        using var env = new TestEnvironment();
        var before = DateTimeOffset.UtcNow;
        var first = await env.Jwt.CreateAndStoreRefreshToken(env.UserId, default);
        var second = await env.Jwt.CreateAndStoreRefreshToken(env.UserId, default);
        Assert.NotEqual(first, second);
        Assert.Equal(64, Convert.FromBase64String(first).Length);
        await using var reload = env.Reload();
        Assert.Equal(2, await reload.RefreshTokens.CountAsync());
        var stored = await reload.RefreshTokens.SingleAsync(x => x.Token == first);
        Assert.Equal(env.UserId, stored.UserId);
        Assert.False(stored.Revoked);
        Assert.InRange(stored.ExpiresAt, before.AddDays(7), DateTimeOffset.UtcNow.AddDays(7));
        Assert.Equal((true, env.UserId), await env.Jwt.ValidateRefreshToken(first, default));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, -1)]
    public async Task RefreshValidationRejectsRevokedAndExpiredTokens(bool revoked, int days)
    {
        using var env = new TestEnvironment();
        env.Db.RefreshTokens.Add(new RefreshToken { UserId = env.UserId, Token = "invalid", Revoked = revoked, ExpiresAt = DateTimeOffset.UtcNow.AddDays(days) });
        await env.Db.SaveChangesAsync();
        Assert.Equal((false, Guid.Empty), await env.Jwt.ValidateRefreshToken("invalid", default));
        Assert.Equal((false, Guid.Empty), await env.Jwt.ValidateRefreshToken("unknown", default));
    }

    [Fact]
    public async Task RevocationPersistsReplacementAndUnknownTokenIsHarmless()
    {
        using var env = new TestEnvironment();
        var token = await env.Jwt.CreateAndStoreRefreshToken(env.UserId, default);
        await env.Jwt.RevokeRefreshToken(token, "replacement", default);
        await env.Jwt.RevokeRefreshToken("unknown", null, default);
        await using var reload = env.Reload();
        var stored = await reload.RefreshTokens.SingleAsync();
        Assert.True(stored.Revoked);
        Assert.Equal("replacement", stored.ReplacedByToken);
        Assert.Equal((false, Guid.Empty), await env.Jwt.ValidateRefreshToken(token, default));
    }
}
