# Changelog

All notable changes to OmenCore will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.3.2] - 2026-01-14 - Critical Safety & Bug Fix Release 🛡️

**Desktop safety protection + Linux GUI fix + Multiple bug fixes**

### 🛡️ CRITICAL: Desktop PC Safety
- **Desktop systems now require explicit user confirmation before enabling fan control**
- OmenCore is designed for OMEN LAPTOPS - desktop EC registers are completely different
- Warning upgraded from "experimental" to "NOT SUPPORTED - USE AT YOUR OWN RISK"
- Fan control disabled by default on desktop systems
- Added blocking confirmation dialog for desktop users

### 🐧 Linux GUI Crash Fix
- **Fixed**: GUI crashed on startup with "StaticResource 'DarkBackgroundBrush' not found"
- Changed `StaticResource` to `DynamicResource` in all Avalonia XAML views
- Fixes resource loading order issue on Debian 13 and Ubuntu 24.04

### 🔧 Bug Fixes
- **OSD Mode Update**: OSD now properly updates when switching performance/fan modes
- **Fan Control Fallback**: Improved V2 command fallback for OMEN Max/17-ck models
- **Window Corners**: Improved rounded corner rendering with explicit clip geometry
- **Window Size**: Reduced minimum size from 900×600 to 850×550

### 📋 Known Issues
- FPS counter is estimated from GPU load (accurate FPS via D3D11 hook in v2.4.0)
- Some OMEN Max/17-ck models may still have fan control issues - we need more testing data

---

## [2.3.1] - 2026-01-12 - Critical Bug Fix Release 🔥

**Thermal Shutdown Fix + Fan Control Improvements + OSD Enhancements**

### 🔴 Critical: Battlefield 6 Thermal Shutdown Fix
- **Fixed**: Storage drive sleep causing SafeFileHandle disposal crash → thermal shutdown during gaming
- RTX 4090 at 87°C: when storage drives slept, temp monitoring crashed, fans couldn't respond → shutdown
- Added per-device exception isolation: storage failures no longer affect CPU/GPU monitoring
- Prevents thermal shutdowns during extended gaming sessions

### 🔴 Critical: Fan Drops to 0 RPM Fix
- **Fixed**: Fans would boost high then drop to 0 RPM at 60-70°C after thermal protection
- Affected Victus 16 and OMEN Max 16 laptops with aggressive BIOS fan control
- Added "safe release" temperature (55°C) and minimum fan floor (30%)
- Prevents BIOS from stopping fans when system is still gaming-warm

### 🌡️ Adjustable Thermal Protection Threshold
- **NEW**: Thermal protection threshold now configurable 70-90°C (default 80°C)
- Advanced users can increase if their laptop handles heat better
- Setting in: Settings → Fan Hysteresis → Thermal Protection Threshold

### 📊 OSD Network Traffic Monitoring
- **Upload speed** display in Mbps (blue arrow ↑)
- **Download speed** display in Mbps (green arrow ↓)
- Auto-detects active network interface (Ethernet/WiFi)
- Updates every 5 seconds alongside ping monitoring

### 📐 OSD Horizontal Layout Option
- Added layout toggle in Settings → OSD → Horizontal Layout
- Stores preference in config (full XAML implementation coming in v2.3.2)

### 📐 Window Sizing for Multi-Monitor
- Reduced minimum window size from 1100×700 to 900×600
- Works better with smaller/vertical secondary monitors

### 🐧 Linux Kernel 6.18 Notes
- Added documentation for upcoming HP-WMI driver improvements
- Better native fan curve control via sysfs

[Full v2.3.1 Changelog](docs/CHANGELOG_v2.3.1.md)

---

## [2.3.0] - 2025-01-12 - Major Feature Release 🚀

**Safety, Diagnostics, and Enhanced Linux Support**

### 🛡️ Fan Curve Safety System
- **Real-time validation** in fan curve editor detects dangerous configurations
- **Hardware watchdog** monitors for frozen temperature sensors (auto-sets 100% if frozen)
- **Curve recovery** system auto-reverts to last-known-good preset on sustained overheating
- Visual warning banners with specific recommendations

### 📦 Profile Import/Export
- **Unified `.omencore` format** for complete configuration backup
- Export fan presets, performance modes, RGB presets, and settings
- Selective import (choose which components to merge)

### 🔋 Custom Battery Thresholds
- **Adjustable charge limit slider** (60-100%, previously fixed at 80%)
- Recommendations: 60-70% for longevity, 80% for daily use, 100% for travel
- Real-time threshold application via HP WMI BIOS

### 🔄 Auto-Update Check
- **Non-intrusive GitHub Releases API check** (once per session)
- Privacy-respecting, no telemetry
- Shows update availability in status bar (UI integration pending)

### 📊 Diagnostics Export
- **One-click ZIP bundle** with logs, config, system info, hardware status
- Ready to attach to GitHub issues

