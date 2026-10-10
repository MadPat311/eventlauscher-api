using System.Text.Json;
using EventLauscherApi.Data;
using Microsoft.EntityFrameworkCore;

namespace EventLauscherApi.Services.Instagram
{
    public class InstagramSourceFetchProcessor : IInstagramSourceFetchProcessor
    {
        private readonly EventContext _context;
        private readonly IInstagramGraphClient _graphClient;
        private readonly ILogger<InstagramSourceFetchProcessor> _logger;

        public InstagramSourceFetchProcessor(
            EventContext context,
            IInstagramGraphClient graphClient,
            ILogger<InstagramSourceFetchProcessor> logger)
        {
            _context = context;
            _graphClient = graphClient;
            _logger = logger;
        }

        public async Task<int> ProcessPendingFetchAsync(CancellationToken cancellationToken = default)
        {
            var sources = await _context.InstagramSources
                .Where(x => x.Status == "PendingFetch")
                .OrderBy(x => x.CreatedAt)
                .Take(10)
                .ToListAsync(cancellationToken);

            var processedCount = 0;

            foreach (var source in sources)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(source.MediaId))
                    {
                        source.Status = "Failed";
                        source.ErrorMessage = "MediaId is missing. Cannot fetch Instagram media details.";
                        source.FetchedAt = DateTimeOffset.UtcNow;
                        processedCount++;
                        continue;
                    }

                    var mediaRawJson = await _graphClient.GetMentionedMediaRawAsync(
                        source.MediaId,
                        cancellationToken
                    );

                    string? commentRawJson = null;

                    if (!string.IsNullOrWhiteSpace(source.LastCommentId))
                    {
                        commentRawJson = await _graphClient.GetMentionedCommentRawAsync(
                            source.LastCommentId,
                            cancellationToken
                        );
                    }

                    ApplyMentionedMediaResponse(source, mediaRawJson);

                    source.RawApiJson = BuildCombinedRawJson(mediaRawJson, commentRawJson);
                    source.Status = "Fetched";
                    source.FetchedAt = DateTimeOffset.UtcNow;
                    source.ErrorMessage = null;

                    processedCount++;

                    _logger.LogInformation(
                        "Instagram source fetched. SourceId: {SourceId}, SourceKey: {SourceKey}",
                        source.Id,
                        source.SourceKey
                    );
                }
                catch (Exception ex)
                {
                    source.Status = "Failed";
                    source.ErrorMessage = ex.Message.Length > 2000
                        ? ex.Message[..2000]
                        : ex.Message;

                    source.FetchedAt = DateTimeOffset.UtcNow;

                    processedCount++;

                    _logger.LogError(
                        ex,
                        "Failed to fetch Instagram source. SourceId: {SourceId}, SourceKey: {SourceKey}",
                        source.Id,
                        source.SourceKey
                    );
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            return processedCount;
        }

        private static void ApplyMentionedMediaResponse(
            EventLauscherApi.Models.Instagram.InstagramSource source,
            string mediaRawJson)
        {
            using var document = JsonDocument.Parse(mediaRawJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("mentioned_media", out var mentionedMedia))
            {
                throw new InvalidOperationException("Instagram API response does not contain 'mentioned_media'.");
            }

            var media = NormalizeObjectOrFirstArrayItem(mentionedMedia);

            source.Username = GetStringOrNull(media, "username");
            source.CaptionOrText = GetStringOrNull(media, "caption");
            source.Permalink = GetStringOrNull(media, "permalink");
            source.MediaType = GetStringOrNull(media, "media_type");
            source.MediaUrl = GetStringOrNull(media, "media_url");
            source.ThumbnailUrl = GetStringOrNull(media, "thumbnail_url");

            var timestamp = GetStringOrNull(media, "timestamp");

            if (!string.IsNullOrWhiteSpace(timestamp) &&
                DateTimeOffset.TryParse(timestamp, out var postedAt))
            {
                source.PostedAt = postedAt;
            }
        }

        private static JsonElement NormalizeObjectOrFirstArrayItem(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                return element;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                var first = element.EnumerateArray().FirstOrDefault();

                if (first.ValueKind == JsonValueKind.Object)
                {
                    return first;
                }
            }

            throw new InvalidOperationException("Unexpected Instagram API response shape.");
        }

        private static string? GetStringOrNull(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.ToString();
        }

        private static string BuildCombinedRawJson(string mediaRawJson, string? commentRawJson)
        {
            if (string.IsNullOrWhiteSpace(commentRawJson))
            {
                return mediaRawJson;
            }

            return JsonSerializer.Serialize(new
            {
                mentioned_media_response = JsonDocument.Parse(mediaRawJson).RootElement,
                mentioned_comment_response = JsonDocument.Parse(commentRawJson).RootElement
            });
        }
    }
}