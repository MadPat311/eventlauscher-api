namespace EventLauscherApi.Models
{
    public class Event
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Date { get; set; }
        public string? Time { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int? MediaId { get; set; }
        public MediaFile? MediaFile { get; set; } // Navigation Property
    }
}
