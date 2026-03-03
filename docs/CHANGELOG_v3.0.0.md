# OmenCore v3.0.0 — Architecture Overhaul & Stability Release

**Release Date:** 2026-03-02
**Type:** Major Release — Architecture, Stability, Bug Fixes & New Features
**Reported By:** Discord community, GitHub issues (#42, #46, #64, #67, #68, and others), internal architecture review

> **hotfix1 (2026-03-03):** Fixes a startup error dialog shown to all first-time users due to `ConfigurationService` not being registered in the DI container. See [CHANGELOG_v3.0.0-hotfix1.md](CHANGELOG_v3.0.0-hotfix1.md).
>
> **hotfix2 (2026-03-03):** Seven fixes: (A) XAML `StaticResourceExtension` crash on startup — five undefined resource keys resolved; (B) Secure Boot status displayed inverted in Settings when PawnIO is available; (C) Ctrl+Shift+O global hotkey dead after window deactivation — ToggleWindow preserved in WindowFocusedHotkeys mode (issue #70); (D) `CapabilityWarning` false positive shown to PawnIO users advising OGH install; (E) five missing event unsubscriptions in `MainViewModel.Dispose()`; (F) `_amdGpuService` field marked `volatile` to close startup thread race; (G) GUI polish — tooltip coverage for 18 action buttons across Settings/Monitoring/Bloatware, hardcoded banner colors replaced with theme resources, Gaming Mode button gains proper disabled-state feedback. See [CHANGELOG_v3.0.0-hotfix2.md](CHANGELOG_v3.0.0-hotfix2.md). Installers and checksums below reflect the hotfix2 build.

---

## Summary

v3.0.0 is the most substantial OmenCore release since v2.0.0. The hardware monitoring
pipeline was rebuilt around a self-sustaining driver-free architecture (WMI BIOS + NVAPI
+ PersistentPerformanceCounter + PawnIO MSR — no WinRing0/LHM kernel drivers required
for monitoring). Seven critical regressions that affected real users post-2.8.x are fully
resolved. Four additional reliability improvements were made to the hardware monitoring
layer. A broad set of new features was added: guided fan diagnostics, a full memory
optimizer tab, keyboard lighting effects and brightness control, V2 keyboard engine,
diagnostics reporting, Linux CLI performance mode improvements, headless mode support,
and a comprehensive set of GUI improvements across every major view.

---

## Critical Bug Fixes (P0 / P1 / P2)

### 1. GPU Telemetry Permanently Lost After NVAPI Error (RC-1) — P0
- **Reported By:** Discord users (OMEN 16-wf1xxx / 8BAB) — GitHub #67, #68
- **Symptom:** After a transient NVAPI failure (driver reset, sleep/wake, game launch spike),
  GPU temperature would freeze at 28 °C and GPU power would read 0 W for the entire session.
  The only recovery was a full app restart.
- **Root Cause:** `_nvapiMonitoringDisabled` was set as a permanent boolean flag with no
  recovery path. A single threshold breach (10 consecutive failures) killed GPU telemetry
  until app exit.
- **Fix:**
  - Added `_nvapiDisabledUntil` (DateTime) and `NvapiRecoveryCooldownSeconds = 60` fields.
  - On hitting MaxNvapiFailuresBeforeDisable (10), NVAPI is now **suspended for 60 seconds**
    with a timed `_nvapiDisabledUntil` timestamp instead of permanently disabled.
  - Each poll cycle checks if `DateTime.Now >= _nvapiDisabledUntil` and auto-re-enables.
  - On autorecover, `_nvapiConsecutiveFailures` is reset to 0 so the next failure doesn't
    immediately re-trigger the cooldown.
- **Files:** `OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### 2. OMEN 16-wf1xxx (ProductId 8BAB) Fan Control Non-Functional (RC-2) — P0
- **Reported By:** Multiple Discord reports; confirmed root cause for #68
- **Symptom:** Fan control slider had no effect. Switching performance modes did nothing.
  Auto fan control could not be restored. All fan operations silently failed.
- **Root Cause:** ProductId `8BAB` (Board 8C78, OMEN 16-wf1xxx, 2024 Intel) was missing
  from `ModelCapabilityDatabase`. The Transcend family fallback template was selected, which
  sets `SupportsFanControlWmi = false`, completely disabling WMI fan control.
- **Fix:**
  - Added dedicated entry for `8BAB` with `SupportsFanControlWmi = true`, `MaxFanLevel = 100`,
    `SupportsThermalPolicyV2 = true`, and `UserVerified = false` (pending community confirmation
    on exact EC register layout).
  - Entry positioned immediately after `8BCA` (OMEN 16 wf0xxx 2023) since both share the
    same WMI fan control path.
- **Files:** `OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`

### 3. Fan Auto Mode Shows 0 RPM After Profile Switch (RC-3) — P0
- **Reported By:** Discord, GitHub
- **Symptom:** Switching from Performance/Quiet/Extreme preset to Auto mode caused displayed
  RPM to read 0 for up to 3 seconds. Some models stayed at 0 RPM until another manual action.
- **Root Cause:** `RestoreAutoControl()` was guarded by
  `if (_isMaxModeActive || IsManualControlActive)` which skipped `ResetFromMaxMode()` when
  transitioning from standard presets. A 3-second RPM debounce window remained active with
  no reset, causing the UI to filter the incoming RPM reads as "in transition."
- **Fix:**
  - Removed the `_isMaxModeActive || IsManualControlActive` guard entirely.
  - `ResetFromMaxMode()` is now called **unconditionally** on every `RestoreAutoControl()` call.
  - `_lastProfileSwitch = DateTime.MinValue` clears the debounce window immediately.
  - Added comment explaining why unconditional reset is correct (clean BIOS handoff).
- **Files:** `OmenCoreApp/Hardware/WmiFanController.cs`

### 4. Linux CLI Performance Mode Silently Fails on hp-wmi-Only Systems (RC-4) — P0
- **Reported By:** Linux users with OMEN 16-wf1xxx, Board 8C78 (hp-wmi present, EC absent)
- **Symptom:** `SetPerformanceMode(Performance)` returned `false` silently. No error, no
  effect. Mode appeared to change in UI but hardware remained on default thermal profile.
- **Root Cause:** `SetPerformanceMode()` only called `WriteByte(REG_PERF_MODE, ...)` which
  requires `HasEcAccess`. On boards where `ec_sys` kernel module is absent but `hp-wmi` is
  present, `IsAvailable` was `true` (because hp-wmi satisfies it) but the EC write path
  silently returned `false`.
- **Fix:**
  - Full priority routing added to `SetPerformanceMode()`: **hp-wmi** `thermal_profile` →
    **ACPI** `platform_profile` → **EC register** direct write. Each path is tried in order
    and the first success is returned.
  - `GetPerformanceMode()` now follows the same priority chain (previously read EC only):
    hp-wmi string read → ACPI profile read → EC register fallback. Reported mode now always
    reflects the active backend, eliminating mode desync on hp-wmi-only boards.
- **Files:** `OmenCore.Linux/Hardware/LinuxEcController.cs`

### 5. Secure Boot Warning Shown Alongside Green PawnIO Badge (RC-5) — P1
- **Reported By:** Internal review; user report on Discord
- **Symptom:** Settings page showed a yellow "Secure Boot is enabled" warning even when
  PawnIO was installed and fully operational, creating user confusion.
- **Root Cause:** `LoadSystemStatus()` set `SecureBootEnabled` from the raw registry value
  without checking PawnIO availability. The two status items were surfaced independently
  even though PawnIO's presence resolves the Secure Boot constraint.
- **Fix:**
  - `SecureBootEnabled = rawSecureBoot && !PawnIOAvailable` — PawnIO presence suppresses
    the warning entirely since PawnIO is explicitly Secure Boot-compatible.
  - Added comprehensive `IsPawnIOAvailable()` method that checks: driver service, HKLM
    registry key, and driver file path for reliable detection.
- **Files:** `OmenCoreApp/ViewModels/SettingsViewModel.cs`

### 6. Clean Install Shows "Standalone = Degraded" (RC-6) — P1
- **Reported By:** Discord; multiple first-install reports of misleading health status
- **Symptom:** Brand-new OmenCore installs on clean systems showed "Standalone = Degraded"
  immediately after first launch, before any hardware interaction.
- **Root Cause:** `PerformDependencyAudit()` counted any 2+ missing optional components as
  Degraded. On clean installs, OGH (OMEN Gaming Hub) and HP System Event Utility are both
  absent by design, hitting the threshold immediately. Additionally, `LibreHardwareMonitor`
  was marked `IsOptional = true` despite being explicitly documented as not required.
- **Fix:**
  - Degraded threshold raised from `>= 2` to `>= 3` (requires at minimum 3 optional
    components absent before degrading status to Degraded).
  - `LibreHardwareMonitor` check changed to `IsOptional = false` — its absence is expected
    and should not contribute to degraded score.
  - Status summary text updated to clarify that OGH and HP-SEU are not required for core
    hardware monitoring and fan control.
- **Files:** `OmenCoreApp/Services/SystemInfoService.cs`

### 7. Monitor Loop Exits Permanently on 5 Consecutive Errors (RC-7) — P2
- **Reported By:** Internal; inferred from user reports of "all telemetry frozen, restart fixes it"
- **Symptom:** After 5 consecutive exceptions in the monitoring loop (common during driver
  reset, sleep/wake, or WMI service restart), all hardware telemetry would permanently stop
  updating. Only a full app restart recovered telemetry.
- **Root Cause:** `MonitorLoopAsync` had a `break` statement executed after hitting
  `maxErrors` consecutive exceptions. This exited the while loop permanently with no
  notification to the user and no recovery mechanism.
- **Fix:**
  - On hitting `maxErrors`, the loop now **resets `consecutiveErrors = 0`**, waits 10 seconds
    (respecting cancellation), then **continues** the monitoring loop.
  - A warning is logged: `"[MonitorLoop] N consecutive errors — backing off 10s then restarting loop"`.
  - The loop only exits cleanly on `OperationCanceledException` (normal app shutdown).
- **Files:** `OmenCoreApp/Services/HardwareMonitoringService.cs`

### 8. UI Freeze on Startup Due to Synchronous Keyboard Lighting Init
- **Reported By:** Discord — "app opens but no telemetry / spinning wheel / tray unresponsive"
- **Symptom:** App starts, UI freezes with spinning cursor on some systems. Tray unresponsive.
  No telemetry updates. Affected systems where keyboard lighting initialization was slow.
- **Root Cause:** `KeyboardLightingService.SetAllZoneColors()` was blocking the UI thread
  synchronously during startup color restoration via `.GetAwaiter().GetResult()`.
- **Fix:** Converted synchronous `.GetAwaiter().GetResult()` calls to proper `async/await`
  pattern throughout keyboard lighting init path.
- **Files:** `OmenCoreApp/Services/KeyboardLightingService.cs`, `OmenCoreApp/ViewModels/LightingViewModel.cs`,
  `OmenCoreApp/Services/TemperatureRgbService.cs`, `OmenCoreApp/ViewModels/MainViewModel.cs`

### 9. All Sensors Read 0°C on Models Where WMI BIOS Is Unavailable
- **Reported By:** Discord — OMEN 15-dc1xxx user (all sensors 0, monitoring reported "Healthy")
- **Symptom:** CPU = 0°C, GPU = 0°C, loads = 0%, power = 0W for entire session. Monitoring
  health badge showed green "Healthy" despite no valid data whatsoever.
- **Root Cause:** `WmiBiosMonitor.UpdateReadings()` had an early-exit guard
  `if (!_wmiBios.IsAvailable) return;` at the top of the method, gating **all** data sources,
  not just WMI BIOS. NVAPI, PerformanceCounter, ACPI thermal zones, PawnIO MSR, and SSD/battery
  sensors were all silently blocked when WMI BIOS was non-functional.
- **Fix:**
  - Moved `!_wmiBios.IsAvailable` guard to protect only SOURCE 1 (WMI BIOS reads).
  - SOURCES 2–5 (NVAPI, PerformanceCounter, ACPI, PawnIO, SSD/battery) now execute
    unconditionally regardless of WMI BIOS availability.
  - Added **zero-temperature health degradation**: if both CPU and GPU temps remain ≤ 0°C
    for 10+ consecutive readings, monitoring health transitions to `Degraded`.
- **Files:** `OmenCoreApp/Hardware/WmiBiosMonitor.cs`, `OmenCoreApp/Services/HardwareMonitoringService.cs`

### 10. CPU/GPU Power Reading Temporary 0W Dropouts
- **Reported By:** Discord — "0W CPU/GPU intermittently"
- **Symptom:** Power telemetry briefly dropped to 0W during transient sensor read failures,
  causing jumpy power charts and false thermal protection triggers.
- **Root Cause:** Sensor reads return transient zeros during startup transitions or short
  API hiccups. No hold-last-valid logic was applied to power readings.
- **Fix:** Retain last valid power reading for short transient zero-read bursts when
  load/temperature data indicates the system is still active. Reset to real zero only after
  a sustained zero-read window.
- **Files:** `OmenCoreApp/Hardware/WmiBiosMonitor.cs`

---

## Reliability & Architecture Improvements

### CPU PerformanceCounter Singleton — Eliminates 100ms Blocking Stall Per Poll
- **Problem:** `UpdateReadings()` instantiated `new PerformanceCounter(...)`, called `NextValue()`
  twice with `Thread.Sleep(100)` between them, then disposed the instance — every 2-second poll
  cycle. This caused 100ms of blocking per cycle (5% CPU wasted on sleep), plus GC pressure
  from repeated allocation and disposal of the counter object.
- **Fix:** `_cpuPerfCounter` is now a persistent field initialised in the constructor with a
  warm-up `NextValue()` call. Each poll cycle calls `NextValue()` once with no sleep — the
  elapsed interval between calls naturally provides the correct average CPU load. If the
  counter becomes unavailable (e.g. PerfSvc restart), `_cpuPerfCounterAvailable` gates it
  safely. Counter is disposed in `Dispose()`.
- **Files:** `OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### TryRestartAsync Now Resets NVAPI Failure State
- **Problem:** `TryRestartAsync()` was a no-op that only logged a message and returned
  `Task.FromResult(true)`. `HardwareMonitoringService` calls this after consecutive timeout
  errors, believing a hardware restart occurred — but in practice nothing changed.
- **Fix:** On `TryRestartAsync()`, if NVAPI is currently suspended (`_nvapiMonitoringDisabled`),
  the method now resets `_nvapiMonitoringDisabled = false`, `_nvapiConsecutiveFailures = 0`,
  and `_nvapiDisabledUntil = DateTime.MinValue`. This gives GPU monitoring a genuinely clean
  restart after a bridge timeout event rather than waiting for the cooldown timer.
- **Files:** `OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### Dashboard Metrics Now Use Real Hardware Data
- **Problem:** `UpdateDashboardMetrics()` contained hardcoded placeholder values:
  `BatteryHealthPercentage = 100`, `BatteryCycles = 0`, `EstimatedBatteryLifeYears = 3.0`,
  `FanEfficiency = 70.0`. The battery health alert threshold (`< 70%`) could never fire.
  The fan speed chart always displayed 70% regardless of actual fan RPM.
- **Fix:**
  - `BatteryHealthPercentage` now reads from `sample.BatteryChargePercent` (clamped 0–100).
  - `FanEfficiency` is computed from `(Fan1Rpm + Fan2Rpm) / 2 / 50.0` (0–100 scale relative
    to ~5000 RPM practical max). Falls back to 0 when both fans report 0 RPM.
  - Battery health threshold warnings and fan speed charts now reflect real hardware.
- **Files:** `OmenCoreApp/Services/HardwareMonitoringService.cs`

### Tray Action Pipeline Hardening
- Added last-write-wins tray queue: stale pending actions are dropped when a newer action
  arrives. Prevents action pile-up under high click-rate conditions.
- GPU power and keyboard backlight tray actions routed through serialized queue (previously
  bypassed safe-mode protection).
- Tray worker loop now governed by `CancellationTokenSource` (`_trayWorkerCts`) — ensures
  the worker exits cleanly before main view model teardown during app shutdown.
- **Files:** `OmenCoreApp/ViewModels/MainViewModel.cs`

### Startup Safe-Mode Guard (Auto-Reset)
- Added startup safe-mode that temporarily blocks tray write actions when early monitoring
  health is degraded/stale with repeated timeouts.
- Safe mode now **auto-resets** when monitoring recovers to Healthy or when the startup
  window timer expires. Previously it was permanent for the process lifetime.
- Added configuration knobs: enable toggle, startup window duration, timeout threshold.
- Safe mode reset timer properly disposed on application shutdown.
- **Files:** `OmenCoreApp/ViewModels/MainViewModel.cs`, `OmenCoreApp/Models/FeaturePreferences.cs`

### Memory Optimizer Concurrency & Settings Persistence
- Fixed TOCTOU race between manual `CleanMemoryAsync` and scheduled auto-clean — both
  paths now share a unified lock-based concurrency guard.
- Added optional periodic auto-clean mode (Mem Reduct-style): run Smart Clean every N
  minutes (1–120, configurable via slider). Independent of threshold-based mode.
- Memory optimizer settings (interval clean toggle/interval, auto-clean toggle/threshold)
  now persist across restarts via 4 new `AppConfig` fields:
  `MemoryIntervalCleanEnabled`, `MemoryIntervalCleanMinutes`,
  `MemoryAutoCleanEnabled`, `MemoryAutoCleanThreshold`.
- **Files:** `OmenCoreApp/Models/AppConfig.cs`, `OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs`,
  `OmenCoreApp/Services/MemoryOptimizerService.cs`

### Hardware Worker Orphan Timeout Configuration
- Worker orphan timeout was hardcoded to 5 minutes, causing issues in headless/server
  scenarios where the worker should persist indefinitely.
- Added `HardwareWorkerOrphanTimeoutEnabled` and `HardwareWorkerOrphanTimeoutMinutes`
  (1–60 min) `AppConfig` fields. Timeout can be disabled entirely for headless operation.
- Timeout settings passed as command-line arguments to the worker process.
- Added "Hardware Worker" section to Settings > Monitoring tab.
- **Files:** `OmenCoreApp/Models/AppConfig.cs`, `OmenCoreApp/Hardware/HardwareWorkerClient.cs`,
  `OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`, `OmenCore.HardwareWorker/Program.cs`,
  `OmenCoreApp/Views/SettingsView.xaml`, `OmenCoreApp/ViewModels/SettingsViewModel.cs`

### Fan Control Reliability — Preset Verification & Diagnostic Guard
- Seeded last-seen RPMs at preset start to improve confirmation accuracy.
- Added multi-sample confirmation counters for large RPM deltas (prevents false-positive
  preset rollback from single-sample spikes).
- Preset application now **blocked during active fan diagnostics** — prevents diagnostic
  test runs from being interrupted or overwritten by profile auto-apply.
- Atomic preset verification + rollback when controller state doesn't match expected outcome.
- **Files:** `OmenCoreApp/Services/FanService.cs`, `OmenCoreApp/Hardware/WmiFanController.cs`,
  `OmenCoreApp/Hardware/FanControllerFactory.cs`

### Fn+Brightness False-Positive OMEN Key Detection Fix
- `OmenKeyService` now **prefers the low-level keyboard hook** over WMI OMEN events.
- WMI OMEN events are suppressed when a brightness-key sequence (Fn+F2/F3) is in progress,
  eliminating false OMEN key triggers that opened/toggled OmenCore unexpectedly.
- WMI query dynamically widens to include `eventData=8614` (firmware Fn+P profile-cycle)
  when `EnableFirmwareFnPProfileCycle` is enabled in config (previously unreachable due to
  overly strict query filtering).
- **Files:** `OmenCoreApp/Services/OmenKeyService.cs`

× References: GitHub #42, #46

### Quick Popup Idle Overhead Fix
- Update timer now paused while quick popup is hidden and resumed on show — eliminates
  wasted dispatch cycles while the popup is invisible.
- **Files:** `OmenCoreApp/Views/QuickPopupWindow.xaml.cs`
### Startup Performance — WinRing0 Check & PerformanceCounter Init (~25s Saved)
- **Problem 1:** `CapabilityDetectionService.CheckWinRing0Available()` used
  `ManagementObjectSearcher("Win32_SystemDriver")` to scan all system drivers, which blocked
  the startup thread for ~17 seconds on systems where WMI driver enumeration is slow.
- **Fix 1:** Replaced WMI scan with a direct registry lookup under
  `HKLM\SYSTEM\CurrentControlSet\Services` for keys `WinRing0_1_2_0` and `WinRing0x64`.
  Check now completes in <1 ms. Result confirmed in production log: timestamp gap reduced
  from ~17 s to <1 ms at the WinRing0 check callsite.
- **Problem 2:** `WmiBiosMonitor` initialized `PerformanceCounter` synchronously on the
  startup thread, including a warm-up `NextValue()` call, blocking for ~8–9 seconds before
  monitoring could begin.
- **Fix 2:** `PerformanceCounter` initialization moved to `Task.Run` — runs fully in the
  background. The counter is guarded by `_cpuPerfCounterAvailable` which is set only after
  successful warm-up. Startup thread is no longer blocked; first CPU load reading appears
  within 2–3 poll cycles after the background init completes.
- **Combined result:** Startup time reduced from ~39 s → ~16 s (remaining time is inherent
  WMI BIOS + NVAPI hardware enumeration, not addressable in the init path).
- **Files:** `OmenCoreApp/Hardware/CapabilityDetectionService.cs`,
  `OmenCoreApp/Hardware/WmiBiosMonitor.cs`
---

## New Features & Enhancements

### Headless Mode — Server / Background Operation
- **Motivation:** GitHub #64 — users wanted fan/performance control without a visible window
  for HTPC, server, or background-only use.
- Added `HeadlessMode` config field and `--headless` command-line flag.
- Headless mode runs all services (monitoring, fan control, power automation, OMEN key
  detection) via system tray without creating the main window.
- Headless mode toggle added to Settings > General.
- **Files:** `OmenCoreApp/Models/AppConfig.cs`, `OmenCoreApp/App.xaml.cs`,
  `OmenCoreApp/Views/SettingsView.xaml`, `OmenCoreApp/ViewModels/SettingsViewModel.cs`

### Guided Fan Diagnostics
- **Motivation:** Users needed a structured way to verify fan hardware behavior and generate
  exportable diagnostic results for support.
- New UI section runs sequential fan tests at **30% → 60% → 100%** for both CPU and GPU fans.
- Live progress bar during test; PASS/FAIL per fan per level with RPM readings and deviation
  score out of 100.
- Current fan preset is saved before the test and **restored on completion or cancellation**.
- Copy-results button for clipboard export to include in support reports or GitHub issues.
- **Files:** `OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs`,
  `OmenCoreApp/Views/FanDiagnosticsView.xaml`

### Keyboard Lighting — Native Brightness & LED Animation Effects
- **Motivation:** Users reported keeping OMEN Gaming Hub solely for brightness control and
  lighting effects (Breathing, Color Cycle, Wave). OmenCore previously only supported
  static four-zone colors.
- Added **native WMI brightness control** via BIOS command types 4 (GetBrightness) and 5
  (SetBrightness). Maps 0–100% to hardware range (0x64–0xE4). Fallback to color-scaling
  if the model doesn't support native brightness.
- Added **LED animation effects** via BIOS command type 7 (SetLedAnimation): **Breathing**,
  **ColorCycle**, and **Wave** with configurable speed and primary/secondary colors.
- `SetBacklightEnabledAsync()` now uses native `SetBacklight(bool)` instead of writing a
  black color table (which previously wiped user color settings on backlight toggle).
- Added `CMD_BRIGHTNESS_GET` (4), `CMD_BRIGHTNESS_SET` (5), `CMD_ANIMATION_SET` (7)
  constants; all commands include graceful fallbacks for older models.
- **Files:** `OmenCoreApp/Hardware/HpWmiBios.cs`,
  `OmenCoreApp/Services/KeyboardLighting/WmiBiosBackend.cs`

### V2 Keyboard Engine — PawnIO-Native Backend Wired
- **Motivation:** The V2 keyboard architecture (`KeyboardLightingServiceV2`, `EcDirectBackend`,
  `KeyboardModelDatabase`) was fully implemented but never instantiated. All operations went
  through V1 WMI-only path.
- V1 `KeyboardLightingService` now creates and probes a V2 engine internally.
  On probe success (PawnIO EC-direct or WMI BIOS backend), all calls (`ApplyProfile`,
  `SetAllZoneColors`, `SetBrightness`, `RestoreDefaults`) are delegated to V2.
  V2 probe failure falls back transparently to V1.
- `IsAvailable` and `BackendType` now reflect active backend (e.g., `V2:EcDirect`,
  `V2:WmiBios`).
- **Files:** `OmenCoreApp/Services/KeyboardLightingService.cs`

### EC Auto-Promotion for Verified Keyboard Models
- Models with fully verified EC register maps (8A14 OMEN 15 2020 Intel, 8A15 OMEN 15 2020
  AMD, 8BAD OMEN 15 2021 Intel) now auto-enable PawnIO-native keyboard writes without
  requiring the `ExperimentalEcKeyboardEnabled` config flag.
- `KeyboardLightingServiceV2.TryInitializeBackend(EcDirect)` checks for `EcColorRegisters`
  (≥12 bytes). Verified models set `PawnIOEcAccess.EnableExperimentalKeyboardWrites` at
  probe time.
- **Files:** `OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`

### Keyboard Model Database Expansion
- Added **ProductId `8BD5`** (HP Victus 16, 2023) and **`8A26`** (HP Victus 16, 2024) to
  ensure per-zone ColorTable is applied instead of the generic Victus fallback.
- Added **OMEN MAX 16 (ak0003nr)** with ThermalPolicy V2 handling and WMI-only fan control
  recommended (legacy EC writes avoided).
- **Files:** `OmenCoreApp/Hardware/KeyboardModelDatabase.cs`,
  `OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`,
  `OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`

### Diagnostics & Model Reporting
- New **"Monitoring Diagnostics"** panel in DiagnosticsView with live sensor source
  breakdown and health indicators.
- **`Report Model`** flow: creates a diagnostics ZIP (logs + sample capture + model info)
  and copies model summary to clipboard for GitHub issue submission.
- One-click **"Export Telemetry"** button exports current telemetry log segment.
- `ModelReportService` and `TelemetryService.ExportTelemetry()` added.
- **Files:** `OmenCoreApp/Views/DiagnosticsView.xaml`, `OmenCoreApp/Services/ModelReportService.cs`,
  `OmenCoreApp/Services/TelemetryService.cs`

### Monitoring Source Indicator
- Dashboard now displays active monitoring source text (e.g. "WMI BIOS + NVAPI") next to
  the health status badge — users and support can immediately see which hardware backends
  are active.
- `MonitoringSource` property exposed on `IHardwareMonitorBridge` and wired through to
  `DashboardViewModel.MonitoringSourceText`.
- **Files:** `OmenCoreApp/Hardware/HardwareMonitorBridge.cs`,
  `OmenCoreApp/ViewModels/DashboardViewModel.cs`, `OmenCoreApp/Views/DashboardView.xaml`

### Strix Point CPU Detection
- `SystemInfoService` now flags Intel 14th-gen "Strix Point" CPUs for model-specific paths.
- NVAPI service now gracefully handles missing NVAPI DLLs during initialization (was crashing
  on non-NVIDIA systems with certain driver configurations).
- **Files:** `OmenCoreApp/Services/SystemInfoService.cs`, `OmenCoreApp/Services/NvapiService.cs`

### Installer PawnIO Skip
- Installer now detects if PawnIO is already present on the target system and skips the
  embedded PawnIO sub-installer — avoids redundant installs and task-switch behavior that
  confused users upgrading from earlier versions.

---

## GUI Improvements

### Fan Curve Ghost Overlay (Preset Preview on Hover)
- Hovering any of the six preset cards (Max / Extreme / Gaming / Auto / Silent / Custom)
  now renders the preset's fan curve as a **dashed blue ghost overlay** on the curve editor
  beneath the active custom curve —  letting users compare before committing.
- Ghost renders below the active curve at α=39% with a dashed stroke (6/3 dash pattern)
  and a "preview" label at the right edge.
- `GhostCurvePoints` dependency property added to `FanCurveEditor`; `DrawGhostCurve()` runs
  before `DrawCurveLine()` so the active curve always sits on top.
- `Tag="Max/Extreme/Gaming/Auto/Silent/Custom"` attributes added to all six RadioButtons;
  `MouseEnter` / `MouseLeave` event handlers in the view's code-behind call
  `SetHoveredPreset()` / `ClearHoveredPreset()` on `FanControlViewModel`.
- **Files:** `OmenCoreApp/Controls/FanCurveEditor.xaml.cs`,
  `OmenCoreApp/ViewModels/FanControlViewModel.cs`,
  `OmenCoreApp/Views/FanControlView.xaml`, `OmenCoreApp/Views/FanControlView.xaml.cs`

### Temperature Chart Time-Range Selector
- A compact **1m / 5m / 15m / 30m** toggle strip now appears above the monitoring charts.
- `MaxThermalSampleHistory` increased from 60 → **1800** (30 minutes at 1 s polling).
- `FilteredThermalSamples` (new `ObservableCollection`) is rebuilt on every poll tick and
  on range change; charts bind to this instead of the raw sliding window.
- `IsTimeRange1m` / `IsTimeRange5m` / `IsTimeRange15m` / `IsTimeRange30m` bool helpers
  work as two-way `RadioButton.IsChecked` targets.
- `TimeRangeButton` style added to `ModernStyles.xaml` — compact pill button with
  highlighted-when-checked state.
- **Files:** `OmenCoreApp/ViewModels/DashboardViewModel.cs`,
  `OmenCoreApp/Views/DashboardView.xaml`, `OmenCoreApp/Styles/ModernStyles.xaml`

### Settings Search Bar
- A search `TextBox` in the top-right of the Settings header filters across all tabs
  instantly as you type.
- Results panel appears below the header with a **tab-badge** (coloured pill showing the
  target tab name) and a description for each match — up to 8 results from a 20-entry
  catalog covering every major setting area.
- `SettingsSearchQuery`, `SettingsSearchVisible`, `SettingsSearchResults`
  (`IEnumerable<SettingsSearchResult>`) properties on `SettingsViewModel`.
- `IconSearch` (already present in styles) wired as the search field prefix icon.
- **Files:** `OmenCoreApp/ViewModels/SettingsViewModel.cs`,
  `OmenCoreApp/Views/SettingsView.xaml`

### Profile Scheduler Tab
- New **Scheduler** tab added to Settings with a rule list for time-of-day automation.
- Each rule has: enable toggle, rule name, trigger time (HH:mm), fan preset selector,
  and performance mode selector.
- Rules are persisted to `AppConfig.ScheduleRules` (`List<ScheduleRule>`) and survive
  restarts.
- `ScheduleRule` model: `IsEnabled`, `RuleName`, `TriggerTime`, `FanPreset`,
  `PerformanceMode`, `ActiveDays` (day-of-week mask for future extension).
- Enforcement runs on a 30 s `DispatcherTimer`; fires each rule once per `HH:mm` minute
  via `_lastScheduleMinute` tracking.
- `AddScheduleRuleCommand` / `RemoveScheduleRuleCommand` with full config persistence.
- `IconSchedule` (clock) geometry added to `ModernStyles.xaml`.
- **Files:** `OmenCoreApp/Models/AppConfig.cs`,
  `OmenCoreApp/ViewModels/SettingsViewModel.cs`,
  `OmenCoreApp/Views/SettingsView.xaml`, `OmenCoreApp/Styles/ModernStyles.xaml`

### Keyboard Zone Visual Schematic
- The old four equal-width "Zone 1–4" label boxes are replaced by a **proportional laptop
  keyboard diagram** — proportional zone widths (3★ / 2.5★ / 2.5★ / 2★) reflect actual
  hardware key counts.
- Each zone shows representative key labels (e.g. "ESC F1-F4 / ~ 1 2 3 4 5 / TAB Q W E R T …"),
  a live-coloured background rectangle (opacity 18%), and a coloured zone-name label.
- A decorative function-key row and spacebar row flank the main zone grid for visual
  context; all rendered inside a dark `#0D0D0D` laptop-bezel `Border`.
- Zone hex `TextBox` inputs are retained below the schematic.
- **Files:** `OmenCoreApp/Views/LightingView.xaml`

### Onboarding Wizard (First-Run Welcome)
- A three-step modal wizard (`OnboardingWindow`) is shown **once**, before the main window,
  when `AppConfig.FirstRunCompleted` is `false`.
- **Step 1 — Welcome:** brief feature overview (fan control, lighting, performance, monitoring).
- **Step 2 — Hardware:** live detection readout of fan control backend, monitoring source,
  and PawnIO driver status.
- **Step 3 — Quick Start:** three actionable tips (apply a preset, customise lighting,
  configure Settings).
- Step-dot progress indicator; Back / Next / Get Started navigation; window drag via title
  bar; `FirstRunCompleted` set to `true` and config saved on Finish.
- **Files:** `OmenCoreApp/Views/OnboardingWindow.xaml`,
  `OmenCoreApp/Views/OnboardingWindow.xaml.cs`, `OmenCoreApp/App.xaml.cs`

---



### Zero-Temperature Warning Indicator ("—°C")
- Sidebar temperature display, GeneralView stat card badges, and Dashboard now show **"—°C"**
  in a dimmed/muted color when sensor temperature is 0°C (unavailable), instead of
  misleadingly displaying "0°C".
- `CpuTempDisplay`, `GpuTempDisplay`, `IsCpuTempAvailable`, `IsGpuTempAvailable` computed
  properties added to `DashboardViewModel`. Sidebar, stat cards, and tray tooltip all data-
  driven from these.
- GeneralView stat card headers use `DataTrigger` on temp value to switch display string
  and color badge between formatted temperature and "—°C".
- Sidebar indicators show tooltip: "CPU/GPU temperature sensor unavailable" on hover when
  dimmed.
- Tray tooltip also displays "—°C" and "GPU: —°C" for zero readings.
- **Files:** `OmenCoreApp/ViewModels/DashboardViewModel.cs`, `OmenCoreApp/Views/MainWindow.xaml`,
  `OmenCoreApp/Views/GeneralView.xaml`, `OmenCoreApp/Utils/TrayIconService.cs`

### Richer Tray Tooltip
- Fan RPM now shows separately: **"CPU Fan: X · GPU Fan: Y RPM"** instead of ambiguous "X/Y RPM".
- Added battery/AC status line: "🔋 85% · AC Power" (or "🔌 Charging" on AC).
- **Files:** `OmenCoreApp/Utils/TrayIconService.cs`

### Monitoring Health Color-Coded in Quick Popup
- Quick popup header monitoring health status text is color-coded:
  **teal** = Healthy, **amber** = Degraded, **red** = Stale, **grey** = Unknown.
- **Files:** `OmenCoreApp/Views/QuickPopupWindow.xaml`

### Stat Card Hover Effect & Profile Card Tooltips
- `StatCard` style now has a subtle hover highlight (background lightens on mouse-over)
  matching `ProfileCard` behavior.
- All four Quick Profile cards (Performance, Balanced, Quiet, Custom) have descriptive
  **hover tooltips** explaining behavior and trade-offs.
- **Files:** `OmenCoreApp/Views/GeneralView.xaml`

### Theme Color Consistency
- `GaugeLabel` style uses `{StaticResource TextMutedBrush}` instead of hardcoded `#666`.
- Power & Fan row separator `BorderBrush` uses `{StaticResource BorderBrush}` (`#2F3448`)
  instead of hardcoded `#222`.
- **Files:** `OmenCoreApp/Views/GeneralView.xaml`

### Fan Curve Editor Drag Performance Instrumentation
- Per-drag telemetry logged at drag end: frame count, drag duration, average render µs,
  and peak render µs (via `Debug.WriteLine`). Used to tune future render budget.
- **Files:** `OmenCoreApp/Controls/FanCurveEditor.xaml.cs`

### System Optimizer Tab — Visual Overhaul & Theme Consistency
- **Motivation:** Optimizer tab used emoji characters as section/button icons (⚡🔧🌐🎯🖼️💾🎮
  ⚖️↩️), a spinning `⟳` TextBlock for loading state, and hardcoded hex colors throughout
  (`#1A1D2E`, `#9CA3AF`, `#FFB800`). All rendered inconsistently with the rest of the app.
- **Loading overlay:** Spinning `⟳` TextBlock + Storyboard replaced with
  `ProgressBar IsIndeterminate` using `AccentBlueBrush` — matches loading patterns elsewhere.
- **Header:** Raw FontSize=28 TextBlocks replaced with `CardBorder` + `Headline`/`Caption`
  styles and `IconSettings` Path icon — consistent with all other tab headers.
- **Section headers (6):** All six emoji headers replaced with `StackPanel` containing a
  themed `Path` icon + `TextBlock`: Power (`IconPerformance`), Services (`IconSettings`),
  Network (`IconNetwork`), Input (`IconMouse`), Visual Effects (`IconEye`),
  Storage (`IconStorage`).
- **Preset buttons:** `🎮 Gaming Max` / `⚖️ Balanced` / `↩️ Revert All` strings replaced
  with `Path` icon + label `StackPanel`s using `IconGamepad`, `IconPerformance`,
  `IconRestore` geometries.
- **Refresh button:** `↻` string → `IconRefresh` Path + `SecondaryButton` style.
- **Footer info row:** `ℹ️` → `IconAbout` Path; `Foreground="#FFB800"` → `WarningBrush`.
- **Hardcoded colors eliminated:** `OptimizationCard` background `#1A1D2E` →
  `SurfaceDarkBrush`; description text `#9CA3AF` → `TextSecondaryBrush`;
  warning text `#FFB800` → `WarningBrush`; summary card bg `#1A1D2E` → `SurfaceDarkBrush`.
- **New icons added to ModernStyles.xaml:** `IconNetwork` (WiFi signal), `IconStorage`
  (server/HDD), `IconEye` (visibility).
- **Files:** `OmenCoreApp/Views/SystemOptimizerView.xaml`,
  `OmenCoreApp/Styles/ModernStyles.xaml`

### Bloatware Manager Tab — Risk Filter, Bulk Progress & Badge Cleanup
- **BETA badge removed:** The BETA label was removed from the tab header — feature is
  production-ready.
- **ACTIVE → INSTALLED badge fix:** Status column badge was bright green (`#4CAF50`) with
  text "ACTIVE" — green implies health/OK, confusing for items that are present and
  recommended for removal. Now uses `SurfaceLightBrush` / `SurfaceMediumBrush` with text
  "INSTALLED" / "REMOVED" for neutral, accurate labelling.
- **Search placeholder fix:** Fragile `VisualBrush` hack for the search field placeholder
  replaced with a standard `Grid` overlay `TextBlock` (hidden via `DataTrigger` when
  `FilterText` is non-empty). Accessible, theme-safe, no rendering edge cases.
- **Risk filter bar:** New compact radio-button strip (All / Low / Med / High) using the
  existing `TimeRangeButton` style, with colored indicator dots (`SuccessBrush` /
  `WarningBrush` / `AccentBrush`). `RiskFilter` property + `IsRiskAll/Low/Medium/High`
  bool helpers added to `BloatwareManagerViewModel`; `ApplyFilter()` now applies risk level
  in addition to category and text filters.
- **Bulk remove progress:** 4 px `ProgressBar` (`SuccessBrush` foreground) appears below
  the toolbar only while bulk removal is running. Bound to `IsBulkRemoving`,
  `BulkRemoveProgress`, `BulkRemoveTotal` properties on the ViewModel.
  `RemoveAllLowRiskAsync()` increments progress per item and resets on completion.
- **Files:** `OmenCoreApp/Views/BloatwareManagerView.xaml`,
  `OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`

---

## CI & Testing

- **Unit tests added:** keyboard hook + WMI filtering, fan `MonitorLoop` confirmation
  counters, diagnostics mode preset guard, power-read stabilization logic, model report
  export command (`ReportModelCommand`), memory optimizer settings persistence.
- **Integration test:** quick-profile switch stress test (added to CI) — verifies no
  transient 0 RPM spikes or single-sample RPM jumps during rapid preset cycling.
- **View-binding assertions:** model report service bindings, diagnostics view command
  wiring, stat card template trigger validation.

---

## Additional Code Quality & Polish (Post-Draft)

### Linux Daemon: DOTNET_BUNDLE_EXTRACT_BASE_DIR Missing From Systemd Service
- **Reported By:** Community report — CachyOS, kernel 7.0rc1, Transcend 14-fb0014no
- **Symptom:** `omencore-cli daemon --install` produced a service that failed to start with
  an error referencing `DOTNET_BUNDLE_EXTRACT_BASE_DIR`. The .NET single-file bundle needs
  a writable directory to extract its contents at runtime; with no env var set and
  `PrivateTmp=true` in the unit, there was no valid extraction path available to the process.
- **Root Cause:** Generated service file (both `daemon --install` and `daemon --generate-service`)
  did not define `DOTNET_BUNDLE_EXTRACT_BASE_DIR`. On systems where the process can't default-
  extract to `~/.net` (root home may be restricted, or `PrivateTmp` isolates `/tmp`), the
  runtime bails immediately with a bundle extraction error.
- **Fix:**
  - Added `Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/tmp/omencore` to the `[Service]`
    block of both the installed service and the `--generate-service` output.
  - Added `ExecStartPre=-/usr/bin/mkdir -p /var/tmp/omencore` so the target directory is
    created automatically before the process starts (the `-` prefix ignores the command if it
    fails, e.g. directory already exists).
  - Added `/var/tmp/omencore` to `ReadWritePaths` so `ProtectSystem=strict` doesn't block
    writes to the extraction directory.
  - **Runtime self-heal (v3.0.0 final):** `daemon --run` now checks whether
    `DOTNET_BUNDLE_EXTRACT_BASE_DIR` is set in the current process environment. If absent,
    it is set programmatically to `/var/tmp/omencore` and that directory is created. A yellow
    warning is printed directing the user to re-run `daemon --install` for a permanent fix.
    This prevents existing installs with old service files from crashing entirely.
- **Workaround for existing installs:** `sudo systemctl edit omencore.service` and add
  `Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/tmp/omencore` under `[Service]`, then
  `sudo systemctl daemon-reload && sudo systemctl restart omencore`.
- **Files:** `OmenCore.Linux/Commands/DaemonCommand.cs`

### Linux Daemon: Performance Mode Thermal Throttle Watchdog
- **Reported By:** Community report — CachyOS, kernel 7.0rc1, Transcend 14-fb0014no (oliver, Discord)
- **Symptom:** With `performance` set in the daemon config, the laptop ran at Performance
  thermal profile until the CPU hit ~100°C (PROCHOT / package temperature limit). The
  hp-wmi kernel module then silently reset `thermal_profile` to `balanced` to protect the
  hardware. OmenCore never noticed the reset — the configured profile was gone with no
  recovery until the daemon was restarted. The kernel logs showed throttle events
  (`CPU clock is throttled / normal`) cycling repeatedly, and the user wanted to stay on
  Performance and let fans manage the load.
- **Root Cause:** `OmenCoreDaemon` applied the configured performance mode once at startup
  via `ApplyStartupConfigAsync()` and never re-checked it. There was no mechanism to detect
  that the BIOS/kernel had silently downgraded the active thermal profile.
- **Fix:** Added `CheckAndRestorePerformanceMode(cpuTemp)` watchdog called on every main
  loop iteration (when not running a custom fan curve):
  - Detects when `cpuTemp >= thermal.throttle_temp_c` (default 95°C) and records a throttle
    event.
  - When `cpuTemp` drops back to `<= thermal.restore_temp_c` (default 80°C), re-calls
    `_ec.SetPerformanceMode()` with the configured mode and logs the restoration.
  - Opt-in via new `[thermal]` config section — disabled by default so existing users
    are unaffected.
- **New `[thermal]` config section:**
  ```toml
  [thermal]
  restore_performance_after_throttle = true   # set to true to enable
  throttle_temp_c = 95
  restore_temp_c = 80
  ```
- **Note:** This does not prevent the BIOS from throttling the CPU (that is the correct
  hardware protection behaviour). It only re-applies the chosen thermal profile after
  cooling so the fan curve / power limits return to the intended level.
- **Files:** `OmenCore.Linux/Daemon/OmenCoreDaemon.cs`, `OmenCore.Linux/Config/OmenCoreConfig.cs`

### OSD RAM/VRAM Display Correctness
- OSD RAM display no longer uses `Microsoft.VisualBasic.Devices.ComputerInfo` (a separate
  WinAPI call each time). It now reads `sample.RamUsageGb` and `sample.RamTotalGb` directly
  from the monitoring sample — consistent, zero overhead, shows "X.X / Y GB" format.
- OSD VRAM display was previously computing `GpuLoadPercent / 100 * 16` (a bogus estimate
  that assumed a 16 GB card). Fixed to use real `sample.GpuVramUsageMb / sample.GpuVramTotalMb`.
- **Files:** `OmenCoreApp/Views/OsdOverlayWindow.xaml.cs`

### RgbSceneService CS4014 Warning Fix
- Two fire-and-forget calls to `_keyboardLightingService.SetBrightness()` in
  `RgbSceneService.ApplyToOmenKeyboardAsync` were not awaited (CS4014 warnings).
  Added `await` to both — correct since the method runs inside `Task.Run(async () => {...})`.
- **Files:** `OmenCoreApp/Services/RgbSceneService.cs`

### Dead Code Removal: DiagnosticsExportService
- Removed `Services/DiagnosticsExportService.cs` (130-line legacy file with hardcoded
  `OmenCore Version: 2.6.1` string). The canonical `Services/Diagnostics/DiagnosticExportService.cs`
  (520 lines, uses `GetOmenCoreVersion()` from assembly) supersedes it entirely. Updated
  `SettingsViewModel`, `MainViewModel`, `ModelReportService`, and all test files to reference
  `DiagnosticExportService` / `CollectAndExportAsync()` instead of the deleted class.
- **Files:** `OmenCoreApp/ViewModels/SettingsViewModel.cs`, `OmenCoreApp/ViewModels/MainViewModel.cs`,
  `OmenCoreApp/Services/ModelReportService.cs`, tests

### LoggingService.LogDirectory Exposed
- Added `public string LogDirectory => _logDirectory` to `LoggingService` so consumers
  (e.g., `DiagnosticExportService`) can locate log files without duplicating the path.
- **Files:** `OmenCoreApp/Services/LoggingService.cs`

### GPU Fan Curve Config Persistence
- `FanControlViewModel.GpuFanCurve` now loads from `AppConfig.GpuFanCurve` on startup
  (previously always reset to defaults). Changes are persisted automatically via a
  `CollectionChanged` handler that calls `SaveGpuCurveToConfig()`.
- Initialization prefers saved config over defaults; if saved curve has ≥2 points it is
  loaded, otherwise defaults are used (stable first-run behavior preserved).
- **Files:** `OmenCoreApp/ViewModels/FanControlViewModel.cs`

### ZeroDoubleToPlaceholderConverter
- Added `ZeroDoubleToPlaceholderConverter` to `DashboardConverters.cs` and registered
  in `App.xaml`. Converter accepts an optional suffix parameter ("°C", "°", "W") and
  returns "—{suffix}" when bound value ≤ 0, eliminating "0°C" / "0W" display when sensors
  are unavailable.
- Applied to: `DashboardView.xaml` CPU/GPU temp badges and chart headers,
  `TuningView.xaml` CPU/GPU temp & power gauges.
- **Files:** `OmenCoreApp/Utils/DashboardConverters.cs`, `OmenCoreApp/App.xaml`,
  `OmenCoreApp/Views/DashboardView.xaml`, `OmenCoreApp/Views/TuningView.xaml`

### Summary String Zero-Sensor Guards
- `DashboardViewModel.CpuSummary`, `GpuSummary`, `StorageSummary` and matching properties
  in `MainViewModel` now show "—°C" instead of "0°C" when sensor readings are absent.
  `GpuSummary` also suppresses the VRAM segment ("• 0 MB VRAM") when `GpuVramUsageMb == 0`.
- **Files:** `OmenCoreApp/ViewModels/DashboardViewModel.cs`, `OmenCoreApp/ViewModels/MainViewModel.cs`

### QuickPopupWindow Temperature Zero Guard
- `QuickPopupWindow.UpdateDisplay` now sets `CpuTempText.Text = "—"` and
  `GpuTempText.Text = "—"` when the respective sensor value is 0 (unavailable).
- **Files:** `OmenCoreApp/Views/QuickPopupWindow.xaml.cs`

### GeneralViewModel Temperature Display Properties
- Added `CpuTempDisplay`, `GpuTempDisplay`, `IsCpuTempAvailable`, `IsGpuTempAvailable`
  to `GeneralViewModel` (matching existing `DashboardViewModel` pattern). Setters for
  `CpuTemp` and `GpuTemp` fire change notifications for these computed properties.
- **Files:** `OmenCoreApp/ViewModels/GeneralViewModel.cs`

### FanControlViewModel CPU/GPU Temp Separation
- `CurrentCpuTemperature` and `CurrentGpuTemperature` now use independent backing fields
  (`_currentCpuTemperature`, `_currentGpuTemperature`) populated from `ThermalSample`
  fields, rather than both returning `CurrentTemperature` (the CPU-derived value).
  GPU fan curve editor now receives actual GPU temperature.
- **Files:** `OmenCoreApp/ViewModels/FanControlViewModel.cs`

---

## Validation Status

### Implemented & Build-Verified
| Item | Status |
|------|--------|
| RC-1: NVAPI 60s cooldown recovery | ✅ Implemented, build clean |
| RC-2: OMEN 16-wf1xxx 8BAB model DB | ✅ Implemented, build clean |
| RC-3: RestoreAutoControl unconditional reset | ✅ Implemented, build clean |
| RC-4: Linux SetPerformanceMode hp-wmi routing | ✅ Implemented, build clean |
| Linux DOTNET_BUNDLE runtime self-heal | ✅ Implemented, build clean |
| Linux thermal throttle watchdog | ✅ Implemented, build clean |
| RC-5: SecureBoot gated on PawnIO | ✅ Implemented, build clean |
| RC-6: Degraded threshold raised to ≥3 | ✅ Implemented, build clean |
| RC-7: MonitorLoop restart on errors | ✅ Implemented, build clean |
| #8: CPU PerformanceCounter singleton | ✅ Implemented, build clean |
| #9: Linux GetPerformanceMode routing | ✅ Implemented, build clean |
| #10: TryRestartAsync NVAPI state reset | ✅ Implemented, build clean |
| #11: Dashboard real battery/fan metrics | ✅ Implemented, build clean |
| Keyboard lighting freeze fix (async) | ✅ Implemented, build clean |
| Zero-sensor detection + UI indicators | ✅ Implemented, build clean |
| Power reading 0W hold-last-valid | ✅ Implemented, build clean |
| Native keyboard brightness + effects | ✅ Implemented, build clean |
| V2 keyboard engine wired | ✅ Implemented, build clean |
| Guided fan diagnostics | ✅ Implemented, build clean |
| Memory optimizer tab + persistence | ✅ Implemented, build clean |
| Headless mode | ✅ Implemented, build clean |
| Tray worker CancellationToken | ✅ Implemented, build clean |
| Startup perf: registry WinRing0 check | ✅ Implemented, build clean |
| Startup perf: background PerformanceCounter | ✅ Implemented, build clean |
| System Optimizer visual overhaul | ✅ Implemented, build clean |
| Bloatware Manager: BETA removed, risk filter, bulk progress | ✅ Implemented, build clean |

### Needs Hardware Confirmation (Field Validation Pending)
- **OMEN 16-wf1xxx 8BAB** — `UserVerified = false`. Fan control path verified by RC analysis.
  Awaiting field report to confirm EC register behavior and promote to `UserVerified = true`.
- **LED animation effects** — byte format (zone/mode/speed/brightness/colors) validated
  against OmenHubLighter's known-good structure; may differ on older BIOS revisions.
- **Native brightness range** — 0x64–0xE4 needs confirmation on pre-2020 OMEN models.
- **EC keyboard auto-promotion** — 3 verified models (8A14, 8A15, 8BAD) need field
  confirmation that register maps match real hardware before `UserVerified` promotion.
- **Firmware Fn+P eventData=8614** — config-gated (experimental). Needs model-specific
  validation on OMEN 16 ap0xxx, xd0xxx, Victus 16-s0xxx before enabling by default.

---

## Code Quality & Reliability Improvements (v3.0.0 Continued)

### DiagnosticsView: "Open Logs Folder" Button Now Opens the Correct Directory
- **Issue:** The **Open Logs Folder** button in the Diagnostics tab was opening
  `Path.GetTempPath()` (the Windows system temp directory) instead of the actual
  OmenCore log directory.
- **Fix:** Button now resolves `App.Logging.LogDirectory` and opens it. Falls back to
  `GetTempPath()` only if the log directory does not yet exist.
- **File:** `OmenCoreApp/Views/DiagnosticsView.xaml.cs`

### AboutWindow: Removed Stale BETA Badge and Expanded Feature List
- **Issue:** A hardcoded `BETA` badge was always displayed in the About window regardless
  of the release type. The feature list was also outdated and missing major features added
  since v1.5.0.
- **Fix:**
  - Removed the static BETA badge (`Border/TextBlock` with `Text="BETA"`).
  - Updated copyright year from 2025 → 2026.
  - Updated feature list to include: Driver-Free Monitoring, V2 Keyboard Engine (Per-Key
    EC & WMI Lighting), Guided Fan Diagnostics, Memory Optimizer, Headless Mode.
- **File:** `OmenCoreApp/Views/AboutWindow.xaml`

### Removed Dead `UpdateCheckService.cs`
- **Issue:** `UpdateCheckService.cs` was a fully implemented but never-instantiated class
  with `const string CurrentVersion = "2.8.6"` hardcoded. The active update checker is
  `AutoUpdateService.cs` (reads version dynamically from the assembly). The dead file was
  a maintenance hazard.
- **Fix:** Deleted `UpdateCheckService.cs` entirely.
- **File:** `OmenCoreApp/Services/UpdateCheckService.cs` *(deleted)*

### HardwareWatchdogService: Removed Stale LibreHardwareMonitor Reference
- **Issue:** The watchdog's emergency alert message referenced "LibreHardwareMonitor
  installation" as a troubleshooting step. OmenCore dropped LHM in v2.8.6 — no user
  has LHM installed. The message also used a blocking `MessageBox.Show()` on the UI thread.
- **Fix:** Replaced the `MessageBox.Show()` call with structured log entries at `Warn`
  level with accurate troubleshooting steps (WMI BIOS availability, system stability,
  Windows updates). Non-intrusive, no UI thread blocking.
- **File:** `OmenCoreApp/Services/HardwareWatchdogService.cs`

### ThermalMonitoringService Now Active (Was Dead Code)
- **Issue:** `ThermalMonitoringService` was a fully implemented service that fires Windows
  toast notifications when CPU, GPU, or SSD temperatures exceed configurable thresholds. It
  was never instantiated anywhere — all thermal alert functionality was silently dead.
- **Fix:**
  - Instantiated in `MainViewModel` constructor immediately after `_notificationService`.
  - Thresholds are loaded from the new `AppConfig.ThermalAlerts` config section at startup,
    so user customizations persist across restarts.
  - `ProcessSample(sample)` called on every hardware monitoring update in
    `HardwareMonitoringServiceOnSampleUpdated`.
- **Default thresholds:** CPU Warning 85°C / Critical 95°C, GPU Warning 85°C / Critical 95°C,
  SSD Warning 70°C. Alert cooldown: 5 minutes (prevents notification spam).
- **Files:** `OmenCoreApp/ViewModels/MainViewModel.cs`, `OmenCoreApp/Models/AppConfig.cs`

### HardwareWatchdogService Now Active (Was Dead Code)
- **Issue:** `HardwareWatchdogService` monitors for frozen temperature sensors. If no
  temperature update is received for ≥60 seconds, it sets fans to 100% as an emergency
  safety measure. This freeze-detection safety net was never instantiated — silent failure.
- **Fix:**
  - Instantiated in `MainViewModel` constructor immediately after `_fanService`.
  - `UpdateTemperature(cpuTemp, gpuTemp)` called on every monitoring sample in
    `HardwareMonitoringServiceOnSampleUpdated` alongside the thermal service.
  - `_watchdogService.Start()` called after `_hardwareMonitoringService.Start()`.
  - `_watchdogService.Dispose()` called in `MainViewModel.Dispose()`.
- **File:** `OmenCoreApp/ViewModels/MainViewModel.cs`

### New Config Section: `ThermalAlerts` in `AppConfig`
- Added `ThermalMonitoringSettings` class with `CpuWarningC`, `CpuCriticalC`, `GpuWarningC`,
  `GpuCriticalC`, `SsdWarningC`, and `IsEnabled` properties.
- Added `ThermalAlerts` field to `AppConfig` with safe defaults (all thresholds enabled).
- Backwards compatible — existing configs without the section use the defaults.
- **File:** `OmenCoreApp/Models/AppConfig.cs`

### WinRing0 Hardening: Legacy Driver Probing/Fallback Now Disabled by Default
- **Issue:** Defender/anti-cheat false positives were still being triggered on some systems
  because OmenCore still attempted legacy WinRing0 probing/fallback in startup/status paths.
- **Fix:**
  - `EcAccessFactory` now treats WinRing0 fallback as explicit opt-in only via
    environment variable `OMENCORE_ENABLE_WINRING0=1`.
  - Startup and Settings driver checks no longer open WinRing0 device handles.
  - Driver guidance now defaults to PawnIO-first messaging.
  - `AppConfig.EcDevicePath` default is now empty (no implicit WinRing0 assumption).
- **Files:** `OmenCoreApp/Hardware/EcAccessFactory.cs`, `OmenCoreApp/App.xaml.cs`,
  `OmenCoreApp/ViewModels/SettingsViewModel.cs`, `OmenCoreApp/Models/AppConfig.cs`,
  `OmenCoreApp/Services/ConfigurationService.cs`

### Battery Health Unknown Semantics (No More Fake 100%)
- **Issue:** When battery telemetry was unavailable, dashboard metrics reported battery health
  as `100%`, which incorrectly represented unknown data as healthy data.
- **Fix:**
  - Unknown battery health now uses `-1` sentinel internally.
  - Battery-health alerts are suppressed when health is unknown.
  - Battery-health chart value is normalized to `0` for unknown samples (no false healthy signal).
- **File:** `OmenCoreApp/Services/HardwareMonitoringService.cs`

### WMI Fan Auto-Restore: Added Reset Cooldown + State Gating
- **Issue:** `RestoreAutoControl()` could run a full reset sequence too aggressively,
  increasing firmware write churn during rapid profile transitions.
- **Fix:**
  - Full reset sequence now runs only when controller state indicates manual/max mode.
  - Added a short 5-second cooldown between reset attempts.
- **File:** `OmenCoreApp/Hardware/WmiFanController.cs`

### Linux Fan CLI Hardening: Per-Fan RPM Bounds + Backend Guardrails
- **Issue:** Per-fan RPM write path accepted unbounded values and allowed unsupported writes on
  hwmon/unsafe models.
- **Fix:**
  - Added explicit RPM validation (`0..5500`) and byte-range clamping (`0..55` units).
  - Blocked per-fan direct RPM writes on unsupported backends/models; users are directed to
    profile/speed abstractions instead.
- **File:** `OmenCore.Linux/Commands/FanCommand.cs`

### Diagnostic Export Reliability: Collision-Proof Paths
- **Issue:** Back-to-back diagnostic exports in the same second could collide on identical names,
  causing intermittent export/test failures.
- **Fix:**
  - Export directory names now include milliseconds + GUID.
  - ZIP output path gains a GUID suffix if a same-name archive already exists.
- **File:** `OmenCoreApp/Services/Diagnostics/DiagnosticExportService.cs`

### QA Pass: Fan RPM Confirmation Counter Carry-Over + Preset Verification Routing
*Found and fixed in QA review following hardening commits.*

- **RPM confirmation counter carry-over (production bug):**
  - **Issue:** `_fanChangeConfirmCounters` accumulated counts from an "inconsistent zero" read
    sequence (rpm = 0, duty > 0) and then immediately accepted the next large-RPM value without
    the required two fresh consecutive confirmations. Result: a brief zero-with-duty transient
    followed by a real RPM spike could bypass the anti-spike filter in one cycle instead of two.
  - **Fix:** Added `_fanChangePendingRpms[]` parallel tracking list. Counter now resets to 0
    whenever the candidate RPM changes, ensuring confirmation cycles are always tied to a single
    consistent value.
  - **File:** `OmenCoreApp/Services/FanService.cs`

- **`FanMode.Max` preset not routed through `VerifyMaxApplied` (production bug):**
  - **Issue:** `isMaxPreset` was keyed solely on the preset name containing the word "max".
    A preset such as `{ Name = "Turbo", Mode = FanMode.Max }` fell through to the RPM-delta
    verification path, which could incorrectly roll back valid max-mode activations.
  - **Fix:** `isMaxPreset` now also gates on `preset.Mode == FanMode.Max`, ensuring any
    explicitly max-mode preset uses the controller-specific `VerifyMaxApplied()` path.
  - **File:** `OmenCoreApp/Services/FanService.cs`

- **Test infrastructure hardening:**
  - `FanService.ForceFixedPollInterval()` test hook added to bypass the 1 s production minimum
    floor and adaptive 5 s slowdown — prevents timing-sensitive tests from flapping on slower CI.
  - `SequenceFanController` in tests made thread-safe with an internal lock.
  - Two `FanSmoothingTests` rewritten from fixed `Task.Delay` windows to `WaitFor(condition)`
    polling loops; robust across scheduler jitter on all machines.
  - `FanPresetVerificationTests.ReactiveController.VerifyMaxApplied` overridden to return `true`,
    matching the new `FanMode.Max` routing fix.
- **Files:** `OmenCoreApp.Tests/Services/FanSmoothingTests.cs`,
  `OmenCoreApp.Tests/Services/FanPresetVerificationTests.cs`

---

## Known Notes
- PawnIO is the primary hardware backend. WMI/OGH paths remain as fallback for unsupported models.
- On some systems, telemetry takes 2–3 polling cycles after launch to converge to stable values.
- LED animation effects (Breathing, ColorCycle, Wave) require WMI BIOS firmware support.
  If the model doesn't support command type 7, static-only behavior is preserved silently.
- Linux build targets `net8.0` with `linux-x64` runtime. Windows build targets WPF on `net8.0-windows`.
- Periodic memory clean interval snaps to 5-minute increments (1, 5, 10 ... 120) for slider UX.
- `EstimatedBatteryLifeYears` remains at 3.0 static estimate — `Win32_Battery` does not
  expose battery cycle count. Expandable via HP WMI in a future revision.
- Linux thermal watchdog (`restore_performance_after_throttle`) is opt-in and disabled by
  default. It re-applies the performance mode after cooldown — it does not suppress CPU
  package temperature protection, which remains a hardware/kernel responsibility.

---

## Downloads

| File | Size |
|------|------|
| `OmenCoreSetup-3.0.0.exe` | Windows installer (recommended) |
| `OmenCore-3.0.0-win-x64.zip` | Windows portable |
| `OmenCore-3.0.0-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

## SHA256 Checksums

```
C3C6DD6F9A4E8001114B7AE0603FFD0B04330297EBAA86176387FF3BE7044BEA  OmenCoreSetup-3.0.0.exe
DFC7A1D3EB12C35492B1BAA56E156D43A22BF37EF53CCDDC0BC9CCCDFBC01E0D  OmenCore-3.0.0-win-x64.zip
605335229F5C403D915E99184CC20C1A047EB709B6F33817464DF88DAA5858D4  OmenCore-3.0.0-linux-x64.zip
```
