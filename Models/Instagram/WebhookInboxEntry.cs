namespace EventLauscherApi.Models.Instagram
{
    public class WebhookInboxEntry
    {
        public int Id { get; set; }

        public string Provider { get; set; } = "Instagram";

        public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

        public string PayloadJson { get; set; } = default!;

        public string Status { get; set; } = "Received";

        public string? ErrorMessage { get; set; }

        public DateTimeOffset? ProcessedAt { get; set; }
    }
}