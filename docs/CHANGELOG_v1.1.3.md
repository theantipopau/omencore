# OmenCore v1.1.3 Release Notes

**Release Date:** December 14, 2025  
**Status:** Bug fix release addressing v1.1.2 issues

---

## 🐛 Bug Fixes

### 1. .NET Runtime Installation Prompt (Issue #7 Related)

**Problem:** Users were getting a popup asking to install .NET 8.0 Desktop Runtime even though the app should be self-contained.

**Solution:** 
- Installer now automatically detects if .NET 8.0 Desktop Runtime is installed
- If missing, the installer downloads and installs it automatically from Microsoft
- Added clear messaging in the installer about .NET requirement
- No manual intervention required - seamless installation experience

### 2. Fan Mode Not Applying / Reverting to Auto ([GitHub Issue #7](https://github.com/theantipopau/omencore/issues/7))

**Problem:** Fan curves and modes were not being applied correctly. Users reported:
- Max fan mode not ramping fans to 100% immediately
- Fan modes reverting to "Auto" on their own
- No apparent change when switching between modes

**Root Causes Found:**
1. The `MapPresetToFanMode` function wasn't properly handling "Max" presets
2. For Max mode, the thermal policy wasn't being set to Performance before enabling max fan
3. The order of WMI BIOS commands was suboptimal

**Solution:**
- Improved `MapPresetToFanMode` to properly recognize Max preset and FanMode enum values
- For Max preset, now properly sets Performance thermal policy first, then enables SetFanMax
- Added fallback to direct fan level setting (55, 55) if SetFanMax fails
- Better logging to help diagnose fan control issues
- GPU power is now set to Maximum when Max fan preset is applied

### 3. High CPU Usage with UI Open (30W) ([GitHub Issue #7](https://github.com/theantipopau/omencore/issues/7))

**Problem:** Even with "Reduce CPU Usage" toggle enabled, the CPU was still pulling 30W with high utilization (70-80°C).

**Root Cause:** The low overhead mode only added 500ms to the polling interval, which was not enough reduction.

**Solution:**
- **5x polling interval** in low overhead mode (5000ms vs 1000ms default)
- **3°C change threshold** in low overhead mode (vs 0.5°C normally) to reduce UI updates
- **Extended hardware cache lifetime** (3 seconds vs 100ms) to minimize driver calls
- These changes dramatically reduce CPU wake-ups and hardware polling

### 4. DPC Latency Spikes (Similar to OmenMon)

**Problem:** Users reported high DPC latency spikes when using OmenCore, similar to what was experienced with OmenMon.

**Root Cause:** LibreHardwareMonitor calls kernel drivers for sensor readings, which can cause DPC latency spikes when polled frequently.

**Solution:**
- Added `SetLowOverheadMode` to LibreHardwareMonitor implementation
- Cache lifetime extended to 3 seconds in low overhead mode
- Reduced frequency of hardware.Update() calls
- When "Reduce CPU Usage" is enabled, hardware polling is significantly reduced

---

## 📊 Performance Improvements

| Setting | Before (v1.1.2) | After (v1.1.3) |
|---------|-----------------|----------------|
| Normal polling interval | 1000ms | 1000ms |
| Low overhead polling interval | 1500ms | **5000ms** |
| Normal change threshold | 0.5°C/% | 0.5°C/% |
| Low overhead change threshold | 0.5°C/% | **3.0°C/%** |
| Hardware cache (normal) | 100ms | 100ms |
| Hardware cache (low overhead) | 100ms | **3000ms** |

---

## 🔧 Technical Changes

### Files Modified

1. **installer/OmenCoreInstaller.iss**
   - Added .NET 8.0 Desktop Runtime detection and auto-download
   - New `IsDotNet80Installed` function checks registry and folder paths
   - Download page for runtime installer from Microsoft CDN
   - Updated version to 1.1.3

2. **src/OmenCoreApp/Hardware/WmiFanController.cs**
   - Improved `ApplyPreset` method for Max preset handling
   - Better `MapPresetToFanMode` with FanMode enum recognition
   - Added fallback to SetFanLevel when SetFanMax fails
   - Better logging for fan control operations

3. **src/OmenCoreApp/Services/HardwareMonitoringService.cs**
   - Added `_lowOverheadInterval` (5 seconds)
   - Added `_lowOverheadChangeThreshold` (3.0)
   - `SetLowOverheadMode` now also sets mode on the bridge

4. **src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs**
   - Added `SetLowOverheadMode` method
   - Configurable cache lifetime based on overhead mode
   - Better documentation about DPC latency considerations

---

## 📝 Recommendations for Users

### If Fan Control Isn't Working

1. **Check the backend** - Look at the status bar to see which fan control backend is active
2. **Try OGH Proxy** - If you have OMEN Gaming Hub installed, the OGH proxy backend may work better
3. **Check logs** - Look at the log file in `%APPDATA%\OmenCore\logs` for error messages
4. **Use Gaming preset** - For aggressive cooling, try the Gaming preset which uses Performance thermal policy

### If CPU Usage Is Still High

1. **Enable "Reduce CPU Usage"** toggle in the Dashboard
2. **Minimize to tray** - The app uses less CPU when minimized
3. **Close charts** - Temperature charts require more UI updates

### If Experiencing DPC Latency

1. **Enable "Reduce CPU Usage"** mode - This dramatically reduces hardware polling
2. **Run minimized to tray** for real-time audio/video work
3. DPC latency is inherent to hardware monitoring; lower polling = lower latency

---

## ⚠️ Known Limitations

1. **Fan curves via WMI BIOS** - Custom point-by-point curves are approximated to thermal policies (Cool/Default/Performance)
2. **Some 2023+ models** may require OGH services running for fan control to work
3. **.NET runtime** is now downloaded during installation if not present (~50MB download)

---

## 🔄 Upgrade Instructions

1. Download OmenCoreSetup-1.1.3.exe
2. Run the installer (it will upgrade the existing installation)
3. If prompted about .NET runtime, allow the download to complete
4. Restart OmenCore if it was running

---

## 📬 Feedback

If you continue to experience issues with fan control:
1. Open an issue on GitHub with your laptop model
2. Include the log file from `%APPDATA%\OmenCore\logs`
3. Note which fan backend is shown (WMI BIOS / OGH Proxy / EC)

