# This script wipes stale web assets and copies the latest React builds to the runtime directories
# used by Atlas. It keeps the Servers, Music, and Addons WebViews in sync with their source bundles.

$targets = @(
	@{
		Name = "Media Streamer"
		Source = "D:\Atlas.OS\Figma\Media Streamer\dist"
		Destinations = @(
			"D:\Atlas.OS\bin\x64\Figma\Media Streamer\dist",
			"D:\Atlas.OS\bin\publish-fixed\Figma\Media Streamer\dist",
			"D:\Atlas.OS\bin\Debug\net8.0-windows\Figma\Media Streamer\dist"
		)
	},
	@{
		Name = "Music_MediaHub"
		Source = "D:\Atlas.OS\Figma\Music_MediaHub\dist"
		Destinations = @(
			"D:\Atlas.OS\bin\x64\Figma\Music_MediaHub\dist",
			"D:\Atlas.OS\bin\publish-fixed\Figma\Music_MediaHub\dist",
			"D:\Atlas.OS\bin\Debug\net8.0-windows\Figma\Music_MediaHub\dist"
		)
	},
	@{
		Name = "Addon Manager"
		Source = "D:\Atlas.OS\Figma\Addon Manager\dist"
		Destinations = @(
			"D:\Atlas.OS\bin\x64\Figma\Addon Manager\dist",
			"D:\Atlas.OS\bin\publish-fixed\Figma\Addon Manager\dist",
			"D:\Atlas.OS\bin\Debug\net8.0-windows\Figma\Addon Manager\dist"
		)
	}
)

function Sync-WebViewDist {
	param(
		[string]$Source,
		[string]$Destination,
		[string]$Name
	)

	if (-not (Test-Path $Source)) {
		throw "Source dist folder not found for ${Name}: ${Source}"
	}

	Write-Host "Removing old $Name web assets from $Destination..."
	if (Test-Path $Destination) {
		Get-ChildItem -Path $Destination -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
	} else {
		New-Item -ItemType Directory -Path $Destination -Force | Out-Null
	}

	Write-Host "Copying new $Name build from $Source to $Destination..."
	Copy-Item -Path "$Source\*" -Destination $Destination -Recurse -Force
}

foreach ($target in $targets) {
	foreach ($destination in $target.Destinations) {
		Sync-WebViewDist -Source $target.Source -Destination $destination -Name $target.Name
	}
}

Write-Host "Done. The latest Servers, Music, and Addons web bundles are now in the x64, publish, and debug runtime directories."
