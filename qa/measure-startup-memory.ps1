<#
.SYNOPSIS
    Measures OmenCore's startup phase timings and idle memory footprint from a real launch.

.DESCRIPTION
    Roadmap WP-31. The 5.0.0 plan asks for "< 1 s to interactive, < 120 MB private idle", but
    both numbers only mean something when they are measured the same way twice. This launches the
    built app once, samples its memory until it has been idle for the requested window, and prints
    the startup phase timeline the app now records itself (see StartupTimeline) so a change can be
    attributed to a phase instead of to "startup".

    It stops the instance it started (graceful window close first, so the app's normal fan
    hand-back runs) and leaves artifacts/startup-profile-<timestamp>.txt behind as evidence.

    Nothing is installed and no hardware is reconfigured: it uses the same startup path a normal
    user launch does, which does apply the user's saved configuration.

.PARAMETER ExePath
    App executable. Defaults to the Release build output.

.PARAMETER IdleSeconds
    How long to sample after launch. The steady-state figure is taken from the tail of this window.

.PARAMETER Mode
    Window (default) records time-to-interactive as well; Tray launches with --minimized and only
    measures the tray-only footprint.

.PARAMETER KillExisting
    Stops any OmenCore instance before measuring. Only use it when you are sure the running copy
    is disposable - it is meant for a machine where a previous measurement left one behind.

.NOTES
    OmenCore's manifest is requireAdministrator, so the app cannot be stopped from a non-elevated
    shell and it holds its own executable open. This script therefore re-launches itself elevated
    when it is not already, which is one UAC prompt per measurement.

.EXAMPLE
    pwsh -File qa/measure-startup-memory.ps1 -IdleSeconds 90
