<#
.SYNOPSIS
    Publishes the portable Windows build and fails when it exceeds the size budget.

.DESCRIPTION
    Roadmap WP-46 / theme I.5. OmenMon's pitch is a ~14 MB download against OMEN Gaming
    Hub's ~1.4 GB, so the portable artifact needs a published ceiling rather than a number
    in a README that drifts away from reality. This publishes exactly what
    build-installer.ps1 ships (self-contained, single-file, compression on), zips it, and
    compares the numbers against qa/size-budget.json.

    The budget is a release gate, not a target: when a change genuinely needs more room,
    raise the number in the JSON in the same commit that adds the weight, so the growth is
    a reviewed decision instead of an accident.

.PARAMETER BudgetFile
    Path to the budget JSON. Defaults to qa/size-budget.json.

.PARAMETER KeepOutput
    Leaves artifacts/size-check in place for inspection. CI always cleans up.

.EXAMPLE
    pwsh -File qa/check-size-budget.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$BudgetFile,
    [switch]$KeepOutput
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$root = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($BudgetFile)) {
    $BudgetFile = Join-Path $scriptRoot "size-budget.json"
}

if (-not (Test-Path $BudgetFile)) {
    throw "Size budget file not found: $BudgetFile"
}

$budget = Get-Content $BudgetFile -Raw | ConvertFrom-Json
foreach ($required in @("windowsPortableZipMb", "windowsPortableExeMb", "hardwareWorkerExeMb")) {
    if ($null -eq $budget.$required) {
        throw "Size budget missing '$required' in $BudgetFile"
    }
}

$outDir = Join-Path $root "artifacts/size-check"
$zipPath = Join-Path $root "artifacts/size-check.zip"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "Publishing portable build ($Configuration/$Runtime) to measure it..." -ForegroundColor Cyan

$appArgs = @(
    "src/OmenCoreApp/OmenCoreApp.csproj",
    "--configuration", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-o", $outDir
)
& dotnet publish @appArgs
if ($LASTEXITCODE -ne 0) { throw "App publish failed with exit code $LASTEXITCODE" }

$workerArgs = @(
    "src/OmenCore.HardwareWorker/OmenCore.HardwareWorker.csproj",
    "--configuration", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-o", $outDir
)
& dotnet publish @workerArgs
if ($LASTEXITCODE -ne 0) { throw "Hardware worker publish failed with exit code $LASTEXITCODE" }

Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

function Get-SizeMb([string]$path) {
    if (-not (Test-Path $path)) { throw "Expected artifact not found: $path" }
    return [math]::Round((Get-Item $path).Length / 1MB, 1)
}

$measured = [ordered]@{
    windowsPortableExeMb = Get-SizeMb (Join-Path $outDir "OmenCore.exe")
    hardwareWorkerExeMb  = Get-SizeMb (Join-Path $outDir "OmenCore.HardwareWorker.exe")
    windowsPortableZipMb = Get-SizeMb $zipPath
}

Write-Host ""
Write-Host "Portable artifact size check" -ForegroundColor Cyan
Write-Host ("  {0,-24} {1,8} MB measured   {2,8} MB budget" -f "OmenCore.exe", $measured.windowsPortableExeMb, [double]$budget.windowsPortableExeMb)
Write-Host ("  {0,-24} {1,8} MB measured   {2,8} MB budget" -f "HardwareWorker.exe", $measured.hardwareWorkerExeMb, [double]$budget.hardwareWorkerExeMb)
Write-Host ("  {0,-24} {1,8} MB measured   {2,8} MB budget" -f "portable zip", $measured.windowsPortableZipMb, [double]$budget.windowsPortableZipMb)

$failures = @()
foreach ($key in $measured.Keys) {
    $budgetMb = [double]$budget.$key
    if ($measured[$key] -gt $budgetMb) {
        $failures += "$key is $($measured[$key]) MB, budget is $budgetMb MB (over by $([math]::Round($measured[$key] - $budgetMb, 1)) MB)"
    }
}

if (-not $KeepOutput) {
    Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host ""
    foreach ($failure in $failures) { Write-Host "SIZE BUDGET EXCEEDED: $failure" -ForegroundColor Red }
    Write-Host "Raise qa/size-budget.json in the same commit as the growth, or shrink the artifact." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Within budget." -ForegroundColor Green
exit 0
