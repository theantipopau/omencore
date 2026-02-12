# OmenCore v2.8.6 — Community Bug Fix Patch

**Release Date:** 2026-02-11
**Type:** Bug Fix Patch
**Reported By:** OsamaBiden, Saixknox, SimplyCarrying (Discord)

---

## Bug Fixes

### 1. CPU Temperature Showing 0°C (OMEN MAX 16t-ah000)
- **Reporter:** SimplyCarrying (OMEN MAX 16t-ah000, Intel Core Ultra 9 275HX, RTX 5090)
- **Symptom:** CPU temperature displays 0°C in dashboard after initial valid readings
- **Root Cause:** Intel Arrow Lake / Core Ultra CPUs expose temperature sensors with names not in the priority search list. After the first LHM update cycle, the primary sensor returns 0 and no fallback was tried.
- **Fix:** Added "CPU DTS" (Intel DTS sensor) to the priority list. Added a safety net: when named sensor search returns 0 but previous temperature was valid (> 5°C), sweep ALL CPU temperature sensors and use the first one with a plausible value (5–120°C). Logs which fallback sensor was used for diagnostics.
- **Files:** `OmenCore.HardwareWorker/Program.cs`, `OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`

### 2. Fn+F2/F3 Brightness Keys Trigger OSD Toggle
- **Reporter:** OsamaBiden (OMEN 16-xd0xxx, 8BCD)
- **Symptom:** Pressing Fn+F2 or Fn+F3 (brightness keys) shows/hides OmenCore window
- **Root Cause:** User had OSD hotkey configured as bare "F2" (no modifiers). `RegisterHotKey` with modifiers=0 registers the key globally, intercepting ALL F2 keypresses system-wide — including laptop Fn+F2 brightness shortcuts.
- **Fix:** In `OsdService.RegisterToggleHotkey()`, if a function key (F1-F24) has no modifiers, Ctrl+Shift is automatically added. In `SettingsViewModel.OsdHotkey` setter, bare function keys are auto-prefixed with "Ctrl+Shift+" and a warning is logged. Default OSD hotkey changed from "F12" to "Ctrl+Shift+F12".
- **Files:** `OmenCoreApp/Services/OsdService.cs`, `OmenCoreApp/ViewModels/SettingsViewModel.cs`

### 3. RPM Glitch / False MaxFanLevel=100
- **Reporter:** OsamaBiden (OMEN 16-xd0xxx), Saixknox (Victus 16-s0xxx, 8BD5)
- **Symptom:** Fan RPM displays inflated values (6200+ RPM), fan slider allows levels above hardware max, "extreme mode made fans go max for no reason"
- **Root Cause:** `DetectMaxFanLevel()` used current fan levels at startup to determine the max. When OMEN Gaming Hub was running and had set fans to elevated levels (e.g., level 57 exceeds threshold of 55), the auto-detection falsely concluded MaxFanLevel=100 instead of 55. This caused:
  - RPM calculation: level 62 × 100 = 6200 RPM (appears to "unlock" extra RPM)
  - Fan slider allowed setting levels 56-100 which are above the actual 0-55 hardware range
  - Percentage display showed 100% at moderate speeds
- **Fix:** Removed the current-fan-level auto-detection heuristic entirely from `DetectMaxFanLevel()`. MaxFanLevel is now determined only by:
  1. User override (if configured)
  2. ThermalPolicy V2+ → MaxFanLevel=100
  3. Default → MaxFanLevel=55 (classic kRPM range)
  Models requiring MaxFanLevel=100 with V1 policy should be added to the model database.
- **File:** `OmenCoreApp/Hardware/HpWmiBios.cs`

### 4. Quick Profile Switching Doesn't Update OMEN Tab
- **Reporter:** SimplyCarrying (OMEN MAX 16t-ah000, 8D41)
- **Symptom:** When switching between quick profiles (Performance/Balanced/Quiet), the performance mode display in the OMEN tab doesn't update — stays on whatever mode was last shown
- **Root Cause:** `GeneralViewModel.ApplyPerformanceProfile()` / `ApplyBalancedProfile()` / `ApplyQuietProfile()` set the WMI BIOS mode and updated their own `CurrentPerformanceMode` property, but did NOT sync `SystemControlViewModel.SelectedPerformanceMode` — which is what the OMEN tab binds to.
- **Fix:** Added `SystemControlViewModel` reference to `GeneralViewModel` (via `SetSystemControlViewModel()`). All three profile methods now call `_systemControlViewModel.SelectModeByNameNoApply()` to sync the OMEN tab display. Wired in `MainViewModel` alongside the existing `SetFanControlViewModel()` pattern.
- **Files:** `OmenCoreApp/ViewModels/GeneralViewModel.cs`, `OmenCoreApp/ViewModels/MainViewModel.cs`

