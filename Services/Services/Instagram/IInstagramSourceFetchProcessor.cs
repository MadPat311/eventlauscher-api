namespace EventLauscherApi.Services.Instagram
{
    public interface IInstagramSourceFetchProcessor
    {
        Task<int> ProcessPendingFetchAsync(CancellationToken cancellationToken = default);
    }
}