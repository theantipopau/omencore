# OmenCore v2.2.3 - Fan Safety & Diagnostics Fixes + Linux GUI Overhaul

**Release Date:** January 2026  
**Type:** Patch Release

## Summary

This release addresses a critical fan safety issue where fans could drop to 0% at high temperatures, multiple improvements to the fan diagnostics tool, and a comprehensive visual overhaul of the Linux Avalonia GUI to better match the Windows version.

---

## 🐛 Bug Fixes

### 🔴 Critical: Fan Speed Drops to 0 RPM at High Temperature
- **Fixed**: Fans could drop to 0% when temperature exceeded all curve points
- **Cause**: Curve evaluation fallback used `FirstOrDefault()` which returned the lowest temperature point (often with low fan speed) instead of the highest
- **Solution**: Changed fallback to use `LastOrDefault()` - when temp exceeds all curve points, use the highest fan speed as a safety measure
- **Affected**: All users with custom fan curves where max temp exceeded curve definition

**Example of the bug:**
```
Curve: 40°C→30%, 60°C→50%, 80°C→80%
At 85°C: OLD behavior → falls back to 40°C point → 30% fans! 🔥
At 85°C: NEW behavior → falls back to 80°C point → 80% fans ✓
```

### 🟠 Fan Diagnostics: Curve Engine Override
- **Fixed**: Fan speed tests in diagnostics were being overridden by curve engine within seconds
- **Cause**: The curve engine continued running during diagnostic tests, resetting fan speed on each tick
- **Solution**: Added diagnostic mode that suspends curve engine during fan testing
- **New Methods**: `FanService.EnterDiagnosticMode()` / `ExitDiagnosticMode()`

### 🟠 Fan Diagnostics: 100% Not Achieving Max RPM
- **Fixed**: Setting 100% in fan diagnostics didn't achieve true maximum fan speed
- **Cause**: Used `SetFanLevel(55, 55)` which may be capped by BIOS on some models
- **Solution**: Now uses `SetFanMax(true)` for 100% requests, with `SetFanLevel` as fallback

### 🟠 Fan Diagnostics: UI Not Updating After Test
- **Fixed**: Fan RPM/level display wouldn't refresh after applying test speed
- **Solution**: Added explicit property change notifications after test completion

### 🟡 Smart App Control Documentation
- **Added**: Workarounds for Windows 11 Smart App Control blocking OmenCore installer
- **Location**: [ANTIVIRUS_FAQ.md](ANTIVIRUS_FAQ.md)

---

## 🎨 Linux GUI Overhaul

### Theme & Styling (OmenTheme.axaml)
- **New card styles**: `.card`, `.cardInteractive`, `.surfaceCard`, `.statusCard`
- **Interactive card hover**: Blue accent border on hover with smooth transitions
- **New button variants**: `.primary`, `.secondary`, `.danger`, `.ghost`, `.iconButton`
- **Navigation styles**: `.navButton` with active state styling, `.sidebarHeader`
- **Text hierarchy**: `.pageHeader`, `.sectionHeader`, `.subsectionHeader`, `.caption`, `.value`, `.valueLarge`
- **Status text variants**: `.accent`, `.warning`, `.error`, `.success`
- **Form control styling**: ComboBox, NumericUpDown, TextBox with dark theme
- **Thin progress bar**: `.thin` variant for compact displays
- **Layout helpers**: `.verticalSeparator`, updated Separator style

