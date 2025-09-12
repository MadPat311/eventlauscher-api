using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EventLauscherApi.Data;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Authorization;

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

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Event>>> GetEvents()
        {
            return await _context.Events.ToListAsync();
        }

        [Authorize]
        [HttpPost]
        public async Task<ActionResult<Event>> CreateEvent(Event e)
        {
            _context.Events.Add(e);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetEvents), new { id = e.Id }, e);
        }
    }
}
