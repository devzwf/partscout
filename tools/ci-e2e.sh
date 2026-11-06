#!/usr/bin/env bash
# Lance le parcours navigateur contre une démo jetable : réseau privé, aucun port publié, tout est retiré à la fin.
# Usage : tools/ci-e2e.sh <image>   (E2E_PLATFORM=linux/arm64 pour tester une autre architecture, émulée)
set -euo pipefail
cd "$(dirname "$0")/.."
image="${1:?indiquer l image a tester}"
id="partscout-e2e-$$-${GITHUB_RUN_ID:-local}"
# Cette image apporte les navigateurs ; le paquet Python, de même version, s'installe au lancement.
runner_image="mcr.microsoft.com/playwright/python:v1.64.0-noble"

cleanup() {
  docker rm -f "$id-test" "$id-app" >/dev/null 2>&1 || true
  docker network rm "$id" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker network create "$id" >/dev/null
docker run -d --name "$id-app" ${E2E_PLATFORM:+--platform "$E2E_PLATFORM"} --network "$id" --network-alias partscout-demo -e PARTSCOUT_DEMO=1 \
  --read-only --tmpfs /tmp --tmpfs /data:uid=1654,gid=1654 --cap-drop ALL --security-opt no-new-privileges:true \
  "$image" >/dev/null

# Alias : pas « app », TLD préchargé en HSTS dans Chromium, qui y forcerait le HTTPS.
# Le dossier du dépôt n'est pas forcément visible du démon Docker (runner en conteneur) : on copie les fichiers.
docker create --name "$id-test" --network "$id" "$runner_image" \
  sh -c 'pip install -q --root-user-action=ignore playwright==1.64.0 && for i in $(seq 1 120); do python -c "import urllib.request as u; u.urlopen(\"http://partscout-demo:8080/api/auth/status\", timeout=2)" 2>/dev/null && break; sleep 1; done; exec python /t/tests/e2e/test_app.py http://partscout-demo:8080' >/dev/null
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"; cleanup' EXIT
mkdir -p "$tmp/tests/e2e"
cp tests/e2e/test_app.py "$tmp/tests/e2e/"
cp VERSION "$tmp/"
docker cp "$tmp" "$id-test:/t" >/dev/null
docker start -a "$id-test"
