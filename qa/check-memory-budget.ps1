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
      - idle private memory, peak private memory, idle working set
      - appStartupMs: time from process start until the app has finished its own startup work
        (the worker_bootstrap phase, after hardware bring-up, view-model construction and the
        hardware worker launch)

    What is reported but not enforced: time to first paint, and idle CPU. Both are dominated by
    things a startup regression cannot fix. Measured on the development machine, the app completes
    its startup work in a tight 1.7-2.2 s band across runs while first paint ranged from 2.4 s to
    12.3 s in the same period - RTSS software rendering plus window-manager and hotkey
    registration stalls, all inside Show(). A gate on that number would fail for reasons nobody
    could act on, and a gate that flakes is a gate people disable. First paint is printed on every
    run so the number stays visible.

    Two launches are measured and the faster one is judged. Startup timing noise is one-sided --
    a cold file cache or an antivirus scan can only add time -- while a real regression is present
    in every attempt, so the best of a few runs is the closest estimate of the app's own cost.

    The budget is a release gate, not a target. When a change genuinely needs more room, raise the
    number in qa/memory-budget.json in the same commit that adds the weight, so the growth is a
    reviewed decision instead of an accident.

.PARAMETER BudgetFile
    Path to the budget JSON. Defaults to qa/memory-budget.json.

.PARAMETER MetricsPath
    Compare a metrics JSON produced by measure-startup-memory.ps1 instead of launching the app.
    Used to prove the gate fails when it should.

.PARAMETER SampleSeconds
    Overrides the budget file's sample window.

.PARAMETER Attempts
    How many launches to measure. Defaults to 2.

.EXAMPLE
    pwsh -File qa/check-memory-budget.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$BudgetFile,
    [string]$MetricsPath,
    [int]$SampleSeconds = 0,
    [int]$Attempts = 2,
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

$artifactsDir = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

$measured = $null

if ([string]::IsNullOrWhiteSpace($MetricsPath)) {
    $MetricsPath = Join-Path $artifactsDir "memory-budget-metrics.json"

    if (-not $SkipBuild) {
        Write-Host "Building $Configuration to measure it..." -ForegroundColor Cyan
        & dotnet build (Join-Path $root "src/OmenCoreApp/OmenCoreApp.csproj") -c $Configuration -v minimal
        if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
    }

    $results = @()
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $attemptPath = Join-Path $artifactsDir "memory-budget-attempt-$attempt.json"
        Write-Host ""
        Write-Host "Measurement attempt $attempt of $Attempts" -ForegroundColor Cyan
        & (Join-Path $scriptRoot "measure-startup-memory.ps1") -IdleSeconds $SampleSeconds -Mode Window -JsonPath $attemptPath

        if (Test-Path $attemptPath) {
            $result = Get-Content $attemptPath -Raw | ConvertFrom-Json
            $results += [pscustomobject]@{ Attempt = $attempt; Metrics = $result; Path = $attemptPath }
        }
        else {
            Write-Host "Attempt $attempt produced no metrics file." -ForegroundColor Yellow
        }
    }

    if ($results.Count -eq 0) {
        throw "No measurement attempt produced metrics. The app never got far enough to report a startup timeline - treat that as a regression rather than a skipped check."
    }

    # Judge on the app's own startup cost, which is what a regression moves; fall back to the first
    # attempt when no attempt got that far so the missing phase fails loudly below.
    $judged = $results | Where-Object { $null -ne (Get-AppStartupMs -Metrics $_.Metrics) } |
        Sort-Object { Get-AppStartupMs -Metrics $_.Metrics } | Select-Object -First 1
    if ($null -eq $judged) { $judged = $results | Select-Object -First 1 }

    Write-Host ""
    Write-Host "Attempts:" -ForegroundColor Cyan
    foreach ($result in $results) {
        $appMs = Get-AppStartupMs -Metrics $result.Metrics
        $appText = if ($null -ne $appMs) { "{0:F0} ms" -f $appMs } else { "startup incomplete" }
        $paintText = if ($null -ne $result.Metrics.timeToInteractiveMs) { "{0:F0} ms" -f $result.Metrics.timeToInteractiveMs } else { "not reported" }
        Write-Host ("  attempt {0}: {1,10} app startup, {2,10} first paint, {3,7:F1} MB private idle" -f `
            $result.Attempt, $appText, $paintText, [double]$result.Metrics.steadyStatePrivateMB)
    }

    $measured = $judged.Metrics
    # Keep the judged attempt as the canonical evidence file.
    $measured | ConvertTo-Json -Depth 6 | Set-Content -Path $MetricsPath -Encoding UTF8
}
else {
    $measured = Get-Content $MetricsPath -Raw | ConvertFrom-Json
}

# A run that never completed startup work is not "fast"; it is unmeasured. Failing here keeps the
# gate honest on machines where the app cannot start, instead of letting the check silently pass.
$appStartupMs = Get-AppStartupMs -Metrics $measured
if ($null -eq $appStartupMs) {
    throw "No worker_bootstrap phase in the measurement - the app never completed its startup work, so startup cannot be checked. Treat this as a failure, not a skip."
}

$checks = @(
    [pscustomobject]@{ Name = "idle private memory (MB)"; Measured = [double]$measured.steadyStatePrivateMB; Budget = [double]$budget.idlePrivateMb }
    [pscustomobject]@{ Name = "peak private memory (MB)"; Measured = [double]$measured.peakPrivateMb; Budget = [double]$budget.peakPrivateMb }
    [pscustomobject]@{ Name = "idle working set (MB)"; Measured = [double]$measured.steadyStateWorkingSetMB; Budget = [double]$budget.idleWorkingSetMb }
    [pscustomobject]@{ Name = "app startup complete (ms)"; Measured = [double]$appStartupMs; Budget = [double]$budget.appStartupMs }
)

# Metrics files may not carry every field; report a metric rather than comparing a null.
$checks = $checks | Where-Object { $null -ne $_.Measured }

Write-Host ""
Write-Host "Startup and idle memory budget" -ForegroundColor Cyan
Write-Host ("  Render mode: {0}" -f $(if ($null -ne $measured.renderMode) { $measured.renderMode } else { "unknown" }))
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

$firstPaintText = if ($null -ne $measured.timeToInteractiveMs) { "{0:F0} ms" -f [double]$measured.timeToInteractiveMs } else { "not reported" }
Write-Host ""
Write-Host ("  First paint: {0} (reported, not enforced - see the script header)" -f $firstPaintText)
Write-Host ("  Idle CPU: {0:F2}% (reported, not enforced - see the script header)" -f [double]$measured.idleCpuPercent)
Write-Host ""

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "BUDGET EXCEEDED: $failure" -ForegroundColor Red }
    Write-Host "Raise qa/memory-budget.json in the same commit as the growth, or shrink the footprint." -ForegroundColor Red
    exit 1
}

Write-Host "Within budget." -ForegroundColor Green
exit 0
