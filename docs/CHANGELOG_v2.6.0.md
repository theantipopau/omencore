# OmenCore v2.6.0 - Fan Control & Stability Release

**Release Date:** February 2026

This release focuses on fixing critical fan control, temperature monitoring, and stability issues. Major improvements include Legacy WMI fallback for BIOS F.15+, fixed dual fan curve UI bug, robust RPM parsing for V2 systems, and EC conflict detection for coexistence with OmenMon.

---

## 🔧 Bug Fixes

### WMI BIOS Compatibility (BIOS F.15+)
- **Added Legacy WMI fallback for newer BIOS versions**
  - CIM-based commands now gracefully fall back to System.Management API
  - Fixes "Invalid method Parameter(s)" error on BIOS F.15+
  - GPU Power Boost now works on systems with latest BIOS updates
  - Automatic detection and switching to legacy mode when needed

### Fan Curve UI
- **Fixed dual curve editor rendering bug**
  - Single/dual curve editors were both rendering on top of each other
  - Added missing `IndependentCurvesEnabled` property to ViewModel
  - Added GPU curve editor commands and properties
  - Toggle now properly switches between single unified curve and dual CPU/GPU curves

### Fan Control Issues
- **Fixed RPM parsing for V2 systems (OMEN MAX 2025+)**
  - Added `GetFanRpmDirect()` with automatic endianness detection
  - Handles both little-endian and big-endian byte orders
  - Added `IsValidRpm()` sanity validation (0-8000 RPM range)
  - Fixes absurd readings like 20297 RPM or 78 RPM

- **Fixed verification circular dependency**
  - `VerifyMaxAppliedWithRetries()` now uses raw WMI BIOS reads
  - No longer uses estimated values that caused false verification failures
  - Added direct RPM command support (0x38) for V2 systems

- **Fixed fan speed reading with sanity validation**
  - `ReadFanSpeeds()` now validates RPM values before returning
  - Filters out implausible readings automatically

- **Fan backend stability** (from earlier v2.6.0 work)
  - Hardened fan backend selection and fallbacks to reduce incorrect 0 RPM reports on affected OMEN Max 16 models
  - Removed unsafe cross-references between the OGH wrapper and EC access code
  - Key file: `src/OmenCoreApp/Hardware/FanControllerFactory.cs`

- **Fan diagnostics UI** (from earlier v2.6.0 work)
  - Fixed the Fan Diagnostics history bindings so diagnostic results render correctly in the UI
  - Key file: `src/OmenCoreApp/Views/FanDiagnosticsView.xaml`

### Temperature Monitoring Issues
- **Added WMI BIOS temperature fallback**
  - New `GetGpuTemperature()` and `GetBothTemperatures()` methods in HpWmiBios
  - Falls back to WMI BIOS when LibreHardwareMonitor fails
  - GPU temperature now available even without LibreHardwareMonitor

- **Fixed temperature freezing**
  - Enhanced stuck-temperature detection now tries WMI fallback before full reinitialize
  - Reduces unnecessary hardware reinitializations
  - More responsive temperature updates

### EC Access & Stability
- **Added EC conflict detection with OmenMon**
  - `ReadByte()` and `WriteByte()` now have retry logic (3 attempts)
  - Raises `EcConflictDetected` event when mutex contention detected
  - More graceful degradation when OmenMon is running simultaneously
  - Increased mutex timeout on retries (200ms → 500ms)

