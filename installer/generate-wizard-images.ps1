# Generate Inno Setup wizard images for OmenCore
# Creates modern, professional-looking installer images
# Requires: System.Drawing (included in .NET)

Add-Type -AssemblyName System.Drawing

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logoPath = Join-Path $scriptDir "..\src\OmenCoreApp\Assets\logo.png"
$largePath = Join-Path $scriptDir "wizard-large.bmp"
$smallPath = Join-Path $scriptDir "wizard-small.bmp"
$versionPath = Join-Path $scriptDir "..\VERSION.txt"

# Read version from VERSION.txt
if (Test-Path $versionPath) {
    $version = (Get-Content $versionPath -First 1).Trim()
    Write-Host "Version: v$version" -ForegroundColor Cyan
} else {
    $version = "2.3.1"
    Write-Host "Warning: VERSION.txt not found, using default v$version" -ForegroundColor Yellow
}

# Check if logo exists
if (-not (Test-Path $logoPath)) {
    Write-Host "Warning: Logo not found at $logoPath" -ForegroundColor Yellow
    Write-Host "Using fallback design..." -ForegroundColor Yellow
    $useLogo = $false
} else {
    $logo = [System.Drawing.Image]::FromFile($logoPath)
    $useLogo = $true
}

# ==================================
# CREATE LARGE WIZARD IMAGE (164x314)
# ==================================
$largeWidth = 164
$largeHeight = 314
$largeBmp = New-Object System.Drawing.Bitmap $largeWidth, $largeHeight
$largeGraphics = [System.Drawing.Graphics]::FromImage($largeBmp)
$largeGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$largeGraphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

# Modern dark gradient background (OMEN style dark blue/purple)
$gradientBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 0, 0),
    (New-Object System.Drawing.Point 0, $largeHeight),
    [System.Drawing.Color]::FromArgb(255, 18, 18, 32),    # Dark top
    [System.Drawing.Color]::FromArgb(255, 32, 28, 58)     # Slightly purple bottom
)
$largeGraphics.FillRectangle($gradientBrush, 0, 0, $largeWidth, $largeHeight)

# Subtle diagonal accent lines
$accentPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(30, 138, 43, 226), 1)  # Faint purple
for ($i = -50; $i -lt 400; $i += 25) {
    $largeGraphics.DrawLine($accentPen, 0, $i, $largeWidth, $i - 80)
}

# Logo or fallback
if ($useLogo) {
    $logoSize = 90
    $logoX = ($largeWidth - $logoSize) / 2
    $logoY = 50
    $largeGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $largeGraphics.DrawImage($logo, $logoX, $logoY, $logoSize, $logoSize)
} else {
    # Fallback: Draw stylized "O" icon
    $circleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 138, 43, 226))
    $largeGraphics.FillEllipse($circleBrush, 50, 55, 64, 64)
    $innerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 18, 18, 32))
    $largeGraphics.FillEllipse($innerBrush, 60, 65, 44, 44)
}

# "OmenCore" title
$titleFont = New-Object System.Drawing.Font("Segoe UI", 18, [System.Drawing.FontStyle]::Bold)
$titleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$titleRect = New-Object System.Drawing.RectangleF(0, 155, $largeWidth, 30)
$largeGraphics.DrawString("OmenCore", $titleFont, $titleBrush, $titleRect, $format)

# Version badge
$versionFont = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$versionBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 138, 43, 226))
$versionRect = New-Object System.Drawing.RectangleF(0, 185, $largeWidth, 20)
$largeGraphics.DrawString("v$version", $versionFont, $versionBrush, $versionRect, $format)

# Tagline
$tagFont = New-Object System.Drawing.Font("Segoe UI", 9)
$tagBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 160, 160, 180))
$tagRect = New-Object System.Drawing.RectangleF(0, 210, $largeWidth, 20)
$largeGraphics.DrawString("Gaming Laptop Control", $tagFont, $tagBrush, $tagRect, $format)

# Feature icons (small text hints) - using ASCII-safe symbols
$featureFont = New-Object System.Drawing.Font("Segoe UI", 8)
$featureBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 120, 120, 140))
$y = 250
$features = @("* Fan Control", "* Monitoring", "* Performance", "* RGB Lighting")
foreach ($feature in $features) {
    $featureRect = New-Object System.Drawing.RectangleF(0, $y, $largeWidth, 15)
    $largeGraphics.DrawString($feature, $featureFont, $featureBrush, $featureRect, $format)
    $y += 14
}

# Save large image
$largeBmp.Save($largePath, [System.Drawing.Imaging.ImageFormat]::Bmp)
Write-Host "[OK] Created: $largePath (164x314)" -ForegroundColor Green

# ==================================
# CREATE SMALL WIZARD IMAGE (55x55)
# ==================================
$smallSize = 55
$smallBmp = New-Object System.Drawing.Bitmap $smallSize, $smallSize
$smallGraphics = [System.Drawing.Graphics]::FromImage($smallBmp)
$smallGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

# Match large image background
$smallGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 0, 0),
    (New-Object System.Drawing.Point 0, $smallSize),
    [System.Drawing.Color]::FromArgb(255, 18, 18, 32),
    [System.Drawing.Color]::FromArgb(255, 32, 28, 58)
)
$smallGraphics.FillRectangle($smallGradient, 0, 0, $smallSize, $smallSize)

# Draw logo or fallback
if ($useLogo) {
    $smallGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $padding = 8
    $smallGraphics.DrawImage($logo, $padding, $padding, $smallSize - $padding * 2, $smallSize - $padding * 2)
} else {
    # Fallback: Draw stylized "O" icon
    $circleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 138, 43, 226))
    $smallGraphics.FillEllipse($circleBrush, 8, 8, 39, 39)
    $innerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 18, 18, 32))
    $smallGraphics.FillEllipse($innerBrush, 14, 14, 27, 27)
}

# Save small image
$smallBmp.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
Write-Host "[OK] Created: $smallPath (55x55)" -ForegroundColor Green

# Cleanup
if ($useLogo) { $logo.Dispose() }
$largeBmp.Dispose()
$smallBmp.Dispose()
$largeGraphics.Dispose()
$smallGraphics.Dispose()

Write-Host ""
Write-Host "[DONE] Wizard images generated successfully!" -ForegroundColor Cyan
Write-Host "   Large: wizard-large.bmp (164x314 px)" -ForegroundColor Gray
Write-Host "   Small: wizard-small.bmp (55x55 px)" -ForegroundColor Gray
