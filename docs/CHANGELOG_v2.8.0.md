# OmenCore v2.8.0 - Feature & Safety Update

**Release Date:** February 8, 2026  
**Branch:** feature/v2.7.0-development

---

## Highlights

v2.8.0 is a comprehensive feature and safety update addressing findings from a full codebase audit. It includes hardware safety clamping for undervolting, GPU overclocking fixes, new hardware support (AMD GPU, display overdrive), enhanced OSD metrics, full Corsair HID effects, bloatware uninstaller fixes, OMEN desktop support, HardwareWorker resilience to eliminate temperature dropouts, significant dead code cleanup, Linux EC safety for 2025 OMEN Max models, auto-detection of per-model fan level ranges, thermal protection debounce tuning, fan curve save/load UX, improved diagnostic exports, tab UI refresh, and all community bug fixes from v2.7.2.

---

## ✨ New Features

### AMD GPU Overclocking (via ADL2/Overdrive8)
- Full AMD discrete GPU overclocking support for RDNA/RDNA2/RDNA3 GPUs
- Core clock offset adjustment (±500 MHz) with hardware-reported range clamping
- Memory clock offset adjustment (±500 MHz)
- Power limit adjustment (relative %, clamped to ADL-reported min/max)
- Reset to defaults functionality
- New `AmdGpuService` using AMD Display Library P/Invoke (`atiadlxx.dll`)
- Full UI in Tuning view with sliders and apply/reset buttons

### Display Overdrive Toggle
- Panel overdrive control via HP WMI BIOS commands (CMD 0x35/0x36)
- Auto-detect support on compatible OMEN displays
- Toggle in Advanced view with proper error handling and UI revert on failure

### Game Library Tab
- New Game Library tab in main window
- Lazy-loaded `GameLibraryViewModel` for zero-cost when unused
- Integrates with existing `GameLibraryService`

### OSD Enhancements
- **Battery %** — Shows charge level with color-coded status (green/yellow/orange/red) and AC/battery icon (⚡/🔋)
- **CPU Clock Speed** — Average across all cores, auto-formats GHz/MHz
- **GPU Clock Speed** — Real-time GPU clock from monitoring sample
- All three new metrics have toggle controls in Settings → OSD configuration
- Follows existing OSD Grid pattern with consistent styling

### Logitech HID++ 2.0 Full Effects
- Breathing effect (type 0x03) with configurable speed
- Spectrum/color cycle effect (type 0x04)
- Flash/strobe effect (type 0x05) with configurable speed
- Wave effect (type 0x06) with direction parameter
- New `SendEffectCommand()` with HID++ 1.0 fallback
- `SpeedToPeriod()` helper for user-friendly speed 1-10 mapping
- All effects gracefully fall back to static color on failure

### Corsair HID Effects (Breathing, Spectrum, Wave)
- **Full effect support** — Corsair devices now support breathing, spectrum cycle, and wave effects via direct HID, matching Logitech parity
- `CorsairHidDirect`: New `BuildEffectReport()` sends 65-byte HID packets with `0xD0` effect-mode marker
- Breathing: Dual-color pulse using primary + secondary color, configurable speed
- Spectrum: Rainbow cycle across all device LEDs
- Wave: Left-to-right sweep on keyboards (mice fall back to spectrum)
- `SendEffectReportAsync()` with full retry logic (3 attempts) + commit
- `SpeedToPeriod(double)` maps 0.5–5.0 speed range to HID period byte
- `GetDeviceClassCmd()` routes mice (0x05), keyboards (0x09), generic (0x07)
- `CorsairRgbProvider.SupportedEffects` now **honestly includes** only implemented effects (Static, Breathing, Spectrum, Wave, Custom, Off)
- `SetBreathingEffectAsync()` calls real effect implementation (was: static color fallback)
- `SetSpectrumEffectAsync()` calls real effect implementation (was: no-op)
- New `SetWaveEffectAsync()` method
- `ApplyEffectAsync()` handles `"effect:breathing"`, `"effect:spectrum"`, `"effect:wave"` string IDs
- `CorsairDeviceService`: New `ApplyBreathingToAllAsync()`, `ApplySpectrumToAllAsync()`, `ApplyWaveToAllAsync()` methods

### Corsair HID Brightness Control
- New `SetBrightness(int percent)` on `CorsairHidDirect` — scales all RGB values 0-100% before sending
- Brightness applied consistently across static colors and all effect modes

