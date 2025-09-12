using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EventLauscherApi.Services
{
    public interface IJwtService
    {
        (string accessToken, DateTimeOffset expires) CreateAccessToken(AppUser user, IList<string> roles);
        Task<string> CreateAndStoreRefreshToken(Guid userId, CancellationToken ct);
        Task<(bool ok, Guid userId)> ValidateRefreshToken(string token, CancellationToken ct);
        Task RevokeRefreshToken(string token, string? replacedBy, CancellationToken ct);
    }

    public class JwtService : IJwtService
    {
        private readonly IConfiguration _cfg;
        private readonly EventContext _db;
        public JwtService(IConfiguration cfg, EventContext db) { _cfg = cfg; _db = db; }

        public (string accessToken, DateTimeOffset expires) CreateAccessToken(AppUser user, IList<string> roles)
        {
            var jwt = _cfg.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTimeOffset.UtcNow.AddMinutes(int.Parse(jwt["AccessTokenMinutes"]!));

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email!),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName ?? user.Email!)
            };
            foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"], audience: jwt["Audience"],
                claims: claims, expires: expires.UtcDateTime, signingCredentials: creds);

            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }

        public async Task<string> CreateAndStoreRefreshToken(Guid userId, CancellationToken ct)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var days = int.Parse(_cfg["Jwt:RefreshTokenDays"]!);
            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                Token = token,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(days)
            });
            await _db.SaveChangesAsync(ct);
            return token;
        }

        public async Task<(bool ok, Guid userId)> ValidateRefreshToken(string token, CancellationToken ct)
        {
            var rt = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Token == token, ct);
            if (rt is null || rt.Revoked || rt.ExpiresAt <= DateTimeOffset.UtcNow) return (false, Guid.Empty);
            return (true, rt.UserId);
        }

        public async Task RevokeRefreshToken(string token, string? replacedBy, CancellationToken ct)
        {
            var rt = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Token == token, ct);
            if (rt is null) return;
            rt.Revoked = true;
            rt.ReplacedByToken = replacedBy;
            await _db.SaveChangesAsync(ct);
        }
    }
}
