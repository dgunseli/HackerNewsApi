using FluentValidation;
using HackerNews.Application.Options;
using Microsoft.Extensions.Options;

namespace HackerNews.Application.Validators;

public sealed class GetBestStoriesValidator : AbstractValidator<int>
{
    public GetBestStoriesValidator(IOptions<StoryOptions> options)
    {
        RuleFor(n => n)
            .GreaterThan(0).WithMessage("Count must be greater than 0.")
            .LessThanOrEqualTo(options.Value.MaxStoriesLimit)
            .WithMessage($"Count must not exceed {options.Value.MaxStoriesLimit}.");
    }
}
