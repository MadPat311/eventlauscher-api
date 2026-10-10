using Microsoft.Extensions.DependencyInjection;

namespace EventLauscherApi.Services.Instagram
{
    public class InstagramWebhookInboxWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InstagramWebhookInboxWorker> _logger;

        public InstagramWebhookInboxWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<InstagramWebhookInboxWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Instagram webhook inbox worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var processor = scope.ServiceProvider
                        .GetRequiredService<IInstagramWebhookInboxProcessor>();

                    var processedCount = await processor.ProcessReceivedAsync(stoppingToken);

                    if (processedCount > 0)
                    {
                        _logger.LogInformation(
                            "Instagram webhook inbox worker processed {ProcessedCount} entries.",
                            processedCount
                        );
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // App fährt runter
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Instagram webhook inbox worker failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }

            _logger.LogInformation("Instagram webhook inbox worker stopped.");
        }
    }
}