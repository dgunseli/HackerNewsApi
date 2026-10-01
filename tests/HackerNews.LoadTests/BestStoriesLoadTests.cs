using System.Net;
using FluentAssertions;
using HackerNews.Application.Abstractions;
using HackerNews.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using ReportFmt = NBomber.Contracts.Stats.ReportFormat;
using Xunit.Abstractions;

namespace HackerNews.LoadTests;

public sealed class BestStoriesLoadTests
{
    private readonly ITestOutputHelper _output;

    public BestStoriesLoadTests(ITestOutputHelper output) => _output = output;

    // ── Counting mock — simulates realistic HN API latency ───────────────────

    private sealed class CountingHackerNewsClient : IHackerNewsClient
    {
        private int _idCallCount;
        private int _itemCallCount;

        public int IdCallCount   => Volatile.Read(ref _idCallCount);
        public int ItemCallCount => Volatile.Read(ref _itemCallCount);
        public int TotalHnCalls  => IdCallCount + ItemCallCount;

        public async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _idCallCount);
            await Task.Delay(50, ct);    // ~50 ms — typical HN beststories.json latency
            return Enumerable.Range(1, 20).ToArray();
        }

        public async Task<HackerNewsItem?> GetItemAsync(int id, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _itemCallCount);
            await Task.Delay(20, ct);    // ~20 ms — typical HN item/{id}.json latency
            return new HackerNewsItem(
                Id: id, Title: $"Story {id}", Url: $"https://example.com/{id}",
                By: "author", Time: 1570887781L, Score: 100 + id,
                Descendants: 10, Type: "story", Dead: false);
        }
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    private sealed class LoadTestFactory : WebApplicationFactory<Program>
    {
        private readonly IHackerNewsClient _client;
        public LoadTestFactory(IHackerNewsClient client) => _client = client;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHackerNewsClient>();
                services.AddSingleton(_client);
            });
        }
    }

    // ── Metric printer ────────────────────────────────────────────────────────

    private void PrintLoadMetrics(
        string title,
        NodeStats stats,
        CountingHackerNewsClient mock,
        int hnIdBaseline,
        int hnItemBaseline)
    {
        var sc           = stats.ScenarioStats[0];
        var totalReq     = sc.Ok.Request.Count + sc.Fail.Request.Count;
        var hnIdDelta    = mock.IdCallCount   - hnIdBaseline;
        var hnItemDelta  = mock.ItemCallCount - hnItemBaseline;
        var hnTotalDelta = hnIdDelta + hnItemDelta;
        var hitRate      = totalReq > 0
            ? (totalReq - hnIdDelta) / (double)totalReq * 100.0
            : 0.0;

        var lines = new[]
        {
            "",
            $"╔══════════════════════════════════════════════════╗",
            $"║  {title,-48}║",
            $"╠══════════════════════════════════════════════════╣",
            $"║  THROUGHPUT                                      ║",
            $"║  Achieved TPS          : {sc.Ok.Request.RPS,8:F1} req/s             ║",
            $"║  Total requests        : {totalReq,8:N0}                    ║",
            $"║  Successful (2xx)      : {sc.Ok.Request.Count,8:N0}                    ║",
            $"║  Failed                : {sc.Fail.Request.Count,8:N0}                    ║",
            $"╠══════════════════════════════════════════════════╣",
            $"║  LATENCY (ms)                                    ║",
            $"║  P50 (median)          : {sc.Ok.Latency.Percent50,8:F1}                    ║",
            $"║  P95                   : {sc.Ok.Latency.Percent95,8:F1}                    ║",
            $"║  P99                   : {sc.Ok.Latency.Percent99,8:F1}                    ║",
            $"╠══════════════════════════════════════════════════╣",
            $"║  HN API CALLS (during load)                      ║",
            $"║  ID list fetches       : {hnIdDelta,8:N0}                    ║",
            $"║  Item fetches          : {hnItemDelta,8:N0}                    ║",
            $"║  Total HN calls        : {hnTotalDelta,8:N0}                    ║",
            $"║  Cache hit rate        : {hitRate,8:F2} %                  ║",
            $"╚══════════════════════════════════════════════════╝",
            "",
        };

        foreach (var line in lines)
        {
            _output.WriteLine(line);           // xunit test output (visible on failure or verbose)
            Console.Error.WriteLine(line);     // stderr — bypasses xunit capture, always visible
        }
    }

    // ── Scenario 1: 1000 TPS sustained load with warm cache ──────────────────

    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task SustainedLoad_1000TPS_WarmCache_ZeroHnCalls()
    {
        var mock = new CountingHackerNewsClient();
        using var factory = new LoadTestFactory(mock);
        var client = factory.CreateClient();

        // Populate both cache levels before measuring
        (await client.GetAsync("/api/stories/best?n=10"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var idBaseline   = mock.IdCallCount;
        var itemBaseline = mock.ItemCallCount;

        // 1 000 RPS × 30 s ≈ 30 000 requests
        var scenario = Scenario.WithoutWarmUp(
            Scenario.WithLoadSimulations(
                Scenario.Create("warm_cache_1000tps", async (IScenarioContext _) =>
                {
                    var response = await client.GetAsync("/api/stories/best?n=10");
                    return response.IsSuccessStatusCode
                        ? Response.Ok(statusCode: response.StatusCode.ToString())
                        : Response.Fail(statusCode: response.StatusCode.ToString());
                }),
                new[] { Simulation.Inject(rate: 1000, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)) }
            )
        );

        var reportDir = Path.Combine(Path.GetTempPath(), "nbomber-hackernews", "warm-1000tps");
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder(reportDir)
            .WithReportFormats(ReportFmt.Txt, ReportFmt.Html)
            .Run();

        foreach (var rf in stats.ReportFiles)
            _output.WriteLine($"Report: {rf.FilePath}");

        PrintLoadMetrics("1 000 TPS — Warm Cache (30 s)", stats, mock, idBaseline, itemBaseline);

        var sc = stats.ScenarioStats[0];
        sc.Fail.Request.Count.Should().Be(0,
            "no requests should fail under cached load");
        sc.Ok.Request.RPS.Should().BeGreaterThan(500,
            "warm-cache serving should achieve at least 500 TPS");
        (mock.IdCallCount   - idBaseline).Should().Be(0,
            "ID list TTL (300 s) >> test duration (30 s)");
        (mock.ItemCallCount - itemBaseline).Should().Be(0,
            "item TTL (1800 s) >> test duration (30 s)");
    }

    // ── Scenario 2: Ramp-up 100 → 500 → 1000 TPS ────────────────────────────

    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task RampUp_100To1000TPS_WarmCache_NoErrors()
    {
        var mock = new CountingHackerNewsClient();
        using var factory = new LoadTestFactory(mock);
        var client = factory.CreateClient();

        (await client.GetAsync("/api/stories/best?n=10"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var idBaseline   = mock.IdCallCount;
        var itemBaseline = mock.ItemCallCount;

        var scenario = Scenario.WithoutWarmUp(
            Scenario.WithLoadSimulations(
                Scenario.Create("ramp_up_1000tps", async (IScenarioContext _) =>
                {
                    var response = await client.GetAsync("/api/stories/best?n=10");
                    return response.IsSuccessStatusCode
                        ? Response.Ok(statusCode: response.StatusCode.ToString())
                        : Response.Fail(statusCode: response.StatusCode.ToString());
                }),
                new[]
                {
                    Simulation.RampingInject(rate: 100,  interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
                    Simulation.RampingInject(rate: 500,  interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
                    Simulation.RampingInject(rate: 1000, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
                }
            )
        );

        var reportDir2 = Path.Combine(Path.GetTempPath(), "nbomber-hackernews", "ramp-1000tps");
        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder(reportDir2)
            .WithReportFormats(ReportFmt.Txt, ReportFmt.Html)
            .Run();

        foreach (var rf in stats.ReportFiles)
            _output.WriteLine($"Report: {rf.FilePath}");

        PrintLoadMetrics("Ramp-up 100 → 500 → 1000 TPS (10 s each)", stats, mock, idBaseline, itemBaseline);

        var sc = stats.ScenarioStats[0];
        sc.Fail.Request.Count.Should().Be(0, "ramp-up should produce zero failures");
        (mock.IdCallCount - idBaseline).Should().Be(0, "cache valid throughout ramp-up");
    }

    // ── Scenario 3: Thundering herd — cold cache, 200 concurrent ─────────────

    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task ColdCache_200Concurrent_HnCalledExactlyOnce()
    {
        const int concurrentRequests = 200;

        var mock = new CountingHackerNewsClient();
        using var factory = new LoadTestFactory(mock);
        var client = factory.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, concurrentRequests)
                .Select(_ => client.GetAsync("/api/stories/best?n=10"))
        );

        var successCount     = responses.Count(r => r.IsSuccessStatusCode);
        var suppressionRatio = concurrentRequests / (double)Math.Max(mock.IdCallCount, 1);

        _output.WriteLine("");
        _output.WriteLine($"╔══════════════════════════════════════════════════╗");
        _output.WriteLine($"║  Thundering Herd — Cold Cache ({concurrentRequests} concurrent)   ║");
        _output.WriteLine($"╠══════════════════════════════════════════════════╣");
        _output.WriteLine($"║  REQUESTS                                        ║");
        _output.WriteLine($"║  Concurrent requests   : {concurrentRequests,8:N0}                    ║");
        _output.WriteLine($"║  Successful (2xx)      : {successCount,8:N0}                    ║");
        _output.WriteLine($"╠══════════════════════════════════════════════════╣");
        _output.WriteLine($"║  HN API CALLS                                    ║");
        _output.WriteLine($"║  ID list fetches       : {mock.IdCallCount,8:N0}  (expected: 1)       ║");
        _output.WriteLine($"║  Item fetches          : {mock.ItemCallCount,8:N0}  (expected: ≤ 20)    ║");
        _output.WriteLine($"║  Total HN calls        : {mock.TotalHnCalls,8:N0}                    ║");
        _output.WriteLine($"╠══════════════════════════════════════════════════╣");
        _output.WriteLine($"║  THUNDERING HERD SUPPRESSION                     ║");
        _output.WriteLine($"║  Requests / HN ID call : {suppressionRatio,8:F0}×  (higher = better) ║");
        _output.WriteLine($"╚══════════════════════════════════════════════════╝");
        _output.WriteLine("");

        responses.Should().OnlyContain(r => r.IsSuccessStatusCode,
            "all 200 concurrent cold-cache requests must succeed");

        mock.IdCallCount.Should().Be(1,
            "_idRefreshLock (SemaphoreSlim(1,1)) allows exactly one ID list refresh");

        mock.ItemCallCount.Should().BeLessThan(21,
            "_itemBatchRefreshLock serialises item batch — 20 items fetched at most once each");

        suppressionRatio.Should().BeGreaterThan(100,
            $"200 concurrent requests must be suppressed to 1 HN call ({suppressionRatio:F0}× ratio)");
    }
}
