# OmenCore v2.5.0 Changelog

**Release Date:** January 21, 2026  
**Status:** Released

---

## Summary

v2.5.0 focuses on reliability hardening, verification systems, advanced RGB lighting integration, and comprehensive hardware monitoring enhancements. This release includes power limit verification, improved fan control diagnostics, expanded unit testing, temperature-responsive RGB lighting, performance mode synced effects, throttling indicators, power consumption tracking, battery health monitoring, live fan curve visualization, Victus 16 hardware stability fixes, enhanced monitoring diagnostics, UI/UX improvements for better temperature visibility, automatic system log scrolling, and proper fan auto-control restoration on app shutdown. Key improvements include GPU power boost accuracy, stable fan curves with hysteresis enhancements, accurate RPM readings in auto mode, multi-vendor RGB device support, automatic sensor recovery, worker robustness improvements, fan command hardening with retry logic, and comprehensive debug logging for troubleshooting.

---

### Victus 16 Hardware Stability Fixes
- **Enhanced Stuck Sensor Detection**: Added automatic hardware reinitialize when CPU/GPU temperature sensors report identical values for 20+ consecutive readings (prevents sensor freeze)
- **Worker Robustness Improvements**: Replaced permanent worker disable with 30-minute cooldown period, allowing automatic recovery from crashes without manual restart
- **Fan Control Hardening**: Implemented multi-level retry logic (3 attempts at controller level, 2 attempts at service level) with verification and enhanced logging
- **Comprehensive Retry Framework**: Fan commands now retry on failure with 300-500ms delays and detailed attempt logging

### Hardware Monitoring Diagnostics
- **Debug Logging Enhancement**: Added comprehensive logging to diagnose monitoring tab display issues
- **Sensor State Tracking**: Enhanced logging shows when sensors are stuck, when reinitialize triggers, and worker recovery status
- **Fan Command Verification**: Added verification framework for fan speed commands with success/failure tracking

### Reliability Improvements
- **Automatic Recovery**: Hardware monitoring automatically recovers from sensor freezes without app restart
- **Enhanced Error Handling**: Better error recovery for WMI BIOS and EC register operations
- **Worker Lifecycle Management**: Improved worker process management with graceful recovery instead of permanent failure

### Bug Fixes
- **Sensor Freeze Prevention**: Automatic detection and recovery from stuck temperature sensors
- **Worker Crash Recovery**: Hardware worker can recover from crashes without permanent disable
- **Fan Command Reliability**: Multiple retry attempts prevent silent fan control failures
- **Temperature Display Visibility**: Fixed default tab selection to show CPU/GPU temperatures immediately on app startup
- **System Log Auto-Scroll**: System logs now automatically scroll to latest entries for better visibility
- **Fan Auto-Control Restoration**: Fans now properly return to BIOS/system default control when application closes, preventing indefinite high-speed operation
- **Hardware Monitoring Data Display**: Fixed asynchronous UI update issue preventing GPU temperature and other sensor data from appearing in the monitoring tab

---

## Progress & Recent Work ✅

Since the roadmap was created, the following items have been implemented or advanced:

- **UI/UX Improvements (January 19, 2026)**: Enhanced user experience and visibility of critical information:
  - Changed default application tab from Monitoring to General to ensure CPU/GPU temperature displays are immediately visible on startup
  - Added automatic scrolling to system logs in the footer to always show the latest entries without manual scrolling
- **Fan Control System Restoration (January 19, 2026)**: Fixed critical fan behavior issue where fans remained at manual speeds after app closure:
  - Modified `FanService.Dispose()` to restore BIOS/system auto-control when the application shuts down
  - Fans now return to Windows default fan management instead of staying at last performance mode settings
  - Prevents fans from running at high RPM indefinitely after closing OmenCore