### 🐧 Linux Improvements
- **Enhanced 2023+ OMEN support** with HP-WMI thermal profile switching
- `omencore-cli diagnose --report` generates pasteable GitHub issue templates
- Direct fan control via `fan1_output`/`fan2_output` (when available)
- Improved detection: hp-wmi only reports available when control files exist
- Thermal profiles work even without direct fan PWM access

[Full v2.3.0 Changelog](docs/CHANGELOG_v2.3.0.md)

---

## [2.2.3] - Not Released (Merged into v2.3.0)

### 🐛 Bug Fixes
- **Critical: Fan Speed Drops to 0 RPM** - Fixed fans dropping to minimum when temp exceeded curve
  - Curve fallback now uses highest fan% instead of lowest as safety measure
  - Prevents thermal shutdowns when temperature exceeds defined curve points
- **Fan Diagnostics: Curve Override** - Test speeds no longer get overridden by curve engine
  - Added diagnostic mode that suspends curve during fan testing
- **Fan Diagnostics: 100% Not Max** - Setting 100% now uses SetFanMax for true maximum RPM
- **Fan Diagnostics: UI Not Updating** - Fixed display not refreshing after test completion

### 🎨 Linux GUI Overhaul
- **Theme System** - Comprehensive new OmenTheme.axaml with 300+ style definitions
  - Card styles: `.card`, `.cardInteractive`, `.surfaceCard`, `.statusCard`
  - Button variants: `.primary`, `.secondary`, `.danger`, `.ghost`, `.iconButton`
  - Text hierarchy: `.pageHeader`, `.sectionHeader`, `.subsectionHeader`, `.caption`
  - Navigation styles with active state tracking
  - Smooth hover transitions on interactive elements
- **Dashboard** - Complete redesign matching Windows version
  - Session uptime tracking with peak temperature display
  - Quick status bar showing fans, performance mode, power source
  - Hardware summary cards for CPU, GPU, Fans, Memory
  - Throttling warning banner when thermal throttling detected
- **Fan Control** - Enhanced fan curve editor
  - Real-time status cards with centered layout
  - Save preset and Emergency Stop buttons
  - Hysteresis setting for curve stability
- **System Control** - Visual performance mode selector
  - 4-column button grid with emoji icons and descriptions
  - Current mode indicator with accent highlight
- **Settings** - Reorganized with card-based dark panels
  - Section icons with emojis
  - About section with version and GitHub link
- **MainWindow** - Improved sidebar navigation
  - System status panel showing Performance/Fan modes
  - Version display and connection indicator
  - "Linux Edition" branding

### 🐧 Linux CLI
- **New: Diagnose command** - `omencore-cli diagnose` prints kernel/modules/sysfs status and next-step recommendations
- **Improved: 2023+ model detection** - More accurate `hp-wmi` detection and fewer misleading EC availability reports

### 📝 Documentation
- **Smart App Control** - Added workarounds for Windows 11 Smart App Control blocking installer

---

## [2.2.2] - 2026-01-10