### 5. Game Library Buttons Don't Work
- **Reporter:** OsamaBiden (OMEN 16-xd0xxx, 8BCD)
- **Symptom:** Unable to select game — Launch, Create Profile, and Edit buttons don't respond
- **Root Cause:** `GameLibraryViewModel.SelectedGame` setter notified `PropertyChanged` but did NOT trigger `CanExecuteChanged` on the button commands. The `RelayCommand` CanExecute predicates (e.g., `_ => SelectedGame != null`) were never re-evaluated after selection changed, so buttons stayed disabled.
- **Fix:** Added `RaiseCanExecuteChanged()` calls for `LaunchGameCommand`, `CreateProfileCommand`, and `EditProfileCommand` in the `SelectedGame` setter.
- **File:** `OmenCoreApp/ViewModels/GameLibraryViewModel.cs`

### 6. GPU Temperature Appears Frozen at Idle
- **Reporter:** theantipopau (OMEN 17-ck2xxx, 8BAD)
- **Symptom:** GPU temperature stuck at 47–48°C with repeated "🥶 GPU temperature appears frozen" warnings every ~30 seconds, even though GPU is functioning normally at idle
- **Root Cause:** Three compounding issues:
  1. **False positive freeze detection** — threshold of 30 identical readings (~30s) is too aggressive for idle GPUs that legitimately maintain stable temperatures
  2. **WMI BIOS confirmation rejected** — when WMI BIOS returned a temperature within 1°C of the "frozen" value, the fallback was rejected instead of treating it as confirmation that the sensor is working correctly
  3. **NVML permanent disable** (in-process mode) — after 3 NVML failures, `_nvmlDisabled` was set permanently with no recovery mechanism, causing GPU temp to remain stale for the entire session
- **Fix:**
  - **Idle-aware threshold** — when GPU load < 10%, freeze threshold is raised from 30 to 120 readings (~2 minutes) to avoid false positives
  - **WMI confirmation logic** — when WMI BIOS returns a value within 1°C of the sensor value, this now CONFIRMS the temperature is valid (not frozen) and resets the freeze counter
  - **Freeze warning dedup** — GPU freeze warning now logs only once per freeze event instead of every 30 readings
  - **NVML auto-recovery** — `_nvmlDisabled` now includes a 60-second cooldown; after cooldown expires, NVML is retried instead of staying permanently disabled
  - **WMI fallback on NVML disable** — when NVML is disabled in-process, `UpdateViaWmiBiosFallback()` is called immediately to keep GPU temp fresh
- **Files:** `OmenCoreApp/Services/HardwareMonitoringService.cs`, `OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`

---

## Enhancements

### 🏗️ Self-Sustaining Monitoring Architecture (Major)
- **OmenCore no longer depends on LibreHardwareMonitor (LHM), WinRing0, or NVML for core monitoring**
- Primary monitoring is now fully self-sustaining using native Windows APIs:
  - **CPU/GPU Temperature**: HP WMI BIOS (command 0x23) — same approach as OmenMon
  - **Fan RPM**: HP WMI BIOS (command 0x38) — hardware-accurate, no kernel driver
  - **GPU Load/Clocks/VRAM/Power**: NVIDIA NVAPI via NvAPIWrapper — direct GPU driver API
  - **CPU Load**: Windows PerformanceCounter — no admin rights needed
  - **CPU Throttling**: PawnIO MSR 0x19C (optional, only if PawnIO available)
  - **RAM/Battery/SSD**: WMI queries — built into Windows
- LHM/Worker process completely removed from monitoring pipeline
- Eliminates: frozen temp false positives, NVML crashes, worker process complexity, WinRing0 antivirus issues
- PawnIO is used ONLY for undervolt/overclock — NOT required for monitoring
- **Files:** `NvapiService.cs` (added GPU monitoring: `GetMonitoringSample()`, `GetGpuLoad()`, `GetGpuTemperature()`, `GetGpuVramUsage()`, `GetGpuPowerWatts()`), `WmiBiosMonitor.cs` (enhanced as primary bridge: NVAPI + MSR + SSD temp + battery discharge), `MainViewModel.cs` (removed LHM, NVAPI initialized early for monitoring)

