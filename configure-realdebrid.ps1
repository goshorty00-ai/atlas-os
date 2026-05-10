# Configure Real-Debrid for Atlas Download Manager
Write-Host "=== Atlas Download Manager - Real-Debrid Configuration ===" -ForegroundColor Cyan
Write-Host ""

# Get token from user
$token = Read-Host "Enter your Real-Debrid API token (from https://real-debrid.com/apitoken)"

if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Host "No token provided. Exiting." -ForegroundColor Red
    exit 1
}

# Token store path
$tokenDir = "$env:APPDATA\AtlasAI\Downloader\tokens"
$tokenFile = "$tokenDir\RealDebrid.token"

# Create directory if it doesn't exist
if (!(Test-Path $tokenDir)) {
    New-Item -ItemType Directory -Path $tokenDir -Force | Out-Null
}

# Encrypt and save token using Windows DPAPI
try {
    $secureToken = ConvertTo-SecureString -String $token -AsPlainText -Force
    $encryptedToken = ConvertFrom-SecureString -SecureString $secureToken
    $encryptedToken | Out-File -FilePath $tokenFile -Encoding UTF8
    
    Write-Host ""
    Write-Host "✓ Real-Debrid token saved successfully!" -ForegroundColor Green
    Write-Host "  Location: $tokenFile" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Now restart Atlas and your downloads will use Real-Debrid." -ForegroundColor Cyan
} catch {
    Write-Host ""
    Write-Host "✗ Error saving token: $_" -ForegroundColor Red
    exit 1
}

# Also update settings.json to enable Real-Debrid
$settingsPath = "$env:APPDATA\AtlasAI\Downloader\settings.json"
try {
    $settings = @{
        maxParallelDownloads = 3
        resolverMode = "Auto"
        providers = @{
            RealDebrid = @{
                enabled = $true
            }
            AllDebrid = @{
                enabled = $false
            }
            Premiumize = @{
                enabled = $false
            }
        }
    }
    
    $settings | ConvertTo-Json -Depth 10 | Out-File -FilePath $settingsPath -Encoding UTF8
    Write-Host "✓ Settings updated to enable Real-Debrid" -ForegroundColor Green
} catch {
    Write-Host "⚠ Warning: Could not update settings.json: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
