# OmenCore v1.2.0 Changelog

## 🚀 OmenCore v1.2.0 - Major Release

**Release Date**: December 14, 2025  
**Download**: [GitHub Releases](https://github.com/theantipopau/omencore/releases/tag/v1.2.0)

---

This is a **major release** that addresses critical user-reported issues, improves performance significantly, introduces new automation features, and adds many quality-of-life improvements based on community feedback.

---

## ✨ Major New Features

### � Visual Fan Curve Editor
- **NEW**: Interactive drag-and-drop fan curve editor
- Visual graph with temperature (X-axis) and fan speed % (Y-axis)
- Drag curve points to adjust temperature/fan speed mapping
- Click empty area to add new points
- Right-click points to remove them
- Live current temperature indicator (blue line)
- Color-coded gradient fill under curve
- Grid lines and axis labels for precise control
- Add/Remove/Reset buttons for quick adjustments
- Real-time curve point summary display
- Save custom curves as named presets

### �🔋 Power Automation (AC/Battery Switching)
- **NEW**: Automatically switch performance profiles based on power source
- Configure separate profiles for AC power and battery
- Settings include:
  - Auto-enable on app startup
  - AC profile preset (Default, Performance, Max, etc.)
  - Battery profile preset (Silent, Balanced, etc.)
- Instantaneous switching when power source changes
- Event-driven with minimal resource usage

### 🌡️ Dynamic Tray Icon with Temperature Display
- **NEW**: System tray icon shows current temperature with color-coded background
- Color scheme indicates temperature range:
  - 🟢 Green: Cool (< 60°C) - system running well
  - 🟡 Yellow/Orange: Warm (60-75°C) - moderate load
  - 🔴 Red: Hot (> 75°C) - high load/thermal throttling risk
- Temperature updates in real-time
- Instantly see thermal state without opening the app

### 🔒 Single Instance Enforcement
- **NEW**: Prevents multiple copies of OmenCore from running simultaneously
- Uses mutex-based locking
- Shows friendly message directing users to system tray if already running
- Reduces resource conflicts and confusion

### 🖥️ Display Control
- **NEW**: Quick refresh rate switching from system tray menu
- Toggle between high (165Hz) and low (60Hz) refresh rates
- "Turn Off Display" feature - screen off while system continues running
- Perfect for overnight downloads, background tasks, or music playback

### 📌 Stay on Top Option
- **NEW**: Keep main window always visible above other windows
- Toggle from tray menu
- Setting persists across restarts
- Visual indicator (📌) shows current state

### ⚠️ Throttling Detection & Display
- **NEW**: Real-time throttling status indicator in dashboard header
- Detects CPU thermal throttling (>95°C)
- Detects CPU power throttling (TDP/PROCHOT limits)
- Detects GPU thermal throttling (>83°C)
- Detects GPU power throttling (power limits)
- Warning badge shows specific throttling reasons

### ⏱️ Fan Countdown Extension
- **NEW**: Automatically re-applies fan settings every 90 seconds
- Prevents HP BIOS from reverting fan settings after 120-second timeout
- Works transparently in background
- Stops automatically when returning to Auto mode

### 📊 Configurable Logging Verbosity
- **NEW**: Log level setting in configuration
- Options: Error, Warning, Info, Debug
- Reduces log spam for normal users
- Debug mode available for troubleshooting

### 🛡️ External Undervolt Controller Detection
- **NEW**: Detects Intel XTU, ThrottleStop, and other undervolt controllers
- Clear warning panel when external controller detected
- Shows which program is blocking OmenCore's undervolt
- Step-by-step instructions on how to disable conflicting software
- Specific guidance for XTU, ThrottleStop, Intel DTT, and OGH

### 🖥️ Reorganized Side Panel
- **Improved**: Cleaner side panel layout
- HP OMEN model now shown with logo at top of System Info
- Backend and Secure Boot info moved to Settings → System Status
- Settings page shows complete system status:
  - Fan control backend (WMI BIOS/PawnIO/WinRing0)
  - Secure Boot status with indicator
  - PawnIO driver availability
  - OMEN Gaming Hub installation status

### 🌀 Smooth Animated Scrolling
- **Improved**: All scrolling now uses smooth animation
- 200ms ease-out animation for natural feel
- Consistent scroll speed (1.2x multiplier) across all views
- No more jarring/jerky scroll behavior

### 🎨 System Tray Menu Styling Fix
- **Fixed**: Removed white panel/strip on left side of context menu
- Complete dark theme applied to menu items
- Cleaner visual appearance matching OMEN aesthetic

### 🧹 Cleaner Log Output
- **Fixed**: Reduced excessive log messages during normal operation
- "No fan sensors found" debug message now logged only once (not every 1.2s)
- Corsair HID warnings only shown once per device type
- Graceful shutdown handling to prevent ObjectDisposedException on app close
- Cleaner log files for easier troubleshooting

---

## ⚡ Critical Bug Fixes

### 🔧 .NET Runtime Now Embedded
- **Fixed**: .NET 8.0 runtime is now fully embedded within the application
- No separate .NET installation required during setup
- Self-contained single-file executable
- Eliminates ".NET not found" errors
- Installer is now simpler and faster

### 🌀 Fan Mode Reverting Issues ([GitHub Issue #7](https://github.com/theantipopau/omencore/issues/7))
- **Fixed**: Fan modes now apply correctly and persist
- Fixed `MapPresetToFanMode` to properly check FanMode enum values first
- Fixed Max preset: Now correctly sets Performance mode → SetFanMax(true) → fallback to SetFanLevel(55, 55)
- Proper WMI command ordering ensures fan modes stick
- **NEW**: 90-second countdown extension prevents BIOS timeout reversion

### 🐢 High CPU Usage During Idle
- **Fixed**: Dramatically reduced CPU usage when app is idle
- Low overhead mode now polls every 5 seconds (was 1 second)
- Temperature change threshold increased to 3°C (was 0.5°C)
- CPU usage reduced by ~80% in typical use

### 📈 DPC Latency Improvements
- **Fixed**: Reduced DPC latency caused by hardware polling
- LibreHardwareMonitor cache extended to 3 seconds in low overhead mode
- `SetLowOverheadMode(bool)` method added for optimized polling
- Better for gaming and audio production

---

## 🎯 HP OMEN Specific Improvements

### 💨 Fan Control Reliability
- Improved WMI command sequencing for HP OMEN hardware
- Better handling of thermal policy transitions
- Max fan mode now works reliably across more OMEN models
- Fixed race conditions in fan mode application
- Countdown extension prevents BIOS auto-revert

### 🔌 Omen Hub Conflict Mitigation
- Better coexistence with HP Omen Gaming Hub
- Reduced polling conflicts
- Cleaner WMI resource handling

---

## 📦 Installer Improvements

### Simplified Installation
- Self-contained build - no .NET download required
- Faster installation time
- Smaller download dependencies
- Removed all .NET runtime checking code
- PawnIO driver installation still available as option

---

## ❓ Known Limitations & FAQ

### GPU Usage Discrepancy
> *"CPU shows correct but GPU usage shows 97% while NVIDIA/AMD software shows 2-4%"*

This is a known difference in how GPU usage is measured:
- **OmenCore/LibreHardwareMonitor**: Reports 3D engine utilization (CUDA/compute workloads)
- **NVIDIA App/AMD Software**: Reports overall GPU activity including idle states

Both readings are technically correct - they measure different aspects of GPU activity. The higher reading typically reflects actual GPU compute usage, while vendor tools may show lower values due to different sampling methods.

### Reverse Fan Cleaning Function
> *"Is there a way to use reverse fan cleaning without Omen Gaming Hub?"*

Currently, the reverse fan cleaning function requires HP Omen Gaming Hub. This is a specialized BIOS-level function that HP has not exposed through their standard WMI interface. We are investigating alternative methods for future releases.

### Corsair Mouse Button Remapping
> *"Does the iCUE replacement support side button remaps?"*

Currently, the Corsair integration focuses on:
- ✅ DPI stage configuration
- ✅ RGB lighting profiles
- ⏳ **Button remapping is planned for v1.3**

The side button/macro functionality requires deeper integration with Corsair's HID protocol and is on our roadmap.

### Per-Key RGB Keyboard Lighting
> *"Do we lose per-key RGB keyboard lighting with OmenCore?"*

**No!** OmenCore supports per-key RGB lighting through the Lighting view:
- Individual key color customization
- Zone-based lighting
- Preset effects (breathing, wave, spectrum, etc.)
- Import/export of custom profiles

OmenCore uses the same HP WMI lighting interface as Omen Gaming Hub, so all keyboard lighting features remain available.

---

## 🔧 Technical Changes

### Build Changes
```
dotnet publish --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeAllContentForSelfExtract=true \
  -p:PublishTrimmed=false
```

### New Services & Files
- `PowerAutomationService.cs` - AC/Battery profile automation
- `DisplayService.cs` - Refresh rate switching and display control
- `BoolToVisibilityConverter.cs` - UI visibility binding helper
- `FanCurveEditor.xaml/.cs` - Interactive visual fan curve editor control
- Enhanced `TrayIconService.cs` - Dynamic temperature icons, display menu, stay-on-top
- Enhanced `LoggingService.cs` - Configurable verbosity levels

### Performance Optimizations
- `HardwareMonitoringService.cs`:
  - Low overhead interval: 5000ms (was 1000ms)
  - Low overhead threshold: 3.0°C (was 0.5°C)
- `LibreHardwareMonitorImpl.cs`:
  - Cache lifetime: 3000ms in low overhead (was 100ms)
  - `SetLowOverheadMode(bool enabled)` method
  - Throttling detection via sensor analysis

### Fan Control Enhancements
- `WmiFanController.cs`:
  - `MapPresetToFanMode` now checks enum values first
  - Max preset uses: Performance → SetFanMax(true) → SetFanLevel(55,55) fallback
  - **NEW**: `_countdownExtensionTimer` - 90-second interval timer
  - **NEW**: `StartCountdownExtension()` / `StopCountdownExtension()` methods
- `HpWmiBios.cs`:
  - **NEW**: `ExtendFanCountdown()` method to refresh fan settings

### Throttling Detection
- `MonitoringSample.cs`:
  - `IsCpuThermalThrottling`, `IsCpuPowerThrottling`
  - `IsGpuThermalThrottling`, `IsGpuPowerThrottling`
  - `IsThrottling` (computed), `ThrottlingStatus` (human-readable)
- `LibreHardwareMonitorImpl.cs`:
  - Sensor-based throttle detection
  - Temperature threshold fallbacks (95°C CPU, 83°C GPU)

### Config Changes
```json
{
  "LogLevel": "Info",
  "StayOnTop": false,
  "PowerAutomation": {
    "Enabled": false,
    "AcFanPreset": "Auto",
    "AcPerformanceMode": "Balanced",
    "AcGpuMode": "Hybrid",
    "BatteryFanPreset": "Quiet",
    "BatteryPerformanceMode": "Silent",
    "BatteryGpuMode": "Eco"
  }
}
```

---

## 📥 Installation

### Upgrade from v1.1.x
1. Close OmenCore if running
2. Download `OmenCoreSetup-1.2.0.exe`
3. Run installer (will upgrade in place)
4. No .NET installation prompts!

### Fresh Install
1. Download `OmenCoreSetup-1.2.0.exe` from releases
2. Run installer
3. Grant Administrator privileges when prompted
4. Configure settings as desired

---

## ⚠️ Breaking Changes

- None - fully compatible with v1.1.x configurations

---

## 📊 What's Next (Planned for v1.3)

- Battery health monitoring and charge limit control
- Omen key interception for custom actions
- Corsair mouse button remapping
- Quick popup UI for instant settings access
- Fan curve hysteresis (different ramp-up vs ramp-down)

---

## 💬 Community

Thank you to everyone who reported issues and provided feedback, especially:
- [GitHub Issue #7](https://github.com/theantipopau/omencore/issues/7) - Fan mode reverting issues
- [GitHub Issue #4](https://github.com/theantipopau/omencore/issues/4) - Feature requests and improvements
- Users who reported .NET installation problems
- Users asking about Corsair integration and RGB support

Your feedback directly shapes OmenCore development!

---

## 🔗 Links

- **Subreddit**: [r/Omencore](https://reddit.com/r/Omencore)
- **GitHub Issues**: [Report bugs](https://github.com/theantipopau/omencore/issues)
- **Discussions**: [GitHub Discussions](https://github.com/theantipopau/omencore/discussions)
- **Website**: [omencore.info](https://omencore.info)

---

*OmenCore v1.2.0 - A major step forward in reliability, performance, and features!*
