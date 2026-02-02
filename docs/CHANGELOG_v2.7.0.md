# OmenCore v2.7.0 Changelog

**Release Date:** TBD  
**Branch:** `feature/v2.7.0-development`

---

## 🚀 New Features

### Monitoring & Reliability
- **Monitoring Health Status Indicator**: Dashboard now shows real-time monitoring health (Healthy/Degraded/Stale) with color-coded status and sample age
- **Last Sample Age Display**: Shows how fresh sensor data is (e.g., "Just now", "5s ago")
- **Synthetic Data Removal**: Charts no longer show fake data when no real samples exist - proper empty states instead

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

---

## 🐛 Bug Fixes

### OSD / Overlay Issues
- [x] **FPS Display Changed to GPU Activity**: OSD now shows GPU activity % instead of fake FPS estimate (no game hooks available)
- [x] **Performance Mode Not Updating in OSD**: Fixed - overlay now updates when performance mode changes
- [x] **Frozen GPU/CPU Values**: Added staleness detection (5s threshold) with fallback to ThermalProvider for live data

### Fan Control Issues (OMEN 16 / OMEN Max)
- [x] **More Aggressive Fan Retention**: Reduced countdown extension from 15s → 8s to combat BIOS reversion
- [x] **Faster Curve Updates**: Reduced curve update interval from 10s → 5s for more responsive fan control
- [x] **More Frequent Force Refresh**: Reduced force refresh from 60s → 30s to maintain fan settings
- [ ] **Max Profile Drops**: Fan ramps to max (~6300 RPM) then drops to low and stays there (timing improved)
- [ ] **Extreme Profile No Effect**: "Max @ 75°C" profile doesn't change fan behavior (timing improved)
- [ ] **Gaming Profile Stuck at Max**: "Max @ 80°C" runs fans at max even at 30°C temps
- [ ] **Auto Profile RPM Zero**: BIOS Auto mode causes RPM to drop to 0
- [ ] **Silent Profile Glitches**: Fans oscillate rapidly (1200→200→2200→500 RPM in 1-2s cycles)

---

## 🔧 Technical Changes

### Services Modified
- `HardwareMonitoringService.cs`: Added `MonitoringHealthStatus` enum, health tracking, removed synthetic data generation
- `HotkeyService.cs`: Added `ShouldSuppressForRdp()` check in WndProc
- `OmenKeyService.cs`: Added RDP suppression in hook callback and WMI event handler
- `HpWmiBios.cs`: Added `WmiHeartbeatHealth` enum, heartbeat failure tracking, `HeartbeatHealthChanged` event
- `FanService.cs`: Reduced curve update interval (10s → 5s), force refresh (60s → 30s) for more responsive control
- `OsdService.cs`: Performance mode now properly synced via `ModeApplied` event handler

### Hardware Modified
- `WmiFanController.cs`: Reduced countdown extension interval (15s → 8s) for more aggressive fan retention

### ViewModels Modified
- `DashboardViewModel.cs`: Added `MonitoringHealthStatus`, `MonitoringHealthStatusText`, `MonitoringHealthColor`, `LastSampleAge`, `HasHistoricalData`, `HasLiveData`
- `MainViewModel.cs`: Added `_osdService.SetPerformanceMode()` call in `OnPerformanceModeApplied` handler

### Views Modified
- `DashboardView.xaml`: Added health status display in header with color-coded indicator
- `OsdOverlayWindow.xaml`: Changed FPS label to "GPU" (activity indicator) with tooltip
- `OsdOverlayWindow.xaml.cs`: Replaced fake FPS estimation with GPU load display, added sample staleness detection (5s threshold)

### Models Modified
- `FeaturePreferences.cs`: Added `SuppressHotkeysInRdp` setting (default: true)

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

### 🔲 Remaining High Priority
- [ ] Standalone dependency audit + UI (#1)
- [ ] PawnIO-only mode (#2)
- [ ] Worker auto-restart/failover (#4)
- [ ] Guided fan diagnostic script (#5)
- [ ] Fan curve validation + preview (#6)
- [ ] Capability probe per model (#7)
- [ ] Fan RPM verification scoring (#9)
- [ ] Unified RGB layer (#18)
- [ ] Logitech SDK (#19)
- [ ] Corsair iCUE SDK (#20)

### 🔲 Remaining Medium Priority
- [ ] GPU power/thermal policy UI (#10)
- [ ] BIOS query reliability + UI (#11)
- [ ] Tray quick actions (#15)
- [ ] Visual polish - gauges/sparklines (#17)
- [ ] Razer Chroma SDK (#21)
- [ ] Linux low-overhead mode (#22)
- [ ] Linux tray integration (#23)
- [ ] Linux sensor robustness (#24)
- [ ] Platform-aware update assets (#25)
- [ ] HP update guidance panel (#26)

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
