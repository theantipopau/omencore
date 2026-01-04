# Changelog v2.1.0

All notable changes to OmenCore v2.1.0 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.1.0] - 2026-01-02

### Added

#### 🆕 OMEN Max 2025 Support (ThermalPolicy V2)
Enhanced support for the new OMEN Max 16/17 (2025) with RTX 50-series:
- **V2 thermal policy detection** - Auto-detects OMEN Max 2025+ systems
- **New fan reading commands** - Uses 0x37/0x38 WMI commands for V2 devices
- **Direct RPM reading** - Fallback to raw RPM values for accurate fan monitoring
- **Per-key RGB ready** - Keyboard model database includes OMEN Max per-key RGB support

#### 🔀 Independent CPU/GPU Fan Curves
Separate fan control for CPU and GPU based on individual component temperatures:
- **Visual curve editors** - Dedicated editors for CPU and GPU fan curves
- **True independent control** - Uses `SetFanLevel(fan1Level, fan2Level)` WMI
- **Temperature isolation** - CPU fan responds to CPU temp, GPU fan to GPU temp
- **Config persistence** - Curves saved and restored on startup
- **Fallback mode** - Falls back to unified control if hardware unsupported

New properties in config: `IndependentFanCurvesEnabled`, `CpuFanCurve`, `GpuFanCurve`

#### 🐧 Linux GUI (Avalonia UI)
Cross-platform graphical interface for Linux:
- **Dashboard view** - Real-time temperatures, fan speeds, CPU/GPU/RAM usage
- **Fan control view** - Custom fan curves with presets (Silent/Balanced/Performance/Aggressive)
- **System control view** - Performance modes, GPU switching, keyboard lighting
- **Settings view** - Startup options, polling interval, theme settings
- **Linux hardware service** - sysfs/hwmon temperature and fan reading
- **TOML configuration** - `~/.config/omencore/config.toml`
- **Dark OMEN theme** - Matching the Windows UI aesthetic

New project: `src/OmenCore.Avalonia/` with full MVVM architecture

#### ⚡ GPU Overclocking (NVAPI)
Full NVIDIA GPU overclocking via NVAPI SDK:
- **Core clock offset** - Adjustable from -500 to +300 MHz (laptop) / +300 MHz (desktop)
- **Memory clock offset** - Adjustable from -500 to +1500 MHz
- **Power limit control** - 50% to 125% TDP adjustment
- **UI sliders** - Full integration in System Control view
- **Config persistence** - Settings saved and reapplied on startup
- **Safety limits** - Conservative limits for laptop GPUs automatically detected
- **Status display** - Real-time GPU name and OC capability status

New files: `src/OmenCoreApp/Hardware/NvapiService.cs`, `GpuOcSettings` in AppConfig

#### 🌈 Ambient Lighting (Screen Color Sampling)
Ambilight-style screen color sync for RGB devices:
- **Edge sampling** - Captures colors from screen edges (top, bottom, left, right)
- **Zone-based** - Configurable zones per edge (1-20)
- **Saturation boost** - 0.5x to 2.0x color enhancement
- **Update rate control** - 10-60 FPS adjustable
- **Settings toggle** - Enable/disable with sliders in Settings view
- **Multi-provider support** - Works with keyboard and all RGB peripherals

New files: `ScreenColorSamplingService.cs`, `AmbientLightingSettings` in AppConfig

#### ⚡ CPU Power Limits (PL1/PL2)
Intel CPU power limit controls:
- **PL1 (Sustained)** - Long-term TDP limit (15-65W)
- **PL2 (Burst)** - Short-term turbo limit (25-115W)
- **Integration with EC** - Via PowerLimitController

#### 🐧 Linux Daemon
Full background service support for Linux:
- **Daemon mode** (`omencore-cli daemon --run`) - Background service with fan curve engine
- **systemd integration** - Auto-generated service file with `daemon --install`
- **TOML configuration** - `/etc/omencore/config.toml` with full settings
- **Automatic fan curves** - Temperature-based fan speed control with hysteresis
- **PID file & signal handling** - Graceful shutdown, config reload support
- **Security hardening** - ProtectSystem=strict, PrivateTmp, read-only home

