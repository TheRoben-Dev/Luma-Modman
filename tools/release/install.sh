#!/usr/bin/env bash
# Installs Luma for this user: ~/Applications/Luma, a menu entry, the nxm link handler and
# (when Steam is closed, or with --restart-steam) a Steam shortcut with Luma's art.
# Run from the unpacked release folder:  ./install.sh   (re-run to repair; updates happen in-app)
set -euo pipefail
SRC=$(cd "$(dirname "$0")" && pwd)
DEST="${LUMA_INSTALL_DIR:-$HOME/Applications/Luma}"
mkdir -p "$(dirname "$DEST")"
if [ "$SRC" != "$DEST" ]; then
  rm -rf "$DEST.installing"
  cp -a "$SRC" "$DEST.installing"
  rm -rf "$DEST"
  mv "$DEST.installing" "$DEST"
fi
chmod +x "$DEST/Luma.x86_64" "$DEST/luma"

APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$APPS"
cat > "$APPS/luma.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=Luma
Comment=Controller-first mod manager
Exec="$DEST/Luma.x86_64"
Icon=$DEST/steam-art/icon.png
Terminal=false
Categories=Game;Utility;
DESK
command -v update-desktop-database >/dev/null && update-desktop-database "$APPS" || true

echo "Installed to $DEST"
if [ "${1:-}" = "--restart-steam" ]; then
  "$DEST/luma" steam-add --restart || true
else
  "$DEST/luma" steam-add || echo "Add Luma to Steam later from Luma → Settings → Updates, or run: $DEST/install.sh --restart-steam"
fi
