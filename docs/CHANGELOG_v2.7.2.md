# OmenCore v2.7.2 - Linux EC Safety & OSD Fix

**Release Date:** June 2025

This release fixes a critical Linux issue where writing to EC registers on 2025 OMEN Max laptops caused system instability (EC panic / Caps Lock blinking), and fixes the OSD "FPS" display which was incorrectly showing GPU load percentage instead of actual FPS data.

---

## 🔧 Bug Fixes

### OSD: FPS Display Showing GPU Load (Windows)
- **Fixed "Est. FPS" OSD metric displaying GPU load percentage instead of FPS**
  - The OSD was assigning `GpuLoadPercent` directly to the FPS field
  - Now integrates with RTSS (RivaTuner Statistics Server) for real FPS data
  - When RTSS is running: Shows actual FPS with "FPS" label and real frametime
  - When RTSS is not running: Falls back to GPU activity with "GPU" label and % suffix
  - Automatic RTSS detection and reconnection on each OSD update cycle
  - RTSS shared memory reader was already fully implemented but never wired in
  - Settings toggle renamed from "Est. FPS" to "FPS / GPU Activity" for clarity
  - Frametime now shows real values from RTSS instead of fake estimation
  - Key files: `OsdOverlayWindow.xaml.cs`, `OsdOverlayWindow.xaml`, `OsdService.cs`

### Linux: EC Panic on OMEN Max 16t/17t (Critical)
- **Fixed EC register writes causing system instability on 2025 OMEN Max laptops**
  - Root cause: The 2025 OMEN Max 16t-ah000 / 17t-ah000 has a completely different EC register layout
  - Legacy EC register 0x34 (fan speed) contains serial number data on these models
  - Legacy EC register 0xB7 (GPU temp) returns garbage data (0xC0 = 192°C)
  - Writing to these wrong addresses corrupts EC state → Caps Lock panic blink
  - Added DMI-based model detection via `/sys/class/dmi/id/product_name`
  - Blocked all EC writes on affected models with clear error message
  - Affected model patterns: `16t-ah0*`, `16-ah0*`, `17t-ah0*`, `17-ah0*`
  - Resolves GitHub Issue #60

### Linux: Invalid Temperature Readings
- **Fixed 128°C / 192°C garbage temperature readings on new OMEN Max models**
  - EC temperature registers contain non-temperature data on these models
  - Added temperature range validation: values outside 10-115°C are rejected
  - `GetCpuTemperature()` and `GetGpuTemperature()` return null on unsafe EC models
  - Prevents misleading temperature data in CLI output

---

## ✨ New Features

### Linux: ACPI Platform Profile Support
- **Added ACPI `platform_profile` interface for performance mode control**
  - Auto-detects `/sys/firmware/acpi/platform_profile` availability
  - Supports reading and setting profiles: `low-power`, `balanced`, `performance`
  - Works on OMEN Max models where direct EC control is unsafe
  - Fan profiles now map to ACPI profiles on supported systems
  - New `fan status` output shows current ACPI profile

### Linux: hwmon PWM Fan Control
- **Added hwmon-based fan speed control via `hp-wmi` driver**
  - Auto-discovers `/sys/devices/platform/hp-wmi/hwmon/hwmonN/pwm1_enable`
  - PWM modes: 0 = full speed, 1 = manual, 2 = auto
  - `fan --speed 100` on unsafe EC models uses PWM full speed mode
  - `fan --speed 0` restores to auto mode via PWM
  - Fan speed readings via `fan_input` sysfs nodes (actual RPM from hwmon)
  - Provides safe fan control alternative to direct EC register access

### Linux: Enhanced Diagnostics
- **Updated `diagnose` command with new safety and capability fields**
  - Shows `acpi_prof:` with current and available platform profiles
  - Shows `hwmon_fan:` availability status
  - Shows `EC Safety:` status (✓ Normal or ⚠ Blocked for new models)
  - Displays detected model name from DMI
  - Added recommendations specific to OMEN Max 2025 models

