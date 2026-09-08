$ErrorActionPreference = "Stop"

Write-Host "==> Limpiando..."
dotnet clean -c Release

Write-Host "==> Restaurando dependencias..."
dotnet restore

Write-Host "==> Compilando en modo Release..."
dotnet build -c Release --no-restore

$Version = "10.11.0.2"
$Out = "dist"
$Dll = "bin\Release\net9.0\Jellyfin.Plugin.FileManager.dll"
$Zip = Join-Path $Out "Jellyfin.Plugin.FileManager_${Version}.zip"
$Staging = Join-Path $Out "staging"

if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }
New-Item -ItemType Directory -Path $Staging -Force | Out-Null

Copy-Item $Dll $Staging
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path "$Staging\*" -DestinationPath $Zip -Force
Remove-Item $Staging -Recurse -Force

$hash = (Get-FileHash $Zip -Algorithm MD5).Hash.ToLower()
Write-Host "==> Paquete ZIP creado exitosamente: $Zip"
Write-Host "==> MD5 Checksum: $hash"
