namespace HackerNews.Infrastructure.Configuration;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public int RetryMaxAttempts { get; init; } = 3;
    public int RetryBaseDelaySeconds { get; init; } = 2;
    public int CircuitBreakerBreakSeconds { get; init; } = 30;
    public int CircuitBreakerSamplingSeconds { get; init; } = 60;
    public int CircuitBreakerMinThroughput { get; init; } = 5;
    public double CircuitBreakerFailureRatio { get; init; } = 0.5;
}