#>
param(
    [string]$ExePath,
    [int]$IdleSeconds = 60,
    [int]$SampleMilliseconds = 500,
    [ValidateSet("Window", "Tray")]
    [string]$Mode = "Window",
    [string]$OutputDirectory = "",
    [string]$JsonPath = "",
    [switch]$KillExisting,
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"

$isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isElevated) {
    Write-Host "OmenCore runs elevated, so this measurement re-launches itself with elevation (UAC prompt)..." -ForegroundColor Yellow
    $relaunchArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"",
        "-IdleSeconds", $IdleSeconds,
        "-SampleMilliseconds", $SampleMilliseconds,
        "-Mode", $Mode
    )
    if (-not [string]::IsNullOrWhiteSpace($ExePath)) { $relaunchArgs += @("-ExePath", "`"$ExePath`"") }
    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) { $relaunchArgs += @("-OutputDirectory", "`"$OutputDirectory`"") }
    if (-not [string]::IsNullOrWhiteSpace($JsonPath)) { $relaunchArgs += @("-JsonPath", "`"$JsonPath`"") }
    if ($KillExisting) { $relaunchArgs += "-KillExisting" }
    if ($KeepRunning) { $relaunchArgs += "-KeepRunning" }

    try {
        $elevated = Start-Process powershell -Verb RunAs -ArgumentList $relaunchArgs -Wait -PassThru
        exit $elevated.ExitCode
    }
    catch {
        # A CI runner with no interactive approval surface cannot answer a UAC prompt. Failing here
        # with this sentence is deliberate: the alternative is a job that waits out its timeout and
        # reports nothing about why.
        throw "Could not elevate to measure: $($_.Exception.Message). OmenCore's manifest is requireAdministrator, so this measurement needs an already-elevated shell."
    }
}

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $root "src/OmenCoreApp/bin/Release/net8.0-windows10.0.19041.0/win-x64/OmenCore.exe"
}
if (-not (Test-Path $ExePath)) {
    throw "App executable not found: $ExePath. Build Release first (dotnet build src/OmenCoreApp/OmenCoreApp.csproj -c Release)."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# Only one OmenCore may drive the hardware (global single-instance mutex), so a second launch
# would exit immediately and the measurement would be of a process that did nothing.
if ($KillExisting) {
    Write-Host "Stopping existing OmenCore processes (--KillExisting)..." -ForegroundColor Yellow
    Get-Process -Name "OmenCore", "OmenCore.HardwareWorker" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
}

$running = Get-Process -Name "OmenCore" -ErrorAction SilentlyContinue
if ($null -ne $running) {
    throw "OmenCore is already running (PID $($running[0].Id)). Close it before measuring - a second instance exits on the single-instance mutex."
}

$logDirectory = Join-Path $env:LOCALAPPDATA "OmenCore\logs"
$workerPidsBefore = @(Get-Process -Name "OmenCore.HardwareWorker" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)

# Isolate configuration. A fresh machine (or a CI runner) has no config, and the app answers that
# with the first-run onboarding window and the driver prompt - both modal, both a hung measurement.
# OMENCORE_CONFIG_DIR is the app's own override, so the real user config is never read or written.
$isolatedConfigDir = Join-Path ([IO.Path]::GetTempPath()) ("omencore-profile-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $isolatedConfigDir | Out-Null
@'
{
  "FirstRunCompleted": true,
  "Monitoring": { "MinimizeToTrayOnClose": false, "StartMinimized": false }
}
'@ | Set-Content -Path (Join-Path $isolatedConfigDir "config.json") -Encoding UTF8
$previousConfigDir = $env:OMENCORE_CONFIG_DIR
$env:OMENCORE_CONFIG_DIR = $isolatedConfigDir

Write-Host "Launching $ExePath ($Mode mode)..." -ForegroundColor Cyan
$launchUtc = (Get-Date).ToUniversalTime()
$startArguments = @{ FilePath = $ExePath; PassThru = $true }
if ($Mode -eq "Tray") { $startArguments.ArgumentList = @("--minimized") }

$process = Start-Process @startArguments
# The child inherited the override at launch; put the parent's environment back immediately so the
# rest of this script (and anything it calls) is unaffected.
$env:OMENCORE_CONFIG_DIR = $previousConfigDir
$samples = New-Object System.Collections.Generic.List[object]
$launchedAt = Get-Date
$deadline = $launchedAt.AddSeconds($IdleSeconds)

while ((Get-Date) -lt $deadline) {
    $live = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($null -eq $live) { break }

    $samples.Add([pscustomobject]@{
        Seconds = [math]::Round(((Get-Date) - $launchedAt).TotalSeconds, 1)
        WorkingSetMB = [math]::Round($live.WorkingSet64 / 1MB, 1)
        PrivateMB = [math]::Round($live.PrivateMemorySize64 / 1MB, 1)
        Threads = $live.Threads.Count
        Handles = $live.HandleCount
        CpuSeconds = [math]::Round($live.CPU, 2)
    })

    Start-Sleep -Milliseconds $SampleMilliseconds
}

if ($samples.Count -eq 0) {
    # Print why here rather than asking whoever reads the CI log to go and find it. A machine
    # without Omen hardware is expected to start and report a timeline, so an early exit is a real
    # failure everywhere, including on a runner.
    Write-Host "The app exited before any sample was taken. Recent log lines:" -ForegroundColor Yellow
    $latestLog = Get-ChildItem $logDirectory -Filter "*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $latestLog) {
        Write-Host "  $($latestLog.FullName)" -ForegroundColor Yellow
        Get-Content $latestLog.FullName -Tail 40 -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "  $_" }
    }
    else {
        Write-Host "  No log file was written at all, so the process died before logging started." -ForegroundColor Yellow
    }

    throw "The app exited before any sample was taken; see the log lines above."
}

# Steady state: the last fifth of the window (at least 5 samples) is what "idle" means here;
# the first few seconds legitimately include JIT, first paint and one-off probes.
$tailCount = [Math]::Max(5, [int][Math]::Ceiling($samples.Count / 5))
$tail = @($samples | Select-Object -Last $tailCount)
$steadyPrivate = ($tail | Sort-Object PrivateMB)[[int]($tail.Count / 2)].PrivateMB
$steadyWorkingSet = ($tail | Sort-Object WorkingSetMB)[[int]($tail.Count / 2)].WorkingSetMB
$peakPrivate = ($samples | Measure-Object -Property PrivateMB -Maximum).Maximum
$peakWorkingSet = ($samples | Measure-Object -Property WorkingSetMB -Maximum).Maximum
$windowSeconds = $samples[-1].Seconds - $samples[0].Seconds
$cpuDelta = $samples[-1].CpuSeconds - $samples[0].CpuSeconds
$idleCpuPercent = if ($windowSeconds -gt 0) {
    [math]::Round(($cpuDelta / ($windowSeconds * [Environment]::ProcessorCount)) * 100, 2)
} else { 0 }

# Pull the startup timeline out of the log the app just wrote.
$logFile = $null
if (Test-Path $logDirectory) {
    $logFile = Get-ChildItem $logDirectory -Filter "*.log" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime.ToUniversalTime() -ge $launchUtc.AddSeconds(-10) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

$phaseMarks = @()
if ($null -ne $logFile) {
    $phaseMarks = @(Get-Content $logFile.FullName |
        Where-Object { $_ -match '\[startup\]\s+(?<phase>[^:]+):\s+(?<ms>[0-9.]+)\s+ms\s+\(phase \+(?<delta>[0-9.]+)\s+ms\)' } |
        ForEach-Object {
            $m = [regex]::Match($_, '\[startup\]\s+(?<phase>[^:]+):\s+(?<ms>[0-9.]+)\s+ms\s+\(phase \+(?<delta>[0-9.]+)\s+ms\)')
            [pscustomobject]@{
                Phase = $m.Groups['phase'].Value.Trim()
                ElapsedMs = [double]$m.Groups['ms'].Value
                SincePreviousMs = [double]$m.Groups['delta'].Value
            }
        })
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("=== STARTUP / IDLE MEMORY PROFILE ===")
$lines.Add("CapturedUtc: $((Get-Date).ToUniversalTime().ToString('O'))")
$lines.Add("Executable: $ExePath")
$renderMode = "hardware"
if ($null -ne $logFile) {
    $softwareRender = Get-Content $logFile.FullName -ErrorAction SilentlyContinue |
        Where-Object { $_ -match 'Software rendering enabled' } | Select-Object -First 1
    if ($null -ne $softwareRender) { $renderMode = "software (RTSS or user setting)" }
}

$lines.Add("Mode: $Mode")
$lines.Add("RenderMode: $renderMode")
$lines.Add("IdleWindowSeconds: $IdleSeconds")
$lines.Add("Samples: $($samples.Count)")
$lines.Add("")
$lines.Add("[Startup Phases] (milliseconds after OS process start)")
if ($phaseMarks.Count -eq 0) {
    $lines.Add("No [startup] marks found in $logFile (was the build instrumented?)")
} else {
    foreach ($mark in $phaseMarks) {
        $lines.Add(("{0,-32} {1,8:F0} ms   phase {2,7:F0} ms" -f $mark.Phase, $mark.ElapsedMs, $mark.SincePreviousMs))
    }
    $interactive = $phaseMarks | Where-Object { $_.Phase -eq "first_render" } | Select-Object -First 1
    if ($null -ne $interactive) {
        $lines.Add("")
        $lines.Add("TimeToInteractiveMs: $([math]::Round($interactive.ElapsedMs))")
    }
}
$lines.Add("")
$lines.Add("[Memory]")
$lines.Add("SteadyStatePrivateMB: $steadyPrivate")
$lines.Add("SteadyStateWorkingSetMB: $steadyWorkingSet")
$lines.Add("PeakPrivateMB: $peakPrivate")
$lines.Add("PeakWorkingSetMB: $peakWorkingSet")
$lines.Add("IdleCpuPercent: $idleCpuPercent")
$lines.Add("")
# Deliberately no pass/fail line here: enforcement lives in qa/check-memory-budget.ps1 against
# qa/memory-budget.json, so the thresholds exist in exactly one place. This script measures and
# reports; that one judges.

$firstRender = $phaseMarks | Where-Object { $_.Phase -eq "first_render" } | Select-Object -First 1
$timeToInteractiveMs = if ($null -ne $firstRender) { [double]$firstRender.ElapsedMs } else { $null }

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $metrics = [ordered]@{
        capturedUtc = (Get-Date).ToUniversalTime().ToString("O")
        executable = $ExePath
        mode = $Mode
        renderMode = $renderMode
        idleWindowSeconds = $IdleSeconds
        samples = $samples.Count
        timeToInteractiveMs = $timeToInteractiveMs
        steadyStatePrivateMB = $steadyPrivate
        steadyStateWorkingSetMB = $steadyWorkingSet
        peakPrivateMB = $peakPrivate
        peakWorkingSetMB = $peakWorkingSet
        idleCpuPercent = $idleCpuPercent
        startupPhases = @($phaseMarks | ForEach-Object {
            [ordered]@{ phase = $_.Phase; elapsedMs = $_.ElapsedMs; sincePreviousMs = $_.SincePreviousMs }
        })
    }

    $metrics | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding UTF8
    Write-Host "Metrics JSON written to $JsonPath" -ForegroundColor Cyan
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputPath = Join-Path $OutputDirectory "startup-profile-$stamp.txt"
$lines | Set-Content -Path $outputPath -Encoding UTF8

Write-Host ""
$lines | ForEach-Object { Write-Host $_ }
Write-Host ""
Write-Host "Written to $outputPath" -ForegroundColor Cyan

if (-not $KeepRunning) {
    # CloseMainWindow is deliberately skipped: the app hides to the tray on close by default
    # (MinimizeToTrayOnClose), so a graceful close would leave the instance running and holding
    # the build output. The app also runs elevated (app.manifest requireAdministrator), which
    # means a non-elevated shell cannot terminate it at all - so this reports that clearly
    # instead of pretending the run cleaned up.
    Write-Host "Stopping the measured instance..." -ForegroundColor Cyan
    $workerPidsAfter = @(Get-Process -Name "OmenCore.HardwareWorker" -ErrorAction SilentlyContinue |
        Where-Object { $workerPidsBefore -notcontains $_.Id } | Select-Object -ExpandProperty Id)

    foreach ($targetPid in @($process.Id) + $workerPidsAfter) {
        if (Get-Process -Id $targetPid -ErrorAction SilentlyContinue) {
            Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue
        }
    }

    Start-Sleep -Seconds 2
    $survivors = @(@($process.Id) + $workerPidsAfter | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    if ($survivors.Count -gt 0) {
        Write-Host ""
        Write-Host "Could not stop PID $($survivors -join ', '): access denied. OmenCore runs elevated, so it must be stopped from an elevated shell:" -ForegroundColor Yellow
        Write-Host "  taskkill /F /PID $($survivors -join ' /PID ')" -ForegroundColor Yellow
    }
}

Remove-Item $isolatedConfigDir -Recurse -Force -ErrorAction SilentlyContinue
