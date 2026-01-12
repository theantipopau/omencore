# OmenCore v2.3.1 - Bug Fix Release

**Release Date:** 2026-01-12

This is a critical bug fix release addressing a thermal shutdown issue during gaming and adding highly requested OSD enhancements.

---

## 🐛 Critical Bug Fixes

### 🔴 **Battlefield 6 Thermal Shutdown Fix**
- **Fixed**: Storage drive sleep causing `SafeFileHandle` disposal crash in HardwareWorker
- **Symptom**: During intense gaming (RTX 4090 @ 87°C), when storage drives went to sleep, temperature monitoring would crash, preventing fan curves from responding to heat → thermal shutdown
- **Root cause**: LibreHardwareMonitor's storage device access would throw `ObjectDisposedException` when drives slept, cascading through the update loop and stopping CPU/GPU monitoring
- **Solution**: Added per-device exception isolation - storage failures no longer affect CPU/GPU monitoring
- **Impact**: Prevents thermal shutdowns during extended gaming sessions when drives go to sleep
- **Files**: `Program.cs` (HardwareWorker)

**Technical details**:
```csharp
// OLD: Single try-catch around all hardware - storage failure stops everything
hardware.Update(); // ← Throws when storage sleeps, stops CPU/GPU monitoring

// NEW: Per-device isolation - storage fails independently
try {
    hardware.Update();
} catch (ObjectDisposedException) when (hardware.HardwareType == HardwareType.Storage) {
    continue; // Skip sleeping storage, CPU/GPU monitoring continues
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
- u/matth (Discord) - Battlefield 6 thermal shutdown crash logs
- u/unknown (Discord) - OSD horizontal layout request
- u/unknown (Discord) - Network speed display request
- u/unknown (Discord) - Polling interval confusion

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
- OSD horizontal layout toggle in Settings UI
- OSD preset layouts (Minimal, Standard, Full, Custom)
- More robust storage device exclusion (ignore all HDD/SSD sleep)

### v2.4.0 (Planned)  
- Per-game OSD profiles (different metrics per game)
- OSD FPS counter via D3D11 hook (accurate frame rate)
- OSD layout editor (drag & drop metric placement)
