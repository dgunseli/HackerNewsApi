using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HackerNews.Application.Abstractions;
using HackerNews.Application.Models;
using Moq;

namespace HackerNews.Api.Tests.Integration;

public sealed class BestStoriesIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private record StoryResponse(
        string Title,
        string? Uri,
        string PostedBy,
        DateTimeOffset Time,
        int Score,
        int CommentCount
    );

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static HackerNewsItem MakeItem(int id, int score, long? unixTime = null) => new(
        Id: id,
        Title: $"Story {id}",
        Url: $"https://example.com/{id}",
        By: "author",
        Time: unixTime ?? 1570887781L,
        Score: score,
        Descendants: 10,
        Type: "story",
        Dead: false);

    private static void SetupClient(Mock<IHackerNewsClient> mock, IEnumerable<HackerNewsItem> items)
    {
        var list = items.ToList();
        mock.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(list.Select(i => i.Id).ToArray());
        foreach (var item in list)
        {
            var captured = item;
            mock.Setup(c => c.GetItemAsync(captured.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(captured);
        }
    }

    // ── Test 1: Returns 200 with exact requested count ────────────────────────

    [Fact]
    public async Task GetBest_Returns200_WithRequestedCount()
    {
        using var factory = new HackerNewsApiFactory();
        SetupClient(factory.ClientMock, Enumerable.Range(1, 20).Select(i => MakeItem(i, 20 - i)));

        var response = await factory.CreateClient().GetAsync("/api/stories/best?n=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stories = await response.Content.ReadFromJsonAsync<List<StoryResponse>>(JsonOptions);
        stories.Should().HaveCount(5);
    }

    // ── Test 2: JSON field names match the spec (camelCase) ───────────────────

    [Fact]
    public async Task GetBest_ResponseJson_MatchesSpecFieldNames()
    {
        using var factory = new HackerNewsApiFactory();
        SetupClient(factory.ClientMock, new[] { MakeItem(1, 100) });

        var response = await factory.CreateClient().GetAsync("/api/stories/best?n=1");
        var json = await response.Content.ReadAsStringAsync();
        var story = JsonDocument.Parse(json).RootElement[0];

        story.TryGetProperty("title", out _).Should().BeTrue();
        story.TryGetProperty("uri", out _).Should().BeTrue();
        story.TryGetProperty("postedBy", out _).Should().BeTrue();
        story.TryGetProperty("time", out _).Should().BeTrue();
        story.TryGetProperty("score", out _).Should().BeTrue();
        story.TryGetProperty("commentCount", out _).Should().BeTrue();
    }

    // ── Test 3: Response is sorted by score descending ────────────────────────

    [Fact]
    public async Task GetBest_Returns_ScoreDescending()
    {
        using var factory = new HackerNewsApiFactory();
        SetupClient(factory.ClientMock, new[]
        {
            MakeItem(1, score: 50),
            MakeItem(2, score: 300),
            MakeItem(3, score: 150),
        });

        var stories = await factory.CreateClient()
            .GetFromJsonAsync<List<StoryResponse>>("/api/stories/best?n=3", JsonOptions);

        stories!.Select(s => s.Score).Should().BeInDescendingOrder();
    }

    // ── Test 4: Unix timestamp serialized as correct ISO 8601 DateTimeOffset ──

    [Fact]
    public async Task GetBest_Time_MatchesUnixTimestamp()
    {
        const long unixTs = 1570887781L;
        var expected = DateTimeOffset.FromUnixTimeSeconds(unixTs);

        using var factory = new HackerNewsApiFactory();
        SetupClient(factory.ClientMock, new[] { MakeItem(1, 100, unixTime: unixTs) });

        var stories = await factory.CreateClient()
            .GetFromJsonAsync<List<StoryResponse>>("/api/stories/best?n=1", JsonOptions);

        stories![0].Time.Should().Be(expected);
    }

    // ── Test 5: n=0 returns 422 ───────────────────────────────────────────────

    [Fact]
    public async Task GetBest_WithZeroN_Returns422()
    {
        using var factory = new HackerNewsApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/stories/best?n=0");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Test 6: n=-1 returns 422 ──────────────────────────────────────────────

    [Fact]
    public async Task GetBest_WithNegativeN_Returns422()
    {
        using var factory = new HackerNewsApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/stories/best?n=-1");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Test 7: Response always includes X-Correlation-ID header ─────────────

    [Fact]
    public async Task GetBest_Response_ContainsCorrelationIdHeader()
    {
        using var factory = new HackerNewsApiFactory();
        SetupClient(factory.ClientMock, new[] { MakeItem(1, 100) });

        var response = await factory.CreateClient().GetAsync("/api/stories/best?n=1");

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.First().Should().NotBeNullOrWhiteSpace();
    }

    // ── Test 8: Caller-provided X-Correlation-ID is echoed back ──────────────

    [Fact]
    public async Task GetBest_EchoesCallerCorrelationId()
    {
        const string correlationId = "test-correlation-abc123";

        using var factory = new HackerNewsApiFactory();
        SetupClient(factory.ClientMock, new[] { MakeItem(1, 100) });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stories/best?n=1");
        request.Headers.Add("X-Correlation-ID", correlationId);

        var response = await factory.CreateClient().SendAsync(request);

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.First().Should().Be(correlationId);
    }

    // ── Test 9: Unhandled exception → 500 ProblemDetails ─────────────────────

    [Fact]
    public async Task GetBest_WhenClientThrows_Returns500ProblemDetails()
    {
        using var factory = new HackerNewsApiFactory();
        factory.ClientMock
            .Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HN API unavailable"));

        var response = await factory.CreateClient().GetAsync("/api/stories/best?n=5");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
