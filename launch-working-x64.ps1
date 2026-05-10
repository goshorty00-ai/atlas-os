$ErrorActionPreference = "Stop"

$exe = "D:\My Apps\AOS\New build\Atlas.OS\bin\x64\Atlas_v2.exe"
$workDir = "D:\My Apps\AOS\New build\Atlas.OS\bin\x64"

if (-not (Test-Path $exe)) {
    Write-Error "Working exe not found: $exe"
    exit 1
}

Write-Host "Launching: $exe" -ForegroundColor Cyan
Start-Process -FilePath $exe -WorkingDirectory $workDir
Write-Host "Atlas launched." -ForegroundColor Green
