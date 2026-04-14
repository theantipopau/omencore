[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidAssignmentToAutomaticVariable', 'Args', Justification='False positive on external command invocation in version probe.')]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Runtime,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactsDir,

    [Parameter(Mandatory = $true)]
    [string]$CliPath,

    [Parameter(Mandatory = $true)]
    [string]$GuiPath,

    [switch]$SkipBinaryExecution
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-VersionFromOutput {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $versionMatch = [regex]::Match($Text, 'v?(\d+\.\d+\.\d+)')
    if ($versionMatch.Success) {
        return $versionMatch.Groups[1].Value
    }

    return $null
}

function Convert-ToWslPath {
    param([string]$WindowsPath)

    $wslpath = Get-Command wsl -ErrorAction SilentlyContinue
    if (-not $wslpath) {
        return $null
    }

    try {
        $resolved = (Resolve-Path $WindowsPath).Path
        $converted = (& wsl wslpath -a $resolved 2>$null | Select-Object -First 1)
        if ([string]::IsNullOrWhiteSpace($converted)) {
            return $null
        }
        return $converted.Trim()
    }
    catch {
        return $null
    }
}

function Get-ReportedVersion {
    param([string]$BinaryPath)

    if (-not (Test-Path $BinaryPath)) {
        throw "Binary not found: $BinaryPath"
    }

    if ($SkipBinaryExecution) {
        return $null
    }

    # Linux host: execute directly.
    if ($IsLinux) {
        $output = & $BinaryPath --version 2>&1 | Out-String
        return Get-VersionFromOutput -Text $output
    }

    # Windows host: try WSL execution.
    $wslBinary = Convert-ToWslPath -WindowsPath $BinaryPath
    if ($null -eq $wslBinary) {
        throw "Binary execution verifier requires Linux host or WSL. Use -SkipBinaryExecution to bypass on local Windows."
    }

    $cmd = "$wslBinary --version"
    $output = (& wsl -- bash -lc $cmd 2>&1 | Out-String)
    return Get-VersionFromOutput -Text $output
}

$zipName = "OmenCore-$Version-$Runtime.zip"
$zipPath = Join-Path $ArtifactsDir $zipName
$manifestPath = Join-Path $ArtifactsDir "version.json"

if (-not (Test-Path $zipPath)) {
    throw "Expected package not found: $zipPath"
}

if (-not (Test-Path $manifestPath)) {
    throw "Expected manifest not found: $manifestPath"
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if ($manifest.version -ne $Version) {
    throw "Manifest version mismatch. Expected '$Version', got '$($manifest.version)'"
}

if ($manifest.packageFile -ne $zipName) {
    throw "Manifest packageFile mismatch. Expected '$zipName', got '$($manifest.packageFile)'"
}

$cliVersion = Get-ReportedVersion -BinaryPath $CliPath
$guiVersion = Get-ReportedVersion -BinaryPath $GuiPath

if (-not $SkipBinaryExecution) {
    if ([string]::IsNullOrWhiteSpace($cliVersion)) {
        throw "Could not parse CLI reported version from --version output."
    }
    if ([string]::IsNullOrWhiteSpace($guiVersion)) {
        throw "Could not parse GUI reported version from --version output."
    }

    if ($cliVersion -ne $Version) {
        throw "CLI reported version mismatch. Expected '$Version', got '$cliVersion'"
    }

    if ($guiVersion -ne $Version) {
        throw "GUI reported version mismatch. Expected '$Version', got '$guiVersion'"
    }
}

$report = [ordered]@{
    version = $Version
    runtime = $Runtime
    zipFile = $zipName
    manifestVersion = $manifest.version
    cliReportedVersion = $cliVersion
    guiReportedVersion = $guiVersion
    binaryExecutionSkipped = [bool]$SkipBinaryExecution
    verifiedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}

$reportPath = Join-Path $ArtifactsDir "linux-version-verification-$Version-$Runtime.json"
$report | ConvertTo-Json -Depth 4 | Set-Content -Path $reportPath -Encoding UTF8

Write-Host "Linux package version verification passed." -ForegroundColor Green
Write-Host "Report: $reportPath" -ForegroundColor Green
