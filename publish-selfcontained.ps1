# Publish Atlas as self-contained (includes .NET runtime)
# This creates a runtime-included build and copies the verified bundle to a release folder.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildOutput = Join-Path $root "bin\x64"
$releaseDir = Join-Path $root "_builds\private_selfcontained"

Write-Host "Publishing Atlas as self-contained application..." -ForegroundColor Cyan

Get-Process -Name "Atlas_v2" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "Atlas" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $buildOutput) {
    Get-ChildItem -Path $buildOutput -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Cleaned previous build output" -ForegroundColor Yellow
}

if (Test-Path $releaseDir) {
    Get-ChildItem -Path $releaseDir -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

dotnet publish AtlasAI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:BuildEmbeddedUi=true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:AtlasLeanSingleFile=true

$exePath = Join-Path $buildOutput "Atlas_v2.exe"
if (-not (Test-Path $exePath)) {
    throw "Build finished without producing bin\\x64\\Atlas_v2.exe"
}

Copy-Item -Path (Join-Path $buildOutput "*") -Destination $releaseDir -Recurse -Force

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
    Write-Host ("Verified output: " + $exePath) -ForegroundColor Cyan
    Write-Host ("Release bundle: " + (Join-Path $releaseDir "Atlas_v2.exe")) -ForegroundColor Cyan
    Write-Host "`nThis executable includes the .NET runtime and can run on any Windows x64 system." -ForegroundColor Green
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit 1
}
