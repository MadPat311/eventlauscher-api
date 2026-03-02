using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using System.Text.Encodings.Web;

namespace EventLauscherApi.Controllers;

[ApiController]
[Route("api/public")]
public class PublicController : ControllerBase
{
    private readonly EventContext _context;
    private readonly UserManager<AppUser> _userManager;

    public PublicController(EventContext context, UserManager<AppUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private async Task<AppUser?> FindByUsernameStrict(string username, CancellationToken ct)
    {
        var norm = _userManager.NormalizeName(username);
        return await _userManager.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedUserName == norm, ct);
    }

    [HttpGet("users/{username}/favorites")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicFavorites(string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("username fehlt.");

        var user = await FindByUsernameStrict(username, ct);
        if (user == null) return NotFound();

        var list = await (
            from s in _context.SavedEvents.AsNoTracking()
            join e in _context.Events.AsNoTracking() on s.EventId equals e.Id
            where s.UserId == user.Id && e.Status == EventStatus.Published
            orderby s.CreatedAt descending
            select new
            {
                e.Id,
                e.Title,
                e.Description,
                e.Location,
                e.Date,
                e.Time,
                e.MediaId
            }
        ).ToListAsync(ct);

        return Ok(list);
    }

    [HttpGet("users/{username}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicUserHeader(string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("username fehlt.");

        var user = await FindByUsernameStrict(username, ct);
        if (user == null) return NotFound();

        var favoritesCount = await (
            from s in _context.SavedEvents.AsNoTracking()
            join e in _context.Events.AsNoTracking() on s.EventId equals e.Id
            where s.UserId == user.Id && e.Status == EventStatus.Published
            select s
        ).CountAsync(ct);

        var uploadsCount = await _context.Events.AsNoTracking()
            .CountAsync(e => e.UploadUserId == user.Id && e.Status == EventStatus.Published, ct);

        return Ok(new
        {
            username = user.UserName,
            favoritesCount,
            uploadsCount,
        });
    }

    [HttpGet("/s/u/{username}")]
    [AllowAnonymous]
    public async Task<IActionResult> ShareUserFavorites(string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username)) return NotFound();
        var user = await FindByUsernameStrict(username.Trim(), ct);
        if (user == null) return NotFound();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var safeUser = Uri.EscapeDataString(user.UserName!);

        var shareUrl = $"{baseUrl}/s/u/{safeUser}";
        var appUrl = $"{baseUrl}/u/{safeUser}";

        var title = HtmlEncoder.Default.Encode($"{user.UserName}`s Events");
        var desc  = HtmlEncoder.Default.Encode("Eventliste bei Eventlauscher.");
        var imageUrl = $"{baseUrl}/assets/share-default.jpg";

        Response.Headers["Cache-Control"] = "public,max-age=300";

        var html = $@"
<!doctype html>
<html lang=""de"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>{title} – Eventlauscher</title>

  <link rel=""canonical"" href=""{shareUrl}"" />

  <meta property=""og:site_name"" content=""Eventlauscher"" />
  <meta property=""og:type"" content=""website"" />
  <meta property=""og:title"" content=""{title}"" />
  <meta property=""og:description"" content=""{desc}"" />
  <meta property=""og:url"" content=""{shareUrl}"" />
  <meta property=""og:image"" content=""{imageUrl}"" />

  <meta name=""twitter:card"" content=""summary_large_image"" />
  <meta name=""twitter:title"" content=""{title}"" />
  <meta name=""twitter:description"" content=""{desc}"" />
  <meta name=""twitter:image"" content=""{imageUrl}"" />

  <meta http-equiv=""refresh"" content=""0; url={appUrl}"" />
</head>
<body>
  <p>Weiterleitung… <a href=""{appUrl}"">Favoriten öffnen</a></p>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
    }
}