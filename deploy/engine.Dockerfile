# syntax=docker/dockerfile:1
# Flowgent engine — CORE PLATFORM image.
# Build context is the repository root (see deploy/README.md).

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the nuget.org-only feed config FIRST so restore is deterministic and never
# probes the authenticated user-level feed.
COPY nuget.config ./

# Central package management lives in backend/ (Directory.Build.props,
# Directory.Packages.props). Keep the same backend/ layout inside the image and copy
# it with the source trees the engine needs (bin/obj/tests are excluded by the
# Dockerfile-specific .dockerignore) so transitive project references resolve, then
# restore + publish the target service.
COPY backend/Directory.Build.props backend/Directory.Packages.props ./backend/
COPY backend/shared/ ./backend/shared/
COPY backend/engine/ ./backend/engine/

RUN dotnet restore backend/engine/Flowgent.Engine.Service/Flowgent.Engine.Service.csproj
RUN dotnet publish backend/engine/Flowgent.Engine.Service/Flowgent.Engine.Service.csproj \
    -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Workflow definitions are not baked into the image: mount one folder per module under /definitions
# (/definitions/<moduleId>/module.yaml, workflows/, ...). See deploy/README.md.
RUN mkdir -p /definitions
ENV Flowgent__Definitions__Path=/definitions

LABEL org.opencontainers.image.title="Flowgent Engine" \
      org.opencontainers.image.description="Flowgent engine — domain-agnostic workflow runtime (MAF declarative) that runs YAML workflows and reaches domain tools over MCP." \
      org.opencontainers.image.source="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.url="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Flowgent"

ENTRYPOINT ["dotnet", "Flowgent.Engine.Service.dll"]
