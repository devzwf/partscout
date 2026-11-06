# Les étapes de build tournent sur l'architecture de la machine ($BUILDPLATFORM) : la sortie est la même
# partout (JavaScript, et .NET portable avec les binaires SQLite de chaque architecture). Seule l'image finale
# dépend de la cible, et ne fait que copier : pas de compilation sous émulation.

# 1. Interface
FROM --platform=$BUILDPLATFORM node:26-alpine AS front
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm test && npm run build

# 2. API
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS back
ARG VERSION=0.0.0
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/PartScout.Api -c Release -o /app --nologo -p:Version=$VERSION
COPY --from=front /src/dist /app/wwwroot
RUN mkdir -p /data

# Tests du backend, pour la CI : docker build --target backend-test .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-test
WORKDIR /src
COPY backend/ ./
RUN dotnet test tests/PartScout.Tests --nologo

# 3. Image finale : sans shell, utilisateur non root
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
ARG VERSION=0.0.0
ARG REVISION=unknown
LABEL org.opencontainers.image.title="PartScout" \
      org.opencontainers.image.description="Recherche de composants multi-fournisseurs. Usage personnel et non commercial." \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION" \
      org.opencontainers.image.licenses="LicenseRef-NonCommercial" \
      org.opencontainers.image.source="https://github.com/devzwf/partscout" \
      org.opencontainers.image.url="https://github.com/devzwf/partscout"
WORKDIR /app
COPY --from=back /app ./
COPY LICENSE NOTICE.md ./
COPY --from=back --chown=1654:1654 /data /data
ENV PARTSCOUT_DATA=/data DOTNET_gcServer=0
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 CMD ["dotnet", "PartScout.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "PartScout.Api.dll"]
