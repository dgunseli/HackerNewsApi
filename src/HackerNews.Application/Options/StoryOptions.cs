namespace HackerNews.Application.Options;

public sealed class StoryOptions
{
    public const string SectionName = "Stories";

    public int MaxStoriesLimit { get; init; } = 200;
    public int MaxConcurrentItemRequests { get; init; } = 10;
}