### OMEN Desktop Support
- **WMI fan control enabled** for OMEN 25L, 30L, 35L, 40L, 45L desktops
- Fan curves via WMI `SetFanLevel` command (same as laptop WMI backend)
- RPM readback available via WMI fan status queries
- Performance modes supported on all OMEN desktop models
- Desktop RGB lighting via USB HID (unchanged, already supported via `OmenDesktopRgbService`)
- New OMEN 35L model entry added to capability database
- Desktop warning messages updated — no longer says "experimental" or "use OGH"

### ConflictDetection Service Wiring
- `ConflictDetectionService` now instantiated and activated at startup
- Initial conflict scan on launch
- Background monitoring every 60 seconds for conflicting software

### Fan Curve Save/Load UX
- **Delete custom presets** with confirmation dialog
- **Import/Export** fan curves as JSON files via file dialogs
- `SavedCustomPresets` filtered property showing only user-created presets
- `HasSavedPresets` visibility binding for conditional UI
- `ReapplySavedPresetCommand` for quick one-click re-apply
- Validation before saving (curve validation + empty name check)
- Auto-apply on save for immediate feedback
- UI: Saved Presets ComboBox with Delete, Import, and Export buttons in Fan Control view

### Tab UI Overhaul
- **Scrollable tab headers** with `ScrollViewer` for overflow support
- **Animated accent underline** on selected tab with `ScaleTransform` and `CubicEase` easing
- **Compact padding** reduced from 16,12 → 10,10 for better space efficiency
- **Hover effects** with `SurfaceHighlightBrush` background on mouse-over
- FontSize reduced 14 → 13 for cleaner appearance
- Hidden-but-functional scrollbar (Height=4, Opacity=0.3)

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
- Updated `diagnose` command with ACPI profile, hwmon fan, EC safety status fields
- Added recommendations specific to OMEN Max 2025 models

---

## 🔧 Bug Fixes

### Thermal Protection Debounce (Community Feedback)
- **Added time-based debounce to prevent fan speed yo-yo during transient temperature spikes**
  - 5-second activation debounce: thermal protection only triggers if temp stays above threshold for 5 continuous seconds
  - 15-second release debounce: fans stay boosted for 15 seconds after temps normalize before releasing
  - Raised default threshold from 80°C to 90°C based on community feedback (configurable 75-95°C)
  - Emergency threshold remains at 95°C (immediate response, no debounce)
  - Increased hysteresis from 5°C to 10°C for smoother transitions
  - Relaxed safety bounds clamping (old 60°C→40%/70°C→70% removed as too aggressive)
  - Logged: `"⚠️ THERMAL WARNING: {temp}°C sustained for {duration}s"` and `"✓ Temps normalized"`

### Game Library Scroll Fix
- **Fixed game library list not scrolling properly**
  - Added `ScrollViewer.HorizontalScrollBarVisibility="Disabled"` to prevent horizontal overflow
  - Added `ScrollViewer.VerticalScrollBarVisibility="Auto"` for proper vertical scroll
  - Enabled pixel-based smooth scrolling via `ScrollViewer.CanContentScroll="False"` and `VirtualizingPanel.ScrollUnit="Pixel"`

### HardwareWorker Temperature Dropout (Critical — Community Report)
- **Fixed**: Temperature readings stopping when OmenCore restarts or crashes
  - Root cause: Worker process was hard-wired to `Environment.Exit(0)` when parent died
  - Worker now **survives parent exit** and continues collecting sensor data
  - New OmenCore instances connect to the already-running worker — no hardware re-scan needed
  - Eliminates the 3-5 second temperature gap on every app restart
- **Added**: `SET_PARENT` IPC protocol command — new OmenCore instances register as the new parent
- **Added**: Global mutex (`OmenCore_HardwareWorker_Mutex`) prevents duplicate worker processes
- **Added**: Orphan watchdog — worker self-exits after 5 minutes with no client activity (prevents leaked processes)
- **Improved**: Client `StartAsync()` now tries connecting to existing pipe before launching a new process
- **Improved**: `GetSampleAsync()` reconnection also tries existing worker before full restart cycle
- Technical: Worker no longer calls `Environment.Exit(0)` on parent death; instead sets `_parentAlive = false` and lets the orphan watchdog handle cleanup

