# syntax=docker/dockerfile:1
# Flowgent dashboard — CORE PLATFORM image.
# Build context is the repository root (see deploy/README.md).

# ---- build stage ----
FROM node:20-alpine AS build
WORKDIR /src

# Install deps against the lockfile first for cache-friendly layers.
COPY dashboard/package.json dashboard/package-lock.json ./
RUN npm ci

# Build the SPA.
COPY dashboard/ ./
RUN npm run build

# ---- runtime stage ----
FROM nginx:1.27-alpine AS runtime

# Minimal SPA-fallback config (try_files $uri /index.html). No gateway
# reverse-proxy wiring this round (see deploy/README.md).
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 80

LABEL org.opencontainers.image.title="Flowgent Dashboard" \
      org.opencontainers.image.description="Flowgent dashboard — generic React console for monitoring any module." \
      org.opencontainers.image.source="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.url="https://github.com/GabrieleTronchin/Flowgent" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="Flowgent"
