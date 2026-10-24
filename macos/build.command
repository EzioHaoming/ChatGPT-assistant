#!/bin/bash
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
cd "$(dirname "$0")"
[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || { echo '需要 Apple 芯片 Mac'; exit 1; }
APP="$PWD/dist/ChatGPT assistant.app"
SIGNING_DIR="$HOME/Library/Application Support/ChatGPT assistant/Signing"
[[ -s "$SIGNING_DIR/identity.sha1" ]] || { echo '缺少固定签名证书。请先运行 runtime/bin/python3 scripts/create-signing-identity.py；不要自动重建已有证书。'; exit 1; }
SIGNING_ID=$(tr -d '\n\r' < "$SIGNING_DIR/identity.sha1")
[[ "$SIGNING_ID" =~ ^[A-F0-9]{40}$ ]] || { echo '固定签名证书指纹无效'; exit 1; }
REQUIREMENT="designated => identifier \"local.chatgpt-assistant.mac\" and certificate leaf = H\"$SIGNING_ID\""

rm -rf "$APP" "$PWD/build/AppIcon.iconset"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bridge" "$PWD/build/AppIcon.iconset"
xcrun swiftc -swift-version 5 -target arm64-apple-macosx13.0 -O -framework AppKit -framework Carbon Sources/main.swift -o "$APP/Contents/MacOS/ChatGPT assistant"
for file in bridge/*.py; do cp "$file" "$APP/Contents/Resources/bridge/"; done
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" app.png --out "build/AppIcon.iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" app.png --out "build/AppIcon.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns build/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"
ditto Resources/renderer "$APP/Contents/Resources/renderer"
cp LICENSE "$APP/Contents/Resources/LICENSE"
ditto docs "$APP/Contents/Resources/docs"
cp Info.plist "$APP/Contents/Info.plist"
if [[ -x runtime/bin/python3 ]]; then
  ditto runtime "$APP/Contents/Resources/python"
fi
runtime/bin/python3 -B scripts/release-hygiene.py --clean "$APP"
plutil -lint "$APP/Contents/Info.plist"
# Sign every real nested Mach-O, including dylibs and Python extensions.
runtime/bin/python3 -B scripts/sign-nested.py "$APP" "$SIGNING_ID"
codesign --force --sign "$SIGNING_ID" --timestamp=none "$APP"
codesign --force --sign "$SIGNING_ID" --timestamp=none -r "=$REQUIREMENT" "$APP"
codesign --verify --deep --strict "$APP"
runtime/bin/python3 -B scripts/sign-nested.py --verify "$APP"
lipo -archs "$APP/Contents/MacOS/ChatGPT assistant"
codesign --verify -R "=identifier \"local.chatgpt-assistant.mac\" and certificate leaf = H\"$SIGNING_ID\"" "$APP"
echo "已生成固定本地证书签名的 Apple Silicon 应用（非 Developer ID 公证安装包）。"
