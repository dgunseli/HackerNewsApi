using HackerNews.Application.Abstractions;
using HackerNews.Infrastructure.Caching;
using HackerNews.Infrastructure.Configuration;
using HackerNews.Infrastructure.Health;
using HackerNews.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace HackerNews.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HackerNewsClientOptions>(
            configuration.GetSection(HackerNewsClientOptions.SectionName));
        services.Configure<CacheOptions>(
            configuration.GetSection(CacheOptions.SectionName));
        services.Configure<ResilienceOptions>(
            configuration.GetSection(ResilienceOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<IStoryListCache, MemoryStoryListCache>();
        services.AddSingleton<IStoryItemCache, MemoryStoryItemCache>();

        services.AddHttpClient(HackerNewsHttpClient.ClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<HackerNewsClientOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            })
            .AddResilienceHandler("hn-pipeline", (builder, context) =>
            {
                var opts = context.ServiceProvider
                    .GetRequiredService<IOptions<ResilienceOptions>>().Value;

                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = opts.RetryMaxAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(opts.RetryBaseDelaySeconds),
                    UseJitter = true
                });

                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    BreakDuration = TimeSpan.FromSeconds(opts.CircuitBreakerBreakSeconds),
                    SamplingDuration = TimeSpan.FromSeconds(opts.CircuitBreakerSamplingSeconds),
                    MinimumThroughput = opts.CircuitBreakerMinThroughput,
                    FailureRatio = opts.CircuitBreakerFailureRatio
                });
            });

        services.AddSingleton<IHackerNewsClient, HackerNewsHttpClient>();

        services.AddHealthChecks()
            .AddCheck<HackerNewsHealthCheck>(
                name: "hackernews-api",
                tags: new[] { "ready" });

        return services;
    }
}
