# Changelog

All notable changes to OmenCore will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