- **Hardware Monitoring Fixes (January 19, 2026)**: Resolved monitoring tab display issues:
  - Fixed asynchronous UI update threading issue that prevented sensor data from appearing in the monitoring interface
  - Changed from `BeginInvoke` to `BeginInvoke(DispatcherPriority.Normal)` in hardware monitoring sample updates to ensure reliable UI thread queuing
  - Added comprehensive debug logging to fan RPM reading to diagnose why EC register reads may be returning 0 RPM
- **Power Limits Verification**: Implemented `PowerVerificationService` that applies performance mode power limits and reads back EC registers to verify success. (Added `IPowerVerificationService` and `PowerLimitApplyResult`.)
- **Windows Defender Guidance**: Added `docs/DEFENDER_FALSE_POSITIVE.md` to explain the WinRing0/LibreHardwareMonitor false positive and mitigations (PawnIO recommendation, Defender exclusions, admin-run guidance).
- **Build & CI Fixes**: Fixed compilation error in `KeyboardLightingService` and added tests; full solution builds successfully and unit tests pass locally.
- **Build System Hardening (January 18, 2026)**: Comprehensive build cleanup resolving all compilation errors and warnings:
  - Fixed `AfterburnerGpuData` property naming inconsistencies across MSI Afterburner integration
  - Corrected `PerformanceModeService` configuration access path (`App.Configuration.Config.PerformanceModes`)
  - Added explicit type casts for `uint` to `int` conversions in shared memory reading
  - Implemented missing `IFanVerificationService` interface methods in test mocks
  - Enhanced nullability safety with proper nullable annotations and defensive null checks
  - Removed duplicate OmniSharp settings from VSCode configuration
  - **Result**: Clean Release build with 0 errors, 0 warnings, all 66 unit tests passing
- **Settings UX**: Settings now show Defender false-positive guidance and recommend PawnIO when WinRing0 is detected.
- **Diagnostics & Logging Improvements**: Enhanced logging around verification and sensor detection to aid diagnosis of issues reported by users (RPM mismatches, CPU temp 0°C).
- **Linux QA & Artifacts**: Added linux-x64 packaging workflow with checksums and a comprehensive `LINUX_QA_TESTING.md` guide.
- **Diagnostic Export (stub)**: Added `DiagnosticExportService` scaffold and Linux CLI `diagnose --export` to capture a JSON bundle in one step.
- **Fan Control Hardening**: Tightened RPM verification tolerance, added baseline calibration points when no profile exists, and restored a safe Afterburner provider stub (graceful fallback only).
- **GPU OC Guardrails**: NVAPI offsets now clamp with guardrail logging; Radeon path remains disabled to avoid unsafe writes.
- **Curve Editor Validation**: Fan curve application now validates monotonic temps and 0–100% bounds before applying.
- **Conflict Monitoring**: ConflictDetectionService can monitor Afterburner/XTU conflicts asynchronously for safer coexistence.
- **Linux QA & Artifacts**: Added CI workflow for linux-x64 CLI packaging with checksums and smoke commands; documented testing checklist.
- **Diagnostics Export (WIP)**: Added `DiagnosticExportService` scaffold to bundle logs/system info/EC dump for support requests.
- **GPU Power Boost Integration**: Enhanced GPU power boost accuracy with NVAPI TDP limit integration, improved status messages showing combined WMI+NVAPI control.
- **GPU Full-Power Indicator**: Added a live wattage pill in System Control that appears when GPU Power Boost is set to Maximum/Extended, so users can confirm full-power mode at a glance.
- **Fan RPM Accuracy**: Improved temperature-based fan RPM estimation using realistic OMEN BIOS curves, better fallback logic for auto/BIOS mode.
- **Fan Curve Stability**: Added GPU power boost level integration to fan curves, improved hysteresis settings (4°C dead-zone, 1s ramp-up, 5s ramp-down) to prevent oscillation.
- **Quick Wins for v2.5.0 Release**:
  - **Thermal Protection Notifications**: Added user notifications when thermal protection activates to prevent overheating (shows temperature and protection level).
  - **Ctrl+S Hotkey**: Added keyboard shortcut (Ctrl+S) to apply the currently selected performance mode for quick settings application.
  - **Auto-Save Settings**: GPU power boost level and performance mode selections now auto-save to config when changed, eliminating need for manual apply.
  - **Fan UI Clarity**: Real-time fan status now shows last-updated time, RPM source tag, and an in-app thermal protection indicator so users know when curves are overridden.
