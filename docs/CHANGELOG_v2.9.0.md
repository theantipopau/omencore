# OmenCore v2.9.0 — Stability & Telemetry Recovery Patch

**Release Date:** 2026-02-14
**Type:** Stability + Bug Fix
**Reported By:** Discord community (OMEN 16 ap0xxx / xd0xxx, Victus 16-s0xxx), GitHub issue reports

---

## Bug Fixes

### 1. App Freeze/Unresponsive After Tray Quick Profile or Fan Mode Changes
- **Reported By:** Discord (`Ethernet`, others)
- **Symptom:** Changing fan mode or quick profile from tray could freeze the app UI until restart/reboot.
- **Root Cause:** Tray actions were allowed to overlap, and hardware calls (fan/performance apply) could execute on the UI path under load.
- **Fix:**
	- Added **last-write-wins tray queue** (latest tray action replaces stale pending actions during contention).
	- Moved tray-triggered hardware apply operations off UI thread.
	- Added queue diagnostics and guarded execution behavior under high click-rate conditions.
- **Files:** `OmenCoreApp/ViewModels/MainViewModel.cs`

### 2. Fn+Brightness Keys Triggering OmenCore Action
- **Reported By:** Discord + GitHub
- **References:** GitHub **#42**, **#46**
- **Symptom:** Brightness keys (and some Fn+Fx combinations) could be interpreted as OMEN key events, opening/toggling OmenCore.
- **Root Cause:** On some HP firmware paths, overlapping VK/scan/WMI event patterns caused false-positive OMEN key detection.
- **Fix:**
	- Added stronger F-key/brightness exclusion logic.
	- Added fail-closed WMI event filtering when event metadata is missing/invalid.
	- Added brightness guard window to suppress nearby ambiguous WMI events.
	- Added **experimental firmware Fn+P profile-cycle path** (config-gated, off by default due model/BIOS variance).
- **Files:** `OmenCoreApp/Services/OmenKeyService.cs`

### 3. Temperature Freeze / Stale Sensor Recovery Improvements
- **Reported By:** Discord (`stuck temp`, `temp briefly updates then freezes again`)
- **Symptom:** CPU/GPU temperature could appear stuck for long periods, then briefly recover.
- **Root Cause:** Freeze detection could over-trigger on stable/idle conditions, and fallback confirmation logic was too strict in some paths.
- **Fix:**
	- Kept idle-aware freeze thresholds for low GPU load scenarios.
	- Improved WMI fallback confirmation behavior for near-equal readings.
	- Preserved recovery path while reducing false freeze churn.
- **Files:** `OmenCoreApp/Services/HardwareMonitoringService.cs`

### 4. CPU/GPU Power Reading Temporary 0W Dropouts
- **Reported By:** Discord (`0W CPU/GPU` intermittently)
- **Symptom:** Power telemetry could momentarily drop to `0W` during transient read failures.
- **Root Cause:** Sensor reads can return transient zeros during startup transitions or short API hiccups.
- **Fix:**
	- Retain last valid power reading for short transient zero-read bursts when load/temperature indicate active system state.
	- Reset to real zero only after sustained zero-read windows.
