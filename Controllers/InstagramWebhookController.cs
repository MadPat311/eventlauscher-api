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

            _logger.LogWarning(
                "Instagram verify debug: mode='{Mode}', verifyTokenLength={VerifyTokenLength}, expectedTokenLength={ExpectedTokenLength}, verifyTokenStart='{VerifyTokenStart}', expectedTokenStart='{ExpectedTokenStart}'",
                mode,
                verifyToken?.Length,
                expectedToken?.Length,
                verifyToken?.Substring(0, Math.Min(5, verifyToken.Length)),
                expectedToken?.Substring(0, Math.Min(5, expectedToken.Length))
            );

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

        [HttpPost]
        public async Task<IActionResult> ReceiveWebhook()
        {
            using var reader = new StreamReader(Request.Body);
            var payloadJson = await reader.ReadToEndAsync();

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
            await _context.SaveChangesAsync();

            _logger.LogInformation("Instagram webhook payload stored with id {WebhookInboxEntryId}", entry.Id);

            return Ok();
        }
    }
}