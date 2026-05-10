@echo off
echo ========================================
echo ATLAS AI - BUILD AND RUN
echo ========================================
echo.

set "ATLAS_NO_PAUSE=1"
if /I "%~1"=="/pause" set "ATLAS_NO_PAUSE=0"

echo Killing Atlas and VLC processes...
taskkill /F /IM Atlas.exe 2>nul
taskkill /F /IM Atlas_v2.exe 2>nul
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { $_.CommandLine -like '*Atlas_v2.dll*' -or $_.CommandLine -like '*Atlas.dll*' } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force } catch {} }" >nul 2>nul
taskkill /F /IM vlc.exe 2>nul
timeout /t 3 /nobreak >nul

echo Building...
dotnet build AtlasAI.csproj

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED!
    if "%ATLAS_NO_PAUSE%"=="0" pause
    exit /b 1
)

echo.
echo Deleting media cache...
del "%APPDATA%\AtlasAI\media_index.json" 2>nul

echo.
echo Starting Atlas...
dotnet run --project AtlasAI.csproj

echo.
echo Atlas started!
if "%ATLAS_NO_PAUSE%"=="0" pause