### Monitoring Source Indicator
- Dashboard health status row now shows the active monitoring source (e.g., "LibreHardwareMonitor (Worker)", "WMI BIOS", "In-Process")
- Helps users and support identify which data path is active at a glance
- Updates live as monitoring mode changes (worker init → connected → fallback)
- **Files:** `HardwareMonitorBridge.cs`, `LibreHardwareMonitorImpl.cs`, `WmiBiosMonitor.cs`, `HardwareMonitoringService.cs`, `DashboardViewModel.cs`, `DashboardView.xaml`

### OMEN Desktop: Experimental Support
- Desktop systems (25L, 30L, 35L, 40L, 45L) now show a warning dialog with option to continue instead of hard-blocking startup
- Allows users to test monitoring and RGB features on desktops while clearly noting fan control may be limited
- **File:** `App.xaml.cs`

### RPM Debounce During Profile Transitions
- 3-second debounce window filters phantom RPM readings during profile switches
- BIOS may return stale target fan levels during transitions which get misinterpreted as actual RPM
- Prevents momentary RPM spikes in UI when switching between Performance/Balanced/Quiet
- **File:** `WmiFanController.cs`

### 🧹 Memory Optimizer Tab (New Feature)
- **New "Memory" tab** with real-time RAM monitoring and one-click memory cleaning
- Uses Windows Native API (`NtSetSystemInformation`) — safe, no third-party dependencies
- **Real-time dashboard**: RAM usage bar (color-coded: teal/amber/red), available memory, system cache, commit charge, page file, process/thread/handle counts — refreshes every 2 seconds
- **Smart Clean** (safe): Trims process working sets + purges low-priority standby pages + combines identical memory pages
- **Deep Clean** (aggressive): All Smart Clean operations PLUS full standby list purge, system file cache flush, modified page list flush — may cause brief system stutter
- **5 individual operations** with risk indicators (Safe/Medium):
  1. Trim Working Sets (Safe) — releases unused memory from all processes
  2. Purge Standby List (Medium) — removes cached pages
  3. Flush File Cache (Medium) — flushes system file cache
  4. Flush Modified Pages (Medium) — writes dirty pages to disk
  5. Combine Memory Pages (Safe, Win10+) — merges identical pages
- **Auto-clean**: Toggle with configurable threshold (50-95%), checks every 30 seconds, runs Smart Clean when exceeded
- Requires admin privileges (OmenCore already runs elevated)
- Enables `SeProfileSingleProcessPrivilege` and `SeIncreaseQuotaPrivilege` automatically
- **Files:** `MemoryOptimizerService.cs` (new), `MemoryOptimizerViewModel.cs` (new), `MemoryOptimizerView.xaml` (new), `MemoryOptimizerView.xaml.cs` (new), `MainViewModel.cs`, `MainWindow.xaml`

### V1/V2-Aware Fan Auto Restore
- `RestoreAutoControl()` now differentiates between V1 (kRPM) and V2 (percentage) fan systems
- V2 systems skip `SetFanLevel(0,0)` which would override BIOS auto control on percentage-scale systems
- V1 systems still use the staged reduction (SetFanLevel → 20 → 0) as a transition hint for BIOS
- **File:** `WmiFanController.cs`

### Hardware Worker Reliability
- **Cooldown reduced from 30 to 5 minutes** — prevents extended frozen temperature readings when worker temporarily fails
- **Reconnect resets restart counter** — successful reconnection to existing worker no longer counts against the retry limit
- **Auto-fallback to in-process monitoring** — when worker enters disabled/cooldown state, automatically reinitializes LibreHardwareMonitor in-process instead of returning stale cached values
- **File:** `HardwareWorkerClient.cs`, `LibreHardwareMonitorImpl.cs`

### Model Database Additions
- **MaxFanLevel property** — `ModelCapabilities` now supports per-model fan level override, eliminating runtime auto-detection guesswork
- **OMEN 16 xd0xxx (2024) AMD** — Added with confirmed V1 fan control, MaxFanLevel=55, 4-zone RGB, 2 fans
- **OMEN MAX 16t-ah000 / 17t-ah000** — MaxFanLevel=100 explicitly set in database entries
- **File:** `ModelCapabilityDatabase.cs`

---

