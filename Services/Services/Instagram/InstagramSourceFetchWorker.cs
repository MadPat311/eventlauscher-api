namespace EventLauscherApi.Services.Instagram
{
    public class InstagramSourceFetchWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InstagramSourceFetchWorker> _logger;

        public InstagramSourceFetchWorker(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<InstagramSourceFetchWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Instagram source fetch worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var enabled = _configuration.GetValue<bool>("Instagram:EnableFetching");

                    if (enabled)
                    {
                        using var scope = _scopeFactory.CreateScope();

                        var processor = scope.ServiceProvider
                            .GetRequiredService<IInstagramSourceFetchProcessor>();

                        var processedCount = await processor.ProcessPendingFetchAsync(stoppingToken);

                        if (processedCount > 0)
                        {
                            _logger.LogInformation(
                                "Instagram source fetch worker processed {ProcessedCount} sources.",
                                processedCount
                            );
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // App fährt runter
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Instagram source fetch worker failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }

            _logger.LogInformation("Instagram source fetch worker stopped.");
        }
    }
}