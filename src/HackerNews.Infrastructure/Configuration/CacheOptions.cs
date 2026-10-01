namespace HackerNews.Infrastructure.Configuration;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Best story ID list TTL — rankings change frequently.</summary>
    public int ListAbsoluteExpirationSeconds { get; init; } = 300;    // 5 min

    /// <summary>Individual story detail TTL — content changes rarely.</summary>
    public int ItemAbsoluteExpirationSeconds { get; init; } = 1800;   // 30 min
}