### MainWindow Enhancements
- **Redesigned sidebar**: Darker background (#0F0F0F), better spacing
- **System status panel**: Shows current Performance Mode and Fan Mode
- **Version display**: Shows app version in sidebar footer
- **Connection indicator**: Color-coded status dot with text
- **Quick actions**: Refresh Sensors and GitHub buttons
- **Navigation tracking**: Active state properly highlights current page
- **"Linux Edition" branding**: Distinguishes from Windows version

### Dashboard Overhaul
- **Quick status bar**: Fan summary, Performance Mode, Fan Mode, Power source in one row
- **Session tracking**: Session uptime timer, peak CPU/GPU temperatures
- **Throttling banner**: Warning banner when thermal throttling detected
- **Hardware summary cards**: 5-column grid with CPU, GPU, CPU Fan, GPU Fan, Memory
- **Interactive cards**: Hover effects on stat cards
- **Large value display**: Temperature shown as large °C values
- **Thin progress bars**: Compact utilization indicators
- **System details panel**: CPU/GPU names, power draw, battery status
- **Performance summary panel**: Usage bars with colored indicators

### Fan Control Improvements
- **Emoji icons**: Visual icons for temperature, fans, settings
- **Real-time status cards**: 4-column grid with centered content
- **Preset section**: Improved layout with Load and Save buttons
- **Curve controls card**: Toggle switches for enable/link, hysteresis setting
- **Redesigned curve editor**: Better point layout with ghost button for add
- **Emergency stop button**: Red danger button for immediate max fan
- **Status message banner**: Info banner for feedback

### System Control Updates
- **Performance mode cards**: 4-column visual selector with icons and descriptions
- **Current mode badge**: Blue accent highlight showing active mode
- **GPU mode buttons**: Full-width buttons with emoji icons
- **Keyboard lighting section**: Brightness slider in dark panel, larger color buttons (48px)
- **Color preview section**: Better organized RGB inputs with preview

### Settings Redesign
- **Section icons**: Emoji icons for each settings category
- **Dark panels**: Settings grouped in #141414 backgrounds
- **About section**: Version with accent color, inline action buttons
- **Action buttons**: Reset on left, Cancel/Save on right

### App Resources (App.axaml)
- **Extended color palette**: Added OmenCyan, OmenYellow, status colors, temperature colors
- **New brushes**: TertiaryTextBrush, status brushes (Success, Warning, Error, Info)
- **Temperature brushes**: TempCold, TempWarm, TempHot, TempCritical
- **Accent variants**: AccentLight, AccentDark, AccentTransparent
- **Theme include**: Now properly includes OmenTheme.axaml

### ViewModel Updates
- **MainWindowViewModel**: Added PerformanceMode, FanMode, AppVersion, navigation flags, Refresh/OpenGitHub commands
- **DashboardViewModel**: Session uptime timer, peak temps, fan summary, throttling detection, memory used/total
- **FanControlViewModel**: Hysteresis setting, status message, SavePreset/EmergencyStop commands
- **SystemControlViewModel**: CurrentPerformanceMode property, SetPerformanceMode command
- **HardwareStatus**: Added CpuFanPercent, GpuFanPercent, MemoryUsedGb, MemoryTotalGb, IsThrottling, ThrottlingReason
- **LinuxHardwareService**: Mock data now includes all new fields

---

## 🐧 Linux CLI Improvements

### Better Diagnostics for 2023+ Models
- **New**: `omencore-cli diagnose` command (and `--json`) to print kernel/module/sysfs status and recommended next steps
- **Improved**: More accurate `hp-wmi` detection (only reports available when OMEN control files are actually exposed)
- **Improved**: Prevents EC register reads/writes when only `hp-wmi` is present (reduces confusing false “EC available” states)

---

## 🔧 Technical Details

### Files Changed

- `OmenCoreApp/Services/FanService.cs`
  - Fixed curve fallback: `?? _activeCurve.LastOrDefault()` instead of `FirstOrDefault()`
  - Added `_diagnosticModeActive` volatile flag
  - Added `EnterDiagnosticMode()` / `ExitDiagnosticMode()` methods
  - Added `IsDiagnosticModeActive` property
  - Curve engine now checks diagnostic flag and skips application when active

- `OmenCoreApp/Hardware/WmiFanController.cs`
  - Fixed curve fallback in `ApplyCustomCurve()`: `?? curveList.Last()` instead of `First()`

- `OmenCoreApp/Services/FanVerificationService.cs`
  - 100% fan requests now use `SetFanMax(true)` for true maximum RPM
  - Falls back to `SetFanLevel(55, 55)` if SetFanMax fails
  - Disables max mode before setting <100% speeds

- `OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs`
  - Added `IsDiagnosticActive` property for UI feedback
  - Calls `FanService.EnterDiagnosticMode()` before testing
  - Calls `FanService.ExitDiagnosticMode()` after testing (in finally block)
  - Forces UI refresh with explicit `OnPropertyChanged()` calls

- `docs/ANTIVIRUS_FAQ.md`
  - Added Windows 11 Smart App Control section
  - Documented workarounds for blocked installer

- `src/OmenCore.Linux/JsonContext.cs` (NEW)
  - Added JSON source generator for AOT/trimming support
  - Typed DTOs: SystemStatus, TemperatureInfo, FanInfo, PerformanceInfo

- `src/OmenCore.Linux/Commands/StatusCommand.cs`
  - Now uses source-generated JSON serializer (fixes trimming warnings)

- `src/OmenCore.Linux/Program.cs`
  - ConfigManager now uses source-generated JSON serialization

- `src/OmenCore.Linux/Commands/DiagnoseCommand.cs` (NEW)
  - Added `diagnose` command to collect Linux environment + hardware interface diagnostics
  - Supports `--json` output using source-generated JSON

- `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
  - Improved `hp-wmi` capability detection (requires OMEN control files to be present)
  - Avoids EC register I/O when `ec_sys` is not available

- `src/OmenCore.Linux/JsonContext.cs`
  - Added `DiagnoseInfo` DTO to JSON source generation context

- Test files (3 files)
  - Added `ResetEcToDefaults()` to all test IFanController implementations

### Linux Avalonia Files Changed

- `OmenCore.Avalonia/App.axaml` - Extended color palette and brush definitions
- `OmenCore.Avalonia/Themes/OmenTheme.axaml` - Complete styling overhaul
- `OmenCore.Avalonia/Views/MainWindow.axaml` - Redesigned sidebar and navigation
- `OmenCore.Avalonia/Views/DashboardView.axaml` - New dashboard layout with status bar
- `OmenCore.Avalonia/Views/FanControlView.axaml` - Enhanced fan curve editor UI
- `OmenCore.Avalonia/Views/SystemControlView.axaml` - Visual mode selector cards
- `OmenCore.Avalonia/Views/SettingsView.axaml` - Reorganized with dark panels
- `OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs` - New properties and commands
- `OmenCore.Avalonia/ViewModels/DashboardViewModel.cs` - Session tracking, peak temps
- `OmenCore.Avalonia/ViewModels/FanControlViewModel.cs` - Emergency stop, presets
- `OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs` - Performance mode command
- `OmenCore.Avalonia/Services/IHardwareService.cs` - Extended HardwareStatus model
- `OmenCore.Avalonia/Services/LinuxHardwareService.cs` - Updated mock data

### Fan Curve Safety Logic

**Before (Dangerous):**
```csharp
// If temp > all curve points, falls back to FIRST (lowest fan%)
var targetPoint = _activeCurve.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                  ?? _activeCurve.FirstOrDefault();
```

**After (Safe):**
```csharp
// If temp > all curve points, falls back to LAST (highest fan%)
var targetPoint = _activeCurve.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                  ?? _activeCurve.LastOrDefault();
```

### Diagnostic Mode Flow
```
User clicks "Apply & Verify" in Fan Diagnostics
    ↓
FanService.EnterDiagnosticMode() - sets _diagnosticModeActive = true
    ↓
ApplyCurveIfNeededAsync() checks flag, returns early (no curve override)
    ↓
FanVerificationService applies test speed
    ↓
Wait for fan response (~2.5s)
    ↓
Read back actual RPM
    ↓
FanService.ExitDiagnosticMode() - sets _diagnosticModeActive = false
    ↓
Normal curve operation resumes
```

---

## 📋 Known Issues

### Still Under Investigation
- **Portable version missing HardwareWorker.exe** - Temperature monitoring falls back to in-process
- **Custom curve speed offset** - Some models show different actual RPM than requested %
- **RGB lighting on Thetiger OMN (8BCA)** - All keyboard backends unavailable
- **UI scroll lag** - Lists with many items need virtualization

### Linux-Specific
- **2023+ OMEN models (wf0000, 13700HX)** - May require kernel 6.5+ with hp-wmi driver; ec_sys won't work on these models
- **Ubuntu 24.04 dual-boot** - BitLocker may trigger recovery on BIOS/bootloader changes
- **Fan control not working** - Most 2023 models only expose `thermal_profile` via hp-wmi, not direct fan speed control

### From Previous Releases
- OMEN 14 Transcend compatibility issues
- 2023 XF Model keyboard lights require OGH
- OMEN key opens main app instead of quick access (some users)

---

## 📥 Downloads

| File | SHA256 |
|------|--------|
| OmenCoreSetup-2.2.3.exe | `TBD` |
| OmenCore-2.2.3-win-x64.zip | `TBD` |
| OmenCore-2.2.3-linux-x64.zip | `TBD` |

---

## 🙏 Acknowledgments

Thanks to the community members who reported these issues:
- @yoke (Discord) - Critical fan 0 RPM bug report on OMEN 16
- Thetiger OMN user (Discord) - Fan diagnostics issues, curve offset reports
- Discord community - Smart App Control blocking reports

---

**Full Changelog:** [v2.2.2...v2.2.3](https://github.com/theantipopau/omencore/compare/v2.2.2...v2.2.3)
