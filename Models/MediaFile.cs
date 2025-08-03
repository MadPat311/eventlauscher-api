using EventlauscherApi.Models;

public class MediaFile
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string? Hash { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public int UploadUserId { get; set; }
    public string SourcePlattform { get; set; } = string.Empty;
    public string SourceURL { get; set; } = string.Empty;

     public ICollection<Event> Events { get; set; }
}