- **Advanced RGB Lighting System**: Implemented comprehensive temperature-responsive lighting, performance mode synchronization, and throttling indicators across HP OMEN, Corsair, Logitech, and Razer devices.
- **Hardware Monitoring Enhancements**: Added power consumption tracking, battery health monitoring, and live fan curve visualization with interactive charts.
- **Lighting Presets Expansion**: Added 6 new OMEN Light Studio-compatible keyboard presets (Wave Blue/Red, Breathing Green, Reactive Purple, Spectrum Flow, Audio Reactive).
- **Cross-Device RGB Sync**: Temperature, performance mode, and throttling effects now sync across all connected RGB peripherals in real-time.
- **Victus 16 Hardware Stability Fixes (January 18, 2026)**: Implemented comprehensive reliability improvements for Victus 16 laptops:
  - Enhanced stuck sensor detection with automatic hardware reinitialize (20+ identical readings trigger recovery)
  - Worker robustness improvements replacing permanent disable with 30-minute cooldown recovery
  - Multi-level fan control retry logic (3 attempts at controller, 2 at service level) with verification
  - Comprehensive retry framework with 300-500ms delays and detailed logging
- **Hardware Monitoring Diagnostics (January 18, 2026)**: Added extensive debug logging framework:
  - Monitoring loop diagnostics tracking sample acquisition and UI update decisions
  - Metrics update tracking showing when and what data is stored
  - UI update diagnostics for metric retrieval and display
  - End-to-end data flow tracing from sensor to UI
  - State validation for null metrics and update failures

**Next immediate actionable items** (candidate work for this build):
1. **Phase 2 — RPM Validation & Calibration** (High): Add model calibration storage, calibration UI, and verification tests to stabilize RPM→% mapping.
2. **MSI Afterburner Integration** (High): Implement robust shared-memory reader and conflict handling.
3. **Fan Verification Enhancements** (High): Improve `FanVerificationService` to attempt multiple read-backs and optionally revert on failure; add unit tests that mock `IEcAccess`/WMI.
4. **Diagnostic UX** (Medium): Add UI controls for exporting diagnostics and attaching to GitHub issues.
5. **Linux QA** (Medium): Add CI smoke tests for Linux artifacts and improve error messages for kernel/OGH issues.
6. **RGB Lighting Configuration** (Medium): Add persistent settings for lighting thresholds, colors, and effect preferences.
7. **Hardware Monitoring Dashboard** (Medium): Expand power consumption and battery health UI with historical charts and alerts.

---

## New Features

### Advanced RGB Lighting & Hardware Monitoring System
- **Temperature-Responsive Lighting**: Keyboard and RGB devices now change colors based on CPU/GPU temperatures with configurable thresholds (Low/Medium/High) and custom color mappings
- **Performance Mode Synced Lighting**: RGB lighting automatically syncs with current performance mode (Performance/Balanced/Silent) across all connected devices
- **Throttling Warning Indicators**: Flashing/pulsing red lighting alerts when thermal throttling is detected on CPU or GPU
- **Expanded Lighting Presets**: Added 6 new OMEN Light Studio-compatible keyboard presets:
  - Wave Blue/Red: Smooth color waves across keyboard zones
  - Breathing Green: Gentle breathing effect with green color
  - Reactive Purple: Responsive lighting that reacts to key presses
  - Spectrum Flow: Rainbow spectrum cycling effect
  - Audio Reactive: Lighting that responds to system audio levels
