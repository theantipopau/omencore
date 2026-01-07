# OmenCore v2.2.0 Release Notes

**Release Date:** January 7, 2026

This release focuses on bug fixes reported by the community, security hardening from a code audit, UI improvements, and new features including GPU OC profiles and fan profile persistence.

---

## 📦 Downloads

| File | Size | SHA256 |
|------|------|--------|
| OmenCoreSetup-2.2.0.exe | 80 MB | `B4982315E979D8DE38471032A7FE07D80165F522372F5EFA43095DE2D42FF56B` |
| OmenCore-2.2.0-win-x64.zip | 104 MB | `542D65C5FD18D03774B14BD0C376914D0A7EE486F8B12D841A195823A0503288` |
| OmenCore-2.2.0-linux-x64.zip | 6 MB | `ADBF700F1DA0741D2EE47061EE2194A031B519C5618491526BC380FE0370F179` |

All builds are **self-contained** - .NET 8 runtime is bundled, no separate installation required.

---

## ✨ New Features

### GPU Overclock Profiles
Save and load GPU overclock configurations with named profiles.

**Features:**
- Create named profiles with core clock, memory clock, and power limit settings
- Quick profile switching via dropdown selector
- Profiles persist to config file across app restarts
- Delete unwanted profiles with one click
- Auto-loads last used profile on startup (optional)

**Files:**
- `src/OmenCoreApp/Models/AppConfig.cs` - Added `GpuOcProfile` class and `GpuOcProfiles` collection
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs` - Profile management methods
- `src/OmenCoreApp/Views/SystemControlView.xaml` - Profile selector UI

---

### Fan Profile Persistence
Custom fan curves now save automatically and restore on app startup.

**Features:**
- Custom curves persist to config file when applied
- Restored on app startup with "Custom" or "Independent" presets
- Works with both unified and independent fan modes

**Files:**
- `src/OmenCoreApp/Models/AppConfig.cs` - Added `CustomFanCurve` property
- `src/OmenCoreApp/ViewModels/FanControlViewModel.cs` - `SaveCustomCurveToConfig()` method
- `src/OmenCoreApp/Services/SettingsRestorationService.cs` - "Custom" preset restoration handling

---

### Linux Auto Mode Restoration (Improved)
Better automatic fan control restoration for Linux users.

**Improvements:**
- Full EC register reset sequence for proper BIOS handoff
- HP-WMI driver support as fallback for newer models (2023+)
- Proper cleanup of manual speed registers
- Timer register reset for complete state cleanup

**Technical Details:**
```csharp
public bool RestoreAutoMode()
{
    WriteByte(REG_BIOS_CONTROL, 0x00);   // Re-enable BIOS control
    WriteByte(REG_FAN_STATE, 0x00);      // Enable auto state
    WriteByte(REG_FAN_BOOST, 0x00);      // Disable boost
    WriteByte(REG_TIMER, 0x78);          // Reset timer to 120 seconds
    WriteByte(REG_FAN1_SPEED_SET, 0x00); // Clear manual speeds
    WriteByte(REG_FAN2_SPEED_SET, 0x00);
    return true;
}
```

**File:** `src/OmenCore.Linux/Hardware/LinuxEcController.cs`

---

### Lazy-Load Peripheral SDKs (Startup Performance)
Corsair, Logitech, and Razer SDKs now only initialize when explicitly enabled in settings.

**Benefits:**
- Faster app startup for users without these peripherals
- Reduced memory footprint when peripherals are disabled
- No more unnecessary SDK initialization logs/errors

**How it works:**
- SDKs check `Features.CorsairIntegrationEnabled`, `Features.LogitechIntegrationEnabled`, `Features.RazerIntegrationEnabled`
- All three default to `false` - user must enable in Settings → Features
- When enabled, SDKs initialize on startup; when disabled, they're skipped entirely

**Files:**
- `src/OmenCoreApp/ViewModels/MainViewModel.cs` - Conditional SDK initialization
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs` - Nullable service handling
- `src/OmenCoreApp/Models/FeaturePreferences.cs` - Feature toggles (already existed)

---

### Dashboard UI Enhancements
The Monitoring dashboard has been improved with at-a-glance status information.

**New Quick Status Bar:**
- 🌀 **Fans** - Real-time CPU/GPU fan RPMs displayed in the status bar
- ⚡ **Performance Mode** - Current active performance mode
- 💨 **Fan Mode** - Current fan preset (Gaming, Auto, etc.)
- 🔌 **Power** - AC or Battery status

