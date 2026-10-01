namespace HackerNews.Application.DTOs;

public sealed record StoryDto(
    string Title,
    string? Uri,
    string PostedBy,
    DateTimeOffset Time,
    int Score,
    int CommentCount
);
