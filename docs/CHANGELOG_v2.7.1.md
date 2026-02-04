# OmenCore v2.7.1 Changelog

**Release Date:** February 4, 2026  
**Branch:** `main`

---

## ✨ New Features

### UI/UX Redesign - Tab Reorganization
- **New "OMEN" Tab**: Renamed "Advanced" to "OMEN" - now contains HP OMEN-specific features only:
  - Performance Modes (Quiet/Balanced/Performance)
  - Custom Fan Curves
  - GPU Switching (Hybrid/Discrete mode)
  - GPU Power Boost (TGP/Dynamic Boost via HP WMI)
- **Renamed "Tuning" to "CPU/GPU Tuning"**: Contains hardware tuning features:
  - CPU Undervolt (voltage offset)
  - CPU Power Limits (PL1/PL2)
  - TCC Offset (thermal throttle point)
  - GPU Overclocking (NVIDIA clock offsets)
- **Removed Duplicate Sections**: Previously, CPU undervolt, power limits, and GPU OC appeared in both tabs

### Active OC Status Display
- **Prominent OC Indicator**: GPU Overclocking section now shows "ACTIVE OC" badge with live values
- **Real-time Display**: Shows current Core offset, Memory offset, and Power Limit % at a glance
- **Example**: `ACTIVE OC | Core: +50 MHz | Memory: +0 MHz | Power: 100%`

### GPU Tuning Tab Redesign
- **Vendor Branding**: GPU section now displays vendor logo (NVIDIA/AMD/Intel) based on detected GPU
- **GPU Info Display**: Shows GPU model name and formatted driver version (e.g., "566.14" for NVIDIA)
- **Dynamic Title**: Section title changes to "NVIDIA GPU Overclocking" or "AMD GPU Overclocking" based on vendor
- **AMD Coming Soon Notice**: AMD GPU users see an info box explaining ADLX integration is in development

### CPU Info in Sidebar
- **Cores/Threads Display**: Sidebar now shows CPU core and thread count beneath the processor name
- Example: "24 Cores / 32 Threads" for Intel Core i9-13900HX

### Keyboard RGB Improvements
- **8 New Color Presets**: Added Cyberpunk, Sunset, Forest, Ocean, Lava, Ice, Stealth, and Off presets
- **Quick Color Buttons**: New "⚡ Quick Colors" section with one-click color buttons (Red, Blue, Green, Purple, Yellow, White, Cyan, Pink, Off)
- **Instant Apply**: Quick color buttons apply the selected color to all 4 keyboard zones immediately

### Corsair Device Identification
- **"💡 Identify" Button**: Each Corsair device card now has an Identify button
- **Flash Pattern**: Clicking it flashes the device LEDs white/off to help identify which physical device it is
- **Multi-Device Support**: Works with all Corsair devices (headsets, mice, keyboards) including multiple devices

### OpenRGB Integration (Desktop RGB Support)
- **Generic RGB Control**: Added OpenRGB provider for controlling any RGB device supported by OpenRGB
- **Motherboard RGB**: Control onboard RGB LEDs (ASUS Aura, MSI Mystic Light, Gigabyte RGB Fusion, etc.)
- **RAM & GPU RGB**: Control RGB on RAM sticks and graphics cards
- **LED Strips & Fans**: Control addressable LED strips, RGB fans, and coolers
- **Quick Colors**: One-click color buttons (Red, Blue, Green, Purple, Yellow, White, Orange, Off)
- **Automatic Discovery**: Connects to OpenRGB SDK server on startup (default: localhost:6742)
- **Requirement**: Requires OpenRGB to be installed and running with SDK Server enabled

### GUI Visual Polish
- **New Animations**: Added loading spinner, success pop, and smooth rotation animations
- **Status Badges**: Added Success, Warning, Error, and Info badge styles
- **Hover Glow Effect**: Interactive elements now have subtle accent color glow on hover
- **Consistent Styling**: Improved visual consistency across all UI elements

### NvAPIWrapper Integration
- **Proper NVAPI Structure Handling**: Integrated NvAPIWrapper.Net library (v0.8.1.101) for reliable GPU overclocking on RTX 40 series cards
- **Better Compatibility**: Uses proper `PerformanceStates20InfoV1` structures instead of manual P/Invoke marshaling
- **Fallback Support**: Falls back to legacy P/Invoke if NvAPIWrapper fails

---

## 🐛 Bug Fixes

### Battery Health Now Shows Real Data
- **Fixed Hardcoded 100%**: Battery health was always showing 100% regardless of actual battery condition
- **Real WMI Data**: Now reads actual battery health by comparing FullChargeCapacity to DesignCapacity via WMI
- **Proper Status**: Shows "Good" (≥80%), "Fair" (50-79%), or "Replace" (<50%) based on actual health
- **Efficient Caching**: Battery health is cached for 60 seconds to avoid excessive WMI queries

### Fan Diagnostics Preset Restoration
- **Preset Now Restored After Test**: Fan diagnostic tests (both single fan and guided) now restore the previous fan preset after completion
- **No More Max Speed Stuck**: Previously, running diagnostics would leave fans at 100% - now they return to Auto/Custom/whatever was active before
- **Safe Exit**: Both normal completion and cancellation properly restore fan state

