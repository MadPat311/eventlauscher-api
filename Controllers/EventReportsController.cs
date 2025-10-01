using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventLauscherApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventReportsController : ControllerBase
    {
        private readonly EventContext _db;

        public EventReportsController(EventContext db) => _db = db;

        public record CreateEventReportDto(int EventId, string Reason);
        public record EventReportDto(int Id, int EventId, string Reason, DateTimeOffset CreatedAt, Guid? ReporterUserId);

        private static EventReportDto ToDto(EventReport r) =>
            new(r.Id, r.EventId, r.Reason, r.CreatedAt, r.ReporterUserId);

        // POST api/eventreports
        [HttpPost]
        // [Authorize] // einschalten, wenn nur eingeloggte melden sollen
        public async Task<ActionResult<EventReportDto>> Create([FromBody] CreateEventReportDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return BadRequest("Reason required.");

            var evExists = await _db.Events.AnyAsync(e => e.Id == dto.EventId, ct);
            if (!evExists) return NotFound("Event not found.");

            // Optional: User aus Claims (AppUser.Id ist Guid)
            Guid? reporterId = null;
            if (User?.Identity?.IsAuthenticated == true)
            {
                var sub = User.FindFirst("sub")?.Value ?? User.FindFirst("uid")?.Value;
                if (Guid.TryParse(sub, out var g)) reporterId = g;
            }

            var report = new EventReport
            {
                EventId = dto.EventId,
                ReporterUserId = reporterId,
                Reason = dto.Reason.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.EventReports.Add(report);
            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(report));
        }

        // GET api/eventreports  (Reviewer sicht)
        [HttpGet]
        [Authorize(Roles = "Reviewer")]
        public async Task<ActionResult<EventReportDto[]>> List(CancellationToken ct)
        {
            var list = await _db.EventReports
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => ToDto(r))
                .ToArrayAsync(ct);

            return Ok(list);
        }

        // DELETE api/eventreports/{id}
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Reviewer")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var r = await _db.EventReports.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r == null) return NotFound();

            _db.EventReports.Remove(r);
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
