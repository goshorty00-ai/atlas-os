# Stop any running Atlas instances and rebuild/run

Write-Host "Stopping Atlas..." -ForegroundColor Yellow

# Kill any running Atlas processes
Get-Process -Name "Atlas_v2" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name "Atlas" -ErrorAction SilentlyContinue | Stop-Process -Force

Start-Sleep -Seconds 2

Write-Host "Building Atlas..." -ForegroundColor Cyan
dotnet build AtlasAI.csproj -c Debug

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Build successful! Starting Atlas..." -ForegroundColor Green
    Start-Process "bin\x64\Atlas_v2.exe"
    Write-Host "Atlas is starting..." -ForegroundColor Green
} else {
    Write-Host "`n❌ Build failed!" -ForegroundColor Red
    exit 1
}
