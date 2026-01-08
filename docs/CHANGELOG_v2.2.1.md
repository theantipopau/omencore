# OmenCore v2.2.1 - Bug Fixes & EC Reset Feature

**Release Date:** January 2026  
**Type:** Patch Release

## Summary

This release addresses several bugs reported by the community after v2.2.0, and adds a new EC Reset feature by user request.

---

## ✨ New Features

### EC Reset to Defaults
**Request:** User requested an option to reset the EC (Embedded Controller) to factory defaults so BIOS displays return to normal values.

**Implementation:**
- Added "Reset EC to Defaults" button in Settings → Hardware Driver section
- Performs comprehensive EC reset sequence:
  - Clears all manual fan speed overrides
  - Disables fan boost mode
  - Restores BIOS control of fans
  - Resets thermal policy timers
  - Sets performance mode to Balanced
- Shows confirmation dialog explaining what will be reset
- Success/failure feedback with troubleshooting tips

**Use this if:**
- BIOS shows stuck or incorrect fan speed values
- Fan behavior doesn't match expected after using OmenCore
- You want to completely restore factory fan behavior

---

## 🐛 Bug Fixes

### Thermal Protection Logic (#32)
**Issue:** Thermal protection was reducing fan speed instead of boosting it when temperatures reached warning threshold (82°C). After thermal event, fan preset was incorrectly restored to "Quiet" instead of the user's selected mode.

**Root Cause:** The thermal protection system was applying a hardcoded percentage (77%) instead of boosting to maximum, and incorrectly restoring the saved preset regardless of the active performance mode.

**Fix:** 
- Thermal protection now correctly boosts fan speed to maximum when triggered
- System now remembers and restores the actual active preset/performance mode after thermal event
- When in Performance mode with max fans, thermal protection no longer interferes

---

### System Tray Max/Auto Mode Not Working (#33)
**Issue:** Selecting "Max" or "Auto" from the system tray menu didn't actually set the correct fan mode. The button state also wasn't persisting between menu opens.

**Root Cause:** The tray menu was applying "Performance" preset instead of true max fan mode when "Max" was selected. Auto mode logic was also not being properly applied.

**Fix:**
- "Max" option in tray now correctly enables SetFanMax for 100% fan speed
- "Auto" option properly enables auto fan curve mode
- Button states correctly reflect the current mode on menu open

---

### OMEN Max 16 Light Bar Zone Order Inverted
**Issue:** On OMEN Max 16 (Intel 255HX with RTX 5080), the RGB zones control the front light bar but the zone order is inverted (right-to-left instead of left-to-right).

**Root Cause:** The OMEN Max 16 has a different zone mapping for its front light bar compared to older models.

**Fix:**
- Added OMEN Max 16 detection for light bar zone mapping
- Zones now correctly map from left-to-right (Zone 1 = Left, Zone 4 = Right)
- Added setting to manually invert zone order for edge cases

---

### OMEN Max 16 Keyboard Lighting Not Working
**Issue:** On OMEN Max 16, the "Keyboard Lighting" section only controls the front light bar, not the actual keyboard backlight. The keyboard has single-color white/amber lighting that isn't controllable via OmenCore.

**Status:** This is a hardware limitation, not a bug. The OMEN Max 16 keyboard does not support per-zone RGB control - it has a simple single-color backlight controlled by the Fn+F4 key. The RGB section in OmenCore controls the 4-zone front light bar which is the only RGB lighting on this model.

**Improvement:**
- UI now clarifies that RGB controls are for "Light Bar" on OMEN Max 16 models
- Added detection for single-color keyboard backlight vs RGB keyboards

---

### Monitor Tab CPU Temperature Stuck at 0°C (#35)
**Issue:** On OMEN 16-n0xxx models, the CPU temperature in the Monitor tab shows 0°C constantly while the General tab shows correct temperature.

**Root Cause:** Different temperature source being used for the detailed monitor vs quick status display.

**Fix:**
- Unified temperature source across all UI elements
- Monitor tab now uses the same reliable temperature provider as General tab

---

### CPU Temperature Always Shows 96°C (#36)
**Issue:** CPU temperature displays a constant 96°C regardless of actual temperature, even on cold boot.

**Root Cause:** Some models report TjMax (96°C) instead of current temperature through certain WMI paths.

**Fix:**
- Added fallback temperature reading from LibreHardwareMonitor
- Cross-validates WMI temperature with hardware sensor data
- Displays accurate temperature from the most reliable source

---

### Temperature Monitoring Freezes When Drives Sleep
**Issue:** CPU/GPU temperatures would freeze and stop updating after the error "Cannot access a disposed object. Object name: 'SafeFileHandle'" appears in HardwareWorker.log.

**Root Cause:** When a storage drive goes to sleep (power saving), LibreHardwareMonitor's `UpdateVisitor` throws a `SafeFileHandle` disposed exception. This exception was thrown **before** entering the per-hardware try/catch block, causing the entire update cycle to abort without updating any sensor values.

