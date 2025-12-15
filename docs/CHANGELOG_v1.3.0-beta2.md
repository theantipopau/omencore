# OmenCore v1.3.0-beta2 Release Notes

**Release Date:** December 15, 2025  
**Type:** Beta Release - Bug Fixes + Enhancements

---

## 🔧 Bug Fixes

### 1. Fixed Tray Icon Version Display
**Problem:** System tray tooltip showed "v1.2.0" instead of actual version.  
**Fix:** Now reads version dynamically from assembly - displays correct "v1.3.0".

### 2. Fixed Intel XTU False Positive Detection
**Problem:** Undervolt showed "Intel XTU active" even when XTU wasn't installed.  
**Root Cause:** XTU detection was checking for process names, but Intel XTU runs as a Windows service, not a process.  
**Fix:** Now properly checks Windows services using ServiceController. Only blocks undervolt if XTU service is actually *running*.

### 3. Fixed OMEN Gaming Hub Detection After Uninstall
**Problem:** Settings tab still showed "OGH Installed ✓" after uninstalling OMEN Gaming Hub.  
**Root Cause:** Old detection checked only for processes and a registry key that might remain after uninstall.  
**Fix:** Improved detection now checks:
- Running processes (most accurate)
- Installation folders (Program Files\HP\OMEN Gaming Hub)
- UWP/Microsoft Store package registration

### 4. More Aggressive Fan Curves (Thermal Fix)
**Problem:** User reported CPU hitting 98°C with "Balanced" fan curve.  
**Root Cause:** Default curves were too conservative, ramping too slowly.  
**Fix:** All built-in curves now based on OmenMon's aggressive profiles:

| Temp   | Old Balanced | New Balanced |
|--------|--------------|--------------|
| 45°C   | 30%          | 30%          |
| 55°C   | ~38%         | 45%          |
| 65°C   | ~52%         | 60%          |
| 75°C   | ~68%         | 80%          |
| 85°C   | ~76%         | 100%         |

Gaming/Performance curves are even more aggressive - 100% by 78-80°C.

### 5. Wider Sidebar Panel
**Problem:** Sidebar navigation was too narrow on some displays.  
**Fix:** Increased default width from 200px to 220px (adjustable 200-260px).

### 6. Start Minimized to Tray
**Problem:** When enabled, the app didn’t always start minimized to the system tray reliably.  
**Fix:** Start minimized behavior is now consistent when the setting is enabled.

### 7. Max Fan Tray Action
**Problem:** Clicking “Max” in the tray/Quick Popup fan menu didn’t always enable true BIOS max-fan mode.  
**Fix:** Max now explicitly enables BIOS max-fan mode; Auto/Default disables max-fan and restores automatic control.
### 8. Update Check Now Works with Pre-Release Versions
**Problem:** "Check for Updates" reported "No updates available" even when a newer version existed.  
**Root Cause:** .NET's `Version.TryParse` doesn't support semantic versioning suffixes like `-beta2`, causing version parsing to fail silently.  
**Fix:** 
- Added custom semantic version parser that handles pre-release suffixes
- Proper version comparison: `1.3.0-beta1 < 1.3.0-beta2 < 1.3.0`
- Improved logging shows exact version comparison result

### 9. Thermal Protection Override (NEW)
**Problem:** CPU hitting 95°C+ with fans on Auto mode - HP BIOS too conservative.  
**Fix:** Added automatic thermal protection that overrides Auto mode:

| Temperature | Action |
|-------------|--------|
| ≥90°C | Fans ramp to 80%+ aggressively |
| ≥95°C | **EMERGENCY** - Fans forced to 100% immediately |
| <85°C | Protection releases, preset restored |

- Works even in Auto/Default mode
- Configurable via `fanHysteresis.thermalProtectionEnabled` in config

### 10. Thermal Protection Release Fix (NEW)
**Problem:** After thermal protection kicked in, fans stayed at 100% even when temps dropped to 65°C.  
**Root Cause:** When thermal protection released, fans weren't reset - BIOS doesn't automatically resume control.  
**Fix:** When temps normalize, the active preset (e.g., "Auto") is re-applied to restore BIOS control.

### 11. Keyboard Lighting Backend Fix (NEW)
**Problem:** HP OMEN keyboard lighting controls weren't working.  
**Root Cause:** `KeyboardLightingService` was created without the `HpWmiBios` instance.  
**Fix:** WMI BIOS backend now properly passed to keyboard service.

### 12. Inno Setup Warnings Fixed
**Problem:** Installer build showed deprecation warnings.  
**Fix:** Removed obsolete directives (`WizardResizable`, `WindowVisible`, etc.) and updated architecture identifier to `x64compatible`.

