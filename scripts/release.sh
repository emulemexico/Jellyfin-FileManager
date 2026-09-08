#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-}"
CHANGELOG="${2:-Release ${VERSION}}"

if [ -z "$VERSION" ]; then
    echo "Uso: ./scripts/release.sh <VERSION> [CHANGELOG]"
    echo "Ejemplo: ./scripts/release.sh 10.11.0.3"
    exit 1
fi

echo "=== Jellyfin File Manager - Local Release Script (Linux) ==="
echo "Target Version: $VERSION"

# 1. Comprobar herramientas
command -v git >/dev/null 2>&1 || { echo "Error: git no está instalado." >&2; exit 1; }
command -v gh >/dev/null 2>&1 || { echo "Error: gh (GitHub CLI) no está instalado." >&2; exit 1; }
command -v dotnet >/dev/null 2>&1 || { echo "Error: dotnet no está instalado." >&2; exit 1; }
command -v zip >/dev/null 2>&1 || { echo "Error: zip no está instalado." >&2; exit 1; }
command -v jq >/dev/null 2>&1 || { echo "Error: jq no está instalado." >&2; exit 1; }

# 2. Comprobar autenticación GitHub
echo "Verificando autenticación GitHub..."
gh auth status || { echo "Error: ejecute 'gh auth login' antes de continuar." >&2; exit 1; }
OWNER=$(gh api user --jq .login | tr -d '[:space:]')
echo "Usuario GitHub: $OWNER"

# 3. Comprobar .NET 9
DOTNET_VER=$(dotnet --version)
if [[ ! "$DOTNET_VER" =~ ^9\. ]]; then
    echo "Error: Se requiere el SDK de .NET 9.x. Versión actual: $DOTNET_VER" >&2
    exit 1
fi

# 4. Clean, restore, build Release
echo "Limpiando..."
dotnet clean -c Release

echo "Restaurando..."
dotnet restore

echo "Compilando Release..."
dotnet build -c Release --no-restore

DLL="bin/Release/net9.0/Jellyfin.Plugin.FileManager.dll"
if [ ! -f "$DLL" ]; then
    echo "Error: No se encontró la DLL generada en $DLL." >&2
    exit 1
fi

# 5. Crear dist y ZIP
mkdir -p dist/staging
cp "$DLL" dist/staging/
ZIP_NAME="Jellyfin.Plugin.FileManager_${VERSION}.zip"
ZIP_PATH="dist/${ZIP_NAME}"
rm -f "$ZIP_PATH"
(cd dist/staging && zip -q "../../${ZIP_PATH}" Jellyfin.Plugin.FileManager.dll)
rm -rf dist/staging

# 6. Checksum MD5
MD5=$(md5sum "$ZIP_PATH" | awk '{print $1}')
echo "ZIP: $ZIP_PATH"
echo "MD5: $MD5"

# 7. Actualizar build.yaml
sed -i -E "s/^version: \".*\"/version: \"${VERSION}\"/" build.yaml
sed -i -E "s/^owner: \".*\"/owner: \"${OWNER}\"/" build.yaml

# 8. Actualizar manifest.json
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
SOURCE_URL="https://github.com/${OWNER}/Jellyfin-FileManager/releases/download/v${VERSION}/${ZIP_NAME}"

jq --arg ver "$VERSION" \
   --arg chg "$CHANGELOG" \
   --arg url "$SOURCE_URL" \
   --arg md5 "$MD5" \
   --arg time "$TIMESTAMP" \
   --arg owner "$OWNER" \
   'map(
      if .guid == "f4217828-0ed8-4d2e-a3cd-6dc74806f716" then
        .owner = $owner |
        .versions = [
          {
            version: $ver,
            changelog: $chg,
            targetAbi: "10.11.0.0",
            sourceUrl: $url,
            checksum: $md5,
            timestamp: $time
          }
        ] + (.versions | map(select(.version != $ver)))
      else . end
   )' manifest.json > manifest.json.tmp && mv manifest.json.tmp manifest.json

# Validar JSON
jq . manifest.json >/dev/null

# 9. Git commit y tag
git add .
git commit -m "Release File Manager ${VERSION}"
git tag -a "v${VERSION}" -m "File Manager ${VERSION}"

# 10. Push
git push origin main
git push origin "v${VERSION}"

# 11. GitHub Release
gh release create "v${VERSION}" "$ZIP_PATH" --title "File Manager ${VERSION}" --notes "$CHANGELOG"

echo "¡Release ${VERSION} completado con éxito!"