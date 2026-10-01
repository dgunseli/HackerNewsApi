using FluentValidation;
using HackerNews.Application.Abstractions;
using HackerNews.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HackerNews.Api.Controllers;

[ApiController]
[Route("api/stories")]
public sealed class StoriesController : ControllerBase
{
    private readonly IBestStoriesService _service;
    private readonly IValidator<int> _validator;

    public StoriesController(IBestStoriesService service, IValidator<int> validator)
    {
        _service = service;
        _validator = validator;
    }

    /// <summary>Returns the best n Hacker News stories ordered by score descending.</summary>
    [HttpGet("best")]
    [ProducesResponseType<IReadOnlyList<StoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetBest(
        [FromQuery] int n,
        CancellationToken cancellationToken)
    {
        var validation = _validator.Validate(n);
        if (!validation.IsValid)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Invalid request.",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage))
            });
        }

        var stories = await _service.GetBestStoriesAsync(n, cancellationToken);
        return Ok(stories);
    }
}
