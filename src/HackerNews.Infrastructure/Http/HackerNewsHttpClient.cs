using System.Net.Http.Json;
using HackerNews.Application.Abstractions;
using HackerNews.Application.Models;
using Microsoft.Extensions.Logging;

namespace HackerNews.Infrastructure.Http;

public sealed class HackerNewsHttpClient : IHackerNewsClient
{
    public const string ClientName = nameof(HackerNewsHttpClient);

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<HackerNewsHttpClient> _logger;

    public HackerNewsHttpClient(IHttpClientFactory factory, ILogger<HackerNewsHttpClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching best story IDs from HN API");

        var client = _factory.CreateClient(ClientName);
        var ids = await client.GetFromJsonAsync<int[]>("beststories.json", cancellationToken);
        return ids ?? [];
    }

    public async Task<HackerNewsItem?> GetItemAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching HN item {ItemId}", id);

        var client = _factory.CreateClient(ClientName);
        var response = await client.GetFromJsonAsync<HackerNewsItemResponse>(
            $"item/{id}.json", cancellationToken);

        if (response is null)
            return null;

        return new HackerNewsItem(
            Id: response.Id,
            Title: response.Title,
            Url: response.Url,
            By: response.By,
            Time: response.Time,
            Score: response.Score,
            Descendants: response.Descendants,
            Type: response.Type,
            Dead: response.Dead);
    }
}