New files:
- `src/OmenCore.Linux/Config/OmenCoreConfig.cs` - TOML configuration model
- `src/OmenCore.Linux/Daemon/FanCurveEngine.cs` - Automatic fan curve engine
- `src/OmenCore.Linux/Daemon/OmenCoreDaemon.cs` - Background service implementation

#### 📊 RTSS Integration
Real FPS monitoring via RivaTuner Statistics Server:
- **Shared memory integration** - Reads RTSS frame data without game hooks
- **Full metrics**: Instant FPS, average, min/max, 1% low, frametime
- **Process detection** - Automatically shows data for active game
- **Graceful fallback** - Works without RTSS (returns empty data)

New file: `src/OmenCoreApp/Services/RtssIntegrationService.cs`

#### 🔔 Toast Notifications
Mode change notifications for better UX:
- **Fan profile changes** - Shows toast when switching profiles
- **Performance mode changes** - Notifies on mode switch
- **GPU power changes** - Toast for power limit adjustments
- **Keyboard lighting** - Notifies on color/brightness changes
- **Auto-dismiss** - Fades out after 2.5 seconds
- **Non-intrusive** - Top-center positioning, OMEN red accent theme

New file: `src/OmenCoreApp/Services/ToastNotificationService.cs`

#### 🎮 Game Library
Multi-platform game discovery and profile management:
- **Platform scanning** - Automatic detection of games from Steam, Epic, GOG, Xbox, Ubisoft, EA
- **Library parsing** - Reads Steam manifests, Epic manifests, GOG registry, etc.
- **Profile integration** - Create/edit game profiles directly from library
- **Game launching** - Launch games via platform URLs (steam://, com.epicgames://)
- **Search & filter** - Find games by name or filter by platform

New files:
- `src/OmenCoreApp/Services/GameLibraryService.cs`
- `src/OmenCoreApp/ViewModels/GameLibraryViewModel.cs`
- `src/OmenCoreApp/Views/GameLibraryView.xaml`

### Changed

#### Configuration
- **OsdSettings** - Added `ShowModeChangeNotifications` option (default: true)
- **OsdSettings** - Added `UseRtssForFps` option (default: true)

#### Linux CLI
- **DaemonCommand** - Complete rewrite with TOML config support
- **Version** - Updated to v2.1.0
- **Tomlyn** package added for TOML parsing

### Fixed

#### � EC Fan Control Safety Allowlist
- **Issue:** Fan preset 'Max' failed with "EC write to address 0x2C is blocked for safety"
- **Root Cause:** Missing EC registers 0x2C/0x2D (XSS1/XSS2 - Fan 1/2 set speed %) from safety allowlist. These are used by OmenMon-style fan control on newer OMEN models (2022+)
- **Fix:** Added 0x2C, 0x2D, 0x2E, 0x2F to `AllowedWriteAddresses` in both EC backends
- **Files changed:** `PawnIOEcAccess.cs`, `WinRing0EcAccess.cs`
#### 🐧 Linux CLI Crash on Any Command
- **Issue:** Running any command (`battery status`, `fan`, etc.) threw `ArgumentException: An item with the same key has already been added. Key: --version`
- **Root Cause:** System.CommandLine automatically adds `--version` to `RootCommand`. We were manually handling `--version` before parsing but the conflict occurred when the parser initialized. Additionally, global `--json` option conflicted with local `--json` in StatusCommand, and `-v` for verbose could conflict with `-V` for version.
- **Fix:** Removed duplicate global `--json` option (StatusCommand has its own), simplified verbose to `--verbose` only (no `-v` alias), kept manual `--version` handling before parsing
- **Files changed:** `src/OmenCore.Linux/Program.cs`

#### 🔄 HardwareWorker SafeFileHandle Error Spam
- **Issue:** HardwareWorker.log filled with repeated "Error updating Primary: Cannot access a disposed object. Object name: 'SafeFileHandle'" when storage devices go to sleep
- **Root Cause:** LibreHardwareMonitor throws ObjectDisposedException when storage devices disconnect or enter sleep mode - this is normal/benign behavior
- **Fix:** Added error rate-limiting to only log these errors once per hour per hardware component, filtering known benign disposed object errors
- **Files changed:** `src/OmenCore.HardwareWorker/Program.cs`

#### 🔄 Multiple WMI BIOS Heartbeat Timers
- **Issue:** Log showed "✓ WMI BIOS heartbeat started" 3 times on startup, indicating 3 separate HpWmiBios instances each starting their own heartbeat timer
- **Root Cause:** HpWmiBios was being instantiated in MainViewModel, WmiFanController, and CapabilityDetectionService - each starting its own 60-second heartbeat timer
- **Fix:** Added singleton pattern to heartbeat timer - only one heartbeat runs globally even with multiple HpWmiBios instances
- **Files changed:** `src/OmenCoreApp/Hardware/HpWmiBios.cs`

#### 🔒 Thread Safety Improvements
- **Issue:** Race conditions in tray icon updates and OMEN key debouncing could cause inconsistent behavior
- **Fix:**
  - `TrayIconService._isUpdatingIcon` changed from `bool` to `int` with `Interlocked.CompareExchange`
  - `TrayIconService._lastIconUpdate` changed to `_lastIconUpdateTicks` (long) with `Interlocked.Exchange`
  - `OmenKeyService._lastKeyPress` changed to `_lastKeyPressTicks` with `Interlocked` operations
  - Both keyboard hook callback and WMI handler now use thread-safe debouncing
- **Files changed:** `TrayIconService.cs`, `OmenKeyService.cs`

#### 🔧 Tray Icon Flicker During Brightness Key Presses
- **Issue:** Pressing brightness keys caused the tray icon to flicker/redraw rapidly
- **Fix:** Added 500ms minimum interval between tray icon updates to prevent flicker during system events
- **Files changed:** `TrayIconService.cs`

#### 🧹 Memory Leak in OMEN Key Service
- **Issue:** WMI event handler was not unsubscribed before disposing the watcher, causing potential memory leak
- **Fix:** Added `_wmiEventWatcher.EventArrived -= OnWmiEventArrived` in `StopInterception()` before disposal
- **Files changed:** `OmenKeyService.cs`

#### 🎚️ Fan Smoothing UI Alignment
- **Issue:** Fan smoothing labels (Duration/Step) were misaligned in the Fan Control view
- **Fix:** Reorganized layout to use proper vertical StackPanel instead of overlapping elements with margin hacks
- **Files changed:** `FanControlView.xaml`

#### 💥 Startup Crash Due to Missing XAML Resources
- **Issue:** App crashed on startup with `XamlParseException` - missing StaticResource definitions
- **Fix:** Added missing resources to `ModernStyles.xaml` and `App.xaml`:
  - `Headline` text style
  - `BooleanToVisibilityConverter` (alias for `BoolToVisibility`)
  - `ModernListView` style for ListView controls
- **Files changed:** `ModernStyles.xaml`, `App.xaml`

#### 🐧 Cross-Platform Registry Warning
- **Issue:** `GameLibraryService` used Registry calls that caused warnings on Linux builds
- **Fix:** Added `if (!OperatingSystem.IsWindows()) return null;` guard before Registry operations
- **Files changed:** `GameLibraryService.cs`

#### 🖥️ Remote Desktop (RDP) Window Activation
- **Issue:** Window would unexpectedly open/activate when starting an RDP session
- **Fix:** Added `SessionSwitch` event handling to suppress window activation during:
  - Remote connect/disconnect events
  - Session lock/unlock events
  - Console connect/disconnect events
- **Grace period:** 2-second delay after session unlock before allowing activation
- **Files changed:** `App.xaml.cs`, `MainViewModel.cs`

#### 🔍 OMEN Model Detection for Replacement Motherboards
- **Issue:** HP laptops with replacement motherboards show codenames like "Thetiger OMN" instead of the actual model name, causing OmenCore to report "Unknown (non-OMEN device?)"
- **Fix:** 
  - Added Product ID-based detection for OMEN devices (patterns: 8Axx, 8Bxx, 8Cxx, 88xx)
  - Added recognition for HP gaming codenames: THETIGER, DRAGONFIRE, SHADOWCAT, VICTUSDRAGON
  - GPU mode switching now works correctly with motherboard codenames
- **Files changed:** `CapabilityDetectionService.cs`, `SystemInfoService.cs`, `GpuSwitchService.cs`

#### 💾 Fan/Performance Mode Settings Not Persisting After Game Exit
- **Issue:** When game profiles restored "default" settings after game exit, the user's fan preset and performance mode were being overwritten in the config file
- **Fix:** 
  - `RestoreDefaultSettingsAsync()` now restores settings temporarily without saving to config
  - Added `SelectPerformanceModeWithoutSave()` method to prevent config overwrites
- **Files changed:** `MainViewModel.cs`, `SystemControlViewModel.cs`

#### 🎨 Keyboard RGB Lighting Not Responding (EC Backend Not Used)
- **Issue:** Even with "Enable experimental EC keyboard control" enabled, the WMI BIOS backend was still being preferred in Auto mode, causing RGB lighting to not work on some models
- **Fix:**
  - Modified `BackendType` property to check `ExperimentalEcKeyboardEnabled` first when in Auto mode
  - EC backend is now properly preferred when the experimental flag is enabled
  - Added runtime update of `PawnIOEcAccess.EnableExperimentalKeyboardWrites` flag when setting changes
- **Files changed:** `KeyboardLightingService.cs`, `SettingsViewModel.cs`

#### 🔋 Battery Charge Limit Not Working (80% Limit Ignored)
- **Issue:** Enabling the 80% battery charge limit had no effect - the battery would still charge to 100%. The implementation only logged a message but never called the HP WMI BIOS
- **Fix:**
  - `ApplyBatteryChargeLimit()` now properly calls `HpWmiBios.SetBatteryCareMode()`
  - Added `HpWmiBios` reference to `SettingsViewModel` constructor
  - Added proper error handling and logging for WMI failures
- **Files changed:** `SettingsViewModel.cs`, `MainViewModel.cs`

#### 💥 CPU Undervolt Toggle Crash
- **Issue:** Toggling CPU undervolting in Settings or System Control crashed the app
- **Fix:**
  - Added null coalescing for `config.Undervolt ?? new UndervoltPreferences()`
  - Wrapped undervolt operations in try-catch to prevent app termination
- **Files changed:** `SystemControlViewModel.cs`

#### 💥 Per-Core Undervolt Toggle Crash
- **Issue:** Clicking "Enable Per-Core Undervolting" toggle crashed the app immediately
- **Fix:**
  - Added null safety for `config.Undervolt.DefaultOffset ?? new UndervoltOffset()`
  - App no longer crashes when undervolt config section is missing or incomplete
- **Files changed:** `SystemControlViewModel.cs`

#### 🔧 TCC Offset Not Working (Silent Failure)
- **Issue:** Setting TCC offset via slider had no effect on CPU throttling - no feedback to user
- **Fix:**
  - Added MSR read-back verification after write to confirm offset was applied
  - Shows success/failure dialog with HVCI/Secure Boot hint on failure
  - Users now know if TCC offset actually took effect
- **Files changed:** `SystemControlViewModel.cs`

#### 🔧 Fan Cleaner PawnIO Error After Refresh
- **Issue:** Fan cleaner worked initially but errored after PawnIO refresh: "Failed to enable max fan via EC access"
- **Fix:**
  - Added `RefreshBackend()` method to re-detect EC access backends
  - Called at start of `StartCleaningAsync()` to pick up newly-installed PawnIO
- **Files changed:** `FanCleaningService.cs`

#### 🔧 AC/Battery Profile Switching Not Working
- **Issue:** Power automation toggle was enabled in UI but profiles didn't switch when plugging/unplugging
- **Fix:**
  - `PowerAutomationService` now receives UI toggle changes at runtime
  - Injected service into `SettingsViewModel` for direct property sync
  - Preset changes (fan mode, performance mode) also sync immediately
- **Files changed:** `SettingsViewModel.cs`, `MainViewModel.cs`

#### 🎯 Hotkey OSD Popup Position Bug
- **Issue:** HotKey OSD popup didn't fully appear on bottom right initially, fixed after subsequent presses
- **Fix:**
  - Window positioning now deferred using `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)`
  - `ActualWidth`/`ActualHeight` are now properly calculated before positioning
  - Added fallback dimensions and bounds checking
- **Files changed:** `HotkeyOsdWindow.xaml.cs`

#### 🐧 Linux CLI Crash on Startup (Duplicate --version Option)
- **Issue:** Running `omencore-cli` on Linux crashed immediately with: `System.ArgumentException: An item with the same key has already been added. Key: --version`
- **Cause:** Code was adding a custom `--version` option, but System.CommandLine's `RootCommand` already has a built-in `--version` option by default
- **Fix:** Removed the redundant custom version option - the built-in one now works correctly
- **Files changed:** `src/OmenCore.Linux/Program.cs`

#### 🔑 Calculator Key Triggering OMEN Key Action
- **Issue:** Pressing Calculator key launches OmenCore instead of the calculator when OMEN key interception is enabled
- **Cause:** Calculator key sends VK_LAUNCH_APP2 (0xB7) same as OMEN key, and previous code accepted ANY scan code with this VK
- **Fix:** Now requires scan code to match known OMEN scan codes; unknown scan codes (like Calculator) are rejected
- **Files changed:** `OmenKeyService.cs`

#### 💾 Fan Preset Not Persisting Across Restarts
- **Issue:** Selected fan preset (Max, Silent, etc.) not restored after app restart - always shows "No saved fan preset to restore"
- **Cause:** Race condition - `ConfigService.Load()` reads from disk while `Config` property holds in-memory changes, potentially overwriting unsaved data
- **Fix:** Changed `SaveLastPresetToConfig()` and related methods to use `_configService.Config` (in-memory) instead of `Load()` (disk read)
- **Files changed:** `FanControlViewModel.cs`

#### ⚙️ Settings Not Persisting (Update Interval, Pre-releases) ([#20](https://github.com/theantipopau/omencore/issues/20))
- **Issue:** Update check interval and "Include pre-release versions" settings reset to defaults after restart
- **Cause:** `LoadSettings()` wasn't mapping `CheckIntervalHours` to `UpdateCheckIntervalIndex`, and `IncludePreReleases` wasn't loaded
- **Fix:** Added proper mapping between hours (6/12/168) and dropdown index (0-3), added IncludePreReleases to load/save
- **Files changed:** `SettingsViewModel.cs`, `UpdatePreferences.cs`

#### 🪟 Minimize Goes to Tray Instead of Taskbar ([#20](https://github.com/theantipopau/omencore/issues/20))
- **Issue:** Clicking minimize button hid the window completely instead of minimizing to taskbar
- **Cause:** `MinimizeButton_Click` called `Hide()` and `StateChanged` event also called `Hide()` on minimize
- **Fix:** Changed minimize button to use `WindowState.Minimized`, removed Hide() call from StateChanged event
- **Files changed:** `MainWindow.xaml.cs`

#### 🔥 Fan Preset Defaults to Extreme Instead of Auto ([#20](https://github.com/theantipopau/omencore/issues/20), [#23](https://github.com/theantipopau/omencore/issues/23))
- **Issue:** On startup, fan preset always showed "Extreme" selected even when user had set "Auto" or "Quiet"
- **Cause:** Constructor set `SelectedPreset = FanPresets[1]` (Extreme) instead of index 2 (Auto)
- **Fix:** Changed default selection to `FanPresets[2]` (Auto), added "Extreme" preset handling in restore logic
- **Files changed:** `FanControlViewModel.cs`, `MainViewModel.cs`

#### 🔒 Auto-Update Blocked by Missing SHA256 ([#20](https://github.com/theantipopau/omencore/issues/20))
- **Issue:** Auto-update downloads failed with "SHA256 verification failed" when GitHub release had no hash file
- **Cause:** SHA256 verification was mandatory - missing hash was treated as a security failure
- **Fix:** Made SHA256 optional - downloads proceed with warning if hash unavailable
- **Files changed:** `AutoUpdateService.cs`

#### ⌨️ Ctrl+Shift+O Hotkey Not Working on Startup ([#19](https://github.com/theantipopau/omencore/issues/19))
- **Issue:** Global hotkey didn't work until user manually opened and closed the window once
- **Cause:** `RegisterHotKey` requires a window handle, which isn't available until window is shown
- **Fix:** Added `Show()/Hide()` sequence during startup to create window handle before hotkey registration
- **Files changed:** `App.xaml.cs`

#### 🔄 Single Instance Doesn't Bring Window to Front ([#19](https://github.com/theantipopau/omencore/issues/19))
- **Issue:** Opening OmenCore when already running did nothing - existing window stayed minimized/hidden
- **Cause:** Mutex prevented duplicate instance but didn't activate the existing window
- **Fix:** Added `BringExistingInstanceToFront()` using P/Invoke (FindWindow, SetForegroundWindow, ShowWindow)
- **Files changed:** `App.xaml.cs`

#### 🌈 Keyboard Backlight Turns On Unwanted After Restart ([#20](https://github.com/theantipopau/omencore/issues/20))
- **Issue:** Keyboard backlight would turn on after restart even if user had turned it off
- **Cause:** Color restoration always happened regardless of user's backlight preference
- **Fix:** Added `BacklightWasEnabled` config property, only restore colors if user had backlight on
- **Files changed:** `AppConfig.cs`, `LightingViewModel.cs`

#### 🔋 Battery Care 80% Limit Not Restored on Startup ([#20](https://github.com/theantipopau/omencore/issues/20))
- **Issue:** Battery charge limit setting was saved but not reapplied when OmenCore started
- **Cause:** `RestoreSettingsOnStartupAsync()` only restored fan preset and GPU boost, not battery care
- **Fix:** Added battery care restoration with retry logic to startup sequence
- **Files changed:** `MainViewModel.cs`

#### 📊 Quick Profiles UI Shows Wrong Selection ([#20](https://github.com/theantipopau/omencore/issues/20))
- **Issue:** General view profile buttons didn't reflect the actual active profile
- **Cause:** `DetermineActiveProfile()` only checked runtime mode, not saved config; `FanService` didn't update `_currentFanMode` when applying presets
- **Fix:** Enhanced `DetermineActiveProfile()` to check saved preset name, updated `ApplyPreset()` to set `_currentFanMode`
- **Files changed:** `GeneralViewModel.cs`, `FanService.cs`

#### ⚡ Power Automation Presets Don't Match Advanced Tab ([#20](https://github.com/theantipopau/omencore/issues/20))
- **Issue:** Power automation fan preset dropdown showed "Performance" but Advanced tab has "Extreme"
- **Cause:** `FanPresetOptions` was hardcoded as `["Auto", "Quiet", "Performance", "Max"]` instead of actual presets
- **Fix:** Updated to `["Auto", "Quiet", "Extreme", "Max"]` to match built-in presets
- **Files changed:** `SettingsViewModel.cs`

#### 🛡️ SDK Services Enabled by Default (Slow Startup) ([#18](https://github.com/theantipopau/omencore/issues/18))
- **Issue:** Corsair, Logitech, and Razer SDK services were enabled by default, causing slow startup for users without those peripherals
- **Cause:** Default values in `FeaturePreferences` were all `true`
- **Fix:** Changed Corsair, Logitech, and Razer integration defaults to `false`
- **Files changed:** `FeaturePreferences.cs`

#### 🚫 Bloatware Optimizer Detects OmenCore as Bloatware
- **Issue:** OmenCore appeared in its own bloatware list as a recommended removal
- **Cause:** Detection logic matched any "Omen" process/startup/task including OmenCore itself
- **Fix:** Added explicit exclusions for "OmenCore" in startup, process, and task detection
- **Files changed:** `BloatwareManagerService.cs`

### Safety

#### ⚠️ EC Write Safety (Pre-existing)
OmenCore includes an **EC write address allowlist** that prevents accidental writes to dangerous registers:
- **Allowed addresses**: Fan control registers only (0x44-0x4D, 0xB0-0xB1, 0xCE-0xCF)
- **Blocked addresses**: VRM control, battery charger, keyboard backlight (varies by model)
- **Why keyboard EC is blocked**: Addresses 0xB2-0xBE caused hard crashes on OMEN 17-ck2xxx
- **Safe alternative**: Use WMI BIOS `SetColorTable()` for keyboard lighting

This safety measure protects against hardware damage from incorrect EC writes.

### 🔀 Independent CPU/GPU Fan Curves - How To Use

**New Feature:** You can now set separate fan curves for CPU and GPU fans, each responding to their respective component temperatures.

#### Why This Matters
- **Better cooling efficiency** - GPU fan doesn't spin up when only CPU is hot
- **Reduced noise** - Keep GPU fan quiet during CPU-only workloads
- **Fine-tuned control** - Optimize each fan for its specific cooling requirements

#### How To Use
1. Go to **Fan & Thermal Control** view
2. Enable **"Independent CPU/GPU Curves"** toggle in the settings area
3. Two curve editors will appear:
   - **CPU Fan Curve** - Controls fan based on CPU temperature
   - **GPU Fan Curve** - Controls fan based on GPU temperature
4. Edit each curve independently with the visual editor
5. Click **Apply** to activate both curves

#### Technical Details
- Uses `SetFanLevel(fan1Level, fan2Level)` WMI method for true independent control
- CPU temperature drives the CPU/system fan
- GPU temperature drives the GPU/exhaust fan
- Curves are saved to config and restored on startup
- Fallback to unified mode if hardware doesn't support dual control

### Known Limitations

#### 🔌 External Undervolt Controller Detection
When "External controller detected: HP OmenCap (DriverStore)" appears:
- HP OmenCap is an HP-provided undervolt controller that may conflict with OmenCore
- If undervolt controls don't work, the CPU may have voltage locked by BIOS (Intel Plundervolt mitigation)
- OmenCore respects external controllers by default (can be changed in settings)

#### ⌨️ Keyboard RGB on Replacement Motherboards (Thetiger OMN, etc.)
Some HP laptops with replacement motherboards (showing codenames like "Thetiger OMN" instead of product names) may have issues with keyboard RGB:
- **WMI BIOS backend may not work** - Colors set but don't persist after sleep/restart
- **Try EC backend** - Go to Settings → Hardware → Keyboard Backend → EC (experimental)
- **OmenMon compatibility** - If RGB works in OmenMon but not OmenCore, EC backend is likely needed
- **Enable experimental EC keyboard** - Settings → Hardware → Enable experimental EC keyboard control

#### 📊 OSD Overlay Toggle Hotkey
The OSD overlay hotkey (default: Ctrl+Shift+F12) only works if OSD is enabled:
- **Enable OSD first** - Go to Settings → On-Screen Display → Enable "Show OSD overlay"
- **Then hotkey works** - After enabling, the hotkey toggles overlay visibility
- **Per-key RGB** - Per-key keyboard RGB is not yet implemented (4-zone only)

### Technical

#### New Dependencies
- `Tomlyn 0.19.0` (Linux only) - TOML configuration parsing

#### New Classes
| File | Purpose |
|------|---------|
| `OmenCoreConfig.cs` | TOML config model with fan curves, performance, keyboard settings |
| `FanCurveEngine.cs` | Temperature-based fan control with hysteresis and smooth transitions |
| `OmenCoreDaemon.cs` | Full daemon with PID file, signal handling, config watching |
| `RtssIntegrationService.cs` | RTSS shared memory integration for FPS monitoring |
| `ToastNotificationService.cs` | WPF toast notifications for mode changes |
| `GameLibraryService.cs` | Multi-platform game discovery (Steam/Epic/GOG/Xbox/Ubisoft/EA) |
| `GameLibraryViewModel.cs` | Game Library UI with scan, filter, profile management |
| `GameLibraryView.xaml` | Game Library visual interface |

---

## Installation (Linux)

### Quick Start
```bash
# Extract and install
sudo cp omencore-cli /usr/local/bin/
sudo chmod +x /usr/local/bin/omencore-cli

# Install systemd service
sudo omencore-cli daemon --install

# Start service
sudo systemctl start omencore
```

### Configuration
Default config is created at `/etc/omencore/config.toml`:

```toml
[general]
poll_interval_ms = 2000
log_level = "info"

[fan]
profile = "auto"  # auto, silent, balanced, gaming, max, custom
boost = false
smooth_transition = true

[fan.curve]
enabled = false
hysteresis = 3
[[fan.curve.points]]
temp = 40
speed = 20
# ... more points

[performance]
mode = "balanced"

[keyboard]
enabled = true
color = "FF0000"
brightness = 100

[startup]
apply_on_boot = true
restore_on_exit = true
```

### Commands
```bash
# Daemon management
omencore-cli daemon --status          # Check status
omencore-cli daemon --install         # Install systemd service
omencore-cli daemon --start           # Start via systemd
omencore-cli daemon --stop            # Stop via systemd
omencore-cli daemon --run             # Run in foreground
omencore-cli daemon --generate-config # Print default TOML config
omencore-cli daemon --uninstall       # Remove service
```

---

## Current Status

- **Version:** 2.1.0
- **Branch:** `main`
- **Build:** ✅ Succeeded (all 5 projects)
- **Tests:** ✅ 66/66 passing
