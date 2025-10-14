using EventLauscherApi.Data;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Authorize(Roles = "Reviewer,Admin")]
[ApiController]
[Route("api/reviewer/events")]
public class ReviewerEventsController : ControllerBase
{
    private readonly EventContext _db;
    private readonly UserManager<AppUser> _userManager;

    public ReviewerEventsController(EventContext db, UserManager<AppUser> um)
    {
        _db = db; _userManager = um;
    }

    // Liste unveröffentlichter Events
    [HttpGet("unpublished")]
    public async Task<IActionResult> GetUnpublished(CancellationToken ct)
    {
        var data = await _db.Events
            .Where(e => e.Status == EventStatus.Draft)
            .OrderBy(e => e.Date)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Description,
                e.Location,
                e.Date,
                e.Time,
                e.MediaId,
                UploaderEmail = e.UploadUser.Email
            })
            .ToListAsync(ct);

        return Ok(data);
    }

    // Detail für Reviewer: Event + Uploader + Reviewer-Meta
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id, CancellationToken ct)
    {
        var e = await _db.Events
            .Include(x => x.UploadUser)
            .Include(x => x.ReviewedBy)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (e is null) return NotFound();

        return Ok(new
        {
            Event = new
            {
                e.Id,
                e.Title,
                e.Description,
                e.Location,
                e.Date,
                e.Time,
                e.Latitude,
                e.Longitude,
                e.Status,
                e.MediaId
            },
            Uploader = new { e.UploadUserId, Email = e.UploadUser.Email },
            Reviewer = e.ReviewedByUserId == null ? null : new
            {
                e.ReviewedByUserId,
                Email = e.ReviewedBy!.Email,
                e.ReviewedAt,
                e.PublishedAt
            }
        });
    }

    // Felder aktualisieren (gleich wie dein Submit), Review-Meta setzen
    public class ReviewUpdate
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Date { get; set; }
        public string? Time { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    [HttpPut("{id:int}/review")]
    public async Task<IActionResult> UpdateForReview(int id, [FromBody] ReviewUpdate req, CancellationToken ct)
    {
        var e = await _db.Events.FindAsync(new object[] { id }, ct);
        if (e is null) return NotFound();

        e.Title = req.Title ?? e.Title;
        e.Description = req.Description ?? e.Description;
        e.Location = req.Location ?? e.Location;
        e.Date = req.Date ?? e.Date;
        e.Time = req.Time ?? e.Time;
        e.Latitude = req.Latitude ?? e.Latitude;
        e.Longitude = req.Longitude ?? e.Longitude;

        var reviewerId = Guid.Parse(_userManager.GetUserId(User)!);
        e.ReviewedByUserId ??= reviewerId;
        e.ReviewedAt ??= DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Veröffentlichen
    [HttpPost("{id:int}/publish")]
    public async Task<IActionResult> Publish(int id, CancellationToken ct)
    {
        var e = await _db.Events.FindAsync(new object[] { id }, ct);
        if (e is null) return NotFound();

        if (string.IsNullOrWhiteSpace(e.Title))
            return BadRequest("Title muss gesetzt sein.");

        e.Status = EventStatus.Published;
        e.PublishedAt = DateTimeOffset.UtcNow;

        var reviewerId = Guid.Parse(_userManager.GetUserId(User)!);
        e.ReviewedByUserId ??= reviewerId;
        e.ReviewedAt ??= DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
    /// <summary>
    /// Setzt ein veröffentlichtes Event zurück auf Draft (unveröffentlicht).
    /// </summary>
    /// <returns>204 NoContent bei Erfolg</returns>
    [HttpPost("{id:int}/unpublish")]
    public async Task<IActionResult> Unpublish(int id, CancellationToken ct)
    {
        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound();

        if (ev.Status != EventStatus.Published)
            return Conflict($"Event {id} ist nicht veröffentlicht.");

        var reviewerId = Guid.Parse(_userManager.GetUserId(User)!);

        // Zurücksetzen
        ev.Status = EventStatus.Draft;
        ev.PublishedAt = null;


        // Optional: aktueller Reviewer & Zeitstempel dokumentieren
        ev.ReviewedAt = DateTimeOffset.UtcNow;
        ev.ReviewedByUserId = reviewerId;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Uploader sperren (Identity Lockout)
    [HttpPost("{id:int}/ban-uploader")]
    public async Task<IActionResult> BanUploader(int id, CancellationToken ct)
    {
        var e = await _db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();

        var user = await _userManager.FindByIdAsync(e.UploadUserId.ToString());
        if (user is null) return NotFound("Uploader nicht gefunden.");

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue); // effektiv unbefristet

        return NoContent();
    }

    // Optional: Entsperren
    [HttpPost("users/{userId:guid}/unban")]
    public async Task<IActionResult> Unban(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();

        await _userManager.SetLockoutEndDateAsync(user, null);
        return NoContent();
    }


    [Authorize(Policy = "Reviewer")]
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> RejectEvent(int id)
    {
        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == id);
        if (ev is null) return NotFound(new { error = "EVENT_NOT_FOUND" });

        var reviewerId = Guid.Parse(_userManager.GetUserId(User)!);

        // Wenn du ein Enum nutzt: ev.Status = EventStatus.Rejected;
        ev.Status = EventStatus.Rejected;
        ev.ReviewedAt = DateTime.UtcNow;            // nur falls vorhanden
        ev.ReviewedByUserId =  reviewerId;         // falls du so ein Feld hast

        await _db.SaveChangesAsync();
        return Ok(new { ok = true, id = ev.Id, status = ev.Status });
    }
}
