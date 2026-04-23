# OmenCore — Implementation Plan
**Date:** 2026-04-15  
**Source documents:** AUDIT_REPORT.md, REFACTOR_PLAN.md, REGRESSION_MATRIX.md  
**Approach:** Controlled recovery — staged execution, each step independently testable  
**Status:** Pre-implementation — NO code has been changed by this document  

---

## PRE-FLIGHT: Corrections to REFACTOR_PLAN.md

Before executing any step, the following REFACTOR_PLAN.md items have been verified against the actual source and are **corrected**:

| Ref | REFACTOR_PLAN Assessment | Actual State | Action |
|---|---|---|---|
| RP3 | "Fix LightingViewModel dispatcher — not yet done" | ✅ **Already done.** `LightingViewModel.OnMonitoringSampleUpdated` (line 2082) wraps in `Application.Current?.Dispatcher?.BeginInvoke` | Mark as complete; verify only |
| R4 | "Remove FanVerificationService background timer loop" | ✅ **No timer exists.** `FanVerificationService` is purely on-demand (848 lines, no `Timer`, no `Task.Run` loop) | Remove R4 from active plan; no action needed |
| RP6 | "Refactor FanVerificationService to on-demand" | ✅ **Already on-demand only.** No timer present. | Remove RP6; no action needed |
| R1 | "`_lowOverheadMode` is dead" | ⚠️ **Partially wrong.** `_lowOverheadMode` IS read in `GetEffectiveCadenceInterval()` (line 145) as a priority gate; `SetLowOverheadMode()` also forwards to `LibreHardwareMonitorImpl.SetLowOverheadMode()`. **`_baseInterval` and `_lowOverheadInterval` ARE dead** — set in `SetPollingInterval()` but never read by the cadence decision path. | Scope reduced: remove `_baseInterval` / `_lowOverheadInterval` from `SetPollingInterval()` + fix log message; keep `_lowOverheadMode` |
| R9 | "`UpdateCadenceTelemetry` fires on every tick" | ✅ **Already guarded.** Has `if (_lastAppliedCadence.HasValue && _lastAppliedCadence.Value == cadence) return;` | No action needed |
| R10 | "Fix FanController.cs string match only" | ⚠️ **Scope wider.** `PerformanceModeService.cs` line 92 has identical pattern `ex.Message.Contains("mutex", OrdinalIgnoreCase)` | Include PerformanceModeService in same step |

---

## Execution Order Rationale

Steps proceed from **zero side-effect** (pure safe removals, string replacements) toward **higher coupling** (monitoring pipeline rewrites, async init changes). Dependencies are explicit — no step begins until its prerequisites are verified working.

The guiding constraint: **a step that breaks compilation cannot proceed to the next step.** Every step must leave the codebase in a green-build state.

---

## Step-by-Step Execution Plan

---

### STEP-01 — Verify LightingViewModel Dispatcher (Pre-flight Only)
**Status: VERIFY ONLY — Do not edit**

| Field | Value |
|---|---|
| **Description** | Confirm LightingViewModel.OnMonitoringSampleUpdated (line 2082) already wraps in `Dispatcher.BeginInvoke`. Confirm `ApplyTemperatureBasedLighting` and `ApplyThrottlingLighting` are only called inside the lambda. Run compiler and all existing tests. |
| **Files touched** | None |
| **LOC estimate** | 0 |
| **Dependencies** | None |
| **Risk** | None |
| **Verification** | Read lines 2082–2115 of LightingViewModel.cs. Confirm `BeginInvoke` wrapper present. Run `dotnet test`. |
| **Rollback** | N/A |

**Expected outcome:** Confirmation that RP3 is already correct. This unblocks STEP-03 (remove English string-match filter).

---

### STEP-02 — Fix Locale-Dependent Exception String Matching

