# Download LibreHardwareMonitor for bundling with OmenCore installer
# Run this before building the installer

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$LibreHWVersion = "0.9.3"
$DownloadUrl = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v$LibreHWVersion/LibreHardwareMonitor-net472.zip"
$ZipPath = "$PSScriptRoot\LibreHardwareMonitor.zip"
$ExtractPath = "$PSScriptRoot\LibreHardwareMonitor"

Write-Host "Downloading LibreHardwareMonitor v$LibreHWVersion..." -ForegroundColor Cyan

# Clean up old files
if (Test-Path $ExtractPath) {
    Remove-Item $ExtractPath -Recurse -Force
}
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

# Download
try {
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $ZipPath -UseBasicParsing
    Write-Host "✓ Downloaded successfully" -ForegroundColor Green
}
catch {
    Write-Host "✗ Failed to download: $_" -ForegroundColor Red
    exit 1
}

# Extract
try {
    Expand-Archive -Path $ZipPath -DestinationPath $ExtractPath -Force
    Write-Host "✓ Extracted to $ExtractPath" -ForegroundColor Green
}
catch {
    Write-Host "✗ Failed to extract: $_" -ForegroundColor Red
    exit 1
}

# Clean up zip
Remove-Item $ZipPath -Force

Write-Host ""
Write-Host "LibreHardwareMonitor is ready for bundling!" -ForegroundColor Green
Write-Host "You can now run build-installer.ps1" -ForegroundColor Cyan
