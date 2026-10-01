using HackerNews.Application.Abstractions;
using HackerNews.Application.DTOs;
using HackerNews.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure.Caching;

public sealed class MemoryStoryItemCache : IStoryItemCache
{
    private readonly IMemoryCache _cache;
    private readonly CacheOptions _options;

    public MemoryStoryItemCache(IMemoryCache cache, IOptions<CacheOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public Task<StoryDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue<StoryDto>(ItemKey(id), out var story);
        return Task.FromResult(story);
    }

    public Task SetAsync(int id, StoryDto story, CancellationToken cancellationToken = default)
    {
        var entryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.ItemAbsoluteExpirationSeconds));

        _cache.Set(ItemKey(id), story, entryOptions);
        return Task.CompletedTask;
    }

    private static string ItemKey(int id) => $"hn_story_{id}";
}
