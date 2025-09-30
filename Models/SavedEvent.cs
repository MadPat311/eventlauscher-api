using EventLauscherApi.Models;

public class SavedEvent
{
    public Guid UserId { get; set; }
    public int EventId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser User { get; set; } = default!;
    public Event Event { get; set; } = default!;
}