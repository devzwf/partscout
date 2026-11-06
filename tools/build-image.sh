#!/usr/bin/env bash
# Construit l'image locale partscout:<VERSION> à partir du dernier commit. Ne pousse rien.
# PLATFORMS=linux/amd64,linux/arm64 tools/build-image.sh : image multi-architecture (stockage d'images containerd requis).
set -euo pipefail
cd "$(dirname "$0")/.."
version="$(cat VERSION)"
if [ -n "$(git status --porcelain)" ]; then
  echo "Des changements ne sont pas commités : l'image ne correspondrait pas à sa révision." >&2
  exit 1
fi
if docker image inspect "partscout:$version" >/dev/null 2>&1; then
  echo "partscout:$version existe déjà : change VERSION, ou retire cette image si c'est voulu." >&2
  exit 1
fi
docker build --pull ${PLATFORMS:+--platform "$PLATFORMS"} --build-arg VERSION="$version" --build-arg REVISION="$(git rev-parse --short HEAD)" -t "partscout:$version" .
docker image inspect "partscout:$version" --format 'Construite : {{index .RepoTags 0}}  révision {{index .Config.Labels "org.opencontainers.image.revision"}}'
[ -n "${PLATFORMS:-}" ] && docker image inspect "partscout:$version" --format 'Architectures : {{range .Manifests}}{{.Descriptor.Platform.Architecture}} {{end}}' 2>/dev/null || true
