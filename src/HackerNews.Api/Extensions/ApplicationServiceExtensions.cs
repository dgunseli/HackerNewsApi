using FluentValidation;
using HackerNews.Application.Abstractions;
using HackerNews.Application.Options;
using HackerNews.Application.Services;
using HackerNews.Application.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HackerNews.Api.Extensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StoryOptions>(
            configuration.GetSection(StoryOptions.SectionName));

        services.AddSingleton<IBestStoriesService, BestStoriesService>();
        services.AddSingleton<IValidator<int>, GetBestStoriesValidator>();

        return services;
    }
}