---

## � Community Bug Fixes (v2.7.2 Patch)

### Window Not Showing After Reinstall (Windows)
- **Fixed main window never appearing after uninstall/reinstall**
  - Root cause: `%APPDATA%\OmenCore\config.json` survived uninstall with `StartMinimized=true`
  - `ShowMainWindow()` was blocked by `ShouldSuppressWindowActivation` during session lock/unlock
  - Added `ForceShowMainWindow()` that bypasses session suppression for explicit user actions (tray double-click, context menu "Show")
  - Installer now cleans up `%APPDATA%\OmenCore` on uninstall to prevent stale config issues

### Undervolt Apply/Reset Does Nothing (Windows)
- **Fixed undervolt silently succeeding without actually writing MSR registers**
  - `ApplyOffsetAsync()` was storing the offset in `_lastApplied` and returning success even when no MSR access was available
  - `ProbeAsync()` then showed `_lastApplied` values as "current", making it appear the undervolt was applied
  - Now throws `InvalidOperationException` with clear message when PawnIO MSR access is unavailable
  - `_lastApplied` is only updated after confirmed successful MSR write

### Fan Curves Reset on AC/Battery Switch (Windows)
- **Fixed custom fan curves being discarded when switching between AC and battery power**
  - `PowerAutomationService.ApplyPowerProfile()` was creating `FanPreset` with `Curve = new()` (empty curve)
  - Added `LookupFanPreset()` that searches user's saved presets from config first, preserving custom curves
  - Falls back to built-in curve definitions (`GetBuiltInCurve()`) for Max/Performance/Quiet/Auto modes

### GPU Power Boost / Fan Preset Not Restored on Startup (Windows)
- **Fixed PBO, GPU Power Boost, and fan preset settings not being applied on Windows startup**
  - `SettingsRestorationService` existed but was never instantiated (dead code)
  - Added `RestoreSettingsOnStartupAsync()` in MainViewModel with 2-second hardware stabilization delay
  - Restores GPU Power Boost level, fan preset (with curves), and TCC offset from saved config
  - Includes retry logic (3 attempts with 1-second delays) for GPU Power Boost

### MIT LICENSE File Missing
- **Added MIT LICENSE file to repository** — was returning 404 on GitHub

### CI/Build Fixes
- Fixed invisible whitespace in `.github/workflows/ci.yml` causing YAML parse error
- Fixed PSScriptAnalyzer unused variable warning in `test-v2.6.0-features.ps1`

---

## �📋 Technical Details

### EC Safety Architecture (Linux)
The new safety system works as follows:
1. On startup, reads `/sys/class/dmi/id/product_name` to identify the laptop model
2. Checks against known unsafe EC model patterns (2025 OMEN Max series)
3. If unsafe: blocks all `WriteByte()` calls, redirects fan control to ACPI/hwmon
4. Fan speed control chain: hp-wmi WMI → ACPI profile + hwmon PWM → EC (if safe)
5. Temperature reads return null on unsafe models to prevent garbage data

### RTSS Integration (Windows)
- Uses `RTSSSharedMemoryV2` memory-mapped file for zero-overhead FPS reading
- Reads `RTSS_SHARED_MEMORY_HEADER` and `RTSS_SHARED_MEMORY_APP_ENTRY` structs
- Polls every 500ms when OSD is visible
- Provides: instant FPS, average FPS, min/max FPS, 1% low, frametime
- Gracefully falls back to GPU load display when RTSS is not installed

---

## 📦 Downloads

| File | SHA256 |
|------|--------|
| `OmenCoreSetup_2.7.2.exe` | `82F377976FA275F6419BC4894B784CDE04314554A3333F0AC0A33B7DBF2A1B2E` |
| `OmenCore-linux-x64` | `9A24F072A458DC241E4883FB8D047E69D35E402F1EEB5E0440F8E5D1F72AC91C` |
