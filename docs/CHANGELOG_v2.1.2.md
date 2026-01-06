# Changelog v2.1.2

All notable changes to OmenCore v2.1.2 will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.1.2] - 2026-01-06

### 🐛 Bug Fixes

#### 🌡️ Temperature Freeze Fix (Windows)
- **Issue:** CPU/GPU temperatures would freeze and stop updating ([GitHub #27](https://github.com/theantipopau/omencore/issues/27))
- **Cause:** When a hardware component threw a SafeFileHandle disposed error (e.g., drive going to sleep), the entire sample would reset to zeros
- **Fix:** Hardware worker now preserves last known good values when a hardware component fails to update
- **Result:** Temperatures now continue displaying even if a storage drive or other component goes to sleep

#### 🖥️ OMEN Max 2025+ Model Detection (V2 Thermal Policy)
- **Issue:** OMEN Max 2025+ models (like 16-ah0xxx with RTX 5080) showing 0 RPM fans constantly
- **Cause:** Some OMEN Max models report V1 thermal policy to BIOS but actually need V2 commands
- **Fix:** Added model name detection - if "OMEN" and "MAX" are in the model name, forces V2 thermal policy
- **Result:** OMEN Max users should now see proper fan RPM readings

#### 🐧 Linux: Cannot Return to Auto Fan Mode ([GitHub #27](https://github.com/theantipopau/omencore/issues/27))
- **Issue:** After setting manual fan speeds, `--profile auto` didn't restore BIOS control
- **Cause:** Auto mode only reset fan state register but not BIOS control register
- **Fix:** `RestoreAutoMode()` now properly resets:
  - BIOS control register (0x62) to 0x00
  - Fan state register (0xF4) to 0x00
  - Fan boost to disabled
  - Timer register to 0x00
  - Manual speed registers to 0x00 (let BIOS control)
- **Result:** `omencore-cli fan --profile auto` now properly restores automatic fan control

#### 🐧 Linux: HP-WMI Support for Newer Models ([GitHub #28](https://github.com/theantipopau/omencore/issues/28))
- **Issue:** OMEN 16 ae0000 and other 2023+ models had no fan control on Linux
- **Cause:** These models use different EC register addresses; the omen-fan register map is for older models
- **Fix:** Added hp-wmi driver support as alternative to EC access:
  - Auto-detects `/sys/devices/platform/hp-wmi/`
  - Uses `thermal_profile` for performance modes (quiet/balanced/performance)
  - Uses `fan_always_on` for max mode
  - Falls back to EC registers for older models
- **Result:** Newer OMEN models can use `sudo modprobe hp-wmi` instead of EC access

### 📋 Technical Details

**Temperature Freeze (HardwareWorker):**
```csharp
// Before: Started with empty sample each cycle
var sample = new HardwareSample();

// After: Preserves last known values if hardware fails
var sample = new HardwareSample
{
    CpuTemperature = _lastSample.CpuTemperature,
    GpuTemperature = _lastSample.GpuTemperature,
    // ... all other values preserved
};
```

**Linux Auto Mode Fix:**
```csharp
public bool RestoreAutoMode()
{
    WriteByte(REG_BIOS_CONTROL, 0x00);  // Re-enable BIOS control
    WriteByte(REG_FAN_STATE, 0x00);     // Enable auto state
    WriteByte(REG_FAN_BOOST, 0x00);     // Disable boost
    WriteByte(REG_TIMER, 0x00);         // Reset timer
    WriteByte(REG_FAN1_SPEED_SET, 0x00); // Clear manual speeds
    WriteByte(REG_FAN2_SPEED_SET, 0x00);
    return true;
}
```

**HP-WMI Support:**
```csharp
if (HasHpWmiAccess && File.Exists(HP_WMI_THERMAL))
{
    return SetHpWmiThermalProfile(profile);  // Use hp-wmi driver
}
// Fall back to EC registers for older models
```

---

### 🔄 Changes from v2.1.1

All fixes from v2.1.1 are included:
- ✅ Desktop detection and blocking (25L/30L/35L/40L/45L)
- ✅ Reduced default polling (1500ms → 2000ms)
- ✅ G-Helper style quick popup on left-click
- ✅ Fan speed reset under load fix
- ✅ Tray minimize on close fix
- ✅ Linux tar.gz release asset fix
- ✅ Performance mode reset fix

---

### 📦 Download

- **Windows Installer:** `OmenCoreSetup-2.1.2.exe` (recommended)
- **Windows Portable:** `OmenCore-2.1.2-win-x64.zip`
- **Linux (Wine):** `omencore-linux-2.1.2.tar.gz`

### 🔐 SHA256 Checksums

```
OmenCoreSetup-2.1.2.exe:        FA19F65E17086BCF37A0545052DCB53B5287019CCD668A5FC839A9CA4991B9A8
OmenCore-2.1.2-win-x64.zip:     7450A612686AB580ECD7E30AB0EDB2AA2B4C06099E9D5F42C86BD309503F29CC
omencore-linux-2.1.2.tar.gz:    34F7CFA19C0854D9318C79508AECAB0394B9778310B625D85D1228CB49A84E16
```

---

### 🆙 Upgrade Instructions

**From v2.1.1:**
1. Download `OmenCoreSetup-2.1.2.exe`
2. Run installer - it will upgrade in place
3. Settings and profiles are preserved

**Portable:**
1. Close OmenCore
2. Extract new version over old
3. Restart OmenCore

---

### 📝 Known Issues

- **OMEN Max GPU Power:** Some OMEN Max users report 175W GPU boost unavailable when CPU draws power - this appears to be a HP firmware limitation, not an OmenCore issue
- **OMEN Desktop RGB:** Desktop users wanting RGB-only support is on the roadmap for a future release

---

### 🙏 Thanks

Thanks to the community for reporting:
- Temperature freeze issue
- OMEN Max 16-ah0xxx fan reading issues
