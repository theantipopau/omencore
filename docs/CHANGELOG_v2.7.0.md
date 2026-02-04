# OmenCore v2.7.0 Changelog

**Release Date:** February 4, 2026  
**Branch:** `main`

---

## 📦 Downloads

| File | SHA256 |
|------|--------|
| `OmenCoreSetup-2.7.0.exe` | `7BB27ADC487DAB186F1CC2EB07C3A350572DE30C06DD7225557C9F0FE1C157A2` |
| `OmenCore-2.7.0-win-x64.zip` | `E3334290C8F197E4577E5199970BFBB7C1F563FB6AC29AEF0877888AF9F35EB9` |
| `OmenCore-2.7.0-linux-x64.zip` | `F6C7BB52920FAF6491DAAA7F943C2A587C8ED3830005AC872B53CAA76DC7BE29` |

---

## 🚀 New Features

### Monitoring & Reliability
- **Monitoring Health Status Indicator**: Dashboard now shows real-time monitoring health (Healthy/Degraded/Stale) with color-coded status and sample age
- **Last Sample Age Display**: Shows how fresh sensor data is (e.g., "Just now", "5s ago")
- **Synthetic Data Removal**: Charts no longer show fake data when no real samples exist - proper empty states instead
- **Worker Auto-Restart/Failover**: Hardware monitoring bridge automatically restarts after 3 consecutive timeouts

### WMI & BIOS Improvements
- **WMI Heartbeat Health Tracking**: New health status for WMI heartbeat (Healthy/Degraded/Failing)
- **Heartbeat Failure Tracking**: Logs and surfaces consecutive heartbeat failures
- **HeartbeatHealthChanged Event**: Allows UI to react to WMI health changes

### RDP / Remote Session Support
- **RDP Popup Suppression**: OmenCore no longer steals focus during Remote Desktop sessions
- **Hotkey Suppression in RDP**: Global hotkeys disabled during remote sessions
- **OMEN Key Suppression in RDP**: Physical OMEN key ignored during remote sessions
- **New Setting**: `SuppressHotkeysInRdp` (default: enabled)

### Dashboard Enhancements
- **Enhanced Status Header**: Added monitoring health, sample age, and status indicators
- **HasHistoricalData/HasLiveData Properties**: For proper empty state UI handling

### Fan Diagnostics
- **Guided Fan Diagnostic Script**: One-click test sequence (30% → 60% → 100%) for both CPU and GPU fans with pass/fail summary
- **Fan Curve Live Preview**: Shows predicted fan speed at current temperature in real-time
- **Curve Validation Messages**: Warnings for dangerous curves (fan drops at high temps, missing high-temp points)

### Standalone Operation
- **Dependency Audit System**: Startup validation checks for OGH, HP services, WMI BIOS, LHM, PawnIO
- **Standalone Status Display**: Settings shows "Standalone/Degraded/Limited" status with summary
- **No HP Software Required**: Clear visibility into what dependencies are detected vs required
- **PawnIO-Only Mode Toggle**: Settings option to force PawnIO-only backend (disables HP service-dependent features)

### Fan & Hardware Verification
- **Fan RPM Verification Scoring**: 0-100 score for fan control accuracy (accuracy + stability + response)
- **Fan Calibration Scoring**: Overall score for calibration runs based on all test points
- **Verification Score Display**: Guided diagnostic now shows scores with rating (Excellent/Good/Fair/Poor/Failed)

### Per-Model Capability Detection
- **Model-Specific Capability Database**: Configurations for 20+ OMEN/Victus models
- **Runtime Capability Probing**: Hardware verification of capabilities at startup
- **UI Visibility Helpers**: `ShowFanCurveEditor`, `ShowMuxSwitch`, `ShowRgbLighting`, `ShowUndervolt`, `ShowPerformanceModes`
- **Model Warnings**: Display warnings for known model limitations

### BIOS Reliability Tracking
- **Command Success/Failure Tracking**: Total attempts, successes, failures tracked
- **Success Rate Calculation**: Percentage success rate with health rating
- **BIOS Reliability UI**: Settings shows BIOS WMI reliability with refresh button
- **Legacy WMI Indicator**: Shows when using legacy WMI fallback

### System Tray Enhancements
- **GPU Power Quick Access**: Tray menu for Minimum/Medium/Maximum GPU power levels
- **Keyboard Backlight Quick Access**: Tray menu for Off/Low/Medium/High/Toggle brightness
- **State Sync**: GPU power and keyboard state synced between tray and main app

