<#
.SYNOPSIS
    Files the two outstanding upstream pull requests for this fork.

.DESCRIPTION
    Everything they need is already pushed; this only opens the PRs, and it prints exactly what it
    would do unless you pass -Commit.

    Dry-run by default on purpose. Opening a pull request is outward-facing and public: it notifies
    maintainers, and the branch contents become visible under someone else's repository. That is not
    something a script should do as a side effect of being run.

    Two branches are unfiled:

      8d87/adapter-power-override   the GPU restart that drops an under-rated adapter's power clamp
      fix/gpu-counter-dispose-race  a pre-existing shutdown race found while testing it

    They are independent. The second is a main-owned bug with nothing to do with the adapter work,
    which is why it is a separate branch and a separate PR rather than a passenger in that diff.

.PARAMETER Commit
    Actually create the pull requests. Without it, nothing is sent.

.PARAMETER Draft
    Open them as drafts, so the diff is visible for your own review before maintainers are notified.

.PARAMETER Branch
    File only this one. Omit to handle both.

.EXAMPLE
    .\Open-AdapterOverridePr.ps1
    Prints the plan for both: the checks, the commits that will appear, and the bodies.

.EXAMPLE
    .\Open-AdapterOverridePr.ps1 -Commit
    Files both.

.EXAMPLE
    .\Open-AdapterOverridePr.ps1 -Branch fix/gpu-counter-dispose-race -Commit
    Files just the race fix.
#>
[CmdletBinding()]
param(
    [switch]$Commit,
    [switch]$Draft,
    [string]$Upstream = 'theantipopau/omencore',
    [string]$ForkOwner = 'tempestnano',
    [ValidateSet('8d87/adapter-power-override', 'fix/gpu-counter-dispose-race')]
    [string]$Branch,
    [string]$BaseBranch = 'main'
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

function Write-Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Write-Bad($text)  { Write-Host "   $text" -ForegroundColor Red }
function Write-Ok($text)   { Write-Host "   $text" -ForegroundColor Green }

# ── bodies ───────────────────────────────────────────────────────────────────────────────────────
# At column 0 because a here-string's closing delimiter has to start a line. Kept out of the
# function for that reason alone.

$adapterTitle = 'Restart the dGPU to drop an under-rated adapter''s GPU power clamp (board 8D87)'

$adapterBody = @'
On boards that clamp discrete GPU power when the adapter is under-rated, the clamp is driver state.
It reaches the NVIDIA driver as a one-off ACPI notification when the firmware evaluates the adapter,
and nothing lets the driver ask again — on board 8D87 (BIOS F.07) the verdict arrives only through
`Notify (PEGP, 0xDx)` from the EC query handler, and appears in no `_DSM` the driver can poll.

A driver that has just initialised therefore holds no verdict at all. So restarting the device drops
the clamp, with no EC access of any kind.

Measured on an OMEN MAX 16 (8D87) with a 280 W supply against a 330 W requirement: enforced limit
**35 W to 80 W**, and the GPU **drew 79.93 W** against it under load at 100% utilisation, with
battery discharge at 0.0 W. The card sitting on its ceiling, so the figure is delivered watts rather
than a reported limit.

### What this adds

`AdapterPowerOverrideService`, and a button in the Diagnostics adapter panel. Offered only when HP's
own `IsLowWattage` verdict is true and an NVIDIA dGPU is present, behind a confirmation dialog,
never on a timer.

- **It reports the limit, and says that is what it is.** `enforced.power.limit` is a requested
  number; a stalled GPU reports a raised limit while delivering nothing. The service says the limit
  moved, not that the watts arrived, and a restart that changes nothing is reported as a failure
  rather than quietly as success.
- **It says the clamp comes back.** The firmware re-evaluates on its own schedule. How long that
  takes is unmeasured, and the UI does not promise permanence.
- **It is board-scoped by construction.** Gated on HP's low-wattage verdict rather than on a board
  ID, so on a board that does not clamp GPU power it either is not offered or reports that nothing
  changed. Neither outcome asserts anything about hardware I have not measured.

### The part worth reviewing carefully

Restarting a display device invalidates the `NvmlDevice` handle LibreHardwareMonitor caches at
`Computer.Open()`. Calling through the stale handle faults inside the driver rather than returning
`NVML_ERROR_GPU_IS_LOST`, and .NET classes that as `AccessViolationException` — a corrupted-state
exception managed code is not permitted to catch. **The first run of this killed both OmenCore and
the hardware worker outright**, same second, same frame. There is no `try` that helps; the guard has
to be preventive.

`GpuRestartGate` does it in three parts:

| | Covers | Mechanism |
|---|---|---|
| Announce | calls not yet started | a marker file every poller reads before entering NVML — cross-process, because the two pollers share no memory, with an absolute expiry so a restarter that dies cannot leave GPU telemetry off for the session |
| Drain | the call already inside NVML | every poller holds a slot in a named semaphore for the duration of each call; the restarter acquires all sixteen, which it can only do once the last in-flight call has returned |
| Re-acquire | the handle afterwards | each poller closes and re-opens its `Computer` once the gate lifts |

The first two compose into a guarantee only in one order: a poller takes its slot **before** it
re-reads the marker, and the restarter sets the marker **before** it starts draining. So a poller
that passed the check is holding a slot the restarter must wait for, and one that has not taken a
slot will fail the check. Waiting out a poll interval instead — which is what this did first — is a
guess about someone else's timer.

Two decisions that look wrong until you hit them:

- **The slots are released as soon as they are all held**, not kept across the restart. Holding them
  adds nothing the marker does not already do, and a semaphore count is not returned when its holder
  dies — so a restarter that crashed mid-way would lock GPU telemetry out for the session, with no
  expiry able to reach it. That asymmetry is why the marker carries the expiry and the semaphore
  does not.
- **A drain that times out is a refusal**, not a longer wait. Something is still reading the card,
  and pulling it under that is the crash this exists to prevent.

`UpdateVisitor`'s callback changes from `Func<IHardware, bool>` ("skip?") to
`Func<IHardware, IDisposable?>` ("lease, or null to skip"). It calls `Update()` itself, so a bool
cannot span the call — and that visitor frame is where the app died.

### Stacking

Based on #162 (the 8D87 capability profile, which supplies the adapter decode this is gated on) and
#166 (the RTD3 poll guard, whose "do not touch the card right now" predicate the quiesce extends
rather than duplicating). Both sets of commits appear here and will drop out of the diff as those
merge.