## Files Changed (26)

| File | Change |
|------|--------|
| `OmenCore.HardwareWorker/Program.cs` | Added "CPU DTS" sensor, fallback sweep for 0°C, `_cpuTempFallbackLogged` field |
| `OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs` | Added "CPU DTS" sensor, in-process 0°C fallback, `MonitoringSource` property, worker disabled auto-fallback, NVML 60s auto-recovery cooldown, WMI fallback on NVML disable |
| `OmenCoreApp/Hardware/NvapiService.cs` | Added GPU monitoring methods: `GetMonitoringSample()`, `GetGpuLoad()`, `GetGpuTemperature()`, `GetGpuVramUsage()`, `GetGpuPowerWatts()`, `GpuMonitoringSample` class |
| `OmenCoreApp/Hardware/WmiBiosMonitor.cs` | **Restructured as primary self-sustaining bridge**: accepts NvapiService + PawnIOMsrAccess, integrates GPU metrics (NVAPI), CPU throttling (MSR), SSD temp (WMI), battery discharge rate |
| `OmenCoreApp/Hardware/HpWmiBios.cs` | Removed current-fan-level MaxFanLevel auto-detection, model DB override parameter |
| `OmenCoreApp/Hardware/HardwareMonitorBridge.cs` | Added `MonitoringSource` property to `IHardwareMonitorBridge` |
| `OmenCoreApp/Hardware/HardwareWorkerClient.cs` | Cooldown 30→5min, reconnect resets restart counter |
| `OmenCoreApp/Hardware/WmiFanController.cs` | RPM debounce tracking, V1/V2-aware RestoreAutoControl |
| `OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` | `MaxFanLevel` property, OMEN 16 xd0xxx + MAX models |
| `OmenCoreApp/Services/OsdService.cs` | Bare F-key → Ctrl+Shift+F-key enforcement |
| `OmenCoreApp/Services/HardwareMonitoringService.cs` | `MonitoringSource` passthrough, idle-aware GPU freeze threshold (120 readings when load <10%), WMI confirmation logic, freeze warning dedup |
| `OmenCoreApp/ViewModels/SettingsViewModel.cs` | OSD hotkey validation, default Ctrl+Shift+F12 |
| `OmenCoreApp/ViewModels/GeneralViewModel.cs` | `_systemControlViewModel` field, `SetSystemControlViewModel()`, mode sync in 3 profile methods |
| `OmenCoreApp/ViewModels/MainViewModel.cs` | **Self-sustaining monitoring init**: NVAPI initialized early, PawnIO MSR for throttling, WmiBiosMonitor as sole bridge (LHM removed from pipeline) |
| `OmenCoreApp/ViewModels/GameLibraryViewModel.cs` | `RaiseCanExecuteChanged()` in `SelectedGame` setter |
| `OmenCoreApp/ViewModels/DashboardViewModel.cs` | `MonitoringSourceText` property + PropertyChanged notifications |
| `OmenCoreApp/Views/DashboardView.xaml` | Monitoring source label in health status row |
| `App.xaml.cs` | Desktop support changed from blocking to experimental warning |
| `OmenCoreApp/Hardware/FanController.cs` | `WriteDuty()` removed 4 readback reads + compatibility register writes + added 15s deduplication; `SetMaxSpeed()` removed 3 readback reads + added deduplication; `SetFanSpeeds()` removed 2 readback reads |
| `OmenCoreApp/Services/FanService.cs` | `CheckThermalProtection()` rate-limited EC writes (15s keepalive instead of every poll cycle), removed curve retry loop (3 attempts → 1) |
| `OmenCoreApp/Services/MemoryOptimizerService.cs` | **New** — Safe memory optimizer using `NtSetSystemInformation` P/Invoke: working set trim, standby list purge, file cache flush, modified page flush, page combining. Auto-clean with configurable threshold. |
| `OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs` | **New** — ViewModel with real-time memory stats (2s refresh), clean commands, auto-clean toggle, color-coded usage indicators |
| `OmenCoreApp/Views/MemoryOptimizerView.xaml` | **New** — Memory tab UI: RAM usage bar, stat cards, individual operation buttons, auto-clean settings, info panel |
| `OmenCoreApp/Views/MemoryOptimizerView.xaml.cs` | **New** — Code-behind for memory bar width binding |
| `OmenCoreApp/Views/MainWindow.xaml` | Added Memory tab after Optimizer tab |

---

