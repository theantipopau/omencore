# OmenCore v3.2.1 - Hotfix Rollup

**Release Date:** 2026-03-21
**Release Status:** In Progress
**Type:** Hotfix release
**Base Version:** v3.2.0

---

## Summary

v3.2.1 is a rolling hotfix release for post-v3.2.0 regressions reported by users. This changelog is updated continuously as fixes land.

---

## Fixed

### 1. False CPU Overheat Notifications (107C/108C spikes)
- **Issue:** Some users received repeated high-temperature notifications (e.g. 107°C and 108°C) even when real system temperatures were not critically high.
- **Root Cause:** Thermal alerts could trigger on transient or implausible sensor spikes without requiring sample persistence, and without filtering by telemetry state quality.
- **Fix Deployed:**
  - Added telemetry sanity checks before thermal notifications:
    - Ignore invalid/unusable sensor states for alerting.
    - Ignore implausible out-of-range values.
  - Added short persistence requirement (consecutive readings) before warning/critical notifications are emitted.
  - Reset per-sensor consecutive counters when temperatures return to normal.
- **File:** src/OmenCoreApp/Services/ThermalMonitoringService.cs
- **Status:** Fixed

### 2. Main Window Always Maximized / Cannot Minimize
- **Issue:** A user reported the main window could stay maximized and could not be minimized reliably.
- **Root Cause:** Custom title bar drag handling could intercept clicks originating from window control buttons, causing minimize/maximize interactions to behave inconsistently.
- **Fix Deployed:**
  - Added guard in title bar mouse handling to ignore input originating from window control buttons.
  - Added resilient drag handling to avoid input-state races impacting window controls.
- **File:** src/OmenCoreApp/Views/MainWindow.xaml.cs
- **Status:** Fixed

### 3. CPU Temperature Stuck at Initial Reading on OMEN 16-xd0xxx
- **Issue:** Users on OMEN 16-xd0xxx reported CPU temperature appearing stuck while GPU telemetry continued updating.
- **Root Cause:** On affected models, WMI/ACPI CPU temp path can become less reliable than worker-backed sensor source under real workloads.
- **Fix Deployed:**
  - Enabled model-specific CPU temperature source override for OMEN 16-xd0xxx family, prioritizing worker-backed CPU telemetry path.
- **File:** src/OmenCoreApp/Hardware/WmiBiosMonitor.cs
- **Status:** Fixed

### 4. Auto Mode Could Briefly Drop Fans to 0 RPM During Reset
- **Issue:** Switching from aggressive modes to Auto could cause a transient fan drop to 0 RPM and short temperature spike.
- **Root Cause:** V1 reset path explicitly wrote `SetFanLevel(0,0)`; some firmware treats this as hard-stop behavior before BIOS auto policy fully resumes.
- **Fix Deployed:**
  - Removed explicit V1 zero-level reset write in max-mode reset sequence.
  - Auto release now relies on `SetFanMode(Default)` plus transition hint (`SetFanLevel(20,20)`) to avoid hard-stop dips.
- **File:** src/OmenCoreApp/Hardware/WmiFanController.cs
- **Status:** Fixed

### 5. Extreme Preset Not Reaching Full Fan at 75°C
- **Issue:** Users reported Extreme mode not fully maxing fan behavior around 75°C.
- **Root Cause:** Curve interpolation and safety clamping could still yield less than 100% at/above 75°C for Extreme-named presets.
- **Fix Deployed:**
  - Added explicit Extreme preset rule: force fan target to 100% when temperature is at or above 75°C.
- **File:** src/OmenCoreApp/Services/FanService.cs
- **Status:** Fixed

### 6. Keyboard Brightness Tray Levels Collapsing (Medium/High)
- **Issue:** Brightness quick actions from tray did not produce distinct Low/Medium/High behavior.
- **Root Cause:** Tray level mapping sent values in 0–255 scale to a service expecting 0–100, causing clamping and collapsed levels.
- **Fix Deployed:**
  - Corrected tray brightness mapping to 0/33/66/100.
- **File:** src/OmenCoreApp/ViewModels/MainViewModel.cs
- **Status:** Fixed

### 7. CPU Temperature Can Stay Stuck After Sleep/Resume Until App Restart
- **Issue:** Some systems could show frozen CPU temperature after waking from sleep; fully restarting OmenCore restored updates.
- **Root Cause:** Monitoring resumed without forcing sensor pipeline reinitialization, so stale post-resume telemetry could persist.
- **Fix Deployed:**
  - Added resume-time monitoring recovery in `HardwareMonitoringService`.
  - On resume, freeze counters and timeout state are reset.
  - Added best-effort bridge refresh (`TryRestartAsync`) shortly after wake to reinitialize sensor reads.
- **File:** src/OmenCoreApp/Services/HardwareMonitoringService.cs
- **Status:** Fixed

