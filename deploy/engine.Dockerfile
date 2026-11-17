# syntax=docker/dockerfile:1
# Flowgent engine — CORE PLATFORM image.
# Build context is the repository root (see deploy/README.md).

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the nuget.org-only feed config FIRST so restore is deterministic and never
# probes the authenticated user-level feed.
COPY nuget.config ./

# Central package management lives under backend/ (Directory.Build.props,
# Directory.Packages.props, backend/nuget.config). Copy the backend source tree
# (bin/obj/tests are excluded by deploy/.dockerignore) so transitive project
# references resolve, then restore + publish the target service.
COPY backend/ ./backend/

RUN dotnet restore backend/src/Services/Flowgent.Engine.Service/Flowgent.Engine.Service.csproj
RUN dotnet publish backend/src/Services/Flowgent.Engine.Service/Flowgent.Engine.Service.csproj \
    -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

LABEL org.opencontainers.image.title="Flowgent Engine" \
      org.opencontainers.image.description="Flowgent engine — domain-agnostic workflow runtime (MAF declarative) that runs YAML workflows and reaches domain tools over MCP." \
      org.opencontainers.image.source="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.url="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Flowgent"

ENTRYPOINT ["dotnet", "Flowgent.Engine.Service.dll"]