### 🐛 Bug Fixes
- **Critical: Temperature Monitoring Freezes (#39, #40)** - Fixed temps getting stuck causing fan issues
  - Added staleness detection to HardwareWorker and client
  - Worker tracks consecutive identical readings and marks sample as stale
  - Client auto-restarts worker when stale data detected for 30+ seconds
  - Prevents fans from staying at high RPM due to frozen temperature readings
  - Prevents thermal throttling caused by fans not responding to heat

### ⚠️ Known Issues
- **OMEN 14 Transcend** - Power mode and fan behavior may be erratic (under investigation)
- **2023 XF Model** - Keyboard lighting requires OMEN Gaming Hub installed
- **Windows Defender** - May flag as `Win32/Sonbokli.A!cl` (ML false positive, common for GitHub projects)

---

## [2.2.1] - 2026-01-08

### ✨ New Features
- **EC Reset to Defaults** - Added option to reset EC to factory state
  - New "Reset EC" button in Settings → Hardware Driver section
  - Resets fan speed overrides, boost mode, BIOS control flags, and thermal timers
  - Use this if BIOS displays show stuck/incorrect fan values after using OmenCore
  - Shows confirmation dialog with explanation of what will be reset

### 🐛 Bug Fixes
- **Thermal Protection Logic (#32)** - Fixed thermal protection reducing fan speed instead of boosting
  - No longer drops from 100% to 77% when temps hit warning threshold
  - Correctly restores original fan mode/preset after thermal event (not always "Quiet")
  - Remembers pre-thermal state: Max mode stays Max, custom presets stay custom
- **Tray Menu Max/Auto Not Working (#33)** - Fixed system tray fan mode buttons
  - "Max" now correctly enables SetFanMax for true 100% fan speed
  - "Auto" properly enables BIOS-controlled automatic fan mode
  - "Quiet" correctly applies quiet/silent mode
- **OMEN Max 16 Light Bar Zone Order** - Fixed inverted RGB zones
  - Added "Invert RGB Zone Order" setting in Settings → Hardware
  - Enable for OMEN Max 16 where light bar zones run right-to-left
  - Zone 1 = Right, Zone 4 = Left when inverted
- **CPU Temp Stuck at 0°C (#35)** - Improved temperature sensor fallback
  - Better detection of alternative temperature sensors when primary fails
  - Auto-reinitialize hardware monitor after consecutive zero readings
- **CPU Temp Always 96°C (#36)** - Fixed TjMax being displayed instead of current temp
  - Added validation to detect stuck-at-TjMax readings
  - Automatically switches to alternative sensor when primary reports TjMax
- **Temperature Freeze When Drives Sleep** - Fixed temps freezing after SafeFileHandle error
  - Storage drives going to sleep no longer freeze all temperature monitoring
  - HardwareWorker now catches disposed object errors at visitor level
  - Other sensors continue updating when one hardware device fails

### ⚠️ Known Issues
- **OMEN Max 16 Keyboard Lighting** - The RGB controls only affect the front light bar, not the keyboard
  - Hardware limitation: OMEN Max 16 keyboard uses single-color white/amber backlight (Fn+F4)
  - RGB section in OmenCore controls the 4-zone front light bar only
- **Linux: Fedora 43+ ec_sys module missing** - Use `hp-wmi` driver as alternative or build module from source

---

## [2.2.0] - 2026-01-07

### 📦 Downloads
| File | SHA256 |
|------|--------|
| OmenCoreSetup-2.2.0.exe | `B4982315E979D8DE38471032A7FE07D80165F522372F5EFA43095DE2D42FF56B` |
| OmenCore-2.2.0-win-x64.zip | `542D65C5FD18D03774B14BD0C376914D0A7EE486F8B12D841A195823A0503288` |
| OmenCore-2.2.0-linux-x64.zip | `ADBF700F1DA0741D2EE47061EE2194A031B519C5618491526BC380FE0370F179` |

### ✨ New Features
- **GPU OC Profiles** - Save and load GPU overclock configurations
  - Create named profiles with core clock, memory clock, and power limit settings
  - Quick profile switching via dropdown
  - Profiles persist across app restarts
  - Delete unwanted profiles with one click
- **Fan Profile Persistence** - Custom fan curves now save automatically
  - Custom curves persist to config file when applied
  - Restored on app startup with "Custom" or "Independent" presets
- **Linux Auto Mode Fix** - Improved automatic fan control restoration
  - Full EC register reset sequence (BIOS control, fan state, boost, timer)
  - HP-WMI driver support as fallback for newer models
  - Proper cleanup of manual speed registers
- **Dashboard UI Enhancements** - Improved monitoring dashboard with at-a-glance status
  - Quick Status Bar: Fan RPMs, Performance Mode, Fan Mode, Power status
  - Session Uptime tracking (updates every second)
  - Peak Temperature tracking (highest CPU/GPU temps this session)

### ⚡ Performance
- **Lazy-Load Peripheral SDKs** - Corsair, Logitech, and Razer SDKs only load when explicitly enabled
  - Faster startup for users without these peripherals
  - Enable in Settings → Features when you have Corsair/Logitech/Razer devices
  - Reduces memory footprint when peripherals are disabled

### 🐛 Bug Fixes
- **Fan Always On Fix** - Auto mode now properly lets BIOS control fans
  - Fixed issue where fans never stopped even at idle/low temperatures
  - Auto/Default presets now call RestoreAutoControl() to reset fan levels to 0
  - BIOS can now spin down fans when system is cool (fixes Reddit report: OMEN 17 13700HX fans always running)
- **Fan Curve Editor Crash** - Fixed `ArgumentException` when dragging points beyond chart bounds (Issue #30)
- **Fan Curve Mouse Release** - Fixed cursor not releasing drag point when moving outside chart area or releasing mouse button
- **Per-Core Undervolt Crash** - Fixed missing `SurfaceBrush` resource causing XAML parse exception (Issue #31)
- **Animation Parse Error** - Fixed invalid `FanSpinStoryboard` that caused XAML parse errors on startup
- **OMEN Key False Trigger** - Fixed window opening when launching Remote Desktop or media apps (VK_LAUNCH_APP1 scan code validation)

### ⚡ Performance & Memory
- **QuickPopup Memory Leak** - Fixed timer event handlers not unsubscribed on window close
- **Thermal Sample Trimming** - Optimized O(n²) removal loop to single-pass calculation
- **Exception Logging** - Added logging to 10+ empty catch blocks for better debugging

### 🔍 Known Issues Under Investigation
- **Fan Max Mode Cycling** - Fan speed cycling between high/low in Max mode (needs more logs to diagnose)
- **dGPU Sleep Prevention** - Constant polling may prevent NVIDIA GPU sleep causing battery drain
- **Fan Speed Throttling** - Max fan speed may decrease under heavy load (6300→5000 RPM reported)

### 🔐 Security & Stability
- **Named Pipe Security** - Added `PipeOptions.CurrentUserOnly` to prevent unauthorized IPC access
- **Async Exception Handling** - Fixed `async void` in worker initialization for proper exception propagation
- **Improved Logging** - Added meaningful logging to previously bare catch blocks in HardwareWorkerClient
- **Installer Verification** - SHA256 hash verification for LibreHardwareMonitor downloads

### 🎨 User Interface Improvements
- **System Tray Menu Overhaul**
  - Consolidated Quick Profiles with descriptive labels (e.g., "🚀 Performance — Max cooling + Performance mode")
  - Grouped Fan Control, Power Profile, and Display under new "Advanced" submenu
  - Monospace font for temperature/load readings for better alignment
  - Clearer menu item descriptions throughout
- **New Animations** - Added 5 new smooth animation presets:
  - FadeInFast, SlideInFromBottom, ScaleIn, Breathing, FanSpin
- **Installer Wizard Images** - Updated to feature-focused design (no hardcoded version numbers)

### 📋 Details
See [CHANGELOG_v2.2.0.md](docs/CHANGELOG_v2.2.0.md) for full details.

---

## [2.1.2] - 2026-01-06

### 🐛 Bug Fixes
- **Temperature Freeze** - Fixed CPU/GPU temps freezing when storage drives go to sleep
- **OMEN Max V2 Detection** - Added model-name-based V2 thermal policy detection for OMEN Max 2025+ models

### 📋 Details
See [CHANGELOG_v2.1.2.md](docs/CHANGELOG_v2.1.2.md) for full details.

---

## [2.1.1] - 2026-01-05

### 🐛 Bug Fixes
- **Desktop Detection** - Block startup on OMEN Desktop (25L/30L/35L/40L/45L) to prevent BIOS corruption
- **Fan Speed Reset** - Fixed fans resetting to auto when starting games/stress tests
- **Quick Popup** - G-Helper style - left-click tray for quick controls
- **Reduced Polling** - Default 2000ms for better performance

### 📋 Details
See [CHANGELOG_v2.1.1.md](docs/CHANGELOG_v2.1.1.md) for full details.

---

## [2.0.0-beta] - 2025-12-28

### 🚀 Major Architecture Changes

#### Out-of-Process Hardware Monitoring
- **HardwareWorker** - New separate process for hardware monitoring
  - Eliminates stack overflow crashes from LibreHardwareMonitor recursive GPU queries
  - JSON-based IPC over named pipes for parent-child communication
  - Automatic restart with exponential backoff if worker crashes
  - Parent process monitoring - worker exits cleanly if main app closes
  - Log rotation: 5MB max file size, 3 backup files retained
  - Graceful shutdown with CancellationToken support

#### Self-Contained Deployment
- **Both executables now embed .NET runtime** - No separate .NET installation required
  - OmenCore.exe: Full WPF app with embedded runtime
  - OmenCore.HardwareWorker.exe: Worker process with embedded runtime
  - Single-file executables with native libraries extracted at first run

### ✨ New Features

#### Logitech SDK Improvements
- **Spectrum/Flash Effects** - New effect types added to ILogitechSdkProvider interface
  - `ApplySpectrumEffectAsync(device, speed)` - Rainbow color cycling
  - `ApplyFlashEffectAsync(device, color, duration, interval)` - Strobe/alert effect
- **80+ Device Support** - Massively expanded device PID database
  - G502 X, G502 X PLUS, G502 X Lightspeed
  - G PRO X 60 (LIGHTSPEED, wired variants)
  - G309 LIGHTSPEED gaming mouse
  - PRO X 2 LIGHTSPEED headset
  - ASTRO A30, A50 Gen 4 headsets
  - G915 X TKL, G915 X Full-size keyboards
  - All 2024/2025 product releases covered
  - Organized by device series (G5xx, G3xx, G9xx, PRO, ASTRO)

#### Linux CLI Enhancements (OmenCore.Linux)
- **ConfigCommand** - Full configuration management
  - `omencore config --show` - Display current settings
  - `omencore config --set key=value` - Update individual settings
  - `omencore config --get key` - Query specific setting
  - `omencore config --reset` - Restore defaults
  - `omencore config --apply` - Apply configuration changes
  - Config stored at `~/.config/omencore/config.json`
- **DaemonCommand** - Systemd service management
  - `omencore daemon --install` - Install as systemd service
  - `omencore daemon --start/--stop/--status` - Control service
  - `omencore daemon --generate-service` - Output service unit file
  - Automatic dependency installation (polkit rules, etc.)
- **JSON Output** - Machine-readable output for scripting
  - `omencore status --json` - JSON formatted temps, fans, perf mode
  - Global `--json` flag available on all commands

#### System Optimizer (Windows)
- **6 Optimization Categories** with individual toggle controls:
  - **Power**: Ultimate Performance plan, Hardware GPU Scheduling, Game Mode, Foreground Priority
  - **Services**: Telemetry, SysMain/Superfetch, Windows Search, DiagTrack
  - **Network**: TCP NoDelay, TCP ACK Frequency, Delivery Optimization, Nagle Algorithm
  - **Input**: Mouse Acceleration, Game DVR, Game Bar, Fullscreen Optimizations
  - **Visual**: Transparency, Animations, Shadows, Best Performance preset
  - **Storage**: SSD TRIM, Last Access Timestamps, 8.3 Names, Prefetch
- **Risk Indicators** - Low/Medium/High risk badges on each optimization
- **Preset Buttons** - "Gaming Maximum" and "Balanced" one-click presets
- **Registry Backup** - Automatic backup before changes, restore on revert
- **System Restore Points** - Creates restore point before applying optimizations

### 🐛 Bug Fixes

- **Stack Overflow Prevention** - Out-of-process architecture eliminates LibreHardwareMonitor crashes
- **SafeFileHandle Disposal** - Fixed "Cannot access a disposed object" errors in HardwareWorker
- **Version Consistency** - All assemblies now report 2.0.0 correctly
- **Log Rotation** - HardwareWorker logs no longer grow unbounded

### 🔧 Technical Changes

- **ILogitechSdkProvider Interface** - Extended with spectrum and flash effect methods
- **LogitechHidDirect** - Reorganized PID database by device series
- **LogitechRgbProvider** - Added `RgbEffectType.Spectrum` to supported effects
- **HardwareWorker IPC** - JSON protocol: `{"type":"temps"|"fans"|"ping"|"quit"}`
- **Build Configuration** - Both csproj files now have explicit SelfContained=true

### 📦 Dependencies

- .NET 8.0 (embedded in single-file executables)
- LibreHardwareMonitorLib 0.9.4
- RGB.NET.Core 3.1.0
- CUE.NET 1.2.0.1 (Corsair SDK)

---

## [1.6.0-alpha] - 2025-12-25

### Added
- **🎨 System RGB provider (experimental)** - `RgbNetSystemProvider` uses RGB.NET to control supported desktop RGB devices; supports static color application via `color:#RRGGBB`.
- **✨ Corsair preset application via providers** - `CorsairRgbProvider` now supports `preset:<name>` applying presets saved in configuration (`CorsairLightingPresets`).
- **🔁 RgbManager wiring & provider stack** - Providers are registered at startup in priority: Corsair → Logitech → Razer → SystemGeneric, enabling a single entrypoint to apply system-wide lighting effects.
- **🧪 Unit tests** - Added `CorsairRgbProviderTests` and preliminary tests for RGB provider wiring and behavior.
- **📄 Docs & dev notes** - Updated `docs/V2_DEVELOPMENT.md` and `CHANGELOG` to reflect Phase 3 design and spike work.

### Changed
- **Lighting subsystem** - `LightingViewModel` now accepts an `RgbManager` instance to expose provider actions to the UI and tests.

---

## [1.5.0-beta] - 2025-12-17

### Added
- **🔍 OmenCap.exe Detection** - Detects HP OmenCap running from Windows DriverStore
  - This component persists after OGH uninstall and blocks MSR access
  - Shows warning with detailed removal instructions
  - Prevents false "XTU blocking undervolt" errors
- **🧹 DriverStore Cleanup Info** - OGH cleanup now detects OmenCap in DriverStore
  - Provides pnputil commands for complete removal
  - Logs detailed instructions for manual cleanup
- **🔧 Experimental EC Keyboard Setting** - Now always visible in Settings > Features
  - Previously was hidden until Keyboard Lighting was enabled

### Fixed
- **💥 System Tray Crash** - Fixed crash when right-clicking tray icon
  - Bad ControlTemplate tried to put Popup inside Border
  - Replaced with simpler style-based approach
- **📐 Sidebar Width** - Increased sidebar from 200px to 230px for better readability
- **🔢 Version Display** - Updated to v1.5.0-beta throughout app
- **🔄 Tray Icon Update Crash** - Fixed "Specified element is already the logical child" error
  - Issue in UpdateFanMode/UpdatePerformanceMode when updating menu headers
  - Now creates fresh UI elements instead of reusing existing ones
  - Fixed SetFanMode, SetPerformanceMode, and UpdateRefreshRateMenuItem methods

### Changed
- Process kill list now includes OmenCap.exe
- Undervolt provider detects OmenCap as external controller
- SystemControl view shows specific OmenCap removal instructions

See [CHANGELOG_v1.5.0-beta.md](docs/CHANGELOG_v1.5.0-beta.md) for full details.

---

## [1.4.0-beta] - 2025-12-16

### Added
- **🎨 Interactive 4-Zone Keyboard Controls** - Visual zone editor with hex color input and presets
- **🚀 StartupSequencer Service** - Centralized boot-time reliability with retry logic
- **🖼️ Splash Screen** - Branded OMEN loading experience with progress tracking
- **🔔 In-App Notification Center** - Extended notification service with read/unread tracking
- **Fan Profile UI Redesign** - Card-based preset selector with visual icons
- **OSD TopCenter/BottomCenter** - New overlay position options
- **Undervolt Status Messages** - Informative explanations when undervolting unavailable

### Fixed
- **TCC Offset Persistence** - CPU temp limit now survives reboots
- **Thermal Protection Thresholds** - More aggressive fan ramping (80°C warning, 88°C emergency)
- **Auto-Start Detection** - Correctly detects existing startup entries
- **SSD Sensor 0°C** - Storage widget hides when no temperature data available
- **Overlay Hotkey Retry** - Hotkey registration retries when starting minimized
- **Tray Refresh Rate Display** - Updates immediately after changing
- **Undervolt Section Visibility** - Hides on unsupported AMD systems

See [CHANGELOG_v1.4.0-beta.md](docs/CHANGELOG_v1.4.0-beta.md) for full details.

---

## [1.3.0-beta2] - 2025-12-15

### Fixed
- **Fan presets now work** - All presets (Auto, Quiet, Max) function correctly on all models
- **GPU Power Boost persists** - TGP settings survive Windows restart with multi-stage retry
- **OSD overlay fixed** - Works correctly when starting minimized to tray
- **OMEN key interception fixed** - Settings UI now properly controls the hook
- **Start minimized reliable** - Consistent tray-only startup behavior
- **Intel XTU false positive** - Now uses ServiceController for accurate detection

### Added
- Built-in "Quiet" fan preset with gentle curve
- "ShowQuickPopup" as default OMEN key action
- Temperature smoothing (EMA) for stable UI display
- Real CPU/GPU load values in OSD overlay

See [CHANGELOG_v1.3.0-beta2.md](docs/CHANGELOG_v1.3.0-beta2.md) for full details.

---

## [1.3.0-beta] - 2025-12-14

See [CHANGELOG_v1.3.0-beta.md](docs/CHANGELOG_v1.3.0-beta.md) for full details.

---

## [1.2.0] - 2025-12-14 (Major Release)

### Added
- **🔋 Power Automation** - Auto-switch profiles on AC/Battery change
  - Configurable presets for AC and Battery power states
  - Settings UI in Settings tab
  - Event-driven, minimal resource usage
- **🌡️ Dynamic Tray Icon** - Color-coded temperature display in system tray
  - Green: Cool (<60°C), Yellow: Warm (60-75°C), Red: Hot (>75°C)
  - Real-time temperature updates
- **🔒 Single Instance Enforcement** - Prevents multiple copies from running (mutex-based)
- **🖥️ Display Control** - Quick refresh rate switching from tray menu
  - Toggle between high/low refresh rates
  - "Turn Off Display" option for background tasks
- **📌 Stay on Top** - Keep main window above all other windows (toggle in tray menu)
- **⚠️ Throttling Detection** - Real-time throttling status in dashboard header
  - Detects CPU/GPU thermal throttling
  - Detects CPU/GPU power throttling (TDP limits)
  - Warning indicator appears when system is throttling
- **⏱️ Fan Countdown Extension** - Automatically re-applies fan settings every 90s to prevent HP BIOS 120-second timeout
- **📊 Configurable Logging** - Log verbosity setting in config (Error/Warning/Info/Debug)
  - Empty log lines filtered in non-Debug mode

### Fixed
- **🔧 .NET Runtime Embedded** - App is now fully self-contained, no separate .NET installation required
- **🌀 Fan Mode Reverting (GitHub #7)** - Improved WMI command ordering, fan modes now persist correctly
  - Added countdown extension timer to prevent BIOS timeout
- **⚡ High CPU Usage** - 5x slower polling in low overhead mode (5s vs 1s)
- **⚡ DPC Latency** - Extended cache lifetime to 3 seconds in low overhead mode

### Changed
- Installer simplified - removed .NET download logic
- Self-contained single-file build with embedded runtime
- Performance optimizations for reduced system impact
- Tray menu reorganized with Display submenu

### Technical Notes
- Build: `dotnet publish --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true`
- New: `PowerAutomationService.cs` for AC/Battery switching
- New: `DisplayService.cs` for refresh rate and display control
- New: `TrayIconService.CreateTempIcon()` for dynamic temperature icons
- New: `WmiFanController._countdownExtensionTimer` - 90-second interval timer
- New: `LogLevel` enum and configurable verbosity
- New: `StayOnTop` config option
- Low overhead cache: 3000ms (was 100ms)

See [CHANGELOG_v1.2.0.md](docs/CHANGELOG_v1.2.0.md) for full details.

---

## [1.1.2] - 2025-12-13

### Added
- **Task Scheduler Startup** - Windows startup now uses scheduled task with elevated privileges (fixes startup issues)
- **Gaming Fan Preset** - New aggressive cooling preset using Performance thermal policy for gaming
- **GPU Power Boost Persistence** - Last used GPU power level saved to config and restored on startup
- **Fan Curve Editor Guide** - "How Fan Curves Work" explanation box with examples
- **Auto Hardware Reinit** - LibreHardwareMonitor auto-reinitializes when CPU temp stuck at 0°C

### Fixed
- **Startup Issues** - OmenCore now properly starts with Windows (Task Scheduler with HIGHEST privileges)
- **CPU Temp 0°C on AMD** - Extended sensor detection for Ryzen 8940HX, Hawk Point, and other AMD CPUs
- **Auto Fan Mode** - Clarified that "Auto" uses BIOS default; added "Gaming" preset for aggressive cooling

### Changed
- **Secure Boot Banner** - Now shows specific limitation and actionable solution (install PawnIO)
- **GPU Mode Switching UI** - Added hardware limitation warning and BIOS fallback guidance
- **GPU Power Boost UI** - Added warning about potential reset after sleep/reboot (BIOS behavior)
- **Fan Presets** - Added detailed tooltips explaining each preset's behavior

### Technical Notes
- Task name: `OmenCore` with `onlogon` trigger and `highest` run level
- New config properties: `LastGpuPowerBoostLevel`, `LastFanPresetName`
- New `FanMode` values: `Performance`, `Quiet`
- Extended AMD CPU sensor fallbacks (15+ patterns including CCD variants, SoC, Socket)
- `LibreHardwareMonitorImpl.Reinitialize()` method for sensor recovery

See [CHANGELOG_v1.1.2.md](docs/CHANGELOG_v1.1.2.md) for full details.

---

## [1.1.1] - 2025-12-13

### Added
- **Smooth Scrolling** - New `SmoothScrollViewer` style with pixel-based scrolling for improved UX
- **SystemControlView Scrolling** - Added ScrollViewer wrapper so long content scrolls correctly
- **Modern Scrollbar Style** - Thin scrollbars with hover-fade effect

### Changed
- Applied smooth scrolling to all major views: Dashboard, Settings, Lighting, SystemControl, MainWindow
- Improved scroll responsiveness throughout the application

### UI/UX Improvements
- Eliminated chunky item-based scrolling that felt slow and unintuitive
- Consistent brand imagery (Corsair/Logitech) in RGB & Peripherals tab
- Verified typography and color palette consistency across all views

---

## [1.0.0.7] - 2025-12-XX

### Fixed
- **Multi-instance game detection** - Fixed crash when multiple instances of the same game are running (dictionary key collision)
- **Thread-safe process tracking** - Switched from `Dictionary` to `ConcurrentDictionary` for lock-free concurrent access
- **Process.StartTime exception** - Added try-catch for processes that exit during WMI scan
- **Resource cleanup** - Added missing `Dispose()` calls for `ProcessMonitoringService` and `GameProfileService` in `MainViewModel`
- **Fire-and-forget save errors** - Profile saves now have proper error handling with logging instead of silent failures

### Technical Notes
- `ActiveProcesses` now keyed by Process ID (int) instead of name (string) to support multiple game instances
- `ConcurrentDictionary<int, ProcessInfo>` eliminates race conditions in multi-threaded process monitoring
- Robust `StartTime` access wrapped in try-catch to handle process exit during enumeration
- Added `IDisposable` pattern enforcement for monitoring services

---

## [1.0.0.6] - 2025-12-XX

### Added
- **Game Profile System** - Complete auto-switching profiles for games with fan presets, performance modes, RGB lighting, and GPU settings
- **Game Profile Manager UI** - Full-featured window with profile list, search, editor panel, import/export (JSON)
- **Process Monitoring Service** - Background WMI-based process detection with 2-second polling
- **Manual Update Check** - "Check for Updates" button in update banner for on-demand update checks
- **Profile Statistics** - Launch count, total playtime tracking per game profile

### Changed
- Updated README to reflect game profile feature availability

---

## [1.0.0.5] - 2025-12-10

### Added
- **First-run detection** - WinRing0 driver prompt now only appears once on initial startup
- **Enhanced tray tooltips** - Now displays CPU, GPU, RAM usage with better formatting and emojis
- **Config validation** - Automatically repairs invalid or missing config properties with sensible defaults
- **Better disabled button states** - Improved visual feedback for disabled buttons with grayed background

### Fixed
- **Driver guide path resolution** - Now correctly finds WINRING0_SETUP.md in installed location or falls back to online docs
- **Monitoring tab scrollbar** - Added ScrollViewer wrapper so content scrolls properly when window is resized
- **Button hover animations** - Smoother scale transitions and better hover color feedback
- **Config persistence** - FirstRunCompleted flag saved after showing driver prompt

### Changed
- **Visual polish** - Enhanced button styles with better disabled states and cursor feedback
- **Tray tooltip format** - Multi-line with emoji icons, version display, and clearer system stats
- **Config loading** - More robust error handling with automatic fallback to defaults
- **Logging** - Version string improved to show both app version and assembly version

### Technical Notes
- Added `FirstRunCompleted` boolean to `AppConfig` model
- `ValidateAndRepair()` method ensures all config collections are initialized
- Driver guide checks three paths: bundled docs, dev location, then GitHub URL
- Monitoring interval validated to be between 500-10000ms
- Button disabled state uses `TextMutedBrush` for better contrast

---

## [1.0.0.4] - 2025-12-10

### Added
- **Live CPU temperature badge** on system tray icon updates every 2 seconds with gradient background and accent ring
- **EC write address allowlist** in `WinRing0EcAccess` prevents accidental writes to dangerous registers (VRM, battery charger)
- **Chart gridlines** in thermal and load monitoring with temperature labels for better readability
- **Sub-ViewModel architecture** with `FanControlViewModel`, `DashboardViewModel`, `LightingViewModel`, and `SystemControlViewModel`
- **Async peripheral services** with proper factory pattern (`CorsairDeviceService.CreateAsync`, `LogitechDeviceService.CreateAsync`)
- **Version logging** on application startup for easier debugging
- **Unit test stubs** for hardware access, auto-update, and Corsair/Logitech services

### Fixed
- **Logging service shutdown flush** - writer thread now joins with 2-second timeout to ensure tail logs are written before exit
- **Cleanup toggle mapping** - "Remove legacy installers" checkbox now correctly maps to `CleanupRemoveLegacyInstallers` option
- **Garbled UI glyphs** - replaced mojibake characters (â¬†, âš ) with ASCII "Update" and "!" in MainWindow update banner
- **Auto-update safety** - missing SHA256 hash now returns null with warning instead of crashing, blocks install button with clear messaging
- **TrayIconService disposal** - properly unsubscribes timer event handler to prevent memory leak
- **FanMode backward compatibility** - defaults to `Auto` for existing configurations without mode property
- **Installer version** - updated to 1.0.0.4 (was incorrectly 1.0.0.3)

### Changed
- **Color palette refresh** - darker backgrounds (#05060A), refined accents (#FF005C red, #8C6CFF purple)
- **Typography improvements** - Segoe UI Variable Text with better text rendering
- **Card styling** - unified `SurfaceCard` style with 12px corners, consistent padding, drop shadows
- **Tab design** - modern pill-style tabs with purple accent underlines
- **ComboBox polish** - enhanced dropdown with chevron icon, better hover states
- **Chart backgrounds** - subtle gradients with rounded corners for visual depth
- **HardwareMonitoringService** - added change detection threshold (0.5°C/%) to reduce unnecessary UI updates
- **Test expectations** - updated `AutoUpdateServiceTests` to match new null-return behavior

### Technical Notes
- Sub-ViewModels reduce `MainViewModel` complexity (future: integrate UserControl views in MainWindow tabs)
- EC allowlist includes fan control (0x44-0x4D), keyboard backlight (0xBA-0xBB), performance registers (0xCE-0xCF)
- Tray icon uses 32px `RenderTargetBitmap` with centered FormattedText rendering
- SHA256 extraction regex: `SHA-?256:\s*([a-fA-F0-9]{64})`

### Migration Notes
- **No breaking changes** for existing configurations
- `FanPreset` objects without `Mode` property will default to `Auto`
- Future releases **must** include `SHA256: <hash>` in GitHub release notes for in-app updater to function

---

## [1.0.0.3] - 2025-11-19

### Initial Stable Release
- Fan curve control with custom presets
- CPU undervolting via Intel MSR
- Performance mode switching (Balanced/Performance/Turbo)
- RGB keyboard lighting profiles
- Hardware monitoring with LibreHardwareMonitor integration
- Corsair iCUE device support (stub implementation)
- Logitech G HUB device support (stub implementation)
- HP OMEN Gaming Hub cleanup utility
- System optimization toggles (animations, services)
- GPU mux switching (Hybrid/Discrete/Integrated)
- Auto-update mechanism via GitHub releases
- System tray integration with context menu

---

## Release Links

- **v1.0.0.4**: https://github.com/theantipopau/omencore/releases/tag/v1.0.0.4
- **v1.0.0.3**: https://github.com/theantipopau/omencore/releases/tag/v1.0.0.3

---

## Versioning Policy

**Major.Minor.Patch**
- **Major**: Breaking changes, architecture overhaul, config incompatibility
- **Minor**: New features, service additions, UI redesign
- **Patch**: Bug fixes, polish, performance, security

## Future Roadmap

**v1.1.0 (Planned)**
- Complete MainWindow tab refactor with UserControl integration
- LightingView and SystemControlView implementation
- Per-game profile switching
- Custom EC address configuration
- Macro recording for peripherals

**v1.2.0 (Planned)**
- Full iCUE SDK integration (replace stub)
- Logitech G HUB SDK integration
- Network QoS controls
- On-screen overlay for FPS/temps

---

For detailed upgrade guidance, see `docs/UPDATE_SUMMARY_2025-12-10.md`
