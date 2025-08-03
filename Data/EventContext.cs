using Microsoft.EntityFrameworkCore;
using EventlauscherApi.Models;

namespace EventlauscherApi.Data
{
    public class EventContext : DbContext
    {
        public EventContext(DbContextOptions<EventContext> options) : base(options) { }

        public DbSet<Event> Events { get; set; }
        public DbSet<MediaFile> MediaFiles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Event>()
                .HasOne(e => e.MediaFile)
                .WithMany(m => m.Events)
                .HasForeignKey(e => e.MediaId)
                .OnDelete(DeleteBehavior.Cascade); // Wenn MediaFile gelöscht wird, werden alle zugehörigen Events gelöscht

            base.OnModelCreating(modelBuilder); // WICHTIG! Nicht vergessen
        }

    }
}
