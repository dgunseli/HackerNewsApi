using HackerNews.Api.Extensions;
using HackerNews.Api.Middleware;
using HackerNews.Infrastructure.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration));

// Graceful shutdown — in-flight requests have up to 30s to complete
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddControllers();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Correlation ID must be first so all downstream logs carry the ID
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.MapControllers();

// /health/live — is the process alive? (no external checks)
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,   // no checks, just returns 200 if the process responds
    ResponseWriter = WriteJsonResponse
});

// /health/ready — can we serve traffic? (checks HN API + cache)
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteJsonResponse
});

// /health — everything
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteJsonResponse
});

app.Run();

static Task WriteJsonResponse(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";

    var result = new
    {
        status = report.Status.ToString(),
        duration = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration = e.Value.Duration.TotalMilliseconds
        })
    };

    return ctx.Response.WriteAsync(
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}

// Exposed for WebApplicationFactory in integration tests
public partial class Program { }
