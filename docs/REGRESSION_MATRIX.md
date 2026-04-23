# OmenCore — Regression Matrix
**Date:** 2026-04-15  
**Scope:** v3.2.5 → v3.3.0 → v3.3.1 regressions and test coverage gaps  
**Purpose:** Explain why regressions shipped, identify untested scenarios, define test requirements

---

## Why 111/111 Tests Passed While the App Crashed

The test suite was passing a 100% green board while the application crashed on startup for all non-English Windows users. This is not a test tooling failure — it is a coverage architecture failure. The tests validated the wrong things thoroughly and skipped the right things entirely.

The three root reasons:

1. **Tests do not spin up a real `DispatcherObject` context.** WPF's thread-affinity model only enforces itself when a `DependencyObject` is accessed from a thread that did not create it. In unit tests, `Application.Current` is typically `null`, which means `Application.Current?.MainWindow?.IsVisible` evaluates to `null` without throwing — the exact opposite of what happens at runtime.

2. **Background services are tested with synchronous mocks.** No test creates a real background `Task.Run` loop and then accesses WPF DependencyObjects from inside that loop. The violation is a runtime concurrency bug that only manifests in the actual async execution context.

3. **No locale simulation.** The exception suppressor `ex.Message.Contains("different thread")` was never tested against a German, Italian, or Korean exception message. There is no test that verifies the suppressor does not silently swallow exceptions it was not intended for.

---

## Confirmed Regressions

| # | Symptom | Root Cause | Version Introduced | Fixed In | Test Coverage |
|---|---|---|---|---|---|
| REG-1 | Crash dialog on non-English Windows | `GetEffectiveCadenceInterval()` reads `Application.Current.MainWindow.IsVisible` from `Task.Run` background thread | v3.3.0 | v3.3.1 | ❌ No test covers this |
| REG-2 | Failsafe fans / watchdog triggers with temperature-based lighting active | `LightingViewModel.OnMonitoringSampleUpdated` accesses WPF properties on monitoring thread; per-subscriber catch isolates it but starves watchdog heartbeat | v3.3.0 | Not fixed (masked) | ❌ No test covers this |
| REG-3 | Fan curves destroyed after verification failure | `FanVerificationService` called `SetFanMode(Default)` on failure, resetting all curves | v3.3.0 | v3.3.0 (same release, fixed in bug-fix commit) | ❌ No test for failure revert path |
| REG-4 | Calibration wizard fails silently | `FanCalibrationControl.xaml.cs` bare `catch { }` swallowed `LoggingService` null ref during initialization | v3.3.0 | v3.3.1 (prev session) | ❌ No test for init failure |
| REG-5 | RGB brightness destroyed by scene application | `RgbSceneService` mapped 0-100% brightness to 0-255 int; ARGB bit position indexing was off-by-one | v3.3.0 | v3.3.1 (prev session) | ❌ No test for brightness mapping |
| REG-6 | Model 8D2F (OMEN 16-am0xxx) not recognised | Entry missing from `KeyboardModelDatabase` and `ModelCapabilityDatabase` | Community report post-3.3.0 | v3.3.1 | ❌ No regression test for DB completeness |
| REG-7 | Model 8C2F (Victus 16-r0xxx) not recognised | Entry missing from `ModelCapabilityDatabase` | Community report post-3.3.0 | v3.3.1 | ❌ No regression test for DB completeness |
| REG-8 | Log format silently changed | `LoggingService.Error(string, Exception)` overload now emits structured JSON payload instead of flat string | v3.3.0 | Not fixed | ❌ No test for log format stability |
| REG-9 | `_baseInterval`/`_lowOverheadMode` dead but publicly settable | Callers that use `SetLowOverheadMode(true)` silently have no effect | v3.3.0 | Not fixed | ❌ No test verifies polling interval is affected by this call |

---

## Highest-Risk Undetected Scenarios

These are known fragile paths in the current codebase that have no test coverage and have not yet caused a user-visible failure. They are latent regressions.

---

### RISK-1: Monitoring Loop Silent Death

**Scenario:** `MonitorLoopAsync` throws an unhandled exception past the `consecutiveErrors >= maxErrors` gate (e.g., LibreHardwareMonitor bridge crash during reinitialize).

**Expected behaviour:** Loop exits, monitoring flatlines. `HardwareWatchdogService` detects stalled `HealthStatusChanged` events and triggers failsafe fans. No monitoring data displayed.

