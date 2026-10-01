using HackerNews.Application.DTOs;

namespace HackerNews.Application.Abstractions;

public interface IStoryItemCache
{
    Task<StoryDto?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task SetAsync(int id, StoryDto story, CancellationToken cancellationToken = default);
}
