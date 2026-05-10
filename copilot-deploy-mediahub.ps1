$ErrorActionPreference = 'Stop'
$src = "D:\My Apps\AOS\New build\Atlas.OS\Figma\Mediahub\dist"
$dstRuntime = "D:\My Apps\AOS\New build\Atlas.OS\bin\x64\Figma\Media Streamer\dist"
$dstSource = "D:\My Apps\AOS\New build\Atlas.OS\Figma\Media Streamer\dist"

if (!(Test-Path $src)) { throw "Source dist not found: $src" }

Copy-Item -Path (Join-Path $src '*') -Destination $dstRuntime -Recurse -Force
Copy-Item -Path (Join-Path $src '*') -Destination $dstSource -Recurse -Force

Write-Output "Deployed Mediahub dist to runtime and source folders."
