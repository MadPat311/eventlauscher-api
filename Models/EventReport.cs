using EventLauscherApi.Models;

public class EventReport
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event Event { get; set; } = default!;

        public Guid? ReporterUserId { get; set; }
        public AppUser? ReporterUser { get; set; }

        public string Reason { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }