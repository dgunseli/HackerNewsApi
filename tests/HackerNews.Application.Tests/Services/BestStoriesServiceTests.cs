using FluentAssertions;
using HackerNews.Application.Abstractions;
using HackerNews.Application.DTOs;
using HackerNews.Application.Models;
using HackerNews.Application.Options;
using HackerNews.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options;
using Moq;

namespace HackerNews.Application.Tests.Services;

public sealed class BestStoriesServiceTests
{
    private readonly Mock<IHackerNewsClient> _clientMock = new();
    private readonly Mock<IStoryListCache> _listCacheMock = new();
    private readonly Mock<IStoryItemCache> _itemCacheMock = new();
    private readonly StoryOptions _options = new() { MaxStoriesLimit = 200, MaxConcurrentItemRequests = 10 };

    private BestStoriesService CreateSut() => new(
        _clientMock.Object,
        _listCacheMock.Object,
        _itemCacheMock.Object,
        MsOptions.Options.Create(_options),
        NullLogger<BestStoriesService>.Instance);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HackerNewsItem MakeItem(int id, int score, int descendants = 10) => new(
        Id: id,
        Title: $"Story {id}",
        Url: $"https://example.com/{id}",
        By: "author",
        Time: 1570887781L,
        Score: score,
        Descendants: descendants,
        Type: "story",
        Dead: false);

    private static StoryDto MakeDto(int id, int score) => new(
        Title: $"Story {id}",
        Uri: $"https://example.com/{id}",
        PostedBy: "author",
        Time: DateTimeOffset.FromUnixTimeSeconds(1570887781L),
        Score: score,
        CommentCount: 10);

    // Convenience: list cache MISS, each ID fetched from HN
    private void SetupColdCaches(IEnumerable<HackerNewsItem> items)
    {
        _listCacheMock.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>?)null);
        _listCacheMock.Setup(c => c.SetAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _itemCacheMock.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoryDto?)null);
        _itemCacheMock.Setup(c => c.SetAsync(It.IsAny<int>(), It.IsAny<StoryDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ids = items.Select(i => i.Id).ToArray();
        _clientMock.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);

        foreach (var item in items)
        {
            var captured = item;
            _clientMock.Setup(c => c.GetItemAsync(captured.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(captured);
        }
    }

    // ── Test 1: n=10 returns exactly 10 stories ───────────────────────────

    [Fact]
    public async Task GetBestStoriesAsync_Returns_ExactCount()
    {
        var ids = Enumerable.Range(1, 50).ToArray();

        _listCacheMock.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);
        _itemCacheMock.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int id, CancellationToken _) => Task.FromResult<StoryDto?>(MakeDto(id, 50 - id)));

        var result = await CreateSut().GetBestStoriesAsync(10);

        result.Should().HaveCount(10);
    }

    // ── Test 2: Result is ordered by score descending ────────────────────

    [Fact]
    public async Task GetBestStoriesAsync_Returns_ScoreDescending()
    {
        SetupColdCaches(new[]
        {
            MakeItem(1, score: 100),
            MakeItem(2, score: 300),
            MakeItem(3, score: 200),
        });

        var result = await CreateSut().GetBestStoriesAsync(3);

        result.Select(s => s.Score).Should().BeInDescendingOrder();
    }

    // ── Test 3: Unix timestamp maps to correct DateTimeOffset ────────────

    [Fact]
    public async Task GetBestStoriesAsync_MapsUnixTime_ToDateTimeOffset()
    {
        const long unixTs = 1570887781L;
        var expected = DateTimeOffset.FromUnixTimeSeconds(unixTs);

        SetupColdCaches(new[] { MakeItem(1, 100) with { Time = unixTs } });

        var result = await CreateSut().GetBestStoriesAsync(1);

        result[0].Time.Should().Be(expected);
    }

    // ── Test 4: HN descendants maps to commentCount ───────────────────────

    [Fact]
    public async Task GetBestStoriesAsync_MapsDescendants_ToCommentCount()
    {
        const int expectedComments = 42;

        SetupColdCaches(new[] { MakeItem(1, 100, descendants: expectedComments) });

        var result = await CreateSut().GetBestStoriesAsync(1);

        result[0].CommentCount.Should().Be(expectedComments);
    }

    // ── Test 5: HN returns null / dead items → filtered out gracefully ────

    [Fact]
    public async Task GetBestStoriesAsync_FiltersOut_NullAndDeadItems()
    {
        _listCacheMock.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>?)null);
        _listCacheMock.Setup(c => c.SetAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _itemCacheMock.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoryDto?)null);
        _itemCacheMock.Setup(c => c.SetAsync(It.IsAny<int>(), It.IsAny<StoryDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _clientMock.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1, 2, 3 });
        _clientMock.Setup(c => c.GetItemAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeItem(1, 100));
        _clientMock.Setup(c => c.GetItemAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HackerNewsItem?)null);
        _clientMock.Setup(c => c.GetItemAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeItem(3, 50) with { Dead = true });

        var result = await CreateSut().GetBestStoriesAsync(10);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Story 1");
    }

    // ── Test 6: Both caches hit → HN client never called ─────────────────

    [Fact]
    public async Task GetBestStoriesAsync_OnFullCacheHit_DoesNotCallClient()
    {
        var ids = new[] { 1, 2, 3 };

        _listCacheMock.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids);
        _itemCacheMock.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => MakeDto(id, 100));

        await CreateSut().GetBestStoriesAsync(3);

        _clientMock.Verify(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
        _clientMock.Verify(c => c.GetItemAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 7: Cache miss → stories fetched and both caches written ──────

    [Fact]
    public async Task GetBestStoriesAsync_OnCacheMiss_FetchesAndPopulatesBothCaches()
    {
        SetupColdCaches(new[] { MakeItem(1, 100), MakeItem(2, 200) });

        var result = await CreateSut().GetBestStoriesAsync(2);

        result.Should().HaveCount(2);
        _listCacheMock.Verify(
            c => c.SetAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _itemCacheMock.Verify(
            c => c.SetAsync(It.IsAny<int>(), It.IsAny<StoryDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ── Test 8: Concurrent requests → ID list fetched only once ──────────

    [Fact]
    public async Task GetBestStoriesAsync_ConcurrentRequests_RefreshesIdListOnlyOnce()
    {
        var idFetchCount = 0;

        // Simulates real cache: once SetAsync is called, GetAsync returns the data
        IReadOnlyList<int>? storedIds = null;
        _listCacheMock.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => storedIds);
        _listCacheMock.Setup(c => c.SetAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<int>, CancellationToken>((ids, _) => storedIds = ids)
            .Returns(Task.CompletedTask);

        _itemCacheMock.Setup(c => c.GetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoryDto?)null);
        _itemCacheMock.Setup(c => c.SetAsync(It.IsAny<int>(), It.IsAny<StoryDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _clientMock.Setup(c => c.GetBestStoryIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref idFetchCount);
                return new[] { 1 };
            });
        _clientMock.Setup(c => c.GetItemAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeItem(1, 100));

        var sut = CreateSut();
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => sut.GetBestStoriesAsync(1))
            .ToArray();

        await Task.WhenAll(tasks);

        idFetchCount.Should().Be(1);
    }
}
