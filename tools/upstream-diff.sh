#!/usr/bin/env bash
# Montre ce que l'auteur a changé dans les fichiers repris depuis le commit noté dans upstream/COMMIT.
# Usage : tools/upstream-diff.sh [dossier d'un clone de LinuxPartsFinder]. Sans argument, clone dans un dossier temporaire.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
clone="${1:-}"
if [ -z "$clone" ]; then
  clone="$(mktemp -d)"
  trap 'rm -rf "$clone"' EXIT
  git clone -q https://github.com/Ian-Johnston/LinuxPartsFinder.git "$clone"
fi
echo "Référence : $(cat "$root/upstream/COMMIT")  →  dernière version : $(git -C "$clone" rev-parse --short HEAD)"
cd "$root/upstream/LinuxPartsFinder"
find . -name '*.cs' | sort | while read -r f; do
  diff -u --label "repris/$f" --label "auteur/$f" "$f" "$clone/LinuxPartsFinder/$f" || true
done
