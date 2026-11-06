using System.ComponentModel.DataAnnotations;

namespace EventLauscherApi.Contracts.Requests;


public sealed record CreateEventRequest
{
    [StringLength(200)]
    public string Title { get; init; } = default!;
    [StringLength(4000)]
    public string? Description { get; init; }
    [StringLength(400)]
    public string? Location { get; init; }
    [StringLength(32)]
    public string? Date { get; init; }
    [StringLength(16)]
    public string? Time { get; init; }
    [Range(-90d, 90d)]
    public double? Latitude { get; init; }
    [Range(-180d, 180d)]
    public double? Longitude { get; init; }
    public int? MediaId { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourcePlatform { get; init; }
}
