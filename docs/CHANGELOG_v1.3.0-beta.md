# OmenCore v1.3.0-beta Release Notes

**Release Date:** December 2025  
**Type:** Beta Release - Major Bug Fixes + New Features

---

## 🎯 Overview

This beta release addresses critical fan control and DPC latency issues reported in v1.2.x, while adding powerful new features to make OmenCore a complete standalone replacement for HP OMEN Gaming Hub.

**Key highlights:**
- ✅ Fan curves now actually work (OmenMon-style continuous monitoring)
- ✅ MAX mode no longer stuck
- ✅ Reduced DPC latency with adaptive polling
- ✅ Quick Popup UI (middle-click tray icon)
- ✅ Modular feature toggles to reduce background presence
- ✅ OMEN key interception (experimental)

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

**Solution:** Added startup check - window stays hidden when minimized to tray enabled.

### 6. Improved AMD iGPU Detection

**Problem:** Some AMD APU + NVIDIA hybrid laptops not detected correctly.

**Solution:** Expanded WMI query to include more AMD iGPU models:
- Radeon 610M, 660M, 680M
- Radeon 740M, 760M, 780M
- Radeon 880M, 890M
- "AMD Radeon Graphics" (generic APU)

---

## ✨ New Features

### 🔋 Battery Care Mode (NEW)

Limit battery charge to 80% to extend battery lifespan:

- **Toggle in Settings** under "Battery Care" section
- Uses HP WMI BIOS command (CMD_BATTERY_CARE = 0x24)
- Prevents overcharging when laptop is plugged in frequently
- Same feature as OMEN Gaming Hub's Battery Care

### 🎮 In-Game OSD Overlay (NEW)

Real-time stats overlay during gaming:

- **Click-through** - Transparent window doesn't interfere with games
- **Configurable metrics:** CPU/GPU temps, load %, fan speeds, RAM usage
- **Position options:** TopLeft, TopRight, BottomLeft, BottomRight
- **Hotkey toggle:** F12 by default (customizable)
- **Master disable toggle** - When OFF, NO background process runs
- **Throttling warning** - Shows alert when CPU/GPU is thermal throttling

Settings in Settings tab → "In-Game OSD" section.

### 🌡️ Fan Hysteresis (NEW)

Prevent fan speed oscillation when temps fluctuate near curve points:

- **Dead-zone threshold:** 3°C default - temps must change by this much to trigger new fan speed
- **Ramp-up delay:** 0.5s - prevents instant speed increases
- **Ramp-down delay:** 3s - prevents rapid speed decreases  
- **Smooths fan behavior** - No more annoying fan ramping up/down constantly

Settings in Settings tab → "Fan Hysteresis" section.

### Quick Popup UI (Middle-Click Tray)

A compact popup window appears when you middle-click the tray icon:

- **Temperature Display** - CPU/GPU temps and load at a glance
- **Fan Mode Buttons** - Quick toggle between Auto, Max, Quiet
- **Performance Mode Buttons** - Balanced, Performance, Quiet
- **Display Off** - Turn off screen while downloads/music continue
- **Refresh Rate Toggle** - Quick switch between high/low Hz

The popup auto-hides when you click outside.

### RGB Keyboard Zones (WMI BIOS Backend)

Added WMI BIOS backend for keyboard RGB control (works on models where EC access fails):

- `SetColorTable()` - Set all 4 zones at once
- `SetZoneColor()` - Set individual zone colors
- `GetColorTable()` - Read current zone colors

### Display Controls

Available in tray menu and Quick Popup:

- **Turn Off Display** - Screen off while system continues running
- **Refresh Rate Toggle** - Quick switch between 60Hz/144Hz/165Hz
- **High/Low Presets** - Battery-saving vs gaming modes

### Modular Feature Toggles

New Settings section "Feature Modules" lets you enable/disable features:

- **Corsair iCUE Integration** - Disable if no Corsair devices
- **Logitech G HUB Integration** - Disable if no Logitech devices
- **Game Profile Auto-Switching** - Disable for manual control only
- **Keyboard Backlight Control** - Disable if lighting doesn't work
- **Custom Fan Curves** - Disable to use BIOS control only
- **Power Source Automation** - Disable AC/Battery profile switching
- **GPU Mode Switching** - Disable if not using hybrid graphics
- **CPU Undervolt Controls** - Disable if not undervolting

