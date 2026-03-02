using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using EventLauscherApi.Contracts.Requests;
using System.Text.Encodings.Web;


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

            var canAutoPublish =
                User.IsInRole("Organizer") ||
                User.IsInRole("Reviewer") ||
                User.IsInRole("Admin");

            var now = DateTimeOffset.UtcNow;

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
                UploadUserId = userId,

                Status = canAutoPublish ? EventStatus.Published : EventStatus.Draft,
                PublishedAt = canAutoPublish ? now : null,

                // Optional (falls du möchtest): Organizer nicht als Reviewer setzen
                // ReviewedAt = canAutoPublish ? now : null,
                // ReviewedByUserId = canAutoPublish ? userId : null,
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

        [HttpGet("/s/e/{id:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> ShareEvent(int id, CancellationToken ct)
        {
            var e = await _context.Events.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.Status == EventStatus.Published, ct);

            if (e == null) return NotFound();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var shareUrl = $"{baseUrl}/s/e/{id}";
            var appUrl = $"{baseUrl}/events/{id}"; // Flutter Route

            // og:image: dein MediaFilesController
            var imageUrl = (e.MediaId != null)
                ? $"{baseUrl}/api/MediaFiles/{e.MediaId}"
                : $"{baseUrl}/assets/share-default.jpg"; // pack dir ein default image in web assets

            var title = HtmlEncoder.Default.Encode(e.Title ?? "Event");
            var datePart = string.IsNullOrWhiteSpace(e.Date) ? "" : e.Date.Trim();
            var timePart = string.IsNullOrWhiteSpace(e.Time) ? "" : e.Time.Trim();
            var locPart = string.IsNullOrWhiteSpace(e.Location) ? "" : e.Location.Trim();

            var descRaw = string.Join(" · ", new[]
            {
        string.Join(" ", new[] { datePart, timePart }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim(),
        locPart
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var desc = HtmlEncoder.Default.Encode(string.IsNullOrWhiteSpace(descRaw) ? "Event bei Eventlauscher" : descRaw);

            // optional: kleine Cache-Header (OG Bots cachen eh, aber ok)
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
  <meta property=""og:image:width"" content=""1200"" />
  <meta property=""og:image:height"" content=""630"" />

  <meta name=""twitter:card"" content=""summary_large_image"" />
  <meta name=""twitter:title"" content=""{title}"" />
  <meta name=""twitter:description"" content=""{desc}"" />
  <meta name=""twitter:image"" content=""{imageUrl}"" />

  <meta http-equiv=""refresh"" content=""0; url={appUrl}"" />
</head>
<body>
  <p>Weiterleitung… <a href=""{appUrl}"">Event öffnen</a></p>
</body>
</html>";

            return Content(html, "text/html; charset=utf-8");
        }
    }
}
