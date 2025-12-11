# OmenCore v1.0.0.5 — Polish & UX Improvements

Quality-of-life release with visual polish, better first-run experience, and enhanced error handling.

## Download

- **Windows Installer**: [OmenCoreSetup-1.0.0.5.exe](https://github.com/theantipopau/omencore/releases/download/v1.0.0.5/OmenCoreSetup-1.0.0.5.exe)
  - **SHA256**: `D587950DBE6A38A5B8E14D89C632BB8ED0883D0D7211BE280F52258B5C61F69B`
- **Portable ZIP**: [OmenCore-1.0.0.5-win-x64.zip](https://github.com/theantipopau/omencore/releases/download/v1.0.0.5/OmenCore-1.0.0.5-win-x64.zip)
  - **SHA256**: `C2DE9F8A7270CB172926CA2249FA8978E88D586E02C1D789C3B9D41D0EC73C7E`

---

## ✨ What's New

### First-Run Experience
- **One-time driver prompt** - WinRing0 setup dialog only appears on first launch
- **Smart path detection** - Driver guide opens from install location, dev path, or GitHub
- **Auto-config repair** - Invalid/missing config properties automatically fixed with defaults

### Visual Polish
- **Enhanced tray tooltips** - Rich multi-line display with CPU, GPU, RAM stats and emojis
- **Better disabled states** - Buttons show clear visual feedback when disabled (grayed background)
- **Smoother animations** - Improved button hover transitions with scale effects
- **Fixed scrolling** - Monitoring tab now scrolls properly when window is resized

### Reliability
- **Config validation** - Robust error handling with automatic fallback to defaults
- **Version tracking** - Improved logging shows both app and assembly version
- **Persistent settings** - FirstRunCompleted flag saved after initial setup

---

## 🐛 Bug Fixes

- **Monitoring tab scrollbar missing** - Added ScrollViewer wrapper for proper content scrolling
- **Driver guide path broken** - Now checks bundled docs, dev location, then online fallback
- **Config loading crashes** - Added try-catch with validation and sensible defaults
- **Button cursor stays as hand when disabled** - Now correctly shows arrow cursor

---

## 📦 Installation

### Fresh Install
1. Download `OmenCoreSetup-1.0.0.5.exe`
2. Run as Administrator
3. Follow first-run setup (driver prompt appears once)
4. Launch from Start Menu

### Upgrade from 1.0.0.4
- **No uninstall required** - Installer upgrades in place
- **Config preserved** - Settings carry over automatically
- **New features** - FirstRunCompleted flag added to existing configs

---

## 📋 Requirements

- **OS**: Windows 10 (build 19041+) or Windows 11
- **Runtime**: .NET 8 Desktop Runtime (x64)
  - Download: https://dotnet.microsoft.com/download/dotnet/8.0
- **Privileges**: Administrator access (for hardware control)
- **Optional**: WinRing0 driver for fan control and undervolting
  - Install via LibreHardwareMonitor: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases

---

## 🔄 Changelog

### Added
- First-run detection with persistent `FirstRunCompleted` flag
- Enhanced tray tooltips with emoji icons and formatted system stats
- Config validation with automatic repair for invalid/missing properties
- Better disabled button visual states

### Fixed
- Monitoring tab scrollbar now appears when content overflows
- Driver guide path resolution checks multiple locations before online fallback
- Config loading no longer crashes on malformed JSON
- Button cursor feedback when disabled

### Changed
- Visual polish on button styles (hover, pressed, disabled states)
- Tray tooltip format improved with multi-line layout
- Config service now has `Config` property for direct access
- Monitoring interval validated to 500-10000ms range

---

## ⚠️ Known Issues

- CUE.NET compatibility warning during build (functional, cosmetic only)
- Windows Defender may flag WinRing0 driver (false positive)
- LibreHardwareMonitor not bundled in installer (manual install required)

---

## 🛠️ For Developers

### Technical Changes
- Added `AppConfig.FirstRunCompleted` boolean property
- `ConfigurationService.ValidateAndRepair()` ensures collection initialization
- Driver guide checks: bundled → dev → online URL
- Tray tooltip uses formatted string with emoji icons
- Button disabled state uses `TextMutedBrush` for contrast

### Testing
See `docs/TESTING_GUIDE.md` for local testing instructions.

---

## 🚀 What's Next?

See `FIXES_FOR_1.0.0.6.md` for planned improvements:
- Per-game performance profiles
- System tray mini-dashboard
- Export/import settings
- Improved RGB device detection

---

## ⚠️ Disclaimer

THIS SOFTWARE IS PROVIDED "AS IS" WITHOUT WARRANTY. Modifying EC registers, undervolting, and mux switching can potentially damage hardware. Always test on non-production hardware first. The developers are not responsible for hardware damage, data loss, or warranty voids.

---

**Report Issues**: [GitHub Issue Tracker](https://github.com/theantipopau/omencore/issues)

**Discussions**: [GitHub Discussions](https://github.com/theantipopau/omencore/discussions)

**Made with ❤️ for the HP OMEN community**
