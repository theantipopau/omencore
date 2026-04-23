# OmenCore — Refactor Plan
**Date:** 2026-04-15  
**Based on:** AUDIT_REPORT.md systemic analysis  
**Priority:** Removal and replacement of layered/obsolete code before any new feature work  
**Status:** Pre-implementation planning — nothing in this document has been executed  

---

## Guiding Principle

> Prefer deletion over addition.  
> If two systems do the same job, one of them must go before writing a third.  
> A codebase that is 20% smaller with 0 silent failures is safer than one that is 20% larger with "defense in depth" swallowing every error.

---

## Removal Candidates

These are code blocks, fields, and services that should be **deleted**. They are not just unused — they are actively misleading to future contributors and mask errors at runtime.

---

### R1: Obsolete Monitoring Interval Fields

**File:** `src/OmenCoreApp/Services/HardwareMonitoringService.cs`

**Code to remove:**
```cs
private TimeSpan _baseInterval;          // dead since 3.3.1
private TimeSpan _lowOverheadInterval;   // dead since 3.3.1
private bool _lowOverheadMode;           // dead since 3.3.1
```

**Public methods to remove:**
```cs
public void UpdateBaseInterval(TimeSpan interval) { ... }
public void SetLowOverheadMode(bool enabled) { ... }
```

**Reason:** `GetEffectiveCadenceInterval()` now uses only `_activeCadenceInterval`, `_idleCadenceInterval`, and `_uiWindowActive`. The old trio has no effect on polling behaviour. Callers that use `SetLowOverheadMode(true)` believe they are reducing poll overhead — they are doing nothing.

**Consumers to update:**
- Grep for `SetLowOverheadMode` and `UpdateBaseInterval` across the solution and remove all call sites.
- Audit `SettingsViewModel` (suspected call site for `SetLowOverheadMode`).

**Risk:** Low. Field removal, no behavioural change.

---

### R2: `_monitoringUiUpdateQueued` + `_monitoringUpdateLock` (Double Throttle)

**File:** `src/OmenCoreApp/ViewModels/MainViewModel.cs`

**Code to remove:**
```cs
private bool _monitoringUiUpdateQueued;
private readonly object _monitoringUpdateLock = new();
```

The associated `QueueMonitoringUiSample` method must be redesigned (not just removed) — see Replacement Plan §RP1.

**Reason:** `HardwareMonitoringService` already has `_pendingUIUpdate` (volatile bool + `CompareExchange`) that prevents concurrent `BeginInvoke` calls. Having a second throttle in `MainViewModel` creates two independent states that can desync. For example: `_pendingUIUpdate = false` (service allows new dispatch), `_monitoringUiUpdateQueued = true` (ViewModel blocks consume). The next sample is dropped.

**Risk:** Medium. Must synchronize removal with `QueueMonitoringUiSample` redesign.

---

### R3: Duplicate `SampleUpdated` + `PropertyChanged` Subscriptions

**File:** `src/OmenCoreApp/Views/HardwareMonitoringDashboard.xaml.cs`

**Code to audit and simplify:**  
The dashboard subscribes to `HardwareMonitoringService.SampleUpdated` directly AND subscribes to `PropertyChanged` on `DashboardViewModel` for `LatestMonitoringSample`. Both call `HandleMonitoringSignalAsync`, which calls `UpdateMetricsAsync`. Every sample triggers two full chart/alert passes.

**Resolution:**  
Remove the direct `SampleUpdated` subscription from the View. The ViewModel should be the sole subscriber to `SampleUpdated`; the View should respond only to ViewModel property/command changes. This is the correct MVVM pattern.

**Risk:** Low. Chart responsiveness unchanged — single update path is sufficient at 1-second cadence.

---

### R4: `FanVerificationService` Background Timer

**File:** `src/OmenCoreApp/Services/FanVerificationService.cs`

**What to remove:** The periodic re-verification timer loop that wakes every `VerificationIntervalMs` and compares current RPMs to expected RPMs.

**What to keep:** The `IsVerified` property and the `RunVerificationAsync()` method for on-demand and startup verification.

**Reason:** Since `FanVerificationService.VerificationPasses()` was changed to "curve exists and is non-empty" (not "hardware RPMs match expected"), the periodic loop now does nothing except generate log warnings. The curve engine re-applies the correct fan speeds every monitoring tick. Periodic RPM over-expectations are now meaningless — you cannot meaningfully assert "fan is at expected RPM" on a curve controller because expected RPM changes based on temperature every second.

