#!/bin/bash
# Flüster installieren oder aktualisieren – macOS und Linux, aus dem neuesten GitHub-Release.
#
#   curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash
#
# Entfernen:
#   curl -fsSL https://raw.githubusercontent.com/KimJongUnNK/fluester/main/scripts/install.sh | bash -s -- --uninstall
#
# Windows: siehe scripts/install.ps1
set -euo pipefail

REPO="${FLUESTER_REPO:-KimJongUnNK/fluester}"
RELEASES="https://github.com/$REPO/releases/latest/download"

bold=$'\033[1m'; dim=$'\033[2m'; red=$'\033[31m'; green=$'\033[32m'; reset=$'\033[0m'
step() { echo "${bold}▶︎ $*${reset}"; }
ok()   { echo "${green}✓${reset} $*"; }
fail() { echo "${red}✗ $*${reset}" >&2; exit 1; }

UNINSTALL=0
[ "${1:-}" = "--uninstall" ] && UNINSTALL=1

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Datei aus dem Release laden (oder für Tests eine lokale Datei nehmen)
fetch() { # fetch <asset> <ziel> [lokale Datei]
  if [ -n "${3:-}" ]; then
    cp "$3" "$2"
  else
    step "Lade die neueste Version …"
    curl -fL --progress-bar --retry 3 -o "$2" "$RELEASES/$1" \
      || fail "Download fehlgeschlagen. Internet prüfen oder manuell laden: https://github.com/$REPO/releases/latest"
  fi
}

# ════════════════════════════════════════════════════════════════════ macOS

install_macos() {
  local APP="/Applications/Flüster.app" BUNDLE_ID="app.fluester.mac"
  as_admin() { if [ -w /Applications ]; then "$@"; else sudo "$@"; fi; }

  if [ "$UNINSTALL" = 1 ]; then
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
    || fail "Die fertige App braucht einen Mac mit Apple-Chip (M1 oder neuer). Für Intel-Macs: docs/macos.md › Selbst bauen."
  local ver; ver="$(sw_vers -productVersion)"
  [ "${ver%%.*}" -ge 14 ] || fail "Flüster braucht macOS 14 oder neuer – hier läuft $ver."

  fetch "Fluester-macOS.zip" "$TMP/Fluester.zip" "${FLUESTER_ZIP:-}"
  step "Entpacke …"
  ditto -x -k "$TMP/Fluester.zip" "$TMP/out"
  local SRC; SRC="$(find "$TMP/out" -maxdepth 1 -name "*.app" -print -quit)"
  [ -n "$SRC" ] || fail "In der ZIP ist keine App. Bitte später erneut versuchen."
  codesign --verify "$SRC" 2>/dev/null || fail "Die geladene App ist beschädigt. Bitte erneut versuchen."

  local UPDATE=0
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
  # Jede Version hat eine neue Signatur – die alte Bedienungshilfen-Freigabe greift dann nicht mehr.
  [ "$UPDATE" = 1 ] && { tccutil reset Accessibility "$BUNDLE_ID" >/dev/null 2>&1 || true; }

  local version; version="$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$APP/Contents/Info.plist" 2>/dev/null || echo "?")"
  open "$APP"
  echo
  ok "Flüster $version ist $([ "$UPDATE" = 1 ] && echo aktualisiert || echo installiert)."
  echo
  echo "  Flüster sitzt oben rechts in der Menüleiste (Wellen-Symbol), nicht im Dock."
  echo "  Die Einrichtung führt durch die Freigaben: Mikrofon, Bedienungshilfen, Spracherkennung."
  [ "$UPDATE" = 1 ] && echo "  Nach dem Update Bedienungshilfen einmal neu erlauben."
  echo "  Danach: in ein Textfeld klicken, ${bold}Fn halten, sprechen, loslassen${reset}."
  echo
  echo "${dim}Anleitung und Hilfe: https://github.com/$REPO/blob/main/docs/macos.md${reset}"
}

# ════════════════════════════════════════════════════════════════════ Linux

