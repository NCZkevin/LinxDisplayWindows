#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 5 ]]; then
  echo "usage: package-macos.sh <publish-dir> <output-dir> <asset-name> <version> <rid>" >&2
  exit 64
fi

publish_dir="$1"
output_dir="$2"
asset_name="$3"
version="$4"
rid="$5"
app="$output_dir/CodexLinxDisplay.app"
iconset="$output_dir/app.iconset"
executable="$app/Contents/MacOS/CodexLinxDisplay"
archive="$output_dir/CodexLinxDisplay-${asset_name}-v${version}.zip"
marketing_version="${version%%-*}"
build_version="${version##*.}"

if [[ "$build_version" == "$version" || ! "$build_version" =~ ^[0-9]+$ ]]; then
  build_version="1"
fi

rm -rf "$app" "$iconset" "$archive"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$iconset"
cp -R "$publish_dir"/. "$app/Contents/MacOS/"
chmod +x "$executable"

case "$rid" in
  osx-arm64) expected_architecture="arm64" ;;
  osx-x64) expected_architecture="x86_64" ;;
  *) echo "unsupported macOS runtime identifier: $rid" >&2; exit 65 ;;
esac

file "$executable" | grep -q "$expected_architecture" || {
  echo "main executable is not built for $expected_architecture" >&2
  file "$executable" >&2
  exit 66
}

for size in 16 32 128 256 512; do
  sips -z "$size" "$size" src/CodexLinxDisplay.Windows/Assets/app-icon.png \
    --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" src/CodexLinxDisplay.Windows/Assets/app-icon.png \
    --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/app.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>LinxDisplay</string>
  <key>CFBundleDisplayName</key><string>Codex 屏显 for Linx68</string>
  <key>CFBundleIdentifier</key><string>com.codexlinxdisplay.desktop</string>
  <key>CFBundleVersion</key><string>${build_version}</string>
  <key>CFBundleShortVersionString</key><string>${marketing_version}</string>
  <key>CFBundleExecutable</key><string>CodexLinxDisplay</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>app.icns</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

plutil -lint "$app/Contents/Info.plist"

# Ad-hoc signing keeps the bundle internally consistent. A future Developer ID
# certificate plus notarization is still required to avoid all Gatekeeper prompts.
while IFS= read -r -d '' binary; do
  if file "$binary" | grep -q "Mach-O"; then
    codesign --force --sign - "$binary"
  fi
done < <(find "$app/Contents/MacOS" -type f -print0)
codesign --force --sign - "$app"
codesign --verify --deep --strict --verbose=2 "$app"

ditto -c -k --sequesterRsrc --keepParent "$app" "$archive"
test -s "$archive"
echo "$archive"