This reduces background resource usage for features you don't need.

### OMEN Key Interception (Experimental)

Capture the physical OMEN key press to show OmenCore instead of HP Gaming Hub:

- **Configurable actions:** Show Popup, Show Window, Toggle Fan Mode, Toggle Performance
- Uses low-level keyboard hook
- **Disabled by default** - enable in Settings → Feature Modules
- ⚠️ OMEN key scan code varies by laptop model

---

## 🎨 UI/UX Polish

Based on community feedback, the following UI improvements were made:

### More Screen Real Estate

- **Slimmer sidebar** - Reduced width from 240px to 200px, compact fonts
- **Smaller footer** - Recent Activity and System Log height reduced from 240px to 160px
- **Removed Dashboard header card** - Eliminated redundant "Dashboard" banner that wasted vertical space

### Fixed Blurry Text After Hover

- **Root cause:** Scale transform animations (1.01x/1.02x) on cards caused sub-pixel rendering
- **Solution:** Removed scale animations, kept shadow/border effects only
- Added `UseLayoutRounding="True"` and `SnapsToDevicePixels="True"` to cards

### Smoother Touchpad Scrolling

- **Root cause:** Original scroll handler optimized for mouse wheel (large delta events)
- **Solution:** Implemented precision touchpad detection:
  - Small delta events (< 50) use direct scrolling for native smoothness
  - Mouse wheel events (120 per notch) use animated scrolling
  - Prevents jitter and lag on precision touchpads

### Minimal Scrollbars

- **Width reduced** from 10px to 6px
- **Auto-fade** - 30% opacity when idle, 80% on hover
- **Subtle thumb** - Rounded corners, highlights blue when dragging
- **Cleaner look** - No background track, just floating thumb

---

## 🔧 Technical Changes

### New Files
- `Views/QuickPopupWindow.xaml(.cs)` - Compact popup near tray
- `Views/OsdOverlayWindow.xaml(.cs)` - In-game OSD overlay window
- `Views/GeneralView.xaml(.cs)` - New General tab with paired profiles
- `Views/AdvancedView.xaml(.cs)` - New Advanced tab for power users
- `Services/OsdService.cs` - OSD management with global hotkey registration
- `Services/OmenKeyService.cs` - Low-level keyboard hook for OMEN key (enhanced)
- `Services/DisplayService.cs` - Display power and refresh rate control
- `Services/ConfigBackupService.cs` - Full config import/export/reset
- `Models/FeaturePreferences.cs` - Feature toggle settings

### Modified Files
- `Services/FanService.cs` - Complete rewrite with continuous curve monitoring + hysteresis
- `Hardware/WmiFanController.cs` - Added ResetFromMaxMode() sequence
- `Hardware/HpWmiBios.cs` - Added keyboard RGB + Battery Care Mode methods
- `Services/KeyboardLightingService.cs` - WMI BIOS backend priority
- `Services/ConfigurationService.cs` - Added Replace() and ResetToDefaults() methods
- `Utils/TrayIconService.cs` - QuickPopup support, middle-click handler, Quick Profiles submenu
- `App.xaml.cs` - Start minimized fix, middle-click wiring, tray profile event
- `ViewModels/MainViewModel.cs` - OMEN key event handling, ApplyQuickProfileFromTray()
- `ViewModels/FanControlViewModel.cs` - SelectPresetByNameNoApply() for external sync
- `ViewModels/GeneralViewModel.cs` - New ViewModel for General tab with telemetry polling
- `ViewModels/SettingsViewModel.cs` - Feature toggle + OSD/Battery/Hysteresis properties
- `Views/SettingsView.xaml` - Feature Modules + OSD + Battery Care + Hysteresis + OGH Cleanup
- `Views/MainWindow.xaml` - Tab reorganization (General, Advanced, Monitoring, RGB, Settings)
- `Models/AppConfig.cs` - Added FeaturePreferences, OsdSettings, BatterySettings, FanHysteresisSettings, OmenKey settings

---

## 📊 Performance Comparison