### Testing

Full suite green. New coverage for the gate is in `GpuRestartGateTests`, including that a restart
cannot begin while a poller is inside NVML, that running out of slots is not misreported as a
restart, and that an expired or unparseable marker fails open rather than leaving telemetry off.
Those tests run against a private marker path and their own semaphore — the production names belong
to a running OmenCore, and draining its slots would stop its telemetry.

The device restart itself is verified by running it on the hardware, not by a unit test.

---

Reverse-engineering notes, measurements and the ACPI evidence for board 8D87 live outside this
repository; this PR states only what was measured.
'@

$raceTitle = 'Fix a shutdown race over the GPU engine performance counters'

$raceBody = @'
`WmiBiosMonitor.Dispose()` enumerates `_gpuEngineCounters.Values` while the monitoring path is still
adding to and removing from that dictionary:

```
System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
   at System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator.MoveNext()
   at OmenCore.Hardware.WmiBiosMonitor.Dispose()
   at OmenCore.Services.HardwareMonitoringService.Dispose()
   at OmenCore.ViewModels.MainViewModel.Dispose()
```

`RefreshGpuEngineCountersIfNeeded` rebuilds the dictionary whenever GPU engine instances appear or
disappear, which on a hybrid laptop is every time the dGPU wakes or sleeps. `Dispose` does not hold
`_updateGate` — it disposes it — so nothing was serialising the two.

It shows up as an intermittent test failure when a view-model test disposes a monitor whose poll is
mid-refresh, and in the field as a noisy shutdown. Being a race, it passes in isolation and fails in
a full run, which is the shape that tends to get attributed to whichever change landed most
recently.

### The fix

The dictionary gets a lock of its own. The slow work stays outside it deliberately:

- enumerating `PerformanceCounterCategory` — a WMI-ish call that can take a while
- `NextValue()` on each counter — milliseconds per instance, in the poll path
- disposing the counters

Only the mutations and the snapshots are inside the lock, so `Dispose` is never queued behind a poll
it is trying to stop. `Dispose` takes the counters out under the lock and disposes them after
releasing it, and wraps each in its own try so one counter failing to close cannot strand the rest.

`RefreshGpuEngineCountersIfNeeded` re-checks `_disposed` **inside** the lock, because the category
enumeration happens outside it: without the second check, a refresh that started before `Dispose`
could repopulate the dictionary afterwards and leak every counter it created, since nothing disposes
them a second time.

### Testing

Full suite green. Found while running the suite repeatedly against unrelated work — it is not
reproducible on demand, so there is no regression test that would be honest about what it proves.
The change is a lock around a dictionary that two threads reach, and the argument for it is the
stack trace above.
'@

