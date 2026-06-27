# OmenCore v3.8.2 - Critical Hang Fix

**Release Date:** TBD
**Release Status:** Code-complete, test-verified, and artifacts built in this environment; field confirmation from the original reporter on physical `8BCD` hardware still pending before tagging
**Type:** Patch release (release-blocker fix)
**Base Version:** v3.8.1

---

## Purpose

v3.8.2 exists solely to fix a critical regression reported within hours of the v3.8.1 release: OmenCore hangs and is force-closed by Windows ("Application Hang", Event ID 1002) within 10-20 seconds of launch. v3.8.1 is withdrawn as a recommended download pending this fix; see [3.8.1-BUG-REPORTS.md](3.8.1-BUG-REPORTS.md) for the full incident writeup (`BUG-3820-001`).

## Fixed

### Critical: Application Hang Within Seconds Of Launch (BUG-3820-001)

**Reported by:** OsamaBiden (Discord, OMEN 16-xd0010ax / ProductId `8BCD`), 2026-06-24, immediately after the v3.8.1 release. Two consecutive launches hung and were force-closed by Windows; Event Viewer confirmed `Application Hang`, `HangType=Cross-process`, `OmenCore.exe 3.8.1.0`.

**Root cause:** `HardwareWorkerClient.SendRequestAsync()` (the named-pipe client that talks to the out-of-process `OmenCore.HardwareWorker.exe`) reused a single `NamedPipeClientStream` across every request with no serialization and no recovery path after a timed-out read:

