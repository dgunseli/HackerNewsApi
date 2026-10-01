# Hacker News Best Stories API

A RESTful API built with ASP.NET Core (.NET 9) that returns the top N Hacker News stories ranked by score in descending order.

This project was developed with Claude AI. The primary focus of the project is the deliberate application of the patterns listed below — even in a scope where some of them might be considered over-engineering — in order to demonstrate the rationale behind each one and share real-world experience with them.

- Clean Architecture
- Factory Pattern
- Dependency Inversion Principle
- Options Pattern
- Caching (Cache-Aside, Two-Level Cache)
- Concurrency Patterns
- Resilience Patterns
- Middlewares & Validations
- Readiness / Liveness / Graceful Shutdown

Each section below explains why the pattern was used, how it is applied in this project, and relevant experience from past work.

---

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- or [Docker](https://www.docker.com/)

### Running with dotnet

```bash
dotnet run --project src/HackerNews.Api --launch-profile http
```

The application starts at `http://localhost:5211`.

### Running with Docker

```bash
docker-compose up
```

The application starts at `http://localhost:8080`.

> All tests are executed during the Docker build stage. If any test fails, the image will not be produced.

---

## API Usage

### Get the Best N Stories

```
GET /api/stories/best?n={n}
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `n` | `int` | Number of stories to return (1–200) |

#### Success Response — 200 OK

```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

Results are ordered by `score` descending.

#### Error Responses

| Status | Description |
|--------|-------------|
| `422 Unprocessable Entity` | `n ≤ 0` or `n > MaxStoriesLimit` |
| `500 Internal Server Error` | Unexpected error (ProblemDetails format) |

#### Example Requests

```bash
# Top 10 stories
curl "http://localhost:5211/api/stories/best?n=10"

# With a correlation ID
curl -H "X-Correlation-ID: my-request-id" "http://localhost:5211/api/stories/best?n=5"
```

---

## Architectural Decisions

### Clean Architecture

Clean Architecture is the architectural approach I have used across all my projects in recent years. After working with 3-tier and then N-tier design patterns, I transitioned to Clean Architecture and have applied it consistently ever since. Its strengths in testability, dependency management, and enabling teams to work in parallel on separate layers are the main reasons I reach for it on most projects.

### Factory Pattern

The Factory Pattern is an object creation and lifecycle management approach. I have worked with various creational patterns — Singleton, Factory, Builder, and Prototype — across many different projects. In this project, the core use of the Factory Pattern is through `IHttpClientFactory` for managing `HttpClient` instances. This provides both object lifecycle control and connection pooling. Internally, it manages `HttpMessageHandler` lifetimes and reuse, which protects against socket exhaustion and ensures connection efficiency without requiring the developer to manually manage client disposal.

### Dependency Inversion Principle

As one of the SOLID principles, Dependency Inversion states that high-level modules should not depend on low-level modules; both should depend on abstractions (interfaces or abstract classes). This means that when an implementation changes, the higher-level code remains completely unaffected. In practice, this makes the system significantly more flexible for technical changes. For example, the in-memory cache used here could be replaced with Redis by simply providing a new implementation — no changes to the service layer or any other consumer. In my past experience, as projects grew larger, having this kind of abstraction in place allowed infrastructure changes to be carried out much faster and with far less risk.

### Options Pattern

One of the recurring challenges in growing projects is managing the configuration values that govern how the system behaves. The Options Pattern in Clean Architecture defines how these configuration values are delivered to lower layers as strongly-typed objects, rather than raw string lookups. This also naturally pairs with runtime overriding of configuration — whether through environment-specific settings, Kubernetes ConfigMaps and Secrets, or centralised configuration tools such as HashiCorp Vault.

### Caching

Caching is an indispensable part of any production system. The speed at which data can be accessed — depending on the storage layer involved (RAM, disk, service, database, or storage) — largely determines the baseline performance of a service. Beyond speed, access frequency matters equally: frequent reads against a target system can create IO pressure on databases or push networks toward transfer limits. Over the course of more than a hundred service integrations and data-access workflows, I have worked with a wide range of caching mechanisms. A key concern in caching is always data freshness and correctness, alongside safe concurrent access and updates. In this project, a two-level caching structure was designed to address these concerns, and a Thundering Herd prevention mechanism was implemented using `SemaphoreSlim` to serialise cache refresh operations under high concurrency.

### Resilience

Resilience defines how a system behaves when something goes wrong in one of the external systems it depends on. Any system that integrates with an external service must have a predictable and controlled response to failures in that service — both to protect its own health and to behave correctly for its users. In this project, a basic resilience pipeline was built using Retry with exponential backoff and a Circuit Breaker. Resilience concerns can also be addressed at the infrastructure or platform level rather than purely within the application. I have implemented resilience strategies at both the application code level and within Kubernetes environments, and I consider it a non-negotiable requirement for any service that communicates with an external dependency.

### Middlewares & Validations

Many software frameworks provide a middleware mechanism that allows developers to manage the request processing pipeline. Middlewares execute on every request in a defined order and are well-suited for cross-cutting concerns such as authentication, authorisation, logging, and error handling. I have designed and used custom middlewares for various purposes in nearly every project I have worked on.

Input validation is equally important: validating requests at the boundary — before they reach the service layer — ensures consistent behaviour and provides clear, actionable feedback to callers when something is malformed or out of range. In this project, FluentValidation is used for both schema and business rule validation.

### Readiness / Liveness / Graceful Shutdown

Managing the startup and shutdown lifecycle of an application correctly is critical to the health of the overall system. In environments such as Kubernetes, exposing whether a service is alive and whether it is ready to handle traffic is essential for the orchestrator to route requests only to instances that are genuinely capable of serving them. Similarly, graceful shutdown — ensuring that in-flight requests are completed before the process exits — is critical for operational correctness.

In this project, three health endpoints are exposed:

| Endpoint | Purpose | Use Case |
|----------|---------|----------|
| `/health/live` | Is the process alive? | Kubernetes liveness probe |
| `/health/ready` | Can the service handle traffic? | Kubernetes readiness probe |
| `/health` | Full system status | General monitoring |

The readiness check reports three states based on actual system condition:
- **Healthy** — cache is warm, serving entirely from memory
- **Degraded** — cache is cold but the HN API is reachable; first request will be slower
- **Unhealthy** — cache is cold and the HN API is unreachable; requests will fail

Graceful shutdown is supported through ASP.NET Core's built-in shutdown handling. The `CancellationToken` is propagated throughout the entire request chain — through the service layer, semaphore waits, and HTTP calls — so that in-flight operations are cleanly cancelled when a shutdown signal is received. A 30-second shutdown timeout is configured to give in-flight requests time to complete before the process is forcibly terminated.

---

## Running the Tests

```bash
# Unit + Integration tests
dotnet test --filter "Category!=LoadTest"

# Load tests (each scenario runs for ~30 seconds)
dotnet test --filter "Category=LoadTest"

# All tests
dotnet test
```

### Load Test Results

Tests were run against an in-memory application host (`WebApplicationFactory`) with simulated HN API latencies.

| Scenario | Load | Total Requests | Success | p50 | p95 | p99 | HN API Calls |
|----------|------|---------------|---------|-----|-----|-----|--------------|
| Warm cache sustained | 1,000 TPS × 30s | 30,000 | 100% | 0.08 ms | 0.32 ms | 1.15 ms | **0** |
| Ramp-up 100→500→1,000 TPS | 10s × 3 stages | 10,500 | 100% | 0.11 ms | 0.46 ms | 1.13 ms | **0** |
| Thundering herd (cold cache) | 200 concurrent | 200 | 100% | — | — | — | **1** (200× suppression) |

Once the cache was warm, 1,000 requests per second were served entirely from memory without a single call to the HN API.

---

## Configuration

All parameters are read from `appsettings.json` and can be overridden via environment variables.

```json
{
  "Stories": {
    "MaxStoriesLimit": 200,
    "MaxConcurrentItemRequests": 10
  },
  "Cache": {
    "ListAbsoluteExpirationSeconds": 300,
    "ItemAbsoluteExpirationSeconds": 1800
  },
  "HackerNews": {
    "BaseUrl": "https://hacker-news.firebaseio.com/v0/",
    "TimeoutSeconds": 30
  },
  "Resilience": {
    "RetryMaxAttempts": 3,
    "RetryBaseDelaySeconds": 2,
    "CircuitBreakerBreakSeconds": 30,
    "CircuitBreakerSamplingSeconds": 60,
    "CircuitBreakerMinThroughput": 5,
    "CircuitBreakerFailureRatio": 0.5
  }
}
```

Environment variable override example (Docker):

```bash
docker run -e Cache__ListAbsoluteExpirationSeconds=60 -p 8080:8080 hackernews-api
```

---

## Areas for Improvement

- **Distributed cache**: This project uses `IMemoryCache` as a proof-of-concept caching layer. More advanced systems would benefit from distributed caching solutions such as Redis, and potentially event-driven cache invalidation mechanisms.

- **Authentication & authorisation**: The project has no authentication or authorisation layer. In a production-grade product, these are security concerns that should be planned from the outset — security by design. Rate limiting is also absent and should be considered.

- **Observability**: Basic structured logging is in place via Serilog. A production system would benefit from a centralised log aggregation solution such as the ELK stack. Distributed tracing via OpenTelemetry with Jaeger or Zipkin is also something I would prioritise — having implemented such integrations before, I consider them essential for diagnosing issues in production environments.

- **Sentinel values for invalid items**: Items that are `null`, `dead`, or of incorrect type are currently not written to the item cache. As a result, `TryGetAllFromCacheAsync` will always return a cache miss for those IDs, causing the `_itemBatchRefreshLock` to be acquired unnecessarily on every request. The fix is to write a short-TTL sentinel entry for invalid items so the cache layer can distinguish "not yet fetched" from "fetched but invalid".

- **Stale-while-revalidate**: Rather than blocking on cache refresh when the TTL expires, the system could serve the stale cached value immediately while triggering a background refresh. This eliminates cold-start latency spikes entirely.

- **Health check depth**: The current health check endpoints reflect real system state (cache warmth and HN API reachability). In a real production system, the health check strategy — including what constitutes liveness vs readiness, and what thresholds trigger degraded vs unhealthy — should be defined collaboratively with the platform and operations teams.
