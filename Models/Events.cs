using System;
using EventLauscherApi.Models; // AppUser

namespace EventLauscherApi.Models
{
    public class Event
    {
        public int Id { get; set; }

        public string Title { get; set; } = default!;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Date { get; set; }
        public string? Time { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int? MediaId { get; set; }
        public MediaFile? MediaFile { get; set; }

        public EventStatus Status { get; set; } = EventStatus.Draft;

        public Guid UploadUserId { get; set; }
        public AppUser UploadUser { get; set; } = default!;

        public Guid? ReviewedByUserId { get; set; }
        public AppUser? ReviewedBy { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }

    public enum EventStatus { Draft = 0, Published = 1, Rejected = 2 }
}
