#!/bin/bash
# Baut Windows- bzw. Linux-Paket (self-contained) und entfernt Laufzeiten anderer Systeme.
#   scripts/package-dotnet.sh linux-x64|linux-arm64|win-x64 <version> <ausgabeordner>
set -euo pipefail
cd "$(dirname "$0")/.."
RID="$1"; VERSION="${2:-1.0.0}"; OUT="${3:-dist}"
case "$RID" in
  win-*) PROJ=windows/Fluester.Windows/Fluester.Windows.csproj ;;
  linux-*) PROJ=linux/Fluester.Linux/Fluester.Linux.csproj ;;
  *) echo "Unbekannte Plattform: $RID" >&2; exit 1 ;;
esac
PUB="$OUT/$RID"
rm -rf "$PUB"
dotnet publish "$PROJ" -c Release -r "$RID" --self-contained true -p:Version="$VERSION" -p:DebugType=none -o "$PUB" -v q
# Native Whisper-Bibliotheken nur für diese Plattform behalten (CPU + Vulkan)
if [ -d "$PUB/runtimes" ]; then
  find "$PUB/runtimes" -mindepth 1 -maxdepth 1 -type d ! -name "$RID" ! -name vulkan -exec rm -rf {} +
  [ -d "$PUB/runtimes/vulkan" ] && find "$PUB/runtimes/vulkan" -mindepth 1 -maxdepth 1 -type d ! -name "$RID" -exec rm -rf {} +
fi
find "$PUB" -name "*.pdb" -delete
mkdir -p "$OUT"
case "$RID" in
  win-*) (cd "$PUB" && rm -f "../../$OUT/Fluester-Windows-${RID#win-}.zip" && zip -qr9 "$OLDPWD/$OUT/Fluester-Windows-${RID#win-}.zip" .) ;;
  linux-*) tar -czf "$OUT/Fluester-Linux-${RID#linux-}.tar.gz" -C "$PUB" . ;;
esac
du -sh "$PUB"
ls -la "$OUT"/Fluester-*
