using HackerNews.Application.Models;

namespace HackerNews.Application.Abstractions;

public interface IHackerNewsClient
{
    Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default);
    Task<HackerNewsItem?> GetItemAsync(int id, CancellationToken cancellationToken = default);
}
