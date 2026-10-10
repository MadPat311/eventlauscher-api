using System.Text.Json;
using EventLauscherApi.Data;
using EventLauscherApi.Models.Instagram;
using Microsoft.EntityFrameworkCore;

namespace EventLauscherApi.Services.Instagram
{
    public class InstagramWebhookInboxProcessor : IInstagramWebhookInboxProcessor
    {
        private readonly EventContext _context;
        private readonly ILogger<InstagramWebhookInboxProcessor> _logger;

        public InstagramWebhookInboxProcessor(
            EventContext context,
            ILogger<InstagramWebhookInboxProcessor> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> ProcessReceivedAsync(CancellationToken cancellationToken = default)
        {
            var entries = await _context.InstagramWebhookInboxEntries
                .Where(x => x.Status == "Received")
                .OrderBy(x => x.ReceivedAt)
                .Take(20)
                .ToListAsync(cancellationToken);

            var processedCount = 0;

            foreach (var entry in entries)
            {
                try
                {
                    var mentions = ExtractMentions(entry.PayloadJson);

                    if (mentions.Count == 0)
                    {
                        entry.Status = "Ignored";
                        entry.ProcessedAt = DateTimeOffset.UtcNow;
                        processedCount++;
                        continue;
                    }

                    foreach (var mention in mentions)
                    {
                        var existingSource = await _context.InstagramSources
                            .FirstOrDefaultAsync(
                                x => x.SourceKey == mention.SourceKey,
                                cancellationToken
                            );

                        if (existingSource is not null)
                        {
                            existingSource.MentionCount += 1;
                            existingSource.LastMentionAt = DateTimeOffset.UtcNow;

                            if (!string.IsNullOrWhiteSpace(mention.CommentId))
                            {
                                existingSource.LastCommentId = mention.CommentId;
                            }

                            existingSource.RawValueJson = mention.RawValueJson;

                            _logger.LogInformation(
                                "Instagram source already exists. SourceKey: {SourceKey}, MentionCount: {MentionCount}",
                                existingSource.SourceKey,
                                existingSource.MentionCount
                            );

                            continue;
                        }

                        var source = new InstagramSource
                        {
                            WebhookInboxEntryId = entry.Id,
                            Provider = "Instagram",
                            InstagramEntryId = mention.InstagramEntryId,
                            SourceKey = mention.SourceKey,
                            MediaId = mention.MediaId,
                            FirstCommentId = mention.CommentId,
                            LastCommentId = mention.CommentId,
                            MentionType = mention.MentionType,
                            MentionCount = 1,
                            Status = "PendingFetch",
                            RawValueJson = mention.RawValueJson,
                            CreatedAt = DateTimeOffset.UtcNow,
                            LastMentionAt = DateTimeOffset.UtcNow
                        };

                        _context.InstagramSources.Add(source);

                        _logger.LogInformation(
                            "Instagram source created. SourceKey: {SourceKey}, MediaId: {MediaId}, CommentId: {CommentId}",
                            source.SourceKey,
                            source.MediaId,
                            source.LastCommentId
                        );
                    }

                    entry.Status = "MentionDetected";
                    entry.ProcessedAt = DateTimeOffset.UtcNow;
                    processedCount++;
                }
                catch (Exception ex)
                {
                    entry.Status = "Failed";
                    entry.ErrorMessage = ex.Message.Length > 2000
                        ? ex.Message[..2000]
                        : ex.Message;

                    entry.ProcessedAt = DateTimeOffset.UtcNow;

                    _logger.LogError(
                        ex,
                        "Failed to process Instagram webhook inbox entry. InboxEntryId: {InboxEntryId}",
                        entry.Id
                    );
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            return processedCount;
        }

        private static List<MentionInfo> ExtractMentions(string payloadJson)
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            var result = new List<MentionInfo>();

            if (!root.TryGetProperty("entry", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                string? instagramEntryId = null;

                if (entry.TryGetProperty("id", out var entryIdElement))
                {
                    instagramEntryId = entryIdElement.GetString();
                }

                if (!entry.TryGetProperty("changes", out var changes) ||
                    changes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("field", out var fieldElement))
                    {
                        continue;
                    }

                    var field = fieldElement.GetString();

                    if (!string.Equals(field, "mentions", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? mediaId = null;
                    string? commentId = null;
                    string? rawValueJson = null;

                    if (change.TryGetProperty("value", out var value) &&
                        value.ValueKind == JsonValueKind.Object)
                    {
                        rawValueJson = value.GetRawText();

                        if (value.TryGetProperty("media_id", out var mediaIdElement))
                        {
                            mediaId = mediaIdElement.GetString();
                        }

                        if (value.TryGetProperty("comment_id", out var commentIdElement))
                        {
                            commentId = commentIdElement.GetString();
                        }
                    }

                    var mentionType = !string.IsNullOrWhiteSpace(commentId)
                        ? "Comment"
                        : !string.IsNullOrWhiteSpace(mediaId)
                            ? "MediaCaption"
                            : "Unknown";

                    var sourceKey = BuildSourceKey(mediaId, commentId);

                    result.Add(new MentionInfo(
                        InstagramEntryId: instagramEntryId,
                        MediaId: mediaId,
                        CommentId: commentId,
                        SourceKey: sourceKey,
                        MentionType: mentionType,
                        RawValueJson: rawValueJson
                    ));
                }
            }

            return result;
        }

        private static string BuildSourceKey(string? mediaId, string? commentId)
        {
            if (!string.IsNullOrWhiteSpace(mediaId))
            {
                return $"media:{mediaId}";
            }

            if (!string.IsNullOrWhiteSpace(commentId))
            {
                return $"comment:{commentId}";
            }

            return $"unknown:{Guid.NewGuid()}";
        }

        private sealed record MentionInfo(
            string? InstagramEntryId,
            string? MediaId,
            string? CommentId,
            string SourceKey,
            string MentionType,
            string? RawValueJson);
    }
}