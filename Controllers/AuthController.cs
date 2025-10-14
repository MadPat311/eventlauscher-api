using System.Security.Claims;
using EventLauscherApi.Services;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;

namespace EventLauscherApi.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<AppRole> _roles;
    private readonly SignInManager<AppUser> _signIn;
    private readonly IJwtService _jwt;
    private readonly IEmailService _mail;
    private readonly IConfiguration _cfg;

    public AuthController(
        UserManager<AppUser> users,
        RoleManager<AppRole> roles,
        SignInManager<AppUser> signIn,
        IJwtService jwt,
        IEmailService mail,
        IConfiguration cfg)
    {
        _users = users;
        _roles = roles;
        _signIn = signIn;
        _jwt = jwt;
        _mail = mail;
        _cfg = cfg;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var user = new AppUser { Email = dto.Email, UserName = dto.UserName ?? dto.Email, EmailConfirmed = false };
        var res = await _users.CreateAsync(user, dto.Password);
        if (!res.Succeeded) return BadRequest(res.Errors);

        if (!await _roles.RoleExistsAsync("User"))
            await _roles.CreateAsync(new AppRole { Name = "User" });

        await _users.AddToRoleAsync(user, "User");

        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var baseUrl = _cfg["PublicApiBaseUrl"] ?? "http://localhost:23822";
        var link = $"{baseUrl}/verify?uid={user.Id}&token={Uri.EscapeDataString(token)}";

        await _mail.SendAsync(user.Email!, "Eventlauscher – E-Mail bestätigen",
            $"<p>Bitte bestätige deine E-Mail: <a href=\"{link}\">Jetzt bestätigen</a></p>");

        return Ok(new { message = "Registered. Check your email." });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email);
        if (user is null) return Unauthorized();

        var pw = await _signIn.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!pw.Succeeded) return Unauthorized();
        if (!user.EmailConfirmed) return BadRequest(new { error = "EMAIL_NOT_CONFIRMED" });

        var roles = await _users.GetRolesAsync(user);
        var (access, _) = _jwt.CreateAccessToken(user, roles);
        var refresh = await _jwt.CreateAndStoreRefreshToken(user.Id, default);

        return Ok(new
        {
            accessToken = access,
            refreshToken = refresh,
            user = new { id = user.Id, email = user.Email, userName = user.UserName, roles, emailConfirmed = user.EmailConfirmed }
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshDto dto)
    {
        var (ok, userId) = await _jwt.ValidateRefreshToken(dto.RefreshToken, default);
        if (!ok) return Unauthorized();

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return Unauthorized();

        var roles = await _users.GetRolesAsync(user);
        var (access, _) = _jwt.CreateAccessToken(user, roles);
        var newRefresh = await _jwt.CreateAndStoreRefreshToken(user.Id, default);
        await _jwt.RevokeRefreshToken(dto.RefreshToken, newRefresh, default);

        return Ok(new { accessToken = access, refreshToken = newRefresh });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshDto dto)
    {
        await _jwt.RevokeRefreshToken(dto.RefreshToken, null, default);
        return Ok();
    }

    [HttpGet("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail([FromQuery] Guid uid, [FromQuery] string token)
    {
        // Token nicht in Referrer weiterreichen (Sicherheit)
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var user = await _users.FindByIdAsync(uid.ToString());
        if (user is null)
        {
            return HtmlPage(
                title: "E-Mail-Bestätigung – Ungültiger Benutzer",
                body: "<h1>Ungültiger Benutzer</h1><p>Der Bestätigungslink ist nicht mehr gültig oder der Benutzer existiert nicht.</p>",
                statusCode: 404
            );
        }

        // Bereits bestätigte E-Mail freundlich behandeln (idempotent)
        if (user.EmailConfirmed)
        {
            var emailSafe = WebUtility.HtmlEncode(user.Email ?? "");
            return HtmlPage(
                title: "E-Mail bereits bestätigt",
                body: $"<h1>E-Mail bereits bestätigt</h1><p><strong>{emailSafe}</strong> ist bereits bestätigt.<br>Du kannst dich jetzt einloggen.</p>",
                statusCode: 200
            );
        }

        var res = await _users.ConfirmEmailAsync(user, token);
        if (res.Succeeded)
        {
            var emailSafe = WebUtility.HtmlEncode(user.Email ?? "");
            return HtmlPage(
                title: "E-Mail bestätigt",
                body: $"<h1>Alles klar!</h1><p><strong>{emailSafe}</strong> wurde bestätigt.<br>Du kannst dich jetzt einloggen.</p>",
                statusCode: 200
            );
        }

        // Fehlerfall – Identity liefert Fehlercodes/-texte
        var sb = new StringBuilder();
        sb.Append("<ul>");
        foreach (var e in res.Errors)
        {
            var msg = WebUtility.HtmlEncode(e.Description ?? e.Code);
            sb.Append($"<li>{msg}</li>");
        }
        sb.Append("</ul>");

        return HtmlPage(
            title: "E-Mail-Bestätigung fehlgeschlagen",
            body: $"<h1>Bestätigung fehlgeschlagen</h1><p>Der Bestätigungslink ist ungültig oder abgelaufen.</p>{sb}",
            statusCode: 400
        );
    }

    // API-Variante (Flutter): bleibt JSON-basiert
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
    {
        var user = await _users.FindByIdAsync(dto.UserId.ToString());
        if (user is null) return NotFound(new { error = "USER_NOT_FOUND" });

        // Idempotent: wenn schon bestätigt, direkt OK zurück
        if (user.EmailConfirmed) return Ok(new { success = true, alreadyConfirmed = true });

        var res = await _users.ConfirmEmailAsync(user, dto.Token);
        return res.Succeeded ? Ok(new { success = true }) : BadRequest(res.Errors);
    }


    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _users.FindByIdAsync(id);
        var roles = await _users.GetRolesAsync(user!);
        return Ok(new { id = user!.Id, email = user.Email, userName = user.UserName, roles, emailConfirmed = user.EmailConfirmed });
    }

    [HttpGet("reviewer/ping")]
    [Authorize(Policy = "Reviewer")]
    public IActionResult ReviewerPing() => Ok(new { ok = true });

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("pwreset")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email);
        if (user is null) return Ok(); // kein User-Leak

        var token = await _users.GeneratePasswordResetTokenAsync(user);

        var frontendBase = _cfg["Frontend:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host.Value}";
        var link = $"{frontendBase}/reset?uid={user.Id}&token={Uri.EscapeDataString(token)}";

        await _mail.SendAsync(user.Email!, "Eventlauscher – Passwort zurücksetzen",
            $"<p>Zum Zurücksetzen klicke hier: <a href=\"{link}\">Passwort zurücksetzen</a></p>");

        return Ok();
    }

    // GET: optional – damit du den Link testen kannst (ohne Frontend)
    [HttpGet("reset-password")]
    [AllowAnonymous]
    public IActionResult ResetPasswordPreview([FromQuery] Guid uid, [FromQuery] string token)
    {
        return Ok(new { info = "POST /auth/reset-password mit userId + token + newPassword aufrufen", uid, token });
    }

    // POST: tatsächliches Zurücksetzen
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var user = await _users.FindByIdAsync(dto.UserId.ToString());
        if (user is null) return NotFound();

        var res = await _users.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
        return res.Succeeded ? Ok() : BadRequest(res.Errors);
    }
    private ContentResult HtmlPage(string title, string body, int statusCode)
    {
        var html = $@"<!doctype html>
<html lang=""de"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <meta name=""robots"" content=""noindex, nofollow"">
  <title>{WebUtility.HtmlEncode(title)}</title>
  <style>
    :root {{ color-scheme: light dark; }}
    body {{
      font-family: system-ui, -apple-system, Segoe UI, Roboto, Ubuntu, Cantarell, 'Helvetica Neue', Arial, 'Noto Sans', 'Apple Color Emoji', 'Segoe UI Emoji';
      margin: 0; padding: 2rem;
      line-height: 1.5;
    }}
    main {{
      max-width: 680px; margin: 0 auto;
    }}
    h1 {{ margin-top: 0.2rem; font-size: 1.6rem; }}
    p, li {{ font-size: 1rem; }}
    ul {{ margin-top: 0.5rem; }}
    .note {{ margin-top: 1rem; opacity: .8; font-size: .9rem; }}
  </style>
</head>
<body>
  <main>
    {body}
    <p class=""note"">Du kannst dieses Fenster jetzt schließen.</p>
  </main>
</body>
</html>";
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = statusCode
        };
    }
}

public record RegisterDto(string Email, string Password, string? UserName);
public record LoginDto(string Email, string Password);
public record RefreshDto(string RefreshToken);
public record ConfirmEmailDto(Guid UserId, string Token);
public record ForgotPasswordDto(string Email);
public record ResetPasswordDto(Guid UserId, string Token, string NewPassword);

