[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidAssignmentToAutomaticVariable', 'Matches', Justification='False positive on pattern scanning logic in stress harness.')]
param(
    [int]$DurationMinutes = 30,
    [int]$SampleSeconds = 30,
    [string]$ProcessName = "OmenCoreApp",
    [string]$ArtifactsDir = "",
    [string]$LogsDir = "",
    [string]$ReleaseApiUrl = "https://api.github.com/repos/theantipopau/omencore/releases/latest"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http

function Invoke-UpdaterProbe {
    param(
        [System.Net.Http.HttpClient]$HttpClient,
        [string]$ApiUrl
    )

    $releaseJson = $HttpClient.GetStringAsync($ApiUrl).GetAwaiter().GetResult()
    $release = $releaseJson | ConvertFrom-Json
    $assets = @($release.assets)

    if ($assets.Count -eq 0) {
        throw "Updater probe failed: release has no assets."
    }

    $installerAsset = $null
    $portableAsset = $null
    foreach ($asset in $assets) {
        $name = [string]$asset.name
        $lower = $name.ToLowerInvariant()

        if ($null -eq $installerAsset -and ($lower.Contains("setup") -or $lower.Contains("installer")) -and $lower.EndsWith(".exe")) {
            $installerAsset = $asset
            continue
        }
        if ($null -eq $portableAsset -and ($lower.Contains("portable") -or $lower.EndsWith(".zip")) -and -not $lower.Contains("source")) {
            $portableAsset = $asset
        }
    }

    if ($null -eq $installerAsset) {
        throw "Updater probe failed: no installer .exe asset found."
    }
    if ($null -eq $portableAsset) {
        throw "Updater probe failed: no portable .zip asset found."
    }

    return [ordered]@{
        tag = [string]$release.tag_name
        installer = [string]$installerAsset.name
        portable = [string]$portableAsset.name
    }
}

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArtifactsDir)) {
    $ArtifactsDir = Join-Path $root "artifacts"
}
if ([string]::IsNullOrWhiteSpace($LogsDir)) {
    $LogsDir = Join-Path $env:LOCALAPPDATA "OmenCore\logs"
}

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null
$sessionDir = Join-Path $ArtifactsDir ("stress-harness-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

$summaryPath = Join-Path $sessionDir "summary.txt"
$jsonPath = Join-Path $sessionDir "summary.json"

$badPatterns = @(
    "invalid executable",
    "Downloaded file has .exe extension but is not a PE executable",
    "failsafe",
    "stale",
    "high-fan",
    "fan lock"
)

$checkpoints = @(
    "Cycle performance modes: Quiet -> Balanced -> Performance",
    "Cycle fan modes and apply current custom/manual profile",
    "Run Fan Verification Sequence once",
    "Trigger update check once"
)

$start = Get-Date
$end = $start.AddMinutes($DurationMinutes)
$nextUpdaterCheck = $start
$events = @()
$http = [System.Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(45)
$http.DefaultRequestHeaders.UserAgent.ParseAdd("OmenCore-StressHarness")
$http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")

$events += [ordered]@{ t = $start.ToUniversalTime().ToString("o"); type = "session"; msg = "Stress harness started" }
$events += [ordered]@{ t = $start.ToUniversalTime().ToString("o"); type = "note"; msg = "Manual checkpoints:" }
foreach ($cp in $checkpoints) {
    $events += [ordered]@{ t = (Get-Date).ToUniversalTime().ToString("o"); type = "checkpoint"; msg = $cp }
}

while ((Get-Date) -lt $end) {
    $now = Get-Date

    $proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $proc) {
        $events += [ordered]@{ t = $now.ToUniversalTime().ToString("o"); type = "warning"; msg = "Process '$ProcessName' not running." }
    }
    else {
        $events += [ordered]@{
            t = $now.ToUniversalTime().ToString("o")
            type = "sample"
            cpu = $proc.CPU
            wsMb = [Math]::Round($proc.WorkingSet64 / 1MB, 2)
            pmMb = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
        }
    }

    if ($now -ge $nextUpdaterCheck) {
        try {
            $probe = Invoke-UpdaterProbe -HttpClient $http -ApiUrl $ReleaseApiUrl
            $events += [ordered]@{ t = (Get-Date).ToUniversalTime().ToString("o"); type = "updater"; msg = "Updater probe passed"; tag = $probe.tag; installer = $probe.installer; portable = $probe.portable }
        }
        catch {
            $events += [ordered]@{ t = (Get-Date).ToUniversalTime().ToString("o"); type = "error"; msg = "Updater regression check failed: $($_.Exception.Message)" }
        }

        $nextUpdaterCheck = (Get-Date).AddMinutes(10)
    }

    if (Test-Path $LogsDir) {
        $recentLogs = Get-ChildItem $LogsDir -Filter *.log -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 3

        foreach ($log in $recentLogs) {
            $tail = Get-Content $log.FullName -Tail 300 -ErrorAction SilentlyContinue
            foreach ($p in $badPatterns) {
                $hitCount = @(
                    $tail | Where-Object {
                        ([string]$_).IndexOf($p, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
                    }
                ).Count
                if ($hitCount -gt 0) {
                    $events += [ordered]@{
                        t = (Get-Date).ToUniversalTime().ToString("o")
                        type = "signal"
                        pattern = $p
                        log = $log.Name
                        count = $hitCount
                    }
                }
            }
        }
    }

    Start-Sleep -Seconds $SampleSeconds
}

$finish = Get-Date
$signals = @($events | Where-Object { $_.type -eq "signal" })
$errors = @($events | Where-Object { $_.type -eq "error" })
$warnings = @($events | Where-Object { $_.type -eq "warning" })

$summary = [ordered]@{
    startedUtc = $start.ToUniversalTime().ToString("o")
    finishedUtc = $finish.ToUniversalTime().ToString("o")
    durationMinutesRequested = $DurationMinutes
    sampleSeconds = $SampleSeconds
    processName = $ProcessName
    warnings = $warnings.Count
    errors = $errors.Count
    signals = $signals.Count
    events = $events
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding UTF8

$lines = @()
$lines += "OmenCore Stress Harness"
$lines += "Started: $($summary.startedUtc)"
$lines += "Finished: $($summary.finishedUtc)"
$lines += "Duration (requested): $DurationMinutes minutes"
$lines += "Warnings: $($summary.warnings)"
$lines += "Errors: $($summary.errors)"
$lines += "Signals: $($summary.signals)"
$lines += ""
$lines += "Manual checkpoints expected during run:"
foreach ($cp in $checkpoints) { $lines += "- $cp" }
$lines += ""
if ($signals.Count -gt 0) {
    $lines += "Detected signal patterns:"
    foreach ($s in $signals) {
        $lines += "- [$($s.t)] pattern='$($s.pattern)' log='$($s.log)' count=$($s.count)"
    }
}
else {
    $lines += "No signal patterns detected in sampled logs."
}
$lines | Set-Content -Path $summaryPath -Encoding UTF8

$http.Dispose()

Write-Host "Stress harness completed." -ForegroundColor Green
Write-Host "Summary: $summaryPath" -ForegroundColor Green
Write-Host "JSON:    $jsonPath" -ForegroundColor Green
