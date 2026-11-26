#!/usr/bin/env bash
# UI smoke test: a fake $HOME, then drives the Godot app with scripted controller, keyboard and
# touch input (--smoke) under Xvfb and saves a screenshot per step.
#
#   tools/smoke/run-ui-smoke.sh <godot-mono-editor | exported Luma.x86_64> <out-dir>
set -euo pipefail
GODOT=$(realpath "$1")
OUT=$(realpath -m "$2")
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# Every XDG dir inside the fake home: CI runners set XDG_CONFIG_HOME.
export HOME="$WORK/home" XDG_RUNTIME_DIR="$WORK/run"
export XDG_CONFIG_HOME="$HOME/.config" XDG_DATA_HOME="$HOME/.local/share" XDG_CACHE_HOME="$HOME/.cache"
mkdir -p "$HOME" "$XDG_RUNTIME_DIR" "$OUT"

# A fake Steam install with a few supported games (one not moddable), an extra library, a
# Proton prefix and cover art, so the game switcher and Mods tab have something to show.
STEAM="$HOME/.local/share/Steam"
LIB2="$WORK/games2"
game() {  # game <library> <appid> <name> <folder> <file inside folder>
  mkdir -p "$1/steamapps/common/$4/$(dirname "$5")"
  : > "$1/steamapps/common/$4/$5"
  printf '"AppState" { "appid" "%s" "name" "%s" "installdir" "%s" }\n' "$2" "$3" "$4" > "$1/steamapps/appmanifest_$2.acf"
}
mkdir -p "$STEAM/steamapps" "$LIB2/steamapps" "$STEAM/appcache/librarycache/489830" "$STEAM/appcache/librarycache/1966720"
printf '"libraryfolders" { "0" { "path" "%s" } "1" { "path" "%s" } }\n' "$STEAM" "$LIB2" > "$STEAM/steamapps/libraryfolders.vdf"
game "$STEAM" 489830 "The Elder Scrolls V: Skyrim Special Edition" "Skyrim Special Edition" SkyrimSE.exe
game "$LIB2" 1966720 "Lethal Company" "Lethal Company" "Lethal Company.exe"
game "$STEAM" 413150 "Stardew Valley" "Stardew Valley" "Stardew Valley.dll"
game "$STEAM" 2767030 "Marvel Rivals" "MarvelRivals" MarvelGame/Marvel/Binaries/Win64/Marvel-Win64-Shipping.exe
game "$STEAM" 12345 "Some Unsupported Game" "Unsupported" game.exe
mkdir -p "$STEAM/steamapps/compatdata/489830/pfx/drive_c"
cp "$ROOT/tools/smoke/fixtures/cover-a.jpg" "$STEAM/appcache/librarycache/489830/library_600x900.jpg"
cp "$ROOT/tools/smoke/fixtures/cover-b.jpg" "$STEAM/appcache/librarycache/1966720/library_600x900.jpg"

if [ -n "${SMOKE_THEME:-}" ]; then  # e.g. SMOKE_THEME=nightgrid for theme screenshots
  mkdir -p "$XDG_CONFIG_HOME/luma"
  printf '{"Theme":"%s"}\n' "$SMOKE_THEME" > "$XDG_CONFIG_HOME/luma/settings.json"
fi

# Offline stand-in for the mod sites (Thunderstore results, icons, a download).
PORT=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1])')
python3 "$ROOT/tools/smoke/fake_sites.py" "$PORT" &
SITES_PID=$!
trap 'kill $SITES_PID 2>/dev/null; rm -rf "$WORK"' EXIT
export LUMA_FAKE_SITES="http://127.0.0.1:$PORT"

cd "$ROOT/src/Luma.App"
PROJECT=(--path .)
[ "$(basename "$GODOT")" = "Luma.x86_64" ] && PROJECT=()  # exported build: project is embedded
set +e
timeout 300 xvfb-run -a -s "-screen 0 1280x800x24" "$GODOT" "${PROJECT[@]}" --rendering-driver opengl3 \
  -- --windowed --smoke "$OUT" 2>&1 | tee "$OUT/godot.log"
code=${PIPESTATUS[0]}
set -e
cp "$XDG_DATA_HOME"/luma/logs/app.log "$OUT/app.log" 2>/dev/null || true
if grep -qE "Unhandled exception|System\.[A-Za-z.]*Exception|smoke: step .* failed" "$OUT/godot.log"; then
  echo "::error::exceptions or failed steps during the UI smoke test (see godot.log)"
  exit 1
fi
shots=$(ls "$OUT"/*.png 2>/dev/null | wc -l)
echo "exit=$code screenshots=$shots"
[ "$code" -eq 0 ] && [ "$shots" -ge 20 ]