### Fan Diagnostics UI Fixes
- **"Testing GPU fan" Text Hidden**: The status text is now hidden when no test is running (was always visible)
- **Guided Diagnostic Panel Width**: Increased panel width from 200 to 280px to prevent text cutoff
- **Better Responsiveness**: Panel now uses MinWidth/MaxWidth for proper sizing

### Fan Control "Active" Display Fix
- **Compact Active Indicator**: The "Active: Auto" status badge is now compact instead of stretching across the entire row
- **Better Layout**: Active mode indicator no longer leaves large empty space in the fan control area

### Bloatware Manager Improvements
- **Auto-Scan on Open**: Bloatware Manager now automatically scans when opened (no more clicking "Scan" first)
- **HP Support Assistant Exclusion**: HP Support Assistant is now correctly excluded from bloatware detection (was showing as removable)
- **Better Bloatware Detection**: Only flags HPPrivacySettings, HPSystemEventUtility, and HP Customer apps - not HP Support Assistant which is needed for driver updates
- **Compact Toolbar**: Reduced button sizes and search field width to prevent UI clipping on smaller screens
- **Better Responsiveness**: Toolbar elements now scale properly with window size

### OMEN Key Interception Warning
- **OGH Conflict Warning**: Settings now shows a warning when OGH is installed explaining that the OMEN key will open OGH instead of OmenCore
- **Clear Guidance**: Users are directed to use Bloatware Manager to remove OGH for OMEN key to work with OmenCore

### GPU Vendor Logo Display
- **Fixed Image Paths**: GPU vendor logos (NVIDIA/AMD/Intel) now use full pack URIs to ensure proper loading
- **Affected**: Sidebar GPU list and Tuning tab GPU section

### Desktop Detection Fix (Critical)
- **Non-HP Desktops No Longer Blocked**: Fixed issue where OmenCore would immediately close on any desktop PC, not just OMEN desktops
- **Improved Detection Logic**: Desktop blocking now only triggers for confirmed HP OMEN desktop systems (25L, 30L, 35L, 40L, 45L, Obelisk)
- **Chassis Check Refinement**: Chassis type check now only runs for HP systems with "OMEN" in the model name
- **Better Logging**: Added clearer log messages explaining why desktop check passed or failed
- **Graceful Fallback**: Non-HP desktops will now launch normally (with monitoring-only mode if fan control fails)

### GPU Driver Version Display
- **Driver Version Now Preserved**: Fixed issue where GPU driver version wasn't displayed in the Tuning tab
- **NVIDIA Format Conversion**: WMI driver version (e.g., "32.0.15.6614") now properly formatted to user-friendly format ("566.14")

### Update Process Improvements ([#58](https://github.com/theantipopau/omencore/issues/58))
- **Fixed System Slowdown During Update**: Removed `/CLOSEAPPLICATIONS` flag that was causing system-wide application closure
- **Proper Process Cleanup**: OmenCore now explicitly stops HardwareWorker process before launching installer
- **Installer Pre-Install Hook**: Installer now kills OmenCore processes before copying files to prevent file locks
- **Cleaner Update Flow**: Uses `/SILENT /NORESTART` instead of aggressive flags

### PawnIO Installer Fix ([#59](https://github.com/theantipopau/omencore/issues/59))
- **Correct Silent Parameter**: Changed PawnIO installer argument from `/SILENT` to `-silent` (PawnIO uses dash-style parameters)

