$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishFixedDir = Join-Path $root "bin\\publish-fixed"
$publishDir = Join-Path $root "bin\\publish"
$x64Dir = Join-Path $root "bin\\x64"
$x64MediaStreamerDist = Join-Path $x64Dir "Figma\Media Streamer\dist\index.html"
$publishFixedExe = Join-Path $publishFixedDir "Atlas_v2.exe"
$publishExe = Join-Path $publishDir "Atlas_v2.exe"
$x64Exe = Join-Path $x64Dir "Atlas_v2.exe"
$x64Dll = Join-Path $x64Dir "Atlas_v2.dll"

function Test-LaunchableFile {
  param([string]$Path)

  if (-not (Test-Path $Path)) {
    return $false
  }

  try {
    $item = Get-Item $Path -ErrorAction Stop
    return $item.Length -gt 0
  }
  catch {
    return $false
  }
}

if ((Test-LaunchableFile $x64Exe) -and (Test-Path $x64MediaStreamerDist)) {
  Start-Process -FilePath $x64Exe -WorkingDirectory $x64Dir
  exit 0
}

if ((Test-Path $x64Dll) -and (Test-Path $x64MediaStreamerDist)) {
  Start-Process -FilePath "dotnet" -ArgumentList @($x64Dll) -WorkingDirectory $x64Dir
  exit 0
}

if (Test-LaunchableFile $publishFixedExe) {
  Start-Process -FilePath $publishFixedExe -WorkingDirectory $publishFixedDir
  exit 0
}

if (Test-LaunchableFile $publishExe) {
  Start-Process -FilePath $publishExe -WorkingDirectory $publishDir
  exit 0
}

if (Test-Path $x64Dll) {
  Start-Process -FilePath "dotnet" -ArgumentList @($x64Dll) -WorkingDirectory $x64Dir
  exit 0
}

if (Test-LaunchableFile $x64Exe) {
  Start-Process -FilePath $x64Exe -WorkingDirectory $x64Dir
  exit 0
}

throw "Build output not found. Run: dotnet build .\\AtlasAI.csproj -c Debug"
