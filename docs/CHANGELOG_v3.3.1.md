# OmenCore v3.3.1 — Release Notes

**Version:** 3.3.1  
**Release Date:** 2026-04-16  
**Release Status:** ✅ Release candidate — pending hardware validation on OMEN PC  
**Previous Release:** v3.3.0 (2026-04-14)  
**Type:** Critical hotfix — all users on v3.3.0 should update

---

## 📦 Artifacts

| File | Platform | SHA256 |
|------|----------|---------|
| `OmenCoreSetup-3.3.1.exe` | Windows Installer | `48BF5F11B30523BE4A39FFE47462A04A1844869B40DC7747143A9143C3C636B1` |
| `OmenCore-3.3.1-win-x64.zip` | Windows Portable | `8558E8E84868CE7AA381CA0B781B4600BB80AA4D4231E653E744670AF81A6FF2` |
| `OmenCore-3.3.1-linux-x64.zip` | Linux (CLI + GUI) | `7211703D295CBA08494D6F14D4930C3B71DFC0453B3CB7438D857F5187128894` |

→ **[View on GitHub Releases](https://github.com/theantipopau/omencore/releases/tag/v3.3.1)**

---

## Overview

v3.3.1 is a targeted hotfix for v3.3.0. The headline issue is a startup crash that affected **all users on non-English Windows** (Italian, Korean, German, French, and any other locale) — manifesting as the `A Task's exception(s) were not observed...` error dialog appearing immediately on launch.

The root cause was a background-thread WPF access violation introduced in v3.3.0's adaptive monitoring cadence feature. The previous session's mitigation extended the English-only exception suppressor; this release removes the violation entirely. Three additional community-reported bugs (RGB backlight killed by scene apply, Calibration Wizard failing to open, missing model IDs) are also fixed.

---

## 🚨 Critical Bug Fixes

### App Crashes on Startup on Non-English Windows ✅ Fixed
**Severity:** CRITICAL  
**Affects:** All users on non-English Windows (Italian — GH #109, Korean — previous report confirmed)  
**Root cause introduced in:** v3.3.0 adaptive monitoring cadence feature  

**Symptom:**  
Crash dialog appears immediately on launch:
```
A Task's exception(s) were not observed either by Waiting on the Task or accessing its
Exception property. As a result, the unobserved exception was rethrown by the finalizer thread.
(Il thread chiamante non riesce ad accedere a questo oggetto perché tale oggetto è di proprietà
di un altro thread.)
```
The inner message is in the OS language. On English Windows the crash was silently suppressed.

**Root Cause:**  
`HardwareMonitoringService.GetEffectiveCadenceInterval()` was introduced in v3.3.0 to adapt the monitor loop's polling cadence based on whether the main window was visible or minimised. It accessed `Application.Current.MainWindow.IsVisible` and `window.WindowState` directly — both WPF `DependencyObject` properties that are thread-affine and may **only** be read from the UI thread that owns them.

`GetEffectiveCadenceInterval()` was called on every iteration of the background monitor loop, which runs on a thread-pool thread via `Task.Run`. Accessing `DependencyObject` properties from a non-owner thread throws `InvalidOperationException: The calling thread cannot access this object because a different thread owns it` on every single polling tick.

On **English Windows**, the pre-existing unobserved-task exception filter in `App.xaml.cs → OnUnobservedTaskException` suppressed the crash via a hardcoded substring match: `invalidOp.Message.Contains("different thread owns it")`. The filter matched, the exception was silently swallowed, and the app worked — albeit with background exceptions on every monitor tick, and the adaptive cadence feature always returning the fallback value.

On **all other locales**, WPF translates the `InvalidOperationException` message into the system language. The English substring check never matched → the exception escalated → `ShowFatalDialog()` was called → the crash dialog appeared.

The monitoring halt/watchdog fix that ran subscriber fan-out with per-subscriber `try/catch` in v3.3.0 meant the exception was caught inside the loop, classified as an unobserved task exception, and forwarded to the filter — it did not crash the process immediately. But the filter misclassified it on non-English systems.

**Fix (root cause removed):**  
`GetEffectiveCadenceInterval()` no longer accesses any WPF objects. The method now reads a `volatile bool _uiWindowActive` field that is updated exclusively from the UI thread.

A new `public void SetUiWindowActive(bool active)` method on `HardwareMonitoringService` is called from `App.xaml.cs`, where `MainWindow.IsVisibleChanged` and `MainWindow.StateChanged` event subscriptions write the correct value. The background thread reads only the primitive — zero thread-affinity risk.

The English-only exception suppressor (`ex.Message.Contains("different thread")`) in `OnUnobservedTaskException` has been **removed**. Two locale-safe checks are retained as the safety net: a stack-trace check (`ex.StackTrace?.Contains("System.Windows.Threading.Dispatcher")`) and a declaring-type check (`ex.TargetSite?.DeclaringType?.FullName?.StartsWith("System.Windows.")`). These match genuine WPF cross-thread violations on any OS language without swallowing unrelated `InvalidOperationException`s whose message happens to contain the words "different thread".

**Files changed:**  
- `Services/HardwareMonitoringService.cs` — `GetEffectiveCadenceInterval()` rewritten; `_uiWindowActive` field + `SetUiWindowActive()` added  
- `App.xaml.cs` — window state wiring (`IsVisibleChanged` + `StateChanged` → `SetUiWindowActive`)  

---

## Bug Fixes

### Calibration Wizard Failed to Open — "LoggingService not available" ✅ Fixed
**Severity:** High  
**Screenshot confirmed:** Error shown in fan curve panel overlay  

`FanCalibrationControl`'s constructor resolved `LoggingService` exclusively from the DI container
and threw `InvalidOperationException("LoggingService not available")` when the container returned
null — a startup-order regression introduced in v3.3.0.

`App.Logging` is a static singleton initialised unconditionally before the DI container is built and
is always available. The constructor now resolves from DI first and falls back to `App.Logging` if DI
returns null, so the wizard opens correctly regardless of DI container initialisation order.

**File changed:** `Controls/FanCalibrationControl.xaml.cs`

---

### RGB Scene Applied from Lighting Tab Turned Off Keyboard Backlight ✅ Fixed
**Severity:** High  
**Reported by:** OMEN 16-xd0xxx community report  

Selecting or applying any RGB scene from the Lighting tab caused the keyboard backlight to turn off
and stay off, with no colours visible even when coloured scenes were active.

`RgbSceneService.ApplyToOmenKeyboardAsync` mapped `scene.Brightness` (0–100 range) through a
`switch` expression to a 0–3 level before calling `KeyboardLightingService.SetBrightness()`.
`SetBrightness()` already expects a 0–100 value and performs the WMI raw-byte mapping internally,
so the value was scaled twice:

```
scene.Brightness (100) → switch → 3 → SetBrightness(3) → WMI raw byte 103
```

The WMI brightness range is 100 (OFF) to 228 (max). Raw byte 103 sits just above the OFF threshold —
colours were written to the hardware correctly, but the backlight was effectively dark.

The 0–3 switch is removed. `scene.Brightness` (0–100) is now passed directly to `SetBrightness()`.

**File changed:** `Services/RgbSceneService.cs`

---

## Added — Model Support

### OMEN Gaming Laptop 16-ap0xxx — ProductId 8D24 (2025 AMD)
Previously fell back to the OMEN16 family default profile, which logged:
```
Capability warning: No exact model entry matched; capability defaults were inferred from the broader model family.
```
Now has a dedicated entry in both databases based on verified community log data:
AMD Ryzen AI 9 365 + RTX 5060, BIOS F.11, V1 ThermalPolicy, MaxFanLevel=55, 2 fans, FourZone keyboard.
PawnIO requires a reboot after first install to activate the driver.

---

### OMEN Gaming Laptop 16-am0xxx — ProductId 8D2F (2024 AMD) — GH #111
**Reported by:** trothbaecher-ship-it (am0168ng)  
Previously logged:
```
OmenCore Model Identity Summary
Resolved model:   Unknown OMEN16 Model (FAMILY_OMEN16)
Resolution source: Family fallback
Confidence: Low
Keyboard model:   Unknown
```
`8D2F` is now in both `ModelCapabilityDatabase` and `KeyboardModelDatabase`. Capabilities are inferred
from the sibling 16-xd0 generation (V1 WMI fan control, MaxFanLevel=55, FourZone keyboard) — UserVerified
is set to false pending community confirmation.

---

### HP Victus by HP Gaming Laptop 16-r0xxx — ProductId 8C2F (2024 Ryzen) — GH #110
**Reported by:** zjkhy94 (Victus 16-r0xxx)  
The keyboard database entry for `8C2F` was already present (added from GH #89). The capability database
entry was missing, causing model identity to fall back to the Victus family default. `8C2F` is now in
`ModelCapabilityDatabase` with the correct Victus-class capabilities (WMI fan control, FourZone RGB,
no MUX switch, no GPU power boost, AMD — no undervolt).

---

## Technical Changes (Implementation Plan)

### Completed implementation steps

| Step | Description | Status |
|---|---|---|
| STEP-01 | Verified `LightingViewModel.OnMonitoringSampleUpdated` is already Dispatcher-safe | ✅ Verified in code — no change needed |
| STEP-02 | Replaced all locale-dependent `ex.Message.Contains(...)` patterns in `FanController`, `PerformanceModeService`, `LibreHardwareMonitorImpl` with type-safe catch clauses | ✅ Complete |
| STEP-03 | Removed English-only `ex.Message.Contains("different thread")` filter from `App.xaml.cs`; replaced with two locale-safe checks (stack trace + declaring type) | ✅ Complete |
| STEP-04 | Replaced 6 bare `catch { }` blocks in `WmiBiosMonitor.cs` with `_logging?.Warn(...)` or explanatory comments | ✅ Complete |
| STEP-05 | Replaced 8 bare `catch { }` blocks in `DiagnosticExportService.cs` with `_logging.Warn(...)` structured logging | ✅ Complete |
| STEP-06 | Remaining locale-dependent string matches cleaned up across additional files | ✅ Complete |
| STEP-07 | Copy constructor added to `MonitoringSample` | ✅ Complete |
| STEP-08 | `NormalizeMonitoringSample()` in `MainViewModel` returns a new copy via copy constructor instead of mutating in place | ✅ Complete |
| STEP-10 | Dashboard subscription deduplication — verified already satisfied in code; no change made | ✅ Verified — no change needed |
| STEP-11 | Dead interval fields `_baseInterval` / `_lowOverheadInterval` removed from `HardwareMonitoringService.SetPollingInterval()`; stub retained for callers | ✅ Complete |
| STEP-12 | `ResumeRecoveryDiagnosticsService` injection made non-nullable (Option A) in `HardwareMonitoringService`, `FanService`, `HardwareWatchdogService`; null guards removed | ✅ Complete — 2026-04-16 |

### Deferred steps (not included in v3.3.1)

| Step | Reason deferred |
|---|---|
| STEP-09 | Requires 60 s hardware chart observation on OMEN PC (SC-4) |
| STEP-13 | High risk; requires hardware sign-off before merge (SC-6) |

---

## New Tests Added

| File | Tests | Area |
|---|---|---|
| `ResumeRecoveryDiagnosticsServiceTests.cs` | 15 | State machine, BeginSuspend/Resume, RecordStep, concurrency (Parallel.For 50 goroutines), Updated event |
| `ModelCapabilityDatabaseFallbackTests.cs` | 15 | Unknown model safe fallback (RISK-7), case-insensitive lookup, family enumeration, DefaultCapabilities never null |
| `FanSafetyClampingTests.cs` | 11 | Safety floor thresholds deterministic and monotone across 0–100°C |
| `ReleaseGateCodeHygieneTests.cs` | 4 | `NoBareCatchBraces_KnownViolations_Advisory`, `NoBareCatchBraces_NewViolations_Blocking`, `NoExMessageContains_InMainSourceTree`, `SourceRoot_ContainsExpectedFiles` |
| `FanControllerEcWatchdogTests.cs` | +2 | `AbandonedMutexException` caught by type, contention flag set correctly |

---

## Release Gate

| Gate | Policy | Threshold |
|---|---|---|
| Bare `catch {}` — pre-existing (83 violations) | **Advisory** — logged as known baseline; never fails build | Reported only |
| Bare `catch {}` — new violations | **Blocking** — fails build immediately | Zero new violations allowed |
| `ex.Message.Contains(...)` in production source | **Blocking** — zero tolerance | Zero violations |

The 83 pre-existing bare-catch violations are recorded in `KnownBareCatchViolations` in `ReleaseGateCodeHygieneTests.cs` (audited 2026-04-16). Any violation with a `filename:line` not in the baseline fails the build. Full cleanup is deferred to a post-v3.3.1 release.

See [RELEASE_GATE_DECISION.md](RELEASE_GATE_DECISION.md) for full rationale.

---

## Release Verification

| Metric | Result |
|---|---|
| Release build | ✅ Build succeeded — 0 errors, 6 pre-existing warnings |
| Test suite | ✅ 171 / 171 passed (0 failed, 0 skipped) |
| Hardware validation | ⚠️ Not performed — development machine has no OMEN hardware |

**Hardware-dependent items not validated:**
- Suspend/resume cycle (suspend → resume → monitoring recovery verified on hardware)
- OMEN MAX 16 EC panic guard
- STEP-09 monitoring dispatch simplification
- STEP-13 `KeyboardLightingService` async init

See [RELEASE_VERIFICATION.md](RELEASE_VERIFICATION.md) for the full verification report.

---

## Issue Tracker

| # | Title | Status |
|---|---|---|
| [#108](https://github.com/theantipopau/omencore/issues/108) | Black screen (Linux) | ⏳ Pending — no repro steps; tracking for 3.3.2 |
| [#109](https://github.com/theantipopau/omencore/issues/109) | Crash after v3.3.0 install — Italian Windows | ✅ Fixed — root cause removed |
| [#110](https://github.com/theantipopau/omencore/issues/110) | Victus 16-r0xxx not in model database | ✅ Fixed — 8C2F added to capability DB |
| [#111](https://github.com/theantipopau/omencore/issues/111) | OMEN 16-am0xxx (8D2F) not recognised | ✅ Fixed — 8D2F added to both DBs |

---

## Files Changed

| File | Change |
|---|---|
| `src/OmenCoreApp/Services/HardwareMonitoringService.cs` | Root-cause fix: thread-safe `_uiWindowActive` flag replaces WPF cross-thread access in `GetEffectiveCadenceInterval()`. Dead interval fields `_baseInterval`/`_lowOverheadInterval` removed (STEP-11). `ResumeRecoveryDiagnosticsService` injection made non-nullable (STEP-12 Option A) |
| `src/OmenCoreApp/App.xaml.cs` | Wire `MainWindow.IsVisibleChanged` + `StateChanged` → `SetUiWindowActive()`; English-only `ex.Message.Contains("different thread")` filter removed (STEP-03); two locale-safe checks retained |
| `src/OmenCoreApp/Controls/FanCalibrationControl.xaml.cs` | Fall back to `App.Logging` when DI returns null for `LoggingService` |
| `src/OmenCoreApp/Services/RgbSceneService.cs` | Remove erroneous 0–3 brightness re-scale before `SetBrightness()` |
| `src/OmenCoreApp/Hardware/FanController.cs` | Replace locale-dependent `ex.Message.Contains("mutex")` with type-safe `ex is TimeoutException or AbandonedMutexException` (STEP-02); bare `catch {}` blocks replaced with structured logging (STEP-04/STEP-06) |
| `src/OmenCoreApp/Services/PerformanceModeService.cs` | Same locale-safe catch classification as FanController (STEP-02); adds explicit `TimeoutException` coverage |
| `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs` | Remove redundant `ex.Message.Contains("SafeFileHandle")` — `ObjectDisposedException` already covers all .NET SafeFileHandle disposals (STEP-02) |
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | Replace 6 bare `catch { }` blocks with `_logging?.Warn(...)` or explanatory comments on static methods (STEP-04) |
| `src/OmenCoreApp/Services/Diagnostics/DiagnosticExportService.cs` | Replace 8 bare `catch { }` blocks with `_logging.Warn(...)` structured logging (STEP-05) |
| `src/OmenCoreApp/Services/FanService.cs` | `ResumeRecoveryDiagnosticsService` injection made non-nullable (STEP-12 Option A) |
| `src/OmenCoreApp/Services/HardwareWatchdogService.cs` | `ResumeRecoveryDiagnosticsService` injection made non-nullable; null-guard block removed (STEP-12 Option A) |
| `src/OmenCoreApp/Models/MonitoringSample.cs` | Copy constructor added (STEP-07) |
| `src/OmenCoreApp/ViewModels/MainViewModel.cs` | `NormalizeMonitoringSample()` returns new copy via copy constructor instead of mutating in place (STEP-08); dead `SetPollingInterval` call paths confirmed clean |
| `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` | Add `8D24`, `8D2F`, `8C2F` |
| `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs` | Add `8D24`, `8D2F` |
| `src/OmenCoreApp.Tests/Services/ResumeRecoveryDiagnosticsServiceTests.cs` | 15 new tests — state machine, concurrency, Updated event |
| `src/OmenCoreApp.Tests/Hardware/ModelCapabilityDatabaseFallbackTests.cs` | 15 new tests — unknown model safe fallback, case-insensitive lookup, family enumeration |
| `src/OmenCoreApp.Tests/Services/FanSafetyClampingTests.cs` | 11 new tests — safety floor thresholds monotone and deterministic |
| `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs` | Restructured: `NoBareCatchBraces_KnownViolations_Advisory` (reports pre-existing debt, never fails) + `NoBareCatchBraces_NewViolations_Blocking` (fails on any violation not in the 83-entry baseline) + `NoExMessageContains_InMainSourceTree` (zero-tolerance, unchanged) |
| `src/OmenCoreApp.Tests/Hardware/FanControllerEcWatchdogTests.cs` | Add 2 tests: `AbandonedMutexException` caught by type, sets contention flag |
| `CHANGELOG.md` | `[3.3.1]` section updated |
| `VERSION.txt` | `3.3.0` → `3.3.1` |
| `src/OmenCoreApp/OmenCoreApp.csproj` | Version metadata bumped to 3.3.1 |
| `src/OmenCore.HardwareWorker/OmenCore.HardwareWorker.csproj` | Version metadata bumped to 3.3.1 |
| `src/OmenCore.Linux/OmenCore.Linux.csproj` | Version metadata bumped to 3.3.1 |
| `src/OmenCore.Avalonia/OmenCore.Avalonia.csproj` | Version metadata bumped to 3.3.1 |
| `src/OmenCore.Desktop/OmenCore.Desktop.csproj` | Version metadata bumped to 3.3.1 |
| `installer/OmenCoreInstaller.iss` | `MyAppVersion` bumped to `3.3.1` |
| `INSTALL.md` | All download links and artifact references updated to v3.3.1 |
