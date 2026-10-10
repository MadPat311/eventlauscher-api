namespace EventLauscherApi.Services.Instagram
{
    public interface IInstagramGraphClient
    {
        Task<string> GetMentionedMediaRawAsync(
            string mediaId,
            CancellationToken cancellationToken = default);

        Task<string> GetMentionedCommentRawAsync(
            string commentId,
            CancellationToken cancellationToken = default);
    }
}