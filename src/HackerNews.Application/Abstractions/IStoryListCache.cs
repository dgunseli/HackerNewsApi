namespace HackerNews.Application.Abstractions;

public interface IStoryListCache
{
    Task<IReadOnlyList<int>?> GetAsync(CancellationToken cancellationToken = default);
    Task SetAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
}
