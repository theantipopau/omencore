# OmenCore v1.3.0-beta2 Release Notes

**Release Date:** December 2025  
**Type:** Beta Release - Critical Bug Fixes + Feature Enhancements

---

## 🎯 Overview (Beta 2)

This beta2 release addresses critical issues reported in beta1, including the non-functional "Max Fan" tray option.

### Bug Fixes (Beta 2)

#### 🟩 Start Minimized to Tray
- Fixed: when enabled, the app now starts minimized to tray reliably.

#### 🌀 Max Fan Tray Option Fixed
**Problem:** Clicking "Max" in the system tray fan menu didn't actually enable max fan speed, although "Max Performance" mode did work.

**Root Cause:** `FanService.ApplyPreset()` detected "Max" presets but only disabled the fan curve - it never called the BIOS `SetMaxFanSpeed(true)` command.

**Solution:** 
- Max preset now calls `SetMaxFanSpeed(true)` to enable BIOS max fan mode
- Auto/Default preset now calls `SetMaxFanSpeed(false)` + `RestoreAutoControl()` to properly restore automatic fan control
- Works with WMI BIOS, OGH proxy, and EC backends

### Enhancements (Beta 2)

#### 🧩 Configurable Quick Fan Presets + Persistence
- Tray and Quick Popup fan buttons are now 3-slot configurable (Auto / Max / Silent) via Settings
- Last-applied fan preset is persisted and re-applied on next startup
- Added a debounced resume hook to reapply last fan/performance/GPU boost after sleep/hibernate

### Known Issues (Beta 2)

#### 🟥 Fan Presets Not Applying (Fans only respond in Max/Performance)
- Some systems report that fan control does not respond to non-max presets.
- Status: under investigation.

#### 🟥 GPU Power Boost (TGP) Resets on Windows Startup
- Some systems report the GPU boost level still resets after reboot.
- Status: under investigation.

#### 🟥 OSD Overlay Not Working
- OSD may not appear even after changing hotkeys.
- Status: under investigation.

#### 🟥 OMEN Key Interception Not Working
- OMEN key interception may not trigger actions on some systems.
- Status: under investigation.

#### 🎮 OSD Shows Real CPU/GPU Load
- OSD overlay now displays actual CPU/GPU load from hardware monitoring service
- Previously showed estimated values, now uses `HardwareMonitoringService.LatestSample`

#### 🔋 Battery Status in Tray Tooltip  
- Tray tooltip now shows battery percentage and charging state
- Dynamic icons: 🔌 (AC), 🔋 (>20%), 🪫 (≤20%)

#### ⌨️ OmenMon-Style Keyboard RGB
- Implemented proper 128-byte color table format per OmenMon documentation
- Added keyboard capability detection (`GetKeyboardType`, `HasBacklightSupport`)
- Proper zone mapping for 4-zone and per-key keyboards

#### 🌡️ Temperature Smoothing
- Added 3-sample exponential moving average for temperature readings
- Reduces noise and provides more stable fan curve response

---

# OmenCore v1.3.0-beta1 Release Notes

**Release Date:** December 2025  
**Type:** Beta Release - Major Bug Fixes + New Features

---

## 🎯 Overview

This beta release addresses critical fan control and DPC latency issues reported in v1.2.x, while adding powerful new features to make OmenCore a **complete standalone replacement** for HP OMEN Gaming Hub.

**🚫 OmenCore is 100% OGH-Independent:**
- WMI BIOS is the **primary** fan control backend
- No OGH services required for fan control, thermal management, or GPU settings
- Works on fresh Windows installs without any HP software
- OGH proxy is a last-resort fallback only for edge cases

**Key highlights:**
- ✅ Fan curves now actually work (OmenMon-style continuous monitoring)
- ✅ MAX mode no longer stuck
- ✅ Reduced DPC latency with adaptive polling
- ✅ Quick Popup UI (middle-click tray icon)
- ✅ Modular feature toggles to reduce background presence
- ✅ OMEN key interception (experimental)
- ✅ **Full OGH independence** - uninstall OGH entirely

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
- **Automatic restore point** - OGH cleanup now automatically creates a System Restore point before starting
  - If restore point creation fails (e.g., System Restore disabled), user is prompted to continue or cancel
  - No more "forgot to create restore point" risk
  - Manual restore point button still available for extra safety

---

## ⚠️ Known Issues

---

## 🔧 Model Compatibility Improvements (December 15, 2025)

### OMEN Transcend 14 / 2024+ Model Support

**Problem Reported:** Users with OMEN Transcend 14 (Ultra 7 + RTX 4060) reported that fan modes would switch in the app but actual fan speeds never changed. CPU temperatures reaching 100°C due to fans not ramping up.

**Root Cause:** Newer OMEN models (Transcend, 2024+ models) have WMI BIOS interfaces that **return success** for fan commands but don't actually change fan behavior. These models require the **OGH Proxy** backend instead.

**Solution Implemented:**

1. **Model Family Detection** - New `OmenModelFamily` enum identifies:
   - `OMEN16` / `OMEN17` - Classic models with full WMI support
   - `Victus` - HP Victus line
   - `Transcend` - Newer ultrabook-style OMEN Transcend 14/16
   - `OMEN2024Plus` - 2024 and newer models
   - `Desktop` - OMEN desktop towers

2. **Smart Backend Selection** - For Transcend and 2024+ models:
   - OGH Proxy is now **preferred over WMI BIOS**
   - If OGH is running, OmenCore automatically uses it
   - Warning shown if OGH not available on these models

3. **Command Verification System** - New `TestCommandEffectiveness()` method:
   - Measures fan RPM before and after sending commands
   - If RPM doesn't change despite "success" return, flags backend as ineffective
   - Recommends OGH proxy or OMEN Gaming Hub installation

**Recommendation for Transcend 14 / 2024+ Users:**
- **Keep OMEN Gaming Hub installed** - OmenCore will use its WMI interface (OGH Proxy) for reliable fan control
- You can still use OmenCore as your main interface while OGH runs in background
- Alternatively, use the **OGH Cleanup** option but understand fan control may be limited

### Other Fixes (December 15, 2025)

- **System Restore Error Handling** - Fixed "Not found" error when creating restore points
  - Added registry check for System Restore enabled/disabled state
  - Better error messages for common failure codes
  
- **Tray Icon Temperature Colors** - Adjusted thresholds for gaming laptops:
  - Blue: < 50°C (was < 60°C)
  - Green: 50-65°C (was 60-70°C)
  - Yellow: 65-75°C (NEW band)
  - Orange: 75-85°C
  - Red: 85-95°C
  - Magenta: > 95°C (critical)

- **OGH Cleanup Section** - Restored to Settings tab (was accidentally removed during UI reorganization)
  - Now only visible when OGH is installed

---

## ⚠️ Known Limitations

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
