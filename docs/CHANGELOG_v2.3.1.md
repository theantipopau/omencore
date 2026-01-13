# OmenCore v2.3.1 - Bug Fix Release

**Release Date:** 2026-01-12

This is a critical bug fix release addressing thermal shutdown issues, fan control bugs, and adding highly requested OSD enhancements.

---

## 🐛 Critical Bug Fixes

### 🔴 **Battlefield 6 Thermal Shutdown Fix**
- **Fixed**: Storage drive sleep causing `SafeFileHandle` disposal crash in HardwareWorker
- **Symptom**: During intense gaming (RTX 4090 @ 87°C), when storage drives went to sleep, temperature monitoring would crash, preventing fan curves from responding to heat → thermal shutdown
- **Root cause**: LibreHardwareMonitor's storage device access would throw `ObjectDisposedException` when drives slept, cascading through the update loop and stopping CPU/GPU monitoring
- **Solution**: Added per-device exception isolation - storage failures no longer affect CPU/GPU monitoring
- **Impact**: Prevents thermal shutdowns during extended gaming sessions when drives go to sleep
- **Files**: `Program.cs` (HardwareWorker)

### 🔴 **Fan Drops to 0 RPM at 60-70°C Fix** (NEW)
- **Fixed**: Fans would boost high during gaming, then suddenly drop to 0 RPM when temps were still 60-70°C
- **Symptom**: Reported by Solar & kastenbier2743 - "Fans shoot up then go to 0 RPM" on Victus 16 and OMEN Max 16
- **Root cause**: When thermal protection released at 75°C, the code immediately restored BIOS auto-control, which on some HP laptop firmware would set fans to 0 RPM even at warm temps
- **Solution**: Added "safe release" temperature (55°C) and minimum fan floor (30%) when releasing thermal protection
  - If temps are above 55°C when thermal protection releases, keep fans at minimum 30% instead of handing back to BIOS
  - Prevents aggressive BIOS fan stopping at gaming-warm temperatures
- **Files**: `FanService.cs`

**Technical details**:
```csharp
// OLD: Release at 75°C and immediately restore BIOS auto-control
if (_thermalProtectionActive && maxTemp < ThermalProtectionThreshold - 5) {
    _fanController.RestoreAutoControl(); // BIOS might set fans to 0!
}

// NEW: Safe release - keep minimum fan floor if still warm
bool stillWarm = maxTemp >= ThermalSafeReleaseTemp; // 55°C
if (stillWarm) {
    _fanController.SetFanSpeed(ThermalReleaseMinFanPercent); // 30% minimum
} else {
    _fanController.RestoreAutoControl(); // Only if truly cool
}
```

---

## ✨ New Features

### 📊 **OSD Network Traffic Monitoring**
- Added **upload speed** display (Mbps)
- Added **download speed** display (Mbps)  
- Auto-detects active network interface (Ethernet/WiFi)
- Updates every 5 seconds alongside ping monitoring
- Shows "k" suffix for speeds < 1 Mbps (e.g., "750k")
- **Settings**:
  - `OsdSettings.ShowNetworkUpload` - Toggle upload speed display
  - `OsdSettings.ShowNetworkDownload` - Toggle download speed display
- **Display format**: 
  - Upload: Blue arrow ↑ (42A5F5)
  - Download: Green arrow ↓ (66BB6A)
- **Files**: `OsdOverlayWindow.xaml`, `OsdOverlayWindow.xaml.cs`, `AppConfig.cs`

### 🌡️ **Adjustable Thermal Protection Threshold**
- **NEW**: Thermal protection threshold is now configurable from 70°C to 90°C (default 80°C)
- Advanced users can increase to 85-90°C if their laptop handles heat better
- Setting in: **Settings → Fan Hysteresis → Thermal Protection Threshold**
- **Files**: `FanHysteresisSettings`, `FanService.cs`, `SettingsViewModel.cs`, `SettingsView.xaml`

### 📐 **OSD Horizontal Layout Option** (UI Framework)
- Added layout toggle: **Settings → OSD → Layout → Horizontal Layout**
- Stores preference in config (`OsdSettings.Layout = "Vertical" | "Horizontal"`)
- Full horizontal XAML implementation coming in v2.3.2
- **Files**: `OsdSettings`, `SettingsViewModel.cs`, `SettingsView.xaml`

