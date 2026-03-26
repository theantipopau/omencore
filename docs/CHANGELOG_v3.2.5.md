# OmenCore v3.2.5 - Stability, Model Support, and UX Improvements

**Release Date:** TBD
**Release Status:** In Development (dev/v3.2.5)
**Type:** Minor+ stability, hardware compatibility, and UX improvement release
**Base Version:** v3.2.1

---

## Summary

v3.2.5 closes a bundle of regressions accumulated since v3.2.1, adds hardware model support for previously misidentified HP product IDs, decouples fan control from performance mode selection, hardens the auto-updater, and introduces improvements to the Quick Access fan curve controls and performance mode button ordering.

This changelog uses a split format:
- **Top section** — proactive fixes, improvements, and new features from the dev team.
- **Bottom section** — targeted fixes directly sourced from user feedback (Discord reports and GitHub issues).

---

---

## ✨ New Features & Improvements

### 1. Performance Mode Buttons Reordered (Quiet → Balanced → Perform)
- **Change:** Performance mode button order in Quick Access and the main window now reads left-to-right as Quiet → Balanced → Perform, matching the natural low-to-high intensity progression users expect.
- **Previously:** Order was Balanced → Perform → Quiet (arbitrary/non-intuitive).
- **Status:** ✅ Fixed

### 2. OMEN-Tab Fan Curve Mode in Quick Access
- **Change:** Quick Access fan control now includes an additional "Custom" mode that applies the active OMEN-tab fan curve preset instead of forcing one of the three fixed modes (Auto / Max / Quiet).
- **Benefit:** Users who tune fan curves in the main OMEN tab can now activate them directly from the tray popup without navigating to the full window.
- **Status:** ✅ Fixed

### 3. Fan Control Decoupled from Performance Mode
- **Change:** Switching performance modes (Silent/Balanced/Turbo/Fans+Power) no longer silently overwrites a manually set fan mode or fan curve.
- **Detail:** Fan policy is now decoupled by default. Performance modes only control power plan and EC power limits. An optional `LinkFanToPerformanceMode` config setting (default `false`) restores the previous coupled behavior for users who prefer it.
- **Files:** `PerformanceModeService.cs`, `AppConfig.cs`, `MainViewModel.cs`
- **Status:** ✅ Fixed

---

---

## 🐛 Fixes & Improvements

