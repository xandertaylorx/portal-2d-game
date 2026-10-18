#!/bin/zsh
# Publishes a new version of Portal 2D.
#
#   ./scripts/release.sh 2.2.0
#
# 1. checks CHANGELOG.md has a "## [2.2.0]" section and the tests pass
# 2. stamps the version into the project, builds self-contained packages for
#    macOS (arm64 + x64), Windows (x64) and Linux (x64)
# 3. commits + tags the source repo, then creates a GitHub release with the
#    builds on the public releases repo - running games see it and offer to update.
set -e
VERSION=${1:?usage: release.sh <version>}
RELEASE_REPO=${RELEASE_REPO:-xandertaylorx/portal-2d-releases}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
PROJ="$ROOT/src/Portal2D/Portal2D.csproj"
OUT="$ROOT/dist/release-$VERSION"
cd "$ROOT"

grep -q "^## \[$VERSION\]" CHANGELOG.md || { echo "CHANGELOG.md has no section for [$VERSION]"; exit 1; }
[[ -z $(git status --porcelain) ]] || { echo "working tree not clean - commit first"; exit 1; }

echo "== tests"
dotnet run --project "$PROJ" -c Release -- --test | tail -1
dotnet run --project "$PROJ" -c Release -- --procgen 30 | tail -1

echo "== version $VERSION"
sed -i '' -E "s#<Version>[^<]*</Version>#<Version>$VERSION</Version>#" "$PROJ"

rm -rf "$OUT" && mkdir -p "$OUT"
for RID in osx-arm64 osx-x64 win-x64 linux-x64; do
  echo "== build $RID"
  dotnet publish "$PROJ" -c Release -r $RID --self-contained true -o "$OUT/$RID" -v quiet -nologo
  (cd "$OUT/$RID" && zip -qr "$OUT/Portal2D-$RID.zip" .)
done

# double-clickable macOS apps for first-time downloads
for ARCH in arm64 x64; do
  APP="$OUT/mac-$ARCH/Portal 2D.app"
  mkdir -p "$APP/Contents/MacOS"
  cp -R "$OUT/osx-$ARCH/." "$APP/Contents/MacOS/"
  cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Portal 2D</string>
  <key>CFBundleIdentifier</key><string>dev.portal2d.game</string>
  <key>CFBundleExecutable</key><string>Portal2D</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
  (cd "$OUT/mac-$ARCH" && ditto -c -k --keepParent "Portal 2D.app" "$OUT/Portal2D-macOS-$ARCH-app.zip")
done

# release notes = this version's CHANGELOG section
awk -v v="$VERSION" '$0 ~ "^## \\[" v "\\]" {f=1; next} /^## \[/ {f=0} f' CHANGELOG.md > "$OUT/notes.md"

echo "== tag + push"
git commit -qam "Release v$VERSION"
git tag "v$VERSION"
git push -q && git push -q --tags

echo "== publish release on $RELEASE_REPO"
gh release create "v$VERSION" --repo "$RELEASE_REPO" --title "Portal 2D v$VERSION" --notes-file "$OUT/notes.md" \
  "$OUT"/Portal2D-*.zip
echo "Released v$VERSION"