### Bloatware Uninstaller Not Working (Community Report)
- **Fixed**: HP bloatware packages that were pre-provisioned by the OEM could not be removed
  - Added 3-tier removal strategy: current-user → `-AllUsers` → `Remove-AppxProvisionedPackage`
  - Provisioned packages now properly removed even when they survive normal `Remove-AppxPackage`
  - Clear error messages when admin elevation is needed ("Run OmenCore as Administrator")
  - Stderr captured and logged for all removal attempts (was silently swallowed)
- **Fixed**: Scan/remove filter mismatch — scan found Realtek packages but remove excluded them
  - Scan no longer includes Realtek (audio/network drivers are NOT bloatware)
  - Scan now includes OMEN Gaming Hub store variants (`AD2F1837.OMEN*`, `HPInc.HP*`)
  - Remove filter exactly matches scan filter — no more phantom counts
- **Added**: 8 new HP bloatware targets — HPWorkWell, HPAccessoryCenter, HPSystemInformation, HPQuickTouch, HPPowerManager, OMENCommandCenter/Dev/Beta, HPInc.HPGamingHub
- **Improved**: `BloatwareManagerService.RemoveAppxPackageAsync()` also upgraded to 3-tier removal
- HP Support Assistant is explicitly preserved in both scan and remove paths
- Failed packages now listed by name with reason (e.g., "needs admin") in the results UI

### GPU Overclock Store-on-Failure (Critical)
- **NvapiService**: `SetCoreClockOffset()`, `SetMemoryClockOffset()`, and `SetVoltageOffset()` no longer store offset values when the hardware rejects the change or NVAPI is unavailable
- Previously, the UI could show offsets that weren't actually applied to hardware
- Values are now only persisted after confirmed `NVAPI_OK` response

### Undervolt Safety Clamping (Safety)
- **Intel MSR**: Core and cache voltage offsets now clamped to [-250, 0] mV via `Math.Clamp()`
- **AMD Curve Optimizer**: All-core and iGPU CO values clamped to [-30, +30] via `Math.Clamp()`
- Prevents accidental extreme values that could cause system instability

### OSD: FPS Display Showing GPU Load (Windows)
- **Fixed "Est. FPS" OSD metric displaying GPU load percentage instead of FPS**
  - Now integrates with RTSS (RivaTuner Statistics Server) for real FPS data
  - When RTSS is running: Shows actual FPS with "FPS" label and real frametime
  - When RTSS is not running: Falls back to GPU activity with "GPU" label and % suffix
  - Automatic RTSS detection and reconnection on each OSD update cycle

### Linux: EC Panic on OMEN Max 16t/17t (Critical)
- **Fixed EC register writes causing system instability on 2025 OMEN Max laptops**
  - Root cause: The 2025 OMEN Max 16t-ah000 / 17t-ah000 has a completely different EC register layout
  - Legacy EC register 0x34 (fan speed) contains serial number data on these models
  - Added DMI-based model detection via `/sys/class/dmi/id/product_name`
  - Blocked all EC writes on affected models with clear error message
  - Affected model patterns: `16t-ah0*`, `16-ah0*`, `17t-ah0*`, `17-ah0*`
  - Resolves GitHub Issue #60

### Linux: Invalid Temperature Readings
- **Fixed 128°C / 192°C garbage temperature readings on new OMEN Max models**
  - Added temperature range validation: values outside 10-115°C are rejected
  - `GetCpuTemperature()` and `GetGpuTemperature()` return null on unsafe EC models

### Window Not Showing After Reinstall (Windows)
- **Fixed main window never appearing after uninstall/reinstall**
  - Root cause: `%APPDATA%\OmenCore\config.json` survived uninstall with `StartMinimized=true`
  - Added `ForceShowMainWindow()` that bypasses session suppression for explicit user actions
  - Installer now cleans up `%APPDATA%\OmenCore` on uninstall

### Undervolt Apply/Reset Does Nothing (Windows)
- **Fixed undervolt silently succeeding without actually writing MSR registers**
  - Now throws `InvalidOperationException` with clear message when PawnIO MSR access is unavailable
  - `_lastApplied` is only updated after confirmed successful MSR write

### Fan Curves Reset on AC/Battery Switch (Windows)
- **Fixed custom fan curves being discarded when switching between AC and battery power**
  - Added `LookupFanPreset()` that searches user's saved presets from config first
  - Falls back to built-in curve definitions for Max/Performance/Quiet/Auto modes

### GPU Power Boost / Fan Preset Not Restored on Startup (Windows)
- **Fixed PBO, GPU Power Boost, and fan preset settings not being applied on Windows startup**
  - Added `RestoreSettingsOnStartupAsync()` in MainViewModel with retry logic

