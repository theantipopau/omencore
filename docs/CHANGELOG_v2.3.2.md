# OmenCore v2.3.2 - Critical Safety & Bug Fix Release

**Release Date:** 2026-01-14

This release adds critical desktop safety protections and fixes multiple bugs reported after v2.3.1.

---

## 🛡️ Safety Improvements

### 🔴 **CRITICAL: Desktop PC Protection** (Safety)
- **Issue**: User installed OmenCore on OMEN 40L Desktop, causing cooling system failure
- **Root Cause**: OmenCore is designed for **OMEN LAPTOPS ONLY** - desktop EC registers are completely different
- **Fixes Implemented**:
  1. ✅ **Blocking dialog** on desktop detection - requires explicit user acknowledgment
  2. ✅ **Fan control disabled by default** on desktop systems
  3. ✅ **Emphatic warning** replaced "experimental" with "NOT SUPPORTED - USE AT YOUR OWN RISK"
  4. ✅ **README updated** with prominent desktop warning
- **Reporter**: u/PackRare5146 (Reddit)

---

## 🐛 Bug Fixes

### ✅ **Linux GUI Crash on Startup** (Fixed)
- **Symptom**: `StaticResource 'DarkBackgroundBrush' not found` crash on Debian 13, Ubuntu 24.04
- **Root Cause**: Avalonia views used `StaticResource` which loads before App resources on Linux
- **Fix**: Changed all `StaticResource` to `DynamicResource` in Avalonia XAML views
- **Reporters**: @dfshsu, @SlopeSlayer910
- **Files**: `MainWindow.axaml`, `DashboardView.axaml`, `SystemControlView.axaml`, `SettingsView.axaml`

### ✅ **OSD Not Updating on Mode Change** (Fixed)
- **Symptom**: OSD overlay showed stale performance/fan mode after switching
- **Fix**: Added `OsdService.SetPerformanceMode()` and `SetFanMode()` calls when modes change
- **Reporter**: @SimplyCarrying
- **Files**: `AdvancedViewModel.cs`

### ✅ **OMEN Max/17-ck Fan Control Issues** (Improved)
- **Symptom**: Fans stuck at high RPM, `BIOS command Default:37 returned code 6`
- **Fix**: Improved V2 command fallback logic - gracefully falls back to V1 when V2 fails
- **Note**: Some models may still have issues - we need more testing data
- **Reporters**: @kastenbier2743, @xenon205 (GitHub #44)
- **Files**: `HpWmiBios.cs`

### ✅ **Window Rounded Corners** (Improved) - GitHub #45
- **Symptom**: Border stroke visible but corners square
- **Fix**: Added explicit Clip geometry and improved background transparency handling
- **Reporter**: @its-urbi
- **Files**: `MainWindow.xaml`

### ✅ **Window Minimum Size Reduced**
- **Change**: Reduced minimum size from 900×600 to 850×550
- **Reporter**: @replaY!
- **Files**: `MainWindow.xaml`, `MainWindow.axaml`

---

## ⚠️ Known Issues & FAQ

### **Fans Running Higher Than Usual**
- **Cause**: v2.3.1 added 30% minimum fan floor when thermal protection releases
- **Status**: Working as intended - prevents 0 RPM bug at 60-70°C

### **FPS Counter Inaccurate** (v2.4.0)
- **Cause**: FPS is estimated from GPU load, not actual frame rate
- **Fix**: D3D11 hook planned for v2.4.0
- **Reporters**: @Glumgy, @SimplyCarrying

### **CPU Undervolt Not Working**
- **Answer**: Requires WinRing0 (blocked by Secure Boot) or PawnIO driver
- **Guide**: See [WINRING0_SETUP.md](WINRING0_SETUP.md) or install PawnIO

### **"Limited Mode" Warning**
- **Answer**: Expected when OMEN Gaming Hub is not installed
- **Recommendation**: Keep OGH installed for best compatibility, or use PawnIO

---

## 🔧 Technical Changes

### MainViewModel.cs (Desktop Safety)
- Added blocking `MessageBox` dialog for desktop detection
- Changed warning from "experimental" to "NOT SUPPORTED"
- Fan control disabled by default on desktop systems
- User must explicitly acknowledge risk to enable fan writes

### Avalonia Views (Linux Fix)
- Changed `StaticResource` → `DynamicResource` for brush references
- Fixes resource loading order on Linux where App.axaml loads after views

### HpWmiBios.cs (Fan Control)
- Improved V2 command fallback - gracefully falls back to V1 when V2 returns error
- Better handling for OMEN Max/17-ck models where V2 forcing causes issues

### AdvancedViewModel.cs (OSD Fix)
- Added `OsdService.SetPerformanceMode()` call on performance mode change
- Added `OsdService.SetFanMode()` call on fan mode change

### MainWindow.xaml (UI)
- Added explicit `Clip` geometry for rounded corners
- Reduced minimum size to 850×550
- Improved background transparency handling

---

## 📦 Download

### Windows
- **OmenCoreSetup-2.3.2.exe** - Full installer with auto-update
  - SHA256: `8F69EB1C9904B986E233463B8503255026D2AF00696F4CB21114BF3FDA93187D`
- **OmenCore-2.3.2-win-x64.zip** - Portable version
  - SHA256: `BB960CD320C451857B890E228B144B0633212E719F45CB4DBBAEF6A19C5988DD`

### Linux
- **OmenCore-2.3.2-linux-x64.zip** - GUI + CLI bundle
  - SHA256: `1DDC02599E4CA008A328A519D12F68AF02E375DB42BB242C91609293CF215A12`

---

## 🙏 Credits

**Bug Reports**:
- **PackRare5146** (Reddit) - OMEN 40L Desktop damage report ⚠️
- **xenon205** (GitHub #44) - OMEN 17-ck1xxx fan presets broken
- **its-urbi** (GitHub #45) - Window rounded corners issue
- **dfshsu** (Discord) - Linux GUI crash on Debian 13
- **SlopeSlayer910** (Discord) - Linux GUI crash on Ubuntu 24.04
- **SimplyCarrying** (Discord) - OSD not updating, FPS counter issue
- **kastenbier2743** (Discord) - OMEN Max fan control issues
- **replaY!** (Discord) - Window sizing on multi-monitor
- **Solar/PMMM** (Discord) - Fan behavior and temp freeze issues
- **Glumgy** (Discord) - FPS counter inaccuracy report
- **Goga** (Discord) - Undervolt FAQ question
- **Anonymous** (Reddit) - Limited Mode FAQ, FPS OSD question

---

## 📖 Upgrade Notes

**Linux users**: This release fixes the GUI crash - please update if you experienced the `DarkBackgroundBrush not found` error.

**Windows users**: No critical changes - update optional unless you want the latest fixes.

---

## 🚀 What's Next?

### Deferred from v2.3.2 → v2.4.0
These features were originally planned for v2.3.2 but are deferred to focus on critical bug fixes:
- **OSD horizontal layout full XAML implementation** - v2.3.1 added toggle, full layout in v2.4.0
- **OSD preset layouts** (Minimal, Standard, Full, Custom) - v2.4.0
- **More robust storage exclusion** - v2.3.1 already handles storage sleep gracefully via SafeFileHandle fix

### v2.4.0 (Planned)
- Accurate FPS counter via D3D11 hook (no RivaTuner needed)
- OSD layout editor with presets
- Per-game OSD profiles
- Full horizontal OSD layout