### 7. EC Overwhelm → False Battery Critical Shutdown (System Crash)
- **Reporter:** theantipopau (OMEN 17-ck2xxx, 8BAD), additional user via Discord
- **Symptom:** System restarts/crashes with Windows Event 524 "Critical Battery Trigger Met" — even with charger plugged in. ACPI Event 13 "EC did not respond within timeout" precedes each crash.
- **Root Cause:** OmenCore's EC (Embedded Controller) register operations overwhelmed the EC. The EC handles both fan control AND battery monitoring. When overwhelmed:
  1. ACPI Event 13 fires (EC timeout)
  2. EC stops responding to OS battery status queries
  3. Windows loses battery status → triggers false "Critical Battery Trigger Met" (Event 524)
  4. Emergency shutdown — even with charger plugged in
  
  **Sources of EC overload:**
  - `WriteDuty()` did 7+ EC writes + 4 readback reads + N compatibility writes = 11+ EC ops per call
  - `SetMaxSpeed()` did 7 writes + 3 readback reads = 10 EC ops
  - `SetFanSpeeds()` did 7 writes + 2 readback reads = 9 EC ops
  - Thermal protection called `SetFanSpeed(100)` EVERY poll cycle (1-5s) when active — 11+ EC ops repeated
  - Curve retry loop retried failed writes up to 3x with 300ms delays — 33+ EC ops on failure
  - Combined: **15-33+ EC operations every 1-5 seconds**

- **Fix (5 changes):**
  1. **Removed readback verification**: `WriteDuty()` -4 reads, `SetMaxSpeed()` -3 reads, `SetFanSpeeds()` -2 reads → saves 9 EC reads per write. These were purely for diagnostic logging and caused "EC output buffer not full" errors.
  2. **Removed compatibility register writes**: `WriteDuty()` no longer writes to unknown `_registerMap` registers
  3. **Added 15s EC write deduplication**: `WriteDuty()` tracks last written percent + timestamp, skips identical writes within 15 seconds. Prevents thermal protection from hammering EC when fans are already at target speed.
  4. **Rate-limited thermal protection**: `CheckThermalProtection()` now only writes to EC on first activation, then re-applies as keepalive every 15 seconds (was every 1-5s poll cycle)
  5. **Removed curve retry loop**: Fan curve application now single attempt instead of 3 retries with 300ms delays
  
  **Before**: ~15-33 EC ops every 1-5s during thermal protection  
  **After**: 7 EC writes once, then 0 for 15 seconds → ~0.5 EC ops/second
  
- **Files:** `OmenCoreApp/Hardware/FanController.cs`, `OmenCoreApp/Services/FanService.cs`

---

## EC Safety Hardening (Session 2)

### 8. Fan Smoothing Ramps Disabled for EC Backend
- **Symptom:** Smoothing ramps generate rapid sequential EC writes (every 200ms for 1000ms) during fan speed transitions, risking ACPI EC timeout
- **Fix:** When `IsEcBackend` is true, smoothing is automatically disabled — fan speed changes are applied as a single EC write instead of a stepped ramp
- **Files:** `FanService.cs` (`IsEcBackend` property, `allowSmoothing` guard, single-write bypass in `RampFanToPercentAsync`)

### 9. EC RPM Read Throttling When LHM Has No Fan Sensors
- **Symptom:** When LHM bridge exists but returns zero fan sensors, `ReadFanSpeeds()` falls through to direct EC register reads every poll cycle (~1.5s)
- **Fix:** Added 10-second throttle interval — EC RPM reads via registers 0x34/0x35 limited to once per 10 seconds when LHM provides no fan data. Returns estimated speeds between reads.
- **File:** `FanController.cs` (`_lastEcRpmReadTime`, `EcRpmReadMinIntervalSeconds`)

### 10. Experimental EC Keyboard Write Throttling
- **Symptom:** `SetAllZoneColors()` and `SetZoneColorViaEc()` could fire EC writes with no rate limiting during rapid color changes
- **Fix:** Added 200ms minimum interval between EC keyboard writes with a lock to prevent concurrent access
- **File:** `KeyboardLightingService.cs` (`_ecKeyboardWriteLock`, `IsEcKeyboardWriteAllowed()`)

### 11. Duplicate Independent Curves Block Removed
- **Symptom:** `ApplyCurveIfNeededAsync` contained a duplicated `hasIndependentCurves` block — harmless but wasteful
- **Fix:** Removed the duplicate block
- **File:** `FanService.cs`