### MIT LICENSE File Missing
- Added MIT LICENSE file to repository

### CI/Build Fixes
- Fixed invisible whitespace in `.github/workflows/ci.yml` causing YAML parse error
- Fixed PSScriptAnalyzer unused variable warning in `test-v2.6.0-features.ps1`

### MaxFanLevel Auto-Detection (Fan Control Accuracy)
- **Fixed fan speed mapping on models that use 0-100 percentage range instead of 0-55 krpm**
  - Root cause: `MaxFanLevel = 55` was hardcoded, but some models (OMEN Max 2025+, newer 16-inch) use 0-100
  - Setting fans to 100% only sent level 55 on percentage-based models, meaning ~55% actual speed
  - New `DetectMaxFanLevel()` in `HpWmiBios` auto-detects at startup:
    - Reads current fan levels — if either exceeds 55, range is definitely 0-100
    - ThermalPolicy V2 (OMEN Max 2025+) systems default to 100
    - Classic V0/V1 systems default to 55 (0-5500 RPM krpm range)
  - `WmiFanController` now uses auto-detected value for all percent→level conversions
  - Both `ApplyCustomCurve()`, `SetFanSpeed()`, `SetFanSpeeds()`, and countdown extension tick all updated
  - Fallback max-mode command `SetFanLevel(55, 55)` replaced with `SetFanLevel(maxLevel, maxLevel)`
  - Removed dead code in `FanService` that computed but never used a local `fanLevel`
  - Logged at startup: `"WmiFanController: Max fan level = {level}"` for user troubleshooting

### Linux Performance Mode "Root Permissions Required" (Critical)
- **Fixed performance mode changes failing even when running as root/sudo**
  - Root cause 1: Hardcoded `/sys/devices/platform/hp-wmi/thermal_profile` path, but modern kernels (5.18+) use `/sys/firmware/acpi/platform_profile`
  - Root cause 2: Wrote wrong profile values (`"quiet"` instead of `"low-power"`)
  - Root cause 3: `File.WriteAllTextAsync` uses `FileMode.Create` which sysfs rejects even as root
  - Multi-path resolution: checks 3 sysfs paths in priority order (`platform_profile` → `hp-wmi` → `thinkpad_acpi`)
  - Reads `platform_profile_choices` to discover valid kernel profile strings
  - 3-tier write fallback: `FileStream(FileMode.Open)` → `echo | tee` → `pkexec`
  - Added `RunCommandWithExitCodeAsync()` helper for shell command execution with exit codes

### Converter Crash Prevention (12 fixes)
- **Fixed all 12 `ConvertBack` methods in WPF value converters** that threw `NotImplementedException`
  - Affects: `BoolConverters`, `DashboardConverters`, `TemperatureToColorConverter`, `StringNotEmptyToVisibilityConverter`, `NullToVisibilityConverter`, `IntZeroToVisibilityConverter`, `IntGreaterThanZeroToVisibilityConverter`
  - Could crash the app on accidental two-way binding scenarios
  - All now return `Binding.DoNothing` (safe no-op)

### Diagnostic Export Real Detection (6 fixes)
- **Replaced 6 stub methods in DiagnosticExportService with real hardware/software detection**
  - `GetSecureBootStatus()`: Reads `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled`
  - `GetHvciStatus()`: Reads `DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled`
  - `GetWinRing0Status()`: Checks `System32\drivers\WinRing0x64.sys` and temp directory for file existence
  - `GetPawnIOStatus()`: Reads `Services\PawnIO` registry with start type decoding (Active/Manual/Disabled/Not Found)
  - `GetXtuServiceStatus()`: Checks `XTU3Service` process + service registry
  - `GetAfterburnerStatus()`: Checks `MSIAfterburner` process + `MSI Afterburner` install path

### AutoUpdate Preference Not Persisting
- **Fixed `SetAutoUpdateEnabled()` not actually saving the preference**
  - Now properly sets `_preferences.AutoCheckEnabled` and `CheckOnStartup`
  - Calls `ConfigureBackgroundChecks()` to immediately apply changes

### WMI Keyboard Brightness Control
- **Implemented keyboard brightness slider** (was a non-functional stub)
  - `brightness=0`: Sends `SetBacklight(false)` (backlight off, byte `0x64`)
  - `brightness>0`: Sends `SetBacklight(true)` (byte `0xE4`) + scales all 12 color table bytes by brightness percentage
  - Uses existing `GetColorTable()` / `SetColorTable()` WMI commands

