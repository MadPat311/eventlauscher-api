using System.Net.Http.Headers;
using System.Text.Json;

namespace EventLauscherApi.Services.Instagram
{
    public class InstagramGraphClient : IInstagramGraphClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InstagramGraphClient> _logger;

        public InstagramGraphClient(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<InstagramGraphClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public Task<string> GetMentionedMediaRawAsync(
            string mediaId,
            CancellationToken cancellationToken = default)
        {
            var fields =
                $"mentioned_media.media_id({mediaId})" +
                "{id,caption,media_type,media_url,permalink,timestamp,username}";

            return SendIgUserFieldsRequestAsync(fields, cancellationToken);
        }

        public Task<string> GetMentionedCommentRawAsync(
            string commentId,
            CancellationToken cancellationToken = default)
        {
            var fields =
                $"mentioned_comment.comment_id({commentId})" +
                "{id,text,timestamp,username}";

            return SendIgUserFieldsRequestAsync(fields, cancellationToken);
        }

        private async Task<string> SendIgUserFieldsRequestAsync(
            string fields,
            CancellationToken cancellationToken)
        {
            var graphApiVersion = _configuration["Instagram:GraphApiVersion"] ?? "v25.0";
            var igUserId = _configuration["Instagram:IgUserId"];
            var accessToken = _configuration["Instagram:AccessToken"];

            if (string.IsNullOrWhiteSpace(igUserId))
            {
                throw new InvalidOperationException("Instagram:IgUserId is missing.");
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Instagram:AccessToken is missing.");
            }

            var url =
                $"https://graph.facebook.com/{graphApiVersion}/{igUserId}" +
                $"?fields={Uri.EscapeDataString(fields)}";

            var client = _httpClientFactory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Instagram Graph API request failed. StatusCode: {StatusCode}, Body: {Body}",
                    response.StatusCode,
                    responseBody
                );

                throw new InvalidOperationException(
                    $"Instagram Graph API request failed with status {(int)response.StatusCode}: {responseBody}"
                );
            }

            return responseBody;
        }
    }
}