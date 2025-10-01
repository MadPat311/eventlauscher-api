using System;
using EventLauscherApi.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
// Optional für Concurrency via xmin (Npgsql):
// using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace EventLauscherApi.Data
{
    /// <summary>
    /// Haupt-DbContext (Identity + Domänen-Entities)
    /// </summary>
    public class EventContext : IdentityDbContext<AppUser, AppRole, Guid>
    {
        public EventContext(DbContextOptions<EventContext> options) : base(options) { }

        // Domänen-Tabellen
        public DbSet<Event> Events { get; set; } = default!;
        public DbSet<MediaFile> MediaFiles { get; set; } = default!;
        public DbSet<SavedEvent> SavedEvents { get; set; } = default!;
        public DbSet<EventReport> EventReports { get; set; } = default!;

        // Auth/Support-Tabellen
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<ReviewerLink> ReviewerLinks => Set<ReviewerLink>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- Event -------------------------------------------------------
            var e = modelBuilder.Entity<Event>();

            e.HasKey(x => x.Id);

            // Strings begrenzen (bessere Indizes/Validierung)
            e.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            e.Property(x => x.Description)
                .HasMaxLength(4000);

            e.Property(x => x.Location)
                .HasMaxLength(400);

            // Aktuell als string -> begrenzen
            // (Wenn du später auf DateOnly/TimeOnly wechselst, siehe Kommentar unten)
            e.Property(x => x.Date).HasMaxLength(32);
            e.Property(x => x.Time).HasMaxLength(16);

            // Zahlen-Typ explizit (optional – Postgres mappt double von selbst)
            e.Property(x => x.Latitude);
            e.Property(x => x.Longitude);

            // Status-Default
            e.Property(x => x.Status)
                .HasDefaultValue(EventStatus.Draft);

            // Beziehungen
            e.HasOne(x => x.MediaFile)
                .WithMany(m => m.Events)
                .HasForeignKey(x => x.MediaId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.UploadUser)
                .WithMany()
                .HasForeignKey(x => x.UploadUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ReviewedBy)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // sinnvolle Indizes (Feed, "Meine Uploads", Reviewer-Queries)
            e.HasIndex(x => new { x.Status, x.Date });
            e.HasIndex(x => x.UploadUserId);
            e.HasIndex(x => x.ReviewedByUserId);
            e.HasIndex(x => x.MediaId);

            // --- SavedEvent (Bookmarks) --------------------------------------
            var se = modelBuilder.Entity<SavedEvent>();

            // PK verhindert Duplikate (User speichert Event nur einmal)
            se.HasKey(x => new { x.UserId, x.EventId });

            se.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            se.HasOne(x => x.Event)
                .WithMany()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // Liste „Meine gespeicherten Events“ performant (neueste zuerst)
#if NET8_0_OR_GREATER
            se.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true);
#else
            se.HasIndex(x => new { x.UserId, x.CreatedAt });
#endif
            // Popularität, Rückwärtslookups
            se.HasIndex(x => x.EventId);

            // DB-Default für CreatedAt (falls Server setzt):
            se.Property(x => x.CreatedAt)
              .HasColumnType("timestamp with time zone")
              .HasDefaultValueSql("now()");

            // --- RefreshToken -------------------------------------------------
            modelBuilder.Entity<RefreshToken>(rt =>
            {
                rt.HasKey(x => x.Id);
                rt.HasIndex(x => x.Token).IsUnique();
                rt.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            // --- ReviewerLink -------------------------------------------------
            modelBuilder.Entity<ReviewerLink>(rl =>
            {
                rl.HasKey(x => new { x.ParentReviewerId, x.ChildReviewerId });

                rl.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(x => x.ParentReviewerId)
                  .OnDelete(DeleteBehavior.Restrict);

                rl.HasOne<AppUser>()
                  .WithMany()
                  .HasForeignKey(x => x.ChildReviewerId)
                  .OnDelete(DeleteBehavior.Restrict);

                // Optional: no self-links
                // rl.ToTable(t => t.HasCheckConstraint("CK_ReviewerLink_NoSelf",
                //    "\"ParentReviewerId\" <> \"ChildReviewerId\""));
            });

            // Optional & nice: Optimistic Concurrency via xmin (Npgsql)
            // modelBuilder.UseXminAsConcurrencyToken();

            // --- Hinweis für später (falls du umstellen willst) --------------
            // Wenn du Date/Time als echte Typen speichern willst:
            // e.Property(x => x.Date).HasColumnType("date");
            // e.Property(x => x.Time).HasColumnType("time without time zone");
            // Entity-Felder dazu: DateOnly? / TimeOnly?
            // --- EventReport -------------------------------------------------------
            var er = modelBuilder.Entity<EventReport>();

            er.ToTable("event_reports");
            er.HasKey(x => x.Id);

            er.Property(x => x.Reason)
              .IsRequired()
              .HasMaxLength(2000);

            er.Property(x => x.CreatedAt)
              .HasColumnType("timestamp with time zone")
              .HasDefaultValueSql("now()");

            er.HasOne(x => x.Event)
              .WithMany()
              .HasForeignKey(x => x.EventId)
              .OnDelete(DeleteBehavior.Cascade);

            er.HasOne(x => x.ReporterUser)
              .WithMany()
              .HasForeignKey(x => x.ReporterUserId)
              .OnDelete(DeleteBehavior.SetNull);

            er.HasIndex(x => x.EventId);
            er.HasIndex(x => x.ReporterUserId);
            er.HasIndex(x => x.CreatedAt);
        }
    }
}
