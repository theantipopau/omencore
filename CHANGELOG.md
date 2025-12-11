# Changelog

All notable changes to OmenCore will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
