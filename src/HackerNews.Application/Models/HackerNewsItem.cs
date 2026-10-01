namespace HackerNews.Application.Models;

public sealed record HackerNewsItem(
    int Id,
    string? Title,
    string? Url,
    string? By,
    long Time,
    int Score,
    int Descendants,
    string? Type,
    bool Dead
);