---

## Fan RPM Accuracy Fix

### 12. OMEN Tab Fan RPMs Now Use WMI BIOS (Real Hardware Values)
- **Reporter:** theantipopau (OMEN 17-ck2xxx, 8BAD)
- **Symptom:** OMEN tab showed estimated fan RPMs (calculated from last-set percent or temperature) instead of actual hardware values. On V1 systems in self-sustaining mode (no LHM), `FanController.ReadFanSpeeds()` returned `RpmSource.Estimated` with names like "CPU Fan (est.)"
- **Root Cause:** Two issues:
  1. `FanController` had no access to `HpWmiBios` — when bridge was null (self-sustaining mode), it could only estimate RPMs
  2. `WmiBiosMonitor.UpdateReadings()` only tried `GetFanRpmDirect()` (command 0x38, V2-only). On V1 systems this returns null, leaving cached RPMs at 0
  3. EC register reads (0x34/0x35) were unreliable: 65% garbage values (0/256, 144/256, etc.) from 1,002 readings during stress test
- **Fix:**
  1. **FanController gets WMI BIOS access** — `HpWmiBios` passed from `FanControllerFactory` to `FanController`. When bridge is null, `ReadWmiBiosFanSpeeds()` calls `GetFanLevel()` (command 0x2D) which returns real fan levels in krpm units (level 44 = 4400 RPM)
  2. **WmiBiosMonitor V1 fallback** — when `GetFanRpmDirect()` returns null, falls back to `GetFanLevel()` × 100 for dashboard RPM values
  3. RPM source tagged as `RpmSource.WmiBios` with proper fan names ("CPU Fan" / "GPU Fan")
- **Files:** `FanController.cs` (`_wmiBios` field, `ReadWmiBiosFanSpeeds()` method), `FanControllerFactory.cs` (passes `_wmiBios` to constructor), `WmiBiosMonitor.cs` (`GetFanLevel` fallback in `UpdateReadings`)

---

## UI Enhancements

### Stay on Top Setting Surfaced in UI
- **StayOnTop toggle** added to Settings > General under new "Window Behavior" subheader
- Shows "✓ Active" badge next to toggle when enabled
- **"Topmost" badge** appears in the General section header when StayOnTop is active
- Changes apply immediately (live `Topmost` update via Dispatcher)
- Loads/saves with existing `AppConfig.StayOnTop` property
- **Files:** `SettingsViewModel.cs` (`StayOnTop` property), `SettingsView.xaml` (toggle, badge, subheaders)

### Settings > General Visual Polish
- **"Window Behavior"** subheader groups the StayOnTop toggle
- **"Tray & UI"** subheader with horizontal divider groups temperature unit, Corsair link, and telemetry settings
- Tray menu "Stay on Top" entry now shows correct initial state from config and has a tooltip: "Keep OmenCore above other windows (On/Off)"
- **Files:** `SettingsView.xaml`, `TrayIconService.cs`

---

## Files Changed (Session 2: +6 files)

| File | Change |
|------|--------|
| `OmenCoreApp/Services/FanService.cs` | Added `IsEcBackend` property, disabled smoothing for EC, single-write bypass in `RampFanToPercentAsync`, removed duplicate `hasIndependentCurves` block |
| `OmenCoreApp/Hardware/FanController.cs` | Added `_wmiBios` field + constructor param, `ReadWmiBiosFanSpeeds()` method (WMI BIOS GetFanLevel), EC RPM read throttling (10s interval) |
| `OmenCoreApp/Hardware/FanControllerFactory.cs` | Passes `_wmiBios` to `FanController` constructor |
| `OmenCoreApp/Hardware/WmiBiosMonitor.cs` | Added `GetFanLevel()` V1 fallback when `GetFanRpmDirect()` returns null |
| `OmenCoreApp/Services/KeyboardLightingService.cs` | Added 200ms EC keyboard write throttle with lock |
| `OmenCoreApp/ViewModels/SettingsViewModel.cs` | Added `StayOnTop` property with live Topmost apply |
| `OmenCoreApp/Views/SettingsView.xaml` | StayOnTop toggle, Topmost badge, Window Behavior / Tray & UI subheaders, divider |
| `OmenCoreApp/Utils/TrayIconService.cs` | Initial Stay on Top state from config, tooltip |

---

## MSI Afterburner Coexistence (Session 3)

