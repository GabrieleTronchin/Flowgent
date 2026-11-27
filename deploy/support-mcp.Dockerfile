# syntax=docker/dockerfile:1
# Flowgent support MCP server — EXAMPLE DOMAIN MODULE (not part of the core platform).
# Build context is the repository root (see deploy/README.md).

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the nuget.org-only feed config FIRST so restore is deterministic.
COPY nuget.config ./

COPY Directory.Build.props Directory.Packages.props ./
COPY backend/shared/ ./backend/shared/
COPY backend/modules/support/mcp/ ./backend/modules/support/mcp/

RUN dotnet restore backend/modules/support/mcp/Flowgent.Mcp.Support/Flowgent.Mcp.Support.csproj
RUN dotnet publish backend/modules/support/mcp/Flowgent.Mcp.Support/Flowgent.Mcp.Support.csproj \
    -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# The locateComponent demo tool searches the example dummy-apps tree.
COPY backend/modules/support/dummy-apps /app/dummy-apps
ENV SUPPORT_DUMMYAPPS_ROOT=/app/dummy-apps

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

LABEL org.opencontainers.image.title="Flowgent Support MCP (example)" \
      org.opencontainers.image.description="Flowgent example MCP server — the support demo's locateComponent domain tool over Streamable HTTP. An EXAMPLE of how to write a domain module, not part of the core platform." \
      org.opencontainers.image.source="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.url="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Flowgent"

ENTRYPOINT ["dotnet", "Flowgent.Mcp.Support.dll"]
