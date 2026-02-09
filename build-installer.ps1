param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
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

Write-Host "Building OmenCore $version ($Configuration/$Runtime)" -ForegroundColor Cyan

# Download LibreHardwareMonitor for bundling
Write-Host "Checking for LibreHardwareMonitor..." -ForegroundColor Cyan
$librehwScript = Join-Path $root "installer\download-librehw.ps1"
if (Test-Path $librehwScript) {
    & $librehwScript
} else {
    Write-Host "LibreHardwareMonitor download script not found - installer will not include driver" -ForegroundColor Yellow
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (-not (Test-Path $publishRoot)) { New-Item $publishRoot -ItemType Directory | Out-Null }
if (-not (Test-Path $artifactsDir)) { New-Item $artifactsDir -ItemType Directory | Out-Null }

# Choose the appropriate project for the requested runtime (GUI is Windows-only; use CLI for Linux builds)
if ($Runtime -like "linux*") {
    $appProject = "src/OmenCore.Linux/OmenCore.Linux.csproj"
} else {
    $appProject = "src/OmenCoreApp/OmenCoreApp.csproj"
}

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
    "-o", $publishDir
)

# SingleFile is now always enabled for proper .NET embedding
Write-Host "Building self-contained single-file executable with embedded .NET runtime..." -ForegroundColor Yellow

& dotnet publish @publishArgs

# Build and publish the hardware worker (out-of-process monitoring for crash isolation)
Write-Host "Building hardware worker process..." -ForegroundColor Yellow
$workerPublishArgs = @(
    "src/OmenCore.HardwareWorker/OmenCore.HardwareWorker.csproj",
    "--configuration", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishTrimmed=false",
    "-p:PublishSingleFile=true",
    "-o", $publishDir
)
& dotnet publish @workerPublishArgs
Write-Host "Hardware worker built successfully" -ForegroundColor Green

# Build Avalonia GUI for Linux platforms
if ($Runtime -like "linux*") {
    Write-Host "Building Avalonia GUI for Linux..." -ForegroundColor Yellow
    $guiProject = "src/OmenCore.Avalonia/OmenCore.Avalonia.csproj"
    if (Test-Path (Join-Path $root $guiProject)) {
        $guiPublishArgs = @(
            $guiProject,
            "--configuration", $Configuration,
            "-r", $Runtime,
            "--self-contained", "true",
            "-p:PublishTrimmed=false",
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:IncludeAllContentForSelfExtract=true",
            "-o", $publishDir
        )
        & dotnet publish @guiPublishArgs
        Write-Host "Avalonia GUI built successfully" -ForegroundColor Green
    } else {
        Write-Host "Avalonia GUI project not found at $guiProject - skipping GUI build" -ForegroundColor Yellow
    }
}

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
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
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
$generated = Join-Path $artifactsDir "OmenCoreSetup-$version.exe"
if (-not (Test-Path $generated)) {
    throw "Inno Setup compiler did not produce the expected output at $generated"
}

if (-not (Test-Path $installer)) {
    throw "Failed to create installer at $installer"
}

Write-Host "Created installer $installer" -ForegroundColor Green
