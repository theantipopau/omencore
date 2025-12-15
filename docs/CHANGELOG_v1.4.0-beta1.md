# OmenCore v1.4.0-beta1 Changelog

**Release Date:** December 16, 2025  
**Status:** Beta  
**Focus:** Bug fixes and UI improvements based on v1.3.0-beta2 community feedback

---

## ✨ New Features

### Fan Profile UI Redesign
- **Unified preset selector**: Replaced confusing "Quick Presets" buttons + "Choose Preset" dropdown with a single card-based interface
- **Visual preset cards**: Max, Gaming, Auto, Silent, and Custom modes now shown as clickable cards with icons
- **Active mode indicator**: Current fan mode clearly displayed
- **Streamlined layout**: Cleaner, more intuitive fan control experience

### Undervolt Status Improvements
- **Informative error messages**: When undervolting is not available, shows detailed explanation of why (Intel Plundervolt, AMD Curve Optimizer)
- **CPU-specific guidance**: Different explanations for Intel vs AMD processors
- **Alternative suggestions**: Points users to BIOS settings or manufacturer tools when OmenCore can't help

### Documentation
- **Antivirus FAQ**: New comprehensive guide explaining why AV software may flag OmenCore and how to whitelist it
- **Whitelist instructions**: Step-by-step guides for Windows Defender, Avast, Bitdefender, Kaspersky, Norton, ESET

---

## 🐛 Bug Fixes

### Critical Fixes

#### 1. TCC Offset (CPU Temperature Limit) Now Persists Across Reboots
- **Issue:** CPU temperature limit reset to 100°C after PC restart
- **Fix:** TCC offset is now saved to config when applied and automatically restored on startup
- **Files:** `SystemControlViewModel.cs`, `AppConfig.cs`
- **Technical:** Added `LastTccOffset` config property with startup restore logic and verification

#### 2. GPU Power Boost Restoration Improved
- **Issue:** GPU TGP/Dynamic Boost sometimes reset to Minimum after reboot
- **Status:** Existing restore logic verified; startup delay ensures WMI BIOS is ready
- **Files:** `SystemControlViewModel.cs`

#### 3. Thermal Protection Made More Aggressive
- **Issue:** Fans were too gentle - CPU reached 85-90°C before ramping
- **Fix:** Lowered thermal protection thresholds:
  - **Warning threshold:** 90°C → **80°C** (fans start ramping at 70%)
  - **Emergency threshold:** 95°C → **88°C** (100% fans immediately)
  - **Release threshold:** 85°C → **75°C** (5°C hysteresis)
- **Files:** `FanService.cs`
- **Technical:** Fan ramp formula now: 70% + 3.75% per °C above 80°C

#### 4. Auto-Start Detection Fixed
- **Issue:** "Start with Windows" toggle didn't correctly detect existing startup entries
- **Fix:** Now checks both Task Scheduler AND registry for startup entries
- **Files:** `SettingsViewModel.cs`
- **Technical:** Added `CheckStartupTaskExists()` and `CheckStartupRegistryExists()` helper methods

### UI/UX Fixes

#### 5. OSD Overlay: Added TopCenter and BottomCenter Positions
- **Request:** Users wanted OSD at top-center of screen
- **Fix:** Added two new position options: TopCenter, BottomCenter
- **Files:** `OsdOverlayWindow.xaml.cs`, `SettingsViewModel.cs`, `AppConfig.cs`

#### 6. Tray Menu Refresh Rate Display Now Updates
- **Issue:** After changing refresh rate, tray popup still showed old value (e.g., "60Hz" after switching to 144Hz)
- **Fix:** Tray menu item header now updates immediately after changing refresh rate
- **Files:** `TrayIconService.cs`

#### 7. Undervolt Section Hides When Not Supported
- **Issue:** Undervolt controls visible on AMD Ryzen systems that don't support it
- **Fix:** CPU Undervolting section in Advanced view now hides when `IsUndervoltSupported` is false
- **Files:** `AdvancedView.xaml`, `SystemControlViewModel.cs`
- **Technical:** Added `IsUndervoltSupported` property with visibility binding

