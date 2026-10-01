using HackerNews.Application.DTOs;

namespace HackerNews.Application.Abstractions;

public interface IBestStoriesService
{
    Task<IReadOnlyList<StoryDto>> GetBestStoriesAsync(int count, CancellationToken cancellationToken = default);
}