### Fan Control Fix for OMEN 17-ck2xxx / Transcend Models
- **Critical Bug**: WMI fan commands returned success but fans didn't actually respond on OMEN 17-ck2xxx (2023) and Transcend 14/16 models
- **Root Cause**: WMI BIOS interface on these models accepts commands but doesn't apply them to hardware
- **Fix**: Model capability database now correctly marks `SupportsFanControlWmi = false` for 17-ck2 models
- **Behavior Change**: These models will now skip WMI and try EC access or OGH proxy instead
- **User Impact**: If you have a 17-ck2 or Transcend, install PawnIO from [pawnio.eu](https://pawnio.eu) for fan control, or install OMEN Gaming Hub as a fallback
- **Symptoms Fixed**: Max profile going back to low RPM, Gaming/Extreme profiles stuck at low RPM

### Previous v2.7.0 Bug (Now Fixed)
The v2.7.0 desktop detection was overly aggressive:
- It checked chassis type for ALL systems, not just HP OMEN
- Chassis types 3, 4, 5, 6, 7, 13, 15 (desktop types) triggered immediate app shutdown
- Users on non-HP desktop PCs reported the app closing immediately on launch

---

## 🔧 Technical Changes

### OmenCoreApp.csproj
- Added `NvAPIWrapper.Net` package reference (v0.8.1.101) for NVIDIA GPU control

### NvapiService.cs
- Added NvAPIWrapper imports and `_primaryGpu` PhysicalGPU field
- New `Initialize()` flow using `NVIDIA.Initialize()` with legacy fallback
- New `QueryPowerLimitsWrapper()` and `QueryOcSupport()` methods
- New `SetClockOffsetWrapper()` and `SetBothClockOffsetsWrapper()` using proper PerformanceStates20 structures
- Updated `SetCoreClockOffset()` and `SetMemoryClockOffset()` to try NvAPIWrapper first

### SystemControlViewModel.cs
- Added GPU vendor detection properties: `GpuVendor`, `IsNvidiaGpu`, `IsAmdGpu`, `GpuOcSectionVisible`
- Added `GpuDisplayName` and `GpuDriverVersion` properties
- Added `DetectGpuVendor()` method to detect GPU from SystemInfo
- Added `InitializeAmdGpu()` placeholder for future ADLX integration
- Fixed driver version preservation when NVAPI initializes

### SystemInfoService.cs
- Added `DriverVersion` query from WMI `Win32_VideoController`
- Added `FormatNvidiaDriverVersion()` helper to convert WMI format to user-friendly format

### MainWindow.xaml
- Added CPU cores/threads display in sidebar below processor name

### TuningView.xaml
- Redesigned GPU section with vendor logo, GPU name, and driver version
- Added DataTriggers for NVIDIA/AMD/Intel logo switching
- Added "AMD Coming Soon" info box for AMD GPU users

### ModelCapabilityDatabase.cs
- Updated OMEN 17-ck2xxx (2023) entry: `SupportsFanControlWmi = false`
- Added notes explaining WMI is ineffective on this model and OGH/EC required

### CapabilityDetectionService.cs
- `DetermineFanControlMethod()` now checks `ModelConfig.SupportsFanControlWmi` before using WMI
- Logs warning when model database indicates WMI fan control is ineffective
- Affected models will skip WMI and try EC access or OGH proxy instead

### LightingViewModel.cs
- Added `ApplyQuickColorCommand` for one-click keyboard color application
- Added `ApplyQuickColorAsync()` method to apply a color to all 4 keyboard zones
- Added 8 new keyboard presets: Cyberpunk, Sunset, Forest, Ocean, Lava, Ice, Stealth, Off
- Added `FlashCorsairDeviceCommand` for device identification

### LightingView.xaml
- Added "⚡ Quick Colors" section with 9 color buttons (Red, Blue, Green, Purple, Yellow, White, Cyan, Pink, Off)
- Added "💡 Identify" button to each Corsair device card in the peripherals section

### ICorsairSdkProvider.cs / CorsairDeviceService.cs
- Added `FlashDeviceAsync()` method for device LED identification
- Implemented in CorsairSdkStub, CorsairICueSdk, and CorsairHidDirect

### CorsairDevice.cs
- Added `CurrentColorHex` property to store current color for flash restore

### OpenRgbProvider.cs (NEW)
- New OpenRGB SDK client for generic RGB device control
- Connects to OpenRGB server via TCP (default: localhost:6742)
- Supports device discovery, LED count enumeration, and color control
- Device types: Motherboard, RAM, GPU, Cooler, LED Strip, Keyboard, Mouse, etc.

### MainViewModel.cs
- Added OpenRGB provider initialization on startup
- Registers OpenRGB provider if server is detected and has devices

### LightingView.xaml
- Added OpenRGB Devices section with device list and quick color buttons
- Shows device name, type, vendor, and LED count for each device

### ModernStyles.xaml
- Added `SpinnerRotateStoryboard` for loading spinner animation
- Added `SuccessPopStoryboard` for success checkmark animation
- Added `LoadingSpinner` style for compact loading indicators
- Added Status Badge styles (Success, Warning, Error, Info)
- Added `HoverGlow` style for interactive element hover effects

### App.xaml.cs
- Refactored `IsOmenDesktop()` method for proper HP-only desktop detection

### AutoUpdateService.cs
- Removed `/CLOSEAPPLICATIONS /RESTARTAPPLICATIONS` flags that caused system slowdown
- Added explicit HardwareWorker process termination before launching installer
- Changed to `/SILENT /NORESTART` for cleaner update flow
- Added logging for process termination steps

### OmenCoreInstaller.iss
- Fixed PawnIO installer parameter: `/SILENT` → `-silent`
- Added `PrepareToInstall()` function to kill OmenCore processes before file copy
- Added 500ms delay after process termination to ensure clean file handles

### README.md & INSTALL.md
- Created new [INSTALL.md](../INSTALL.md) with comprehensive installation guide
- Updated README.md to link to INSTALL.md for cleaner layout
- Added quick Linux installation instructions for CachyOS/Arch/Fedora users
- Updated version references from 2.1.2 to 2.7.1

---

## 📋 Upgrade Notes

This release includes significant GPU tuning improvements:

1. **RTX 40 Series Users**: The NvAPIWrapper integration should provide more reliable GPU overclocking
2. **AMD GPU Users**: GPU detection and branding now works, but overclocking requires AMD Adrenalin Software (ADLX integration coming in future release)
3. **Desktop Users**: Non-HP desktops will no longer be blocked from launching
4. **Linux Users**: See [INSTALL.md](../INSTALL.md) for updated installation instructions

If you were affected by the immediate shutdown issue on a non-HP desktop PC, this update should resolve it.
