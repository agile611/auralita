#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PUBLISH_DIR="$ROOT/artifacts/publish/osx-arm64"
APP_BUNDLE="$ROOT/artifacts/Auralita.app"
CONTENTS_DIR="$APP_BUNDLE/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
ICONSET_DIR="$ROOT/artifacts/Auralita.iconset"

if [ -x "$ROOT/.tooling/dotnet/dotnet" ]; then
    DOTNET="$ROOT/.tooling/dotnet/dotnet"
else
    DOTNET=dotnet
fi

"$DOTNET" publish "$ROOT/src/Auralita.App/Auralita.App.csproj" \
    --configuration Release \
    --runtime osx-arm64 \
    --self-contained true \
    --output "$PUBLISH_DIR" \
    -p:PublishAot=true

if [ ! -x "$PUBLISH_DIR/Auralita.App" ]; then
    echo "Publish did not produce the expected arm64 executable: $PUBLISH_DIR/Auralita.App" >&2
    exit 1
fi

rm -rf "$APP_BUNDLE"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
cp "$PUBLISH_DIR/Auralita.App" "$MACOS_DIR/Auralita.App"
for library in "$PUBLISH_DIR"/*.dylib; do
    [ -f "$library" ] || continue
    cp "$library" "$MACOS_DIR/"
done
swift "$ROOT/scripts/generate-macos-icon.swift" "$ICONSET_DIR"
iconutil -c icns "$ICONSET_DIR" -o "$RESOURCES_DIR/Auralita.icns"
cp "$ROOT/src/Auralita.App/Info.plist" "$CONTENTS_DIR/Info.plist"
printf 'APPL????' > "$CONTENTS_DIR/PkgInfo"

codesign --force --deep --sign - "$APP_BUNDLE"

echo "Created Apple Silicon app bundle: $APP_BUNDLE"
