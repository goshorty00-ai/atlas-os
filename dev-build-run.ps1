$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path $root "bin\\x64"
$exe = Join-Path $outDir "Atlas_v2.exe"
$dll = Join-Path $outDir "Atlas_v2.dll"

Get-Process -Name "Atlas_v2" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "Atlas" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

dotnet build ".\\AtlasAI.csproj" -c Debug -p:UseAppHost=true -p:BuildEmbeddedUi=false

if (!(Test-Path $dll) -and !(Test-Path $exe)) {
  throw "Build finished but Atlas output was not found at: $outDir"
}

if (Test-Path $dll) {
  Start-Process -FilePath "dotnet" -ArgumentList @($dll) -WorkingDirectory $outDir
  exit 0
}

Start-Process -FilePath $exe -WorkingDirectory $outDir

