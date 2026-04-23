# OmenCore — Systemic Audit Report
**Date:** 2026-04-15  
**Scope:** Full codebase audit with primary focus on the v3.2.5 → v3.3.0 regression window  
**Status:** Pre-fix analysis — no implementation changes in this document  

---

## Executive Summary

The v3.3.0 release introduced **94 changed files, 11,930 insertions, 1,476 deletions** relative to v3.2.5. This is not a patch release — it is a large feature delivery shipped simultaneously with stability fixes. The combination created layered complexity, introduced at least one critical thread-safety violation in the monitoring core, and left old logic running alongside new logic in several key systems.

The pattern across nearly all 3.3.0 regressions is the same:
> New behaviour was added on top of existing code paths instead of replacing them. When the new code contained a bug, the old suppression/fallback code masked it on some systems (English Windows) while it crashed on others (all non-English locales).

---

## Phase 0 — Regression Analysis: v3.2.5 → v3.3.0

### Scope of Change

| Metric | Value |
|---|---|
| Files changed (src) | 94 |
| Lines inserted | 11,930 |
| Lines deleted | 1,476 |
| Insertion:deletion ratio | **8.1:1** |
| Core monitoring files changed | 5 |
| ViewModel files changed | 10 |
| View files changed | 12 |

An 8:1 insertion-to-deletion ratio is a strong architectural signal. This means the release added 8 lines for every 1 it removed. In a stability release, the inverse should be true.

---

### High-Risk Changes by System

#### 1. `HardwareMonitoringService.cs` — THE ROOT CAUSE (Critical)

**What changed:** Added `GetEffectiveCadenceInterval()` — a polling cadence adapter that checks whether the main window is visible or minimised.

**The bug:** The method accessed `Application.Current.MainWindow.IsVisible` and `window.WindowState` — both WPF `DependencyObject` properties that are thread-affine. `GetEffectiveCadenceInterval()` was called on every iteration of the background monitor loop (`Task.Run`). Every iteration threw `InvalidOperationException`.

**Why it was hidden on English Windows:** The `OnUnobservedTaskException` handler in `App.xaml.cs` was expanded in the same release to include an English string check `invalidOp.Message.Contains("different thread owns it")`. This suppressed the exception silently on English-locale systems. The fix and the bug shipped together, and the fix was locale-dependent.

**What was NOT replaced:** The original `_lowOverheadMode` / `_baseInterval` / `_lowOverheadInterval` polling logic still exists. `GetEffectiveCadenceInterval()` was layered on top without removing the prior system. Result: three parallel concepts governing the same interval — `_lowOverheadMode`, `_baseInterval`, and the new cadence system — with no clear ownership.

**Files:** `HardwareMonitoringService.cs` (cadence logic), `App.xaml.cs` (exception suppressor)

---

#### 2. `MainViewModel.cs` — Monitoring Pipeline Redesign (High Risk)

**What changed:** 3.3.0 introduced a `QueueMonitoringUiSample` / `NormalizeMonitoringSample` abstraction to coalesce monitoring updates before UI dispatch. It also added `Safe Mode` — a startup window that gates hardware write actions, checked via `Task.Run(() => PostResumeSelfCheckAsync(...))`.

**Issues identified:**

1. **Two monitoring update paths coexist.** The original `LatestMonitoringSample` property setter still contains the full update pipeline (`_general?.UpdateFromMonitoringSample`, `OnPropertyChanged` for all derived properties). `QueueMonitoringUiSample` is a new wrapper that calls `BeginInvoke → consume queue → set LatestMonitoringSample`. If `QueueMonitoringUiSample` and a direct `LatestMonitoringSample` assignment both occur, updates can arrive out of order.

2. **`NormalizeMonitoringSample` mutates the sample object in-place** before assigning it. The monitoring service hands the same sample reference to multiple subscribers (via `GetInvocationList` fan-out). If any subscriber received the sample before `MainViewModel` normalizes it, they see the raw values; the normalized values are visible only through `LatestMonitoringSample`. The mutation is not thread-safe — it occurs on the dispatcher thread while the monitoring loop may have already delivered the same object reference to other consumers.

3. **`PostResumeSelfCheckAsync` is a `Task.Run` fire-and-forget** that checks monitoring health 45 seconds after resume. The result is logged but no corrective action is taken (it only logs). This is a telemetry stub shipped as if it were a diagnostic feature.

