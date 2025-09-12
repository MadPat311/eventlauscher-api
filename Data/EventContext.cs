using EventLauscherApi.Models;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventLauscherApi.Data
{
    // <-- früher: DbContext; jetzt: IdentityDbContext<AppUser, AppRole, Guid>
    public class EventContext : IdentityDbContext<AppUser, AppRole, Guid>
    {
        public EventContext(DbContextOptions<EventContext> options) : base(options) { }

        // Deine bestehenden Tabellen
        public DbSet<Event> Events { get; set; }
        public DbSet<MediaFile> MediaFiles { get; set; }

        // Neue Auth-Tabellen
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<ReviewerLink> ReviewerLinks => Set<ReviewerLink>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder); // wichtig: Identity-Mappings

            // Deine bestehende Beziehung
            modelBuilder.Entity<Event>()
                .HasOne(e => e.MediaFile)
                .WithMany(m => m.Events)
                .HasForeignKey(e => e.MediaId)
                .OnDelete(DeleteBehavior.Cascade);

            // RefreshToken Mappings
            modelBuilder.Entity<RefreshToken>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.Token).IsUnique();
                e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
            });

            // ReviewerLink Mappings
            modelBuilder.Entity<ReviewerLink>(e =>
            {
                e.HasKey(x => new { x.ParentReviewerId, x.ChildReviewerId });
                e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ParentReviewerId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ChildReviewerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
