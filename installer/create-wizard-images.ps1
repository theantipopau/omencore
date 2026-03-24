# Create modern installer wizard images for OmenCore.
# Outputs:
# - wizard-large.bmp (164x314)
# - wizard-small.bmp (55x58)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$logoPath = Join-Path (Split-Path -Parent $scriptPath) 'src\OmenCoreApp\Assets\logo.png'

$omenRed = [System.Drawing.Color]::FromArgb(230, 0, 46)
$darkTop = [System.Drawing.Color]::FromArgb(14, 16, 26)
$darkBottom = [System.Drawing.Color]::FromArgb(8, 10, 18)
$accentBlue = [System.Drawing.Color]::FromArgb(69, 199, 255)
$white = [System.Drawing.Color]::White

function New-LinearBrush($x1, $y1, $x2, $y2, $c1, $c2) {
    return New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        [System.Drawing.Point]::new([int]$x1, [int]$y1),
        [System.Drawing.Point]::new([int]$x2, [int]$y2),
        [System.Drawing.Color]$c1,
        [System.Drawing.Color]$c2
    )
}

Write-Host 'Generating installer wizard images...' -ForegroundColor Cyan

$logo = $null
if (Test-Path $logoPath) {
    $logo = [System.Drawing.Image]::FromFile($logoPath)
    Write-Host "Using logo: $logoPath" -ForegroundColor Green
} else {
    Write-Host "Logo not found: $logoPath. Using fallback text-only artwork." -ForegroundColor Yellow
}

# Large image (left banner)
$largeBmp = New-Object System.Drawing.Bitmap 164, 314
$gLarge = [System.Drawing.Graphics]::FromImage($largeBmp)
$gLarge.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gLarge.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gLarge.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

$bgLarge = New-LinearBrush 0 0 0 314 ([System.Drawing.Color]$darkTop) ([System.Drawing.Color]$darkBottom)
$gLarge.FillRectangle($bgLarge, 0, 0, 164, 314)

# Subtle texture lines
$linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(20, 255, 255, 255), 1)
for ($y = 0; $y -lt 314; $y += 16) {
    $gLarge.DrawLine($linePen, 0, $y, 164, $y)
}

# Accent corners
$accentPen = New-Object System.Drawing.Pen($accentBlue, 2)
$gLarge.DrawLine($accentPen, 0, 0, 16, 0)
$gLarge.DrawLine($accentPen, 0, 0, 0, 16)
$gLarge.DrawLine($accentPen, 148, 0, 164, 0)
$gLarge.DrawLine($accentPen, 164, 0, 164, 16)

if ($logo -ne $null) {
    $logoSize = 84
    $logoX = [int]((164 - $logoSize) / 2)
    $logoY = 28

    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse($logoX - 16, $logoY - 16, $logoSize + 32, $logoSize + 32)
    $glowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
    $glowBrush.CenterColor = [System.Drawing.Color]::FromArgb(90, $omenRed)
    $glowBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $omenRed))
    $gLarge.FillPath($glowBrush, $glowPath)

    $gLarge.DrawImage($logo, $logoX, $logoY, $logoSize, $logoSize)

    $glowBrush.Dispose()
    $glowPath.Dispose()
}

$titleFont = New-Object System.Drawing.Font('Segoe UI', 22, [System.Drawing.FontStyle]::Bold)
$titleBrush = New-Object System.Drawing.SolidBrush($white)
$titleText = 'OMENCORE'
$titleSize = $gLarge.MeasureString($titleText, $titleFont)
$gLarge.DrawString($titleText, $titleFont, $titleBrush, (164 - $titleSize.Width) / 2, 128)

$tagFont = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Regular)
$tagBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(170, 255, 255, 255))
$tagText = 'Premium Control Suite'
$tagSize = $gLarge.MeasureString($tagText, $tagFont)
$gLarge.DrawString($tagText, $tagFont, $tagBrush, (164 - $tagSize.Width) / 2, 166)

$separatorPen = New-Object System.Drawing.Pen($omenRed, 2)
$gLarge.DrawLine($separatorPen, 28, 194, 136, 194)

$featureFont = New-Object System.Drawing.Font('Segoe UI', 7, [System.Drawing.FontStyle]::Regular)
$featureBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(190, 255, 255, 255))
$features = @(
    '- Fan Curves and Thermal Control',
    '- Real-time Hardware Monitoring',
    '- Performance and Power Modes',
    '- RGB and Profile Automation'
)
$featureY = 206
foreach ($feature in $features) {
    $size = $gLarge.MeasureString($feature, $featureFont)
    $gLarge.DrawString($feature, $featureFont, $featureBrush, (164 - $size.Width) / 2, $featureY)
    $featureY += 14
}

$largePath = Join-Path $scriptPath 'wizard-large.bmp'
$largeBmp.Save($largePath, [System.Drawing.Imaging.ImageFormat]::Bmp)

# Small image (top-right badge)
$smallBmp = New-Object System.Drawing.Bitmap 55, 58
$gSmall = [System.Drawing.Graphics]::FromImage($smallBmp)
$gSmall.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gSmall.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

$bgSmall = New-LinearBrush 0 0 55 58 ([System.Drawing.Color]::FromArgb(22, 24, 38)) ([System.Drawing.Color]::FromArgb(12, 14, 24))
$gSmall.FillRectangle($bgSmall, 0, 0, 55, 58)

$smallBorderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(110, $omenRed), 1)
$gSmall.DrawRectangle($smallBorderPen, 0, 0, 54, 57)
$gSmall.DrawLine($accentPen, 0, 0, 10, 0)
$gSmall.DrawLine($accentPen, 0, 0, 0, 10)

if ($logo -ne $null) {
    $smallLogoSize = 40
    $smallLogoX = [int]((55 - $smallLogoSize) / 2)
    $smallLogoY = [int]((58 - $smallLogoSize) / 2)
    $gSmall.DrawImage($logo, $smallLogoX, $smallLogoY, $smallLogoSize, $smallLogoSize)
} else {
    $fallbackFont = New-Object System.Drawing.Font('Segoe UI', 8, [System.Drawing.FontStyle]::Bold)
    $fallbackBrush = New-Object System.Drawing.SolidBrush($white)
    $gSmall.DrawString('OC', $fallbackFont, $fallbackBrush, 15, 20)
    $fallbackFont.Dispose()
    $fallbackBrush.Dispose()
}

$smallPath = Join-Path $scriptPath 'wizard-small.bmp'
$smallBmp.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)

# Cleanup
if ($logo -ne $null) { $logo.Dispose() }
$bgLarge.Dispose()
$bgSmall.Dispose()
$linePen.Dispose()
$accentPen.Dispose()
$smallBorderPen.Dispose()
$titleFont.Dispose()
$titleBrush.Dispose()
$tagFont.Dispose()
$tagBrush.Dispose()
$separatorPen.Dispose()
$featureFont.Dispose()
$featureBrush.Dispose()
$gLarge.Dispose()
$gSmall.Dispose()
$largeBmp.Dispose()
$smallBmp.Dispose()

Write-Host "Created: $largePath" -ForegroundColor Green
Write-Host "Created: $smallPath" -ForegroundColor Green
Write-Host 'Installer wizard visual assets refreshed.' -ForegroundColor Green