### 8. Auto-Update Could Miss New Builds When Pre-Release Channel Is Enabled
- **Issue:** Some users still had to manually download/install newer builds.
- **Root Cause:** Update checks queried only GitHub `releases/latest` and did not fully honor `IncludePreReleases` preference.
- **Fix Deployed:**
  - Added release-feed selection logic using GitHub `releases?per_page=20`.
  - Update service now respects `IncludePreReleases` and selects the first non-draft matching release.
  - Stable endpoint is still used as fast path when prereleases are disabled, with feed fallback for resiliency.
- **File:** src/OmenCoreApp/Services/AutoUpdateService.cs
- **Status:** Fixed

### 9. Standalone Status Could Report HP WMI BIOS as Hard Missing Despite Fallback Backend
- **Issue:** Settings could show "Missing required component: HP WMI BIOS" even when PawnIO and/or OMEN service fallback was active.
- **Root Cause:** Dependency audit treated HP WMI BIOS as always required, without considering active fallback backends.
- **Fix Deployed:**
  - Audit now reclassifies missing HP WMI BIOS as fallback-limited (not hard-required) when PawnIO or OMEN fallback is detected.
  - Status summary now explains that fallback backend is active and only HP-dependent features may be limited.
- **File:** src/OmenCoreApp/Services/SystemInfoService.cs
- **Status:** Fixed

### 10. Ryzen CPU Temperature Could Read Implausibly Low During Gaming
- **Issue:** Some OMEN Ryzen + RTX systems reported unrealistically low CPU temperatures (e.g. ~34°C while gaming), creating fan-control trust concerns.
- **Root Cause:** CPU temperature selection could prioritize suboptimal sensor names on some AMD systems (especially per-core style sensors), under-reporting package-level thermal reality.
- **Fix Deployed:**
  - Added vendor-aware CPU sensor ranking in HardwareWorker.
  - AMD path now prioritizes aggregate/package/Tctl/Tdie/CCD-style sensors over weak per-core picks.
  - Added guardrail: when CPU load is meaningful, reject implausibly low candidate if a much hotter valid sensor exists.
- **File:** src/OmenCore.HardwareWorker/Program.cs
- **Status:** Fixed

### 11. OMEN 16-ap0xxx (Board 8E35) Model-Specific CPU Temp Override
- **Issue:** OMEN 16-ap0xxx (board 8E35) required worker-backed CPU telemetry preference for accurate temperature reads.
- **Fix Deployed:**
  - Expanded model override match list to include `16-ap0` and board identifier `8E35` in monitoring source selection.
- **File:** src/OmenCoreApp/Hardware/WmiBiosMonitor.cs
- **Status:** Fixed

### 12. Linux Board Support Request Workflow Added
- **Issue:** Users asked what data to provide when requesting new board support (for example 16-ap0xxx / 8E35).
- **Fix Deployed:**
  - Added a dedicated diagnostics checklist in Linux README with required and optional data.
  - Includes `omencore-cli diagnose`, `dmidecode`, hp-wmi/sysfs tree capture, module state, and optional `acpidump`.
- **File:** src/OmenCore.Linux/README.md
- **Status:** Documented

### 13. Performance Mode Could Drop Unexpectedly Due to Transient Power-State Glitches
- **Issue:** Some users reported switching from max/performance behavior to power-saving-like behavior at random intervals.
- **Root Cause:** AC/battery status-change handling could react to transient line-state jitter and apply battery profile too aggressively.
- **Fix Deployed:**
  - Added debounced, verified power-state transitions (multi-sample confirmation before applying profile changes).
  - Resume path now uses the same verified transition flow.
  - Pending transitions are canceled cleanly when superseded.
- **File:** src/OmenCoreApp/Services/PowerAutomationService.cs
- **Status:** Fixed

### 14. App Version Could Stay Stale After Update on Some Install Paths
- **Issue:** Some users reported still seeing old version text after updating multiple times.
- **Root Cause:** Current-version detection relied heavily on `VERSION.txt`; stale file copies could win over actual binary metadata.
- **Fix Deployed:**
  - Hardened current-version resolution in updater service.
  - Now parses both `VERSION.txt` and assembly metadata, preferring the newer semantic version when they differ.
  - Added robust semver parsing for informational/file versions.
- **File:** src/OmenCoreApp/Services/AutoUpdateService.cs
- **Status:** Fixed

