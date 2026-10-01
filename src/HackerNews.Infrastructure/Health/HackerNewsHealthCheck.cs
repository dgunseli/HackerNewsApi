using HackerNews.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HackerNews.Infrastructure.Health;

public sealed class HackerNewsHealthCheck : IHealthCheck
{
    private readonly IStoryListCache _listCache;
    private readonly IHackerNewsClient _client;

    public HackerNewsHealthCheck(IStoryListCache listCache, IHackerNewsClient client)
    {
        _listCache = listCache;
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Cache sıcaksa hafızadan servis edebiliyoruz — kesinlikle sağlıklı
        var cached = await _listCache.GetAsync(cancellationToken);
        if (cached is not null)
            return HealthCheckResult.Healthy(
                $"Cache warm — {cached.Count} story IDs cached, serving from memory.");

        // Cache soğuk — HN API'ye ulaşabiliyor muyuz?
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var ids = await _client.GetBestStoryIdsAsync(timeout.Token);

            return ids.Count > 0
                ? HealthCheckResult.Degraded(
                    $"Cache cold — HN API reachable ({ids.Count} IDs). First request will be slower.")
                : HealthCheckResult.Degraded(
                    "Cache cold — HN API returned empty story list.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Cache cold and HN API unreachable. Requests will fail.", ex);
        }
    }
}
