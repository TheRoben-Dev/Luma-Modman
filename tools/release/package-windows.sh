#!/usr/bin/env bash
# Packages an exported Windows build into the release zip the in-app updater expects:
#   Luma-windows-x86_64.zip (one folder, Luma/), and adds it to SHA256SUMS in the out dir.
#   tools/release/package-windows.sh <export dir with Luma.exe and luma-cli.exe> <version> <out dir>
set -euo pipefail
BUILD=$(realpath "$1"); VERSION="$2"; OUT=$(realpath -m "$3")
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT
APP="$WORK/Luma"
mkdir -p "$APP" "$OUT"
cp -a "$BUILD"/. "$APP/"
cp -a "$ROOT/data/steam-art" "$APP/steam-art"
cp "$ROOT/tools/release/windows/"* "$APP/"
cp "$ROOT/LICENSE" "$APP/LICENSE.txt" 2>/dev/null || true
printf 'Luma %s (Windows x86_64)\r\n\r\nDouble-click "Install Luma.cmd" to install for this user, or start Luma.exe from here.\r\nhttps://github.com/TheRoben-Dev/Luma-Modman\r\n' "$VERSION" > "$APP/README.txt"
test -f "$APP/Luma.exe" && test -f "$APP/luma-cli.exe"
rm -f "$OUT/Luma-windows-x86_64.zip"
(cd "$WORK" && zip -qr -9 "$OUT/Luma-windows-x86_64.zip" Luma)
(cd "$OUT" && sha256sum Luma-windows-x86_64.zip >> SHA256SUMS)
cat "$OUT/SHA256SUMS"
