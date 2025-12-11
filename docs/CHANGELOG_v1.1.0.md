# OmenCore v1.1.0 Changelog

## 🚀 OmenCore v1.1.0 - Code Quality & Enhanced Monitoring

**Release Date**: December 2024  
**Download**: [GitHub Releases](https://github.com/theantipopau/omencore/releases/tag/v1.1.0)

---

## ✨ New Features

### 🔧 BIOS Update Checker
- Check for HP BIOS updates directly from OmenCore
- View current BIOS version and date
- Quick link to HP Support page for your specific model

### 📤 Fan Profile Import/Export
- Export your custom fan curves to JSON files
- Import fan profiles from other OmenCore users
- Share your optimized fan configurations with the community

### 📊 Enhanced GPU Monitoring
- **GPU Power Draw** - Real-time wattage consumption
- **GPU Core Clock** - Current GPU frequency
- **GPU Memory Clock** - VRAM clock speed
- **GPU VRAM Total** - Total video memory
- **GPU Fan Speed** - Fan percentage
- **GPU Hotspot Temperature** - Junction/hotspot temp (if available)

### 🔔 Advanced Thermal Alerts
- Configurable CPU/GPU warning thresholds
- Critical temperature notifications
- SSD temperature monitoring
- Alert cooldown to prevent notification spam
- Driver issue notifications

### 💻 HP Victus Support
- Full support for HP Victus gaming laptops
- Same fan control and monitoring features as OMEN

---

## 🛠️ Code Quality Improvements

### High Priority Fixes
1. **Async Exception Handling** - All `async void` methods now have proper try/catch blocks
2. **Memory Leak Prevention** - Fixed event handler memory leaks with proper unsubscription in Dispose()
3. **Deadlock Prevention** - Changed all `Dispatcher.Invoke` to `BeginInvoke` to prevent UI deadlocks

### Performance Optimizations
- Improved hardware monitoring efficiency
- Reduced UI thread blocking
- Better resource cleanup on application exit

---

## 📝 Technical Details

### Files Modified
- `MainViewModel.cs` - Event unsubscription, Dispatcher fixes
- `DashboardViewModel.cs` - Dispatcher fixes
- `SettingsViewModel.cs` - Dispatcher fixes
- `GameProfileManagerViewModel.cs` - Exception handling
- `FanControlViewModel.cs` - Import/Export commands
- `TrayIconService.cs` - Dispatcher fixes
- `MonitoringSample.cs` - Enhanced GPU metrics
- `LibreHardwareMonitorImpl.cs` - GPU metric collection
- `NotificationService.cs` - New notification types

### New Files
- `BiosUpdateService.cs` - HP BIOS update checking
- `ThermalMonitoringService.cs` - Thermal threshold monitoring

---

## 📥 Installation

### Fresh Install
1. Download `OmenCoreSetup-1.1.0.exe` from [Releases](https://github.com/theantipopau/omencore/releases/tag/v1.1.0)
2. Run the installer (requires Administrator)
3. Launch OmenCore from the Start Menu

### Upgrade from v1.0.x
1. Close OmenCore if running
2. Run the new installer - it will upgrade in place
3. Your settings and profiles are preserved

---

## 🐛 Bug Fixes
- Fixed potential deadlocks when updating UI from background threads
- Fixed resource leaks in WinRing0 driver access
- Fixed event handler memory leaks in MainViewModel
- Fixed async void methods that could crash silently

---

## 📊 Changelog Summary

| Category | Changes |
|----------|---------|
| New Features | 4 |
| Bug Fixes | 4 |
| Code Quality | 3 |
| Files Modified | 9 |
| New Files | 2 |

---

## 💬 Community

- **Subreddit**: [r/omencore](https://reddit.com/r/omencore)
- **GitHub Issues**: [Report bugs](https://github.com/theantipopau/omencore/issues)
- **Discussions**: [GitHub Discussions](https://github.com/theantipopau/omencore/discussions)

---

*Thank you to everyone who reported bugs and suggested features! Your feedback makes OmenCore better for everyone.*
