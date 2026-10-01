using HackerNews.Application.Abstractions;
using HackerNews.Application.DTOs;
using HackerNews.Application.Models;
using HackerNews.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HackerNews.Application.Services;

public sealed class BestStoriesService : IBestStoriesService, IDisposable
{
    private readonly IHackerNewsClient _client;
    private readonly IStoryListCache _listCache;
    private readonly IStoryItemCache _itemCache;
    private readonly ILogger<BestStoriesService> _logger;
    private readonly StoryOptions _options;

    // Lock 1: prevents concurrent refreshes of the best-story ID list
    private readonly SemaphoreSlim _idRefreshLock = new(1, 1);

    // Lock 2: prevents concurrent item batch fetches when item cache is cold.
    // NOTE: invalid/dead items are never written to item cache (no sentinel value),
    // so TryGetAllFromCacheAsync will always miss for those IDs and this lock
    // will be acquired on every request containing dead stories.
    // Fix: write a short-TTL sentinel for invalid items (deferred enhancement).
    private readonly SemaphoreSlim _itemBatchRefreshLock = new(1, 1);

    public BestStoriesService(
        IHackerNewsClient client,
        IStoryListCache listCache,
        IStoryItemCache itemCache,
        IOptions<StoryOptions> options,
        ILogger<BestStoriesService> logger)
    {
        _client = client;
        _listCache = listCache;
        _itemCache = itemCache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StoryDto>> GetBestStoriesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var ids = await GetOrRefreshIdsAsync(cancellationToken);
        var stories = await GetOrRefreshItemsAsync(ids, cancellationToken);
        return stories.Take(count).ToList();
    }

    // ── ID list layer ──────────────────────────────────────────────────────

    private async Task<IReadOnlyList<int>> GetOrRefreshIdsAsync(CancellationToken cancellationToken)
    {
        var cached = await _listCache.GetAsync(cancellationToken);
        if (cached is not null)
            return cached;

        await _idRefreshLock.WaitAsync(cancellationToken);
        try
        {
            cached = await _listCache.GetAsync(cancellationToken);
            if (cached is not null)
                return cached;

            _logger.LogInformation("Story ID list cache miss — fetching from HN API");
            var ids = await _client.GetBestStoryIdsAsync(cancellationToken);
            await _listCache.SetAsync(ids, cancellationToken);
            _logger.LogInformation("Cached {Count} story IDs", ids.Count);
            return ids;
        }
        finally
        {
            _idRefreshLock.Release();
        }
    }

    // ── Item layer ─────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<StoryDto>> GetOrRefreshItemsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
    {
        // Fast path: all items already cached → sort and return with no lock
        var fromCache = await TryGetAllFromCacheAsync(ids, cancellationToken);
        if (fromCache is not null)
        {
            _logger.LogDebug("Full item cache hit for {Count} IDs", ids.Count);
            return fromCache.OrderByDescending(s => s.Score).ToList();
        }

        // Slow path: at least one item cold — serialise item batch fetch
        await _itemBatchRefreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check: another request may have already completed the batch
            fromCache = await TryGetAllFromCacheAsync(ids, cancellationToken);
            if (fromCache is not null)
            {
                _logger.LogDebug("Item cache populated by concurrent request");
                return fromCache.OrderByDescending(s => s.Score).ToList();
            }

            _logger.LogInformation("Item cache cold — fetching from HN API");
            return await FetchAndCacheItemsAsync(ids, cancellationToken);
        }
        finally
        {
            _itemBatchRefreshLock.Release();
        }
    }

    // Returns all items from cache if every ID is present; null if any is missing.
    private async Task<List<StoryDto>?> TryGetAllFromCacheAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
    {
        var results = new List<StoryDto>(ids.Count);
        foreach (var id in ids)
        {
            var item = await _itemCache.GetAsync(id, cancellationToken);
            if (item is null)
                return null;
            results.Add(item);
        }
        return results;
    }

    // Fetches every item from HN (item cache checked first per ID),
    // caches valid results, and returns them sorted.
    private async Task<IReadOnlyList<StoryDto>> FetchAndCacheItemsAsync(
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
    {
        using var fetchSemaphore = new SemaphoreSlim(_options.MaxConcurrentItemRequests);

        var tasks = ids.Select(async id =>
        {
            var cached = await _itemCache.GetAsync(id, cancellationToken);
            if (cached is not null)
                return cached;

            await fetchSemaphore.WaitAsync(cancellationToken);
            try
            {
                cached = await _itemCache.GetAsync(id, cancellationToken);
                if (cached is not null)
                    return cached;

                var item = await _client.GetItemAsync(id, cancellationToken);
                if (item is null || item.Dead || item.Type != "story" || item.Title is null)
                    return null;

                var dto = MapToDto(item);
                await _itemCache.SetAsync(id, dto, cancellationToken);
                return dto;
            }
            finally
            {
                fetchSemaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        var stories = results
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderByDescending(s => s.Score)
            .ToList();

        _logger.LogInformation("Item batch complete — {Valid} valid, {Total} total IDs",
            stories.Count, ids.Count);

        return stories;
    }

    private static StoryDto MapToDto(HackerNewsItem item) => new(
        Title: item.Title!,
        Uri: item.Url,
        PostedBy: item.By ?? string.Empty,
        Time: DateTimeOffset.FromUnixTimeSeconds(item.Time),
        Score: item.Score,
        CommentCount: item.Descendants
    );

    public void Dispose()
    {
        _idRefreshLock.Dispose();
        _itemBatchRefreshLock.Dispose();
    }
}
