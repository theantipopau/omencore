# OmenCore v2.2.3 Bug Tracking

**Status:** In Development  
**Last Updated:** January 2026

---

## ✅ Fixed in v2.2.3

### 🔴 Critical: Fan Speed Drops to 0 RPM at High Temperature
**Reporter:** yoke (Discord)  
**Status:** ✅ FIXED

**Root Cause:** When temperature exceeded ALL curve points, the fallback logic used `FirstOrDefault()` which returned the lowest temperature point (often with low fan%). Should use `LastOrDefault()` to get highest fan speed as safety fallback.

**Fix:** Changed curve evaluation fallback in 3 locations:
- [FanService.cs](../src/OmenCoreApp/Services/FanService.cs) - Single curve and independent curves
- [WmiFanController.cs](../src/OmenCoreApp/Hardware/WmiFanController.cs) - Custom curve application

---

### 🟠 Fan Diagnostics: Curve Override During Test
**Status:** ✅ FIXED

**Root Cause:** Fan curve engine continued running during diagnostic tests, overriding test speeds within 10 seconds.

**Fix:** Added diagnostic mode to FanService:
- `EnterDiagnosticMode()` / `ExitDiagnosticMode()` methods
- Curve engine checks `_diagnosticModeActive` flag and skips application
- FanDiagnosticsViewModel now properly suspends curve during tests

---

### 🟠 Fan Diagnostics: 100% Not Achieving Max RPM
**Status:** ✅ FIXED

**Root Cause:** FanVerificationService used `SetFanLevel(55, 55)` for 100%, but BIOS may cap this. `SetFanMax(true)` achieves true maximum.

**Fix:** Updated `ApplyAndVerifyFanSpeedAsync()` to use `SetFanMax(true)` for 100% requests.

---

### 🟠 Fan Diagnostics: UI Not Updating After Apply
**Status:** ✅ FIXED

**Fix:** Added explicit `OnPropertyChanged()` calls after test completion to force UI refresh.

---

### 🟡 Smart App Control Blocking Installer
**Status:** ✅ DOCUMENTED

**Fix:** Updated [ANTIVIRUS_FAQ.md](ANTIVIRUS_FAQ.md) with Smart App Control workarounds.

---

## 📋 Remaining Issues (To Investigate)

### 🟠 High Priority

#### 1. Portable Version Missing HardwareWorker.exe
**Reporter:** Multiple users  
**Symptoms:**
- Log shows: `Hardware worker executable not found, falling back to in-process`
- Temperature monitoring may have issues
- Portable ZIP doesn't include OmenCore.HardwareWorker.exe

**Solution:** Update build/publish script to include HardwareWorker.exe in portable builds.

---

#### 2. Custom Fan Curve Speed Offset Issues
**Reporter:** Thetiger OMN user (Discord)  
**Symptoms:**
- Curve says 50% but actual speed is different
- Possible calibration mismatch between WMI level and actual RPM

**Probable Cause:**
- `MaxFanLevel = 55` constant may not match all models
- Some models may have different level-to-RPM mapping
- Need per-model calibration or auto-detection

**Investigation:**
- Compare GetFanLevel() values to actual RPM readings
- Check if model-specific MaxFanLevel is needed

---

### 🟡 Medium Priority

#### 3. RGB Lighting Not Working on Thetiger OMN
**Reporter:** Thetiger OMN user (Discord)  
**Device:** Thetiger OMN (8BCA)  
**Symptoms:**
- Log shows all keyboard backends fail:
  - WMI BIOS: not available
  - WMI: not available
  - EC: not available
- Keyboard lighting controls show but don't work

**Probable Cause:** 
- Replacement motherboard may have different WMI namespace
- This device has both RTX 4070 and AMD Radeon iGPU - may be newer platform
- May need to detect and handle differently

**Investigation:**
- Check WMI namespaces available on this device
- See if `root\hp\InstrumentedBIOS` exists
- May need new backend or different WMI calls

---

#### 4. UI Scroll/Responsiveness Bugs
**Reporter:** Discord users  
**Symptoms:**
- Lists with many items lag when scrolling
- UI freezes briefly during heavy operations

**Probable Cause:**
- WPF ScrollViewer without VirtualizingStackPanel
- No virtualization on ItemsControls with many items

**Solution:**
- Enable UI virtualization on ListBoxes/ItemsControls
- Use `VirtualizingStackPanel.IsVirtualizing="True"`
- Consider `VirtualizingStackPanel.VirtualizationMode="Recycling"`

---

### 🟢 Low Priority

#### 5. OMEN Key Opens Main App Instead of Quick Access
**Status:** Previously reported, still occurring for some users

**Investigation:**
- Check `OmenKeyService` hotkey registration
- May be conflict with OGH or other software

---

## 📝 Technical Notes

### Fan Curve Safety Fix
**Before (Bug):**
```csharp
var targetPoint = _activeCurve.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                  ?? _activeCurve.FirstOrDefault(); // BUG: Returns lowest fan%!
```

**After (Fixed):**
```csharp
var targetPoint = _activeCurve.LastOrDefault(p => p.TemperatureC <= maxTemp) 
                  ?? _activeCurve.LastOrDefault(); // SAFE: Returns highest fan%
```

### Fan Diagnostics Mode
Added to FanService:
- `_diagnosticModeActive` volatile bool flag
- `EnterDiagnosticMode()` / `ExitDiagnosticMode()` public methods
- Curve engine checks flag in `ApplyCurveIfNeededAsync()` and skips if true

### MaxFanLevel Calibration (Future)
Currently hardcoded `MaxFanLevel = 55` but this may vary by model:
- Some models use 0-55 (5500 RPM max)
- Some models use 0-100 directly
- Need to detect or allow user calibration

---

## 🔧 Fix Summary

| Issue | Priority | Status |
|-------|----------|--------|
| Fan drops to 0 RPM | 🔴 Critical | ✅ Fixed |
| Fan diag curve override | 🟠 High | ✅ Fixed |
| 100% fan not max RPM | 🟠 High | ✅ Fixed |
| Fan diag UI stuck | 🟠 High | ✅ Fixed |
| Smart App Control | 🟡 Medium | ✅ Documented |
| Missing HardwareWorker | 🟠 High | 🔄 TODO |
| Curve speed offset | 🟠 High | 🔄 Investigate |
| RGB on Thetiger | 🟡 Medium | 🔄 Investigate |
| UI virtualization | 🟡 Medium | 🔄 TODO |

---

## 📊 Affected Models

| Model | Issue(s) |
|-------|----------|
| OMEN 16 | Fan drops to 0 RPM (fixed) |
| Thetiger OMN (8BCA) | RGB, curve offset |
| Various | Smart App Control blocking |

---

## 🔗 Related Issues

- GitHub #39, #40 - Temperature freeze (fixed in v2.2.2)
- GitHub #37 - RDP window popup

---

**Files Changed:**
- `FanService.cs` - Curve fallback fix, diagnostic mode
- `WmiFanController.cs` - Curve fallback fix
- `FanVerificationService.cs` - SetFanMax for 100%
- `FanDiagnosticsViewModel.cs` - Diagnostic mode integration, UI refresh
- `ANTIVIRUS_FAQ.md` - Smart App Control docs
