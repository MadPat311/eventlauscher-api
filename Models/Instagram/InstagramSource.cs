namespace EventLauscherApi.Models.Instagram
{
    public class InstagramSource
    {
        public int Id { get; set; }

        // Erster Webhook, durch den diese Quelle entstanden ist
        public int WebhookInboxEntryId { get; set; }
        public WebhookInboxEntry? WebhookInboxEntry { get; set; }

        public string Provider { get; set; } = "Instagram";

        // Eindeutiger Schlüssel für Deduplizierung
        // Beispiel: media:180123456789
        public string SourceKey { get; set; } = default!;

        // Instagram-IDs
        public string? InstagramEntryId { get; set; }
        public string? MediaId { get; set; }

        // Falls die Mention über einen Kommentar kam
        public string? FirstCommentId { get; set; }
        public string? LastCommentId { get; set; }

        // MediaCaption oder Comment
        public string MentionType { get; set; } = "Unknown";

        // Wie oft wurde Eventlauscher für diesen Post erwähnt?
        public int MentionCount { get; set; } = 1;

        // PendingFetch, Fetched, Failed, Processed
        public string Status { get; set; } = "PendingFetch";

        // Rohes value-Objekt aus dem Webhook
        public string? RawValueJson { get; set; }

        // Später per Instagram API gefüllt
        public string? Username { get; set; }
        public string? CaptionOrText { get; set; }
        public string? Permalink { get; set; }
        public string? MediaType { get; set; }
        public string? MediaUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public DateTimeOffset? PostedAt { get; set; }
        public string? RawApiJson { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastMentionAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? FetchedAt { get; set; }

        public string? ErrorMessage { get; set; }
    }
}