namespace EventLauscherApi.Services.Instagram
{
    public interface IInstagramWebhookInboxProcessor
    {
        Task<int> ProcessReceivedAsync(CancellationToken cancellationToken = default);
    }
}