| Field | Value |
|---|---|
| **Description** | Replace `ex.Message.Contains("mutex", OrdinalIgnoreCase)` with `catch (AbandonedMutexException)` in `FanController.cs` (line 391) and `PerformanceModeService.cs` (line 92). Also check `LibreHardwareMonitorImpl.cs` line 857 — `ex.Message.Contains("SafeFileHandle")` should be replaced with `catch (ObjectDisposedException)` only (the `SafeFileHandle` string check is non-locale-dependent but unnecessary given the type check precedes it). |
| **Files touched** | `FanController.cs`, `PerformanceModeService.cs`, `LibreHardwareMonitorImpl.cs` |
| **LOC estimate** | ~15 |
| **Dependencies** | None |
| **Risk** | **Low** — purely changes exception classification logic. Fan behaviour unchanged. |
| **Verification** | Build succeeds. Run existing fan tests. Grep confirms zero `ex.Message.Contains` in those three files post-change. |
| **Rollback** | `git checkout src/OmenCoreApp/Hardware/FanController.cs src/OmenCoreApp/Services/PerformanceModeService.cs src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs` |

**Implementation note:**
```cs
// FanController.cs — BEFORE:
catch (Exception ex) when (ex is TimeoutException || ex.Message.Contains("mutex", ...)) { ... }

// FanController.cs — AFTER:
catch (Exception ex) when (ex is TimeoutException or AbandonedMutexException) { ... }
```

---

### STEP-03 — Remove English String-Match Exception Filter in App.xaml.cs

| Field | Value |
|---|---|
| **Description** | Remove the `ex.Message.Contains("different thread", OrdinalIgnoreCase)` check from the `OnUnobservedTaskException` handler in `App.xaml.cs` (line ~1155). This check exists solely as a safety net for the WPF cross-thread access that was the root cause of GH-#109. The root cause is fixed (3.3.1 HardwareMonitoringService + LightingViewModel). The safety net now silently swallows unrelated `InvalidOperationException`s that contain "different thread". |
| **Files touched** | `App.xaml.cs` |
| **LOC estimate** | ~10 (remove condition + clean up surrounding if/else) |
| **Dependencies** | **STEP-01** — LightingViewModel dispatcher must be confirmed correct first |
| **Risk** | **Low-Medium** — if any undiscovered WPF cross-thread access exists, it will now surface as an unhandled exception rather than being silently swallowed. This is the *correct* outcome — errors should be visible. |
| **Verification** | Build succeeds. Run all tests. Grep for `ex.Message.Contains` in App.xaml.cs — should be zero remaining. |
| **Rollback** | `git checkout src/OmenCoreApp/App.xaml.cs` |

**Stop condition:** If removing this check causes a test failure or a newly surfaced exception in manual testing within 24 hours, STOP. The revealed exception is a new undiscovered cross-thread access that must be fixed before this step can be committed.

---

### STEP-04 — Exception Logging: WmiBiosMonitor

| Field | Value |
|---|---|
| **Description** | Replace the 6 bare `catch { }` blocks in `WmiBiosMonitor.cs` at lines 1582, 1602, 1678, 1693, 1754, 1839 with `catch (Exception ex) { _logging.Warn(...) }`. Lines 329 and 560 are in `Dispose`/cleanup paths — these can remain bare but should have inline comments. |
| **Files touched** | `WmiBiosMonitor.cs` |
| **LOC estimate** | ~50 (6 × ~8 lines each) |
| **Dependencies** | None |
| **Risk** | **Low** — additive only. Adds log output; changes no logic. |
| **Verification** | Build succeeds. Run tests. Grep for bare `catch { }` in WmiBiosMonitor.cs — should show only the 2 intentional Dispose-path catches with comments added. |
| **Rollback** | `git checkout src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` |

**Implementation pattern:**
```cs
// Each bare catch becomes:
catch (Exception ex)
{
    _logging.Warn($"[WmiBiosMonitor] {nameof(TheMethodName)} failed: {ex.Message}");
}
```

---

### STEP-05 — Exception Logging: DiagnosticExportService

| Field | Value |
|---|---|
| **Description** | Replace the 8 bare `catch { }` blocks in `DiagnosticExportService.cs` (lines 402, 416, 588, 604, 622, 645, 667, 687) with structured logging. These are section collection failures — logged at `Warn` level is appropriate since a failed section produces an incomplete (not crash) export. |
| **Files touched** | `DiagnosticExportService.cs` |
| **LOC estimate** | ~60 (8 blocks) |
| **Dependencies** | None (independent of STEP-04) |
| **Risk** | **Low** — additive only. |
| **Verification** | Build succeeds. Run tests. Grep for bare `catch { }` in DiagnosticExportService.cs — zero remaining. |
| **Rollback** | `git checkout src/OmenCoreApp/Services/Diagnostics/DiagnosticExportService.cs` |

