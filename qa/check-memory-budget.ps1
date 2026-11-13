<#
.SYNOPSIS
    Launches the built app, measures a real startup and idle footprint, and fails when either
    exceeds qa/memory-budget.json.

.DESCRIPTION
    Roadmap WP-31 follow-up (with WP-42's "performance budget in CI"). The 5.0.0 plan promises
    startup and memory behaviour, and both were only ever checked by hand: a regression could ship
    and be discovered from a user report weeks later, which is exactly what happened to the
    >400 MB sessions documented in v4.1.0. This runs the same measurement the profiling harness
    uses (qa/measure-startup-memory.ps1) against the Release build and compares the result with a
    published ceiling.

    What is enforced:
      - idle private memory, peak private memory, idle working set: the worst of the attempts
      - appStartupMs: the median of the attempts (time from process start until the app has
        finished its own startup work, i.e. the worker_bootstrap phase)

    Why the two are judged differently. Memory is deterministic here - the same build on the same
    machine lands within a couple of MB run to run - so the worst observation is simply the honest
    number, and taking the best would hide a leak. Startup is not: three launches of one build on
    one GitHub runner measured 3650 / 2285 / 2301 ms, a 1.4 s spread with no code change. Judging
    the fastest attempt let the slowest run sit 22% over budget and still pass, which is exactly
    the judgement being carried by noise; judging the slowest would fail on a busy runner that
    changed nothing. The median ignores one outlier in either direction and moves only when the
    underlying cost moves.

    What is reported but not enforced: time to first paint, and idle CPU. Both are dominated by
    things a startup regression cannot fix. Measured on the development machine, the app completes
    its startup work in a tight 1.7-2.2 s band across runs while first paint ranged from 2.4 s to
    12.3 s in the same period - RTSS software rendering plus window-manager and hotkey
    registration stalls, all inside Show(). A gate on that number would fail for reasons nobody
    could act on, and a gate that flakes is a gate people disable. First paint is printed on every
    run so the number stays visible.

    The budget is a release gate, not a target. When a change genuinely needs more room, raise the
    number in qa/memory-budget.json in the same commit that adds the weight, so the growth is a
    reviewed decision instead of an accident.

.PARAMETER BudgetFile
    Path to the budget JSON. Defaults to qa/memory-budget.json.

.PARAMETER MetricsPath
    One or more metrics JSON files produced by measure-startup-memory.ps1, compared instead of
    launching the app. Used to prove the gate fails when it should, including the median rule.

.PARAMETER SampleSeconds
    Overrides the budget file's sample window.

.PARAMETER Attempts
    How many launches to measure. Defaults to 3; the median needs an odd count to be a middle value.

.EXAMPLE
    pwsh -File qa/check-memory-budget.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$BudgetFile,
    [string[]]$MetricsPath,
    [int]$SampleSeconds = 0,
    [int]$Attempts = 3,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
$root = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($BudgetFile)) {
    $BudgetFile = Join-Path $scriptRoot "memory-budget.json"
}

if (-not (Test-Path $BudgetFile)) {
    throw "Memory budget file not found: $BudgetFile"
}

$budget = Get-Content $BudgetFile -Raw | ConvertFrom-Json
foreach ($required in @("idlePrivateMb", "peakPrivateMb", "idleWorkingSetMb", "appStartupMs")) {
    if ($null -eq $budget.$required) {
        throw "Memory budget missing '$required' in $BudgetFile"
    }
}

