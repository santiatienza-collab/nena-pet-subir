#!/bin/bash
# Compila Nena Pet para macOS y genera NenaPet.app, NenaPet-mac.zip y NenaPet.dmg en ./dist
# Requisitos: un Mac con las herramientas de línea de comandos (xcode-select --install). Uso: ./build-mac.sh
set -euo pipefail
cd "$(dirname "$0")"
APP="dist/Nena Pet.app"
rm -rf dist && mkdir -p dist

echo "==> Compilando (universal arm64 + x86_64)..."
swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/NenaPet"

echo "==> Creando la app..."
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/NenaPet"
cp Info.plist "$APP/Contents/Info.plist"
cp -R Resources/sounds "$APP/Contents/Resources/sounds"
ZIP=$(find "$PWD" -name frames.zip | head -n 1)
echo "Fotogramas: $ZIP"
mkdir -p "$APP/Contents/Resources/frames"
if [ -n "$ZIP" ]; then
  TMP=$(mktemp -d); unzip -q -o "$ZIP" -d "$TMP" || python3 -m zipfile -e "$ZIP" "$TMP"
  cp "$TMP"/frames/*.png "$APP/Contents/Resources/frames/" 2>/dev/null || cp "$TMP"/*.png "$APP/Contents/Resources/frames/"
elif [ -d Resources/frames ]; then
  cp Resources/frames/*.png "$APP/Contents/Resources/frames/"
else
  echo "FALTAN LOS FOTOGRAMAS (frames.zip)"; exit 1
fi

echo "==> Icono..."
ICS="dist/AppIcon.iconset"; mkdir -p "$ICS"
for s in 16 32 64 128 256; do
  sips -z $s $s Resources/icon.png --out "$ICS/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) Resources/icon.png --out "$ICS/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICS" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICS"

echo "==> Firmando (firma local, sin cuenta de Apple)..."
codesign --force --deep --sign - "$APP"

echo "==> Empaquetando..."
(cd dist && ditto -c -k --keepParent "Nena Pet.app" NenaPet-mac.zip)
mkdir -p dist/dmgroot && cp -R "$APP" dist/dmgroot/ && ln -s /Applications dist/dmgroot/Applications
hdiutil create -volname "Nena Pet" -srcfolder dist/dmgroot -ov -format UDZO dist/NenaPet.dmg >/dev/null
rm -rf dist/dmgroot

echo "Listo: dist/NenaPet.dmg  y  dist/NenaPet-mac.zip"