$specs = @(
    [pscustomobject]@{
        Branch   = '8d87/adapter-power-override'
        Title    = $adapterTitle
        Body     = $adapterBody
        Note     = 'Stacks on #162 and #166, so their commits appear here and drop out of the diff as those merge.'
    },
    [pscustomobject]@{
        Branch   = 'fix/gpu-counter-dispose-race'
        Title    = $raceTitle
        Body     = $raceBody
        Note     = 'Standalone, straight off main. Should be a single commit.'
    }
)

if ($Branch) { $specs = $specs | Where-Object { $_.Branch -eq $Branch } }

# ── checks and filing ────────────────────────────────────────────────────────────────────────────

function Test-Preconditions {
    param([string]$Branch)

    $problems = @()

    $localSha = (git rev-parse $Branch 2>$null)
    if ($LASTEXITCODE -ne 0) { $problems += "Local branch $Branch does not exist." }

    $remoteSha = (git rev-parse "fork/$Branch" 2>$null)
    if ($LASTEXITCODE -ne 0) { $problems += "fork/$Branch does not exist. Push it first." }

    # A PR filed from a stale remote branch is the failure worth catching: the description says one
    # thing and the diff shows another.
    if ($localSha -and $remoteSha -and $localSha -ne $remoteSha) {
        $problems += "fork/$Branch is at $($remoteSha.Substring(0,7)) but local is at $($localSha.Substring(0,7)). Push before filing."
    }

    $existing = gh pr list --repo $Upstream --head "$ForkOwner`:$Branch" --state all --json number,state,url 2>$null | ConvertFrom-Json
    if ($existing -and $existing.Count -gt 0) {
        $problems += "A pull request already exists: $($existing[0].url) ($($existing[0].state))"
    }

    return ,$problems
}

# gh, once, before anything else. Every check below is a gh call.
try {
    gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Bad "gh is not authenticated. Run: gh auth login"; exit 1 }
} catch {
    Write-Bad "gh CLI not found on PATH."
    exit 1
}

$ready = @()

foreach ($spec in $specs) {
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor DarkGray
    Write-Host " $($spec.Branch)" -ForegroundColor White
    Write-Host ("=" * 78) -ForegroundColor DarkGray

    Write-Step "Preconditions"
    $problems = Test-Preconditions -Branch $spec.Branch

    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Bad " - $_" }
        continue
    }
    Write-Ok "pushed, up to date, and not already filed"

    Write-Step "Commits the maintainer will see"
    $commits = git log --oneline --no-merges "origin/$BaseBranch..$($spec.Branch)" 2>$null
    $count = ($commits | Measure-Object).Count
    Write-Host "   $count against $BaseBranch. $($spec.Note)"
    Write-Host ""
    $commits | Select-Object -First 30 | ForEach-Object { Write-Host "     $_" }
    if ($count -gt 30) { Write-Host "     ... and $($count - 30) more" }

    Write-Step "Title"
    Write-Host "   $($spec.Title)"

    Write-Step "Body"
    ($spec.Body -split "`n") | ForEach-Object { Write-Host "   $_" }

    $ready += $spec
}

if ($ready.Count -eq 0) {
    Write-Host ""
    Write-Bad "Nothing to file."
    exit 1
}

if (-not $Commit) {
    Write-Host ""
    Write-Host "DRY RUN. Nothing was sent. $($ready.Count) pull request(s) ready." -ForegroundColor Yellow
    Write-Host "Re-run with -Commit to file (add -Draft to open them as drafts)." -ForegroundColor Yellow
    exit 0
}

foreach ($spec in $ready) {
    Write-Step "Filing $($spec.Branch)"

    $bodyFile = Join-Path ([System.IO.Path]::GetTempPath()) "pr-body-$([guid]::NewGuid().ToString('n')).md"
    Set-Content -Path $bodyFile -Value $spec.Body -Encoding UTF8

    $ghArgs = @(
        'pr', 'create',
        '--repo', $Upstream,
        '--head', "$ForkOwner`:$($spec.Branch)",
        '--base', $BaseBranch,
        '--title', $spec.Title,
        '--body-file', $bodyFile
    )
    if ($Draft) { $ghArgs += '--draft' }

    & gh @ghArgs
    $code = $LASTEXITCODE

    Remove-Item $bodyFile -ErrorAction SilentlyContinue

    if ($code -ne 0) { throw "gh pr create failed for $($spec.Branch) with exit code $code" }
    Write-Ok "Filed."
}
