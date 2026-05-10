$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Get-Process Atlas_v2 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*Atlas*" } | Stop-Process -Force -ErrorAction SilentlyContinue

$atlasWebViewPattern = [regex]::Escape((Join-Path $root "bin\x64\Atlas_v2.exe.WebView2"))
Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
    Where-Object {
        $commandLine = $_.CommandLine
        $executablePath = $_.ExecutablePath
        ($commandLine -and $commandLine -match $atlasWebViewPattern) -or
        ($executablePath -and $executablePath -like "*WebView2*")
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

dotnet build (Join-Path $root "AtlasAI.csproj") -c Debug -r win-x64 -v:m
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$x64Dir = Join-Path $root "bin\x64"
$outDir = $x64Dir
$exe = Join-Path $outDir "Atlas_v2.exe"
$dll = Join-Path $outDir "Atlas_v2.dll"

if (Test-Path $dll) {
    $logDir = Join-Path $root "bin\_run_logs"
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $logOut = Join-Path $logDir "atlas_stdout.log"
    $logErr = Join-Path $logDir "atlas_stderr.log"

    Start-Process -WorkingDirectory $outDir -FilePath "dotnet" -ArgumentList @("`"$dll`"") -WindowStyle Hidden -RedirectStandardOutput $logOut -RedirectStandardError $logErr
    exit 0
}

if (Test-Path $exe) {
    Start-Process -WorkingDirectory $outDir -FilePath $exe
    exit 0
}

if (!(Test-Path $dll) -and !(Test-Path $exe)) { throw "Build output missing: $dll" }