---

## ✨ Enhancements

### 1. Improved Keyboard RGB Control (OmenMon-Inspired)
- **OmenMon-compatible WMI BIOS calls** for better hardware support
- **Proper ColorTable format** with 128-byte structure matching OmenMon
- **Keyboard type detection** (Standard, WithNumPad, TenKeyLess, PerKeyRgb)
- **Backlight capability check** before attempting color changes
- **Zone order correction** for proper color mapping
- Automatic backlight enable when applying colors

### 2. Temperature Smoothing (Reduced Fluctuation)
**Problem:** Temperature readings fluctuated rapidly in the UI, making it hard to read.  
**Solution:** Added Exponential Moving Average (EMA) smoothing:
- Alpha = 0.3 provides smooth display while remaining responsive
- Temperature and load values now update smoothly
- Actual sensor data remains unaffected for fan curve calculations

### 3. OSD Overlay Improvements
- **Real CPU/GPU load values** now displayed (no longer estimated from temperature)
- Uses HardwareMonitoringService for accurate, smoothed data
- Better fallback handling when monitoring service unavailable

### 4. Enhanced Tray Tooltip
- **Battery status indicator** shows charge level and power source
- Dynamic icons: 🔌 AC, 🔋 Battery, 🪫 Low Battery
- All system metrics at a glance

### 5. Modern Update Banner
- New gradient design with deep blue/purple theme
- Version badge highlights the new version number
- File size displayed alongside version
- Glowing download button with hover effects
- Animated progress bar during download

### 6. Enhanced Installer
- Custom branded wizard images with OmenCore logo
- Improved welcome message highlighting features
- Better compression (LZMA2/ultra64) for smaller download
- Modern dark theme colors

### 8. Fan Presets Now Work Correctly
**Problem:** Fans didn't respond to non-max presets; only "Max / Performance" worked.  
**Root Cause:** Missing built-in "Quiet" preset caused Silent mapping to fail; tray/popup used wrong preset list.  
**Fix:** 
- Added built-in "Quiet" preset with gentle fan curve
- Tray/Quick Popup now uses authoritative preset list from FanControlViewModel
- Last applied fan preset is persisted and restored on app restart

### 9. GPU Power Boost Persistence (TGP)
**Problem:** GPU Power Boost level reset after Windows startup.  
**Root Cause:** Some OMEN models reset TGP multiple times during boot/login.  
**Fix:** Startup now retries applying saved GPU boost level at 2s, 10s, 30s, and 60s intervals.

### 10. OSD Overlay Fixed
**Problem:** OSD didn't appear when starting minimized or after changing hotkeys.  
**Root Cause:** Hotkey registration depended on MainWindow handle (unavailable when minimized).  
**Fix:** 
- OSD now uses dedicated hidden message window for hotkey registration
- OSD reinitializes immediately when settings change (no restart required)
- Works correctly even when app starts minimized to tray

### 11. OMEN Key Interception Fixed
**Problem:** OMEN key interception didn't trigger actions.  
**Root Cause:** Settings UI wrote to `FeaturePreferences`, but OmenKeyService read legacy `AppConfig` fields.  
**Fix:**
- OmenKeyService now reads from `FeaturePreferences` (same as Settings UI)
- Added "ShowQuickPopup" action (default) - opens the tray Quick Popup
- Hook restarts automatically when settings change (no restart required)
- Proper action mapping: ShowQuickPopup, ShowWindow, ToggleFanMode, TogglePerformanceMode

---

## ⚠️ Known Issues

### Keyboard RGB Control
- Per-key RGB keyboards (type 0x03) need additional implementation
- Some older models may still require EC access for full control
- Effects (wave, color cycle) require animation table support (WIP)
- **Fallback:** OmenMon CLI still recommended for full control

### Macro Editing
- Macro functionality is currently basic (record/playback only)
- For advanced macros, use Corsair iCUE or Logitech G HUB
- Full macro editor planned for v1.4.0

### OGH Cleanup Progress
- Cleanup process runs but lacks detailed progress feedback
- A progress dialog is planned for next release

---

## 📥 Installation

1. Download `OmenCoreSetup-1.3.0-beta2.exe`
2. Run installer as Administrator
3. If upgrading, your settings will be preserved

**SHA256:** `EF6DFA09D9C023E3A40AEB81FBB4FBFCF75340D4076512C08A079CA6A19CA111`

---

## 🔗 Links

- [Full v1.3.0-beta Changelog](CHANGELOG_v1.3.0-beta.md)
- [GitHub Releases](https://github.com/Jeyloh/OmenCore/releases)
- [Issue Tracker](https://github.com/Jeyloh/OmenCore/issues)
