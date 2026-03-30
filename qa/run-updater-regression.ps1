param(
    [int]$Runs = 3,
    [string]$ReleaseApiUrl = "https://api.github.com/repos/theantipopau/omencore/releases/latest",
    [string]$ArtifactsDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http

if ([string]::IsNullOrWhiteSpace($ArtifactsDir)) {
    $root = Split-Path -Parent $PSScriptRoot
    $ArtifactsDir = Join-Path $root "artifacts"
}

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

$http = [System.Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(60)
$http.DefaultRequestHeaders.UserAgent.ParseAdd("OmenCore-UpdaterRegression")
$http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")

function Get-HeadInfo {
    param([string]$Url)

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Head, $Url)
    $response = $http.SendAsync($request).GetAwaiter().GetResult()
    return [ordered]@{
        StatusCode = [int]$response.StatusCode
        ContentType = if ($response.Content.Headers.ContentType) { [string]$response.Content.Headers.ContentType } else { "" }
    }
}

function Select-AssetForMode {
    param(
        [object[]]$Assets,
        [string]$Mode
    )

    $installer = $null
    $portable = $null
    $fallbackExe = $null

    foreach ($asset in $Assets) {
        $name = [string]$asset.name
        $lower = $name.ToLowerInvariant()

        if (($lower.Contains("setup") -or $lower.Contains("installer")) -and $lower.EndsWith(".exe")) {
            $installer = $asset
        }
        elseif ($lower.Contains("portable") -or ($lower.EndsWith(".zip") -and -not $lower.Contains("source"))) {
            $portable = $asset
        }
        elseif ($lower.EndsWith(".exe") -and $null -eq $fallbackExe) {
            $fallbackExe = $asset
        }
    }

    switch ($Mode) {
        "Installer" {
            if ($null -ne $installer) { return $installer }
            if ($null -ne $fallbackExe) { return $fallbackExe }
            return $null
        }
        "Portable" {
            if ($null -ne $portable) { return $portable }
            return $null
        }
        default {
            return $null
        }
    }
}

function Get-AssetSignature {
    param([string]$Url)

    try {
        $raw = $http.GetByteArrayAsync($Url).GetAwaiter().GetResult()

        if ($raw.Length -lt 2) {
            return "UNKNOWN"
        }

        if ($raw[0] -eq 0x4D -and $raw[1] -eq 0x5A) { return "MZ" }
        if ($raw[0] -eq 0x50 -and $raw[1] -eq 0x4B) { return "PK" }
        return ("{0:X2}{1:X2}" -f $raw[0], $raw[1])
    }
    catch {
        return "ERROR: $($_.Exception.Message)"
    }
}

$results = @()

for ($i = 1; $i -le $Runs; $i++) {
    Write-Host "Updater regression run $i/$Runs..." -ForegroundColor Cyan

    $releaseJson = $http.GetStringAsync($ReleaseApiUrl).GetAwaiter().GetResult()
    $release = $releaseJson | ConvertFrom-Json
    $assets = @($release.assets)

    if ($assets.Count -eq 0) {
        throw "Release contains no assets."
    }

    $installerAsset = Select-AssetForMode -Assets $assets -Mode "Installer"
    $portableAsset = Select-AssetForMode -Assets $assets -Mode "Portable"

    if ($null -eq $installerAsset) {
        throw "Installed-path selection failed: no installer/fallback .exe selected."
    }
    if ($null -eq $portableAsset) {
        throw "Portable-path selection failed: no .zip portable asset selected."
    }

    $installerName = [string]$installerAsset.name
    $portableName = [string]$portableAsset.name

    if (-not $installerName.ToLowerInvariant().EndsWith(".exe")) {
        throw "Installed-path contract failed: selected asset is not .exe ($installerName)."
    }
    if (-not $portableName.ToLowerInvariant().EndsWith(".zip")) {
        throw "Portable-path contract failed: selected asset is not .zip ($portableName)."
    }

    $installerHead = Get-HeadInfo -Url $installerAsset.browser_download_url
    $portableHead = Get-HeadInfo -Url $portableAsset.browser_download_url

    $installerSig = Get-AssetSignature -Url $installerAsset.browser_download_url
    $portableSig = Get-AssetSignature -Url $portableAsset.browser_download_url

    $runResult = [ordered]@{
        run = $i
        timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
        releaseTag = [string]$release.tag_name
        installedPath = [ordered]@{
            assetName = $installerName
            statusCode = [int]$installerHead.StatusCode
            contentType = [string]$installerHead.ContentType
            signature = $installerSig
        }
        portablePath = [ordered]@{
            assetName = $portableName
            statusCode = [int]$portableHead.StatusCode
            contentType = [string]$portableHead.ContentType
            signature = $portableSig
        }
    }

    $results += $runResult

    if ($installerSig -notlike "MZ") {
        throw "Installed-path binary signature check failed for $installerName (got: $installerSig)."
    }
    if ($portableSig -notlike "PK") {
        throw "Portable-path archive signature check failed for $portableName (got: $portableSig)."
    }

    Start-Sleep -Seconds 2
}

$report = [ordered]@{
    runs = $Runs
    passed = $true
    checkedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    results = $results
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$jsonPath = Join-Path $ArtifactsDir "updater-regression-$stamp.json"
$txtPath = Join-Path $ArtifactsDir "updater-regression-$stamp.txt"

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8

$lines = @()
$lines += "Updater Regression Report"
$lines += "Generated: $((Get-Date).ToUniversalTime().ToString('o'))"
$lines += "Runs: $Runs"
$lines += ""
foreach ($r in $results) {
    $lines += "Run $($r.run) - Release $($r.releaseTag)"
    $lines += "  Installed: $($r.installedPath.assetName) | HTTP $($r.installedPath.statusCode) | CT=$($r.installedPath.contentType) | Sig=$($r.installedPath.signature)"
    $lines += "  Portable:  $($r.portablePath.assetName) | HTTP $($r.portablePath.statusCode) | CT=$($r.portablePath.contentType) | Sig=$($r.portablePath.signature)"
    $lines += ""
}
$lines | Set-Content -Path $txtPath -Encoding UTF8

Write-Host "Updater regression automation passed." -ForegroundColor Green
Write-Host "JSON: $jsonPath" -ForegroundColor Green
Write-Host "TXT:  $txtPath" -ForegroundColor Green

$http.Dispose()