4. **Safe Mode (`IsSafeModeActive`)** gates tray actions behind a 30-second startup window but the gate is checked in the tray action dispatcher, not in the fan/performance services. Services can still be called directly from other paths (hotkeys, automation) while Safe Mode is nominally active.

**What was NOT replaced:** The original direct `SampleUpdated` subscribing in `DashboardViewModel`, `SystemControlViewModel`, and `LightingViewModel`. Each of these still has its own `PropertyChanged` subscription on `LatestMonitoringSample` AND a `SampleUpdated` event subscription. This means every monitoring sample triggers two independent update paths per consumer.

---

#### 3. `LightingViewModel.cs` — SampleUpdated Dispatcher Fix (Incomplete)

**What changed:** The dispatcher-coalescing work (`BeginInvoke` wrapping on `SampleUpdated`) was applied to `DashboardViewModel`, `SystemControlViewModel`, and `MainViewModel`. `LightingViewModel.OnMonitoringSampleUpdated` was **not updated**.

**Result:** `LightingViewModel.OnMonitoringSampleUpdated` continued to access WPF-bound properties (`CpuTempThresholdHigh`, `_keyboardLightingService.IsAvailable`) on the background monitor thread. This threw `InvalidOperationException` on every monitoring tick when temperature-responsive or throttling lighting was active. The exception propagated through the subscriber fan-out in `HardwareMonitoringService`, was not caught by the per-subscriber guard (which was added in the same 3.3.0 changeset), and starved the watchdog heartbeat from `MainViewModel`.

**Status:** Fixed in 3.3.0 (the per-subscriber isolation caught it), but this means the `LightingViewModel` is still calling back on the wrong thread — the fix is defensive, not corrective.

---

#### 4. `BackgroundTimerRegistry.cs` — New Global State (Medium Risk)

**What added:** A new static `BackgroundTimerRegistry` class was introduced to track all active background timers for diagnostic telemetry. Every service now calls `BackgroundTimerRegistry.Register/Unregister` at start/stop.

**Issues:**
- It is a static singleton with no thread-safety annotations. `Register` and `Unregister` use a `ConcurrentDictionary` but the `Register` call in `HardwareMonitoringService.Start()` runs on the UI thread (called from `App.xaml.cs`), while `Unregister` in `Stop()` can run from the GC finalizer thread or a background task. These are additive operations on a concurrent collection so they are safe, but callers assume a specific registration order that is not enforced.
- `UpdateCadenceTelemetry` calls `Unregister` then `Register` on every polling tick when the cadence changes. On a 1-second cadence with window state changes, this can be called 10+ times per minute, adding noise to any telemetry dashboard that consumes this registry.

---

#### 5. `ResumeRecoveryDiagnosticsService.cs` — New Service, Incomplete Integration (Low-Medium Risk)

**What added:** A new `ResumeRecoveryDiagnosticsService` that records suspend/resume recovery steps to a timeline. It is injected (optionally, with `= null` default) into `HardwareMonitoringService`, `FanService`, `HardwareWatchdogService`.

