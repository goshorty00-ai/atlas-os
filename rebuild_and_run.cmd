@echo off
echo ===== ATLAS AI - CLEAN REBUILD AND RUN =====
echo.
echo Step 1: Killing any running Atlas processes...
taskkill /F /IM Atlas.exe 2>nul
timeout /t 2 /nobreak >nul

echo Step 2: Cleaning build...
dotnet clean AtlasAI.csproj
timeout /t 1 /nobreak >nul

echo Step 3: Building with LibVLC support...
dotnet build AtlasAI.csproj --no-incremental

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED!
    pause
    exit /b 1
)

echo.
echo Step 4: Deleting media cache to force fresh scan...
del "%APPDATA%\AtlasAI\media_index.json" 2>nul

echo.
echo Step 5: Starting Atlas...
echo.
echo IMPORTANT: After Atlas starts:
echo 1. Go to Media Centre
echo 2. Click Configure Folders
echo 3. Click Scan Now
echo 4. Wait for REAL scan (30-60 seconds)
echo 5. Movies should now show MKV files
echo 6. Music should play FLAC/OGG files
echo.
start "" "bin\Debug\net8.0-windows\win-x64\Atlas.exe"

echo.
echo Atlas is starting...
echo.
pause
