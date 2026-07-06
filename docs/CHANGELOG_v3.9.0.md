# OmenCore v3.9.0 – UX Polish, Silent-Failure Fixes, and Model Additions

**Release Date:** TBD
**Release Status:** In development on `release/3.9.0`
**Type:** Minor release — no fan/thermal/EC control behavior changes; UX fixes, reliability improvements, and model additions
**Base Version:** v3.8.2

---

## Purpose

v3.9.0 follows immediately after v3.8.2's critical-fix cycle. Rather than another emergency patch, the focus here is polishing the day-to-day experience: fixing silent breakage discovered via codebase audit (the OMEN key action setting was completely non-functional for all four UI options), improving diagnostics for hard-to-debug failure modes (EC write failures, game profile data loss), and addressing user-facing friction (eye-straining tray icon, accidental Display Off clicks). Also includes the first two model additions of the 3.9 series.

---

## Fixed

### OMEN Key Action Setting Was Completely Non-Functional (All Four UI Options)

**Root cause:** `SettingsViewModel.OmenKeyAction` persists the user's selection to `Config.Features.OmenKeyAction` as the UI display string (`"ShowQuickPopup"`, `"ShowWindow"`, `"ToggleFanMode"`, `"TogglePerformanceMode"`). `OmenKeyService.LoadSettings()` then called `Enum.TryParse<OmenKeyAction>(actionStr, ...)` to restore it — but zero of the four UI strings matched any `OmenKeyAction` enum value (`ToggleOmenCore`, `CyclePerformance`, `CycleFanMode`, `ToggleMaxCooling`, `LaunchExternalApp`, `DoNothing`). The parse silently failed for every option and `_currentAction` stayed at its default `ToggleOmenCore`. Any OMEN key action the user selected in Settings was silently discarded on the next launch.

**Fix:**
- Added `ShowQuickPopup`, `ShowWindow`, `ToggleFanMode`, `TogglePerformanceMode` enum values to `OmenKeyAction` to match the UI strings exactly.
- Replaced `Enum.TryParse` in `LoadSettings()` with an explicit string-switch that maps both the old enum names (backwards-compatible with existing configs) and the new UI strings.
- Added `ShowQuickPopupRequested` event to `OmenKeyService`; `ExecuteAction()` fires it when `ShowQuickPopup` is selected; `MainViewModel` subscribes and calls `App.TrayIcon?.ShowQuickPopup()` — respecting the `QuickPopupEnabled` setting added in 3.9.0's first commit.
- The `ShowWindow` alias maps to `ToggleOmenCoreRequested` (same behavior); `ToggleFanMode`/`TogglePerformanceMode` map to their existing `CycleFanMode`/`CyclePerformance` handlers.

**Impact:** Every user who set an OMEN key action other than the default has had that setting silently ignored since the feature was introduced. This is now fixed.

---

### Tray Icon: White Text On Yellow/Green Background (Eye Strain, #Discord)

**Reported by:** Discord user feedback — "white text on a yellow background is insanely eye straining."

**Root cause:** `TrayIconService.CreateTempIcon()` always used `Brushes.White` for the temperature digit regardless of background color. The yellow badge (65–75°C, the range most users sit at during normal desktop use) is `#DCC800` — a luminance of ~0.74 — making white text nearly unreadable at small tray icon sizes.

