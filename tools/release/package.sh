#!/usr/bin/env bash
# Packages an exported build into the release files the in-app updater expects:
#   Luma-linux-x86_64.tar.gz (one folder, Luma/) and SHA256SUMS.
#   tools/release/package.sh <export dir with Luma.x86_64 and luma> <version> <out dir>
set -euo pipefail
BUILD=$(realpath "$1"); VERSION="$2"; OUT=$(realpath -m "$3")
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT
APP="$WORK/Luma"
mkdir -p "$APP" "$OUT"
cp -a "$BUILD"/. "$APP/"
cp -a "$ROOT/data/steam-art" "$APP/steam-art"
cp "$ROOT/tools/release/install.sh" "$APP/install.sh"
cp "$ROOT/LICENSE" "$APP/LICENSE" 2>/dev/null || true
printf 'Luma %s (Linux x86_64)\n\nRun ./install.sh to install for this user, or start Luma.x86_64 from here.\nhttps://github.com/TheRoben-Dev/Luma-Modman\n' "$VERSION" > "$APP/README.txt"
test -x "$APP/Luma.x86_64" && test -x "$APP/luma"
tar -C "$WORK" -czf "$OUT/Luma-linux-x86_64.tar.gz" Luma
(cd "$OUT" && sha256sum Luma-linux-x86_64.tar.gz > SHA256SUMS)
cat "$OUT/SHA256SUMS"
