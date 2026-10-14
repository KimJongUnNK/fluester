#!/bin/bash
# Baut Flüster.app (Release), signiert ad hoc und packt ein ZIP.
#   ./scripts/build-app.sh            → build/Flüster.app + build/Fluester-macOS.zip
#   INSTALL=1 ./scripts/build-app.sh  → zusätzlich nach /Applications kopieren und starten
#   UNIVERSAL=1 ./scripts/build-app.sh → arm64 + x86_64
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${VERSION:-1.0.0}"
BUILD="${BUILD:-$(date +%Y%m%d%H%M)}"
APP="build/Flüster.app"

echo "▶︎ Kompilieren (Release) …"
if [ "${UNIVERSAL:-0}" = "1" ]; then
  swift build -c release --arch arm64 --arch x86_64
  BIN_DIR="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)"
else
  swift build -c release
  BIN_DIR="$(swift build -c release --show-bin-path)"
fi

echo "▶︎ App-Bundle …"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/Fluester" "$APP/Contents/MacOS/Fluester"
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD/" Resources/Info.plist > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

echo "▶︎ Symbol …"
ICONSET="build/AppIcon.iconset"
rm -rf "$ICONSET"; mkdir -p "$ICONSET"
swift scripts/make-icon.swift build/AppIcon-1024.png >/dev/null
for s in 16 32 128 256 512; do
  sips -z $s $s build/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d build/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

echo "▶︎ Signieren (ad hoc) …"
codesign --force --sign - --entitlements Resources/Fluester.entitlements --options runtime "$APP"
codesign --verify --verbose=1 "$APP"

echo "▶︎ ZIP …"
rm -f build/Fluester-macOS.zip
ditto -c -k --keepParent "$APP" build/Fluester-macOS.zip
echo "✓ $APP  ($(du -sh "$APP" | cut -f1))"
echo "✓ build/Fluester-macOS.zip"

if [ "${INSTALL:-0}" = "1" ]; then
  echo "▶︎ Installieren nach /Applications …"
  pkill -x Fluester 2>/dev/null || true
  rm -rf "/Applications/Flüster.app"
  cp -R "$APP" /Applications/
  # Neue Signatur → alte Freigabe passt nicht mehr; zurücksetzen, damit macOS sauber neu fragt
  tccutil reset Accessibility app.fluester.mac >/dev/null 2>&1 || true
  open "/Applications/Flüster.app"
  echo "✓ Installiert. Bedienungshilfen einmal neu erlauben (Einrichtung öffnet sich)."
fi