### 📐 **Window Sizing for Multi-Monitor**
- **Improved**: Window can now be resized smaller for secondary monitors
- Reduced minimum width from 1100px to 900px
- Reduced minimum height from 700px to 600px
- Works better with smaller/vertical secondary monitors
- **Files**: `MainWindow.xaml`

### 🐧 **Linux Kernel 6.18 Documentation**
- Added notes about upcoming Linux kernel 6.18 HP-WMI improvements
- Better native fan curve control via sysfs
- Improved thermal profile switching
- **Files**: `README.md`

---

## ⚙️ Configuration Changes

### OsdSettings Model
Added new properties:
```json
{
  "ShowNetworkUpload": false,
  "ShowNetworkDownload": false
}
```

---

## 📝 Known Issues

- **OSD Horizontal Layout**: Framework added, UI toggle pending (coming in v2.3.2)
- **Polling Interval Confusion**: Clarified in FAQ - polling interval (1500ms) only affects UI updates, NOT fan response speed (fan curve runs every 10s independently)

---

## 🎯 User Questions Answered

### Q: Does polling interval change how fast fans react to temperature changes?
**A**: No. Polling interval (default 1500ms) only controls how often the **UI updates** display values. The fan curve engine runs **independently every 10 seconds** and reads temperatures directly from WMI/EC. Changing polling to 500ms just makes the UI more visually responsive - it doesn't make fans react faster.

### Q: Should I change the default polling interval?
**A**: No, leave it at 1500ms unless you want smoother UI animations. Lowering it uses more CPU for minimal visual benefit.

---

## 🔧 Technical Changes

### HardwareWorker
- **Enhanced error isolation**: Each hardware device now updates in isolated try-catch blocks
- **Storage-specific handling**: ObjectDisposedException from Storage devices doesn't propagate to CPU/GPU monitoring
- **Rate-limited logging**: Storage sleep errors logged once per hour (normal behavior, not spam logs)
- **Graceful degradation**: If one hardware device fails, others continue updating

### OSD Overlay
- **Network monitoring**: Uses `NetworkInterface.GetIPv4Statistics()` to track byte deltas
- **Auto-detection**: Finds active Ethernet/WiFi interface, ignores virtual adapters/VPN
- **Performance**: Updates every 5 seconds (same as ping), minimal network overhead

---

## 📦 Download

### Windows
- **OmenCoreSetup-2.3.1.exe** - Full installer with auto-update
  - SHA256: `TBD`
- **OmenCore-2.3.1-win-x64.zip** - Portable version
  - SHA256: `TBD`

### Linux
- **OmenCore-2.3.1-linux-x64.zip** - GUI + CLI bundle
  - SHA256: `TBD`

---

## 🙏 Credits

**Bug Reports**:
- **matth** (Discord) - Battlefield 6 thermal shutdown crash logs
- **Solar** (Discord) - Fan drops to 0 RPM bug report (Victus 16)
- **kastenbier2743** (Discord) - Fan 0 RPM confirmation + adjustable thermal limit request (OMEN Max 16)
- **SimplyCarrying** (Discord) - OSD horizontal layout request
- **replaY!** (Discord) - Window resizing for multi-monitor
- **vuvu** (Discord) - Linux kernel 6.18 HP-WMI notes

**Testing**:
- Community testing on Discord

---

## 📖 Upgrade Notes

This is a **critical bug fix release** - all v2.3.0 users should update immediately if experiencing:
- Thermal shutdowns during gaming
- Temperature readings freezing after storage drives sleep
- Fans not responding to heat during extended sessions

**Breaking changes**: None - fully compatible with v2.3.0 configs.

---

## 🚀 What's Next?

### v2.3.2 (Planned)
- OSD horizontal layout full XAML implementation
- OSD preset layouts (Minimal, Standard, Full, Custom)
- More robust storage device exclusion (ignore all HDD/SSD sleep)

### v2.4.0 (Planned)  
- Per-game OSD profiles (different metrics per game)
- OSD FPS counter via D3D11 hook (accurate frame rate)
- OSD layout editor (drag & drop metric placement)