**Fix:**
- Wrapped `UpdateVisitor.Accept()` call in try/catch to handle disposed storage devices
- Made `UpdateVisitor.VisitHardware()` itself resilient to disposed object exceptions
- Storage devices going to sleep no longer affect CPU/GPU temperature monitoring
- Other hardware sensors continue updating even when one device fails

**Technical Details:**
```csharp
// Before: Entire update aborted if any hardware threw
_computer.Accept(new UpdateVisitor());

// After: Catch at visitor level + individual hardware level
try { _computer.Accept(new UpdateVisitor()); }
catch (ObjectDisposedException) { /* Continue with individual updates */ }

// Plus: UpdateVisitor now catches disposed errors per-hardware
public void VisitHardware(IHardware hardware)
{
    try { hardware.Update(); ... }
    catch (ObjectDisposedException) { /* Skip this hardware */ }
}
```

---

## 📋 Known Issues

### OMEN Max 16 Specific
- Keyboard backlight is single-color only (hardware limitation) - use Fn+F4 for brightness
- RGB controls only affect the front light bar
- Some users may need to use EC backend for lighting on certain configurations

### Linux (Fedora 43+)
- **ec_sys module not available** - Fedora 43+ kernels don't include the `ec_sys` module by default
  - **Workaround 1:** Use `hp-wmi` driver instead: `sudo modprobe hp-wmi` (for OMEN 2023+ models)
  - **Workaround 2:** Build `ec_sys` module from kernel source
  - **Workaround 3:** Some systems have EC access via `/sys/kernel/debug/ec/ec0/io` without needing `ec_sys`
  - This affects fan speed reading on affected systems

### General
- Secure Boot blocks WinRing0 MSR access (undervolt unavailable)
- Some BIOS versions may not support all WMI commands

---

## 📝 Feature Requests (For Future Releases)

*No pending requests - EC Reset feature implemented in this release!*

---

## 🔧 Technical Details

### EC Reset Implementation
The EC Reset feature writes to the following registers (OMEN EC register map):
- `0x34`, `0x35` - Fan 1/2 speed set (clears manual speeds)
- `0x2E`, `0x2F` - Fan 1/2 PWM percent (clears manual PWM)
- `0xEC` - Fan boost mode (disables boost)
- `0xF4` - Fan state (returns to BIOS control)
- `0x62` - BIOS control flag (restores BIOS authority)
- `0x63` - Timer register (clears countdown)

WMI-based reset sequence:
1. SetFanMax(false) - Disable max fan mode
2. SetFanMode(0xFF) - Default mode
3. SetFanLevel(0,0) + SetFanLevel(1,0) - Clear fan levels
4. ExtendFanCountdown(0) - Clear timer
5. SetFanMode(0x0) - Balanced mode
6. SetFanMode(0xFF) - Final reset

### Models Affected
- **OMEN MAX Gaming Laptop 16t-ah000** (Intel Core Ultra 7 255HX, RTX 5080)
- **OMEN 16-n0xxx** series
- Various models with thermal protection edge cases
- **Linux:** Fedora 43+ users affected by ec_sys removal

### Fixes Applied
- `FanService.cs` - Thermal protection logic overhaul, EC Reset wrapper
- `MainViewModel.cs` - Tray menu Max/Auto/Quiet mode handling
- `KeyboardLightingService.cs` - Light bar zone mapping for OMEN Max 16
- `LibreHardwareMonitorImpl.cs` - Temperature stuck-at-TjMax detection
- `AppConfig.cs` - Added InvertRgbZoneOrder setting
- `SettingsViewModel.cs` / `SettingsView.xaml` - Zone inversion toggle, EC Reset button
- `FanControllerFactory.cs` - IFanController.ResetEcToDefaults() interface
- `WmiFanController.cs`, `FanController.cs` - EC Reset implementations
- `OmenCore.HardwareWorker/Program.cs` - Temperature freeze fix
- `StatusCommand.cs` (Linux) - Improved error messages for missing ec_sys/hp-wmi

---

## 📥 Downloads

| File | SHA256 |
|------|--------|
| OmenCoreSetup-2.2.1.exe | `A1A641C00A9BCF4A496E6A60AA3D7083B234C0B9CB954EA5D6563B6DE1B9DE6A` |
| OmenCore-2.2.1-win-x64.zip | `B9E00DAEDC895C2EDDE07E10D68A324B8C42BFDCB526D9DD49440E8EC05CF75A` |
| OmenCore-2.2.1-linux-x64.zip | `27BECB535FE39B691E4E95D7B3F18F4107134E78F66AED407CF6FCC54C3A66D3` |

---

## 🙏 Acknowledgments

Thanks to the community members who reported these issues:
- @kg290 - Thermal protection and tray menu bugs (#32, #33, #34)
- @its-urbi - Monitor tab temperature bug (#35)
- @yongzhouwangbowen - CPU temperature stuck at 96°C (#36)
- @dfshsu - Fedora 43 ec_sys module issue (Discord)
- Reddit user - OMEN Max 16 light bar zone order and keyboard lighting clarification

---

**Full Changelog:** [v2.2.0...v2.2.1](https://github.com/theantipopau/omencore/compare/v2.2.0...v2.2.1)
