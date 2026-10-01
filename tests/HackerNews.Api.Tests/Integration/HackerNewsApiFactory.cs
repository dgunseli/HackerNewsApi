using HackerNews.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace HackerNews.Api.Tests.Integration;

/// <summary>
/// Spins up the full application in-memory with a mocked IHackerNewsClient.
/// Create a new instance per test to guarantee clean cache state.
/// </summary>
public sealed class HackerNewsApiFactory : WebApplicationFactory<Program>
{
    public Mock<IHackerNewsClient> ClientMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Replace the real HN HTTP client with our mock
            services.RemoveAll<IHackerNewsClient>();
            services.AddSingleton(ClientMock.Object);
        });
    }
}