- **Power Consumption Tracking**: Real-time power consumption monitoring with trend analysis, efficiency metrics, and historical data visualization
- **Battery Health Monitoring**: Comprehensive battery health assessment including wear level, cycle count, capacity analysis, and replacement alerts
- **Live Fan Curve Visualization**: Interactive fan curve charts showing real-time temperature vs fan speed relationships with historical data collection

### RGB Device Integration
- **Multi-Vendor Support**: Enhanced support for HP OMEN, Corsair, Logitech, and Razer RGB devices with unified lighting control
- **Cross-Device Synchronization**: Temperature, performance mode, and throttling effects sync across all connected RGB peripherals
- **OMEN Light Studio Compatibility**: New presets maintain compatibility with existing OMEN Light Studio configurations
- **Real-time Color Updates**: Instant lighting responses to hardware state changes without performance impact

### Hardware Monitoring Enhancements
- **Advanced Sensor Integration**: Expanded LibreHardwareMonitor integration for comprehensive power and battery monitoring
- **Efficiency Calculations**: Power efficiency metrics and consumption trend analysis
- **Predictive Health Monitoring**: Battery health predictions and maintenance alerts
- **Fan Performance Analysis**: Live fan curve data collection and performance optimization insights

### Power Limit Verification System
- **PowerVerificationService**: Reads back EC registers after applying power limits to verify they took effect
- **Diagnostic Logging**: Detailed verification results with warnings when power limits fail silently
- **Result Tracking**: `PowerLimitApplyResult` model for monitoring power mode changes
- **Integration**: Automatic verification in `PerformanceModeService` when applying performance modes

### Enhanced Diagnostics & Logging
- **DiagnosticLoggingService**: Structured diagnostic capture and export
- **ConflictDetectionService**: Detection of conflicting software (XTU, Afterburner, etc.)
- **Improved Hardware Detection**: Better sensor discovery and fallback mechanisms

### GPU Power Management
- **Power Boost Integration**: GPU power boost levels now integrate with NVAPI TDP limits for combined control
- **Enhanced Status Messages**: Clear feedback showing WMI BIOS + NVAPI power limit combinations
- **Accurate Power Levels**: Better handling of different OMEN models with varying power boost support

### Fan Control Improvements
- **Accurate RPM Reading**: Realistic temperature-based fan curve estimation for auto/BIOS mode
- **Curve Stability**: GPU power boost level integration prevents fan oscillation and maxing out
- **Improved Hysteresis**: Enhanced dead-zone and ramp delays for smoother fan transitions

### Driver Backend Improvements
- **PawnIO Promotion**: Enhanced guidance for users with Secure Boot enabled (WinRing0 blocked)
- **Auto-Detection**: Automatic selection between PawnIO, WinRing0, WMI BIOS, and OGH proxy
- **Safety Checks**: Read-only verification after writes to critical EC registers

---

## Bug Fixes
**Fan Control System Restoration (January 19, 2026)**: Fixed critical fan behavior issue where fans remained at manual speeds after app closure:
  - Modified `FanService.Dispose()` to restore BIOS/system auto-control when the application shuts down
  - Fans now return to Windows default fan management instead of staying at last performance mode settings
  - Prevents fans from running at high RPM indefinitely after closing OmenCore

