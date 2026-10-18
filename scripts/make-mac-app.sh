#!/bin/zsh
# Builds a self-contained, double-clickable Portal 2D.app for macOS (Apple Silicon by default).
set -e
RID=${1:-osx-arm64}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT="$ROOT/dist/Portal 2D.app"
dotnet publish "$ROOT/src/Portal2D/Portal2D.csproj" -c Release -r $RID --self-contained true -o "$ROOT/dist/publish-$RID"
rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS" "$OUT/Contents/Resources"
cp -R "$ROOT/dist/publish-$RID/." "$OUT/Contents/MacOS/"
cat > "$OUT/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Portal 2D</string>
  <key>CFBundleIdentifier</key><string>dev.portal2d.game</string>
  <key>CFBundleExecutable</key><string>Portal2D</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
echo "Built: $OUT"
