# OmenCore v3.0.0 — Architecture Overhaul & Stability Release

Hey r/OmenCore!

v3.0.0 is here — the biggest release since v2.0.0. The entire hardware monitoring pipeline was rebuilt around a self-sustaining driver-free architecture, seven community-reported regressions are fully resolved, and a comprehensive set of new features and GUI improvements ship across every tab.

**Download:** https://github.com/theantipopau/omencore/releases/tag/v3.0.0

---

## ✨ New Features

### Architecture Overhaul — Driver-Free Self-Sustaining Monitoring
- **Complete independence** from LibreHardwareMonitor, WinRing0, and NVML
- Monitoring now uses WMI BIOS + NVAPI natively for all temperature and power sensors
- No kernel drivers required; eliminates Defender false positives and anti-cheat conflicts
- On systems where WMI BIOS is unavailable, fallback sources (NVAPI, PerformanceCounter, ACPI thermal zones, PawnIO MSR, SSD/battery sensors) work independently
- **Result:** More stable telemetry, zero silent failures, better antivirus compatibility

### Guided Fan Diagnostics
- New diagnostic UI in the Fan Control tab
- Sequential test at **30% → 60% → 100%** for both CPU and GPU fans
- Live progress bar with PASS/FAIL results per fan per level
- RPM readings and deviation score (0-100) for each test
- Current fan preset auto-saved before test and restored on completion
- Copy-results button for quick support ticket inclusion
- **Use case:** Verify fan hardware health; generate exportable diagnostics for support

### Memory Optimizer Tab
- Real-time RAM usage monitoring with live gauge
- **Smart Clean:** Trims processes + purges standby memory
- **Deep Clean:** Full system memory optimization
- **Auto-clean mode:** Configurable thresholds and periodic intervals (1-120 minutes)
- Settings persist across restarts via AppConfig
- **Result:** Declutter RAM without manual intervention; optional automation

### Keyboard Lighting — Native Brightness & Animation Effects
- **Native WMI brightness control:** 0–100% slider maps directly to hardware (0x64–0xE4 range)
- **LED animation effects:** Breathing, ColorCycle, and Wave with configurable speed
- Primary/secondary color selection for animations
- Graceful fallback to color-scaling or static-only for older models
- **V2 Keyboard Engine** now wired as primary backend: uses PawnIO EC-direct for verified models, falls back to WMI BIOS, then V1
- **Auto-promotion:** 8A14, 8A15, 8BAD models with verified EC maps auto-enable EC writes without config flags
- **Result:** Replace OMEN Gaming Hub entirely; native brightness control without external tools

### Headless Mode
- `--headless` flag starts OmenCore without displaying the main window
- All monitoring, fan control, power automation, and OMEN key detection run via system tray
- Perfect for HTPC, server, or background operation
- Toggle in Settings > General for persistent configuration
- **Use case:** Run on startup without UI clutter; integrate with home automation

### Diagnostics & Model Reporting
- New **"Monitoring Diagnostics"** panel in DiagnosticsView with live sensor source breakdown
- **"Report Model"** flow generates a ZIP with logs, telemetry sample, and model info + copies summary to clipboard
- **"Export Telemetry"** button exports current telemetry log segment for GitHub issue submission
- `ModelReportService` and expanded `TelemetryService`
- **Result:** One-click diagnostics collection for support threads; no manual log hunting

### Temperature Charts — Multi-Range Time Selector
- Charts now support **1m / 5m / 15m / 30m** time-range toggle above the graph
- Max thermal sample history increased from 60 → 1800 samples (30 minutes at 1s polling)
- Filtered sample collection rebuilt on every poll and range change
- `TimeRangeButton` style for compact pill-button look
- **Result:** Better historical perspective on temperature stability

### Settings Search Bar
- Searchable text input in the Settings header
- Instant filtering across all tabs with formatted results (up to 8 matches)
- Tab badges show which setting is in which section
- 20-entry catalog covering every major setting area
- **Result:** Find settings 10x faster; no more hunting through tabs

### Profile Scheduler
- New **Scheduler** tab under Settings
- Create time-of-day rules to auto-switch fan presets and performance modes
- Rule includes: toggle, name, trigger time (HH:mm), fan preset, performance mode, day-of-week mask (extensible)
- Rules persist in `AppConfig.ScheduleRules` and survive restarts
- Enforcement checks every 30 seconds; fires each rule once per HH:mm window
- **Result:** Automatic profiles during work hours, quiet mode at night, etc.

### Keyboard Zone Visual Schematic
- Old four equal-width "Zone 1–4" boxes replaced with **proportional laptop keyboard diagram**
- Zone widths (3★ / 2.5★ / 2.5★ / 2★) reflect real hardware key counts
- Each zone shows representative key labels (e.g. "ESC F1-F4 / ~ 1 2 3 4 5 / TAB Q W E R T")
- Live zone colors with opacity overlay for visual feedback
- Decorative function-key and spacebar rows for context
- **Result:** Clear, visually accurate keyboard layout reference