- **Files:** `OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### 5. Fan Curve Editor Drag Lag / Jitter Under Heavy Updates
- **Reported By:** Discord (`curve dots laggy while dragging`)
- **Symptom:** Dragging curve points felt laggy and visually jittery on some systems.
- **Root Cause:** High-frequency per-pixel drag updates caused excessive render/property churn.
- **Fix:**
	- Increased drag render throttling interval for smoother frame pacing under load.
	- Quantized drag updates to reduce redundant point/property updates.
	- Added final validation/render pass on drag release.
- **Files:** `OmenCoreApp/Controls/FanCurveEditor.xaml.cs`

### 6. Monitoring Health Visibility in Quick Popup / Tray
- **Reported By:** Community telemetry debugging requests
- **Symptom:** Users/support could not quickly tell whether monitoring was healthy/degraded/stale from tray UI.
- **Fix:**
	- Added monitoring health state line in tray tooltip/context menu.
	- Added monitoring health badge in quick popup header.
	- Wired live health status updates from monitoring service to tray UI.
- **Files:** `OmenCoreApp/Utils/TrayIconService.cs`, `OmenCoreApp/Views/QuickPopupWindow.xaml`, `OmenCoreApp/Views/QuickPopupWindow.xaml.cs`, `App.xaml.cs`

### 7. Startup Safe Mode Guard for Early Instability
- **Reported By:** Follow-up from freeze/lockup reports
- **Symptom:** Early-start telemetry instability could still lead to risky write actions while sensors/backends were settling.
- **Fix:**
	- Added startup safe-mode guardrails that can temporarily block tray write actions when early monitoring health is degraded/stale with repeated timeouts.
	- Added configuration knobs for guard enable/window/timeout threshold.
- **Files:** `OmenCoreApp/ViewModels/MainViewModel.cs`, `OmenCoreApp/Models/FeaturePreferences.cs`

### 8. All Sensors Read 0°C / 0% on Models Where WMI BIOS Is Unavailable
- **Reported By:** Discord user with OMEN 15-dc1xxx
- **Symptom:** CPU = 0°C, GPU = 0°C, all loads = 0% for the entire session. Monitoring reports "Healthy" despite no useful data.
- **Root Cause:** `WmiBiosMonitor.UpdateReadings()` had an early-exit guard `if (_disposed || !_wmiBios.IsAvailable) return;` at the top of the method, which gated **all** data sources — not just WMI BIOS. On models where WMI BIOS is non-functional (e.g., OMEN 15-dc1xxx), this guard blocked NVAPI GPU metrics, PerformanceCounter CPU load, ACPI thermal zones, PawnIO MSR throttling, and SSD/battery sensors from ever running.
- **Fix:**
	- Moved the `!_wmiBios.IsAvailable` guard to only protect SOURCE 1 (WMI BIOS temp/fan reads). SOURCES 2–5 (NVAPI, PerformanceCounter, ACPI, PawnIO, SSD/battery) now execute unconditionally.
	- Added **zero-temperature health degradation** in `HardwareMonitoringService`: if both CPU and GPU temps remain ≤ 0°C for 10+ consecutive readings, monitoring health is set to `Degraded` instead of `Healthy`, giving users and support a clear signal that sensor data is incomplete.
- **Files:** `OmenCoreApp/Hardware/WmiBiosMonitor.cs`, `OmenCoreApp/Services/HardwareMonitoringService.cs`

---

## Reliability Enhancements

### Tray Action Pipeline Hardening
- Added clearer diagnostics around tray action queueing/execution.
- Implemented last-write-wins semantics to prevent stale action pile-ups.
- Routed GPU power and keyboard backlight tray actions through the same serialized queue for consistent safe-mode protection.

### Monitoring Stability Guardrails
- Improved classification of idle-stable temperatures vs actual frozen telemetry.
- Maintained fallback path while reducing noisy repeated warnings.

### Startup Guardrails
- Added automatic startup safe-mode activation logic when repeated early monitoring timeouts indicate unstable state.
- Added explicit configuration flags to tune startup safe-mode behavior.
- Safe mode now **auto-resets** when monitoring recovers to Healthy or when the startup window timer expires — previously it was permanent for the process lifetime.
- Safe mode reset timer is properly disposed on application shutdown.

### Settings UX Additions
- Added Advanced Settings controls for startup safe mode (enable toggle, startup window, timeout threshold).
- Added Advanced Settings toggle for experimental firmware Fn+P profile-cycle mapping.

### Memory Optimizer Enhancements
- Added optional periodic auto-clean mode (Mem Reduct-style) to run Smart Clean every N minutes (1–120, configurable via slider).
- Preserved threshold-based auto-clean mode so users can choose load-triggered, interval-triggered, or both.
- Hardened scheduled clean execution to prevent overlapping auto-clean runs via unified lock-based concurrency guard.
- Fixed TOCTOU race between manual `CleanMemoryAsync` and scheduled auto-clean that could allow concurrent `NtSetSystemInformation` calls.

### Quick Popup Improvements
- Monitoring health status text is now color-coded: teal (Healthy), amber (Degraded), red (Stale), grey (Unknown).
- Quick popup update timer now stops when the popup is hidden and resumes on show — eliminates wasted dispatches while invisible.

### OmenKey Service Improvements
- Fixed experimental firmware Fn+P (eventData=8614) being unreachable due to overly strict WMI query filtering.
- WMI query now dynamically widens to include eventData=8614 when `EnableFirmwareFnPProfileCycle` is enabled in config.

---

## QoL & Visual Polish

### Zero-Temperature Warning Indicator
- Sidebar temp display and GeneralView stat card badges now show **"—°C"** in a dimmed/muted color when sensor temperature is 0°C (unavailable), instead of misleadingly displaying "0°C".
- Added `CpuTempDisplay`, `GpuTempDisplay`, `IsCpuTempAvailable`, `IsGpuTempAvailable` properties to `DashboardViewModel` for data-driven UI.
- Sidebar indicators show a tooltip ("CPU/GPU temperature sensor unavailable") on hover when dimmed.
- Tray tooltip and context menu also display "—°C" for zero readings.
- **Files:** `OmenCoreApp/ViewModels/DashboardViewModel.cs`, `OmenCoreApp/Views/MainWindow.xaml`, `OmenCoreApp/Views/GeneralView.xaml`, `OmenCoreApp/Utils/TrayIconService.cs`

### Richer Tray Tooltip
- Fan RPM display now labels fans separately as **"CPU Fan: X · GPU Fan: Y RPM"** instead of ambiguous "X/Y RPM".
- Added **battery/AC power status line** to the tray tooltip (e.g., "🔋 85% · AC Power").
- **Files:** `OmenCoreApp/Utils/TrayIconService.cs`

### Stat Card Hover Effect
- The **StatCard** style in GeneralView now has a subtle hover highlight (matching ProfileCard behavior) — the card background lightens on mouse-over for visual feedback.
- **Files:** `OmenCoreApp/Views/GeneralView.xaml`

### Theme Color Consistency
- **GaugeLabel** style now uses `{StaticResource TextMutedBrush}` instead of hardcoded `#666`, matching the global theme.
- All **Power & Fan row** separator `BorderBrush` values changed from hardcoded `#222` to `{StaticResource BorderBrush}` (`#2F3448`) for theme consistency.
- **Files:** `OmenCoreApp/Views/GeneralView.xaml`

