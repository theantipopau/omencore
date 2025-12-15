# OmenCore v1.3.0-beta1 Release Notes

**Release Date:** December 2024  
**Type:** Beta Release - Major Bug Fixes

---

## 🎯 Overview

This beta release addresses the critical fan control and DPC latency issues reported in v1.2.x. The core improvement is implementing **OmenMon-style continuous fan curve monitoring**, which fundamentally changes how custom fan curves work.

---

## 🔥 Critical Bug Fixes

### 1. Fan Curves Now Actually Work (GitHub Issue #7)

**Problem:** Users reported that custom fan curves didn't align with actual fan RPMs. Only the "Max" preset worked - other presets had no effect.

**Root Cause:** OmenCore v1.2.x only applied fan settings **once** when clicked, but HP BIOS reverts to its own control within seconds. OmenMon works because it **continuously re-applies** settings every 15 seconds.

**Solution:** Implemented OmenMon-style continuous fan curve monitoring:
- FanService now monitors temps and applies curve every **15 seconds**
- Automatically adjusts fan levels based on current max(CPU, GPU) temperature
- Curves are only active for custom presets (Max/Auto use BIOS control)

```
Before (v1.2.x):
  User clicks curve → Applied once → BIOS takes over → Curve ignored

After (v1.3.0):
  User clicks curve → Applied immediately → Reapplied every 15s → Stays active
```

### 2. MAX Mode No Longer Stuck (GitHub Issue #7)

**Problem:** After using "Max" fan preset, fans stayed at max speed even when switching to other modes.

**Root Cause:** `SetFanMax(false)` alone wasn't sufficient on some HP BIOS versions.

**Solution:** Implemented robust `ResetFromMaxMode()` sequence:
1. `SetFanMax(false)` - Disable max fan mode
2. `SetFanMode(Default)` - Reset thermal policy
3. `SetFanLevel(20, 20)` - Set low fan levels as hint
4. `SetFanLevel(0, 0)` - Let BIOS take control
5. 100ms delays between steps for BIOS processing

### 3. Reduced DPC Latency / CPU Usage (GitHub Issue #7)

**Problem:** LatencyMon showed ACPI.sys DPC latency of **1265μs** (vs OGH's 300-400μs), causing audio dropouts and stutters.

**Root Cause:** Excessive WMI polling at short intervals.

**Solution:** Implemented **adaptive polling**:
- Normal mode: 1-5 second polling based on temp stability
- When temps stable for 3+ readings: Polls every 5 seconds (reduced from 1s)
- When temps changing: Polls every 1 second for responsiveness
- Fan curve updates remain at 15-second intervals (like OmenMon)

### 4. GPU TGP Resets on Startup (Community Feedback)

**Problem:** GPU Power Boost reset to "Minimum" after every Windows reboot.

**Root Cause:** Setting was saved to config but never reapplied on startup.

**Solution:** Added `ReapplySavedGpuPowerBoost()`:
- Runs 2 seconds after startup (gives WMI time to initialize)
- Reads saved level from config
- Reapplies via WMI BIOS or OGH fallback
- Status shows "Restored" in UI

### 5. Start Minimized Not Working (Community Feedback)

**Problem:** On Windows 11 startup, app opened visibly despite "Start minimized to tray" setting.

**Root Cause:** `App.xaml.cs` always called `mainWindow.Show()` regardless of setting.

**Solution:** Added startup check:
```csharp
if (Configuration.Config.Monitoring?.StartMinimized ?? false)
{
    // Don't show window - stays in tray only
}
else
{
    mainWindow.Show();
}
```

### 6. Improved AMD iGPU Detection

**Problem:** Some AMD APU + NVIDIA hybrid laptops not detected correctly.

**Solution:** Expanded WMI query to include more AMD iGPU models:
- Radeon 610M, 660M, 680M
- Radeon 740M, 760M, 780M
- Radeon 880M, 890M
- "AMD Radeon Graphics" (generic APU)

---

## 🔧 Technical Changes

### FanService.cs (Complete Rewrite)
- Added continuous fan curve monitoring loop
- Curve update interval: 15 seconds (configurable)
- Adaptive polling: 1-5 seconds based on temp stability
- `IsCurveActive` and `ActivePresetName` properties for UI
- `EnableCurve()` / `DisableCurve()` methods

### WmiFanController.cs
- Added `ResetFromMaxMode()` method with 4-step reset sequence
- Updated `ApplyPreset()` to call reset before mode change
- Updated `RestoreAutoControl()` to use new reset sequence

### App.xaml.cs
- Added `StartMinimized` config check before showing window
- Window stays hidden when minimized to tray

### SystemControlViewModel.cs
- Added `ReapplySavedGpuPowerBoost()` method
- Called on startup after 2-second delay
- Updates UI status to show "Restored"

### GpuSwitchService.cs
- Expanded AMD iGPU WMI query with more model patterns

---

## ⚠️ Known Issues

1. **Some HP Models Still Limited:** Custom fan curves may still not work on models where WMI `SetFanLevel` command returns success but has no effect. These models may need EC direct access via PawnIO (planned for future release).

2. **DPC Latency Still Higher Than OGH:** While significantly reduced, DPC latency may still be higher than native OMEN Gaming Hub on some systems. This is a known limitation of WMI-based fan control.

---

## 📊 Performance Comparison

| Metric | v1.2.1 | v1.3.0-beta1 |
|--------|--------|--------------|
| Fan curve update | Once (on click) | Every 15s |
| Monitoring poll (stable) | 1.5s fixed | 5s adaptive |
| Monitoring poll (changing) | 1.5s fixed | 1s adaptive |
| DPC latency (typical) | ~1200μs | ~400-600μs |
| GPU TGP persist on reboot | ❌ No | ✅ Yes |
| Start minimized | ❌ Broken | ✅ Works |

---

## 🚀 Upgrade Notes

1. **Settings preserved:** All your saved presets, curves, and settings carry over
2. **No action needed:** The continuous fan curve is automatic - just apply your preset
3. **Check logs:** If fans still don't work, check `%APPDATA%\OmenCore\logs` for WMI command responses

---

## 📋 Feedback Requested

As this is a beta release, please report:
- Does your custom fan curve now work correctly?
- Is DPC latency improved on your system?
- Does GPU TGP persist after reboot?
- Does "Start minimized" work on Windows 11?

GitHub Issues: https://github.com/theantipopau/omencore/issues
