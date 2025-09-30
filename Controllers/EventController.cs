using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using EventLauscherApi.Contracts.Requests;


namespace EventLauscherApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly EventContext _context;

        public EventsController(EventContext context)
        {
            _context = context;
        }

        // Öffentlich: nur veröffentlichte Events
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<IEnumerable<Event>>> GetEvents()
        {
            var list = await _context.Events
                .Where(e => e.Status == EventStatus.Published)
                .OrderBy(e => e.Date) // optional
                .ToListAsync();

            return list;
        }

        // Öffentlich: einzelnes Event (nur wenn veröffentlicht)
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<Event>> GetEvent(int id)
        {
            var e = await _context.Events.FindAsync(id);
            if (e == null || e.Status != EventStatus.Published)
                return NotFound();
            return e;
        }

        // Erstellen: immer als Draft; UploadUserId aus JWT, Client-Werte gehärtet übernehmen
        [Authorize]
        [HttpPost]
        public async Task<ActionResult<Event>> CreateEvent([FromBody] CreateEventRequest req)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!Guid.TryParse(userIdStr, out var userId)) return Forbid();
            if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title ist erforderlich.");

            var e = new Event
            {
                Title = req.Title.Trim(),
                Description = req.Description,
                Location = req.Location,
                Date = req.Date,
                Time = req.Time,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                MediaId = req.MediaId,
                Status = EventStatus.Draft,
                UploadUserId = userId
            };

            _context.Events.Add(e);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetEvent), new { id = e.Id }, e);
        }

        [Authorize]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyUploads(CancellationToken ct)
        {
            var uid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var items = await _context.Events
                .Where(e => e.UploadUserId == uid)
                .OrderByDescending(e => e.PublishedAt ?? e.ReviewedAt ?? (DateTimeOffset?)null)
                .ThenByDescending(e => e.Id)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Description,
                    e.Location,
                    e.Date,
                    e.Time,
                    e.Status,
                    e.ReviewedAt,
                    e.PublishedAt,
                    e.MediaId
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        // Eigene gespeicherte Events (nur Published anzeigen)
        [Authorize]
        [HttpGet("saved")]
        public async Task<IActionResult> GetSavedEvents(CancellationToken ct)
        {
            var uid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var q = from s in _context.SavedEvents
                    join e in _context.Events on s.EventId equals e.Id
                    where s.UserId == uid && e.Status == EventStatus.Published
                    orderby s.CreatedAt descending
                    select new { e.Id, e.Title, e.Description, e.Location, e.Date, e.Time, e.MediaId };

            var list = await q.ToListAsync(ct);
            return Ok(list);
        }

        // Event speichern (idempotent)
        [Authorize]
        [HttpPost("{id:int}/save")]
        public async Task<IActionResult> SaveEvent(int id, CancellationToken ct)
        {
            var uid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Nur veröffentlichte Events können gespeichert werden
            var exists = await _context.Events
                .AnyAsync(e => e.Id == id && e.Status == EventStatus.Published, ct);
            if (!exists) return NotFound();

            var entity = await _context.SavedEvents.FindAsync(new object?[] { uid, id }, ct);
            if (entity == null)
            {
                _context.SavedEvents.Add(new SavedEvent { UserId = uid, EventId = id });
                await _context.SaveChangesAsync(ct);
            }
            // idempotent: wenn schon vorhanden -> trotzdem 204
            return NoContent();
        }

        // Event aus gespeicherten entfernen (idempotent)
        [Authorize]
        [HttpDelete("{id:int}/save")]
        public async Task<IActionResult> UnsaveEvent(int id, CancellationToken ct)
        {
            var uid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var entity = await _context.SavedEvents.FindAsync(new object?[] { uid, id }, ct);
            if (entity != null)
            {
                _context.SavedEvents.Remove(entity);
                await _context.SaveChangesAsync(ct);
            }
            return NoContent();
        }

        // Optional: Prüfen, ob ein Event gespeichert ist
        [Authorize]
        [HttpGet("{id:int}/saved")]
        public async Task<IActionResult> IsSaved(int id, CancellationToken ct)
        {
            var uid = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var saved = await _context.SavedEvents.AnyAsync(s => s.UserId == uid && s.EventId == id, ct);
            return Ok(new { saved });
        }

        
        [Authorize] // Upload erfordert Login -> für den Dup-Check reicht Auth
        [HttpGet("dup-corpus")]
        public async Task<IActionResult> GetDupCorpus(CancellationToken ct)
        {
            var items = await _context.Events
                .AsNoTracking()
                .OrderByDescending(e => e.Id)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.Description,
                    e.Location,
                    e.Date,
                    e.Time,
                    e.Latitude,
                    e.Longitude,
                    e.MediaId,
                    e.Status // falls du’s im UI nutzen willst
                })
                .ToListAsync(ct);

            return Ok(items);
        }
    }
}