**Risk:** Low. Removing the background timer reduces CPU/bus overhead; no fan behaviour changes.

---

### R5: `NormalizeMonitoringSample` In-Place Mutation

**File:** `src/OmenCoreApp/ViewModels/MainViewModel.cs`

**What to remove:** The current `NormalizeMonitoringSample(MonitoringSample value, MonitoringSample? previous)` method that mutates `value` in-place.

**How to replace (see RP2):** Return a new `MonitoringSample` from `Normalize(value, previous)` and assign it. The original sample object delivered to all subscribers must not be modified.

**Reason:** The sample object is shared between multiple subscribers registered on `SampleUpdated`. Mutating after delivery means subscribers that received the object before MainViewModel's dispatcher callback see a different (un-normalized) version. This causes telemetry inconsistencies and ghost values.

**Risk:** Medium. Ensure all `MonitoringSample` properties can be set (not readonly). If properties are init-only, a copy constructor is needed.

---

### R6: English String-Match Exception Filter

**File:** `src/OmenCoreApp/App.xaml.cs`  
**Line:** ~1155 — `ex.Message.Contains("different thread", OrdinalIgnoreCase)`

**What to remove:** This catch condition exists as a safety net for `LightingViewModel.OnMonitoringSampleUpdated` accessing WPF properties on the monitoring background thread. Once `LightingViewModel` is corrected to marshal to the UI thread before accessing WPF-bound state (see RP3), the safety net becomes dead code.

**Prerequisite:** RP3 must be completed first.

**Risk:** Low once RP3 is done. High if removed prematurely.

---

### R7: `ResumeRecoveryDiagnosticsService` Optional Injection (Make Mandatory or Remove)

**File:** All injection sites — `HardwareMonitoringService.cs`, `FanService.cs`, `HardwareWatchdogService.cs`

**Current pattern:**
```cs
private readonly ResumeRecoveryDiagnosticsService? _resumeDiagnostics; // nullable optional
```

**Decision required:** Either:
- **(A) Make it mandatory** — inject non-null everywhere, eliminate the null checks. The service already exists and is always registered in DI.
- **(B) Remove it** — if the telemetry is not consumed by a live UI surface and no alert/action is triggered, the collection is pure overhead.

**Recommendation:** Option A for services already registered; Option B for the `PostResumeSelfCheckAsync` 45-second fire-and-forget (see R8).

**Risk:** Low for Option A (change nullable to non-nullable). Medium for Option B (removes diagnostic capability).

---

### R8: `PostResumeSelfCheckAsync` Fire-and-Forget (Remove or Promote)

**File:** `src/OmenCoreApp/ViewModels/MainViewModel.cs`  
**Code:** `_ = Task.Run(() => PostResumeSelfCheckAsync(resumeCycleId));`

**What it does:** 45 seconds after resume, checks `HardwareMonitoringService.IsMonitoringActive`, gathers a `ResumeRecoveryDiagnosticsService` snapshot, and logs it. No corrective action. No UI surface.

**Decision required:** Either:
- **(A) Promote it** — when `IsMonitoringActive == false` 45 seconds after resume, restart the monitoring service and show a notification. This gives the check actual value.
- **(B) Remove it** — it is logging-only telemetry with no consumer. The same information is available from the `HardwareWatchdogService` alert logic.

**Recommendation:** Option A for the check; Option B for the fire-and-forget wrapper (replace with a proper Task stored in a field so exceptions can be observed).

**Risk:** Medium (behavioural if Option A; non-breaking if Option B).

---

### R9: `BackgroundTimerRegistry.UpdateCadenceTelemetry` on Every Poll Tick

**File:** `src/OmenCoreApp/Services/HardwareMonitoringService.cs`

**What to remove:** The `BackgroundTimerRegistry.Register()` / `Unregister()` calls inside `GetEffectiveCadenceInterval()` that fire every time the cadence changes. Because cadence is determined by window focus state, this can fire hundreds of times per hour during normal use.

**How to replace:** Fire `Register`/`Unregister` only when the service starts/stops, not when the interval changes. Update the registered description on cadence transitions without re-registering.

**Risk:** Low. No behavioural change; pure overhead reduction.

---

### R10: `FanController.cs` Locale-Dependent String Match

