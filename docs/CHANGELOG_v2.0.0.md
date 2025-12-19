# Changelog v2.0.0

All notable changes to OmenCore v2.0.0 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.0.0-alpha1] - 2025-12-19

### Added
- **🎛️ System Optimizer** - Complete Windows gaming optimization suite
  - Power: Ultimate Performance plan, GPU scheduling, Game Mode, foreground priority
  - Services: Telemetry, SysMain/Superfetch, Search Indexing, DiagTrack
  - Network: TCP NoDelay, ACK frequency, Nagle algorithm, P2P updates
  - Input: Mouse acceleration, Game DVR, Game Bar, fullscreen optimizations
  - Visual: Transparency, animations, shadows, performance presets
  - Storage: TRIM, last access timestamps, 8.3 names, SSD detection
- **🎯 One-click optimization presets** (Gaming Max, Balanced, Revert All)
- **🔒 Registry backup and system restore point creation**
- **🏷️ Risk indicators** for each optimization (Low/Medium/High)
- **⚡ Extreme fan preset** (100% at 75°C for high-power systems)
- **🎨 DarkContextMenu control** - Custom context menu with no white margins
- **🔄 Modern toggle switches** - Replaced checkboxes with iOS-style toggles in Settings
- **⚡ GPU Voltage/Current Graph** - Added GPU V/C monitoring chart to dashboard
- **🔧 Per-Core Undervolt** - Individual undervolt controls for each CPU core

### Changed
- **📄 Roadmap renamed** from v1.6 to v2.0
- **🔄 TrayIconService refactored** to use DarkContextMenu
- **📐 Sidebar width increased** from 200px to 230px for better readability
- **📊 LoadChart Performance** - Improved rendering smoothness from 10 FPS to 20 FPS
  - Reduced render throttling from 100ms to 50ms intervals
  - Implemented polyline reuse to avoid object recreation overhead
  - Added BitmapCache for better visual performance
- **🔢 Version updated** to v1.5.0-beta throughout app

### Fixed
- **💥 System Tray Crash** - Fixed crash when right-clicking tray icon
  - Bad ControlTemplate tried to put Popup inside Border
  - Replaced with simpler style-based approach
- **🔄 Tray Icon Update Crash** - Fixed "Specified element is already the logical child" error
  - Issue in UpdateFanMode/UpdatePerformanceMode when updating menu headers
  - Now creates fresh UI elements instead of reusing existing ones
  - Fixed SetFanMode, SetPerformanceMode, and UpdateRefreshRateMenuItem methods
- **❌ Right-click Context Menu** - Fixed tray icon right-click menu not appearing
  - Temporarily reverted to standard ContextMenu while DarkContextMenu is debugged
  - Applied dark theme styling to regular ContextMenu (dark background, white text, OMEN red accents)
  - Temperature display and color changes restored to tray icon
- **🔢 Version Updated** - Changed from v1.5.0-beta4 to v2.0.0-alpha1 for v2.0 development
- **✅ Platform compatibility warnings** - Added [SupportedOSPlatform("windows")] attributes
  - Fixed 57 compilation warnings for Windows-only APIs in System Optimizer
 - **🐞 Adopted v1.5.0-beta4 bug fixes** - Incorporated community-reported regressions into v2.0 fixes
   - Fixed unexpected keyboard color reset on startup (auto-set to red)
   - Fixed incorrect quick profile state (panel vs active profile mismatch)
   - Fixed slow fan-profile transition/latency when switching profiles
   - Investigated high CPU temperature readings at low usage; added improved smoothing and sensor selection fallbacks
 - **🔴 Keyboard Color Auto-Change** - Fixed program automatically changing keyboard to red on startup
 - **🌡️ High CPU Temps at Low Usage** - Improved CPU temperature monitoring accuracy
 - **🐌 Fan Profile Transition Latency** - Optimized fan profile switching speed
 - **🔄 Quick Profile State Mismatch** - Fixed discrepancy between active profile display and actual state

### Technical
- **🏗️ MVVM Architecture** - Maintained clean separation of concerns
- **🔧 Service Layer** - RegistryBackupService, OptimizationVerifier, multiple optimizer classes
- **🎨 XAML Styling** - Modern UI with consistent theming
- **🧪 Unit Tests** - All tests passing, including new Corsair device service test
- **📦 Build System** - Clean compilation with no warnings

---

## Development Progress

### Phase 1: Foundation & Quick Wins ✅
- [x] System Tray Overhaul (context menu, dark theme, icons)
- [x] Settings View Improvements (toggle switches instead of checkboxes)
- [x] Tray Icon Update Crash fixes (WPF logical parent issues)

### Phase 2: System Optimizer ✅
- [x] Core Infrastructure (services, backup, verification)
- [x] Power Optimizations (Ultimate Performance, GPU scheduling, Game Mode)
- [x] Service Optimizations (telemetry, SysMain, search indexing)
- [x] Network Optimizations (TCP settings, Nagle algorithm)
- [x] Input & Graphics (mouse acceleration, Game DVR, fullscreen opts)
- [x] Visual Effects (animations, transparency, presets)
- [x] Storage Optimizations (TRIM, 8.3 names, SSD detection)
- [x] UI Implementation (tabs, toggles, risk indicators, presets)

### Phase 3: RGB Overhaul (Planned)
- [ ] Asset Preparation (device images, brand logos)
- [ ] Enhanced Corsair SDK (iCUE 4.0, full device enumeration)
- [ ] Full Razer Chroma SDK (per-key RGB, effects library)
- [ ] Enhanced Logitech SDK (G HUB integration, direct HID)
- [ ] Unified RGB Engine (sync all, cross-brand effects)
- [ ] Lighting View Redesign (device cards, connection status)

### Phase 4: Linux Support (Planned)
- [ ] Linux CLI (EC access, fan control, keyboard lighting)
- [ ] Linux Daemon (systemd service, automatic curves)
- [ ] Distro Testing (Ubuntu, Fedora, Arch, Pop!_OS)

### Phase 5: Advanced Features (Planned)
- [ ] OSD Overlay (RTSS integration, customizable metrics)
- [ ] Game Profiles (process detection, per-game settings)
- [ ] GPU Overclocking (NVAPI, core/memory offsets)
- [ ] CPU Overclocking (PL1/PL2, turbo duration)

### Phase 6: Polish & Release (Planned)
- [ ] Linux GUI (Avalonia UI port)
- [ ] Bloatware Manager (enumeration, safe removal)
- [ ] Final Testing (regression, performance, documentation)

**Overall Progress: 43/114 tasks (38%)**

---

*Last Updated: December 19, 2025*