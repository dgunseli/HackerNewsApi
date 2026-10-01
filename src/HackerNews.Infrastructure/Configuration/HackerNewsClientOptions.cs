namespace HackerNews.Infrastructure.Configuration;

public sealed class HackerNewsClientOptions
{
    public const string SectionName = "HackerNews";

    public string BaseUrl { get; init; } = "https://hacker-news.firebaseio.com/v0/";
    public int TimeoutSeconds { get; init; } = 30;
}