**File:** `src/OmenCoreApp/Controllers/FanController.cs`  
**Line:** ~391 — `ex.Message.Contains("mutex", OrdinalIgnoreCase)`

**What to change:** Replace with `ex is SomeSpecificExceptionType` OR catch the specific mutex exception type explicitly. If the exception is `System.Threading.AbandonedMutexException`, catch that directly:

```cs
// Before:
catch (Exception ex) when (ex.Message.Contains("mutex", OrdinalIgnoreCase)) { ... }

// After:
catch (AbandonedMutexException) { ... }
catch (InvalidOperationException ex) when (ex.HResult == -2147024890) { ... }
```

**Risk:** Low. Same exception class, just deterministic matching instead of string search.

---

## Replacement Plan

These are items that must be replaced (not just deleted) because they serve a real function.

---

### RP1: Replace `QueueMonitoringUiSample` + Inline Throttle

**Current design:** `QueueMonitoringUiSample` replaces `_queuedMonitoringSample`, then calls `BeginInvoke(() => ConsumeMonitoringQueue())`. `ConsumeMonitoringQueue` checks `_monitoringUiUpdateQueued` under a lock, sets it false, then assigns `LatestMonitoringSample`.

**The problem:** This is a manual, lock-based single-item queue that duplicates what the `HardwareMonitoringService._pendingUIUpdate` volatile CompareExchange already does.

**Replacement design:**

```cs
// In MainViewModel:
// Remove: bool _monitoringUiUpdateQueued, object _monitoringUpdateLock
// Remove: QueueMonitoringUiSample, ConsumeMonitoringQueue

// Subscriber in HardwareMonitoringService fan-out:
// Already uses BeginInvoke via the existing _pendingUIUpdate gate.
// MainViewModel just receives the dispatched call on the UI thread:

private void OnMonitoringSampleReceived(MonitoringSample rawSample)
{
    // Already on UI thread (via HardwareMonitoringService.BeginInvoke)
    var normalized = Normalize(rawSample, _latestMonitoringSample); // immutable copy
    LatestMonitoringSample = normalized;
}
```

**Effort:** Medium (needs `Normalize` to return a new object — see R5/RP2).

---

### RP2: Replace `NormalizeMonitoringSample` Mutation with Immutable Pattern

**Current design:**
```cs
private void NormalizeMonitoringSample(MonitoringSample value, MonitoringSample? previous)
{
    value.CpuLoadPercent = ...;  // mutates shared object
    value.GpuTemperatureC = ...; // mutates shared object
}
```

**Replacement:**
```cs
private MonitoringSample Normalize(MonitoringSample raw, MonitoringSample? previous)
{
    return new MonitoringSample
    {
        CpuLoadPercent    = raw.CpuLoadPercent > 0 ? raw.CpuLoadPercent : previous?.CpuLoadPercent ?? 0,
        GpuTemperatureC   = raw.GpuTemperatureC > 0 ? raw.GpuTemperatureC : previous?.GpuTemperatureC ?? 0,
        // ... all other fields copied
    };
}
```

**Prerequisite:** `MonitoringSample` must have a copy constructor or all properties settable at construction time.

**Effort:** Medium. Mechanical change once copy constructor exists.

---

### RP3: Fix `LightingViewModel.OnMonitoringSampleUpdated` Dispatcher Marshal

**Current design:**
```cs
private void OnMonitoringSampleUpdated(MonitoringSample sample)
{
    ApplyTemperatureBasedLighting(); // accesses WPF properties on monitoring thread
    ApplyThrottlingLighting();       // same
}
```

**Replacement:**
```cs
private void OnMonitoringSampleUpdated(MonitoringSample sample)
{
    Application.Current?.Dispatcher?.BeginInvoke(() =>
    {
        ApplyTemperatureBasedLighting();
        ApplyThrottlingLighting();
    });
}
```

**This is the prerequisite for R6 (removing the English string-match exception filter).**

**Effort:** Minimal. One wrapping change.

---

### RP4: Replace `KeyboardLightingService` Constructor Synchronous Block

**File:** `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingService.cs`  
**Line:** ~140 — `InitializeAsync().GetAwaiter().GetResult();`

**Problem:** Synchronous wait in constructor. If the constructor is ever called from a context with a SynchronizationContext (UI startup, test frameworks with async context), this deadlocks.