### Avalonia Fan Control Error Notification
- **Fixed fan control errors on Linux silently swallowed** (only went to `Debug.WriteLine`)
  - `ApplyCurve()` catch block now sets `StatusMessage` for user-visible feedback

---

## 🧹 Code Cleanup

### Dead Code Removal (~1,525 lines)
- **SettingsRestorationService.cs** — Superseded by `RestoreSettingsOnStartupAsync()` in MainViewModel
- **WinRing0MsrAccess.cs** — Driver removed in v2.7.0 but file remained
- **HpCmslService.cs** — Superseded by `BiosUpdateService`
- **ConfigBackupService.cs** — Duplicated by SettingsViewModel save/load

### Verified Alive (Not Deleted)
- `StartupSequencer.cs` — Used by SplashWindow.xaml.cs
- `FanCalibrationService.cs` / `FanCalibrationProfile.cs` — Used by FanCalibrationViewModel

---

## 📋 Technical Details

### EC Safety Architecture (Linux)
1. On startup, reads `/sys/class/dmi/id/product_name` to identify the laptop model
2. Checks against known unsafe EC model patterns (2025 OMEN Max series)
3. If unsafe: blocks all `WriteByte()` calls, redirects fan control to ACPI/hwmon
4. Fan speed control chain: hp-wmi WMI → ACPI profile + hwmon PWM → EC (if safe)
5. Temperature reads return null on unsafe models to prevent garbage data

### RTSS Integration (Windows)
- Uses `RTSSSharedMemoryV2` memory-mapped file for zero-overhead FPS reading
- Polls every 500ms when OSD is visible
- Provides: instant FPS, average FPS, min/max FPS, 1% low, frametime
- Gracefully falls back to GPU load display when RTSS is not installed

### Build Info
- **Version**: 2.8.0 (VERSION.txt, AssemblyVersion, FileVersion)
- **Target**: .NET 8.0, `net8.0-windows10.0.19041.0`, win-x64
- **Build**: Release, self-contained
- **New file**: `src/OmenCoreApp/Hardware/AmdGpuService.cs`
- **Deleted files**: 4 (see Dead Code Removal above)
- **Files modified**: ~20 across ViewModels, Views, Hardware, Services, Models

### Corsair HID Effect Protocol
- 65-byte report: `[0]=0x00, [1]=device-cmd, [2]=0xD0 (effect marker), [3]=effect-id`
- Effect IDs: Static (0x01), Breathing (0x02), Spectrum (0x03), Wave (0x04)
- Primary color at bytes [4-6], secondary at [7-9], period at [10], extra at [11]
- Device routing: mice → cmd 0x05, keyboards → cmd 0x09, generic → cmd 0x07
- All effects fall back to static color on device rejection

### Desktop Support Architecture
- WMI fan control uses same `HpWmiBios.SetFanLevel()` as laptop WMI backend
- Desktop EC registers are NOT accessed (no `WinRing0`/`PawnIO` needed for fans)
- Desktop RGB uses USB HID controller at VID `0x103C`, PIDs `0x84FD/84FE/8602/8603`
- 58-byte HID packets with modes: Static, Direct, Breathing, Cycle, Blinking, Wave, Radial
- Zone support: Logo, Bar, Fan, CPU, Bottom/Mid/Top Fan

---

## 📦 Release Artifacts & SHA256 Checksums

| File | SHA256 |
|------|--------|
| `OmenCoreSetup-2.8.0.exe` | `ADD02976B8AE5FF4151E169876D50A54DF60F74B02A8BA2D5FAA11BCB2213173` |
| `OmenCore-2.8.0-win-x64.zip` | `7DC97B96316FFF37444AB16D60170AF04DC7709D4BEA204CE3545289E70FAAC5` |
| `OmenCore-2.8.0-linux-x64.zip` | `D45942DE957781B9C36C7659778465E597C6788AF0BC86A28331195A8C6A45E9` |

**Verification:**
- Windows: `Get-FileHash -Algorithm SHA256 OmenCoreSetup-2.8.0.exe`
- Linux: `sha256sum OmenCore-2.8.0-linux-x64.zip`

---

## Upgrade Notes

- Existing configs are fully backward-compatible (new OSD settings default to `false`)
- AMD GPU OC requires AMD Adrenalin drivers installed (`atiadlxx.dll`)
- Display overdrive support depends on OMEN panel hardware capability
- No breaking changes to existing functionality