### Profile Card Tooltips
- All four Quick Profile cards (Performance, Balanced, Quiet, Custom) now display descriptive tooltips on hover explaining their behavior and trade-offs.
- **Files:** `OmenCoreApp/Views/GeneralView.xaml`

### Keyboard Lighting: Native Brightness & LED Animation Effects
- **Motivation:** Users reported needing to keep OMEN Gaming Hub installed solely for keyboard lighting brightness control and effects (breathing, color cycle, wave). OmenCore previously only supported static four-zone colors.
- **Reference:** WMI API structure validated against [`OmenHubLighter`](https://github.com/Joery-M/OmenHubLighter) and upstream [`OmenHubLight`](https://github.com/determ1ne/OmenHubLight).
- **Changes:**
  - Added **native WMI brightness control** via BIOS command types 4 (GetBrightness) and 5 (SetBrightness). Maps 0–100% user input to the hardware brightness range (0x64–0xE4). Falls back to color-scaling approach if the native command is unsupported on a given model.
  - Added **LED animation effects** via BIOS command type 7 (SetLedAnimation). Supports **Breathing**, **ColorCycle**, and **Wave** effects with configurable speed and primary/secondary colors.
  - Added `GetBrightness()`, `SetBrightnessLevel()`, `GetLedAnimation()`, `SetLedAnimation()` methods to `HpWmiBios`.
  - `SetBacklightEnabledAsync()` now uses proper WMI `SetBacklight(bool)` instead of writing a black color table (which lost user colors).
  - Added `CMD_BRIGHTNESS_GET` (type 4) and `CMD_ANIMATION_SET` (type 7) constants; clarified `CMD_HAS_BACKLIGHT` (type 6) is also `GetLedAnimation` (dual-purpose command).
  - All new commands include graceful fallbacks — hardware that doesn't support native brightness or LED animations will degrade to the previous color-scaling / static-only behavior without errors.
- **Files:** `OmenCoreApp/Hardware/HpWmiBios.cs`, `OmenCoreApp/Services/KeyboardLighting/WmiBiosBackend.cs`

### Discord Community Link Updated
- All Discord invite links across documentation updated to the new permanent link: `https://discord.gg/9WhJdabGk8`.
- **Files:** `README.md`, `INSTALL.md`, `docs/CHANGELOG_v1.3.0-beta.md`, `docs/DISCORD_ANNOUNCEMENT_v2.7.0.md`, `docs/LINUX_INSTALL_GUIDE.md`, `docs/REDDIT_POST_v2.7.0_omencore.md`, `docs/REDDIT_POST_v2.7.0_hpomen.md`, `docs/REDDIT_POST_v2.8.6_omencore.md`, `docs/ROADMAP_v1.4.md`

---

## PawnIO-Only Keyboard Lighting (V2 Engine)

### V2 Keyboard Engine Wired as Internal Backend
- **Motivation:** The V2 keyboard architecture (`KeyboardLightingServiceV2`, `EcDirectBackend`, `KeyboardModelDatabase`) was fully implemented but never instantiated — all keyboard operations still went through the V1 WMI-only path.
- **Change:** V1 `KeyboardLightingService` now creates and probes a V2 engine internally. On systems where V2 successfully initializes (PawnIO EC-direct or WMI BIOS backend), all calls (`ApplyProfile`, `SetAllZoneColors`, `SetBrightness`, `RestoreDefaults`) are delegated to V2. If V2 probe fails, V1 falls back transparently to its existing WMI path.
- `IsAvailable` and `BackendType` properties reflect the active backend (e.g., `V2:EcDirect` or `V2:WmiBios`).
- **Files:** `OmenCoreApp/Services/KeyboardLightingService.cs`

### EC Auto-Promotion for Verified Models
- **Motivation:** Three OMEN models (8A14, 8A15, 8BAD) have fully verified EC register maps for keyboard color/brightness/effect control. Previously, PawnIO-native keyboard writes required the user to manually enable `ExperimentalEcKeyboardEnabled` in config.
- **Change:** `KeyboardLightingServiceV2.TryInitializeBackend(EcDirect)` now checks if the detected model has verified `EcColorRegisters` (≥12 bytes). For verified models, EC-direct keyboard writes are auto-enabled without the experimental flag — `PawnIOEcAccess.EnableExperimentalKeyboardWrites` is set at probe time.
- Unverified models still require the experimental config flag.
- **Files:** `OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`

---

## Reliability Enhancements (continued)

### Tray Worker Cancellation Token
- **Motivation:** `ProcessTrayActionQueueAsync` ran an infinite `while(true)` loop that could outlive the application's dispose sequence, potentially executing hardware writes after the main view model was torn down.
- **Change:** Added `CancellationTokenSource` (`_trayWorkerCts`) to `MainViewModel`. The tray worker loop checks `_trayWorkerCts.Token` on each iteration and during queue waits. `Dispose()` cancels the token before other cleanup, ensuring the tray worker exits cleanly.
- **Files:** `OmenCoreApp/ViewModels/MainViewModel.cs`

### Memory Auto-Clean Settings Persistence
- **Motivation:** Memory optimizer settings (interval clean toggle/interval, auto-clean toggle/threshold) were lost on every restart — users had to reconfigure each session.
- **Change:**
  - Added 4 new `AppConfig` fields: `MemoryIntervalCleanEnabled`, `MemoryIntervalCleanMinutes`, `MemoryAutoCleanEnabled`, `MemoryAutoCleanThreshold` (with sensible defaults).
  - `MemoryOptimizerViewModel` now accepts an optional `ConfigurationService`, calls `RestorePersistedSettings()` on init (applies saved values to the service), and calls `PersistSettings()` whenever any of the 4 settings change.
- **Files:** `OmenCoreApp/Models/AppConfig.cs`, `OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs`

### Fan Curve Drag Performance Instrumentation
- **Motivation:** Follow-up from v2.9.0 drag-lag fix — quantified render budget needed for future optimization.
- **Change:** Added per-drag instrumentation to `FanCurveEditor`: frame counter, per-frame `Stopwatch`-timed render microseconds (total + max), and drag start timestamp. On `ReleaseDrag()`, a summary is logged via `Debug.WriteLine` with total frames, drag duration, average render µs, and peak render µs.
- **Files:** `OmenCoreApp/Controls/FanCurveEditor.xaml.cs`

### Hardware Worker Orphan Timeout Configuration
- **Motivation:** Hardware worker orphan timeout was hardcoded to 5 minutes, causing issues for headless/server scenarios where the worker should persist indefinitely or have a different timeout.
- **Change:**
  - Added 2 new `AppConfig` fields: `HardwareWorkerOrphanTimeoutEnabled` (default: true), `HardwareWorkerOrphanTimeoutMinutes` (default: 5, range: 1-60).
  - Modified `HardwareWorkerClient` constructor to accept orphan timeout settings and pass them as command-line arguments to the worker process.
  - Updated `LibreHardwareMonitorImpl` constructor to accept and forward orphan timeout settings to the worker client.
  - Worker process now parses orphan timeout settings from command-line args and uses them in the `OrphanWatchdog` task.
  - Orphan timeout can be disabled entirely for headless scenarios where the worker should never exit.
- **Files:** `OmenCoreApp/Models/AppConfig.cs`, `OmenCoreApp/Hardware/HardwareWorkerClient.cs`, `OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`, `OmenCore.HardwareWorker/Program.cs`

### Hardware Worker Settings UI
- **Motivation:** Worker orphan timeout settings were only configurable in config file, not in the UI.
- **Change:** Added "Hardware Worker" section to Settings > Monitoring tab with toggles and slider for orphan timeout configuration.
- **Files:** `OmenCoreApp/Views/SettingsView.xaml`, `OmenCoreApp/ViewModels/SettingsViewModel.cs`

### Headless Mode Support
- **Motivation:** Users requested headless operation for servers or when only fan/performance control is needed (addresses GitHub issue #64).
- **Change:**
  - Added `HeadlessMode` config field (default: false) to run without main window, only tray icon and background services.
  - Added `--headless` command-line flag for headless startup.
  - Modified `App.xaml.cs` to conditionally create main window based on headless mode.
  - Added headless mode toggle in Settings > General.
  - When headless, all services (monitoring, fan control, etc.) remain active via system tray.
- **Files:** `OmenCoreApp/Models/AppConfig.cs`, `OmenCoreApp/App.xaml.cs`, `OmenCoreApp/Views/SettingsView.xaml`, `OmenCoreApp/ViewModels/SettingsViewModel.cs`

---

## Validation Status (Important)

### Addressed in code
- Tray profile/fan mode freeze path from quick menu: **addressed** via serialized async tray actions with last-write-wins queue.
- GPU power / keyboard backlight tray actions now routed through same queue: **addressed** (previously bypassed safe mode + serialization).
- Fn+F2/F3 false OMEN trigger path: **addressed** in OMEN key filtering and WMI fail-closed behavior (GitHub #42/#46).
- Experimental Fn+P firmware event path: **fixed** — WMI query now dynamically widens when config flag is enabled.
- Transient `0W` telemetry spikes: **addressed** by power reading stabilization.
- Curve drag responsiveness path: **addressed** with drag quantization + render throttling.
- Monitoring health visibility gap: **addressed** in tray + quick popup UI with color-coded health status.
- Startup safe mode lifecycle: **fixed** — safe mode now auto-resets on healthy recovery or window expiry (was permanent).
- Memory optimizer concurrency: **fixed** — eliminated TOCTOU race between manual and scheduled clean paths.
- Quick popup idle overhead: **fixed** — timer paused while hidden.
- All-zero sensors on non-WMI-BIOS models: **fixed** — UpdateReadings() early-exit guard no longer blocks NVAPI/PerformanceCounter/ACPI/PawnIO sources.
- Zero-temp health detection: **added** — monitoring auto-degrades after 10 consecutive 0°C readings from both CPU and GPU.
- Zero-temp UI indicator: **added** — sidebar/stat cards show "—°C" with dimmed color and tooltip when sensor data is unavailable.
- Tray tooltip enrichment: **added** — separate CPU/GPU fan labels, battery/AC status line, zero-temp "—°C" display.
- Visual polish: **added** — stat card hover, theme-consistent colors, profile tooltips.
- Native keyboard brightness: **added** — WMI BIOS command types 4/5 with fallback to color scaling.
- LED animation effects: **added** — Breathing, ColorCycle, Wave via WMI command type 7. Static/Off unchanged.
- Backlight on/off: **fixed** — uses native `SetBacklight(bool)` instead of destructive black color table write.
- Discord links: **updated** — all 10 instances across 9 files point to new permanent invite.
- V2 keyboard engine: **wired** — V1 service auto-probes V2 backends (EcDirect → WmiBios → V1 fallback).
- EC auto-promotion: **added** — verified models (8A14, 8A15, 8BAD) use PawnIO-native keyboard writes without experimental flag.
- Tray worker shutdown: **added** — CancellationTokenSource ensures tray loop exits before dispose.
- Memory settings persistence: **added** — 4 AppConfig fields + MemoryOptimizerViewModel restore/persist lifecycle.
- Fan drag instrumentation: **added** — per-drag frame count, avg/max render µs, duration logged at drag end.
- Hardware worker orphan timeout: **added** — configurable timeout (1-60 min) or disabled for headless scenarios.
- Hardware worker UI settings: **added** — orphan timeout controls in Settings > Monitoring.
- Headless mode: **added** — no-main-window operation with `--headless` flag and config option.
- WinRing0 known note: **fixed** — removed stale WinRing0 reference (removed in v2.7.0).

### Needs hardware confirmation (final verdict)
- Full freeze elimination on all affected OMEN/Victus models still requires field validation on real devices.
- Sensor behavior under hybrid GPU mode and startup transitions can vary by BIOS/driver build.
- Firmware Fn+P eventData=8614 needs validation across OMEN 16 ap0xxx, xd0xxx, and Victus 16-s0xxx models before promoting out of experimental.
- LED animation effect byte format (zone/mode/speed/brightness/colors) needs validation across keyboard models — tested against OmenHubLighter's known-good structure but may differ on some BIOS revisions.
- Native brightness range (0x64–0xE4) needs confirmation on older OMEN models (pre-2020).
- EC-direct keyboard lighting auto-promotion needs field validation on the 3 verified models (8A14 OMEN 15 2020 Intel, 8A15 OMEN 15 2020 AMD, 8BAD OMEN 15 2021 Intel) to confirm register maps match real hardware.

---

## Known Notes
- PawnIO is the primary hardware backend; WMI/OGH paths remain as fallback for unsupported models.
- On some systems, telemetry can take several polling cycles after startup to converge to stable values.
- Periodic memory clean interval snaps to 5-minute increments (1, 5, 10, 15 ... 120) for comfortable slider UX.

---

## Suggested Follow-up for v2.9.x
- ~~Add targeted instrumentation for fan-curve drag latency (frame time/update budget) and log export hooks.~~ **Done** — drag frame count, avg/max render µs logged at drag end.
- Add model-specific validation list for firmware Fn+P event codes before enabling by default.
- ~~Persist periodic memory auto-clean settings across restarts via app config.~~ **Done** — 4 AppConfig fields + restore/persist logic in MemoryOptimizerViewModel.
- ~~Add tray worker cancellation token for cleaner shutdown during pending action execution.~~ **Done** — CancellationTokenSource in MainViewModel.Dispose().
- ~~Make hardware worker orphan timeout configurable for headless scenarios.~~ **Done** — 2 AppConfig fields + command-line args to worker + UI controls.
- ~~Add headless mode for server/headless operation.~~ **Done** — HeadlessMode config + --headless flag + conditional window creation.

---

## Downloads

| File | Size |
|------|------|
| `OmenCore-2.9.0-win-x64.zip` | Windows portable |
| `OmenCore-2.9.0-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

## SHA256 Checksums

```
37265CEF301576D6492E153BE413B1B686DF9162A01A07F8D53F15F0EB0E1B48  OmenCore-2.9.0-win-x64.zip
EB59465DEC2F28EE2E11D686D0FDCECCA6BF89A9FF7D3125B6EE6E5E531588C7  OmenCore-2.9.0-linux-x64.zip

# Individual Executables
9C1FC2CAEA6447D07D444BF9CD2B66750D63F2449DD26C94630CF4C4B1C476FF  OmenCore.exe (Windows)
4AB8E267A9FB104FB2FDC9F9079CA0AD91D7D52547CF4CB612177FD11E219ACC  OmenCore.HardwareWorker.exe (Windows)
FB02F397C12187ABBE25CFFED170DA38B33AB0B72FABC2A5BE5F02C87B619847  omencore-cli (Linux)
D683674E44D63BB9FBEFADEAE586D4BB66D5814237013EABBAF81EA524E0F21E  omencore-gui (Linux)
A0179E69132FE5992AE7DB791CE0AAB9654671BB8472FFA8AC875AD9788A8F07  OmenCore.HardwareWorker (Linux)
```
