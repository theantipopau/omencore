param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$publishRoot = Join-Path $root "publish"
$publishDir = Join-Path $publishRoot $Runtime
$artifactsDir = Join-Path $root "artifacts"
$versionFile = Join-Path $root "VERSION.txt"

if (-not (Test-Path $versionFile)) {
    throw "VERSION.txt not found."
}

$version = (Get-Content $versionFile | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION.txt must contain a semantic version string."
}

# Build assembly version (major.minor.patch.0) for embedding in Windows PE headers
$versionParts = (($version -split '-')[0]) -split '\.'
$assemblyVersion = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2]).0"

Write-Host "Building OmenCore $version ($Configuration/$Runtime)" -ForegroundColor Cyan

# Linux packaging is handled by build-linux-package.ps1 so this script remains Windows-only.
if ($Runtime -like "linux*") {
    throw "Linux packaging is handled by build-linux-package.ps1. Use that script for linux runtimes."
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (-not (Test-Path $publishRoot)) { New-Item $publishRoot -ItemType Directory | Out-Null }
if (-not (Test-Path $artifactsDir)) { New-Item $artifactsDir -ItemType Directory | Out-Null }

$appProject = "src/OmenCoreApp/OmenCoreApp.csproj"

$publishArgs = @(
    $appProject,
    "--configuration", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:PublishSingleFile=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-p:Version=$version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    "-o", $publishDir
)

# SingleFile is now always enabled for proper .NET embedding
Write-Host "Building self-contained single-file executable with embedded .NET runtime..." -ForegroundColor Yellow

& dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "App publish failed with exit code $LASTEXITCODE"
}

# Build and publish the hardware worker (out-of-process monitoring for crash isolation)
Write-Host "Building hardware worker process..." -ForegroundColor Yellow
$workerPublishArgs = @(
    "src/OmenCore.HardwareWorker/OmenCore.HardwareWorker.csproj",
    "--configuration", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishTrimmed=false",
    "-p:PublishSingleFile=true",
    "-p:Version=$version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    "-o", $publishDir
)
& dotnet publish @workerPublishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Hardware worker publish failed with exit code $LASTEXITCODE"
}
Write-Host "Hardware worker built successfully" -ForegroundColor Green

$zipPath = Join-Path $artifactsDir "OmenCore-$version-$Runtime.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
Write-Host "Created $zipPath" -ForegroundColor Green

# Resolve Inno Setup CLI
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $defaultPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "C:\InnoSetup\ISCC.exe"
    )
    foreach ($candidate in $defaultPaths) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        if (Test-Path $candidate) {
            $iscc = Get-Item $candidate
            break
        }
    }
}

if (-not $iscc) {
    throw "Inno Setup (iscc) not found. Install Inno Setup 6 to build the installer."
}

$installer = Join-Path $artifactsDir "OmenCoreSetup-$version.exe"
if (Test-Path $installer) { Remove-Item $installer -Force }
& $iscc.FullName "installer/OmenCoreInstaller.iss" "/DMyAppVersion=$version"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compile failed with exit code $LASTEXITCODE"
}
$generated = Join-Path $artifactsDir "OmenCoreSetup-$version.exe"
if (-not (Test-Path $generated)) {
    throw "Inno Setup compiler did not produce the expected output at $generated"
}

if (-not (Test-Path $installer)) {
    throw "Failed to create installer at $installer"
}

Write-Host "Created installer $installer" -ForegroundColor Green
