# Generate Inno Setup wizard images from OmenCore logo
# Requires: System.Drawing (included in .NET)

Add-Type -AssemblyName System.Drawing

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logoPath = Join-Path $scriptDir "..\src\OmenCoreApp\Assets\logo.png"
$largePath = Join-Path $scriptDir "wizard-large.bmp"
$smallPath = Join-Path $scriptDir "wizard-small.bmp"

# Load the logo
$logo = [System.Drawing.Image]::FromFile($logoPath)

# Create large wizard image (164x314 pixels for modern style)
$largeWidth = 164
$largeHeight = 314
$largeBmp = New-Object System.Drawing.Bitmap $largeWidth, $largeHeight
$largeGraphics = [System.Drawing.Graphics]::FromImage($largeBmp)

# Dark gradient background
$brush1 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point 0, 0),
    (New-Object System.Drawing.Point 0, $largeHeight),
    [System.Drawing.Color]::FromArgb(255, 26, 26, 46),
    [System.Drawing.Color]::FromArgb(255, 22, 33, 62)
)
$largeGraphics.FillRectangle($brush1, 0, 0, $largeWidth, $largeHeight)

# Add logo centered
$logoSize = 100
$logoX = ($largeWidth - $logoSize) / 2
$logoY = 60
$largeGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$largeGraphics.DrawImage($logo, $logoX, $logoY, $logoSize, $logoSize)

# Add "OmenCore" text
$font = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$textRect = New-Object System.Drawing.RectangleF(0, 180, $largeWidth, 30)
$largeGraphics.DrawString("OmenCore", $font, $textBrush, $textRect, $format)

# Add tagline
$smallFont = New-Object System.Drawing.Font("Segoe UI", 9)
$grayBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 180, 180, 180))
$taglineRect = New-Object System.Drawing.RectangleF(0, 210, $largeWidth, 20)
$largeGraphics.DrawString("Gaming Laptop Control", $smallFont, $grayBrush, $taglineRect, $format)

# Save large image
$largeBmp.Save($largePath, [System.Drawing.Imaging.ImageFormat]::Bmp)
Write-Host "Created: $largePath"

# Create small wizard image (55x55 pixels)
$smallSize = 55
$smallBmp = New-Object System.Drawing.Bitmap $smallSize, $smallSize
$smallGraphics = [System.Drawing.Graphics]::FromImage($smallBmp)

# Dark background
$darkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 26, 26, 46))
$smallGraphics.FillRectangle($darkBrush, 0, 0, $smallSize, $smallSize)

# Draw logo
$smallGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$padding = 6
$smallGraphics.DrawImage($logo, $padding, $padding, $smallSize - $padding * 2, $smallSize - $padding * 2)

# Save small image
$smallBmp.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
Write-Host "Created: $smallPath"

# Cleanup
$logo.Dispose()
$largeBmp.Dispose()
$smallBmp.Dispose()
$largeGraphics.Dispose()
$smallGraphics.Dispose()

Write-Host "`nWizard images generated successfully!"
Write-Host "Large: 164x314 px"
Write-Host "Small: 55x55 px"