install_linux() {
  local DATA="${XDG_DATA_HOME:-$HOME/.local/share}/fluester"
  local APPDIR="$DATA/app" BIN="$HOME/.local/bin"
  local CONFIG="${XDG_CONFIG_HOME:-$HOME/.config}"

  if [ "$UNINSTALL" = 1 ]; then
    step "Entferne Flüster …"
    if [ -x "$APPDIR/fluester" ]; then "$APPDIR/fluester" stop >/dev/null 2>&1 || true; fi
    pkill -f "$APPDIR/fluester run" 2>/dev/null || true
    rm -rf "$APPDIR"
    rm -f "$BIN/fluester" "$CONFIG/autostart/fluester.desktop" "${XDG_DATA_HOME:-$HOME/.local/share}/applications/fluester-toggle.desktop"
    ok "Flüster entfernt."
    echo "${dim}Einstellungen: $CONFIG/fluester · Modelle und Verlauf: $DATA"
    echo "Ganz weg damit: rm -rf $CONFIG/fluester $DATA"
    echo "Rechte zurücknehmen (optional): sudo rm /etc/udev/rules.d/80-fluester-uinput.rules /etc/modules-load.d/fluester-uinput.conf${reset}"
    exit 0
  fi

  local arch
  case "$(uname -m)" in
    x86_64|amd64) arch="x64" ;;
    aarch64|arm64) arch="arm64" ;;
    *) fail "Prozessor $(uname -m) wird nicht unterstützt (nur x86_64 und arm64)." ;;
  esac

  fetch "Fluester-Linux-$arch.tar.gz" "$TMP/fluester.tar.gz" "${FLUESTER_TARBALL:-}"
  step "Entpacke …"
  mkdir -p "$TMP/out"
  tar -xzf "$TMP/fluester.tar.gz" -C "$TMP/out"
  [ -x "$TMP/out/fluester" ] || fail "Im Archiv fehlt das Programm. Bitte später erneut versuchen."

  local UPDATE=0 RUNNING=0
  [ -d "$APPDIR" ] && UPDATE=1
  if [ -x "$APPDIR/fluester" ] && "$APPDIR/fluester" status >/dev/null 2>&1; then
    RUNNING=1
    step "Beende laufendes Flüster …"
    "$APPDIR/fluester" stop >/dev/null 2>&1 || true
    sleep 1
  fi

  step "Installiere nach $APPDIR …"
  mkdir -p "$DATA" "$BIN"
  rm -rf "$APPDIR.new"
  mv "$TMP/out" "$APPDIR.new"
  rm -rf "$APPDIR"
  mv "$APPDIR.new" "$APPDIR"
  ln -sf "$APPDIR/fluester" "$BIN/fluester"

  local version; version="$("$APPDIR/fluester" version 2>/dev/null || echo "Flüster")"
  echo
  ok "$version ist $([ "$UPDATE" = 1 ] && echo aktualisiert || echo installiert) ($BIN/fluester)."
  case ":$PATH:" in
    *":$BIN:"*) ;;
    *) echo "  ${dim}Hinweis: $BIN ist nicht im PATH – einmal neu anmelden oder 'export PATH=\"\$HOME/.local/bin:\$PATH\"' in ~/.bashrc.${reset}" ;;
  esac

  if [ "$UPDATE" = 1 ]; then
    [ "$RUNNING" = 1 ] && { "$APPDIR/fluester" start || true; }
    echo
    echo "${dim}Anleitung und Hilfe: https://github.com/$REPO/blob/main/docs/linux.md${reset}"
    return
  fi

  # Erstinstallation: Einrichtung im Terminal (liest von /dev/tty, auch bei „curl | bash“)
  if [ "${FLUESTER_NO_SETUP:-0}" != 1 ] && [ -r /dev/tty ] && { : < /dev/tty; } 2>/dev/null; then
    echo
    "$APPDIR/fluester" setup < /dev/tty || true
  else
    echo
    echo "  Weiter mit: ${bold}fluester setup${reset}"
  fi
  echo
  echo "${dim}Anleitung und Hilfe: https://github.com/$REPO/blob/main/docs/linux.md${reset}"
}

case "$(uname -s)" in
  Darwin) install_macos ;;
  Linux) install_linux ;;
  *) fail "Dieses Skript ist für macOS und Linux. Windows: https://github.com/$REPO/blob/main/docs/windows.md" ;;
esac
