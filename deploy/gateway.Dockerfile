# syntax=docker/dockerfile:1
# Flowgent gateway — CORE PLATFORM image.
# Build context is the repository root (see deploy/README.md).

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the nuget.org-only feed config FIRST so restore is deterministic.
COPY nuget.config ./

COPY backend/ ./backend/

RUN dotnet restore backend/src/Services/Flowgent.Gateway/Flowgent.Gateway.csproj
RUN dotnet publish backend/src/Services/Flowgent.Gateway/Flowgent.Gateway.csproj \
    -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

LABEL org.opencontainers.image.title="Flowgent Gateway" \
      org.opencontainers.image.description="Flowgent gateway — single ingress (YARP) for the dashboard and clients." \
      org.opencontainers.image.source="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.url="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Flowgent"

ENTRYPOINT ["dotnet", "Flowgent.Gateway.dll"]