### 13. Afterburner Shared Memory Integration
- **Problem:** When MSI Afterburner is running alongside OmenCore, both apps poll the NVIDIA driver via NVAPI simultaneously. The NVIDIA driver serializes GPU sensor access, causing:
  - NVAPI calls to block/stall (NVIDIA serializes concurrent GPU queries)
  - GPU temperature appearing frozen (stale data from contention)
  - Potential UI latency from slow polling cycles
- **Solution:** When Afterburner is detected, OmenCore reads GPU metrics directly from Afterburner's MAHM shared memory (zero-copy, zero-contention) instead of polling NVAPI:
  - **GPU temperature** — read from Afterburner (same die sensor data, no driver contention)
  - **Core/memory clocks** — read from Afterburner shared memory
  - **GPU power** — read from Afterburner shared memory
  - **GPU load** — read from Afterburner if available, otherwise lightweight NVAPI call
  - **VRAM** — lightweight NVAPI call (minimal contention)
- **Behavior:**
  - Auto-activates when Afterburner's `MAHMSharedMemory` is available
  - Auto-deactivates when Afterburner exits (falls back to full NVAPI monitoring)
  - Logs `[WmiBiosMonitor] ✓ Afterburner coexistence active` when activated
  - GPU OC controls in OmenCore remain fully functional (NVAPI write operations are unaffected)
- **Conflict severity** downgraded from Medium to Low — no user action needed
- **Files:** `WmiBiosMonitor.cs` (Afterburner coexistence logic), `NvapiService.cs` (`GetLoadAndVramOnly()` method), `ConflictDetectionService.cs` (severity + GPU usage parsing), `MainViewModel.cs` (wiring)

## Files Changed (Session 3: +4 files)

| File | Change |
|------|--------|
| `OmenCoreApp/Hardware/WmiBiosMonitor.cs` | Afterburner coexistence in `UpdateReadings()` — reads GPU temp/clocks/power from shared memory, NVAPI reduced to load+VRAM |
| `OmenCoreApp/Hardware/NvapiService.cs` | New `GetLoadAndVramOnly()` method — lightweight NVAPI polling (load + VRAM only) |
| `OmenCoreApp/Services/ConflictDetectionService.cs` | Afterburner severity Low, GPU usage/load parsing from MAHM, `AfterburnerCoexistenceActive` property |
| `OmenCoreApp/ViewModels/MainViewModel.cs` | Stores `_wmiBiosMonitor` reference, wires `SetAfterburnerCoexistence()` |

---

## Power Monitoring Fixes (Session 4)

### 14. CPU Power Always Shows 0W
- **Reporter:** theantipopau (OMEN 17-ck2xxx, 8BAD)
- **Symptom:** General tab CPU power reading permanently shows 0W
- **Root Cause:** `CpuPowerWatts` field existed on the `MonitoringSample` model but was never populated — no data source was wired to provide CPU power readings. `WmiBiosMonitor.BuildSampleFromCache()` did not include any CPU power value.
- **Fix:** Implemented Intel RAPL MSR power reading via PawnIO:
  - New `ReadCpuPackagePowerWatts()` method in `PawnIOMsrAccess.cs` reads MSR 0x606 (RAPL_POWER_UNIT, bits [12:8] for Energy Status Units) and MSR 0x611 (PKG_ENERGY_STATUS, 32-bit energy counter)
  - Computes watts from energy delta between successive calls with 32-bit overflow handling
  - Sanity capped at 500W to filter invalid readings
  - `WmiBiosMonitor` reads RAPL power in its monitoring loop (SOURCE 4 block) and populates `CpuPowerWatts` in `BuildSampleFromCache()`
- **Files:** `PawnIOMsrAccess.cs` (`ReadCpuPackagePowerWatts()`, `_raplEnergyUnit`, `_lastEnergyReading`, `_lastEnergyTimestamp`), `WmiBiosMonitor.cs` (`_cachedCpuPowerWatts`, wired into `BuildSampleFromCache()`)

