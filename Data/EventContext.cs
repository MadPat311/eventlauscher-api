using Microsoft.EntityFrameworkCore;
using EventlauscherApi.Models;

namespace EventlauscherApi.Data
{
    public class EventContext : DbContext
    {
        public EventContext(DbContextOptions<EventContext> options) : base(options) {}

        public DbSet<Event> Events { get; set; }
    }
}
