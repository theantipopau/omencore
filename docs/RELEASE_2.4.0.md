# OmenCore v2.4.0 - Release Summary

**Release Date:** January 15, 2026  
**Status:** ✅ Production Ready

## Release Artifacts

### Windows
- **OmenCoreSetup-2.4.0.exe** (100.51 MB)
  - Windows installer with all dependencies
  - Self-contained .NET 8.0 runtime
  - Hardware Worker included for crash isolation
  - SHA256: `91DAF951A8E357B90359E7C1557DC13EF3472F370F0CB30073C541244FCAE32C`

- **OmenCore-2.4.0-win-x64.zip** (103.78 MB)
  - Portable version without installer
  - Self-contained executable
  - SHA256: `18CEB337EB9FA99604F96A352E48744601703046DEA54528BDDFD666E35F0DE1`

### Linux x86_64
- **OmenCore-2.4.0-linux-x64.zip** (66.24 MB)
  - CLI daemon and Avalonia GUI
  - Self-contained for glibc-based systems
  - SHA256: `6C13F67F377D7140ECE28DABAC77C9C0267636BE732E87512AED466D7B0DE437`

### Linux ARM64
- **OmenCore-2.4.0-linux-arm64.zip** (35.80 MB)
  - ARM64 optimized for Raspberry Pi and similar devices
  - CLI daemon and Avalonia GUI
  - SHA256: `60BF36CCECC576642830DC8E85AD747A9D534E491984A5445E3BDB9A2AFE5408`

## Key Improvements

### 🔴 Critical Fixes
1. **GitHub #49 - Fan Runaway (SAFETY CRITICAL)**
   - Multi-layer Math.Clamp protection (0-100%)
   - Prevents fans from exceeding safe maximum speed
   - Files: FanCurveService.cs, FanCurveEngine.cs

2. **EC 0x2C Blocking Resolution**
   - Confirmed 0x2C already in allowlist since v2.1.0+
   - Users on older versions should update to v2.4.0

3. **UI Freeze During Gaming (FIXED)**
   - WMI timeout + Dispatcher throttling
   - No more hangs during extended gaming sessions

### ✨ GitHub #47 - Thermal Protection
- Tuned Quiet mode fan curve for optimal thermal protection
- Prevents overheating to 75°C+ in Quiet mode
- Maintains acoustics while ensuring safety

### ✨ GitHub #48 - UX/UI Improvements
- **Settings reorganized into 5 tabs:**
  - **Status**: System info, backend status, Secure Boot, PawnIO, OGH
  - **General**: Startup, minimize to tray, Corsair settings, auto-update
  - **Advanced**: Monitoring intervals, hotkeys, fan hysteresis, power, EC reset, OMEN key
  - **Appearance**: OSD settings, notifications
  - **About**: Version info, update settings, links

- **Diagnostics Tab**: Combined fan and keyboard diagnostics
- **Collapsible Logs**: Hide/show application logs in UI
- **Smart Update Banner**: Only shown when update is actually available

### 🔧 Quality Improvements
- Enhanced fallback telemetry for fan control failures
- CPU sensor diagnostic logging (helps troubleshoot temp reading issues)
- All version strings updated to 2.4.0 consistently

## Build Information
- **Configuration**: Release
- **Framework**: .NET 8.0
- **Build Errors**: 0
- **Build Warnings**: 0
- **Latest Commit**: 9dd69f6

## Release Commits (10 total)
```
9dd69f6 - Add CPU sensor diagnostic logging to HardwareWorker
a03a1af - Update all remaining version strings to 2.4.0
71607d6 - Fix build errors (WMI timeout API, XAML structure)
7467f65 - Complete Settings sub-tabs reorganization
095963e - UI reorganization (Diagnostics tab)
1b2e177 - Critical safety fixes & enhanced diagnostics
c09913e - Fix GitHub #47: Tune Quiet mode fan curve
00f0fa0 - Implement GitHub #48 UI improvements
d5b5f92 - Fix critical issues (Linux RAM, UI freeze)
3d2ddf0 - Roadmap update with v2.4.0 issues
```

## Installation Instructions

### Windows
1. Download `OmenCoreSetup-2.4.0.exe`
2. Run the installer
3. Follow the setup wizard
4. Application will start automatically

**Portable Alternative:**
1. Extract `OmenCore-2.4.0-win-x64.zip`
2. Run `OmenCore.exe`

### Linux
1. Download `OmenCore-2.4.0-linux-x64.zip` (x86_64) or `OmenCore-2.4.0-linux-arm64.zip` (ARM64)
2. Extract the archive
3. Run `./omencore-gui` for GUI or `./omencore-cli` for CLI daemon

## Known Issues
- None reported for v2.4.0

## Security Notes
- All downloads are SHA256 verified above
- Self-contained binaries reduce dependency vulnerabilities
- Hardware Worker runs in separate process for crash isolation
- WinRing0/PawnIO drivers optional for EC access (Secure Boot compatible)

## Support
- Report issues: https://github.com/theantipopau/omencore/issues
- Documentation: https://github.com/theantipopau/omencore/wiki

---

**Ready for production deployment.**