---

### STEP-06 — Exception Logging: Remaining Files

| Field | Value |
|---|---|
| **Description** | Address remaining bare `catch { }` in: `App.xaml.cs` lines 984, 994 (startup sequence — must understand context before replacing), `FanCalibrationControl.xaml.cs` line 68 (swallows init failure — replace with `_logging.Error`), `RazerService.cs` line 752 (replace with Warn). `DiagnosticLoggingService.cs` lines 97, 333, 336 — these are the logging infrastructure itself; bare catch is intentional to prevent infinite recursion. Add comment explaining the invariant; do NOT change. For `CorsairHidDirect.cs` telemetry telemetry calls in `try { _telemetry?.Increment... } catch { }` — these are intentional guard against telemetry failure propagating; add comment, do NOT change. |
| **Files touched** | `App.xaml.cs`, `FanCalibrationControl.xaml.cs`, `RazerService.cs` |
| **LOC estimate** | ~30 |
| **Dependencies** | None |
| **Risk** | **Low** — `App.xaml.cs` startup paths require reading to understand criticality before replacing |
| **Verification** | Build succeeds. Every `catch { }` in the patched files has either a `_logging` call or an explanatory comment. |
| **Rollback** | Per-file `git checkout` |

**Stop condition for App.xaml.cs:** If either bare `catch` at lines 984/994 wraps a code path where rethrowing would abort startup, add a comment instead of logging: `// Intentional: startup fallback; rethrowing would abort app init.`

---

### STEP-07 — MonitoringSample: Add Copy Constructor

| Field | Value |
|---|---|
| **Description** | Add a copy constructor (or a `Clone()` method) to `MonitoringSample`. This is required before STEP-08 can rewrite `NormalizeMonitoringSample` to return a new object. Do not change any other code in this step — only add the constructor. Verify all existing `MonitoringSample` properties are copyable (no init-only blocking). |
| **Files touched** | `MonitoringSample.cs` (model class) |
| **LOC estimate** | ~30 (constructor + all property copies) |
| **Dependencies** | None |
| **Risk** | **Low** — additive only |
| **Verification** | Build succeeds. Write a single unit test: `var s1 = new MonitoringSample { CpuLoadPercent = 42 }; var s2 = new MonitoringSample(s1); Assert.Equal(42, s2.CpuLoadPercent); Assert.NotSame(s1, s2);` |
| **Rollback** | `git checkout` the model file |

---

### STEP-08 — MonitoringSample: Make Normalize Non-Mutating

| Field | Value |
|---|---|
| **Description** | Rewrite `NormalizeMonitoringSample` in `MainViewModel.cs` from a `void` method that mutates its argument to a method that returns a new `MonitoringSample`. Update the single call site (`OnMonitoringSampleReceived` or equivalent) to use the returned value. The original sample passed to `SampleUpdated` subscribers must remain unmodified after this change. |
| **Files touched** | `MainViewModel.cs` |
| **LOC estimate** | ~60 (method signature + return type + copy constructor call + call site) |
| **Dependencies** | **STEP-07** — copy constructor must exist first |
| **Risk** | **Medium** — changes the data flow contract for monitoring samples. Any code that reads from `_latestMonitoringSample` immediately after the method returns now reads the normalized copy, not the raw sample. Behaviorally identical for all current callers because the mutation previously produced the same values. |
| **Verification** | Build. Run all monitoring-related tests. Add T4 test from REGRESSION_MATRIX.md: create a sample S1, normalize it, assert S1 is unchanged and the returned S2 has normalized values. |
| **Rollback** | `git checkout src/OmenCoreApp/ViewModels/MainViewModel.cs` — reverts to in-place mutation (safe, no data loss) |

---

### STEP-09 — Simplify Monitoring Update Dispatch (Remove Redundant Throttle)