### Unified RGB Layer (#18)
- **RgbSceneService**: Centralized RGB scene management with presets, scheduling, and performance mode triggers
- **ScreenSamplingService**: Real-time screen edge color sampling for ambient lighting
- **8 Built-in Scenes**: OMEN Red, Gaming, Night Mode, Work, Rainbow, Cool Blue, Ambient, Lights Off
- **Scene Quick Select UI**: Grid of clickable scene buttons in Lighting tab
- **Ambient Mode Toggle**: Enable screen-reactive lighting that syncs RGB to screen colors
- **Performance Mode Triggers**: Auto-switch scenes when performance mode changes (e.g., Gaming scene on Performance mode)
- **Time-based Scheduling**: Schedule scenes for specific times (e.g., Night Mode at 10 PM)
- **Per-Zone Colors**: Scenes can define different colors for each keyboard zone

### Visual Polish - Gauges & Sparklines (#17)
- **Sparkline Control**: Lightweight inline mini-chart for showing recent trends
- **CircularGauge Control**: Semi-circular gauge for percentage values
- **Dashboard Sparklines**: CPU, GPU, and RAM cards now show live temperature/usage sparklines
- **Real-time Updates**: Sparklines update with last 20 samples for smooth trend visualization
- **Color-coded**: Each component has its own accent color (CPU=Red, GPU=Blue, RAM=Purple)

### HP Driver & Support Guidance Panel (#26)
- **Quick Links Panel**: New card in Settings→Hardware with HP driver and support resources
- **HP Support Assistant**: Direct link to HP Support page for automatic driver detection
- **Driver Downloads**: Quick access to HP's official driver download page
- **OMEN Gaming Hub**: Link to official OMEN Hub in Microsoft Store for comparison/compatibility
- **User-friendly Tips**: Helpful guidance on using HP Support Assistant for best results

### Platform-Aware Update Assets (#25)
- **Installation Type Detection**: Auto-detect whether running from installer or portable mode
- **Registry Check**: Looks for uninstall registry entries to identify installed versions
- **Directory Check**: Checks for Program Files location and uninstaller presence
- **Smart Asset Selection**: Downloads installer for installed versions, portable ZIP for portable versions
- **Seamless Updates**: Users always get the appropriate update format for their installation type

### Linux Low-Overhead Mode (#22)
- **Battery-Aware Polling**: Automatically reduces polling frequency when on battery power
- **Configurable Intervals**: Normal mode (2000ms) vs low-overhead (5000ms) configurable in config.toml
- **Reduced Logging**: Optional reduced log verbosity in low-overhead mode to save disk I/O
- **Sensor Caching**: Uses cached sensor paths instead of rescanning in low-overhead mode

### Linux Tray Integration (#23)
- **System Tray Script**: New `omencore-tray.sh` helper script for DE integration
- **YAD Support**: GTK-based tray using YAD (Yet Another Dialog)
- **Pystray Support**: Cross-desktop tray using Python pystray library
- **Quick Actions Menu**: Fan profiles, performance modes, config folder, daemon status from tray

### Linux Sensor Robustness (#24)
- **Multiple Sensor Sources**: Reads from hwmon + thermal_zone for more coverage
- **Fallback Chain**: CPU sensors try coretemp → k10temp → zenpower → acpitz → thermal zones
- **Sensor Health Tracking**: Tracks failures per sensor, skips after 5 consecutive failures
- **Auto-Rescan**: Rescans sensors every 5 minutes to pick up newly loaded drivers
- **GPU Sensor Support**: NVIDIA, Nouveau, and AMD GPU sensor detection

### Desktop RGB HID Support (SignalRGB/OpenRGB Protocol)
- **Direct HID Communication**: Uses HidSharp for USB HID access to HP OMEN desktop RGB controllers
- **OpenRGB-Compatible Protocol**: 58-byte HID packet structure matching OpenRGB HPOmen30LController
- **7 RGB Zones**: OMEN Logo, Light Bar, Front Fan, CPU Cooler, Bottom Fan, Middle Fan, Top Fan
- **Device Auto-Detection**: Scans for HP OMEN RGB controllers (VID: 0x103C, PIDs: 0x84FD, 0x84FE, 0x8602, 0x8603)
- **Brightness & Effects**: Supports brightness (25/50/75/100%), modes (static/breathing/cycle/wave/blinking/radial), themes, and speed
- **WMI Fallback**: Falls back to WMI BIOS method when HID access fails

