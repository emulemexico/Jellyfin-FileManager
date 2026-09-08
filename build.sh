#!/usr/bin/env bash
set -euo pipefail

echo "==> Limpiando..."
dotnet clean -c Release

echo "==> Restaurando dependencias..."
dotnet restore

echo "==> Compilando en modo Release..."
dotnet build -c Release --no-restore

VERSION="10.11.0.2"
OUT="dist"
DLL="bin/Release/net9.0/Jellyfin.Plugin.FileManager.dll"
ZIP="${OUT}/Jellyfin.Plugin.FileManager_${VERSION}.zip"
STAGING="${OUT}/staging"

rm -rf "$STAGING"
mkdir -p "$STAGING"
mkdir -p "$OUT"
cp "$DLL" "$STAGING/"
rm -f "$ZIP"
(cd "$STAGING" && zip -q "../../${ZIP}" Jellyfin.Plugin.FileManager.dll)
rm -rf "$STAGING"

MD5=$(md5sum "$ZIP" | awk '{print $1}')
echo "==> Paquete ZIP creado exitosamente: $ZIP"
echo "==> MD5 Checksum: $MD5"