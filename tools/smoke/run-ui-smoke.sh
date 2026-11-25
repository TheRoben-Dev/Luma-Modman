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

if [ -n "${SMOKE_THEME:-}" ]; then  # e.g. SMOKE_THEME=nightgrid for theme screenshots
  mkdir -p "$XDG_CONFIG_HOME/luma"
  printf '{"Theme":"%s"}\n' "$SMOKE_THEME" > "$XDG_CONFIG_HOME/luma/settings.json"
fi

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