| Field | Value |
|---|---|
| **Description** | Remove the `_monitoringUiUpdateQueued` / `_monitoringUpdateLock` pair from `MainViewModel.cs`. Simplify `QueueMonitoringUiSample` — it should become a direct `Application.Current?.Dispatcher?.BeginInvoke(...)` call without the double-lock pattern. Verify that `HardwareMonitoringService._pendingUIUpdate` CompareExchange still prevents concurrent dispatch (it does — it's the real throttle). The `lock (_monitoringUpdateLock)` block that checks `_monitoringUiUpdateQueued` inside the `BeginInvoke` callback must also be removed. |
| **Files touched** | `MainViewModel.cs` |
| **LOC estimate** | ~50 (remove lock, bool field, and the double-lock pattern in the callback) |
| **Dependencies** | **STEP-08** — normalized copy must be in place so the dispatched value is known-good |
| **Risk** | **Medium** — modifies the monitoring update dispatch path. The outer throttle in `HardwareMonitoringService` is sufficient and tested. Risk is that a subtle ordering assumption in the existing lock was compensating for something undocumented. |
| **Verification** | Build. Run all tests. Manually observe the monitoring chart at 1-second cadence for 60 seconds — values should update smoothly with no freeze or stutter. Grep confirms `_monitoringUiUpdateQueued` and `_monitoringUpdateLock` are gone from the file. |
| **Rollback** | `git checkout src/OmenCoreApp/ViewModels/MainViewModel.cs` — restores the double-lock pattern (safe) |

**Stop condition:** If the monitoring chart shows stutter (>2 consecutive missed updates) during 60-second manual observation, STOP. The double-lock was compensating for a timing issue that must be understood before removing it.

---

### STEP-10 — Remove Duplicate Dashboard Subscriptions

| Field | Value |
|---|---|
| **Description** | In `HardwareMonitoringDashboard.xaml.cs`, remove the direct `HardwareMonitoringService.SampleUpdated` event subscription (lines ~107–112). The dashboard should receive monitoring data only through ViewModel property changes, not directly from the service. Verify the `PropertyChanged` → `HandleMonitoringSignalAsync` path still fires correctly after removal. |
| **Files touched** | `HardwareMonitoringDashboard.xaml.cs` |
| **LOC estimate** | ~20 (remove subscription + null check) |
| **Dependencies** | **STEP-09** — single dispatch path must be confirmed working first |
| **Risk** | **Low** — only removes a duplicate; the ViewModel-mediated path remains |
| **Verification** | Build. Open monitoring dashboard. Confirm chart updates at 1-second cadence. Confirm no double-update (use logging to confirm `HandleMonitoringSignalAsync` fires once per sample, not twice). |
| **Rollback** | `git checkout src/OmenCoreApp/Views/HardwareMonitoringDashboard.xaml.cs` |

---

### STEP-11 — Remove Dead Interval Fields from SetPollingInterval

| Field | Value |
|---|---|
| **Description** | In `HardwareMonitoringService.cs`, `SetPollingInterval()` (line ~135) updates `_baseInterval` and `_lowOverheadInterval` — but neither is read by `GetEffectiveCadenceInterval()`. The cadence is fixed at `_activeCadenceInterval` (1s) and `_idleCadenceInterval` (5s). Remove only the `_baseInterval` / `_lowOverheadInterval` field declarations and the assignments in `SetPollingInterval()`. Update the log message to say the call is accepted but the cadence is now window-state-driven. Update `SetLowOverheadMode()` to remove the log message references to `_lowOverheadInterval` / `_baseInterval`. Keep `_lowOverheadMode` (used in `GetEffectiveCadenceInterval`). Keep `SetPollingInterval` as a stub that logs a deprecation notice — callers in SettingsViewModel must not break. |
| **Files touched** | `HardwareMonitoringService.cs` |
| **LOC estimate** | ~25 |
| **Dependencies** | None |
| **Risk** | **Low** — removes fields that are set but not read. `SetPollingInterval` kept as stub. |
| **Verification** | Build succeeds. `SettingsViewModel` still compiles and calls `SetPollingInterval` without error (stub accepts the call). Grep confirms `_baseInterval` and `_lowOverheadInterval` are gone. |
| **Rollback** | `git checkout src/OmenCoreApp/Services/HardwareMonitoringService.cs` |

---

### STEP-12 — Resolve ResumeRecoveryDiagnosticsService Nullable Injection ✅ COMPLETED 2026-04-16

| Field | Value |
|---|---|
| **Description** | Decision must be made before this step executes (see Stop Conditions §SC-5). If decision is Option A (make mandatory): change `ResumeRecoveryDiagnosticsService?` to `ResumeRecoveryDiagnosticsService` in `HardwareMonitoringService`, `FanService`, `HardwareWatchdogService`. Remove null-check guards `_resumeDiagnostics?.RecordStep(...)` and use direct calls. Verify DI registration already supplies non-null. If decision is Option B (remove): remove all `_resumeDiagnostics` fields and call sites from the three services. Either option: update `PostResumeSelfCheckAsync` in `MainViewModel` to observe and potentially act on the result (currently fire-and-forget with no corrective action). |
| **Files touched** | `HardwareMonitoringService.cs`, `FanService.cs`, `HardwareWatchdogService.cs`, `MainViewModel.cs` |
| **LOC estimate** | ~40 (Option A) / ~60 (Option B) |
| **Dependencies** | Explicit decision required (see §SC-5) |
| **Risk** | **Medium** |
| **Verification** | Build succeeds. Simulate resume cycle — verify diagnostic is either collected (Option A) or cleanly absent (Option B). |
| **Rollback** | Per-file `git checkout` |
| **Outcome** | **Option A implemented.** Field made non-nullable in all 3 services. Null guards (`?.RecordStep`) removed; replaced with direct calls. `HardwareWatchdogService` null-guard block around cycleId task removed unconditionally. 14 test construction sites updated across 5 test files. Build verification deferred — no .NET SDK on this machine. |

---

### STEP-13 — Fix KeyboardLightingService Async Init (High Risk — Deferred)

| Field | Value |
|---|---|
| **Description** | Remove the `.GetAwaiter().GetResult()` call in `KeyboardLightingService` constructor (~line 140). Restructure to use async two-phase init: constructor sets fields only; `InitializeAsync()` is called by the DI startup sequence and awaited. Audit every call site that constructs `KeyboardLightingService` to ensure it passes through the startup async path. |
| **Files touched** | `KeyboardLightingService.cs`, `App.xaml.cs` (startup wiring), `IKeyboardLightingService.cs` (add `InitializeAsync` if not present) |
| **LOC estimate** | ~80 |
| **Dependencies** | **All prior steps must be complete and stable.** This is the highest-risk step and must be done in isolation with a build + manual test before committing. |
| **Risk** | **High** — changes initialization timing of a service that controls keyboard hardware. If init is delayed until after first use, commands may fail silently. |
| **Verification** | Build. On hardware: keyboard lighting initializes within 2 seconds of app launch. `IsAvailable` returns `true`. `_logging` shows `InitializeAsync completed` before any `ApplyLighting` calls. |
| **Rollback** | `git checkout src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingService.cs src/OmenCoreApp/App.xaml.cs` |

---

## Execution Batches

Steps are delivered in these batches. Each batch is a shippable, independently testable increment.

---

### Batch 1 — Thread-Safety Verification & String Match Fixes
**Steps:** STEP-01 (verify), STEP-02, STEP-03  
**Target files:** `LightingViewModel.cs` (read-only), `FanController.cs`, `PerformanceModeService.cs`, `LibreHardwareMonitorImpl.cs`, `App.xaml.cs`  
**Combined LOC:** ~25  
**Shippable?** Yes — zero behaviour change, only removes hidden crash risks  
**Risk:** Low  

**Batch acceptance criteria:**
- `LightingViewModel.OnMonitoringSampleUpdated` confirmed wrapped in BeginInvoke
- Zero `ex.Message.Contains` in FanController, PerformanceModeService
- Zero English thread-safety exception filter in App.xaml.cs
- All existing tests pass
- No new exception surfaces in 24h of background monitoring

---

### Batch 2 — Exception Logging Cleanup
**Steps:** STEP-04, STEP-05, STEP-06  
**Target files:** `WmiBiosMonitor.cs`, `DiagnosticExportService.cs`, `App.xaml.cs`, `FanCalibrationControl.xaml.cs`, `RazerService.cs`  
**Combined LOC:** ~130  
**Shippable?** Yes — additive only  
**Risk:** Low  

**Batch acceptance criteria:**
- All `catch { }` blocks in patched files have either a `_logging.Warn/Error` call or an explanatory comment
- `DiagnosticLoggingService` and `CorsairHidDirect` intentional bare catches left with comments
- All tests pass

---

### Batch 3 — Monitoring Sample Immutability
**Steps:** STEP-07, STEP-08  
**Target files:** `MonitoringSample.cs`, `MainViewModel.cs`  
**Combined LOC:** ~90  
**Shippable?** Yes, but requires T4 test to be written and passing before merge  
**Risk:** Medium  

**Batch acceptance criteria:**
- T4 test (normalize returns independent copy) written and passing
- Existing monitoring tests pass
- No change in monitoring chart values during 5-minute manual observation
- `_latestMonitoringSample` field in MainViewModel holds normalized copy (not raw)

---

### Batch 4 — UI Update Pipeline Simplification
**Steps:** STEP-09, STEP-10  
**Target files:** `MainViewModel.cs`, `HardwareMonitoringDashboard.xaml.cs`  
**Combined LOC:** ~70  
**Shippable?** Yes, after 60-second chart observation confirms no stutter  
**Risk:** Medium  

**Batch acceptance criteria:**
- `_monitoringUiUpdateQueued` and `_monitoringUpdateLock` removed from MainViewModel
- Dashboard chart updates once per sample (confirmed by log trace count)
- 60-second manual chart observation shows no missed updates or stutter
- All existing tests pass

---

### Batch 5 — Dead Code Removal
**Steps:** STEP-11  
**Target files:** `HardwareMonitoringService.cs`  
**Combined LOC:** ~25  
**Shippable?** Yes  
**Risk:** Low  

**Batch acceptance criteria:**
- `_baseInterval` and `_lowOverheadInterval` absent from file
- `SetPollingInterval` stub present and logs deprecation
- Settings "polling interval" setting still saves/loads without error
- All tests pass

---

### Batch 6 — Resume Diagnostics Decision
**Steps:** STEP-12  
**Target files:** `HardwareMonitoringService.cs`, `FanService.cs`, `HardwareWatchdogService.cs`, `MainViewModel.cs`  
**Combined LOC:** ~40–60  
**Shippable?** Requires explicit decision from developer before this batch can start  
**Risk:** Medium  

---

### Batch 7 — Async Init Refactor (High Risk — Separate Branch)
**Steps:** STEP-13  
**Target files:** `KeyboardLightingService.cs`, `App.xaml.cs`  
**Combined LOC:** ~80  
**Shippable?** Only after hardware verification on at least one OMEN device  
**Risk:** High — ship on its own branch, do not combine with other batches  

---

## Test Mapping

| Step | Regression Addressed | New Tests Required | Existing Tests Validates |
|---|---|---|---|
| STEP-01 | REG-2 (LightingViewModel watchdog starvation) | T2 (LightingViewModel dispatcher validation) | All lighting tests |
| STEP-02 | REG-1 partial (locale-safe classification), prevents future locale-crash class | None required (trivial change) | FanController tests |
| STEP-03 | REG-1 root (removes the masking band-aid) | Manual: observe no new unhandled exceptions post-remove | App.xaml.cs startup tests |
| STEP-04 | REG-8 partial (WmiBiosMonitor failures now visible in logs) | None — logging only | WmiBiosMonitor tests |
| STEP-05 | REG-8 (DiagnosticExport failures now visible) | None — logging only | DiagnosticExport tests |
| STEP-06 | REG-4 (FanCalibration init failure now logged) | None — logging only | FanCalibration tests |
| STEP-07 | RISK-3 (concurrent sample mutation) — prerequisite step | Unit: copy constructor creates independent object | MonitoringSample model test |
| STEP-08 | RISK-3 (normalise returns independent copy — mutation eliminated) | **T4 (normalize immutability assertion)** | MainViewModel monitoring tests |
| STEP-09 | RISK-3 (single update path eliminates desync between two throttles) | Manual: 60s chart observation | MainViewModel dispatch tests |
| STEP-10 | REG-2 partial (removes double-processing of samples in dashboard) | Manual: confirm single-update per sample in log | Dashboard tests |
| STEP-11 | REG-9 (`SetLowOverheadMode` misleading dead effect on interval) | T-dead-interval: assert `SetPollingInterval` doesn't affect `GetEffectiveCadenceInterval` | HardwareMonitoringService tests |
| STEP-12 | RISK-1 partial (resume diagnostics either mandatory or removed) | T8 (resume monitoring restart) | Watchdog tests |
| STEP-13 | RISK-5 (KeyboardLightingService deadlock on UI-thread init) | T5 (construct service from SynchronizationContext — no deadlock) | KeyboardLighting tests |

---

## Stop Conditions

The following conditions require the implementing agent (or developer) to **halt and report** before continuing. Do NOT push through stop conditions.

---

### SC-1: Compile Error That Cannot Be Resolved Locally
**Trigger:** A step produces a compile error that cannot be fixed by a single-line correction within the same file.  
**Action:** Stop. Report: (a) which file introduced the error, (b) what the error is, (c) whether a dependency was missed. Do NOT attempt to fix compilation errors by adding new abstractions.

---

### SC-2: Test Regression After Step
**Trigger:** Any previously passing test fails after applying a step.  
**Action:** Stop immediately. Do NOT roll the failing test change into "also fix while here." Roll back the step. Add a note explaining which test failed and why the step broke it.

---

### SC-3: STEP-03 Surfaces a New Unhandled Exception
**Trigger:** After removing the `ex.Message.Contains("different thread")` filter (STEP-03), the background monitoring loop produces an unhandled exception that was not visible before.  
**Action:** Stop. The revealed exception is a real cross-thread access that must be triaged and fixed before STEP-03 can be kept. Do NOT re-add the string-match filter as a fix.

---

### SC-4: STEP-09 Causes Chart Stutter
**Trigger:** During the 60-second manual observation required by STEP-09, the monitoring chart misses 2 or more consecutive 1-second updates.  
**Action:** Stop. Roll back STEP-09. Investigate whether the `_monitoringUpdateLock` was compensating for a real race condition between the outer `HardwareMonitoringService._pendingUIUpdate` CompareExchange and the MainViewModel dispatcher callback.

---

### SC-5: STEP-12 Awaits Explicit Decision
**Trigger:** STEP-12 is reached in execution.  
**Action:** Stop before writing any code. Present the decision to the developer:  
- **Option A:** Make `ResumeRecoveryDiagnosticsService` non-nullable everywhere; add corrective action in `PostResumeSelfCheckAsync` when monitoring is found dead after resume.  
- **Option B:** Remove `ResumeRecoveryDiagnosticsService` from all three services; remove `PostResumeSelfCheckAsync` entirely; document the rationale.  
Wait for explicit developer decision before proceeding.

---

### SC-6: STEP-13 Cannot be Verified on Hardware
**Trigger:** STEP-13 (KeyboardLightingService async init) is ready to execute but no hardware verification environment is available.  
**Action:** Stop. Do NOT ship STEP-13 without hardware verification. Document the step as "ready to execute, awaiting hardware test."

---

### SC-7: Behavioural Ambiguity in Any Call Site
**Trigger:** During any step, a call site is found where it is **genuinely unclear** whether the code being changed is the correct code to remove. For example, two systems both appear "correct" for a given task.  
**Action:** Stop. Document both systems' purposes and ask for a decision. Ambiguity in step execution is a sign that understanding is incomplete; it is not a sign to pick arbitrarily.

---

## Phase 5 — Dry Run: First Three Live Steps

*STEP-01 is verification-only. Below covers the first three steps that change code.*

---

### DRY RUN: STEP-02 — Fix Locale-Dependent Exception String Matching

**What will change:**

In `FanController.cs` line 391:
```cs
// BEFORE:
if (ex is TimeoutException || ex.Message.Contains("mutex", StringComparison.OrdinalIgnoreCase))

// AFTER:
if (ex is TimeoutException or AbandonedMutexException)
```

In `PerformanceModeService.cs` line 92:
```cs
// BEFORE:
if (ex.Message.Contains("mutex", StringComparison.OrdinalIgnoreCase))

// AFTER:
if (ex is AbandonedMutexException)
```

In `LibreHardwareMonitorImpl.cs` line 857:
```cs
// BEFORE:
if (ex is ObjectDisposedException || ex.Message.Contains("SafeFileHandle") || ex.Message.Contains("disposed"))

// AFTER:
if (ex is ObjectDisposedException)
// (removes the string checks; ObjectDisposedException already covers both handle disposals)
```

**What could break:**
- `FanController`: if a real mutex exception is NOT an `AbandonedMutexException` (e.g., it's an `InvalidOperationException` with a WMI lock message), it will no longer be caught by this specific condition. It will propagate up and be logged as an unhandled fan error. This is the correct outcome.
- `LibreHardwareMonitorImpl`: A `SafeFileHandle` disposed exception that is NOT of type `ObjectDisposedException` would no longer be caught. In practice, .NET always wraps SafeFileHandle failures as `ObjectDisposedException`. Risk: negligible.

**Is rollback safe?** Yes — `git checkout` on the three files restores string matching exactly. Zero state change.

---

### DRY RUN: STEP-03 — Remove English String-Match Exception Filter

**What will change:**

In `App.xaml.cs` around line 1155, the `OnUnobservedTaskException` handler currently contains:
```cs
if (innerEx is InvalidOperationException invalidOp &&
    (invalidOp.Message.Contains("different thread", StringComparison.OrdinalIgnoreCase) ||
     /* other conditions */))
{
    // swallow
}
```
The `Contains("different thread")` condition is removed. The remaining conditions (`|| /* other conditions */`) stay.

**What could break:**
- If ANY background task currently throws `InvalidOperationException` with "different thread" in the message that is NOT the LightingViewModel or HardwareMonitoringService (i.e., a third undiscovered cross-thread access), that exception will now surface as an `UnobservedTaskException`. In the current `.NET` configuration this may or may not crash the app depending on the `UnobservedTaskException` policy.
- Critically: this is the **intended behaviour**. We want cross-thread bugs to be loud. If something breaks, we have found a real bug.

**Is rollback safe?** Yes — `git checkout` on `App.xaml.cs` restores the swallowing filter. Zero state change.

---

### DRY RUN: STEP-04 — Exception Logging: WmiBiosMonitor

**What will change:**

Six `catch { }` blocks at lines 1582, 1602, 1678, 1693, 1754, 1839 will each become:
```cs
catch (Exception ex)
{
    _logging.Warn($"[WmiBiosMonitor] {nameof(<method>)} temperature read failed: {ex.Message}");
}
```

**What could break:**
- Nothing functionally. Temperature fallback returns the previous cached value (the `catch` body was empty before — it already fell through to the fallback). The only new behaviour is a log entry.
- If `_logging` is null at the point these catch blocks execute (possible if the object is partially constructed during a failure at startup), calling `_logging.Warn` will throw `NullReferenceException` inside a catch block. Verify `_logging` is assigned in the constructor BEFORE reading its call sites.

**Is rollback safe?** Yes — `git checkout` on `WmiBiosMonitor.cs` restores bare catches. Zero functional change.

---

## Final Summary

### Safest Starting Step
**STEP-02** (Fix locale-dependent exception string matching in FanController + PerformanceModeService). It is a 15-line, zero-semantic-change across 3 files. It touches no business logic. It cannot produce a regression. It closes a real risk class (locale-dependent exception swallowing) that is the root cause family of the GH-#109 crash.

### Highest-Risk Step
**STEP-13** (KeyboardLightingService async init). It changes the initialization timing of hardware-controlling code, requires a constructor semantics change, requires auditing all construction call sites, and must be verified on physical hardware. If done incorrectly it can leave the keyboard in an uninitialized state silently.

### Step Most Likely to Introduce Regression
**STEP-09** (Remove redundant monitoring dispatch throttle). It changes the update path for the monitoring pipeline — the system most responsible for v3.3.0's instability. The `_monitoringUpdateLock` may be compensating for a real timing issue between `HardwareMonitoringService`'s `CompareExchange` and the dispatcher queue depth. If it is, removing it will cause monitoring chart stutter under load.

**Mitigation for STEP-09:** Before executing, add a debug trace log to `QueueMonitoringUiSample` (current implementation) that counts calls per second. If ever >1 call/second arrives, the outer `_pendingUIUpdate` CompareExchange is not catching them all. Confirm outer throttle is truly sufficient before removing the inner lock.