**Issues:**
- The service is optional everywhere (`? null`). All call sites guard with `_resumeDiagnostics?.RecordStep(...)`. This means it can be silently absent and none of the callers will know.
- The resume diagnostic is written to a file but there is no UI surface that shows the user the content (the Settings model identity card doesn't show this). The data is collected but not acted upon.
- `CurrentCycleId` is an int incremented on each sleep/wake. The watchdog uses it to check if the resume cycle is still the same one. This is a correlation ID design layered on the watchdog rather than a proper event/observable pattern.

---

#### 6. `SystemControlViewModel.cs` — 1,164 Changed Lines (High Risk)

**What changed:** The largest single file change in 3.3.0 (+1,164 lines). Added NVIDIA per-device tuning (Test Apply workflow, per-device ranges, voltage offset, profile tiers), AMD Radeon curve optimizer wording, power-limit-only system detection, GPU OC profile load with own voltage state, NVAPI power-only restore on startup.

**Risk:** 1,164 changed lines in a file that manages GPU power limits, fan control decoupling, and MUX switch state means regression surface is very large. This file interacts directly with hardware through NVAPI and WMI. The new Test Apply / auto-revert workflow introduces a time-dependent state machine that is not tested.

---

#### 7. `WmiBiosMonitor.cs` — 450 Changed Lines (High Risk)

**What changed (+450 lines):** Significant expansion of temperature fallback logic, new metrics (power consumption, battery health), async temperature reads.

**Issues found:**
- `WmiBiosMonitor.cs` line 917: `fallbackTask.Result` — this blocks the calling thread waiting for a `Task.Run` result. If called from a context with a synchronization context (e.g., a dispatcher continuation), this is a potential deadlock.
- Multiple `catch { }` blocks throughout (lines 1582, 1602, 1678, 1693, 1754, 1839) swallow exceptions silently with no logging. Failures in temperature fallback are invisible.

---

### Summary: New Logic vs. Replaced Logic

| System | Old Logic Removed | New Logic Replaces | Verdict |
|---|---|---|---|
| Monitoring cadence | ❌ No (still has `_lowOverheadMode`, `_baseInterval`) | `GetEffectiveCadenceInterval()` added on top | Layered |
| Monitoring update dispatch | ❌ No (direct property setter still runs) | `QueueMonitoringUiSample` added on top | Layered |
| Sample normalization | ❌ No (NormalizeMonitoringSample mutates shared object) | Added on top of setter | Layered |
| Fan verification RPM check | ✅ Yes (removed 50 RPM change check loop) | Replace with curve-existence check | Replaced |
| Fan verification revert | ✅ Yes (removed `SetFanMode(Default)` on failure) | Now logs warning only | Replaced |
| WatchdogService timer | ❌ No (old timer still runs) | Added `BackgroundTimerRegistry` alongside | Layered |
| Exception filter in App.xaml | ❌ No (string match kept, new stack-trace check added) | Extended not replaced | Partially layered |
| LightingViewModel dispatcher | ❌ No (VM still calls on wrong thread) | Per-subscriber catch isolates it | Safety net, not fix |

---

## Phase 1 — Thread Safety & Hidden Exception Risks

### Category A: WPF Object Access from Background Threads

These locations access WPF `DependencyObject` / `Application.Current.MainWindow` from contexts that are **not guaranteed to be the UI thread**:

| Location | Risk Level | Notes |
|---|---|---|
| `HardwareMonitoringService.GetEffectiveCadenceInterval()` (3.3.0) | ~~Critical~~ **FIXED in 3.3.1** | Root cause of startup crash |
| `HardwareMonitoringDashboard.xaml.cs` line 208 — `Application.Current?.MainWindow` in `UpdateChartSuppression()` | **High** | Called from `IsVisibleChanged` and `StateChanged` (UI thread, safe) BUT also called from `InitializeWithViewModel()` Task.Run block at line 113. Must audit call chain. |
| `HardwareMonitoringDashboard.xaml.cs` line 215 — `Application.Current?.MainWindow` in `UpdateChartSuppression()` | **Medium** | Same as above |
| `MainViewModel.cs` line 3760 — `Application.Current.MainWindow` **without null check** | **Medium** | Inside `BeginInvoke` lambda (safe if `Application.Current` is live), but `.MainWindow` without `?.` will throw if MainWindow is null during shutdown |
| `MainViewModel.cs` line 2176 — `Owner = Application.Current.MainWindow` | **Medium** | Dialog owner assignment — safe only if called from UI thread (review call site) |
| `MainViewModel.cs` line 2920 — `Owner = Application.Current.MainWindow` | **Medium** | Same |
| `LightingViewModel.cs` line 1214 — `Owner = Application.Current.MainWindow` | **Medium** | Same |
| `LightingViewModel.cs` line 1667 — `Owner = Application.Current.MainWindow` | **Medium** | Same |
| `LightingViewModel.OnMonitoringSampleUpdated` | **High** | Accesses WPF properties before first `await`. Called from monitoring background thread. Currently masked by per-subscriber catch. Root fix not applied. |
| `WmiBiosMonitor.cs` line 917 — `fallbackTask.Result` (blocking await) | **High** | Blocks calling thread. If called with a SynchronizationContext active, this is a deadlock. |
| `ThermalSensorProvider.cs` line 84 — `.GetAwaiter().GetResult()` | **High** | Same deadlock risk |
| `KeyboardLightingService.cs` line 140 — `.GetAwaiter().GetResult()` | **Medium** | Comment says "IMPORTANT: Do NOT use on UI thread" but call site must be audited |
| `RgbSceneService.cs` line 700 — `StopAsync().GetAwaiter().GetResult()` | **Medium** | Same deadlock risk in Dispose |

### Category B: Exception Suppression via String Matching

| Location | Pattern | Risk |
|---|---|---|
| `App.xaml.cs` line 1155 | `ex.Message.Contains("different thread", OrdinalIgnoreCase)` | **Medium** — locale-safe (uses `OrdinalIgnoreCase` + "different thread" is shorter than original), retained as safety net post-3.3.1 fix |
| `FanController.cs` line 391 | `ex.Message.Contains("mutex", OrdinalIgnoreCase)` | **Medium** — suppresss mutex contention exceptions; may hide real WMI lock failures |

### Category C: Silent Exception Swallowing (No Logging)

These `catch { }` blocks permanently hide errors with no trace:

| Location | Count | Impact |
|---|---|---|
| `WmiBiosMonitor.cs` | 8 bare `catch { }` or single-line swallow | Temperature fallback failures completely invisible |
| `DiagnosticExportService.cs` | 8 bare `catch { }` | Export failures silently produce incomplete reports |
| `FanControllerFactory.cs` | 5 bare `catch { }` | Fan backend selection failures hidden |
| `FanCalibrationControl.xaml.cs` line 68 | 1 bare `catch { }` | Calibration init failure swallowed (bug masked by this) |
| `LightingViewModel.cs` lines 2010, 2028 | 2 bare `catch { }` | RGB apply failures invisible |
| `TrayIconService.cs` | Multiple `Debug.WriteLine` only | Tray style failures go to debug trace, not log file |
| `App.xaml.cs` lines 984, 994 | 2 bare `catch { }` | Startup sequence failure states silent |

### Category D: Fire-and-Forget Tasks Without Error Propagation

| Location | Risk |
|---|---|
| `MainViewModel.cs` line 1775 — `_ = Task.Run(() => PostResumeSelfCheckAsync(...))` | Result never observed; exceptions silently become unobserved |
| `MainViewModel.cs` line 3260 — `_ = Task.Run(ProcessTrayActionQueueAsync)` | Same |
| `LibreHardwareMonitorImpl.cs` lines 517, 979, 1092, 1174, 1288 — multiple `Task.Run(() => Reinitialize())` | Reinitialize failures completely silent |
| `HardwareMonitoringService.cs` — `_ = Task.Run(() => MonitorLoopAsync(...))` | The loop itself is unwrapped; if it throws unhandled the service silently dies |
| `RazerService.cs` — multiple `Task.Run(async () => ...)` with only `task.IsCompletedSuccessfully` checks | Failures silently return false |

---

## Phase 2 — Redundant / Obsolete Code Identified

### 1. Dual Monitoring Interval System

**Old system:** `_baseInterval` + `_lowOverheadInterval` + `_lowOverheadMode` flag. Interval determined by: `_lowOverheadMode ? _lowOverheadInterval : _baseInterval`.

**New system (3.3.0):** `_activeCadenceInterval` (1s) + `_idleCadenceInterval` (5s) + `_uiWindowActive` flag (3.3.1). Interval determined by: `_uiWindowActive ? _activeCadenceInterval : _idleCadenceInterval`.

**Status:** Both systems coexist. `_baseInterval` and `_lowOverheadInterval` are still set and logged but their values are never read by `GetEffectiveCadenceInterval()`. `SetBaseInterval()` / `SetLowOverheadMode()` public methods still exist on the service and are called externally, but have no effect on the actual polling interval. This is silent dead code that will confuse future callers.

---

### 2. Dual `SampleUpdated` + `PropertyChanged(LatestMonitoringSample)` Subscription

In `HardwareMonitoringDashboard.xaml.cs`: two event subscriptions deliver the same data:
- `SampleUpdated` event → `HandleMonitoringSignalAsync()` (lines 107-112)
- `PropertyChanged(LatestMonitoringSample)` on `DashboardViewModel` → `HandleMonitoringSignalAsync()` (lines 153-156)

If DashboardViewModel is present, both fire for the same sample. `HandleMonitoringSignalAsync` calls `UpdateMetricsAsync()` which calls `CheckForAlertsAsync()` — both doing the same work twice per sample.

---

### 3. `_pendingUIUpdate` Flag (Original Update Throttle) + `_monitoringUiUpdateQueued` (New Queue)

Two separate mechanisms to prevent UI update backlog:
- Old: `volatile bool _pendingUIUpdate` — set/cleared in `HardwareMonitoringService`
- New: `bool _monitoringUiUpdateQueued` + `_monitoringUpdateLock` in `MainViewModel`

Both are active simultaneously. The old `_pendingUIUpdate` in `HardwareMonitoringService` gates `BeginInvoke` calls. The new `_monitoringUiUpdateQueued` in `MainViewModel` gates the queued sample dispatch. They operate on different objects in the same pipeline — the combined effect is two throttle layers with independent state. Neither was documented as intentional.

---

### 4. `LoggingService.Error` Overload Collision

The 3.3.0 `LoggingService` added `[CallerMemberName]` and `[CallerFilePath]` optional parameters to `Error(string message, Exception? ex = null, ...)`. Any existing call `logging.Error("msg", ex)` now silently hits the new overload and emits a structured telemetry payload instead of the plain `"msg: ex"` format. The log format changed without a version bump or migration. Any log parsing, regex searching, or alert rules based on the old format will silently break.

---

### 5. `QueueMonitoringUiSample` Coalesce vs. Direct Assignment

Both code paths write to `LatestMonitoringSample`:
- `QueueMonitoringUiSample` → dispatches via `BeginInvoke` → consumes queue → calls `LatestMonitoringSample = ...`
- Direct: `LatestMonitoringSample = value` from `OnMonitoringSampleReceived`

If both paths are active (e.g., during wake from suspend while the queue flush is in-flight), two assignments can arrive for the same tick. The `NormalizeMonitoringSample(value, _latestMonitoringSample)` call uses `_latestMonitoringSample` as the "previous" reference — but if the previous assignment hasn't been committed yet, the normalization compares against stale state.

---

### 6. `ResumeRecoveryDiagnosticsService` — Telemetry Stub

`PostResumeSelfCheckAsync` in `MainViewModel` fires 45 seconds after resume. It reads `HardwareMonitoringService.HealthStatus`, checks if monitoring is running, and logs the result. It takes no corrective action. The `ResumeRecoveryDiagnosticsService` is written to file as `resume-recovery.txt` but:
- The UI Settings page does not surface this file content
- No alert is triggered when recovery fails
- The only consumer is `DiagnosticExportService` (which bundles it into exports)

This is telemetry infrastructure that was shipped before the consumer (the UI card and alert) was built.

---

## Phase 3 — Core System Integrity Review

### 1. Monitoring Loop

**State of truth:** `HardwareMonitoringService.MonitorLoopAsync` runs on a thread pool thread. It reads hardware, fires `SampleUpdated` to per-subscriber isolated handlers, then sleeps for `GetEffectiveCadenceInterval()`.

**Integrity issues:**

- **Three interval sources** with unclear priority: `_baseInterval`, `_lowOverheadMode`, `_uiWindowActive` (see Phase 2 §1)
- **No loop restart guard.** If `MonitorLoopAsync` throws unhandled past the `maxErrors` gate, the method returns and the background task completes. No external watchdog restarts the monitor loop itself. The `HardwareWatchdogService` triggers failsafe fans but cannot restart the loop — it is a separate service.
- **`consecutiveErrors` resets after the 10-second backoff,** not after a successful read. An alternating error/success pattern can reset `consecutiveErrors` to 0 while the underlying problem persists.
- **`_restartInProgress` flag** (bridge restart) is a simple bool, not a proper lock. It prevents concurrent restarts but a race between the error path (`_restartInProgress = true`) and cleanup (`_restartInProgress = false`) exists if two exceptions arrive in the same iteration after a partial restart.

### 2. Sensor & Telemetry Pipeline

**Integrity issues:**

- **Sample object mutation after delivery.** `NormalizeMonitoringSample` in `MainViewModel` mutates `sample.CpuLoadPercent`, `sample.GpuLoadPercent`, `sample.CpuTemperatureC`, `sample.GpuTemperatureC` in-place. Because `SampleUpdated` delivers the object reference before normalization (it runs in a different subscriber lambda on the dispatcher thread), subscribers that received the object first see un-normalized values.
- **Stale load inference.** The `NormalizeMonitoringSample` uses `previous?.CpuTemperatureC` as a fallback when temperature state is invalid. The "previous" value is `_latestMonitoringSample` — which may itself be un-normalized at the time of comparison. Previous + current both potentially stale.
- **`_cachedRamUsage` fallback** in `LibreHardwareMonitorImpl.cs` line 604: `catch { _cachedRamUsage = totalGb * 0.5; }` — on any computation error, RAM usage is silently set to 50% of total RAM. Users see a flat 50% line in the monitoring chart rather than an error state.

### 3. Fan Control

**Integrity issues:**

- **`FanService.VerificationPasses()`** now returns `true` for any curve-based preset if `preset.Curve != null && preset.Curve.Count > 0`. This means a zero-point curve (technically valid, count > 0) passes verification. The original RPM-change check, while flawed, tested that the hardware actually responded. The replacement tests only that the data model is non-null.
- **`FanVerificationService` is still running.** Despite the critical fix removing the destructive `SetFanMode(Default)` revert, `FanVerificationService` still attempts periodic RPM verification. Its results are logged as warnings but not used. The service runs a timer (every `VerificationIntervalMs`) that wakes up, reads fan RPMs, compares to expected, and logs. This is background I/O load with no downstream value.
- **Two fan control backends coexist:** `WmiFanController` (WMI path) and `EcFanControllerWrapper` (EC path). Both can be active on the same model. Fan control routing through `FanControllerFactory` selects a backend at startup, but `FanService` accepts both via `IFanController`. If a model switches from WMI to EC mid-session (e.g., after OGH detaches), the backend is not hot-swapped.
- **V1 BIOS `SetFanLevel(0,0)` call after `SetFanMode(Default)`** — added as a workaround for fans staying at 100%. This writes to hardware twice for every auto-mode restore. On models where it was not tested, this could produce unexpected fan behavior.

### 4. Model Capability System

**Integrity issues:**

- **`ModelCapabilityDatabase` and `KeyboardModelDatabase` are maintained independently** with duplicate product ID entries. Adding a model requires updating two files manually with consistent data. There is no validation that a product ID in the keyboard DB has a corresponding entry in the capability DB (or vice versa). Models 8D24 and 8D2F are now in both; `8C2F` is only in the keyboard DB and the capability DB separately — but there is nothing that enforces this.
- **`UserVerified = false` entries** are treated identically to `UserVerified = true` at runtime. The field exists for documentation but has no behavioral effect. An unverified entry for a model with wrong capabilities (e.g., wrong MaxFanLevel or wrong flag for EC access) will silently damage hardware operations.
- **Family fallback resolution returns `OMEN16` defaults for all unknown 16" models.** If `MaxFanLevel` for the real hardware is 100 but the family fallback uses 55, fan curves will be clamped to 55 even though the hardware can run higher. The family fallback is conservative but undiscoverable — there is no warning that the fallback may be wrong.

---

## Phase 4 — External Comparison Findings

*Based on analysis of: OmenMon, arfelious/omen-fan-control, alou-S/omen-fan, noahpro99/omenix*

### Key Findings

**1. OmenMon (C#, Windows) — closest architectural sibling**

OmenMon uses a **synchronous main-thread polling model**. Temperature reads, fan writes, and UI updates all occur on the same thread. This is less scalable but eliminates the entire category of WPF cross-thread violations. OmenMon has three defining patterns OmenCore should adopt:
- **EC countdown timer integration:** every fan write resets a countdown timer; the BIOS resets control after 120s without writes. OmenMon explicitly manages this. OmenCore's `FanService` does not have an explicit countdown — the curve engine re-applies every poll tick, which works, but is not documented as intentional countdown management.
- **State persistence to EC registers:** fan mode and GPU power state are stored in EC registers on shutdown, not in a config file. Restore on startup reads from hardware, not from a potentially-stale cached value.
- **Blocking WMI + EC fallback chain.** If WMI fails, OmenMon tries EC. OmenCore has this chain but it is implicit — the `FanControllerFactory` selects backend at startup and does not re-probe.

**2. omenix (Rust, Linux) — best threading model**

omenix uses a **Rust channel (mpsc)** to communicate between the temperature monitor thread and the fan control thread. The UI (GTK) runs its own loop and receives updates via a `Receiver`. Zero WPF-equivalent cross-thread property access is possible — the architecture physically separates the threads. The key insight: **never share state directly; communicate via messages.** OmenCore should move toward a similar model where the monitoring loop publishes samples to a channel/queue and the UI consumes from it, never the other way around.

**3. The 120-second BIOS reset (all Linux repos)**

All Linux repos demonstrate explicit management of the HP BIOS's 120-second fan control reset timer. omenix writes `max_fan_write_interval` on a 2-minute cadence to keep the BIOS from reclaiming control. arfelious has a 90-second watchdog. OmenCore's 1-second polling cadence means the BIOS never gets a chance to reset, but this is an implementation accident — if the monitor loop pauses (e.g., 3.3.0 crash), the BIOS will reclaim fans after 120 seconds. This is not documented and there is no explicit countdown management.

**4. Unknown model handling**

OmenCore's current approach (family fallback with `UserVerified = false`) is similar to OmenMon's approach (default 2-fan config). This is acceptable. The improvement used by arfelious — an `enable_experimental` flag per model — is worth adopting to make it explicit when a model is running with unverified capabilities.

---

## Phase 5 — Test Suite Failure Analysis

### Why Tests Passed Despite Regressions

The test suite (111/111 passing at 3.3.0) passed because:

1. **No test exercises the background monitoring loop end-to-end.** `HardwareMonitoringService` is tested with mock bridges, but no test spins up an actual `Task.Run` loop and calls `GetEffectiveCadenceInterval()` from it. The thread-safety violation only manifests at runtime when the real WPF `Application.Current` exists.

2. **No test covers non-English locale.** The exception filter string match is never tested in a context where the WPF exception message would be in another language. Tests run against English mocks.

3. **`LightingViewModel` tests use mock services.** The `OnMonitoringSampleUpdated` dispatcher path is not tested with a real SynchronizationContext. In tests, `Application.Current` is null, so `Application.Current?.Dispatcher?.BeginInvoke(...)` silently no-ops — the bug is not triggered.

4. **`FanVerificationService` has no test for the `SetFanMode(Default)` revert path.** The test verifies that verification runs, not what happens when it fails.

5. **New features (NVidia OC profiles, audio-reactive lighting, Safe Mode, resume diagnostics) have zero test coverage.** These were shipped as "tested manually."

### Untested Scenarios

- The monitoring loop running under load while window state changes
- Wake from sleep / resume cycle end-to-end (especially LibreHardwareMonitor reinitialize)
- Model database fallback returning wrong capabilities
- Fan verification failure path (no reset side effect)
- WMI failure → EC fallback routing
- Multiple sampling subscribers receiving the same object concurrently
- App startup on non-English locale (exception filter)
- `QueueMonitoringUiSample` with concurrent calls during high-frequency monitoring

---

## Phase 6 — Non-Negotiable Engineering Rules

The following rules must be enforced on all future changes. Any PR that violates them must be rejected at review.

### Rule 1: No WPF Object Access Outside the UI Thread
`Application.Current.MainWindow`, `DependencyObject` properties, and all WPF UI elements may only be accessed on the UI thread (or via `Dispatcher.Invoke/BeginInvoke`). Background tasks must communicate with the UI via volatile primitives, channels, or explicit dispatcher calls.

**Enforcement:** Search for `Application.Current.MainWindow` in any `Task.Run` lambda, `Timer` callback, non-async method called from a background context.

### Rule 2: No Exception Suppression via String Matching on Exception Messages
`ex.Message.Contains(...)` must never be the sole criterion for swallowing an exception. Exception messages are locale-dependent in the .NET runtime and WPF. Use `ex is SpecificExceptionType`, HRESULT codes, or stack-trace type checks.

**The existing `"different thread"` check in `App.xaml.cs` must be removed once `LightingViewModel` and any remaining callers are properly fixed.**

### Rule 3: No Bare `catch { }` Without Logging
Every `catch` block must either: (a) rethrow, (b) log a structured error with `_logging.Error(...)`, or (c) be explicitly annotated with a comment explaining why the exception is being discarded and what the caller invariant guarantees. `catch { }` with no body and no comment is forbidden.

### Rule 4: No `.GetAwaiter().GetResult()` or `.Result` on Awaitable Tasks
Blocking on async work causes deadlocks when called from a context with a SynchronizationContext. All async work must be `await`-ed or moved to a `Task.Run` context where the SynchronizationContext is absent. Exceptions: `Task.IsCompletedSuccessfully && task.Result` patterns inside sync code (already completed, non-blocking).

### Rule 5: New Code Must Remove the Code It Replaces
When a function or system is redesigned, the old implementation must be deleted in the same commit. Leaving both active creates two code paths with different bugs that mask each other. The ratio of insertions to deletions in a refactor should approach 1:1 for replaced logic.

### Rule 6: Thread-Shared Objects Must Not Be Mutated After Delivery
Objects passed to event subscribers must be treated as immutable after the event fires. `NormalizeMonitoringSample` mutating the sample in-place violates this rule. Create a normalized copy; do not mutate the original.

### Rule 7: `UserVerified = false` Must Carry Behavioral Significance
An unverified model entry must not be treated identically to a verified one at runtime. At minimum, a log warning should fire on startup for any model using an unverified profile. This makes regression visible in community logs.

### Rule 8: Every Background Service Must Have a Restart Path
If `MonitorLoopAsync` exits due to unrecoverable error, the service must be restartable without restarting the application. Every long-running background task must be wrapped in a supervisor that can detect termination and restart it. The `HardwareWatchdogService` cannot perform this role (it only manages fan failsafe).

### Rule 9: No Features Accepted Without Test Coverage
New code paths must have at minimum one unit test that exercises the happy path and one that exercises the error path. Fire-and-forget tasks must include a test that verifies the exception handling.

### Rule 10: Release Scope Must Match Release Type
Hotfixes must change ≤10 files, ≤200 lines. Patch releases must change ≤30 files. If a patch release requires 94 files and 11,930 lines, it must be treated as a major release with a full regression test pass before shipping.

---

## Final Analysis

### Top 5 Regression Causes Introduced in v3.3.0

1. **`GetEffectiveCadenceInterval()` — WPF thread-affinity violation** (crashes on all non-English locales, masked on English by the exception suppressor added in the same release)
2. **`LightingViewModel.OnMonitoringSampleUpdated` not marshal-dispatched** (starved watchdog, triggering failsafe fans on systems with temperature-responsive lighting active)
3. **`FanVerificationService.SetFanMode(Default)` on verification failure** (destroyed active fan curves permanently after first save)
4. **`QueueMonitoringUiSample` / `NormalizeMonitoringSample` layered without removing direct assignment** (two monitoring update paths with different normalization applied to the same mutable object)
5. **`LoggingService.Error()` silent overload change** (structured telemetry payload emitted instead of simple error string, breaking any external log parsing against 3.2.5 format)

### Systems Currently Fragile but Working

| System | Why Fragile | Current Safety Net |
|---|---|---|
| Monitoring cadence | `_baseInterval`/`_lowOverheadMode` dead but still set | `_uiWindowActive` volatile now owns the decision |
| LightingViewModel dispatcher | Still accesses WPF on wrong thread | Per-subscriber `catch` in `HardwareMonitoringService` isolates it |
| `QueueMonitoringUiSample` coalesce | Two write paths to `LatestMonitoringSample` | `BeginInvoke` dispatch provides ordering on the dispatcher queue |
| Fan verification | No RPM test — any non-null curve passes | Curve engine re-applies correct speeds each cycle |
| WmiBiosMonitor temperature fallback | 8 bare `catch {}` | Monitoring service has stale-data fallback |

### Biggest Remaining Architectural Risk

The **monitoring sample pipeline** is the single highest-risk system. The same `MonitoringSample` object reference is:
1. Delivered to N subscribers via `SampleUpdated.GetInvocationList()` (background thread)
2. Queued in `MainViewModel._queuedMonitoringSample` (background thread)
3. Consumed from the queue on the dispatcher thread
4. Mutated in-place by `NormalizeMonitoringSample` (dispatcher thread)
5. Read by `_general?.UpdateFromMonitoringSample` (dispatcher thread)
6. Read by `DashboardViewModel` subscriber (may fire before or after dispatch)

This means subscribers 1 and 6 potentially see a different (pre-mutation) version of the sample object than subscriber 5. This is a latent data consistency bug that will produce ghost values or chart jitter. It has not manifested as a user-visible crash but will produce incorrect telemetry under high update rates.

### All Code That Should Be Removed Before Adding Any New Code

*(Full list in REFACTOR_PLAN.md)*

1. `_baseInterval`, `_lowOverheadInterval`, `_lowOverheadMode` fields + ` UpdateBaseInterval()` / `SetLowOverheadMode()` — dead since 3.3.1
2. `_monitoringUpdateLock` + `_monitoringUiUpdateQueued` — one throttle mechanism is sufficient; the `_pendingUIUpdate` in `HardwareMonitoringService` should be the single throttle
3. The `FanVerificationService` timer-based periodic re-verification loop — all it does now is log warnings; the curve engine makes it redundant
4. The `ResumeRecoveryDiagnosticsService` optional injection pattern — should either be mandatory or removed
5. `NormalizeMonitoringSample` in-place mutation — replace with immutable copy pattern
6. The English string-match exception check (`"different thread"`) once `LightingViewModel` is properly dispatched
7. `BackgroundTimerRegistry.UpdateCadenceTelemetry` calls inside the monitor loop tight path — move to state-change events only
