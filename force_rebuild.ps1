# Force rebuild Atlas with LibVLC support
Write-Host "===== ATLAS AI - FORCE REBUILD =====" -ForegroundColor Cyan
Write-Host ""

# Step 1: Kill all Atlas and VLC processes
Write-Host "Step 1: Killing all Atlas and VLC processes..." -ForegroundColor Yellow
Get-Process | Where-Object {$_.ProcessName -like "*Atlas*" -or $_.ProcessName -like "*vlc*"} | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

# Step 2: Delete locked DLLs
Write-Host "Step 2: Removing locked files..." -ForegroundColor Yellow
Remove-Item "bin\Debug\net8.0-windows\win-x64\*.dll" -Force -ErrorAction SilentlyContinue
Remove-Item "bin\Debug\net8.0-windows\win-x64\*.exe" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Step 3: Clean build
Write-Host "Step 3: Cleaning..." -ForegroundColor Yellow
dotnet clean AtlasAI.csproj | Out-Null
Start-Sleep -Seconds 1

# Step 4: Build
Write-Host "Step 4: Building with LibVLC support..." -ForegroundColor Yellow
$buildResult = dotnet build AtlasAI.csproj 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "BUILD SUCCESS!" -ForegroundColor Green
} else {
    Write-Host "BUILD FAILED!" -ForegroundColor Red
    Write-Host $buildResult
    Read-Host "Press Enter to exit"
    exit 1
}

# Step 5: Delete media cache
Write-Host "Step 5: Deleting media cache..." -ForegroundColor Yellow
$cacheFile = Join-Path $env:APPDATA "AtlasAI\media_index.json"
Remove-Item $cacheFile -Force -ErrorAction SilentlyContinue

# Step 6: Start Atlas
Write-Host ""
Write-Host "Step 6: Starting Atlas..." -ForegroundColor Green
Write-Host ""
Write-Host "AFTER ATLAS STARTS:" -ForegroundColor Cyan
Write-Host "1. Go to Media Centre" -ForegroundColor White
Write-Host "2. Click Configure Folders" -ForegroundColor White
Write-Host "3. Click Scan Now (will take 30-60 seconds)" -ForegroundColor White
Write-Host "4. Movies should show MKV files" -ForegroundColor White
Write-Host "5. Music should play FLAC/OGG files" -ForegroundColor White
Write-Host "6. NO external VLC window should open" -ForegroundColor White
Write-Host ""

Start-Process "bin\Debug\net8.0-windows\win-x64\Atlas.exe"
Write-Host "Atlas started!" -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to close this window"