### Desktop RGB Advanced Features (OpenRGB Research)
- **Direct Mode**: Per-zone RGBA control with individual brightness for SignalRGB integration (`SetZoneDirectModeAsync`)
- **Suspend State Colors**: Set different RGB colors for powered-on vs suspended states (`SetSuspendColorsAsync`)
- **Per-Zone Brightness**: Independent brightness control for each zone (`SetZoneBrightnessAsync`)
- **Predefined Themes**: Galaxy, Volcano, Jungle, Ocean, Unicorn from OMEN Command Center (`ApplyThemeAsync`)
- **Effect Methods**: Breathing (`SetBreathingEffectAsync`), Color Cycle (`SetColorCycleEffectAsync`), Wave (`SetWaveEffectAsync`)
- **Zone Power Control**: Turn off individual zones or all zones (`TurnOffZoneAsync`, `TurnOffAllAsync`)
- **Desktop Model Support**: OMEN 45L, 40L, 35L, 30L, 25L, and Obelisk series

### Performance Throttling Mitigation (omen-fan Research)
- **EC Register 0x95 Support**: Added throttling mitigation register discovered from omen-fan Linux utility
- **ApplyThrottlingMitigation() API**: New method in IFanController interface for all controller types
- **EC Direct Write**: EcFanControllerWrapper writes 0x31 to register 0x95 for performance mode
- **WMI Fallback**: WmiFanController uses WMI SetFanMode to Performance as fallback
- **OGH Support**: OghFanControllerWrapper sets Performance thermal policy

### Slope-Based Fan Curve Interpolation (omen-fan Algorithm)
- **Linear Interpolation**: Smooth fan speed transitions between curve points instead of step changes
- **omen-fan Algorithm**: `slope = (speed[i] - speed[i-1]) / (temp[i] - temp[i-1])`
- **Applied Speed**: `speed = speed[i-1] + slope * (current_temp - temp[i-1])`
- **Unified & Independent Modes**: Works with both single curve and separate CPU/GPU curves
- **Safety Clamping**: Results clamped to 0-100% with existing thermal protection layers

---

## 🐛 Bug Fixes

### Version Display Fix
- **Settings → About Shows Correct Version**: Fixed version displaying as 2.6.1 instead of 2.7.0 - updated `AssemblyVersion` and `FileVersion` in OmenCoreApp.csproj

### Sidebar Temperature Display Fix
- **Live Temps Now Working**: Fixed sidebar CPU/GPU temperature indicators showing "--" instead of actual values
- **New Properties**: Added `CpuTemperature` and `GpuTemperature` properties to `DashboardViewModel` that expose `LatestMonitoringSample` temperatures
- **Proper Bindings**: Sidebar now correctly binds to `Dashboard.CpuTemperature` and `Dashboard.GpuTemperature`

### Quick Actions Visual Feedback
- **Disabled State Styling**: Quick Action buttons (Apply Fan Preset, Performance Mode, Apply Lighting) now show greyed out appearance when unavailable
- **40% Opacity on Disabled**: Buttons fade to 40% opacity when their commands cannot execute
- **Icon Background Changes**: Colored icon badges change to neutral `SurfaceMediumBrush` when disabled
- **Better UX**: Users can now visually distinguish available vs unavailable actions

### Temperature Freeze Detection & Recovery (Critical Fix)
- **Consecutive Same-Temp Detection**: Tracks when CPU/GPU temperatures remain exactly the same for 30+ readings
- **WMI BIOS Fallback**: When freeze detected, automatically falls back to HP WMI BIOS temperature readings
- **Auto Bridge Restart**: If both CPU and GPU temps freeze for extended period, automatically restarts LibreHardwareMonitor
- **Recovery Logging**: Clear log messages when freeze detected (`🥶`) and when temps recover (`✅`)
- **Intelligent Detection**: Uses 0.1°C epsilon to differentiate frozen sensors from genuinely stable temps

### OSD / Overlay Issues
- [x] **FPS Display Changed to GPU Activity**: OSD now shows GPU activity % instead of fake FPS estimate (no game hooks available)
- [x] **Performance Mode Not Updating in OSD**: Fixed - overlay now updates when performance mode changes
- [x] **Frozen GPU/CPU Values**: Added staleness detection (5s threshold) with fallback to ThermalProvider for live data