### Onboarding Wizard
- Three-step welcome modal shown once on first launch
- **Step 1 — Welcome:** Feature overview (fan control, lighting, performance, monitoring)
- **Step 2 — Hardware:** Live detection readout of fan controller, monitoring source, and PawnIO status
- **Step 3 — Quick Start:** Three actionable tips (apply preset, customize lighting, configure Settings)
- Step-dot progress indicator, Back/Next/Get Started navigation
- `FirstRunCompleted` flag set on finish; config saved automatically
- **Result:** Smooth onboarding for new users

---

## 🐛 Critical Bug Fixes (7 Regressions + 3 Reliability Improvements)

### GPU Telemetry Permanently Lost After NVAPI Error (RC-1)
- **Before:** NVAPI error → `_nvapiMonitoringDisabled = true` permanently. Only full app restart recovered (no recovery path).
- **After:** On hitting max consecutive failures, NVAPI suspended for 60 seconds with timed `_nvapiDisabledUntil` timestamp. Auto-recovery checks every poll; failure counters reset on recovery.
- **User impact:** Brief NVAPI glitches no longer kill GPU monitoring for the entire session.

### OMEN 16-wf1xxx (8BAB) Fan Control Non-Functional (RC-2)
- **Before:** ProductId `8BAB` missing from ModelCapabilityDatabase. Fallback to Transcend template with `SupportsFanControlWmi = false`, silently disabling all WMI fan control.
- **After:** Dedicated `8BAB` entry added with `SupportsFanControlWmi = true`, `MaxFanLevel = 100`, proper thermal policy support.
- **User impact:** Fan control slider now works on OMEN 16-wf1xxx 2024 Intel models.

### Fan Auto Mode Shows 0 RPM After Profile Switch (RC-3)
- **Before:** `RestoreAutoControl()` guarded by `if (_isMaxModeActive || IsManualControlActive)`, skipping reset from standard presets. RPM debounce window (3 sec) remained active with no reset.
- **After:** Guard removed. `ResetFromMaxMode()` and debounce clear called unconditionally on every `RestoreAutoControl()` call.
- **User impact:** Profile switches to Auto mode now show correct RPM immediately (no 0 RPM ghost).

### Linux CLI Performance Mode Silently Fails on hp-wmi-Only Systems (RC-4)
- **Before:** `SetPerformanceMode()` attempted EC write only, which failed silently on hp-wmi-only boards (no ec_sys kernel module). Mode appeared to change in UI but hardware stayed on default.
- **After:** Full priority routing: hp-wmi `thermal_profile` → ACPI `platform_profile` → EC register direct write. Each path tried in order; first success returned.
- **User impact:** Performance mode now works on CachyOS, Fedora, and other distros without ec_sys.

### Secure Boot Warning Shown Alongside Green PawnIO Badge (RC-5)
- **Before:** Secure Boot warning displayed independently of PawnIO status, confusing users with an unnecessary alert.
- **After:** Warning now suppressed when PawnIO is available: `SecureBootEnabled = rawSecureBoot && !PawnIOAvailable`.
- **User impact:** Clean Settings > Monitoring tab with no confusing contradictions.

### Clean Install Shows "Standalone = Degraded" (RC-6)
- **Before:** Missing OGH + HP System Event Utility (both absent by design on clean installs) = degraded status immediately.
- **After:** Degraded threshold raised from ≥2 to ≥3 missing optional components. LibreHardwareMonitor marked as `IsOptional = false`.
- **User impact:** First launch on clean systems now shows green "Healthy" instead of misleading amber "Degraded."

### Monitor Loop Exits Permanently on 5 Consecutive Errors (RC-7)
- **Before:** MonitorLoopAsync hit `maxErrors` → `break` statement → permanent exit. Telemetry frozen, no recovery.
- **After:** On hitting `maxErrors`, loop resets error counter and continues after 10-second backoff. Only exits on `OperationCanceledException` (normal shutdown).
- **User impact:** Brief transient errors (WMI service restart, driver reset) no longer kill telemetry for the entire session.

### All Sensors Read 0°C on Models Where WMI BIOS Is Unavailable
- **Before:** Early-exit guard `if (!_wmiBios.IsAvailable) return;` blocked **all** telemetry sources when WMI BIOS unavailable.
- **After:** Guard moved to protect only WMI BIOS source. NVAPI, PerformanceCounter, ACPI thermal zones, PawnIO MSR, and SSD/battery sources execute independently.
- **User impact:** Systems without WMI BIOS now get CPU load, GPU power, SSD temps, and battery health from fallback sources.