**Replacement:**
```cs
// Remove constructor sync block.
// Use a two-phase initialization pattern:
// 1. Constructor: set fields, no async work
// 2. InitializeAsync(): called at service startup, awaited properly

// In the DI startup sequence:
await keyboardLightingService.InitializeAsync();
```

**Effort:** Medium. Requires auditing all paths that construct `KeyboardLightingService`.

---

### RP5: Replace Bare `catch { }` Blocks with Structured Logging

**Scope:** 30+ locations across `WmiBiosMonitor`, `DiagnosticExportService`, `FanControllerFactory`, `LightingViewModel`, `App.xaml.cs`

**Replacement pattern:**

```cs
// Before:
catch { }

// After (minimum):
catch (Exception ex)
{
    _logging.Warn($"[{nameof(WmiBiosMonitor)}] Temperature read failed: {ex.Message}");
}

// Or, if truly non-critical and intentional:
catch (Exception ex) when (LogAndSwallow(ex)) { }
// where LogAndSwallow logs at Trace level and returns true
```

**Effort:** Low per instance, medium total (30+ changes). Can be done incrementally.

---

### RP6: Replace `FanVerificationService` Periodic Loop with On-Demand Only

**Current:** `FanVerificationService` has a background timer that wakes at `VerificationIntervalMs` and calls `RunVerificationAsync()`. Result is logged.

**Replacement:** Remove the timer. Keep `RunVerificationAsync()` as a callable method for:
- Startup one-time verification
- User-triggered diagnostic from Settings

**Effort:** Low. Timer removal + call-site audit.

---

## Priority Sequence

Execute in this order to avoid breaking working code:

1. **RP3** — Fix `LightingViewModel` dispatcher (unblocks R6; no other dependencies)
2. **R10** — Fix `FanController.cs` string match (trivial, standalone)
3. **RP5** — Replace bare `catch {}` blocks (standalone, incremental)
4. **RP2** — Implement immutable `Normalize` for `MonitoringSample` (unblocks RP1)
5. **RP1** — Simplify `QueueMonitoringUiSample` + remove `_monitoringUiUpdateQueued` (depends on RP2)
6. **R3** — Remove duplicate dashboard subscriptions (unblocks chart simplification)
7. **R1** — Remove dead interval fields + public methods (after confirming no callers)
8. **R2** — Remove `_monitoringUpdateLock` (after RP1 completed)
9. **R4** — Remove `FanVerificationService` timer loop (standalone)
10. **RP6** — Refactor `FanVerificationService` to on-demand (follows R4)
11. **R6** — Remove English string-match filter (after RP3 verified working)
12. **R7/R8** — Resolve `ResumeRecoveryDiagnosticsService` inject/remove decision
13. **R9** — `BackgroundTimerRegistry` telemetry rate reduction
14. **RP4** — `KeyboardLightingService` async init (requires startup sequence refactor)

---

## Complexity Estimates

| Ref | Change | Lines Changed (est.) | Files | Risk |
|---|---|---|---|---|
| R1 | Dead interval fields | ~30 | 2 | Low |
| R2 | Remove `_monitoringUiUpdateQueued` | ~40 | 1 | Medium |
| R3 | Remove duplicate subscriptions | ~20 | 1 | Low |
| R4 | Remove FanVerification timer | ~50 | 1 | Low |
| R5 | Normalize non-mutating | ~60 | 1 | Medium |
| R6 | Remove English filter | ~5 | 1 | Low (after RP3) |
| R7 | Nullable injection | ~20 | 4 | Low |
| R8 | PostResumeSelfCheck | ~40 | 1 | Medium |
| R9 | BackgroundTimer rate | ~15 | 1 | Low |
| R10 | String-match exception | ~10 | 1 | Low |
| RP1 | QueueMonitoring simplify | ~80 | 1 | Medium |
| RP2 | Immutable normalize | ~100 | 2 | Medium |
| RP3 | LightingViewModel dispatch | ~10 | 1 | Low |
| RP4 | KeyboardLightingService init | ~80 | 3 | High |
| RP5 | Bare catch logging | ~150 | 8 | Low |
| RP6 | FanVerification on-demand | ~30 | 1 | Low |
| **TOTAL** | | **~740** | **~20** | |

These 740 lines of changes against ~140,000 lines of code are mostly removals. The goal is to reduce the codebase, not grow it.
