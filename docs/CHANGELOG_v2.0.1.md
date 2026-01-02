# Changelog v2.0.1-beta

All notable changes to OmenCore v2.0.1 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.0.1-beta] - 2026-01-02

### Added

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
NVIDIA GPU control via NVAPI SDK integration:
- **Core clock offset** - Adjust GPU core clock (-500 to +200 MHz)
- **Memory clock offset** - Adjust VRAM clock (-500 to +500 MHz)
- **Power limit control** - Set GPU power limit (50-115%)
- **Laptop detection** - Conservative limits for mobile GPUs
- **Profile support** - Save/load GPU OC profiles

New file: `src/OmenCoreApp/Hardware/NvapiService.cs`

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
- **Version** - Updated to v2.0.1-beta
- **Tomlyn** package added for TOML parsing

### Fixed

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

## Development Progress

| Phase | Status | Progress |
|-------|--------|----------|
| 1. Foundation & Quick Wins | ✅ Complete | 16/20 |
| 2. System Optimizer | ✅ Complete | 35/35 |
| 3. RGB Overhaul | ✅ Complete | 22/24 |
| 4. Linux Support | ✅ Complete | 11/12 |
| 5. Advanced Features | ✅ Complete | 15/15 |
| 6. Polish & Release | 🔧 In Progress | 22/24 |

**Overall: 121/130 tasks (93%)**

---

## Current Status

- **Version:** 2.0.1-beta
- **Branch:** `main`
- **Build:** ✅ Succeeded (all 5 projects)
- **Tests:** ✅ 66/66 passing
- **Windows:** OmenCoreSetup-2.0.1-beta.exe (100 MB)
- **Linux:** OmenCore-2.0.1-beta-linux-x64.zip (5.7 MB)
