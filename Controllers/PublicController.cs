using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EventLauscherApi.Data;
using EventLauscherApi.Models;

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
}