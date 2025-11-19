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

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (-not (Test-Path $publishRoot)) { New-Item $publishRoot -ItemType Directory | Out-Null }
if (-not (Test-Path $artifactsDir)) { New-Item $artifactsDir -ItemType Directory | Out-Null }

$publishArgs = @(
    "src/OmenCoreApp/OmenCoreApp.csproj",
    "--configuration", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $publishDir
)

if ($SingleFile) {
    $publishArgs += "-p:PublishSingleFile=true"
}

& dotnet publish @publishArgs

$zipPath = Join-Path $artifactsDir "OmenCore-$version-$Runtime.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
Write-Host "Created $zipPath" -ForegroundColor Green

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($iscc) {
    $installer = Join-Path $artifactsDir "OmenCoreSetup-$version.exe"
    if (Test-Path $installer) { Remove-Item $installer -Force }
    & $iscc.Source "installer/OmenCoreInstaller.iss" "/DMyAppVersion=$version"
    $generated = Join-Path $artifactsDir "OmenCoreSetup.exe"
    if (Test-Path $generated) {
        Rename-Item $generated $installer -Force
    }
    Write-Host "Created installer $installer" -ForegroundColor Green
} else {
    Write-Warning "Inno Setup (iscc) not found. Install it to build OmenCoreSetup.exe"
}
