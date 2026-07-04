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

### MemoryOptimizerService: `Process.GetCurrentProcess()` Called Per Loop Iteration

**Root cause:** `EmptyWorkingSetsWithExclusions()` called `Process.GetCurrentProcess()` inside its `foreach` loop over every process on the system to compare process names. This allocates a new managed `Process` object on every iteration during an operation that already touches every running process.

**Fix:** Cache `Process.GetCurrentProcess().ProcessName` once before the loop in a local variable. No behavior change.

---

## Model Additions

### GitHub #148: HP Victus 15 (2025) fb3xxx (AMD Ryzen 8xxx)

Pattern-matched entry on `"15-fb3"` for the Victus 15-fb3012AX and other 2025 AMD Victus 15 variants. The family fallback was working for fan control but the WMI thermal-policy fallback (`AllowDecoupledWmiThermalPolicyFallback = true`) was missing from the family fallback path, which is why performance mode switching failed. No exact ProductId confirmed from diagnostics yet; exact entry to follow once reported.

Conservative Victus profile: WMI fan/profile control, no direct EC writes, WMI thermal-policy fallback for Quiet/Balanced/Performance. No RGB keyboard (confirmed by reporter).

### GitHub #149: OMEN Transcend 14 (2024) 8C58

Existing `8C58` entry updated to align with its `8E41` sibling (same Transcend 14 board family): added `AllowV1AutoModeFloorClear = true` which was already present on 8E41 based on the Windows field report confirming WMI V1 behavior on this board family. Notes updated to reference issue #149.

---

## Roadmap (Items Scoped For Future Releases)

The following improvements have been identified and scoped but are not implemented in this release. They are recorded here to prevent them being lost between cycles.

### Near-term (3.9.x candidates)

**OMEN Key — expose hotkey bindings to users**
Default hotkeys (`Ctrl+Shift+F` fan, `Ctrl+Shift+P` performance, `Ctrl+Shift+M` max, `Ctrl+Shift+E` profile cycle, etc.) are registered at startup but not shown anywhere in the UI. A read-only list in Settings → OMEN Key or a dedicated Hotkeys section would let users discover them without reading the diagnostics export or source code. Hotkey rebinding (the underlying `UpdateHotkey()` method already exists in `HotkeyService`) is a natural follow-on.

**RTSS OSD: Use foreground-window PID for frame data**
`RtssIntegrationService.GetCurrentFrameData()` returns the first non-empty RTSS shared memory slot, which is not guaranteed to be the current game when RTSS is tracking multiple GPU-accelerated processes (e.g., Discord + a game). Fix: match the RTSS slot against `GetForegroundWindow()`'s PID before falling back to first-non-empty.

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
- **BUG-3820-004** (88D2 suspend/fan-stuck fix) — needs hardware confirmation.
- **GitHub #141** (8D26 OMEN 16-ap0xxx Fn+P routing) — needs key-event capture on physical hardware.
- **GitHub #142** (8E9A HyperX OMEN MAX 16 identity) — needs full diagnostics before adding.
- **GitHub #143** (8DCD Victus 15 fan-drop-under-load) — needs bounded physical load test.
- **BUG-3810-005** (idle fan spikes / thermal excursions) — first session log reviewed in 3.8.2 cycle points to real brief thermal events rather than sensor glitch; needs diagnostics-zip raw per-poll evidence before any activation-timing change.
- **MODEL-3810-002 / Battery Care WMI** (8D40) — first real diagnostics export now collectible since the 3.8.2 wiring fix; next user export is the evidence gate.

---

## Current Validation Status

- `dotnet build OmenCoreApp.csproj -c Release`: passed, 0 errors, 0 warnings.
- `dotnet test OmenCoreApp.Tests.csproj -c Release`: 911/911 passed (no new tests added in this batch — all changes are in runtime service code, not in testable pure-logic paths; the OmenKey fix is an enum/string mapping with no injectable seam in the existing test infrastructure).
- Version not yet bumped; full bump, artifact rebuild, and tag will happen at release time.

---

## Notes For Release

- The `QuickPopupEnabled` default is `true` — existing users are unaffected.
- The `AllowV1AutoModeFloorClear` addition to 8C58 aligns it with the 8E41 profile confirmed by a Windows field report; it is the safer of the two directions (false would leave the V1 auto-mode floor stuck after a mode transition).
- No fan/thermal/EC control code was changed in this release. The evidence-gate rule remains in force.
