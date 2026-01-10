# OmenCore v2.2.3 - Fan Safety & Diagnostics Fixes

**Release Date:** January 2026  
**Type:** Patch Release

## Summary

This release addresses a critical fan safety issue where fans could drop to 0% at high temperatures, plus multiple improvements to the fan diagnostics tool.

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
