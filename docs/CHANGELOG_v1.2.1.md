# OmenCore v1.2.1 Release Notes

**Release Date:** January 2025  
**Type:** Hotfix Release

## Bug Fixes

### Fan Control
- **Fixed: Fan stuck on Max speed** - After using Max fan mode, changing to other profiles now properly resets fan speed. Added explicit `SetFanMax(false)` command with 100ms delay to ensure BIOS processes the mode change before applying new fan curves.

### UI Fixes
- **Fixed: Cannot type in preset name** - The custom preset name TextBox now accepts keyboard input correctly. Added explicit `IsReadOnly="False"`, `Focusable="True"`, and `Mode=TwoWay` binding properties.

### GPU Detection
- **Fixed: AMD iGPU not detected in hybrid systems** - Systems with AMD APU (Radeon 610M/680M/780M) + NVIDIA discrete GPU now correctly show "Hybrid" mode instead of "Discrete". This affects OMEN laptops with AMD Ryzen processors and NVIDIA graphics.

### AMD Undervolt Support (GitHub Issue #8)
- **Added support for additional AMD CPUs:**
  - AMD Ryzen 9 8940HX (Hawk Point)
  - AMD Ryzen 9 8940H
  - AMD Ryzen 7 8845H
  - AMD Ryzen 7 8840H
  - AMD Ryzen 7 6800H
  - Generic H-series and HX-series mobile Ryzen CPUs

## Technical Details

### Files Changed
- `WmiFanController.cs` - Added delay after disabling fan max mode
- `FanControlView.xaml` - Fixed TextBox binding properties
- `GpuSwitchService.cs` - Added AMD iGPU detection for hybrid mode
- `RyzenControl.cs` - Extended CPU support list for undervolting

## Upgrade Notes
- This is a drop-in replacement for v1.2.0
- No configuration changes required
- Existing fan presets and settings are preserved

## Known Issues
- None reported for this version

## Download
- **Installer:** `OmenCoreSetup-1.2.1.exe`
- **Portable:** Extract `publish/win-x64/` folder

---

## Issues Addressed
- GitHub Issue #4: OMEN 17-ck2003ns hybrid GPU detection
- GitHub Issue #8: AMD Ryzen 9 8940HX undervolt not working
- Community reported: Fan max mode persistence bug
- Community reported: Preset name TextBox not accepting input