**Current gap:** No test verifies the watchdog fires when `MonitorLoopAsync` exits prematurely. No test verifies the monitoring silence is surfaced to the user. No test verifies the app is still otherwise functional.

**Test required:** Inject a fault into `IHardwareBridge.GetCurrentSample()` that throws on every call. Assert that `HardwareWatchdogService.WatchdogTriggered` fires within the timeout and that `IsFanSafeMode` becomes `true`.

---

### RISK-2: Resume Race Condition (Window State During Wake)

**Scenario:** System resumes from sleep. MainWindow fires `Activated`, then `IsVisibleChanged`. The `HardwareMonitoringService.OnResume()` calls `Start()` on the monitoring loop. The loop starts before `SetUiWindowActive(true)` fires (because `IsVisibleChanged` is delayed until the window is actually rendered). Result: first few monitoring cycles run at idle cadence (5 seconds) even though the window is visible.

**Expected behaviour:** Minor impact — first update may be 5 seconds late post-resume. Not a crash.

**Current gap:** This scenario is not tested. The `Dispatcher.InvokeAsync(..., DispatcherPriority.Loaded)` wiring added in 3.3.1 mitigates but does not eliminate this. If the monitoring service starts before `Loaded` priority fires, the residual window is up to 5 seconds.

**Test required:** Mock the resume sequence; verify `SetUiWindowActive(true)` fires before the fifth monitoring tick.

---

### RISK-3: `NormalizeMonitoringSample` Data Consistency Under Load

**Scenario:** Under high monitoring load (>1 subscriber processing slowly), the dispatcher is backed up. `NormalizeMonitoringSample` mutates sample object S1 on the dispatcher thread. Meanwhile, subscriber N (not yet run) still holds reference to S1 and reads `CpuLoadPercent` — which was just set to a normalized value mid-read.

**Expected behaviour:** Subscriber N reads normalized value from S1, but the normalization was applied for a different subscriber's context (possibly with different `previous` state).

**Current gap:** Object mutation is not thread-safe. No test exercises concurrent subscriber access to the same sample object. The scenario does not cause a crash (no exception is thrown) but produces wrong telemetry values.

**Test required:** Two subscribers on `SampleUpdated` — one fast, one slow (100ms delay). Assert that both receive consistent values for the same sample. This test will likely fail with current code.

---

### RISK-4: LibreHardwareMonitor Reinitialize Mid-Read

**Scenario:** LibreHardwareMonitor is reinitialized on resume (`_ = Task.Run(() => Reinitialize())`). While reinitializing, the monitoring loop is also running and calling `IHardwareBridge.GetCurrentSample()`. These access the same `Computer` object concurrently.

**Current gap:** `LibreHardwareMonitorImpl` has no lock around `GetCurrentSample()` vs `Reinitialize()`. Both are fire-and-forget `Task.Run`. Concurrent access to `Computer.Hardware` during reinitialize has caused `NullReferenceException` and `IndexOutOfRangeException` in earlier versions (documented in BUGS_v2.x.x.md).

**Test required:** Run `Reinitialize()` and `GetCurrentSample()` concurrently 100 times. Assert no exception escapes.

---

### RISK-5: `KeyboardLightingService` Deadlock on UI-Thread Init

**Scenario:** In a future startup change, `KeyboardLightingService` is constructed on the UI thread (directly in `App.xaml.cs`, not in a `Task.Run`). The constructor calls `InitializeAsync().GetAwaiter().GetResult()`. `InitializeAsync()` awaits `_hpBiosService.GetKeyboardFirmwareVersion()` which calls WMI. The UI thread is blocked. The WMI call internally attempts to marshal to the SynchronizationContext for completion. The SynchronizationContext is the WPF dispatcher, which is blocked waiting for `GetResult()`. Deadlock.

**Current state:** This does not deadlock today because the construction is done from a `Task.Run` context (no SynchronizationContext). But the constructor does not document this requirement and the call site is not guarded.

**Test required:** Construct `KeyboardLightingService` on a test SynchronizationContext. Assert it completes within a timeout (not deadlock). This test will likely hang with current code.

---

### RISK-6: Fan Curve Applied Before Safe Mode Clears