| Metric | v1.2.1 | v1.3.0-beta |
|--------|--------|-------------|
| Fan curve update | Once (on click) | Every 15s |
| Fan hysteresis | ❌ No | ✅ Dead-zone + delays |
| Monitoring poll (stable) | 1.5s fixed | 5s adaptive |
| Monitoring poll (changing) | 1.5s fixed | 1s adaptive |
| DPC latency (typical) | ~1200μs | ~400-600μs |
| GPU TGP persist on reboot | ❌ No | ✅ Yes |
| Start minimized | ❌ Broken | ✅ Works |
| Quick access popup | ❌ No | ✅ Middle-click |
| In-game OSD | ❌ No | ✅ Click-through overlay |
| Battery charge limit | ❌ No | ✅ 80% via BIOS |
| Feature toggles | ❌ No | ✅ Yes |
| Tray Quick Profiles | ❌ No | ✅ One-click switching |
| OGH cleanup tool | ✅ Yes | ✅ Moved to Settings |

---

## 🔄 Latest Updates (Dec 15, 2025)

### UI Reorganization
- **New General tab** - Combined Performance + Fan profiles with 4 quick profile cards
- **New Advanced tab** - CPU undervolt, GPU power, custom fan curves grouped together
- **Profile-Fan Sync** - General tab selections now properly update Fan Control tab
- **Tabs streamlined** - Removed redundant System Control tab, merged features

### System Tray Enhancements
- **Quick Profiles menu** - Right-click tray → "🎮 Quick Profiles" submenu
- **One-click switching** - Performance, Balanced, Quiet profiles accessible from tray
- **Applies both** - Each profile sets both performance mode and fan mode together

### OMEN Key Interception (Enhanced)
- **Configurable actions** - Toggle OmenCore, Cycle Performance, Cycle Fan Mode, Show Popup, Launch External App
- **Per-app launching** - Can launch any .exe when OMEN key is pressed
- **Debouncing** - 300ms debounce to prevent double-triggering
- **Config persistence** - Settings saved and restored on startup

### HP OMEN Gaming Hub Cleanup (Restored)
- **Moved to Settings tab** - OGH cleanup tool now in Settings under "HP OMEN Gaming Hub Cleanup" section
- **All options preserved** - Kill processes, uninstall, remove services, registry cleanup, file removal
- **System restore point** - Create restore point before cleanup with one click

---

## ⚠️ Known Issues

1. **Some HP Models Still Limited:** Custom fan curves may still not work on models where WMI `SetFanLevel` command returns success but has no effect. These models may need EC direct access via PawnIO.

2. **DPC Latency Still Higher Than OGH:** While significantly reduced, DPC latency may still be higher than native OMEN Gaming Hub on some systems due to WMI overhead.

3. **OMEN Key Varies by Model:** The OMEN key scan code differs between laptop generations. May not work on all models.

4. **Feature Toggles Require Restart:** Changing feature toggles requires restarting OmenCore to take effect.

5. **QuickPopup Auto-Hides:** The popup closes when clicking outside - this is intentional behavior.

---

## 🚀 Upgrade Notes

1. **Settings preserved:** All your saved presets, curves, and settings carry over
2. **No action needed:** The continuous fan curve is automatic - just apply your preset
3. **New tray interaction:** Middle-click for Quick Popup, left-click for main window
4. **Check Feature Modules:** Disable unused features in Settings to reduce background usage
5. **OGH Cleanup moved:** Now in Settings tab instead of separate System Control tab

---

## 📋 Feedback Requested

As this is a beta release, please report:
- Does your custom fan curve now work correctly?
- Is DPC latency improved on your system?
- Does GPU TGP persist after reboot?
- Does "Start minimized" work on Windows 11?
- Does the Quick Popup appear on middle-click?
- Does the OMEN key work on your laptop model?
- Does the **In-Game OSD** appear correctly during gaming?
- Does **Battery Care Mode** properly limit charge to 80%?
- Does **Fan Hysteresis** reduce annoying fan oscillation?
- Do **Tray Quick Profiles** work correctly for fast switching?

**Discord:** https://discord.gg/ahcUC2Un  
**GitHub Issues:** https://github.com/theantipopau/omencore/issues

---

## 💖 Support Development

If OmenCore has helped you, consider supporting development:

[![PayPal](https://img.shields.io/badge/PayPal-Donate-blue.svg)](https://www.paypal.com/donate/?business=XH8CKYF8T7EBU&no_recurring=0&item_name=Thank+you+for+your+generous+donation%2C+this+will+allow+me+to+continue+developing+my+programs.&currency_code=AUD)