### Startup Performance — WinRing0 Check & PerformanceCounter Init
- **Problem:** `CheckWinRing0Available()` used WMI driver scan (~17s) + synchronous PerformanceCounter init (~8s) on startup thread.
- **Fix:** Registry lookup for WinRing0 (<1ms) + background Task.Run for PerformanceCounter init.
- **Result:** Startup reduced from ~39 seconds → ~16 seconds (remaining time is inherent WMI BIOS + NVAPI enumeration).

---

## 🎨 GUI Improvements

### System Optimizer Tab Visual Overhaul
- **Before:** Emoji section headers (⚡🔧🌐🎯🖼️💾), spinning `⟳` TextBlock loading state, hardcoded hex colors (`#1A1D2E`, `#9CA3AF`, `#FFB800`)
- **After:**
  - Loading overlay: `ProgressBar IsIndeterminate` with `AccentBlueBrush`
  - Header: `CardBorder` + `Headline/Caption` styles + `IconSettings` path
  - Section headers: `Path` icons (Power, Services, Network, Input, Visual Effects, Storage) + themed labels
  - Preset buttons: `IconGamepad`, `IconPerformance`, `IconRestore` paths + labels
  - Footer: `IconAbout` path; warning text uses `WarningBrush`
  - All hardcoded colors: `SurfaceDarkBrush`, `TextSecondaryBrush`, `WarningBrush`
- **Result:** Consistent with rest of app; professional icon set replaces emoji clutter

### Bloatware Manager Tab Refinements
- **BETA badge removed** — feature is production-ready
- **Status badges fixed:** Bright green was misleading. Now `SurfaceLightBrush` for "INSTALLED", `SurfaceMediumBrush` for "REMOVED"
- **Search placeholder:** Fragile `VisualBrush` hack → standard `Grid` overlay `TextBlock` (accessible, theme-safe)
- **Risk level filter:** New radio-button strip (All / Low / Med / High) filters by removal risk
- **Bulk remove progress:** 4px `ProgressBar` during multi-item removal with live counter
- **Result:** Clearer risk communication; faster filtering and bulk operations

### Other UI Polish
- **Zero-temperature "—°C" indicator** — no more false 0°C displays when sensors unavailable
- **Tray tooltip enriched:** CPU/GPU fan RPM split display + battery/AC status line
- **Monitoring health color-coded:** Teal = Healthy, Amber = Degraded, Red = Stale, Grey = Unknown
- **Stat cards hover effect:** Subtle background highlight on mouse-over
- **Quick profile tooltips:** Descriptive hover text explaining each preset behavior
- **Theme consistency:** GaugeLabel, borders, separators use global brushes instead of hardcoded hex
- **Fan curve drag telemetry:** Frame count, duration, and render µs logged per drag

---

## 📱 Linux & Daemon

### Linux Systemd Service Fixes
- `DOTNET_BUNDLE_EXTRACT_BASE_DIR` environment variable now auto-configured in service files
- Directory created automatically on service start via `ExecStartPre`
- Added to `ReadWritePaths` for `ProtectSystem=strict` compatibility
- **Runtime self-heal:** If service file is missing the setting, daemon detects and applies it at startup with a yellow warning

### Thermal Throttle Watchdog (Linux)
- **New `[thermal]` config section:** `restore_performance_after_throttle = true`
- Detects CPU thermal throttle events (CPU temp ≥ 95°C)
- Re-applies configured performance mode when temp drops to ≤ 80°C
- Does not prevent BIOS hardware throttling (correct behavior); only re-applies the chosen mode after cooldown
- **Opt-in:** Disabled by default for existing installs

### Per-Fan RPM Hardening (Linux)
- RPM writes now bounds-checked: 0..5500 RPM with 0..55 unit clamping
- Blocked unsupported writes on hwmon/unsafe models; users directed to profile/speed abstractions
- **Result:** Safer hardware writes; prevents accidental damage on untested models

---

## 📊 Monitoring & Health

### Battery Health Unknown Semantics
- Unknown battery health no longer reported as `100%`; uses `-1` sentinel internally
- Health alerts suppressed when unknown; chart normalized to `0` (no false healthy signal)
- **Result:** No more misleading battery health displays

### Dashboard Metrics Now Use Real Hardware Data
- Battery health from actual sensor (clamped 0–100)
- Fan efficiency computed from real RPM: `(Fan1RPM + Fan2RPM) / 2 / 50.0`
- Chart legend and health alerts now reflect genuine hardware state
- **Before:** Hardcoded 100% battery, 70% fan efficiency, 3-year estimate

### ThermalMonitoringService Now Active
- Was fully implemented but never instantiated; **now activated in MainViewModel**
- Fires Windows toast notifications on temperature thresholds
- **Default:** CPU Warning 85°C / Critical 95°C, GPU Warning 85°C / Critical 95°C, SSD Warning 70°C
- Alert cooldown: 5 minutes (prevents spam)
- Settings persist in `AppConfig.ThermalAlerts`

