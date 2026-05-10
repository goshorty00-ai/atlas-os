$src = "D:\My Apps\AOS\New build\Atlas.OS\bin\x64"
$dst = "D:\My Apps\AOS\New build\Atlas.OS_BACKUP_WORKING_X64_2026-05-10"

if (Test-Path $dst) {
    Write-Host "BACKUP_ALREADY_EXISTS"
    exit 1
}

Write-Host "BACKUP_NOT_FOUND_STARTING_COPY"
Copy-Item -Path $src -Destination $dst -Recurse -Force
Write-Host "COPY_COMPLETE"

# Verify
$exeOk   = Test-Path (Join-Path $dst "Atlas_v2.exe")
$dllOk   = Test-Path (Join-Path $dst "Atlas_v2.dll")
$figmaOk = Test-Path (Join-Path $dst "Figma")
$msOk    = Test-Path (Join-Path $dst "Figma\Media Streamer\dist\index.html")
$amOk    = Test-Path (Join-Path $dst "Figma\Addon Manager\dist\index.html")

Write-Host "Atlas_v2.exe: $exeOk"
Write-Host "Atlas_v2.dll: $dllOk"
Write-Host "Figma folder: $figmaOk"
Write-Host "Media Streamer dist: $msOk"
Write-Host "Addon Manager dist: $amOk"