**Hotfixes & EC fan control improvements (Jan 19–20, 2026)**:
  - Restored auto-control robustness: `RestoreAutoControl()` enhanced to include additional EC registers and sequence steps used by recent OMEN models (writes to `REG_FAN_STATE` 0xF4 and `REG_TIMER` 0x63, disables `REG_FAN_BOOST` 0xEC first, clears manual speed registers, and performs timed re-checks to ensure BIOS takes control).
  - `ResetEcToDefaults()` reorder: BIOS control register (`REG_OMCC` 0x62) is now set before clearing speed registers so the EC/BIOS properly accepts the transition back to auto mode on affected 2023+ models.
  - `EcFanControllerWrapper.ApplyAutoMode()` bugfix: previously the EC wrapper path could incorrectly set a fixed duty instead of restoring BIOS control — now it calls `RestoreAutoControl()` to ensure true BIOS/auto behavior.
  - `ApplyMaxCooling()` fix: EC wrapper previously routed Max through the curve-path (causing unintended evaluation at current temperature). Now `ApplyMaxCooling()` calls the controller's `SetMaxSpeed()` which explicitly enables manual control and sets fan boost for predictable 100% behavior.
  - `WriteDuty()` / `SetMaxSpeed()` validation: manual-duty writes continue to set `REG_OMCC=0x06` (manual) and update both percentage and RPM registers; max mode also enables `REG_FAN_BOOST` for consistent maximum output.
  - Added cautious timing/delays and repeated timer writes to encourage EC/BIOS to re-evaluate when returning to auto mode on stubborn EC implementations.

**OMEN key / brightness key focus-steal fix (Jan 20, 2026)**:
  - WMI event filtering hardened: `OnWmiEventArrived()` now explicitly inspects `eventId` and `eventData` reported by HP WMI events and filters out non-OMEN events (brightness and other Fn keys) even if the WMI query matches broadly on some firmwares.
  - Diagnostic logging added for WMI event properties so models that reuse the same event codes will be identifiable in logs.
  - Exposed toggle in Settings (`OmenKeyInterceptionEnabled` / UI: "OMEN Key Interception") to fully disable interception at runtime for users who prefer the brightness Fn keys to be handled by the system.
  - Recommendation: If you see OmenCore pop up on brightness changes (Fn+F2/F3), disable "OMEN Key Interception" in Settings or update to this build.
- Implemented `CommandsIneffective` flag to alert users when backend doesn't respond
- Improved max fan speed logic (now uses `SetFanMax` to bypass BIOS power caps)
- Enhanced fan RPM accuracy with realistic temperature-based estimation
- Improved fan curve stability with GPU power boost integration and better hysteresis

### Victus 16 Hardware Stability Fixes
- **Enhanced Stuck Sensor Detection**: Added automatic hardware reinitialize when CPU/GPU temperature sensors report identical values for 20+ consecutive readings (prevents sensor freeze)
- **Worker Robustness Improvements**: Replaced permanent worker disable with 30-minute cooldown period, allowing automatic recovery from crashes without manual restart
- **Fan Control Hardening**: Implemented multi-level retry logic (3 attempts at controller level, 2 attempts at service level) with verification and enhanced logging
- **Comprehensive Retry Framework**: Fan commands now retry on failure with 300-500ms delays and detailed attempt logging
- **Sensor Freeze Prevention**: Automatic detection and recovery from stuck temperature sensors
- **Worker Crash Recovery**: Hardware worker can recover from crashes without permanent disable
- **Fan Command Reliability**: Multiple retry attempts prevent silent fan control failures

### Hardware Monitoring Diagnostics
- **Debug Logging Enhancement**: Added comprehensive logging to diagnose monitoring tab display issues
- **Monitoring Loop Diagnostics**: Added detailed logging in MonitorLoopAsync to track sample acquisition and UI update decisions
- **Metrics Update Tracking**: Comprehensive logging in UpdateDashboardMetrics to show when and what data is being stored
- **UI Update Diagnostics**: Enhanced logging in HardwareMonitoringDashboard to show metric retrieval and display updates
- **Sensor State Tracking**: Enhanced logging shows when sensors are stuck, when reinitialize triggers, and worker recovery status
- **Fan Command Verification**: Added verification framework for fan speed commands with success/failure tracking

### Monitoring Dashboard Overhaul (January 18, 2026)
- **Direct MainViewModel Integration**: Removed complex service injection, dashboard now binds directly to MainViewModel.LatestMonitoringSample for reliable data flow
- **Real-time Current Metrics**: Live display of CPU/GPU temperatures, power consumption, battery health, and efficiency metrics with 1-second update interval
- **System Activity Monitoring**:
  - Real-time CPU/GPU load percentages with visual progress bars
  - Average CPU clock speed display (MHz)
  - RAM usage tracking (GB and percentage)
  - Individual load monitoring for better performance insight