- **Fixed Ctrl+S global hotkey conflict (Issue #53)**
  - Removed `Ctrl+S` global hotkey that conflicted with Photoshop/VSCode save
  - Replaced with `Ctrl+Shift+Alt+A` for apply fan settings

---

## 🚀 Performance Improvements

### Faster Startup
- Reduced `WorkerStartupDelayMs` from 1500ms to 500ms
- Hardware worker now starts 1 second faster
- Connection retries still available if needed

---

## ✨ New Features

### RAM "0/0 GB" Display Fix
- **Fixed RAM monitoring returning 0 values**
  - Added `GetRamFromWmi()` fallback in HardwareWorker when LibreHardwareMonitor has no RAM sensors
  - Added immediate WMI query fallback in `LibreHardwareMonitorImpl` when worker returns 0/0 GB
  - Queries `Win32_ComputerSystem.TotalPhysicalMemory` and `Win32_OperatingSystem` for reliable RAM info
  - Users experiencing "0/0 GB" display should now see correct RAM values

### Constant Fan Speed Mode (OmenMon-Inspired)
- **Added manual constant-speed fan control**
  - New `FanMode.Constant` enum value for fixed-percentage fan operation
  - Users can set a specific fan speed percentage (0-100%)
  - Estimated RPM display based on percentage
  - `ApplyConstantSpeedCommand` in FanControlViewModel for one-click application
  - Complements existing Auto, Performance, Balanced, and Curve modes

### Temperature-Based RGB Lighting (OmenMon-Inspired)
- **New `TemperatureRgbService` for dynamic keyboard lighting**
  - Keyboard color changes based on CPU/GPU temperature in real-time
  - Color gradient: Blue (cool, <50°C) → Yellow (warm, 70°C) → Red (hot, >90°C)
  - 2-second polling interval for responsive updates
  - Uses WMI BIOS `SetColorTable` for direct 4-zone keyboard control
  - `TemperatureBasedLightingEnabled` toggle in RGB settings
  - Color interpolation for smooth temperature-to-color transitions

### Power Limit Control Enhancement
- **Added PL1/PL2 status and lock detection**
  - `IsPowerLimitLocked()` - Check if BIOS has locked power limits
  - `GetPowerLimitStatus()` - Get detailed PL1, PL2, enable states, and lock status
  - `SetPowerLimits(pl1, pl2)` - Set both power limits atomically with verification

- **MSR 0x610 full support**
  - Proper bit-field handling for PL1 (bits 14:0), PL2 (bits 46:32)
  - Enable bit handling (bits 15, 47)
  - Lock bit detection (bit 63)

### Self-Sufficient Architecture
- **New WmiBiosMonitor class**
  - OmenCore now works without external dependencies
  - Uses HP WMI BIOS for CPU/GPU temperature and fan RPM
  - No kernel driver required for basic monitoring
  - LibreHardwareMonitor is now optional (for enhanced metrics like GPU clocks, VRAM)

- **Improved hardware abstraction**
  - `ThermalSensorProvider` now supports `IHardwareMonitorBridge` interface
  - `WmiFanController` works without LibreHardwareMonitor
  - Automatic fallback chain: WMI BIOS → LibreHardwareMonitor (if available)

- **PawnIO now installed by default**
  - Installer checkbox defaults to checked (was previously unchecked)
  - Enables EC direct access and MSR/undervolt features
  - WinRing0 kept as fallback (Windows Defender may flag it)

---

## 📝 Technical Details

### Files Modified

#### Hardware Layer
- `HpWmiBios.cs` - Added Legacy WMI fallback (SendBiosCommandLegacy), GetFanRpmDirect, GetGpuTemperature, GetBothTemperatures, IsValidRpm (now public), IsConnected alias
- `WmiFanController.cs` - Fixed VerifyMaxAppliedWithRetries, enhanced ReadFanSpeeds, made LibreHardwareMonitor optional
- `WmiBiosMonitor.cs` - **NEW** - Self-sufficient hardware monitor using WMI BIOS only
- `ThermalSensorProvider.cs` - Added IHardwareMonitorBridge constructor with WMI BIOS fallback
- `FanControllerFactory.cs` - Refined wrapper/fallback logic, added SensorHelper class, RPM validation
- `PawnIOEcAccess.cs` - Added EC conflict retry logic with EcConflictDetected event
- `PawnIOMsrAccess.cs` - Added power limit status and SetPowerLimits
- `WinRing0MsrAccess.cs` - Added power limit status and SetPowerLimits
- `IMsrAccess.cs` - Added IsPowerLimitLocked, GetPowerLimitStatus, SetPowerLimits

#### Monitoring Layer
- `LibreHardwareMonitorImpl.cs` - Enhanced WMI BIOS fallback with GPU temperature, added RAM 0/0 GB fallback using WMI
- `HardwareWorkerClient.cs` - Reduced WorkerStartupDelayMs to 500ms
- `OmenCore.HardwareWorker/Program.cs` - Added GetRamFromWmi() helper for RAM fallback

#### ViewModels
- `MainViewModel.cs` - Updated initialization to use WmiBiosMonitor first, exposed HardwareMonitoringService property
- `FanControlViewModel.cs` - Added Constant fan mode, IndependentCurvesEnabled, GpuFanCurve, GPU curve commands, thermal protection status properties

#### Services
- `HotkeyService.cs` - Removed Ctrl+S, added Ctrl+Shift+Alt+A
- `TemperatureRgbService.cs` - **NEW** - Temperature-based keyboard RGB lighting
- `RgbLightingSettingsService.cs` - Added TemperatureBasedLightingEnabled property

#### Models
- `FanMode.cs` - Added Constant enum value for fixed-percentage fan mode

#### Installer
- `OmenCoreInstaller.iss` - PawnIO now checked by default

#### Views
- `FanDiagnosticsView.xaml` - Fixed history bindings

### Known Issues
- V2 systems may require firmware updates for full WMI BIOS support
- Power limit unlocking only works if BIOS hasn't set the lock bit
- EC conflict detection adds ~100ms latency per retry when OmenMon is active
- Keyboard RGB / lighting backends: not fully fixed in this release

---

## � Downloads & SHA256 Checksums

### Windows
| File | SHA256 |
|------|--------|
| `OmenCoreSetup-2.6.0.exe` | `BB5B82264BEAFE67FC9B38F1F1AD2F987F40DF755B5AEEFACD830DAD02D2AF84` |
| `OmenCore-2.6.0-win-x64.zip` | `391A3F2EA1F0099463212A73EE6862269BD84D84970B4351B475CEC698C3453A` |

### Linux
| File | SHA256 |
|------|--------|
| `OmenCore-2.6.0-linux-x64.zip` | `65FE1681026B04D39AE522F6B26DC553E4F7173B63E4FD567FBBCDBE51004324` |

---

## �🔍 Addressed GitHub Issues
- **Issue #52** - Fan reading showing 0 RPM then absurd values
- **Issue #53** - Ctrl+S global hotkey conflict
- **Issue #54** - EC conflicts with OmenMon
- **Issue #55** - Temperature freezing on values

---

## 🔄 Upgrade Notes

1. **If upgrading from v2.3.2 or earlier:**
   - All settings should migrate automatically
   - Fan presets may need to be re-applied once

2. **If running alongside OmenMon:**
   - OmenCore will now gracefully handle EC conflicts
   - May see brief delays when both apps access EC simultaneously
   - For best results, close one app when actively tuning with the other

3. **Hotkey changes:**
   - `Ctrl+S` no longer applies fan settings
   - Use `Ctrl+Shift+Alt+A` instead

---

## 📊 Testing Checklist

### Fan Control
- [ ] Fan speeds read correctly on V1 systems
- [ ] Fan speeds read correctly on V2 systems (OMEN MAX 2025)
- [ ] Max fan verification works without false failures
- [ ] **NEW: Constant speed mode applies correctly**
- [ ] **NEW: Constant speed percentage slider works (0-100%)**

### Temperature Monitoring
- [ ] Temperature monitoring doesn't freeze
- [ ] GPU temperature shows in fallback mode
- [ ] **NEW: RAM displays correctly (not 0/0 GB)**

### Self-Sufficiency
- [ ] App starts without LibreHardwareMonitor installed
- [ ] WMI BIOS fallback provides temps and fan RPM
- [ ] EC access works when OmenMon is running
- [ ] **NEW: Startup time reduced noticeably (~1 second faster)**

### RGB & Lighting
- [ ] **NEW: Temperature-based RGB toggle works**
- [ ] **NEW: Keyboard color changes with temperature (blue→yellow→red)**

### Power & System
- [ ] Power limit status reads correctly
- [ ] Ctrl+Shift+Alt+A applies fan settings
- [ ] Fan Diagnostics history displays correctly

---

## 🙏 Thanks

Thanks to all users who reported issues and provided detailed logs!