**Scenario:** Safe Mode (`IsSafeModeActive = true`) is active for 30 seconds post-startup. A hotkey that triggers `SetCurvePreset` directly calls `FanService.ApplyCurveAsync()`. Safe Mode is checked in the tray action dispatcher but NOT in `FanService.ApplyCurveAsync()` itself. The fan curve is applied during the boot window when hardware state is not fully settled.

**Current gap:** The Safe Mode guard is UI-layer only. No test verifies that fan commands are rejected during Safe Mode.

**Test required:** Set `IsSafeModeActive = true`. Send a `SetCurvePreset` command via the hotkey path. Assert the command is held or rejected. Assert it executes after Safe Mode clears.

---

### RISK-7: Model Fallback Returns Wrong `MaxFanLevel`

**Scenario:** User has model `8C0F` (Victus 16, 2023 AMD). This is not in the database. Family fallback resolves to `VictusAMD` family defaults. Family defaults have `MaxFanLevel = 55`. The hardware supports `MaxFanLevel = 75`. Fan curves above 55% are silently clamped to 55.

**Current gap:** No user notification that fallback is active. No warning in logs that capability is potentially wrong. `UserVerified = false` entries have identical runtime behaviour to `UserVerified = true`.

**Test required:** Construct `ModelCapabilityDatabase` with a model not in the DB. Call `GetCapabilities("8C0F")`. Assert the returned entry has `IsExactMatch = false` and a warning is logged. Assert that calls to fan level exceed capped value log a diagnostic.

---

## Test Coverage Matrix

Colour code: ✅ Has test | ⚠️ Partial test | ❌ No test

| Scenario | Coverage | Priority |
|---|---|---|
| Monitoring loop starts, polls, fires `SampleUpdated` | ✅ | — |
| Monitoring loop handles consecutive errors (backoff, max, stop) | ⚠️ Partial | High |
| Monitoring loop restarts after bridge reconnect | ❌ | High |
| `GetEffectiveCadenceInterval()` reads volatile bool, not WPF objects | ✅ (new) | — |
| `SetUiWindowActive` called correctly from UI thread | ❌ | Medium |
| `NormalizeMonitoringSample` returns independent copy | ❌ | Medium |
| LightingViewModel `OnMonitoringSampleUpdated` uses dispatcher | ❌ | High |
| `QueueMonitoringUiSample` single-item coalesce | ❌ | Medium |
| Watchdog fires when monitoring flatlines | ❌ | High |
| Watchdog fires when fan RPMs too-high too-long | ⚠️ Partial | High |
| Fan curve applied successfully (WMI path) | ✅ | — |
| Fan curve applied successfully (EC path) | ✅ | — |
| Fan curve rejected if hardware returns no-ok | ⚠️ | High |
| Fan verification failure does NOT revert fan mode | ✅ (new) | — |
| Fan verification periodic timer does NOT run | ❌ | Medium |
| Model in database — exact match | ✅ | — |
| Model not in database — family fallback used and warned | ❌ | High |
| Model DB consistency (keyboard DB ↔ capability DB parity) | ❌ | Medium |
| Safe Mode gates tray action fan commands | ✅ | — |
| Safe Mode does NOT gate direct `FanService.ApplyCurveAsync` | ❌ | Medium |
| `KeyboardLightingService` init from Task.Run (no deadlock) | ✅ (implicit) | — |
| `KeyboardLightingService` init from UI thread (deadlock guard) | ❌ | High |
| `WmiBiosMonitor` temperature read failure logged (not swallowed) | ❌ | High |
| `DiagnosticExportService` failure logged (not swallowed) | ❌ | Medium |
| Log format stability (Error overload emits expected format) | ❌ | Medium |
| Resume cycle: monitoring restarts within 10s | ❌ | High |
| Resume cycle: `SetUiWindowActive(true)` fires before 5th tick | ❌ | Medium |
| LibreHardwareMonitor concurrent reinit + read (no crash) | ❌ | High |
| Brightness mapping 0-100% → expected RGB values | ✅ (new) | — |
| RGB scene application does not destroy pre-scene brightness | ✅ (new) | — |

---

## Required New Tests — Priority Queue

Ordered by impact (regression prevention value vs. effort):

### T1 — Watchdog Fires on Monitoring Loop Death
```
Priority: P0
Test type: Integration (requires real Task.Run + cancellation token)
Assert:
  1. MonitorLoopAsync exits unexpectedly
  2. HardwareWatchdogService.MonitoringSilenceDuration exceeds threshold within 10s
  3. WatchdogTriggered event is raised
  4. Fan safe mode is active
```