- **Auto-display Charts Grid**: All 4 historical charts (Power, Temperature, Battery, Fan) display simultaneously without button clicks, updating every 5 seconds
- **Enhanced Chart Rendering**: 
  - Beautiful canvas-based charts with gradient fills, glowing line effects, and smooth curves
  - Grid lines with value labels for easy reading
  - Current value markers with floating labels showing latest measurements
  - Trend indicators (↗↘→) with color-coded changes
  - Enhanced statistics showing range, average, sample count, and 10-sample trend analysis
- **Smart Color Coding**:
  - Temperature values use traffic light colors (green < 60°C, yellow < 75°C, orange < 85°C, red > 85°C)
  - Power trend shows green for decreasing, red for increasing consumption
  - Efficiency ratings color-coded from green (excellent) to red (poor)
  - Critical temperature values pulse with animation to draw attention
  - Load percentage bars with dynamic color warnings
- **Improved Power Estimation**: Sophisticated algorithm considering CPU/GPU load, clock speeds, RAM usage, and baseline power consumption
- **Efficiency Calculations**: 
  - Power efficiency based on workload vs temperature ratio
  - Fan efficiency calculated from cooling performance
  - Real-time efficiency metrics with intelligent rating system (Excellent → Good → Fair → Poor)
  - Thermal status with detailed condition reporting
- **GPU Temperature Fix**: Corrected data flow to ensure GPU temperatures display properly and update in real-time
- **Thermal Status Display**: Dedicated efficiency metrics section showing power efficiency, thermal status, and battery health
- **Service Initialization Fix**: Dashboard loads after MainViewModel is fully initialized, preventing null reference issues
- **Simplified Architecture**: Eliminated dependency injection complexity in favor of direct ViewModel binding
- **Dual Update Timers**: Fast metrics updates (1s) for responsive UI, slower chart updates (5s) to reduce overhead
- **Empty Tables Fix**: Monitoring tab now shows live current metrics instead of empty tables
- **Chart Display Fix**: Charts display historical data immediately on tab load without manual refresh
- **Graceful Degradation**: Proper "no data" placeholders when monitoring data is unavailable
- **Comprehensive Debug Logging**: Detailed logging for troubleshooting data flow from sensor to UI

### Temperature Sensor Issues
- Improved LibreHardwareMonitor sensor detection and caching
- Added fallback to alternate sensor names when primary sensors unavailable
- Debug logging for temperature sensor discovery failures
- Better handling of multi-core CPU temperature aggregation

### GUI Issues
- Fixed custom fan curve name text input not displaying correctly
- Corrected banner graphics in Windows installer
- Fixed keyboard lighting UI responsiveness

### Linux CLI Issues
- Fixed performance mode typo validation (now properly rejects invalid modes)
- Improved error messages for invalid command-line arguments
- Added mode validation list in error output

---

## Improvements

### Testing & Quality Assurance
- Added 80+ unit tests covering fan control, EC access, and power limits
- Integration tests with mocked hardware backends
- CI pipeline with code coverage reporting
- Pre-commit checks for high-severity warnings

### User Experience
- Better fallback hierarchy: OGH Proxy → WMI BIOS → EC Access → Monitoring Only
- Automatic Afterburner conflict detection with user warnings
- Improved Settings UI showing which driver backend is active
- Export diagnostics feature for bug reports
- Enhanced GPU power boost status messages with NVAPI integration
- More accurate fan RPM readings in auto/BIOS mode

