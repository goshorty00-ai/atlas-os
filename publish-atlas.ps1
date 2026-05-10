param(
	[string]$ObfuscatorCommand = "",
	[string]$ObfuscatorArguments = "",
	[switch]$SingleFile,
	[switch]$SkipLaunch
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildOutput = Join-Path $root "bin\x64"
$publishDir = if ($SingleFile) {
	Join-Path $root "_builds\private_singlefile"
} else {
	Join-Path $root "_builds\private_selfcontained"
}

if ([string]::IsNullOrWhiteSpace($ObfuscatorCommand)) {
	$ObfuscatorCommand = $env:ATLAS_OBFUSCATOR_CMD
}

if ([string]::IsNullOrWhiteSpace($ObfuscatorArguments)) {
	$ObfuscatorArguments = $env:ATLAS_OBFUSCATOR_ARGS
}

Get-Process -Name "Atlas_v2" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "Atlas" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $buildOutput) {
	Get-ChildItem -Path $buildOutput -Force -ErrorAction SilentlyContinue |
	  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $publishDir) {
	Get-ChildItem -Path $publishDir -Force -ErrorAction SilentlyContinue |
	  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
	New-Item -ItemType Directory -Path $publishDir | Out-Null
}

$publishSingleFile = if ($SingleFile) { "true" } else { "false" }
$atlasLeanSingleFile = $publishSingleFile

dotnet publish ".\AtlasAI.csproj" -c Release -r win-x64 --self-contained true -p:UseAppHost=true -p:BuildEmbeddedUi=true -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=$publishSingleFile -p:AtlasLeanSingleFile=$atlasLeanSingleFile

$exePath = Join-Path $buildOutput "Atlas_v2.exe"
if (-not (Test-Path $exePath)) {
	throw ("Expected build output not found: " + $exePath)
}

Copy-Item -Path (Join-Path $buildOutput "*") -Destination $publishDir -Recurse -Force

Get-ChildItem -Path $publishDir -Filter *.pdb -Recurse -ErrorAction SilentlyContinue |
	Remove-Item -Force -ErrorAction SilentlyContinue

if (-not [string]::IsNullOrWhiteSpace($ObfuscatorCommand)) {
	Write-Host ("Running protection tool: " + $ObfuscatorCommand)

	$rawObfuscatorArguments = $ObfuscatorArguments
	if ($null -eq $rawObfuscatorArguments) {
		$rawObfuscatorArguments = ""
	}

	$resolvedArgs = $rawObfuscatorArguments.Replace('{publishDir}', $publishDir)
	$argList = @()
	if (-not [string]::IsNullOrWhiteSpace($resolvedArgs)) {
		$argList += $resolvedArgs
	}

	& $ObfuscatorCommand @argList
	if ($LASTEXITCODE -ne 0) {
		throw ("Protection tool failed with exit code " + $LASTEXITCODE)
	}
}

Write-Host ("Published to: " + $publishDir)
Write-Host ("Run: " + (Join-Path $publishDir "Atlas_v2.exe"))
Write-Host ("Verified build output: " + $buildOutput)

if (-not $SkipLaunch) {
	Start-Process (Join-Path $publishDir "Atlas_v2.exe")
}