### T2 — LightingViewModel Uses Dispatcher for WPF Access
```
Priority: P0
Test type: Unit (requires TestDispatcher or explicit SynchronizationContext)
Assert:
  1. Call OnMonitoringSampleUpdated from a background thread
  2. No InvalidOperationException thrown from the method itself
  3. WPF property access (temperature thresholds) occurs on dispatcher thread
```

### T3 — Model Fallback Warns and Returns Conservative Defaults
```
Priority: P0
Test type: Unit
Assert:
  1. GetCapabilities("FFFFFFFF") returns non-null (no throw)
  2. Result.IsExactMatch == false
  3. LoggingService.Warn was called at least once referencing model ID
  4. MaxFanLevel is within valid range
```

### T4 — Normalize Returns Independent Copy
```
Priority: P1
Test type: Unit
Assert:
  1. MonitoringSample S1 created with CpuLoadPercent = 0
  2. Normalize(S1, previous) returns S2
  3. S2.CpuLoadPercent != 0 (normalized from previous)
  4. S1.CpuLoadPercent still == 0 (original unmutated)
```

### T5 — LibreHardwareMonitor Concurrent Access
```
Priority: P1
Test type: Stress (run 100 times)
Assert:
  1. GetCurrentSample() and Reinitialize() called concurrently from Parallel.For
  2. No exception escapes after 100 iterations
  3. At least 80% of GetCurrentSample() calls return non-null
```

### T6 — Fan Level Clamping at MaxFanLevel Logs Warning
```
Priority: P1
Test type: Unit
Assert:
  1. ModelCapabilities.MaxFanLevel = 55
  2. FanService.ApplyLevelAsync(70) is called
  3. Fan receives SetFanLevel(55)
  4. LoggingService.Warn called with "clamped" or "exceeds configured max"
```

### T7 — `SetLowOverheadMode` Has No Effect on Actual Poll Interval
```
Priority: P2 (or alternatively: delete the method, no test needed)
Test type: Unit
Assert:
  1. HardwareMonitoringService.SetLowOverheadMode(true) called
  2. GetEffectiveCadenceInterval() returns _idleCadenceInterval (not _lowOverheadInterval)
  3. Warning logged: "SetLowOverheadMode has no effect; use SetUiWindowActive"
```

### T8 — Resume Monitoring Restart Within Timeout
```
Priority: P1
Test type: Integration
Assert:
  1. HardwareMonitoringService.Stop() called (simulate suspend)
  2. HardwareMonitoringService.Start() called (simulate resume)
  3. SampleUpdated fires within 3 seconds
  4. HealthStatus == Healthy within 5 seconds
```

---

## Root Cause Summary: Why These Tests Were Never Written

1. **The test suite was structured to test functionality, not thread concurrency.** Happy-path tests are fast and reliable. Concurrency tests are slow, non-deterministic, and sometimes require application-level integration. They were deferred and never added.

2. **WPF thread-affinity errors only appear when `Application.Current` is fully initialized.** Unit tests that don't boot a full WPF application cannot trigger this class of error. A proper integration test fixture running a headless WPF application (available via `Dispatcher.Run` on a background STA thread) is needed.

3. **The locale test gap is systemic.** Testing exception messages in different languages requires setting `Thread.CurrentThread.CurrentCulture` and `CurrentUICulture` in test setup. No test in the suite does this.

4. **New features were tested manually.** The 3.3.0 changelog credits "tested on OMEN 16 2024 AMD" for new features — manual, one configuration, one locale. There is no documented regression test protocol for releases.

---

## Proposed Release Gate Criteria

Before any future release is tagged:

| Gate | Requirement |
|---|---|
| Test pass rate | 100% of existing tests |
| Thread safety | Zero WPF property accesses in Task.Run lambdas (grep-level check) |
| Exception swallowing | Zero bare `catch { }` without log comment (grep-level check) |
| New features | Each new code path has at minimum T-happy + T-error test |
| Model DB | Parity check: every product ID in `KeyboardModelDatabase` has a corresponding entry in `ModelCapabilityDatabase` (and vice versa) |
| String matching | Zero `ex.Message.Contains(...)` exception classification (grep-level check) |
| Release scope | Hotfix: ≤10 files; Patch: ≤30 files; Minor: ≤60 files with partial regression test; Major: full regression suite |