**Fix:** Compute approximate WCAG relative luminance (`0.2126·R + 0.7152·G + 0.0722·B`, normalised to [0,1]) and switch to `Brushes.Black` when luminance exceeds 0.45. This affects yellow (#DCC800, lum ≈ 0.74) and light orange (#FF8C00, lum ≈ 0.61) — all other ranges (blue, green, red, magenta) continue to use white. No new settings required.

---

### Quick Access Popup: Accidental Display Off Clicks (User Feedback, #Discord)

**Reported by:** Discord user — "I keep clicking Display Off" on the Quick Access popup.

**Fix (two-part):**
1. The existing **Quick Access shortcut** combo (Settings → Tray & UI) already has `"Disabled"` to hide the middle button. This option was already present — the answer for users who just want to suppress Display Off without losing the popup.
2. New **"Enable quick access popup"** toggle (Settings → Tray & UI, `AppConfig.QuickPopupEnabled`, default `true`). When disabled, both `ShowQuickPopup()` and `ToggleQuickPopup()` fall back to opening the main window instead — so tray left-click and the OMEN key's `ShowQuickPopup` action still do something useful rather than nothing.

---

### Game Profiles Lost On App Crash After Create or Duplicate

**Root cause:** `GameProfileService.CreateProfile()` and `DuplicateProfile()` added the profile to the in-memory list and called `UpdateTrackedProcesses()`, but neither called `SaveProfilesAsync()`. `UpdateProfileAsync()` and `DeleteProfileAsync()` both saved correctly. If the app crashed before the user triggered another save-inducing action, a newly created or duplicated profile was silently lost.

**Fix:** Both methods now fire `_ = SaveProfilesAsync()` (fire-and-forget, matching the pattern used elsewhere in the service) immediately after adding to the list.

---

### FanController EC Write Failures Were Completely Silent

**Root cause:** `FanController.ResetEcToDefaults()` — the method that restores BIOS fan control by writing 10 sequential EC registers — had a bare `catch { return false; }`. Any exception mid-sequence left the EC in a partial manual-control state with zero diagnostic information: no exception type, no message, no indication of which write step failed. `GetBridgeTemperatures()` similarly swallowed bridge read exceptions entirely (`catch { /* fall through */ }`).

**Fix:**
- `ResetEcToDefaults()` now catches `Exception ex` and logs `Warn("ResetEcToDefaults failed (EC may be in partial manual-control state): {message}")` before returning `false`.
- `GetBridgeTemperatures()` now logs `Debug("Bridge temperature read failed, using 0°C fallback: {message}")`.

Neither change affects control behavior — they only surface failures that were previously invisible.

---

### Crash Reports Had No Stack Trace — Impossible to Diagnose

**Root cause:** `App.OnDispatcherUnhandledException` and `OnDomainUnhandledException` both called `Logging.ErrorWithContext(...)`, which formats the exception as `exception={type}: {ex.Message}`. The stack trace was never logged. Community crash reports that include an OmenCore log file show `exception=FileNotFoundException: ` (empty message, no location) with no way to determine which code path caused the crash.

**Fix:** After each `ErrorWithContext` call in both handlers, log `[CrashTrace] {type}: {message}\n{stackTrace}` and `[CrashTrace.Inner] ...` for the inner exception. This uses `ex.GetType().FullName` and `ex.StackTrace` directly — properties that `BuildContextPayload` was not capturing. No behavior change; future crash reports from real users will include the full call stack.

---

### Dashboard Refresh Button: Unhandled `async void` Exception Path

**Root cause:** `HardwareMonitoringDashboard.RefreshDataButton_Click` was `async void` with three sequential `await` calls (`UpdateMetricsAsync`, `CheckForAlertsAsync`, `RefreshAllChartsAsync`) and no top-level `try/catch`. Any exception thrown by these methods after the first `await` would propagate directly to `DispatcherUnhandledException` and show the fatal crash dialog.

**Fix:** Wrapped the three awaits in a `try/catch(Exception ex)` that logs the error to the app log.

---

### SystemOptimizerView Toggle: Unhandled `async void` Exception Path

**Root cause:** `SystemOptimizerView.OnToggleClicked` was `async void` and called `await item.Toggle(desiredState)` with no exception guard. If `Toggle` threw after the await, the exception would propagate to `DispatcherUnhandledException`.

**Fix:** Wrapped `await item.Toggle(desiredState)` in `try/catch(Exception ex)` with error logging.

---

### General Tab Profiles Did Not Sync GPU Power Boost Level

**Reported by:** Community member OsamaBiden (OMEN 16 xd0010AX, 8BCD) — Performance profile gave 90W instead of expected 120W; Quiet profile also gave 90W instead of expected ~50W.

**Root cause:** `GeneralViewModel.ApplyPerformanceProfile()`, `ApplyBalancedProfile()`, and `ApplyQuietProfile()` never set `GpuPowerBoostLevel`. The user's last manually-selected boost level (e.g., "Minimum") persisted through all profile switches. Switching to Performance had no effect on GPU TGP — it stayed at whatever the user last saved in the Custom tab.

**Fix:** Three profile methods in `GeneralViewModel` now check `_systemControlViewModel?.GpuPowerBoostAvailable` and, if true, set the appropriate level: Performance → `"Maximum"`, Balanced → `"Medium"`, Quiet → `"Minimum"`. The same change was made to `MainViewModel.ApplyQuickProfileFromTray()` (the tray quick-profile switcher, which had its own parallel code path that also missed the boost setting). Custom profile and performance-mode-only tray actions deliberately leave boost unchanged. The setter on `SystemControlViewModel.GpuPowerBoostLevel` handles fan service update and config save.

**Impact:** Affects all devices with GPU Power Boost support (`SupportsGpuPowerBoost = true`). Triggered from the General tab profile cards, the tray quick-profile menu, and the Ctrl+Shift+E hotkey cycle (the hotkey path calls `General.ApplyXxxProfile()` which is covered by the GeneralViewModel fix).

---

### OSD Showed Stale "Balanced" Default Before First StateChanged Fires

**Root cause:** `OsdService._lastPerformanceMode` starts as an empty string. When the OSD is shown for the first time, `Show()` applies `_lastPerformanceMode` to the overlay window only if non-empty. If the user opens the OSD within the first few seconds of startup — before `RuntimeStateEngine` fires its first `StateChanged` snapshot — `_lastPerformanceMode` is still `""`, so no mode is applied, and the overlay falls back to `OsdOverlayWindow`'s hardcoded field default (`"Balanced"`). This means a user in Performance mode who opens the OSD immediately at startup sees "Balanced."

**Fix (initial):** In `App.xaml.cs`, immediately after wiring up the `StateChanged` handler, seed `OsdService` with `mainViewModel.CurrentPerformanceMode`. This was later superseded by the root-cause fix in "OSD Performance-Mode Row Showed Stale 'Balanced' At Startup" (see below), which removed this seed because `CurrentPerformanceMode` is also "Balanced" at init time, making the seed counterproductive once the window default was changed to empty.

---

### Custom Tab Appeared White (Default WPF Theme Instead of Dark Theme)

**Reported by:** Community member OsamaBiden (OMEN 16 xd0010AX, 8BCD) — Custom tab was "super bright, all white, definitely something from the theme that's not working."

**Root cause:** `OmenTabItem` in `MainWindow.xaml` defines a local `<TabItem.Style>` to control its visibility via a `MultiDataTrigger` (visible only when Custom profile is selected AND `ShowAdvancedControls` is true). In WPF, a local `TabItem.Style` property completely overrides `ItemContainerStyle` — the `ItemContainerStyle="{StaticResource ModernTabItem}"` set on the parent `TabControlMain` is ignored for any `TabItem` that has its own `Style` defined. `ModernTabItem` provides the entire dark-themed tab template; without it, the OmenTabItem fell back to WPF's default `TabItem` control template, which renders the tab content area with a white/system-theme background.

**Fix:** Added `BasedOn="{StaticResource ModernTabItem}"` to the inline `Style` declaration on `OmenTabItem`. The style now inherits the full dark template from `ModernTabItem` while still overriding `Visibility` via its trigger.

---

### OSD Performance-Mode Row Showed Stale "Balanced" At Startup (Root Cause Fix)

**Root cause:** `OsdOverlayWindow._performanceMode` was initialised to `"Balanced"` as a hardcoded field default. When the OSD was opened within the first ~2 seconds of startup (before `RuntimeStateEngine` fired its first `StateChanged` snapshot), the overlay displayed "Balanced" regardless of the user's actual mode. The previous fix (seeding `OsdService` from `MainViewModel.CurrentPerformanceMode` in `App.xaml.cs`) was a partial mitigation, but `CurrentPerformanceMode` also starts at "Balanced", so the seed value was identical to the bogus default and the row continued to display before real data arrived.

**Fix (root cause):** Changed `_performanceMode = "Balanced"` to `_performanceMode = ""`. `ShowPerformanceModeRow` now guards `!string.IsNullOrWhiteSpace(_performanceMode)` so the row is hidden until a real value is received from `StateChanged` (within ~2 s of startup). Also added `OnPropertyChanged(nameof(ShowPerformanceModeRow))` to the `PerformanceMode` setter so the row appears correctly as soon as the first real mode arrives.

---

### AutomationService Idle Trigger Misbehaved After ~24.9 Days Uptime

**Root cause:** `EvaluateIdleTrigger()` computed idle time as `TimeSpan.FromMilliseconds(Environment.TickCount - lastInputInfo.dwTime)`. `Environment.TickCount` is a signed 32-bit integer that wraps negative at ~24.9 days uptime. Subtracting a `uint` (the Win32 `LASTINPUTINFO.dwTime`) from a negative `int` produces undefined overflow behaviour — idle triggers could fire constantly or never fire on long-running systems.

**Fix:** Changed to `(uint)Environment.TickCount64 - lastInputInfo.dwTime`. Truncating the 64-bit counter to `uint` and performing unsigned subtraction gives the correct millisecond delta modulo 2³², matching the documented Win32 behaviour for `GetLastInputInfo`. Also removed the unused `_lastIdleCheck` field that was allocated but never read.

---

### `DispatcherHelper.RunOnUiThreadAsync` Returned Before Async Action Completed

**Root cause:** `DispatcherHelper.RunOnUiThreadAsync` called `await dispatcher.InvokeAsync(asyncAction)` where `asyncAction` is `Func<Task>`. `InvokeAsync` returns a `DispatcherOperation<Task>`; the single `await` unwraps only that outer operation, returning the `Task` returned by `asyncAction` without awaiting it. Any work the async action did after its first `await` was silently fire-and-forget. The method was not called anywhere in the current codebase, so no user-visible regressions existed, but the bug would surface immediately upon any future call site.

**Fix:** Changed to `await await dispatcher.InvokeAsync(asyncAction)` — the second `await` waits for the inner `Task` returned by `asyncAction` to complete, matching the contract implied by the method's signature.

---

### AutoUpdateService: HardwareWorker Kill Loop Aborted On First Exception; Exit Not Verified

**Root cause:** `InstallUpdateAsync()` called `proc.Kill()` and `proc.WaitForExit(3000)` inside a single `try/catch` wrapping the entire loop. If `Kill()` threw on one process (e.g., `InvalidOperationException` because it already exited), the `catch` at the outer level swallowed the exception and exited the loop entirely — any remaining HardwareWorker processes were not killed. Additionally, `WaitForExit(3000)` returns `bool` (true = exited, false = timed out), but the return value was never checked, so the installer was launched even when the worker process had not yet released its file locks.

**Fix:** Moved `try/catch` inside the loop so one failure doesn't skip remaining processes. Added `proc.HasExited` check before `Kill()`. Check `WaitForExit` return value and log a warning if the process did not exit in time. Added `proc.Dispose()` in a `finally` block.

---

### MemoryOptimizerService: `Process.GetCurrentProcess()` Called Per Loop Iteration

**Root cause:** `EmptyWorkingSetsWithExclusions()` called `Process.GetCurrentProcess()` inside its `foreach` loop over every process on the system to compare process names. This allocates a new managed `Process` object on every iteration during an operation that already touches every running process.

**Fix:** Cache `Process.GetCurrentProcess().ProcessName` once before the loop in a local variable. No behavior change.

---

## Model Additions

### GitHub #125: HP Victus 15-fa1xxx — Direct 8C3F Entry (Fan Control Delay Fix)

HP reuses ProductId `8BB1` across two unrelated models (OMEN 17 2021 and Victus 15-fa1xxx), so the `8BB1` lookup path requires a model-name disambiguation step. Reporter on issue #125 observed a 10-minute delay applying fan speed changes — a symptom of the `8BB1` disambiguation path taking the wrong branch and routing fan commands through a slower fallback.

Added a direct `8C3F` entry with the same conservative Victus 15-fa1xxx profile (`SupportsFanControlWmi = true`, no direct EC writes, no four-zone RGB, single backlight). An exact ProductId match bypasses the ambiguous-ID path entirely.

### Crash Report (2026-07): OMEN 16 (2024) wf1xxx Intel — Direct 8C77 Entry (V1/V2 Profile Mismatch Fix)

**Reported by:** Community member — `FileNotFoundException` (empty message) on the Custom Fan Curve tab ~6 s after navigating to it and applying Quiet mode on an OMEN 16 (2024) wf1xxx Intel (ProductId 8C77, BIOS F.19, v3.8.2).

**Root cause:** ProductId `8C77` had no exact entry in `ModelCapabilityDatabase`. The database fell through to a pattern match on `"16-wf1"` and returned the `8BAB` entry — a V2 percentage-based profile with `MaxFanLevel = 100`. The reporter's device uses V1 WMI fan control (same board family as the confirmed `8C76` sibling, also BIOS F.19). Sending 100-point percentage fan commands to a V1 WMI stack is the most likely source of the `FileNotFoundException` thrown from within the WMI fan-control path.

**Fix:** Added a direct `8C77` entry mirroring the confirmed `8C76` sibling profile: `SupportsFanControlWmi = true`, `SupportsFanControlEc = false`, `MaxFanLevel = 55` (V1 WMI), `FanZoneCount = 2`, `HasFourZoneRgb = true`, `HasMuxSwitch = true`, `SupportsGpuPowerBoost = true`, `UserVerified = false`. An exact ProductId match now prevents the wrong V2/8BAB profile from being applied. Profile will be updated to `UserVerified = true` after the reporter confirms.

### GitHub #148: HP Victus 15 (2025) fb3xxx (AMD Ryzen 8xxx)

Pattern-matched entry on `"15-fb3"` for the Victus 15-fb3012AX and other 2025 AMD Victus 15 variants. The family fallback was working for fan control but the WMI thermal-policy fallback (`AllowDecoupledWmiThermalPolicyFallback = true`) was missing from the family fallback path, which is why performance mode switching failed. No exact ProductId confirmed from diagnostics yet; exact entry to follow once reported.

Conservative Victus profile: WMI fan/profile control, no direct EC writes, WMI thermal-policy fallback for Quiet/Balanced/Performance. No RGB keyboard (confirmed by reporter).

### GitHub #149: OMEN Transcend 14 (2024) 8C58

Existing `8C58` entry updated to align with its `8E41` sibling (same Transcend 14 board family): added `AllowV1AutoModeFloorClear = true` which was already present on 8E41 based on the Windows field report confirming WMI V1 behavior on this board family. Notes updated to reference issue #149.

---

## Improvements

### RTSS OSD: FPS Now Matches The Foreground Game

`RtssIntegrationService.GetCurrentFrameData()` previously returned the first non-empty RTSS shared memory slot. When RTSS tracks multiple GPU-accelerated processes simultaneously (a game plus Discord, a browser, or a game launcher), the first slot is not guaranteed to be the active game — it's insertion order in RTSS's memory, not recency or foreground status. Result: OmenCore could show the wrong game's FPS in the OSD overlay.

Fixed: now reads the foreground window PID via `GetForegroundWindow()` + `GetWindowThreadProcessId()` and selects the RTSS slot matching that PID. Falls back to first-non-empty only when the foreground process is not tracked by RTSS (e.g., when the user is on the desktop). The original "first slot" path is now a fallback, not the primary path.

---

## Roadmap (Items Scoped For Future Releases)

The following improvements have been identified and scoped but are not implemented in this release. They are recorded here to prevent them being lost between cycles.

### Near-term (3.9.x candidates)

**OMEN Key — expose hotkey bindings to users**
Default hotkeys (`Ctrl+Shift+F` fan, `Ctrl+Shift+P` performance, `Ctrl+Shift+M` max, `Ctrl+Shift+E` profile cycle, etc.) are registered at startup but not shown anywhere in the UI. A read-only list in Settings → OMEN Key or a dedicated Hotkeys section would let users discover them without reading the diagnostics export or source code. Hotkey rebinding (the underlying `UpdateHotkey()` method already exists in `HotkeyService`) is a natural follow-on.

**RTSS OSD: Use foreground-window PID for frame data** — ✅ Done in this release (see Improvements above).

**FanController: EC write sequence recovery on partial failure**
`ResetEcToDefaults()` writes 10 sequential EC registers in a single try block; a failure on step 3 leaves registers 1–2 written and 4–10 not. While the log message added in this release now surfaces the failure, a retry loop or step-by-step state tracking would give the EC a better chance of returning to a safe state.

**Corsair iCUE device status stub**
`CorsairSdkStub.GetDeviceStatusAsync()` returns hardcoded `BatteryPercent = 100`, `PollingRateHz = 1000`, `FirmwareVersion = "Unknown"` with a `// TODO: Query device status via iCUE SDK` comment. The iCUE RGB.NET surface exposes device metadata on the `_surface.Devices` collection; battery/polling/firmware data should be queryable from there. Currently surfaced with a `Warn` log so the stub is visible in diagnostics exports.

### Medium-term (3.10+ candidates)

**GPU temperature source when dGPU is idle**
`WmiBiosMonitor` unconditionally prefers the NVIDIA dGPU's NVAPI die-temperature over the WMI BIOS GPU reading whenever NVAPI is available, regardless of which GPU is active. When the dGPU is idle (Eco/hybrid mode, battery save), the NVAPI reading is the dGPU package temp at idle — often 10–15°C higher than what the user's active GPU is actually running at — which pushes fan curves higher than necessary. A correct fix needs a reliable "is the dGPU actually active" signal (GPU load %, MUX state, or NVAPI active/idle status) wired into the monitoring loop without regressing accuracy on the majority of models where the dGPU is genuinely the active GPU. Needs a second corroborating report with diagnostics before any behavior change.

**Hotkey rebinding UI**
`HotkeyService.UpdateHotkey()` exists but is not exposed. A settings panel to let users remap the 8 registered hotkeys would be a meaningful quality-of-life addition for users with conflicting third-party tool bindings.

**AutoUpdateService: verify HardwareWorker process exit before installer launch**
`InstallUpdateAsync()` kills `OmenCore.HardwareWorker.exe` by name with a 3-second `WaitForExit` before launching the installer, but does not verify the process actually exited before proceeding. If the kill fails or times out, the installer may be unable to replace locked binaries. Should check exit code and warn (or abort) if the process is still running.

### Ongoing / Hardware-gated

These items require physical OMEN/Victus hardware to validate and are intentionally not touched from this development environment:

- **BUG-3820-001** (8BCD hang fix) — needs hardware confirmation from the original reporter.
- **8BCD fan oscillation (OsamaBiden)** — Balanced profile: fan RPM reported oscillating rapidly on profile switch (drops to 0 then ramps). Likely BIOS auto-mode floor interaction. Needs per-poll EC register dump from the original reporter during the oscillation window; not reproducible on dev hardware.
- **8BCD Quiet RPM floor too high** — Quiet profile: fans reported at 3000–3400 RPM at idle instead of expected ~1800. May be related to `AllowV1AutoModeFloorClear` logic or a different thermal-policy register on this BIOS revision. Needs RPM vs. EC register snapshot from reporter.
- **8BCD Quiet mode thermal ceiling** — Reporter observes Quiet mode capping at ~70°C instead of throttling gracefully. Could be a thermal-policy WMI mismatch or a fan curve with too-low 100% threshold for this board. Evidence gate: WMI ThermalPolicy confirmation + per-zone temp log at thermal ceiling.
- **8BCD fan ramp-down stepping artifacts** — Rapid small-step oscillation during ramp-down visible in RPM telemetry. Hardware-timing specific; needs RPM log at 100ms resolution during ramp-down from a sustained load. Cannot safely change ramp timing constants without evidence from affected hardware.
- **BUG-3820-004** (88D2 suspend/fan-stuck fix) — needs hardware confirmation.
- **GitHub #141** (8D26 OMEN 16-ap0xxx Fn+P routing) — needs key-event capture on physical hardware.
- **GitHub #142** (8E9A HyperX OMEN MAX 16 identity) — needs full diagnostics before adding.
- **GitHub #143** (8DCD Victus 15 fan-drop-under-load) — needs bounded physical load test.
- **BUG-3810-005** (idle fan spikes / thermal excursions) — first session log reviewed in 3.8.2 cycle points to real brief thermal events rather than sensor glitch; needs diagnostics-zip raw per-poll evidence before any activation-timing change.
- **MODEL-3810-002 / Battery Care WMI** (8D40) — first real diagnostics export now collectible since the 3.8.2 wiring fix; next user export is the evidence gate.

---

## Current Validation Status

- `dotnet build OmenCoreApp.csproj -c Release`: passed, 0 errors, 0 warnings.
- `dotnet test OmenCoreApp.Tests.csproj -c Release`: 913/913 passed (all new fixes are runtime-only; no new tests required).
- Version not yet bumped; full bump, artifact rebuild, and tag will happen at release time.

---

## Notes For Release

- The `QuickPopupEnabled` default is `true` — existing users are unaffected.
- The `AllowV1AutoModeFloorClear` addition to 8C58 aligns it with the 8E41 profile confirmed by a Windows field report; it is the safer of the two directions (false would leave the V1 auto-mode floor stuck after a mode transition).
- No fan/thermal/EC control code was changed in this release. The evidence-gate rule remains in force.
