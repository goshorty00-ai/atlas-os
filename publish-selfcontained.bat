@echo off
echo Publishing Atlas as self-contained application...
echo.

set "BUILD_OUTPUT=bin\x64"
set "RELEASE_DIR=_builds\private_selfcontained"

REM Stop running Atlas before replacing build output
taskkill /F /IM Atlas_v2.exe >nul 2>&1
taskkill /F /IM Atlas.exe >nul 2>&1

REM Clean previous builds
if exist "%BUILD_OUTPUT%" (
    echo Cleaning previous build output...
    rmdir /s /q "%BUILD_OUTPUT%"
)

if exist "%RELEASE_DIR%" (
    echo Cleaning previous release bundle...
    rmdir /s /q "%RELEASE_DIR%"
)

mkdir "%RELEASE_DIR%"

REM Publish with embedded runtime to the project's verified default output path
dotnet publish AtlasAI.csproj -c Release -r win-x64 --self-contained true -p:BuildEmbeddedUi=true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:AtlasLeanSingleFile=true

if %ERRORLEVEL% EQU 0 (
    if not exist "%BUILD_OUTPUT%\Atlas_v2.exe" (
        echo.
        echo Build completed but %BUILD_OUTPUT%\Atlas_v2.exe was not created.
        pause
        exit /b 1
    )

    xcopy "%BUILD_OUTPUT%\*" "%RELEASE_DIR%\" /E /I /Y >nul

    echo.
    echo Build successful!
    echo Verified output: %BUILD_OUTPUT%\Atlas_v2.exe
    echo Release bundle: %RELEASE_DIR%\Atlas_v2.exe
    echo.
    echo This executable includes the .NET runtime and can run on any Windows x64 system.
    pause
) else (
    echo.
    echo ❌ Build failed!
    pause
    exit /b 1
)
