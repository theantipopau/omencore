# OmenCore v2.6.1 - Bug Fix Release

**Release Date:** January 2025

This is a bug fix release addressing issues reported via Discord after the v2.6.0 release.

---

## 🐛 Bug Fixes

### 🌀 Fan Max Mode from Quick Access (Critical)

**Issue:** When selecting "Max" from the system tray Quick Access menu, the fan mode would show as "Performance" and fans wouldn't actually run at maximum RPM.

**Root Cause:** 
1. The preset search in `SetFanModeFromTray` used `FirstOrDefault(p => p.Name.Contains("Max") || p.Name.Contains("Performance"))` which would find "Performance" first due to preset list ordering
2. The `ApplyPreset` call was missing `immediate: true` parameter
3. The OGH proxy controller mapped "Max" to `ThermalPolicy.Performance` instead of calling `SetMaxFan(true)`
4. The default "Max" preset in configuration was missing `Mode = FanMode.Max`

**Fixes Applied:**
- Changed preset lookup to prioritize exact "Max" match: `FirstOrDefault(p => p.Name.Equals("Max", ...)) ?? FirstOrDefault(p => p.Name.Contains("Max", ...))`
- Added `immediate: true` parameter to `ApplyPreset` call so max fans are applied immediately
- Added explicit `SetMaxFan(true)` call in OGH proxy's `ApplyPreset` method when preset Mode is Max or name is "Max"
- Added `Mode = FanMode.Max` to the default "Max" preset in `DefaultConfiguration.cs`

**Files Modified:**
- `ViewModels/MainViewModel.cs` - Fixed preset lookup and immediate apply
- `Hardware/FanControllerFactory.cs` - Fixed OGH ApplyPreset to call SetMaxFan
- `Services/DefaultConfiguration.cs` - Added Mode property to Max preset

---

### 🌡️ Temperature Freezing / Stuck Readings

**Issue:** Temperature readings would sometimes freeze, requiring a full app restart to recover.

**Root Cause:** The stuck temperature detection logic waited too long (40 seconds) before attempting recovery, and didn't have a permanent fallback mechanism when LibreHardwareMonitor repeatedly failed.

**Improvements:**
- Reduced `MaxSameTempReadingsBeforeLog` from 10 to 5 readings (10s → 10s @ 2s poll)
- Reduced `MaxSameTempReadingsBeforeReinit` from 20 to 10 readings (40s → 20s @ 2s poll)
- Added `_reinitializeAttempts` counter with `MaxReinitializeAttempts = 3`
- Added `_forceWmiBiosMode` flag for permanent WMI fallback after repeated failures
- Enhanced stuck detection to try WMI BIOS immediately at warning threshold
- Added bypass for WMI-only mode in CPU temperature reading when LHM repeatedly fails

**Logic Flow:**
1. After 5 identical temp readings: Log warning, try WMI BIOS fallback immediately
2. If WMI gives different temp: Use it, reset counters
3. After 10 identical readings: Try WMI BIOS, then full reinitialize if needed
4. After 3 failed reinitialize attempts: Switch to WMI-only mode permanently
5. In WMI-only mode: Skip LibreHardwareMonitor entirely for temperature

**Files Modified:**
- `Hardware/LibreHardwareMonitorImpl.cs` - Improved stuck detection and WMI fallback

---

## 📝 Technical Details

### Version Updates
- VERSION.txt: `2.6.0` → `2.6.1`
- OmenCoreApp.csproj: AssemblyVersion/FileVersion updated
- Installer: OmenCoreInstaller.iss version updated
- TrayIconService: Tooltip version updated
- UpdateCheckService: CurrentVersion updated
- DiagnosticsExportService: Export version updated
- ProfileExportService: Export version updated

---

## ⬆️ Upgrade Notes

This is a drop-in replacement for v2.6.0. Simply install over your existing installation or replace the portable files.

### Recommended For:
- Users experiencing "Max" fan mode not working from tray
- Users experiencing frozen temperature readings
- All users on v2.6.0 (no breaking changes)

---

## 📦 Downloads

### Windows
| File | SHA256 |
|------|--------|
| `OmenCoreSetup-2.6.1.exe` | `361D8A4F6B886041C9EF043C3F38F2E359D0C254C506C5FDE8A03A8129C5754F` |
| `OmenCore-2.6.1-win-x64-portable.zip` | `8AFA57A3D19D65715ED8074505A1DE50859EDE8D33FD36B2BBA1FE7804A6AEB8` |

### Linux
| File | SHA256 |
|------|--------|
| `OmenCore-2.6.1-linux-x64.zip` | `0CD404CE33398D5DBB4F3951D8DD422B34C07EC400B631A3BA9337AF865B8DEA` |

---

## 🙏 Thanks

Thanks to the Discord community for reporting these issues with detailed logs and steps to reproduce!