# Invoked with -File, PowerShell hands "a,b,c" over as one string rather than an array, so
# normalise both forms; a single path is unaffected.
if ($MetricsPath) {
    $MetricsPath = @($MetricsPath | ForEach-Object { $_ -split ',' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

if ($SampleSeconds -le 0) {
    $SampleSeconds = if ($null -ne $budget.sampleSeconds) { [int]$budget.sampleSeconds } else { 45 }
}
if ($Attempts -lt 1) { $Attempts = 1 }

# The app's own startup cost: the last phase it records before the window is shown. Named phase
# rather than "whatever came last" so a later phase added to the timeline cannot silently redefine
# what this budget measures.
function Get-AppStartupMs {
    param($Metrics)

    $phase = $Metrics.startupPhases | Where-Object { $_.phase -eq "worker_bootstrap" } | Select-Object -First 1
    if ($null -eq $phase) { return $null }
    return [double]$phase.elapsedMs
}

function Get-Median {
    param([double[]]$Values)

    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2
}

$artifactsDir = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

$results = @()

if (-not $MetricsPath -or $MetricsPath.Count -eq 0) {
    if (-not $SkipBuild) {
        Write-Host "Building $Configuration to measure it..." -ForegroundColor Cyan
        & dotnet build (Join-Path $root "src/OmenCoreApp/OmenCoreApp.csproj") -c $Configuration -v minimal
        if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
    }

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $attemptPath = Join-Path $artifactsDir "memory-budget-attempt-$attempt.json"
        Write-Host ""
        Write-Host "Measurement attempt $attempt of $Attempts" -ForegroundColor Cyan
        & (Join-Path $scriptRoot "measure-startup-memory.ps1") -IdleSeconds $SampleSeconds -Mode Window -JsonPath $attemptPath

        if (Test-Path $attemptPath) {
            $results += [pscustomobject]@{ Attempt = $attempt; Metrics = (Get-Content $attemptPath -Raw | ConvertFrom-Json) }
        }
        else {
            Write-Host "Attempt $attempt produced no metrics file." -ForegroundColor Yellow
        }
    }

    if ($results.Count -eq 0) {
        throw "No measurement attempt produced metrics. The app never got far enough to report a startup timeline - treat that as a regression rather than a skipped check."
    }
}
else {
    $index = 0
    foreach ($path in $MetricsPath) {
        $index++
        if (-not (Test-Path $path)) { throw "Metrics file not found: $path" }
        $results += [pscustomobject]@{ Attempt = $index; Metrics = (Get-Content $path -Raw | ConvertFrom-Json) }
    }
}

$resultsWithStartup = @($results | Where-Object { $null -ne (Get-AppStartupMs -Metrics $_.Metrics) })

# A run that never completed startup work is not "fast"; it is unmeasured. Failing here keeps the
# gate honest on machines where the app cannot start, instead of letting the check silently pass.
if ($resultsWithStartup.Count -eq 0) {
    $lastPhase = @($results[0].Metrics.startupPhases | Select-Object -Last 1).Phase
    if ([string]::IsNullOrWhiteSpace($lastPhase)) { $lastPhase = "none" }
    throw "No worker_bootstrap phase in any measurement - the app never completed its startup work (last phase: $lastPhase), so startup cannot be checked. Treat this as a failure, not a skip."
}

$appStartupMs = Get-Median -Values @($resultsWithStartup | ForEach-Object { Get-AppStartupMs -Metrics $_.Metrics })
$worstPrivate = ($results | ForEach-Object { [double]$_.Metrics.steadyStatePrivateMB } | Measure-Object -Maximum).Maximum
$worstPeakPrivate = ($results | ForEach-Object { [double]$_.Metrics.peakPrivateMB } | Measure-Object -Maximum).Maximum
$worstWorkingSet = ($results | ForEach-Object { [double]$_.Metrics.steadyStateWorkingSetMB } | Measure-Object -Maximum).Maximum
$firstPaint = Get-Median -Values @($results | Where-Object { $null -ne $_.Metrics.timeToInteractiveMs } | ForEach-Object { [double]$_.Metrics.timeToInteractiveMs })
$idleCpu = ($results | ForEach-Object { [double]$_.Metrics.idleCpuPercent } | Measure-Object -Maximum).Maximum

Write-Host ""
Write-Host "Attempts:" -ForegroundColor Cyan
foreach ($result in $results) {
    $appMs = Get-AppStartupMs -Metrics $result.Metrics
    $appText = if ($null -ne $appMs) { "{0:F0} ms" -f $appMs } else { "startup incomplete" }
    $paintText = if ($null -ne $result.Metrics.timeToInteractiveMs) { "{0:F0} ms" -f $result.Metrics.timeToInteractiveMs } else { "not reported" }
    Write-Host ("  attempt {0}: {1,10} app startup, {2,10} first paint, {3,7:F1} MB private idle" -f `
        $result.Attempt, $appText, $paintText, [double]$result.Metrics.steadyStatePrivateMB)
}
if ($results.Count -gt 1) {
    $spreads = @($resultsWithStartup | ForEach-Object { Get-AppStartupMs -Metrics $_.Metrics } | Sort-Object)
    Write-Host ("  startup spread: {0:F0} ms (min {1:F0}, max {2:F0}); judged on the median, memory on the worst attempt" -f `
        ($spreads[-1] - $spreads[0]), $spreads[0], $spreads[-1])
}

# Keep a canonical summary for evidence: the judged numbers, not one arbitrary attempt.
$summaryPath = Join-Path $artifactsDir "memory-budget-metrics.json"
[ordered]@{
    capturedUtc = (Get-Date).ToUniversalTime().ToString("O")
    attempts = $results.Count
    renderMode = $results[0].Metrics.renderMode
    appStartupMs = $appStartupMs
    appStartupJudge = "median of $($resultsWithStartup.Count) attempt(s)"
    steadyStatePrivateMB = $worstPrivate
    peakPrivateMB = $worstPeakPrivate
    steadyStateWorkingSetMB = $worstWorkingSet
    memoryJudge = "worst attempt"
    firstPaintMs = $firstPaint
    idleCpuPercent = $idleCpu
} | ConvertTo-Json -Depth 4 | Set-Content -Path $summaryPath -Encoding UTF8

$checks = @(
    [pscustomobject]@{ Name = "idle private memory (MB)"; Measured = [double]$worstPrivate; Budget = [double]$budget.idlePrivateMb }
    [pscustomobject]@{ Name = "peak private memory (MB)"; Measured = [double]$worstPeakPrivate; Budget = [double]$budget.peakPrivateMb }
    [pscustomobject]@{ Name = "idle working set (MB)"; Measured = [double]$worstWorkingSet; Budget = [double]$budget.idleWorkingSetMb }
    [pscustomobject]@{ Name = "app startup, median (ms)"; Measured = [double]$appStartupMs; Budget = [double]$budget.appStartupMs }
)

Write-Host ""
Write-Host "Startup and idle memory budget" -ForegroundColor Cyan
Write-Host ("  Render mode: {0}" -f $(if ($null -ne $results[0].Metrics.renderMode) { $results[0].Metrics.renderMode } else { "unknown" }))
foreach ($check in $checks) {
    $unit = if ($check.Name -like "*(ms)*") { "ms" } else { "MB" }
    Write-Host ("  {0,-30} {1,8:F1} {2} measured   {3,8:F0} {2} budget" -f $check.Name, $check.Measured, $unit, $check.Budget)
}

$failures = @()
foreach ($check in $checks) {
    if ($check.Measured -gt $check.Budget) {
        $unit = if ($check.Name -like "*(ms)*") { "ms" } else { "MB" }
        $failures += ("{0} is {1:F1} {2}, budget is {3:F0} {2} (over by {4:F1} {2})" -f `
            $check.Name, $check.Measured, $unit, $check.Budget, ($check.Measured - $check.Budget))
    }
}

$firstPaintText = if ($null -ne $firstPaint) { "{0:F0} ms" -f $firstPaint } else { "not reported" }
Write-Host ""
Write-Host ("  First paint: {0} (reported, not enforced - see the script header)" -f $firstPaintText)
Write-Host ("  Idle CPU: {0:F2}% (reported, not enforced - see the script header)" -f [double]$idleCpu)
Write-Host ""

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "BUDGET EXCEEDED: $failure" -ForegroundColor Red }
    Write-Host "Raise qa/memory-budget.json in the same commit as the growth, or shrink the footprint." -ForegroundColor Red
    exit 1
}

Write-Host "Within budget." -ForegroundColor Green
exit 0