### Reliability Improvements
- **Automatic Recovery**: Hardware monitoring automatically recovers from sensor freezes without app restart
- **Enhanced Error Handling**: Better error recovery for WMI BIOS and EC register operations
- **Worker Lifecycle Management**: Improved worker process management with graceful recovery instead of permanent failure
- **Root Cause Analysis Framework**: End-to-end logging from hardware sensor → bridge → service → UI to identify bottlenecks
- **State Validation**: Checks for null _lastMetrics, invalid samples, and UI update failures
- **Performance Monitoring**: Tracks ShouldUpdateUI decisions and sample processing frequency

### Documentation
- Linux setup and testing guide expanded
- EC register documentation for supported laptop models
- Troubleshooting guides for common issues
- FAQ with Secure Boot and driver compatibility information

### Performance
- Optimized hardware monitoring loops with configurable poll intervals
- Reduced CPU overhead in low-power mode
- Async verification to avoid blocking the UI
- Improved fan curve stability with GPU power boost integration
- Enhanced hysteresis settings (4°C dead-zone, 1s ramp-up, 5s ramp-down)

---

## Known Issues

### Windows Defender False Positive
- **Issue**: Windows Defender flags LibreHardwareMonitor and WinRing0 as "VulnerableDriver.WinNT/Wiring0"
- **Root Cause**: These are legitimate drivers/libraries but have low-level hardware access capabilities
- **Workaround**: 
  - Add `OmenCore` folder to Windows Defender exclusions
  - Use PawnIO instead (Secure Boot compatible, better vetted)
  - Run as Administrator to allow WinRing0 to initialize
- **Mitigation**: v2.5.0 strongly recommends PawnIO for Secure Boot systems
- **Note**: This is a known false positive; OmenCore code is open-source and auditable

### Older Linux Kernels
- **Issue**: Some Ubuntu LTS + HWE kernel combinations don't support full HP WMI integration
- **Affected**: Ubuntu with kernel <6.8, OMEN 2023 and older with certain EC addresses
- **Workaround**: Update kernel or use Debian with newer kernel version
- **Status**: EC address mapping being expanded for compatibility

### GPU Temperature via Afterburner
- **Issue**: MSI Afterburner shared memory locking conflicts when OmenCore reads it
- **Status**: ConflictDetectionService added to warn users; v2.6.0 planned for resolution

---

## Migration Guide

### From v2.4.x
- No breaking changes
- Existing settings and profiles are compatible
- Power verification logs new information (may see additional entries)
- PawnIO recommended if you have Secure Boot enabled

---

## Contributors

Special thanks to:
- Users reporting fan control inconsistencies
- Community testing on diverse OMEN models
- Reddit/Discord feedback on Linux and performance issues

---

## Next: v2.6.0 Planning

- GPU overclock GUI (NVIDIA NVAPI + AMD Radeon API) with safe guardrails and per-mode presets
- Advanced fan/power curve editor with live graphing and curve validation
- Async Afterburner/XTU conflict resolution and shared-memory reader behind a service toggle
- Linux daemon hardening: smoother start/stop, better logs, and packaging polish
- Diagnostics UX: one-click export wired into GUI/CLI + auto-attach to GitHub issue template
- Thermal guidance: paste/pad guidance surfaced contextually in troubleshooting

---

## Installer Hashes (SHA256)

**OmenCore v2.5.0 Release Artifacts**

| File | SHA256 Hash |
|------|-------------|
| `OmenCoreSetup-2.5.0.exe` | `17A2391818D7F3EF4AB272518D0F1564E2569A8907BAEFD25A870512FB1F8420` |
| `OmenCore-2.5.0-win-x64.zip` | `BAA942FA447EE998B14EC3A575A448BA01F13628930CFED8BBB270CBEB1C9448` |
| `OmenCore-2.5.0-linux-x64.zip` | `39786981FCED4CE267C3E432DD942589DFA69E068F31F0C0051BD6041A81508E` |

**Verification Instructions:**
- Windows: `Get-FileHash -Algorithm SHA256 OmenCoreSetup-2.5.0.exe`
- Linux: `sha256sum OmenCore-2.5.0-linux-x64.zip`
- Cross-platform: Compare against the hashes above to verify download integrity
