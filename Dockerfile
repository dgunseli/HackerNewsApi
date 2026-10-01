# ── Stage 1: restore ──────────────────────────────────────────────────────────
# Copy only project manifests first so package restore is cached as a layer.
# The layer is invalidated only when a .csproj changes, not on every source edit.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore
WORKDIR /src

COPY HackerNews.sln .
COPY src/HackerNews.Api/HackerNews.Api.csproj                                   src/HackerNews.Api/
COPY src/HackerNews.Application/HackerNews.Application.csproj                   src/HackerNews.Application/
COPY src/HackerNews.Infrastructure/HackerNews.Infrastructure.csproj             src/HackerNews.Infrastructure/
COPY tests/HackerNews.Application.Tests/HackerNews.Application.Tests.csproj     tests/HackerNews.Application.Tests/
COPY tests/HackerNews.Infrastructure.Tests/HackerNews.Infrastructure.Tests.csproj tests/HackerNews.Infrastructure.Tests/
COPY tests/HackerNews.Api.Tests/HackerNews.Api.Tests.csproj                     tests/HackerNews.Api.Tests/

RUN dotnet restore HackerNews.sln

# ── Stage 2: test ─────────────────────────────────────────────────────────────
# Build all projects and run tests before producing the final artefact.
FROM restore AS test
WORKDIR /src

COPY src/ src/
COPY tests/ tests/

RUN dotnet test tests/HackerNews.Application.Tests \
        --no-restore \
        --configuration Release \
        --logger "console;verbosity=minimal"

RUN dotnet test tests/HackerNews.Api.Tests \
        --no-restore \
        --configuration Release \
        --logger "console;verbosity=minimal"

# ── Stage 3: publish ──────────────────────────────────────────────────────────
FROM test AS publish

RUN dotnet publish src/HackerNews.Api/HackerNews.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# ── Stage 4: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Run as non-root user
RUN adduser --disabled-password --gecos "" appuser \
 && chown -R appuser /app
USER appuser

COPY --from=publish /app/publish .

# ASP.NET Core defaults to port 8080 in containers since .NET 8
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "HackerNews.Api.dll"]