### Fan Control Issues (OMEN 16 / OMEN Max)
- [x] **More Aggressive Fan Retention**: Reduced countdown extension from 15s → 8s to combat BIOS reversion
- [x] **Faster Curve Updates**: Reduced curve update interval from 10s → 5s for more responsive fan control
- [x] **More Frequent Force Refresh**: Reduced force refresh from 60s → 30s to maintain fan settings

> **Note:** The following issues are timing-related and should be improved by the above changes. User testing required to confirm fixes:
> - Max Profile Drops (fan ramps then drops)
> - Extreme Profile No Effect
> - Gaming Profile Stuck at Max
> - Auto Profile RPM Zero
> - Silent Profile Glitches

---

## 🔧 Technical Changes

### WinRing0 Removal (Antivirus False Positive Fix)
- **PawnIO-Only MSR Backend**: Removed WinRing0 fallback from `MsrAccessFactory` - now exclusively uses PawnIO for MSR access
- **No More AV False Positives**: WinRing0 device access attempts were triggering Windows Defender heuristics
- **Marked Obsolete**: `MsrBackend.WinRing0` enum value marked with `[Obsolete("WinRing0 support removed in v2.7.0")]`
- **Secure Boot Compatible**: PawnIO is signed and works with Secure Boot enabled
- **Clean Install**: Users no longer need to whitelist WinRing0 in Windows Defender

### Services Modified
- `HardwareMonitoringService.cs`: Added `MonitoringHealthStatus` enum, health tracking, removed synthetic data generation, added auto-restart after 3 consecutive timeouts, **added temperature freeze detection with WMI BIOS fallback**, consecutive same-temp tracking, `SetWmiBiosService()` method
- `HotkeyService.cs`: Added `ShouldSuppressForRdp()` check in WndProc
- `OmenKeyService.cs`: Added RDP suppression in hook callback and WMI event handler
- `HpWmiBios.cs`: Added `WmiHeartbeatHealth` enum, heartbeat failure tracking, `HeartbeatHealthChanged` event
- `FanService.cs`: Reduced curve update interval (10s → 5s), force refresh (60s → 30s) for more responsive control
- `OsdService.cs`: Performance mode now properly synced via `ModeApplied` event handler
- `SystemInfoService.cs`: Added `PerformDependencyAudit()` method with 6 dependency checks (HP WMI BIOS, OGH, HP System Event, LHM, PawnIO, WinRing0)

### Hardware Modified
- `WmiFanController.cs`: Reduced countdown extension interval (15s → 8s) for more aggressive fan retention; Added `ApplyThrottlingMitigation()` method using WMI SetFanMode to Performance
- `HardwareMonitorBridge.cs`: Added `TryRestartAsync()` method to interface for bridge restart capability
- `FanControllerFactory.cs`: Added `ApplyThrottlingMitigation()` to IFanController interface; Implemented in all wrappers (EcFanControllerWrapper, WmiFanControllerWrapper, OghFanControllerWrapper, FallbackFanController)
- `FanController.cs`: Added `WriteEc()` public method for throttling mitigation; Used by EcFanControllerWrapper
- `OmenDesktopRgbService.cs`: Enhanced with HidSharp direct HID communication; Added 58-byte SignalRGB-compatible packet protocol; Added `SetZoneColorViaHidAsync()`, `BuildHidPacket()`, `WriteHidPacket()` methods; Added SignalRGB zone configuration (Diamond, Light Bar, CPU Cooler, ARGB 1-3)
- `PawnIOEcAccess.cs`: Added EC register 0x95 to write allowlist for throttling mitigation
- `WinRing0EcAccess.cs`: Added EC register 0x95 to write allowlist for throttling mitigation

### Services Modified
- `HardwareMonitoringService.cs`: Added `MonitoringHealthStatus` enum, health tracking, removed synthetic data generation, added auto-restart after 3 consecutive timeouts
- `FanService.cs`: Reduced curve update interval (10s → 5s), force refresh (60s → 30s); Added `InterpolateFanSpeed()` method with slope-based linear interpolation; Applied to both unified and independent curve modes
- `LibreHardwareMonitorImpl.cs`: Added `TryRestartAsync()` implementation - restarts worker or reinitializes in-process monitor
- `WmiBiosMonitor.cs`: Added `TryRestartAsync()` stub (WMI BIOS has no persistent state)