**Session Tracking:**
- **Session Uptime** - Shows how long OmenCore has been running (updates every second)
- **Peak Temperatures** - Tracks highest CPU/GPU temps seen this session
- Automatically resets when app is restarted

**Files:**
- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs` - Added session tracking and fan summary properties
- `src/OmenCoreApp/Views/DashboardView.xaml` - Added Quick Status Bar and session info header
- `src/OmenCoreApp/Services/FanService.cs` - Initial fan telemetry population on Start()
- `src/OmenCoreApp/Utils/BoolConverters.cs` - Enhanced converter with ConverterParameter support

---

## 🐛 Bug Fixes

### Fan Always On Bug (Reddit Report)
**Problem:** OMEN 17 13700HX fans never stopped even at idle/low temperatures (25°C). User had "fan always on" disabled in BIOS but fans kept running constantly.

**Root Cause:** OmenCore's Auto/Default fan mode was continuously applying a fan curve that started at 30% speed at 40°C. Even in "Auto" mode, OmenCore was managing fans instead of letting the BIOS control them, which prevented fans from spinning down to 0% at idle.

**Fix:** Auto/Default mode now truly restores BIOS control:
1. Calls `DisableCurve()` to stop OmenCore's continuous curve monitoring
2. Calls `RestoreAutoControl()` which resets fan levels to 0 and sets FanMode.Default
3. This allows BIOS to control fans and stop them at idle temperatures

```csharp
// New Auto mode handling in FanService.ApplyPreset()
else if (nameLower.Contains("auto") || nameLower.Contains("default"))
{
    // Let BIOS control fans completely - allows fans to stop at idle
    DisableCurve();
    _fanController.RestoreAutoControl();
    _logging.Info($"✓ Preset '{preset.Name}' using BIOS auto control (fans can stop at idle)");
}
```

**Files:** 
- `src/OmenCoreApp/Services/FanService.cs` - Added Auto mode special case in ApplyPreset()
- `src/OmenCoreApp/Hardware/WmiFanController.cs` - RestoreAutoControl() already properly resets fans

---

### Fan Curve Editor Crash (Issue #30)
**Problem:** Dragging fan curve points beyond chart bounds caused an `ArgumentException`:
```
System.ArgumentException: '90' cannot be greater than 87.
at System.Math.ThrowMinMaxException[T](T min, T max)
at OmenCore.Controls.FanCurveEditor.Point_MouseMove
```

**Fix:** Added safety check when neighbor temperature constraints conflict. When `minTemp > maxTemp` (edge case when points are too close together), the point now stays at its current position instead of crashing.

**File:** `src/OmenCoreApp/Controls/FanCurveEditor.xaml.cs`

---

### Fan Curve Mouse Release Bug (Issue #30)
**Problem:** The cursor kept holding the drag point even after:
- Releasing the mouse button
- Moving outside the chart area
- The control losing mouse capture

**Fix:** Added global mouse handlers to properly release drag state:
- `ChartCanvas_MouseLeave` - Releases on mouse exit
- `ChartCanvas_MouseLeftButtonUp` - Releases on any mouse up in canvas
- `ChartCanvas_LostMouseCapture` - Releases when capture is lost
- Shared `ReleaseDrag()` method ensures consistent cleanup

**File:** `src/OmenCoreApp/Controls/FanCurveEditor.xaml.cs`

---

### Per-Core Undervolt Crash (Issue #31)
**Problem:** Opening the Per-Core Undervolt section caused a XAML parse exception:
```
System.Windows.Markup.XamlParseException: Operacja podawania wartości 
elementu „System.Windows.Markup.StaticResourceHolder" wywołała wyjątek.
```

**Root Cause:** The `PerCoreOffsets` ItemTemplate referenced a non-existent `SurfaceBrush` resource.

**Fix:** Changed `{StaticResource SurfaceBrush}` to `{StaticResource SurfaceMediumBrush}` (the correct resource name).

**File:** `src/OmenCoreApp/Views/AdvancedView.xaml` (line 257)

---

### Animation Parse Error on Startup
**Problem:** Invalid `FanSpinStoryboard` animation caused XAML parse errors during application startup.

**Root Cause:** `LinearDoubleKeyFrame` was incorrectly used as an `EasingFunction` (it's not an easing function, it's a keyframe type).

**Fix:** Simplified the animation to use a plain linear `DoubleAnimation` without easing.

**File:** `src/OmenCoreApp/Styles/ModernStyles.xaml`

---

### OMEN Key False Trigger with Remote Desktop
**Problem:** Opening Remote Desktop Connection or other apps caused OmenCore window to appear unexpectedly. Some users reported the window popping open when launching mstsc.exe.

**Root Cause:** `VK_LAUNCH_APP1 (0xB6)` was being treated as the OMEN key unconditionally without scan code validation. Remote Desktop and media applications can send this virtual key code during startup or certain operations.

**Fix:** Added scan code validation for `VK_LAUNCH_APP1` to match the validation already used for `VK_LAUNCH_APP2`. Only key presses with OMEN-specific scan codes (0xE045, 0xE046, 0x0046, 0x009D) are now treated as the OMEN key.

**File:** `src/OmenCoreApp/Services/OmenKeyService.cs`

---

## 🔐 Security & Stability Improvements

### Named Pipe Security Hardening
Added `PipeOptions.CurrentUserOnly` flag to the named pipe server used for IPC between OmenCore and HardwareWorker. This prevents other users on the system from connecting to the pipe.

**File:** `src/OmenCore.HardwareWorker/HardwareWorkerService.cs`

---

### Async Exception Handling
Converted `async void InitializeWorker()` to `async Task InitializeWorker()` with proper exception handling. This ensures exceptions are properly propagated and logged instead of crashing the application silently.

**File:** `src/OmenCoreApp/Services/HardwareWorkerClient.cs`

---

### Improved Exception Logging
Added meaningful logging to previously bare `catch` blocks in HardwareWorkerClient. Errors are now logged with context for easier debugging.

**File:** `src/OmenCoreApp/Services/HardwareWorkerClient.cs`

---

### Installer Download Verification
Added SHA256 hash verification for LibreHardwareMonitor downloads in the installer build script. This prevents supply chain attacks from compromised downloads.

**File:** `installer/download-librehw.ps1`

---

## ⚡ Performance & Memory Improvements

### QuickPopup Window Memory Leak Fix
**Problem:** Timer event handlers weren't unsubscribed when QuickPopupWindow closed, causing memory leaks.

**Fix:** Added proper cleanup in `OnClosed`:
- Unsubscribe `_updateTimer.Tick -= UpdateDisplay`
- Dispose `_displayService`

**File:** `src/OmenCoreApp/Views/QuickPopupWindow.xaml.cs`

---

### TrayIconService Exception Logging
**Problem:** Empty `catch { }` blocks in submenu styling code silently swallowed errors.

**Fix:** Added `Debug.WriteLine` logging to 5 catch blocks to aid debugging without affecting performance.

**File:** `src/OmenCoreApp/Utils/TrayIconService.cs`

---

### DashboardViewModel Optimizations
**Problem:** 
1. Magic number `60` for thermal sample history
2. `while` loop calling `RemoveAt(0)` is O(n) per removal

**Fix:**
1. Extracted constant: `private const int MaxThermalSampleHistory = 60;`
2. Changed to single-pass removal calculation

**File:** `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`

---

### GeneralViewModel Exception Logging
**Problem:** Empty catch blocks in `UpdateFanSpeeds` and `UpdateTemperatures` hid errors.

**Fix:** Added `Debug.WriteLine` logging for fan speed and temperature update errors.

**File:** `src/OmenCoreApp/ViewModels/GeneralViewModel.cs`

---

## 🎨 User Interface Improvements

### System Tray Menu Overhaul
The system tray context menu has been reorganized for better clarity:

**Before:**
- Quick Profiles ▶ (Performance, Balanced, Quiet)
- Fan Mode ▶ (Auto, Max, Quiet)
- Performance ▶ (Balanced, Performance, Quiet)
- Display ▶ (...)

**After:**
- Quick Profile ▶ (with descriptive labels)
  - 🚀 Performance — Max cooling + Performance mode
  - ⚖️ Balanced — Auto cooling + Balanced mode
  - 🤫 Quiet — Quiet fans + Power saving
- Advanced ▶
  - 🌀 Fan Control ▶ (Auto, Max, Quiet with descriptions)
  - ⚡ Power Profile ▶ (Balanced, Performance, Power Saver)
  - 🖥️ Display ▶ (Refresh rate controls)

**Additional changes:**
- CPU/GPU temperature display now uses monospace font for alignment
- Changed separator style from parentheses to middle dot (·)
- Menu headers show current mode in brackets: `[Auto]`

**File:** `src/OmenCoreApp/Utils/TrayIconService.cs`

---

### New Animation Presets
Added 5 new smooth animation storyboards for UI transitions:

| Animation | Duration | Use Case |
|-----------|----------|----------|
| `FadeInFastStoryboard` | 150ms | Quick transitions |
| `SlideInFromBottomStoryboard` | 250ms | Cards/panels appearing |
| `ScaleInStoryboard` | 200ms | Popups/dialogs with subtle bounce |
| `BreathingStoryboard` | 1.5s loop | Active status indicators |
| `FanSpinStoryboard` | 1s loop | Fan icon rotation |

**File:** `src/OmenCoreApp/Styles/ModernStyles.xaml`

---

### Installer Wizard Images
Updated the installer wizard image generation script to remove hardcoded version numbers. Images now show feature highlights instead:

- ✓ Fan Control
- ✓ RGB Lighting
- ✓ Performance Modes
- ✓ Game Profiles

**File:** `installer/create-wizard-images.ps1`

---

## 🔍 Known Issues Under Investigation

These issues have been reported but require more information to diagnose:

### Fan Max Mode Cycling
Some users report fan speed cycling between high and low when Max mode is enabled.
```
[INFO] [HP WMI] Fan levels: Fan1=25 krpm (2500 RPM), Fan2=28 krpm (2800 RPM)
[INFO] [FanControl] Fan Max mode: enabled (re-applied via countdown)
```
**Status:** Needs more logs to reproduce

---

### dGPU Sleep Prevention
OmenCore's constant polling may prevent NVIDIA dGPU from entering sleep state, causing increased battery drain.
**Status:** Investigating polling intervals and GPU query patterns

---

### Fan Speed Throttling Under Load
Max fan speed may decrease under heavy load (6300→5000 RPM reported on some models).
**Status:** May be thermal throttling or BIOS limitation

---

## 📁 Files Changed

| File | Changes |
|------|---------|
| `VERSION.txt` | 2.1.2 → 2.2.0 |
| `src/OmenCoreApp/OmenCoreApp.csproj` | Version bump |
| `installer/OmenCoreInstaller.iss` | Version bump |
| `src/OmenCore.Linux/Program.cs` | Version constant |
| `src/OmenCoreApp/Models/AppConfig.cs` | GpuOcProfile class, CustomFanCurve |
| `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs` | GPU OC profile management |
| `src/OmenCoreApp/Views/SystemControlView.xaml` | GPU OC profile selector UI |
| `src/OmenCoreApp/ViewModels/FanControlViewModel.cs` | Custom curve persistence |
| `src/OmenCoreApp/Services/SettingsRestorationService.cs` | Custom/Independent preset restore |
| `src/OmenCore.Linux/Hardware/LinuxEcController.cs` | Improved RestoreAutoMode |
| `src/OmenCoreApp/Services/OmenKeyService.cs` | VK_LAUNCH_APP1 scan code fix |
| `src/OmenCoreApp/Utils/TrayIconService.cs` | Menu overhaul, version in tooltip |
| `src/OmenCoreApp/Styles/ModernStyles.xaml` | New animations, fix FanSpin |
| `src/OmenCoreApp/Controls/FanCurveEditor.xaml.cs` | Mouse handling fixes |
| `src/OmenCoreApp/Views/AdvancedView.xaml` | Fix SurfaceBrush reference |
| `src/OmenCore.HardwareWorker/HardwareWorkerService.cs` | Pipe security |
| `src/OmenCoreApp/Services/HardwareWorkerClient.cs` | Async/exception fixes |
| `installer/download-librehw.ps1` | SHA256 verification |
| `installer/create-wizard-images.ps1` | Remove version from images |
| `CHANGELOG.md` | Release notes |

---

## 🙏 Thanks

Thanks to the community members who reported these issues:
- Issue #30 - Fan curve editor bugs
- Issue #31 - Per-core undervolt crash
- Discord reports on fan cycling, dGPU sleep, and UI feedback

---

## 📥 Download

Download the latest installer from the [GitHub Releases](https://github.com/theantipopau/omencore/releases) page.
