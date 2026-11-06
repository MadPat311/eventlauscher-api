using System.Security.Cryptography;
using System.Text;
using EventLauscherApi.Data;
using EventLauscherApi.Models.Instagram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventLauscherApi.Controllers
{
    [ApiController]
    [Route("api/webhooks/instagram")]
    [AllowAnonymous]
    public class InstagramWebhookController : ControllerBase
    {
        private readonly EventContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InstagramWebhookController> _logger;

        public InstagramWebhookController(
            EventContext context,
            IConfiguration configuration,
            ILogger<InstagramWebhookController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult VerifyWebhook()
        {
            var mode = Request.Query["hub.mode"].ToString();
            var verifyToken = Request.Query["hub.verify_token"].ToString();
            var challenge = Request.Query["hub.challenge"].ToString();

            var expectedToken = _configuration["Instagram:WebhookVerifyToken"];

            if (mode == "subscribe" &&
                !string.IsNullOrWhiteSpace(verifyToken) &&
                verifyToken == expectedToken &&
                !string.IsNullOrWhiteSpace(challenge))
            {
                _logger.LogInformation("Instagram webhook verification successful.");
                return Content(challenge, "text/plain");
            }

            _logger.LogWarning("Instagram webhook verification failed.");
            return Unauthorized();
        }

        private const int MaxPayloadBytes = 1024 * 1024;

        [HttpPost]
        [RequestSizeLimit(MaxPayloadBytes)]
        public async Task<IActionResult> ReceiveWebhook(CancellationToken cancellationToken = default)
        {
            var appSecret = _configuration["Instagram:AppSecret"];
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                _logger.LogError("Instagram webhook signing secret is not configured.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var signature = Request.Headers["X-Hub-Signature-256"].ToString();
            if (signature.Length != 71 || !signature.StartsWith("sha256=", StringComparison.Ordinal))
                return Unauthorized();

            byte[] suppliedHash;
            try { suppliedHash = Convert.FromHexString(signature[7..]); }
            catch (FormatException) { return Unauthorized(); }

            // Verify the exact request bytes, before decoding or persisting anything.
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (body.Length + read > MaxPayloadBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            var payload = body.ToArray();
            var expectedHash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), payload);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))
                return Unauthorized();

            var payloadJson = Encoding.UTF8.GetString(payload);

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return BadRequest("Empty webhook payload.");
            }

            var entry = new WebhookInboxEntry
            {
                Provider = "Instagram",
                ReceivedAt = DateTimeOffset.UtcNow,
                PayloadJson = payloadJson,
                Status = "Received"
            };

            _context.InstagramWebhookInboxEntries.Add(entry);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Instagram webhook payload stored with id {WebhookInboxEntryId}", entry.Id);

            return Ok();
        }
    }
}