### 15. GPU Power Always Shows 0W (NVAPI Fallback TDP)
- **Reporter:** theantipopau (OMEN 17-ck2xxx, 8BAD)
- **Symptom:** General tab GPU power reading permanently shows 0W
- **Root Cause:** `DefaultPowerLimitWatts` was 0 because both `QueryPowerLimits()` and `QueryPowerLimitsWrapper()` silently failed — no "Power limits" entry ever appeared in logs. `PowerTopologyInformation` returned raw percentage values or threw silently, so power percentage × 0W TDP = 0W.
- **Fix:**
  - New `EstimateFallbackTdp(string? gpuName)` method maps known laptop GPU names to default TDPs (RTX 4090→150W, 4080→150W, 4070→140W, 4060/4050→115W, 3080→150W, 3070→125W, 3060→115W)
  - `GetMonitoringSample()` uses `effectiveTdp = DefaultPowerLimitWatts > 0 ? DefaultPowerLimitWatts : EstimateFallbackTdp(GpuName)`
  - Added catch block with logging + fallback to `GetGpuPowerWatts()` on exception
  - Rounds power to 1 decimal place
- **File:** `NvapiService.cs` (`EstimateFallbackTdp()`, power section in `GetMonitoringSample()`, `GetGpuPowerWatts()`)

### 16. Afterburner Coexistence Never Activates (MAHM Shared Memory Offset Bug)
- **Reporter:** theantipopau (OMEN 17-ck2xxx, 8BAD)
- **Symptom:** Logs show "coexistence configured" but never "coexistence active" — GPU data from Afterburner is never used despite Afterburner running
- **Root Cause:** MAHM v2 shared memory data was being read at the wrong offset. Code read float at `offset + 260` which lands in the `szSrcUnits` string field, not the actual data field. The MAHM v2 entry struct layout is:
  ```
  szSrcName[260] + szSrcUnits[260] + szLocSrcName[260] + szLocSrcUnits[260] + dwSrcId(4) + dwSrcFlags(4) + data(4) = offset 1048
  ```
  Reading at offset 260 returned garbage for all fields, so `GpuTemperature > 0` was always false and coexistence never activated.
- **Fix:** Changed data float read from hardcoded `offset + 260` to dynamic `offset + dataOffset` where `dataOffset = entrySize >= 1072 ? 1048 : 528` (v2 vs v1 format). This correctly reads the `data` float field in both MAHM v1 and v2 entry structures.
- **File:** `ConflictDetectionService.cs` (MAHM data offset calculation)

## Files Changed (Session 4: +3 files)

| File | Change |
|------|--------|
| `OmenCoreApp/Hardware/PawnIOMsrAccess.cs` | Intel RAPL MSR CPU power reading: `ReadCpuPackagePowerWatts()` using MSR 0x606 + 0x611 |
| `OmenCoreApp/Hardware/WmiBiosMonitor.cs` | `_cachedCpuPowerWatts` field, RAPL read in SOURCE 4 block, `CpuPowerWatts` in `BuildSampleFromCache()` |
| `OmenCoreApp/Hardware/NvapiService.cs` | `EstimateFallbackTdp()` with known laptop GPU TDPs, fallback power in `GetMonitoringSample()` + `GetGpuPowerWatts()` |
| `OmenCoreApp/Services/ConflictDetectionService.cs` | Fixed MAHM v2 data offset from 260 to 1048 (dynamic v1/v2) |

---

## Tested Systems

| System | Model ID | Issue | Status |
|--------|----------|-------|--------|
| OMEN 16-xd0xxx | 8BCD | Fn+F2/F3, RPM glitch, game library buttons | Fixed |
| Victus 16-s0xxx | 8BD5 | RPM glitch, extreme mode max fans | Fixed |
| OMEN MAX 16t-ah000 | 8D41 | CPU 0°C, quick profile UI desync | Fixed |
| OMEN 17-ck2xxx | 8BAD | GPU temp frozen at idle, EC crash/false battery shutdown, CPU/GPU 0W power, Afterburner coexistence | Fixed |

---

## Downloads

| File | Size |
|------|------|
| `OmenCoreSetup-2.8.6.exe` | Windows installer (recommended) |
| `OmenCore-2.8.6-win-x64.zip` | Windows portable |
| `OmenCore-2.8.6-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

## SHA256 Checksums

```
931704AE3B733046CA81C9586A9E41645BCDCEB1C0B1D0F0EF3DE14DBC600EC0  OmenCoreSetup-2.8.6.exe
2FEE152809400A913D3811A913CC0F13409966B99245ABF9E4A6B81CC900B3A5  OmenCore-2.8.6-win-x64.zip
2ED425B6840BE8142BDCFA63ADD8927B9A02B835712B99414B9417688726BC6D  OmenCore-2.8.6-linux-x64.zip
```