#### 8. Lighting ViewModel: Added Device Availability Properties
- **Improvement:** Added `HasCorsairDevices` and `HasLogitechDevices` properties
- **Purpose:** Allows UI to conditionally show/hide peripheral sections
- **Files:** `LightingViewModel.cs`

---

## 📋 Known Issues (Deferred to v1.4.0-beta2)

### Not Fixed in This Release

1. **Fan Profile UI Redundancy** (BUG-10)
   - Quick Presets (Max, Gaming, Auto, Silent) vs Choose Preset dropdown still confusing
   - Requires significant UI refactoring
   
2. **System Restore Point Creation** (BUG-1)
   - May fail on non-English Windows or when System Restore is disabled
   - Error: "No encontrado" (Spanish for "Not found")
   
3. **CPU Undervolt MSR Blocked** (BUG-2)
   - Intel Plundervolt patches block MSR 0x152 writes on most modern systems
   - AMD Ryzen doesn't support voltage offset via this method
   - Informative error message planned

4. **Antivirus False Positives** (BUG-13)
   - WinRing0 driver triggers heuristic detection
   - Code signing certificate under consideration

---

## 🔧 Technical Changes

### AppConfig Changes
```csharp
// New property for TCC offset persistence
public int? LastTccOffset { get; set; }

// OSD position now supports 6 options
// TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight
```

### FanService Thermal Protection
```csharp
// Old thresholds
private const double ThermalProtectionThreshold = 90.0;
private const double ThermalEmergencyThreshold = 95.0;

// New thresholds (more aggressive)
private const double ThermalProtectionThreshold = 80.0;
private const double ThermalEmergencyThreshold = 88.0;
```

### TrayIconService
```csharp
// New method to update refresh rate display
private void UpdateRefreshRateMenuItem()
```

---

## 📦 Installation

### Fresh Install
1. Download `OmenCore-v1.4.0-beta1-Setup.exe`
2. Run installer (may require admin rights)
3. Launch OmenCore from Start Menu or desktop shortcut

### Upgrade from v1.3.x
1. Close OmenCore completely (exit from system tray)
2. Run the new installer - it will upgrade in place
3. Your settings and custom fan curves are preserved

---

## 🧪 Testing Notes

### What to Test
- [ ] Set TCC offset, reboot PC, verify it's restored
- [ ] Set GPU Power Boost to Maximum, reboot, verify it's restored
- [ ] Enable "Start with Windows", reboot, verify OmenCore starts
- [ ] Run CPU stress test, verify fans ramp at 80°C, not 90°C
- [ ] Change refresh rate from tray, verify tray menu shows new value
- [ ] Change OSD position to TopCenter/BottomCenter, verify positioning
- [ ] On AMD system, verify undervolt section is hidden

### Thermal Protection Test
1. Set fans to "Auto" mode
2. Run a CPU stress test (Prime95, Cinebench)
3. Monitor temperatures
4. Expected: Fans should start ramping at 80°C
5. Expected: Fans should hit 100% by 88°C

---

## 📝 Community Feedback Addressed

Based on reports from:
- Omen 17-ck2xxx users (thermal issues)
- Omen 15-en0027ur user (AMD Ryzen, UI feedback)
- Omen Max 16 users (TCC/undervolt issues)
- Multiple users (refresh rate display, auto-start)

Thank you for the detailed bug reports and logs! 🙏

---

## 🔗 Links

- **GitHub:** https://github.com/theantipopau/omencore
- **Issues:** https://github.com/theantipopau/omencore/issues
- **v1.4 Roadmap:** [ROADMAP_v1.4.md](ROADMAP_v1.4.md)

---

## SHA256 Checksum
```
OmenCoreSetup-1.4.0-beta1.exe: 3CC9A70E5DF8AA626676C9BC040855698DA463B0401FB4CB25BF36D7A62F0CF7
```