- If a worker response took longer than the client's `RequestTimeoutMs` (2000ms) — plausible under GC pauses, AMD ADL2/NVAPI driver calls, or just system load — the client's read was cancelled, but the now-late response message was left sitting, unconsumed, in the pipe's receive buffer.
- The *next* request's read would then consume that stale message instead of its own reply, permanently shifting every subsequent request/response pair by one. There was no request/response correlation and no reconnect-on-failure, so the connection never resynchronized itself.
- This was already visible in the field logs as the repeated `🥶 CPU/GPU temperature appears frozen` warnings (`WmiBiosMonitor.UpdateReadings`) immediately before the hang. The affected model (`8BCD`) has `_workerBackedCpuTempOverrideEnabled` set, so it calls into this exact pipe path on every monitoring cycle (every 2-5s) — far more aggressively than models that don't rely on the worker-backed CPU temperature override — which explains why this model hit the bug hard enough to hang within seconds while it went unnoticed elsewhere.
- The escalating retries from this desync (`WmiBiosMonitor`'s `Task.Run(...).Wait(timeout)` wrapper around the now-permanently-failing worker calls, fired every monitoring tick) is consistent with the eventual thread-pool/responsiveness exhaustion that Windows reported as a cross-process hang.

**Fix (`HardwareWorkerClient.cs`):**
- Added a `SemaphoreSlim(1, 1)` request gate around the entire write+read round-trip in `SendRequestAsync`, so concurrent callers queue instead of racing reads/writes on the shared pipe handle.
- On any write/read failure or timeout, the pipe handle is now disposed and nulled instead of reused. The existing `ShouldRecoverConnection`/`TryConnectToExistingWorkerAsync`/`TryRestartWorkerAsync` machinery (already used for worker-process-death recovery) now also handles this case, establishing a fresh, correctly-synchronized connection on the next call instead of perpetuating a desynced one.
- `WriteAsync`/`FlushAsync` are now covered by the same per-request cancellation token as the read (previously unguarded).
- Also fixed two newly-introduced bare `catch {}` blocks flagged by the repo's release-gate hygiene test (`ReleaseGateCodeHygieneTests`) to log the swallowed exception instead of silently discarding it.

**Why this was not a fan/thermal control change:** This is an IPC reliability fix in the telemetry transport layer only. No fan-control activation timing, debounce, EC-write gating, or thermal-protection threshold was touched — consistent with this project's standing rule that those require physical-hardware evidence before any change (see `feedback-omencore-safety` norms). The hang reproduces independent of any specific thermal event.

**Verification performed in this environment (not OMEN hardware):**
- Added `HardwareWorkerClientPipeTests` (2 new tests) using a real `NamedPipeServerStream`/`NamedPipeClientStream` pair with reflection-injected pipe state: one proves a non-responding server now disposes the client pipe instead of leaving it reusable; one proves 5 concurrent requests against a deliberately slow echo server each get back their *own* response, never another caller's.
- Full Release build: 0 errors.
- Full Release test suite: 895/895 passed (up from 894 — includes the 2 new tests and the hygiene-gate fix).
- Smoke-launched `OmenCore.exe` (Release build) on this dev machine (non-OMEN hardware) for 18+ seconds: process stayed responsive (`Get-Process.Responding = True`), clean shutdown, no errors/exceptions in the session log.

**Not yet done / explicitly still open:**
- The original reporter has not yet confirmed v3.8.2 resolves the hang on their `8BCD` hardware. Do not mark this "Fixed" in the public sense until they confirm — see Release Conditions below.
- This fix addresses the protocol-desync/hang mechanism; it does not change the deeper question of *why* the worker sometimes responds slowly on this model (driver contention, etc.). If slow responses continue, they should now degrade gracefully (timeout + clean reconnect) instead of cascading into a hang.

### Critical (Safety): Fans Stuck At Max Independent Of Temperature; Lid-Close Failed To Suspend, Followed By A BIOS Thermal Shutdown (BUG-3820-004)

**Reported by:** nsilveri ([GitHub #146](https://github.com/theantipopau/omencore/issues/146)), 2026-06-25. OMEN Laptop 15-en1xxx, ProductId `88D2`, AMD Ryzen 7 5800H + NVIDIA RTX 3070 hybrid GPU, v3.8.0. Fans were observed stuck at maximum speed independent of temperature; the reporter closed the lid to trigger standby, the laptop did not actually enter low-power standby (fans kept running, screen stayed black), and was later found powered on with a BIOS message reporting a shutdown due to overheating — while sitting closed inside a backpack.

**Root cause:** `WmiFanController`'s Max-mode keepalive/reassertion timer (`CountdownExtensionCallback`) runs on its own independent `System.Threading.Timer`, separate from `FanService`'s monitor loop, with no suspend awareness of its own:

- It was only stopped as a side effect reached partway through a *successful* `RestoreAutoControl()` call. `RestoreAutoControl()` had an early `if (!IsAvailable) return false;` guard *before* that point — any transient WMI unavailability (plausible during a suspend transition) skipped stopping the timer entirely.
- `FanService.HandleSystemSuspend()` only attempted the restore `if (FanWritesAvailable)` and silently discarded the result either way, so a failed or skipped restore during suspend left the keepalive timer running with no further attempt to stop it — while the rest of the system correctly proceeded to suspend.
- Net effect: if Max mode was active at lid-close and the BIOS auto-control restore failed or was skipped for any reason during the brief suspend transition window, the timer kept reasserting Max fan mode via WMI every ~8 seconds for as long as the process had threads scheduled, directly matching "fans remained at full speed" through lid-close — periodic background hardware I/O of exactly the kind that can interfere with a clean Modern Standby/S0ix transition.

**Fix:**
- `IFanController.StopCountdownExtension()` added as a default no-op interface method, overridden in `WmiFanControllerWrapper` to delegate to the real timer.
- `WmiFanController.RestoreAutoControl()` now stops the countdown timer unconditionally, first, before the `IsAvailable` check and before any reset-sequence logic — closing the gap for every caller (manual "switch to Auto," preset switching, suspend handling), not just suspend.
- `FanService.HandleSystemSuspend()` additionally calls `StopCountdownExtension()` directly and unconditionally (defense in depth for the case where fan writes are unavailable entirely and `RestoreAutoControl()` is never invoked), and now correctly logs whether the BIOS auto-control restore actually succeeded instead of unconditionally claiming success regardless of outcome.

**Scope discipline — what this does NOT change:** No fan curve, thermal-protection threshold, or EC-write gating logic was touched while the system is awake. The change is scoped entirely to suspend-time and restore-to-auto behavior, and strictly *reduces* background WMI write activity during those transitions rather than adding any.

**Investigated but deliberately not fixed in this patch:** the reporter also described the displayed "GPU temperature" as ambiguous and consistently higher than CPU temperature even with the dGPU confirmed idle. Code review found the root cause — `WmiBiosMonitor` unconditionally prefers the NVIDIA dGPU's NVAPI die-temperature reading over the WMI BIOS's own GPU reading whenever NVAPI is available, regardless of which GPU is actually active, and that value feeds fan-curve evaluation via `Math.Max(cpuTemp, gpuTemp)`. A correct fix needs a reliable "is the dGPU actually active" signal wired into the monitoring loop without regressing temperature accuracy on the majority of models where the NVIDIA dGPU genuinely is the active GPU — broader and riskier than this patch's evidence (a single report) justifies. See `BUG-3820-004` in [3.8.1-BUG-REPORTS.md](3.8.1-BUG-REPORTS.md) for the full writeup.

**Verification performed in this environment (not OMEN hardware):**
- Added `FanServiceSuspendTests.cs` (5 new tests) proving the keepalive timer is stopped on suspend in every failure mode: restore succeeds, restore returns `false`, restore throws, fan writes unavailable entirely, and a failure stopping the timer itself is caught rather than bubbling up.
- Full Release build: 0 errors, 0 warnings.
- Full Release test suite: 895/895 passed (see Current Validation Status below for the post-#146 count).

**Not yet done / explicitly still open:**
- The reporter has not yet confirmed v3.8.2 resolves the stuck-fan/failed-standby behavior on their physical `88D2` hardware.
- The GPU-temperature-source root cause remains unfixed pending a second corroborating report (ideally with the full diagnostics zip, not just the session log).
- Per explicit instruction, this fix was **not** rolled into a new installer/portable build for this round — see Current Validation Status.

### Diagnostics: WMI Command History, Hardware Info, And EC State Were Never Actually Collected (Affects GitHub #145 Evidence Gap)

**How this was found:** the `wmi-command-history.txt` supplied by a `MODEL-3810-002`/#145 reporter as the Battery Care evidence requested in [3.8.1-BUG-REPORTS.md](3.8.1-BUG-REPORTS.md) contained only the literal placeholder string `"WMI fan controller not available"` — on a machine where WMI fan control was demonstrably working throughout the same session's logs. That contradiction led to the real defect.

**Root cause:** `DiagnosticExportService.CollectAndExportAsync()` accepts optional `wmiController`/`hwMonitor`/`ecAccess` parameters that three collectors (`wmi-command-history.txt`, `hardware-info.txt`, `ec-state.txt`) depend on — but every production call site (`SettingsViewModel.ExportDiagnosticsAsync`, the "Export Diagnostics" button users are told to attach to GitHub issues, and `MainViewModel.ReportModelAsync`, the "Report Model" button) called it with none of these arguments. These three files have been the same "not available" placeholder in every diagnostics export any user has ever produced, regardless of their actual hardware state. Separately, even with correct wiring, `HpWmiBios.SetBatteryCareMode`/`GetBatteryCareMode` had no command-history recording at all — only `WmiFanController`'s fan commands were ever tracked — so Battery Care evidence specifically still would not have appeared.

**Fix (diagnostics-only; no fan/thermal/EC control behavior changed):**
- `HpWmiBios.cs`: added a command-history list and `GetCommandHistory()`, recorded at every success/failure exit point of `SendBiosCommand`/`SendBiosCommandLegacy` — the chokepoint every BIOS command (fan, GPU power, battery care, lighting, overdrive) routes through — so any WMI BIOS command attempt is now visible in diagnostics exports, not just fan commands.
- `DiagnosticExportService.cs`: `wmiController` is now also accepted as a constructor parameter with the same `?? ` fallback pattern already used for `monitoringService`/`fanService`, so future call sites can't silently omit it the way both existing ones did.
- `SettingsViewModel.cs` / `MainViewModel.cs`: both `DiagnosticExportService` construction sites now pass `_wmiBios`; the "Report Model" path now also passes `_hardwareMonitoringService`/`_fanService`, which it was missing entirely (its `hardware-info.txt`/resource-footprint sections were degraded too).

**Verification performed in this environment:** Full Release build 0 errors/warnings; full Release test suite 900/900 (no new tests added — this is plumbing existing, already-tested collector logic to data it never previously received; the collectors' own reflection-based parsing was already exercised by existing diagnostics tests).

**Not yet done / explicitly still open:** the next diagnostics zip exported by any user is the actual remaining evidence needed before any `CMD_BATTERY_CARE` command-handling change for #145 — this fix only makes that evidence collectible, it does not itself resolve the underlying Battery Care WMI failure.

### Power Automation Never Applied The Battery/AC Profile At Boot (Discord, OMEN MAX 16 `8D41`)

**Reported by:** ACF (Discord), 2026-06-27, OMEN 16 Max ah0500na — one of nine items reported on this model; see `BUG-3820-005` in [3.8.1-BUG-REPORTS.md](3.8.1-BUG-REPORTS.md) for the full list, most of which are confirmed deliberate model-safety gates or NVAPI/firmware-reported hardware locks rather than bugs.

**Root cause:** `PowerAutomationService.ApplyCurrentProfile()` — explicitly commented "useful for initial setup" — had no caller anywhere in the codebase. The service only reacted to `SystemEvents.PowerModeChanged`; on a fresh launch that event hasn't fired, so a user with Power Automation enabled kept whatever fan/performance state was last manually set or startup-restored, regardless of their configured AC/Battery profile, until the next AC↔battery transition. This was not model-specific.

**Fix:** added a call to `_powerAutomationService.ApplyCurrentProfile()` at the end of `MainViewModel.RestoreSettingsOnStartupAsync()`, after the existing GPU-Power-Boost/fan-preset/TCC-offset startup restores, wrapped in its own try/catch. It runs last so Power Automation (if enabled) has final say over the generic last-state restores, matching the feature's purpose. The method already no-ops internally when Power Automation is disabled, so this is zero-risk for users who don't use the feature.

**Verification performed in this environment:** Full Release build 0 errors/warnings; full Release test suite 900/900.

**Not yet done / explicitly still open:** reporter confirmation that the Battery/AC profile is now applied at boot; the other eight items from this report remain either confirmed-by-design (CPU/GPU power-limit locks, Dynamic Boost) or evidence-needed (RGB light-bar routing, battery-preset-name substitution to "Custom"/"Balanced", Optimizer "Disable Last Access Timestamps" failure).

## Minor Improvements (Code-Quality / Reliability Polish)

These are small, hardware-independent cleanups verified by build + the full test suite. They do **not** touch any fan/thermal/EC control path and carry no behavior risk; they ride along with the hotfix because they are zero-risk and thematically aligned with its telemetry-reliability/diagnosability focus.

- **Removed per-poll wasted work in the hardware-worker telemetry path.** `HardwareWorkerClient.GetSampleAsync()` previously logged a debug line on every ~2-second worker poll that ran an `O(n)` `json.Contains("GpuTemperature")` substring scan over the full telemetry payload purely to format a boolean — work that executed on every poll even though the message is dropped at the sink in production. Removed; the adjacent "Deserialized sample" debug line already records the parsed result. Eliminates a recurring per-poll string-build + substring scan on the monitoring hot path (relevant to the standing background-resource concern).
- **Made the diagnostic-logging subsystem's own failures visible.** Converted three bare `catch {}` blocks in `DiagnosticLoggingService` (capture-task shutdown wait, and the relevant-process enumeration loop) to typed catches — `Debug.WriteLine` for single-point failures, a typed silent skip for the per-process inspection loop where logging would be noise across normal system processes. This is the same "logs just stop with no trace" lesson that made `BUG-3820-001` hard to diagnose, applied to the diagnostic subsystem itself. Baseline updated in `ReleaseGateCodeHygieneTests`.
- **Cleared all build warnings and a stale hygiene-baseline entry** (carried from earlier this session): four `CS1998` async-without-`await` warnings in `BloatwareManagerViewModel` (the preset methods did purely synchronous work) were resolved by making the methods synchronous; the resolved `HardwareWorkerClient` bare-catch baseline entry from the hang fix was removed. The main app now builds with **0 warnings**.

## Carried Forward From v3.8.1 (Unchanged, Still Hardware-Gated)

These items were already pending hardware validation in v3.8.1 and are out of scope for this hang-fix patch. They are listed here only so the release gate isn't mistaken for "everything closed." See [3.8.1-BUG-REPORTS.md](3.8.1-BUG-REPORTS.md) for full detail:

- GitHub #141 (OMEN 16-ap0xxx key routing / shipped-artifact provenance) — needs physical `8D26` key-event capture.
- GitHub #142 (HyperX OMEN MAX 16 `8E9A` identity) — needs full diagnostic evidence before any exact-identity entry is added.
- GitHub #143 (Victus 15 `8DCD` fan/thermal regression) — needs a bounded, abortable physical load test.
- BUG-3810-005 (Discord fan-spike-at-idle reports) — diagnostics-only change shipped in 3.8.1; needs an affected user's diagnostic export before any activation-timing change is considered.
- PERF-3810-001 resource/responsiveness scenario matrix — needs physical OMEN/Victus hardware to measure against budget.
- AMD GPU OC startup-restore — still manual-only by design; not revisited in this patch.

## Release Conditions

- This patch does not get marked "Fixed" for BUG-3820-001 from this environment's testing alone — it requires the original reporter (or another `8BCD`/worker-override-enabled user) to confirm v3.8.2 launches and runs without hanging.
- All carried-forward items above remain pending exactly as documented in v3.8.1; nothing here should be read as resolving them.
- Version files and release artifacts move to 3.8.2 only for this hang fix; no unrelated version-gated claims are made.

## Current Validation Status

- `dotnet build OmenCoreApp.csproj -c Release`: passed, 0 errors, 0 warnings.
- `dotnet test OmenCoreApp.Tests.csproj -c Release`: passed, 900/900 (895 from the BUG-3820-001 hang fix and minor improvements, plus 5 new `FanServiceSuspendTests` added for BUG-3820-004; the diagnostics-wiring fix and BUG-3820-005 Power Automation startup-apply fix are covered by this same 900/900 run — no net new tests, as both are plumbing/call-site fixes exercised by existing collector and startup-restore coverage).
- Smoke launch of the built `OmenCore.exe` on this (non-OMEN) dev machine: ran 18+ seconds responsive, clean exit, no exceptions logged. (Pre-dates the BUG-3820-004 fix and everything after it; see artifact note below.)
- Version metadata bumped to `3.8.2` across `VERSION.txt`, `OmenCoreApp`, `OmenCore.Avalonia`, `OmenCore.Linux`, `OmenCore.HardwareWorker` project files, the installer script (`OmenCoreInstaller.iss`), the wizard-image generator default, and the Avalonia version fallback string.
- **Artifact note — important:** the Windows/Linux artifacts and SHA256 hashes below were built *before* the BUG-3820-004 fix (fans-stuck-at-max / failed-standby / BIOS thermal shutdown), the diagnostics-export wiring fix (#145 evidence gap), and the BUG-3820-005 Power Automation startup-apply fix. Per explicit instruction, installers were **not** rebuilt after any of these while still waiting on pre-release feedback for the BUG-3820-001 hang fix — the source code on this branch is ahead of these binaries. Do not publish these specific artifacts as containing any of the post-hash fixes; they must be rebuilt before any tag/release that is meant to include them.
  - Windows artifacts built with `build-installer.ps1`: `OmenCoreSetup-3.8.2.exe` and `OmenCore-3.8.2-win-x64.zip`.
  - Linux artifact built with `build-linux-package.ps1 -SkipBinaryVersionCheck`: `OmenCore-3.8.2-linux-x64.zip`, `.sha256`, and `version.json`. Binary execution smoke was skipped because this run was on Windows, not Linux/WSL (`binaryExecutionSkipped: true` in the verification manifest).
  - Artifact SHA256 (also recorded in `artifacts/SHA256SUMS-3.8.2.txt`):
    - `F6FEAB2DDB13E1E70470C7665A414F41E219A96E56E35F0C43C9AB3F595EA86E  OmenCoreSetup-3.8.2.exe`
    - `A61D81D36CFF0839A9E74DCC5C31337318BA258A5F43C5F4C2E9AC5BF6D2E895  OmenCore-3.8.2-win-x64.zip`
    - `B37C02B0FDA17743A95094685D8EDAD182EAB50FF98BA0711093B166FDCB2EBC  OmenCore-3.8.2-linux-x64.zip`
- No claim is made that either fix has been validated on physical OMEN hardware; this development environment is not HP hardware. Reporter confirmation is the actual acceptance criterion for both BUG-3820-001 and BUG-3820-004.
