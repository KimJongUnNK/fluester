#!/bin/bash
# Flüster installieren oder aktualisieren – aus dem neuesten GitHub-Release.
#
#   curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash
#
# Entfernen:
#   curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash -s -- --uninstall
set -euo pipefail

REPO="${FLUESTER_REPO:-KimJongUnNK/fluester}"
URL="https://github.com/$REPO/releases/latest/download/Fluester-macOS.zip"
APP="/Applications/Flüster.app"
BUNDLE_ID="app.fluester.mac"

bold=$'\033[1m'; dim=$'\033[2m'; red=$'\033[31m'; green=$'\033[32m'; reset=$'\033[0m'
step() { echo "${bold}▶︎ $*${reset}"; }
ok()   { echo "${green}✓${reset} $*"; }
fail() { echo "${red}✗ $*${reset}" >&2; exit 1; }

# Ohne Schreibrecht auf /Applications mit sudo (fragt nach dem Mac-Passwort)
as_admin() { if [ -w /Applications ]; then "$@"; else sudo "$@"; fi; }

[ "$(uname -s)" = "Darwin" ] || fail "Flüster läuft nur auf macOS."

if [ "${1:-}" = "--uninstall" ]; then
  step "Entferne Flüster …"
  pkill -x Fluester 2>/dev/null || true
  pkill -f "whisper-server.*Fluester" 2>/dev/null || true
  as_admin rm -rf "$APP"
  tccutil reset All "$BUNDLE_ID" >/dev/null 2>&1 || true
  ok "App entfernt, Freigaben zurückgesetzt."
  echo "${dim}Verlauf, Einstellungen und Whisper-Modelle liegen noch in ~/Library/Application Support/Fluester."
  echo "Ganz weg damit: rm -rf ~/Library/Application\\ Support/Fluester${reset}"
  exit 0
fi

# Apple-Chip? (sysctl statt uname, damit ein Terminal unter Rosetta nicht täuscht)
[ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = "1" ] \
  || fail "Die fertige App braucht einen Mac mit Apple-Chip (M1 oder neuer). Für Intel-Macs: README › Selbst bauen."

ver="$(sw_vers -productVersion)"
[ "${ver%%.*}" -ge 14 ] || fail "Flüster braucht macOS 14 oder neuer – hier läuft $ver."

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

if [ -n "${FLUESTER_ZIP:-}" ]; then
  # Für Tests: lokale ZIP statt Download
  cp "$FLUESTER_ZIP" "$TMP/Fluester.zip"
else
  step "Lade die neueste Version …"
  curl -fL --progress-bar --retry 3 -o "$TMP/Fluester.zip" "$URL" \
    || fail "Download fehlgeschlagen. Internet prüfen oder manuell laden: https://github.com/$REPO/releases/latest"
fi

step "Entpacke …"
ditto -x -k "$TMP/Fluester.zip" "$TMP/out"
SRC="$(find "$TMP/out" -maxdepth 1 -name "*.app" -print -quit)"
[ -n "$SRC" ] || fail "In der ZIP ist keine App. Bitte später erneut versuchen."
codesign --verify "$SRC" 2>/dev/null || fail "Die geladene App ist beschädigt. Bitte erneut versuchen."

UPDATE=0
[ -d "$APP" ] && UPDATE=1

if pgrep -x Fluester >/dev/null; then
  step "Beende laufendes Flüster …"
  pkill -x Fluester || true
  sleep 1
fi

step "Installiere nach /Applications …"
as_admin rm -rf "$APP"
as_admin ditto "$SRC" "$APP"
as_admin xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true

if [ "$UPDATE" = 1 ]; then
  # Jede Version hat eine neue Signatur – die alte Bedienungshilfen-Freigabe greift dann nicht mehr.
  # Zurücksetzen, damit macOS sauber neu fragt (die Einrichtung öffnet sich von selbst).
  tccutil reset Accessibility "$BUNDLE_ID" >/dev/null 2>&1 || true
fi

version="$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$APP/Contents/Info.plist" 2>/dev/null || echo "?")"
open "$APP"

echo
ok "Flüster $version ist $([ "$UPDATE" = 1 ] && echo aktualisiert || echo installiert)."
echo
echo "  Flüster sitzt oben rechts in der Menüleiste (Wellen-Symbol), nicht im Dock."
echo "  Die Einrichtung führt durch die Freigaben: Mikrofon, Bedienungshilfen, Spracherkennung."
if [ "$UPDATE" = 1 ]; then
  echo "  Nach dem Update Bedienungshilfen einmal neu erlauben."
fi
echo "  Danach: in ein Textfeld klicken, ${bold}Fn halten, sprechen, loslassen${reset}."
echo
echo "${dim}Anleitung und Hilfe: https://github.com/$REPO${reset}"