### HardwareWatchdogService Now Active
- Monitors for frozen temperature sensors; if no update for ≥60 seconds, sets fans to 100%
- Emergency safety net that was dead code; **now instantiated and running**
- Logged warnings instead of blocking UI via `MessageBox`

---

## 🛠️ Under the Hood

### Memory Optimizer Concurrency & Settings Persistence
- Unified lock-based concurrency guard for manual and scheduled cleaning
- Optional periodic auto-clean mode (1-120 minute intervals, independent of threshold mode)
- Settings persist via `AppConfig` fields: `MemoryIntervalCleanEnabled`, `MemoryIntervalCleanMinutes`, `MemoryAutoCleanEnabled`, `MemoryAutoCleanThreshold`

### Hardware Worker Orphan Timeout Configuration
- Worker timeout no longer hardcoded to 5 minutes
- `HardwareWorkerOrphanTimeoutEnabled` and `HardwareWorkerOrphanTimeoutMinutes` (1-60) configurable in Settings > Monitoring
- Timeout disabled entirely for headless operation
- **Result:** Proper headless server support; no orphaned processes

### Fan Control Reliability Hardening
- Multi-sample confirmation counters for large RPM deltas (prevents false rollback from spikes)
- Preset application blocked during active fan diagnostics
- Atomic preset verification + rollback on controller state mismatch
- **QA fix:** `FanMode.Max` now routed through correct verification path (not just name-based)

### FN+Brightness False-Positive OMEN Key Detection Fix
- Low-level keyboard hook now preferred over WMI OMEN events
- WMI events suppressed during Fn+F2/F3 brightness sequences
- Query widened to include `eventData=8614` (firmware Fn+P profile-cycle) when enabled
- **Result:** No more unexpected OMEN key triggers during brightness adjustment

### Tray Action Pipeline Hardening
- Last-write-wins queue: stale pending actions dropped when newer action arrives
- GPU power and keyboard backlight tray actions routed through safe-mode protection
- **Worker governance:** `CancellationTokenSource` ensures clean exit on app shutdown

### Startup Safe-Mode Guard (Auto-Reset)
- Temporarily blocks tray write actions if early monitoring health is degraded/stale
- **Now auto-resets** when monitoring recovers to Healthy or startup window expires (previously permanent)
- Configuration knobs: enable toggle, startup window duration, timeout threshold

### WinRing0 Legacy Hardening
- Legacy WinRing0 fallback disabled by default; opt-in only via `OMENCORE_ENABLE_WINRING0=1` env var
- Startup and Settings driver checks no longer open WinRing0 device handles
- Driver guidance defaults to PawnIO-first messaging
- **Result:** Eliminates Defender false positives from legacy probing

---

## 📦 Downloads

| File | Description |
|------|-------------|
| `OmenCoreSetup-3.0.0.exe` | **Windows installer (recommended)** |
| `OmenCore-3.0.0-win-x64.zip` | Windows portable |
| `OmenCore-3.0.0-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

### SHA256 Checksums
```
29053D5C60A79C71FB2B892F9835AE066E3CB211316F21C5D0C578B961FF29DB  OmenCoreSetup-3.0.0.exe
5AE6FC781ADB5D0E5DA86C82550179DFBD176191A29EF0DF5C0CCE5134CB5E2B  OmenCore-3.0.0-win-x64.zip
605335229F5C403D915E99184CC20C1A047EB709B6F33817464DF88DAA5858D4  OmenCore-3.0.0-linux-x64.zip
```

---

## ℹ️ Notes & Migration

- **No breaking changes** — all settings carry over automatically
- **Windows Defender:** May flag as false positive. See [Antivirus FAQ](https://github.com/theantipopau/omencore/blob/main/docs/ANTIVIRUS_FAQ.md)
- **PawnIO:** Primary hardware backend; WMI/OGH paths remain as fallback for unsupported models
- **Linux:** `./omencore-cli` for terminal, `./omencore-gui` for Avalonia GUI. Self-contained; no .NET runtime needed
- **Telemetry:** Takes 2–3 polling cycles after launch to converge to stable values
- **LED animations:** Require WMI BIOS firmware support; gracefully degrade to static-only on older models
- **Battery estimator:** `EstimatedBatteryLifeYears` remains at 3.0 static estimate (expandable via HP WMI in future)
- **Linux thermal watchdog:** Opt-in, disabled by default. Re-applies perf mode after cooldown; does not suppress CPU package protection

---

**Full changelog:** https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.0.md

**GitHub:** https://github.com/theantipopau/omencore

**Discord:** https://discord.gg/9WhJdabGk8

**Thank you to everyone on Discord and GitHub for the detailed bug reports and feature requests!** 🙏
