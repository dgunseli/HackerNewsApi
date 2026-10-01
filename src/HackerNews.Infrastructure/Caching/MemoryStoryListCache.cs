using HackerNews.Application.Abstractions;
using HackerNews.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure.Caching;

public sealed class MemoryStoryListCache : IStoryListCache
{
    private const string CacheKey = "hn_best_story_ids";

    private readonly IMemoryCache _cache;
    private readonly CacheOptions _options;

    public MemoryStoryListCache(IMemoryCache cache, IOptions<CacheOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public Task<IReadOnlyList<int>?> GetAsync(CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue<IReadOnlyList<int>>(CacheKey, out var ids);
        return Task.FromResult(ids);
    }

    public Task SetAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default)
    {
        var entryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.ListAbsoluteExpirationSeconds));

        _cache.Set(CacheKey, ids, entryOptions);
        return Task.CompletedTask;
    }
}
