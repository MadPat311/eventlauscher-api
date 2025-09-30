namespace EventLauscherApi.Contracts.Requests;


public sealed record CreateEventRequest
{
    public string Title { get; init; } = default!;
    public string? Description { get; init; }
    public string? Location { get; init; }
    public string? Date { get; init; }
    public string? Time { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int? MediaId { get; init; }
}