### ViewModels Modified
- `DashboardViewModel.cs`: Added `MonitoringHealthStatus`, `MonitoringHealthStatusText`, `MonitoringHealthColor`, `LastSampleAge`, `HasHistoricalData`, `HasLiveData`
- `MainViewModel.cs`: Added `_osdService.SetPerformanceMode()` call in `OnPerformanceModeApplied` handler
- `SettingsViewModel.cs`: Added `StandaloneStatus`, `StandaloneStatusColor`, `StandaloneStatusSummary`, `DependencyAudit`, `PawnIOOnlyMode` properties and `RefreshStandaloneStatus()` method
- `FanDiagnosticsViewModel.cs`: Added guided diagnostic script with `RunGuidedDiagnosticAsync()`, progress tracking, and pass/fail summary
- `FanControlViewModel.cs`: Added `PredictedFanPercent`, `CurvePreviewText`, `CurveValidationMessage` for live curve preview

### Views Modified
- `DashboardView.xaml`: Added health status display in header with color-coded indicator
- `OsdOverlayWindow.xaml`: Changed FPS label to "GPU" (activity indicator) with tooltip
- `OsdOverlayWindow.xaml.cs`: Replaced fake FPS estimation with GPU load display, added sample staleness detection (5s threshold)
- `SettingsView.xaml`: Added Standalone Status panel with color-coded status/summary, PawnIO-Only Mode toggle
- `FanDiagnosticsView.xaml`: Added Guided Diagnostic panel with Run Full Test button, progress bar, and results display
- `FanControlView.xaml`: Added Curve Preview panel showing predicted fan % and validation messages

### Models Modified
- `FeaturePreferences.cs`: Added `SuppressHotkeysInRdp` setting (default: true), `PawnIOOnlyMode` setting (default: false)
- `SystemInfo.cs`: Added `DependencyCheck`, `StandaloneStatus` enum, `DependencyAudit` classes for standalone operation tracking

---

## 📋 Known Issues

### From User Reports (v2.6.1)
1. **OMEN Max (RTX 5090)**: Fan profiles not working correctly on 2025 OMEN Max models
2. **OMEN 16 (RTX 4050)**: Similar fan control issues with all profile types
3. **OSD**: FPS, performance mode display, and sensor values not updating correctly

### Investigation Notes
- Both affected systems use Legacy WMI fallback (CIM commands fail)
- Both have Thermal Policy V1 (OMEN Max forces V2 for fan commands)
- Fan control backend reports as working but behavior suggests timing/command issues
- OSD sensor refresh may be disconnected from main monitoring loop

---

## 🎯 Roadmap Progress (v2.7.0)

### ✅ Completed
- [x] Monitoring health status & stale detection (#3)
- [x] WMI heartbeat health tracking (#8)
- [x] RDP popup suppression (#13)
- [x] Unified status header (#14)
- [x] Empty states for charts (#16)
- [x] Desktop safe mode detection (#12 - already existed)
- [x] Standalone dependency audit + UI (#1)
- [x] PawnIO-only mode (#2)
- [x] Worker auto-restart/failover (#4)
- [x] Guided fan diagnostic script (#5)
- [x] Fan curve validation + preview (#6)
- [x] Capability probe per model (#7)
- [x] Fan RPM verification scoring (#9)
- [x] GPU power/thermal policy UI (#10)
- [x] BIOS query reliability + UI (#11)
- [x] Tray quick actions (#15)
- [x] Temperature history bug fix (v2.6.1)
- [x] Unified RGB layer (#18)
- [x] Visual polish - gauges/sparklines (#17)
- [x] HP update guidance panel (#26)
- [x] Logitech SDK (#19) - Already implemented
- [x] Corsair iCUE SDK (#20) - Already implemented
- [x] Razer Chroma SDK (#21) - Already implemented
- [x] Platform-aware update assets (#25)
- [x] Linux low-overhead mode (#22)
- [x] Linux tray integration (#23)
- [x] Linux sensor robustness (#24)

### ✅ All Roadmap Items Complete!

---

## 📊 Testing Checklist

### Pre-Release Testing
- [ ] Test RDP suppression during active remote session
- [ ] Verify monitoring health shows correct status
- [ ] Confirm empty state displays when no data
- [ ] Test WMI heartbeat health indicator
- [ ] Verify fan profiles on OMEN 16 models
- [ ] Verify fan profiles on OMEN Max models
- [ ] Test OSD FPS accuracy
- [ ] Test OSD performance mode updates

### Affected Models to Test
- OMEN by HP Gaming Laptop 16-xd0xxx (RTX 4050, BIOS F.31)
- OMEN MAX Gaming Laptop 16t-ah000 (RTX 5090, BIOS F.21)