### 15. Curve Optimizer Returns "Bad" Error on HP Victus with Phoenix CPU (Ryzen 7 7445HS)
- **Issue:** Users on HP Victus 15 (fb-3093dx, Ryzen 7 7445HS + RTX 4050) reported a red error banner: "Failed to set All-Core CO: Bad".
- **Root Cause:** HP Victus BIOS on Phoenix 2 variants (Model 0x78) routes the Curve Optimizer mailbox through MP1 rather than PSMU. The code only tried PSMU 0x5D; when the BIOS blocks PSMU, it returns `RyzenSmu.SmuStatus.Bad`.
- **Fix Deployed:**
  - Added MP1 0x5D fallback for Phoenix/HawkPoint/Mendocino/Rembrandt/StrixPoint families: if PSMU returns non-OK, the same opcode is retried via the MP1 mailbox before raising an error.
  - Improved error message for persistent `Bad` status: now explains HP BIOS restriction and missing admin rights as likely causes, rather than showing a raw status code.
- **File:** src/OmenCoreApp/Hardware/AmdUndervoltProvider.cs
- **Status:** Fixed

### 16. Bloatware Manager Silently Fails to Remove Apps Without Admin Rights
- **Issue:** Users on HP Victus reported that selecting apps in Bloatware Manager and clicking Remove appeared to do nothing — no error, no confirmation.
- **Root Cause:** `Remove-AppxPackage` and Win32 uninstallers both fail with access-denied when OmenCore is not running as Administrator, but the service did not check privileges and swallowed the failure silently.
- **Fix Deployed:**
  - Added `IsRunningAsAdmin` property (via `WindowsIdentity`/`WindowsPrincipal`) to `BloatwareManagerService`.
  - `RemoveAppAsync` now returns `false` immediately with a clear status message when not elevated.
  - `BloatwareManagerViewModel` now shows an admin-required warning banner on construction if OmenCore lacks elevation.
- **Files:** src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs, src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs
- **Status:** Fixed

### 17. Fan Diagnostic Reports "Failed" on HP Victus (False Negative)
- **Issue:** Running the fan diagnostic on HP Victus hardware (e.g. Ryzen 7 7445HS + RTX 4050) produced a red "Failed" result even when fans were working correctly.
- **Root Cause:** `FanVerificationService` constants were calibrated for OMEN Max hardware: `MaxRpm = 5500`, `RpmTolerance = 0.25`. Victus-class fans peak at 3500–4500 RPM, producing ~30–40% RPM deviations against the 5500 RPM baseline — enough to zero out the accuracy score under the old 15% scoring threshold.
- **Fix Deployed:**
  - `MaxRpm` reduced from 5500 → 4500 RPM (typical HP Victus / mid-range HP gaming fan peak).
  - `RpmTolerance` increased from 0.25 → 0.30 (30% base tolerance accommodates budget-tier fan variance).
  - Accuracy score drop-off denominator doubled (15% → 30%) so deviations in the 15–30% range are not scored as total failures.
- **File:** src/OmenCoreApp/Services/FanVerificationService.cs
- **Status:** Fixed

### 18. Fans Stuck at 100% After Resume from Sleep (OMEN MAX 16-ak0xxx)
- **Issue:** After waking from sleep (especially long sleeps >20 min), fans would spin up to 100% and remain there regardless of the active fan preset, until OmenCore was manually restarted. Logs showed repeated "ReadSampleAsync timed out" pairs cycling for hours after resume.
- **Root Cause (primary):** After a long sleep, LibreHardwareMonitor/NVAPI sensor stacks take >20 seconds to reinitialise. With `DegradedTimeoutThreshold = 2` and a 10-second per-read timeout, two consecutive timeouts trigger a `Stale` health transition. In `Stale`, fan curve output stops and BIOS thermal protection holds fans at 100%. When monitoring self-recovered to `Healthy`, **no code reasserted the active preset** — the recovery notification only updated the UI tray icon.
- **Root Cause (secondary):** The post-resume bridge restart delay was only 1000 ms, too short for firmware to settle; the partial restart drove repeated Stale/Healthy cycling for hours.
- **Fix Deployed:**
  - **`HardwareMonitoringService`:** Increased `RecoverAfterResumeAsync` bridge restart grace delay from 1000 ms → 5000 ms, giving sensor stacks adequate time to reinitialise before the bridge reconnects.
  - **`MainViewModel`:** Added `_prevHealthStatus` tracking in `HardwareMonitoringServiceOnHealthStatusChanged`. When status transitions from `Degraded` or `Stale` back to `Healthy` (outside startup safe mode), `FanService.HandleSystemResume()` is called after a 2-second settle delay to reassert the active fan preset.
- **Files:** src/OmenCoreApp/Services/HardwareMonitoringService.cs, src/OmenCoreApp/ViewModels/MainViewModel.cs
- **Status:** Fixed

---

## Notes

- This is a rolling changelog. Additional v3.2.1 fixes will be appended as new reports are addressed.
- Per maintainer workflow, build/package steps are deferred until explicitly requested.
- Feature request: reverse fan spin (dust-clean mode) requires model/firmware support. No universal WMI method is currently confirmed across OMEN/Victus; implement only on explicitly supported models after capability detection and safety validation.
