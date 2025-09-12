namespace EventLauscherApi.Models;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Revoked { get; set; }
    public string? ReplacedByToken { get; set; }
}

public class ReviewerLink
{
    public Guid ParentReviewerId { get; set; }
    public Guid ChildReviewerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