### 4. Model Capability DB: Added OMEN 17 (2021) Intel (8BB1) Entry
- **Issue:** Product ID `8BB1` was absent from `ModelCapabilityDatabase`. Systems with this board ID silently fell back to `DefaultCapabilities`, which assumes full OMEN feature support — causing incorrect capability flags on affected hardware.
- **Fix:** Added explicit `8BB1` entry for OMEN 17 (2021) Intel with correct fan/RGB/power capability flags.
- **File:** `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- **Status:** ✅ Fixed

### 5. 8BB1 Collision: Victus 15-fa1xxx Misidentified as OMEN 17
- **Issue:** HP product ID `8BB1` is shared between OMEN 17 (2021) Intel laptops and Victus 15-fa1xxx (2022) laptops. Previously, any system with `8BB1` would receive the OMEN 17 keyboard and capability profile, causing incorrect RGB backend selection and wrong capability flags on Victus hardware.
- **Fix:** Added model-name pattern disambiguation logic:
  - `ModelCapabilityDatabase` — new virtual entry `8BB1-VICTUS15` with `ModelNamePattern = "15-fa1"` resolves before the raw product ID hit via `GetCapabilitiesByModelName()`.
  - `KeyboardModelDatabase` — new virtual entry `8BB1-VICTUS15` with `ModelNamePattern = "fa1"`, resolved via `TryDisambiguateByModelName()` when product ID is in `_ambiguousProductIds`.
  - `KeyboardLightingServiceV2.DetectModelConfig()` — now passes WMI model name to `GetConfig(productId, wmiModelName)` enabling disambiguation at detection time.
- **Result:** Victus 15-fa1xxx users with board ID `8BB1` now correctly receive `BacklightOnly` keyboard profile and Victus-family capability flags instead of OMEN 17 4-zone RGB profile.
- **Files:** `ModelCapabilityDatabase.cs`, `KeyboardModelDatabase.cs`, `KeyboardLightingServiceV2.cs`



## 🎯 Targeted Bug Fixes (User Feedback)
### 6. Auto-Updater: Hardened Asset Selection and SHA256 Enforcement
- **Issue:** In-app updater could fail with "invalid executable" even though the manual GitHub download succeeded. Root cause: three compounding bugs:
  1. `SelectPlatformAwareAsset()` only detected installer assets named with `"setup"`, and the fallback for installed-mode builds could incorrectly return the portable `.zip` archive — which cannot be executed as an installer.
  2. No response diagnostics were logged on download (Content-Type, final URL), so redirect-loop and error-page failures were opaque.
  3. Downloads with missing SHA256 in release notes were allowed to proceed without verification.
- **Fixes applied:**
  - Installer detection now also accepts assets containing `"installer"` in the filename (covers any future naming changes).
  - For installed-mode builds, asset selection is strict: only an installer `.exe` is returned; portable archives are explicitly excluded from the fallback path.
  - Response diagnostics logged on every download attempt: HTTP status code, Content-Type, and final URL after redirects.
  - A Content-Type warning is emitted if the download response is not a binary type (e.g., returns HTML — error page detected before saving the file).
  - Downloads are now **blocked** (return `null`) before any network call if no SHA256 hash is present in the release notes, preventing unverifiable installs. MainViewModel already shows "Update requires manual download (missing SHA256)" with the Open GitHub Release button active in this case.
- **Files:** `AutoUpdateService.cs`, `AboutWindow.xaml.cs`


---

### 7. Fan Diagnostics: RPM Calibration, Backend Reporting, and UX Clarity
- **Issues:**
  1. `FanVerificationService` used `MaxRpm = 4500` as the baseline for expected RPM calculations. On high-end OMEN 16/17 models where fans reach 5500–6500 RPM at full speed, this caused verification to report false negatives ("commands have no effect") even though the fan had responded correctly.
  2. The guided diagnostic result contained no information about which backend was used (WMI/EC) or the RPM source, making support triage difficult.
  3. The fan diagnostics view used a fixed-width horizontal `StackPanel` that clipped panels at smaller window widths or high DPI scaling.
  4. The "Guided Diagnostic" button label was unclear about what the test does.
- **Fixes applied:**
  - `MaxRpm` increased from 4500 to 6000. Budget HP/Victus fans (3800–4500 RPM) continue to pass via the 500 RPM absolute floor tolerance in `VerifyRpm()`.
  - Guided diagnostic result now includes backend name and RPM source on the first line.
  - `FanService.FanControlStateDescription` property added — exposes the current fan control state (thermal/diagnostic/curve/preset/auto) for diagnostic UIs.
  - Outer horizontal layout changed from rigid `StackPanel` to `WrapPanel` for responsive wrapping at smaller widths.
  - Left control column changed from fixed `Width="240"` to `MinWidth="200" MaxWidth="280"`.
  - "Guided Diagnostic" renamed to "Fan Verification Sequence" with descriptive subtitle.
  - "Run Full Test" button renamed to "Run Verification Sequence".
  - RPM source legend added to the current state panel (EC=Direct · HWMon=LHM · MAB=Afterburner · Est=Estimated).
- **Files:** `FanVerificationService.cs`, `FanDiagnosticsViewModel.cs`, `FanService.cs`, `FanDiagnosticsView.xaml`
- **Status:** ✅ Fixed

---

### 8. Post-Sleep High Fan RPM Regression: Watchdog Sleep/Wake Race
- **Issue:** After long sleep periods, the hardware watchdog could misinterpret suspend time as a frozen monitoring pipeline and force fans to 90% shortly after wake. On recovery, a race between watchdog recovery and monitoring-health recovery could leave repeated 90% writes latched until the user manually switched fan modes.
- **Root causes:**
  1. `HardwareWatchdogService` remained active across suspend, so a long sleep could satisfy the 90-second freeze threshold immediately after wake.
  2. The watchdog only set `_isWatchdogArmed = false` inside an async `Task.Run`, leaving a race window where duplicate timer ticks could queue extra 90% failsafe writes.
- **Fixes applied:**
  - Added explicit watchdog suspend/resume handlers and wired them into `MainViewModel` system power events.
  - On suspend: watchdog now disarms, clears freeze-breach state, and resets its heartbeat baseline.
  - On resume: watchdog re-arms with a 120-second grace period so sensor reattachment after wake is not treated as a hard monitoring freeze.
  - Closed the async race by setting `_failsafeActive` and `_isWatchdogArmed` before scheduling the failsafe write, preventing duplicate queued 90% writes after recovery.
- **Files:** `HardwareWatchdogService.cs`, `MainViewModel.cs`
- **Status:** ✅ Fixed

---

### 9. System Optimizer: Toggle Responsiveness and Per-Item Pending State
- **Issue:** Optimizer toggles could appear to do nothing or flip back unexpectedly. The root cause was a UI binding race: the toggle was bound two-way, so `OptimizationItem.IsEnabled` changed before the async action ran, and `SystemOptimizerViewModel` interpreted the new value as the previous state, calling apply/revert in the wrong direction.
- **Fixes applied:**
  - Changed optimizer toggles to one-way state binding so the UI no longer mutates the model before the async operation starts.
  - `SystemOptimizerView.xaml.cs` now captures the user’s intended state explicitly and snaps the visual toggle back to the last authoritative value until the apply/revert completes.
  - `SystemOptimizerViewModel.ToggleOptimizationAsync()` now takes `desiredState` directly, so apply vs revert is based on actual user intent rather than transient bound state.
  - Single-item toggles now use `OptimizationItem.IsApplying` instead of the full-page loading overlay.
  - Added an inline `Applying...` indicator per toggle and disabled only the active toggle while it runs.
  - Single-item toggles now refresh optimizer state without showing the global blocking overlay.
- **Files:** `SystemOptimizerViewModel.cs`, `SystemOptimizerView.xaml`, `SystemOptimizerView.xaml.cs`
- **Status:** ✅ Fixed

---

### 10. Bloatware Manager: Win32 Uninstall No-Op and False Success
- **Issue:** Some Win32 removals appeared to succeed while the application remained installed. The service treated most Win32 uninstallers as success regardless of exit code and never verified whether the app actually disappeared from uninstall registry keys.
- **Root causes:**
  1. MSI uninstall strings such as `msiexec /I {GUID}` were preserved as `/I`, which opens modify/repair flows instead of uninstall.
  2. Generic Win32 uninstall commands were only parsed correctly in a few cases, so unquoted `.exe` uninstallers with arguments could fail to launch cleanly.
  3. The service returned success without any post-uninstall verification, so the UI could report removal even when the app was still present.
- **Fixes applied:**
  - Added robust Win32 uninstall command parsing for quoted and unquoted `.exe` uninstallers.
  - MSI uninstall strings now rewrite `/I` to `/X` before execution so they actually uninstall.
  - Silent flags are appended only when they are missing.
  - Win32 uninstall now polls uninstall registry keys after execution and only reports success when the app is no longer detected.
  - Non-zero uninstaller exit codes are no longer treated as blanket success; the final outcome is based on actual post-uninstall state.
- **Files:** `BloatwareManagerService.cs`
- **Status:** ✅ Fixed

---
### [Discord - 2026-03-26] CPU Temperature Random Drops to 40°C on OMEN 17-ck1xxx
**Reported by:** Serg (Discord)
**System:** v3.2.1 · Windows 11 · OMEN 17-ck1xxx · Secure Boot enabled · HP background services running

**Symptom:**
> "On load temp is still 40. Few seconds later it becomes normal and keeps updating. Still from time to time it randomly drops to 40 for several seconds."

**Notes:** User observed improvement over previous version (previous: persistent stuck+spikes). Current behavior: transient ~40°C drops under load before reading normalizes, with occasional recurrence.

**Root-cause hypothesis:**
- The 40°C floor is characteristic of a WMI/ACPI CPU temperature path returning a stale or platform-clamped value during sensor arbitration.
- On OMEN 17-ck1xxx, the worker-backed sensor source can briefly lag behind or drop out, causing the pipeline to fall back to a stale WMI sample before the next poll cycle catches up.
- Similar to the fix applied for OMEN 16-xd0xxx in v3.2.1 (fix #3) and OMEN MAX 16-ah0000 in v3.1.0 (GitHub #78) — model-specific CPU temperature source override is the likely resolution path.

**Planned fix:**
- Added OMEN 17-ck1xxx to the model-specific CPU temperature source override list in `WmiBiosMonitor.ShouldPreferWorkerCpuTemp()`, alongside OMEN 17-ck2xxx, 16-xd0, and OMEN MAX 16. Worker-backed CPU temperature is now prioritized for affected models, preventing the WMI fallback from surfacing stale/clamped 40°C readings during active load.
- **Files:** `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- **Status:** ✅ Fixed

---

### [Discord - 2026-03-26] Quick Access Performance Mode Buttons — Wrong Order

**Reported by:** Serg (Discord) — feature request
**Symptom:** Performance mode buttons in Quick Access show Balanced → Perform → Quiet, which feels backwards. Request: reorder to Quiet → Balanced → Perform (low to high intensity, left to right).
**Status:** ✅ Fixed

---

### [Discord - 2026-03-26] Quick Access — Add OMEN Fan Curve as Fan Mode Option

**Reported by:** Serg (Discord) — feature request
**Symptom:** Quick Access fan section only offers Auto / Max / Quiet. Users who maintain a custom OMEN-tab fan curve have no way to activate it from the popup without opening the full window.
**Request:** Add a "Custom" or fan-curve option that activates the current OMEN-tab preset.
**Status:** ✅ Fixed

---

*This changelog is updated continuously as fixes land on the `dev/v3.2.5` branch.*
