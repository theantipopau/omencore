param(
    [string]$Configuration = "Release",
    [string]$Runtime = "linux-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$versionFile = Join-Path $root "VERSION.txt"
if (-not (Test-Path $versionFile)) {
    throw "VERSION.txt not found."
}

$version = (Get-Content $versionFile | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "VERSION.txt must contain a semantic version string."
}

if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)') {
    throw "VERSION.txt must start with Major.Minor.Patch (for example 3.2.5). Found: '$version'"
}

$assemblyVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"

$artifactsDir = Join-Path $root "artifacts"
$stagingRoot = Join-Path $root "publish\$Runtime\$version"
$guiOut = Join-Path $stagingRoot "gui"
$cliOut = Join-Path $stagingRoot "cli"
$packageDir = Join-Path $stagingRoot "package"

# Ensure we start from a clean staging area so stale files cannot leak into the package.
if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
New-Item -ItemType Directory -Force -Path $guiOut | Out-Null
New-Item -ItemType Directory -Force -Path $cliOut | Out-Null
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

Write-Host "Building OmenCore Linux package $version ($Configuration/$Runtime)" -ForegroundColor Cyan

# Build GUI first into its own folder.
# This project references OmenCore.Linux and may emit an apphost for omencore-cli.
# Keeping outputs isolated prevents it from replacing the final CLI binary.
dotnet publish "$root\src\OmenCore.Avalonia\OmenCore.Avalonia.csproj" `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion `
    -o $guiOut

# Build CLI second into its own folder.
dotnet publish "$root\src\OmenCore.Linux\OmenCore.Linux.csproj" `
    -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion `
    -o $cliOut

if (-not (Test-Path "$guiOut\omencore-gui")) {
    throw "GUI publish did not produce omencore-gui at $guiOut"
}

if (-not (Test-Path "$cliOut\omencore-cli")) {
    throw "CLI publish did not produce omencore-cli at $cliOut"
}

# Assemble package from GUI payload + known-good CLI binary.
Copy-Item "$guiOut\*" $packageDir -Recurse -Force
Copy-Item "$cliOut\omencore-cli" $packageDir -Force
if (Test-Path "$cliOut\omencore-cli.pdb") {
    Copy-Item "$cliOut\omencore-cli.pdb" $packageDir -Force
}

# Remove framework-dependent sidecar files that may be emitted via project references
# in the GUI publish output. The final CLI in this package is a self-contained single-file binary.
@(
    "omencore-cli.runtimeconfig.json",
    "omencore-cli.deps.json",
    "omencore-cli.dll"
) | ForEach-Object {
    $stalePath = Join-Path $packageDir $_
    if (Test-Path $stalePath) {
        Remove-Item $stalePath -Force
    }
}

$zipPath = Join-Path $artifactsDir "OmenCore-$version-$Runtime.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$packageDir\*" -DestinationPath $zipPath -Force

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
$shaPath = "$zipPath.sha256"
$sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)

Set-Content -Path $shaPath -Value "$hash  $([IO.Path]::GetFileName($zipPath))" -NoNewline

# Emit machine-readable package manifest for support/release verification.
$manifest = [ordered]@{
    version = $version
    assemblyVersion = $assemblyVersion
    runtime = $Runtime
    configuration = $Configuration
    packageFile = [IO.Path]::GetFileName($zipPath)
    packageSha256 = $hash
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
}
$manifestPath = Join-Path $artifactsDir "version.json"
$manifest | ConvertTo-Json -Depth 3 | Set-Content -Path $manifestPath -Encoding UTF8

if (-not ($zipPath -like "*-$version-$Runtime.zip")) {
    throw "Version verification failed: package filename does not include expected version/runtime ($version/$Runtime)."
}
if (-not (Test-Path $manifestPath)) {
    throw "Version verification failed: manifest was not generated."
}

Write-Host "Created $zipPath" -ForegroundColor Green
Write-Host "Size: $sizeMb MB" -ForegroundColor Green
Write-Host "SHA256: $hash" -ForegroundColor Green
Write-Host "Hash file: $shaPath" -ForegroundColor Green
Write-Host "Manifest: $manifestPath" -ForegroundColor Green
