# OmenCore v2.7.0 – Codebase Review & Roadmap

Date: 2026-02-02

## Scope
This document reviews the current codebase and core functions, then proposes detailed additions and improvements for v2.7.0.

---

## 1) Codebase Review (Core Functions)

### 1.1 Fan Control Pipeline
The fan control stack is centered on `FanService`. It handles curve application, thermal protection, diagnostic mode, and UI telemetry population. Curve application is continuous (time-based) with fan preset logic to switch between BIOS Auto/Max/Custom curves.

Key behaviors:
- Initializes telemetry and starts a monitoring loop to keep fan RPM data visible as soon as the UI loads.
- Applies presets, choosing between Auto control and custom curves based on the selected preset.
- Supports thermal protection and curve smooth transitions.

References:
- Fan service initialization, start, and preset application flow in [src/OmenCoreApp/Services/FanService.cs](src/OmenCoreApp/Services/FanService.cs#L240-L360)

### 1.2 Hardware Monitoring Loop
`HardwareMonitoringService` provides the main telemetry loop with adaptive polling, low-overhead mode, change detection, and a timeout wrapper to prevent hangs. It also generates dashboard metrics and throttles UI updates to avoid Dispatcher backlog.

Key behaviors:
- Adaptive polling based on low-overhead mode
- Timeout-protected `ReadSampleAsync` calls with heartbeat logging
- Change detection to reduce UI churn

References:
- Monitoring loop, timeout, and UI throttling in [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220)
- Change detection and historical data handling in [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L220-L360)

### 1.3 Sensor Bridge (LibreHardwareMonitor)
`LibreHardwareMonitorImpl` is the primary sensor bridge. It caches readings for performance, supports low-overhead mode, and optionally uses a crash-isolated worker process.

Key behaviors:
- In-process vs out-of-process worker mode
- Cache lifetime control to reduce DPC latency
- Temperature smoothing and stuck-reading detection

References:
- Worker mode and caching strategy in [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200)

### 1.4 BIOS/EC Control Layer (HP WMI)
`HpWmiBios` encapsulates HP WMI BIOS commands and maintains a heartbeat for 2023+ models to keep fan control unlocked. This is the foundation for fan mode, temperature, GPU power, and keyboard lighting commands.

References:
- BIOS command surface and heartbeat strategy in [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs#L1-L200)

### 1.5 Performance Modes
`PerformanceModeService` applies a performance mode in three phases: Windows power plan, EC power limits, then fan policy. It exposes what features are available for the current device.

References:
- Apply flow and capability description in [src/OmenCoreApp/Services/PerformanceModeService.cs](src/OmenCoreApp/Services/PerformanceModeService.cs#L1-L175)

### 1.6 Undervolting
`UndervoltService` monitors and applies offsets through a provider abstraction. It supports periodic probing and event updates for UI state.

References:
- Apply/reset/refresh and monitoring loop in [src/OmenCoreApp/Services/UndervoltService.cs](src/OmenCoreApp/Services/UndervoltService.cs#L1-L170)

### 1.7 GPU Mode Switching
`GpuSwitchService` gates support to HP OMEN devices and uses detection logic to infer the current GPU mode. It already documents the limitations and only enables when BIOS support is detected.

References:
- Support checks and detection pipeline in [src/OmenCoreApp/Services/GpuSwitchService.cs](src/OmenCoreApp/Services/GpuSwitchService.cs#L1-L170)

### 1.8 Notifications
`NotificationService` provides toast notifications for fan mode, performance mode, updates, and game profiles with opt-in toggles.

References:
- Notification preferences and fan/perf notifications in [src/OmenCoreApp/Services/NotificationService.cs](src/OmenCoreApp/Services/NotificationService.cs#L1-L150)

### 1.9 Auto-Update
`AutoUpdateService` checks GitHub releases, parses version tags (with prerelease handling), and prepares update metadata and download information.

References:
- Update check flow and parsing logic in [src/OmenCoreApp/Services/AutoUpdateService.cs](src/OmenCoreApp/Services/AutoUpdateService.cs#L1-L200)

---

## 2) Observations (Strengths & Risks)

### Strengths
- Clear service boundaries and lifecycle management for core hardware loops.
- Monitoring loop includes timeout protections and heartbeat logging, improving reliability.
- Hardware monitoring supports low-overhead mode with cache control for DPC latency management.
- Fan control supports BIOS Auto mode and custom curves, including thermal safety.

### Risks / Gaps
- Dashboard historical data still generates synthetic data when no samples exist, which can mask telemetry failures and confuse users. This is visible in `GenerateSampleData()`.
  - Reference: [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L300-L360)
- GPU switching is detection-heavy but still lacks a clearly documented user-facing reboot flow or BIOS setting messaging in UI.
  - Reference: [src/OmenCoreApp/Services/GpuSwitchService.cs](src/OmenCoreApp/Services/GpuSwitchService.cs#L1-L170)
- Auto-update relies on release parsing; there is no explicit artifact selection by platform (installer vs zip) and no in-app integrity validation visible here.
  - Reference: [src/OmenCoreApp/Services/AutoUpdateService.cs](src/OmenCoreApp/Services/AutoUpdateService.cs#L1-L200)
- Fan diagnostics UX is data-rich but lacks guided test scripts or pass/fail scoring that non-technical users can interpret.
  - Reference: [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml)

---

## 3) v2.7.0 Roadmap – Recommended Additions & Improvements

The following roadmap is organized by impact area and includes target files and acceptance criteria.

### 3.1 Monitoring & Reliability (High Priority)

**A) Monitoring Health Status & UI Warnings**
- Add a “Last Sample Age” and “Monitoring Health” indicator to the dashboard so users can detect stale data quickly.
- Emit a warning state if last sample age exceeds a threshold (e.g., 10s) or when multiple timeouts occur.

Implementation notes:
- Add health status calculation in `HardwareMonitoringService` and surface via ViewModel properties.
- Integrate into dashboard UI panel.

Targets:
- [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220)
- [src/OmenCoreApp/ViewModels/DashboardViewModel.cs](src/OmenCoreApp/ViewModels/DashboardViewModel.cs)
- [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml)

Acceptance criteria:
- UI shows “Healthy / Degraded / Stale” status.
- Logs include status transitions and last sample age.

**B) Worker Auto-Restart & Failover**
- When `ReadSampleAsync` times out 3+ consecutive times, auto restart the worker (if enabled) or reinitialize LHM.
- Provide a soft failover to WMI-only fallback if worker restarts fail.

Targets:
- [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220)
- [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200)

Acceptance criteria:
- A test scenario with forced sensor timeout recovers automatically without app restart.

**C) Remove Synthetic Charts in Live Mode**
- Replace generated chart data with “No data” states until real samples exist. Provide a clear UI prompt to prevent misinterpretation.

Targets:
- [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L260-L360)
- [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml)

Acceptance criteria:
- Charts show an explicit empty state when no data is available.

---

### 3.2 Fan Control & Diagnostics (High Priority)

**A) Guided Fan Test Scripts**
- Add a “Quick Diagnostic” flow that runs a scripted 3-step test:
  1) 30% for 10s
  2) 60% for 10s
  3) 100% for 10s
- Track RPM delta and variance; compute pass/fail for each fan.

Targets:
- [src/OmenCoreApp/Services/FanService.cs](src/OmenCoreApp/Services/FanService.cs#L240-L360)
- [src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs](src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs)
- [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml)

Acceptance criteria:
- Diagnostics view shows PASS/FAIL with tolerance thresholds.
- Results exportable to logs/diagnostics ZIP.

**B) Fan Curve Preview & Safety Guard**
- Add a live preview showing predicted fan percent for the current CPU/GPU temps before applying.
- Enforce min/max curve boundaries and warn on non-monotonic curves.

Targets:
- [src/OmenCoreApp/Services/FanService.cs](src/OmenCoreApp/Services/FanService.cs#L240-L360)
- [src/OmenCoreApp/ViewModels/FanControlViewModel.cs](src/OmenCoreApp/ViewModels/FanControlViewModel.cs)

Acceptance criteria:
- UI blocks applying unsafe curves and shows a validation message.

---

### 3.3 Performance Modes & Power Limits (Medium Priority)

**A) Configurable “What This Changes” UI**
- Use `ControlCapabilityDescription` to explain what changes will be applied for this device.
- Show per-mode CPU/GPU power limits and fan policy in the UI before apply.

Targets:
- [src/OmenCoreApp/Services/PerformanceModeService.cs](src/OmenCoreApp/Services/PerformanceModeService.cs#L1-L175)
- [src/OmenCoreApp/ViewModels/SystemControlViewModel.cs](src/OmenCoreApp/ViewModels/SystemControlViewModel.cs)

Acceptance criteria:
- Users can see the exact effects of each mode.

**B) Power Limit Verification UI**
- Surface the verification result (pass/fail) in UI and log any mismatch.

Targets:
- [src/OmenCoreApp/Services/PerformanceModeService.cs](src/OmenCoreApp/Services/PerformanceModeService.cs#L1-L175)
- [src/OmenCoreApp/ViewModels/SystemControlViewModel.cs](src/OmenCoreApp/ViewModels/SystemControlViewModel.cs)

---

### 3.4 GPU Switching (Medium Priority)

**A) Explicit Reboot Workflow**
- Implement a guided flow for hybrid/discrete switching: pre-check, notify reboot required, and optionally schedule the action.

Targets:
- [src/OmenCoreApp/Services/GpuSwitchService.cs](src/OmenCoreApp/Services/GpuSwitchService.cs#L1-L200)
- [src/OmenCoreApp/ViewModels/SystemControlViewModel.cs](src/OmenCoreApp/ViewModels/SystemControlViewModel.cs)

Acceptance criteria:
- User sees supported modes, required reboot notice, and success/failure feedback.

---

### 3.5 Auto-Update Improvements (Medium Priority)

**A) Platform-Aware Asset Selection**
- Prefer installer for Windows and zip for portable mode. Allow user override.
- Validate SHA256 hash in-app after download using release notes hash.

Targets:
- [src/OmenCoreApp/Services/AutoUpdateService.cs](src/OmenCoreApp/Services/AutoUpdateService.cs#L1-L200)

Acceptance criteria:
- Update UI indicates which artifact will be installed and verifies integrity.

---

### 3.6 UX & Diagnostics (Medium Priority)

**A) Monitoring Diagnostics Panel**
- Add a diagnostics page showing:
  - Last sample age
  - Consecutive timeout count
  - Monitoring backend (LHM vs WMI fallback)
  - Worker mode enabled/disabled

Targets:
- [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220)
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)

**B) Expand Telemetry in General View**
- Surface GPU hotspot temp, VRAM usage, and throttling flags from `LibreHardwareMonitorImpl`.

Targets:
- [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200)
- [src/OmenCoreApp/ViewModels/GeneralViewModel.cs](src/OmenCoreApp/ViewModels/GeneralViewModel.cs)
- [src/OmenCoreApp/Views/GeneralView.xaml](src/OmenCoreApp/Views/GeneralView.xaml)

Acceptance criteria:
- General tab shows additional GPU metrics if available, otherwise a clear “not supported” state.

---

### 3.7 Standalone Operation & Packaging (High Priority)

**A) Hard Guarantee: No OGH/HP Dependencies**
- Add a startup validation checklist that reports any reliance on OGH services or HP background processes.
- Show explicit “Standalone Status: OK/Degraded” in Settings → Diagnostics.
- Auto-disable features that require HP services if they are unavailable, and report why.

Targets:
- [src/OmenCoreApp/Services/SystemInfoService.cs](src/OmenCoreApp/Services/SystemInfoService.cs)
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)
- [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml)

Acceptance criteria:
- UI shows a clear standalone status with detected dependencies.
- Logs include a dependency audit summary at startup.

**A2) PawnIO-Only Standalone Mode (No Other Software Required)**
- Add a “PawnIO-only” backend mode that disables any feature requiring HP services or third‑party SDKs.
- Ensure fan control, temps, and basic telemetry still operate using PawnIO + EC/WMI only.
- Add a settings toggle to force PawnIO-only mode for stability and portability.

Targets:
- [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200)
- [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220)
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)
- [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml)

Acceptance criteria:
- App runs with PawnIO-only mode enabled and does not attempt to load any external SDKs or HP services.
- Diagnostics report the active backend as “PawnIO-only”.

**B) Single-File + Portable Mode Validation**
- Ensure portable mode does not require registry writes or external installers.
- Validate all resources (icons, assets, toast payloads) resolve when extracted to a non-admin directory.
- Add a basic “self-check” that validates missing assets and reports results in Diagnostics.

Targets:
- [src/OmenCoreApp/Services/DiagnosticsExportService.cs](src/OmenCoreApp/Services/DiagnosticsExportService.cs)
- [src/OmenCoreApp/Services/LoggingService.cs](src/OmenCoreApp/Services/LoggingService.cs)
- New: docs/STANDALONE_CHECKLIST.md

Acceptance criteria:
- App runs from a USB path without errors and passes self-check.

---

### 3.8 GUI Improvements (High Priority)

**A) Consistency & State Clarity**
- Add “Last applied” timestamps for fan and performance modes.
- Surface backend source (WMI/EC/Worker) for telemetry panels.

Targets:
- [src/OmenCoreApp/ViewModels/DashboardViewModel.cs](src/OmenCoreApp/ViewModels/DashboardViewModel.cs)
- [src/OmenCoreApp/ViewModels/FanControlViewModel.cs](src/OmenCoreApp/ViewModels/FanControlViewModel.cs)
- [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml)
- [src/OmenCoreApp/Views/FanControlView.xaml](src/OmenCoreApp/Views/FanControlView.xaml)

**B) General Tab UX Polish**
- Add clearer status text for “Custom” profile (points to Advanced tab).
- Add micro tooltips for power, load, and temperature source.

Targets:
- [src/OmenCoreApp/ViewModels/GeneralViewModel.cs](src/OmenCoreApp/ViewModels/GeneralViewModel.cs)
- [src/OmenCoreApp/Views/GeneralView.xaml](src/OmenCoreApp/Views/GeneralView.xaml)

**C) Diagnostics UX**
- Add visual pass/fail badge to fan diagnostics history.
- Add “Copy results” button to diagnostics views.

Targets:
- [src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs](src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs)
- [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml)

---

### 3.9 RGB & Peripheral SDK Integration (High Priority)

**A) RGB Unification Layer**
- Create a single RGB abstraction that maps zones and effects to HP + third‑party devices.
- Expose a device capability table (zones, effects, brightness, supported animations).

Targets:
- New: [src/OmenCoreApp/Services/RgbService.cs](src/OmenCoreApp/Services/RgbService.cs)
- New: [src/OmenCoreApp/Models/RgbCapabilities.cs](src/OmenCoreApp/Models/RgbCapabilities.cs)
- New: [src/OmenCoreApp/Models/RgbEffect.cs](src/OmenCoreApp/Models/RgbEffect.cs)

Acceptance criteria:
- Lighting UI can target “All devices” or per-device selection.

**B) Logitech G HUB Integration (Implement, not stub)**
- Replace stub calls with real Logitech LED SDK usage.
- Add device discovery and feature flags (per-device zones and effect support).

Targets:
- [src/OmenCoreApp/Logitech/LogitechGHubSdk.cs](src/OmenCoreApp/Logitech/LogitechGHubSdk.cs)
- New: [src/OmenCoreApp/Logitech/LogitechDeviceCapabilities.cs](src/OmenCoreApp/Logitech/LogitechDeviceCapabilities.cs)

**C) Corsair iCUE Integration (Implement, not stub)**
- Replace stub calls with CUE SDK binding.
- Support basic effects: static, breathing, wave, per-zone color.

Targets:
- [src/OmenCoreApp/Corsair/CorsairICueSdk.cs](src/OmenCoreApp/Corsair/CorsairICueSdk.cs)
- New: [src/OmenCoreApp/Corsair/CorsairDeviceCapabilities.cs](src/OmenCoreApp/Corsair/CorsairDeviceCapabilities.cs)

**D) Razer Chroma Integration**
- Add a Razer integration layer with device enumeration and a minimal effect set.
- Guard with opt-in settings and clear fallback behavior.

Targets:
- [src/OmenCoreApp/Razer/RazerService.cs](src/OmenCoreApp/Razer/RazerService.cs)
- New: [src/OmenCoreApp/Razer/RazerDeviceCapabilities.cs](src/OmenCoreApp/Razer/RazerDeviceCapabilities.cs)

**E) Lighting UI Updates**
- Add device selector, effect picker, and per-device zone preview.
- Provide a “Test effect” button with automatic revert.

Targets:
- [src/OmenCoreApp/ViewModels/LightingViewModel.cs](src/OmenCoreApp/ViewModels/LightingViewModel.cs)
- [src/OmenCoreApp/Views/LightingView.xaml](src/OmenCoreApp/Views/LightingView.xaml)

---

### 3.10 OMEN-Specific Feature Parity (High Priority)

**A) Per‑Model Capability Probe**
- Add a capability probe that identifies which BIOS/EC commands are available per model.
- Store capabilities in a model profile to gate UI controls and prevent unsupported actions.

Targets:
- [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs)
- New: [src/OmenCoreApp/Models/DeviceCapabilities.cs](src/OmenCoreApp/Models/DeviceCapabilities.cs)
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)

**B) WMI Heartbeat Health & Recovery**
- Expose heartbeat status, last success time, and automatic recovery attempts.
- Show a UI badge when heartbeat is failing and provide a one‑click reset.

Targets:
- [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs)
- [src/OmenCoreApp/ViewModels/DashboardViewModel.cs](src/OmenCoreApp/ViewModels/DashboardViewModel.cs)
- [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml)

**C) Fan RPM Verification & Scoring**
- Add tolerance‑based RPM verification (actual vs expected) with pass/fail scoring.
- Use results to flag faulty fans or BIOS read errors.

Targets:
- [src/OmenCoreApp/Services/FanService.cs](src/OmenCoreApp/Services/FanService.cs#L240-L360)
- [src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs](src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs)
- [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml)

**D) GPU Power & Thermal Policy Enumeration**
- Surface GPU power presets and thermal policy version in UI for transparency.

Targets:
- [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs)
- [src/OmenCoreApp/ViewModels/SystemControlViewModel.cs](src/OmenCoreApp/ViewModels/SystemControlViewModel.cs)
- [src/OmenCoreApp/Views/SystemControlView.xaml](src/OmenCoreApp/Views/SystemControlView.xaml)

**E) BIOS Query Reliability Improvements**
- Prefer CIM queries over legacy WMI and add retry + fallback logic.
- Cache BIOS data with a validity window to reduce failures.
- Expose “last BIOS query ok” timestamp and error reason in UI.

Targets:
- [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs)
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)
- [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml)

---

### 3.11 OMEN Desktop Support (Monitor‑Only Safe Mode)

**A) Desktop Detection & Safety Lock**
- Detect OMEN desktops and automatically force monitoring‑only mode.
- Hard‑block fan control, EC writes, and GPU power changes on desktops.
- Add UI messaging to explain the safety lock.

Targets:
- [src/OmenCoreApp/Services/SystemInfoService.cs](src/OmenCoreApp/Services/SystemInfoService.cs)
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)
- [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml)

Acceptance criteria:
- Desktops can monitor sensors but cannot alter cooling or power settings.

---

### 3.12 UI/UX, Layout, Tray, and Visual Polish (High Priority)

**A) Unified Status Header**
- Add a persistent header with backend, monitoring health, and last sample age.

Targets:
- [src/OmenCoreApp/ViewModels/DashboardViewModel.cs](src/OmenCoreApp/ViewModels/DashboardViewModel.cs)
- [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml)

**B) System Tray Improvements**
- Add quick fan/perf mode selectors, pause monitoring, open logs, and exit.
- Show current mode in tray tooltip.

Targets:
- [src/OmenCoreApp/ViewModels/MainViewModel.cs](src/OmenCoreApp/ViewModels/MainViewModel.cs)
- [src/OmenCoreApp/Services/NotificationService.cs](src/OmenCoreApp/Services/NotificationService.cs)
- [src/OmenCoreApp/Views/MainWindow.xaml](src/OmenCoreApp/Views/MainWindow.xaml)

**C) Layout Consistency & Empty States**
- Add empty states for charts and diagnostics (no data, not supported).
- Standardize spacing and typography scale across tabs.

Targets:
- [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml)
- [src/OmenCoreApp/Views/GeneralView.xaml](src/OmenCoreApp/Views/GeneralView.xaml)
- [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml)

**D) Visual Polish & Graphics**
- Add gradient ring gauges for CPU/GPU temps.
- Add live fan‑curve marker on the curve editor.
- Add compact sparklines for recent temps.

Targets:
- [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml)
- [src/OmenCoreApp/Views/FanControlView.xaml](src/OmenCoreApp/Views/FanControlView.xaml)

---

### 3.13 Hardware Update Guidance (Medium Priority)

**A) Safe Update Guidance Panel (No External Dependencies)**
- Add a settings panel that links to HP Support Assistant and model‑specific driver pages.
- Pre‑fill detected model/serial in the UI for easier copy/paste.

Targets:
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)
- [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml)
- [src/OmenCoreApp/Services/SystemInfoService.cs](src/OmenCoreApp/Services/SystemInfoService.cs)

**B) Optional Update Check (If Allowed)**
- If HP provides a reliable public endpoint, add an opt‑in check for BIOS/driver updates.
- Must be opt‑in and clearly labeled as “best effort.”

Targets:
- New: [src/OmenCoreApp/Services/HardwareUpdateService.cs](src/OmenCoreApp/Services/HardwareUpdateService.cs)
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)

---

### 3.14 RDP Pop‑Up Suppression (High Priority)

**A) Remote Session Detection**
- Detect RDP/remote sessions and suppress any “bring to front” logic.
- Disable hotkey handlers and OMEN key triggers while in RDP.

Targets:
- [src/OmenCoreApp/Services/HotkeyService.cs](src/OmenCoreApp/Services/HotkeyService.cs)
- [src/OmenCoreApp/Services/OmenKeyService.cs](src/OmenCoreApp/Services/OmenKeyService.cs)
- [src/OmenCoreApp/ViewModels/MainViewModel.cs](src/OmenCoreApp/ViewModels/MainViewModel.cs)

**B) User Setting: Suppress Popups in RDP**
- Add a settings toggle: “Suppress popups during Remote Desktop”.
- Default to enabled to prevent repeated UI focus on remote sessions.

Targets:
- [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs)
- [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml)

Acceptance criteria:
- App never steals focus during RDP.
- Hotkeys and OMEN key are ignored during remote sessions.

---

### 3.15 Linux Improvements (Medium Priority)

**A) Low‑Overhead Mode Parity**
- Add a low‑overhead monitoring mode to Linux (matching Windows).
- Reduce polling when stable to lower CPU usage.

Targets:
- [src/OmenCore.Avalonia/Services/LinuxHardwareService.cs](src/OmenCore.Avalonia/Services/LinuxHardwareService.cs)
- [src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs](src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs)

**B) Tray Integration (Linux GUI)**
- Add system tray menu for quick fan/perf modes and exit.

Targets:
- [src/OmenCore.Avalonia/App.axaml.cs](src/OmenCore.Avalonia/App.axaml.cs)
- [src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs](src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs)

**C) Sensor Robustness**
- Improve hwmon path discovery and auto‑reprobe if sensors disappear.
- Surface “missing sensor” states in UI instead of stale values.

Targets:
- [src/OmenCore.Avalonia/Services/LinuxHardwareService.cs](src/OmenCore.Avalonia/Services/LinuxHardwareService.cs)
- [src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs](src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs)

---
## 6) Effort Estimates (Rough)

| Area | Item | Effort | Notes | Target Files |
|---|---|---|---|---|
| Standalone | Dependency audit + UI | 4–6h | Status in Settings + startup log | [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs), [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml) |
| Standalone | PawnIO-only mode | 6–10h | Backend toggle + safe fallbacks | [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200), [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220) |
| Monitoring | Health status & stale detection | 4–6h | UI + status thresholds | [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220), [src/OmenCoreApp/ViewModels/DashboardViewModel.cs](src/OmenCoreApp/ViewModels/DashboardViewModel.cs) |
| Monitoring | Worker auto-restart/failover | 6–10h | Restart policy + fallback | [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200) |
| Fan | Guided diagnostic script | 6–8h | Scripted test + pass/fail | [src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs](src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs), [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml) |
| Fan | Curve validation + preview | 4–6h | Guard rails + prediction | [src/OmenCoreApp/ViewModels/FanControlViewModel.cs](src/OmenCoreApp/ViewModels/FanControlViewModel.cs) |
| GUI | General tab polish | 3–5h | Tooltips + status text | [src/OmenCoreApp/Views/GeneralView.xaml](src/OmenCoreApp/Views/GeneralView.xaml) |
| GUI | Diagnostics UX | 3–5h | Badges + copy button | [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml) |
| RGB | Unified RGB layer | 8–12h | New abstraction models | [src/OmenCoreApp/Services/RgbService.cs](src/OmenCoreApp/Services/RgbService.cs) |
| RGB | Logitech SDK | 8–12h | Device discovery + effects | [src/OmenCoreApp/Logitech/LogitechGHubSdk.cs](src/OmenCoreApp/Logitech/LogitechGHubSdk.cs) |
| RGB | Corsair iCUE | 8–12h | CUE SDK binding | [src/OmenCoreApp/Corsair/CorsairICueSdk.cs](src/OmenCoreApp/Corsair/CorsairICueSdk.cs) |
| RGB | Razer Chroma | 6–10h | Basic effects + enum | [src/OmenCoreApp/Razer/RazerService.cs](src/OmenCoreApp/Razer/RazerService.cs) |
| Update | Platform-aware assets + hash | 4–6h | Installer vs zip | [src/OmenCoreApp/Services/AutoUpdateService.cs](src/OmenCoreApp/Services/AutoUpdateService.cs#L1-L200) |

## 7) Master Work Table (v2.7.0)

| ID | Area | Item | Priority | Status | Owner | Target Files |
|---|---|---|---|---|---|---|
| 1 | Standalone | Dependency audit + UI | High | ☐ |  | [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs), [src/OmenCoreApp/Views/SettingsView.xaml](src/OmenCoreApp/Views/SettingsView.xaml) |
| 2 | Standalone | PawnIO‑only mode | High | ☐ |  | [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200), [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220) |
| 3 | Monitoring | Health status & stale detection | High | ☐ |  | [src/OmenCoreApp/Services/HardwareMonitoringService.cs](src/OmenCoreApp/Services/HardwareMonitoringService.cs#L1-L220), [src/OmenCoreApp/ViewModels/DashboardViewModel.cs](src/OmenCoreApp/ViewModels/DashboardViewModel.cs) |
| 4 | Monitoring | Worker auto‑restart/failover | High | ☐ |  | [src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs](src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs#L1-L200) |
| 5 | Fan | Guided diagnostic script | High | ☐ |  | [src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs](src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs), [src/OmenCoreApp/Views/FanDiagnosticsView.xaml](src/OmenCoreApp/Views/FanDiagnosticsView.xaml) |
| 6 | Fan | Curve validation + preview | High | ☐ |  | [src/OmenCoreApp/ViewModels/FanControlViewModel.cs](src/OmenCoreApp/ViewModels/FanControlViewModel.cs) |
| 7 | OMEN | Capability probe per model | High | ☐ |  | [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs), [src/OmenCoreApp/Models/DeviceCapabilities.cs](src/OmenCoreApp/Models/DeviceCapabilities.cs) |
| 8 | OMEN | WMI heartbeat health | High | ☐ |  | [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs) |
| 9 | OMEN | Fan RPM verification scoring | High | ☐ |  | [src/OmenCoreApp/Services/FanService.cs](src/OmenCoreApp/Services/FanService.cs#L240-L360), [src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs](src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs) |
| 10 | OMEN | GPU power/thermal policy UI | Medium | ☐ |  | [src/OmenCoreApp/ViewModels/SystemControlViewModel.cs](src/OmenCoreApp/ViewModels/SystemControlViewModel.cs) |
| 11 | BIOS | Query reliability + UI | Medium | ☐ |  | [src/OmenCoreApp/Hardware/HpWmiBios.cs](src/OmenCoreApp/Hardware/HpWmiBios.cs), [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs) |
| 12 | Desktop | Monitor‑only safe mode | High | ☐ |  | [src/OmenCoreApp/Services/SystemInfoService.cs](src/OmenCoreApp/Services/SystemInfoService.cs) |
| 13 | RDP | Suppress popups in RDP | High | ☐ |  | [src/OmenCoreApp/Services/HotkeyService.cs](src/OmenCoreApp/Services/HotkeyService.cs), [src/OmenCoreApp/Services/OmenKeyService.cs](src/OmenCoreApp/Services/OmenKeyService.cs) |
| 14 | GUI | Unified status header | High | ☐ |  | [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml) |
| 15 | Tray | Quick actions + status | Medium | ☐ |  | [src/OmenCoreApp/ViewModels/MainViewModel.cs](src/OmenCoreApp/ViewModels/MainViewModel.cs) |
| 16 | GUI | Empty states + spacing | Medium | ☐ |  | [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml), [src/OmenCoreApp/Views/GeneralView.xaml](src/OmenCoreApp/Views/GeneralView.xaml) |
| 17 | GUI | Visual polish (gauges/sparklines) | Medium | ☐ |  | [src/OmenCoreApp/Views/DashboardView.xaml](src/OmenCoreApp/Views/DashboardView.xaml) |
| 18 | RGB | Unified RGB layer | High | ☐ |  | [src/OmenCoreApp/Services/RgbService.cs](src/OmenCoreApp/Services/RgbService.cs) |
| 19 | RGB | Logitech SDK | High | ☐ |  | [src/OmenCoreApp/Logitech/LogitechGHubSdk.cs](src/OmenCoreApp/Logitech/LogitechGHubSdk.cs) |
| 20 | RGB | Corsair iCUE SDK | High | ☐ |  | [src/OmenCoreApp/Corsair/CorsairICueSdk.cs](src/OmenCoreApp/Corsair/CorsairICueSdk.cs) |
| 21 | RGB | Razer Chroma SDK | Medium | ☐ |  | [src/OmenCoreApp/Razer/RazerService.cs](src/OmenCoreApp/Razer/RazerService.cs) |
| 22 | Linux | Low‑overhead mode | Medium | ☐ |  | [src/OmenCore.Avalonia/Services/LinuxHardwareService.cs](src/OmenCore.Avalonia/Services/LinuxHardwareService.cs) |
| 23 | Linux | Tray integration | Medium | ☐ |  | [src/OmenCore.Avalonia/App.axaml.cs](src/OmenCore.Avalonia/App.axaml.cs) |
| 24 | Linux | Sensor robustness | Medium | ☐ |  | [src/OmenCore.Avalonia/Services/LinuxHardwareService.cs](src/OmenCore.Avalonia/Services/LinuxHardwareService.cs) |
| 25 | Updates | Platform‑aware assets + hash | Medium | ☐ |  | [src/OmenCoreApp/Services/AutoUpdateService.cs](src/OmenCoreApp/Services/AutoUpdateService.cs#L1-L200) |
| 26 | Updates | HP update guidance panel | Low | ☐ |  | [src/OmenCoreApp/ViewModels/SettingsViewModel.cs](src/OmenCoreApp/ViewModels/SettingsViewModel.cs) |

## 8) Final Checklist (v2.7.0)

### Standalone & Stability
- [ ] Standalone audit shows no external dependencies required
- [ ] PawnIO‑only mode works end‑to‑end (temps + fan control + basic telemetry)
- [ ] Monitoring health status visible and accurate

### OMEN‑Specific Features
- [ ] Per‑model capability probe gating UI actions
- [ ] WMI heartbeat health + recovery visible
- [ ] Fan RPM verification scoring integrated
- [ ] GPU power/thermal policy shown in UI
- [ ] BIOS query reliability improvements in UI

### Desktop Safe Mode
- [ ] OMEN desktops forced to monitor‑only mode
- [ ] UI explains safety lock

### RDP / Remote Sessions
- [ ] Suppress popups during Remote Desktop sessions
- [ ] Hotkeys and OMEN key ignored in RDP

### RGB & SDKs
- [ ] Unified RGB abstraction enabled
- [ ] Logitech G HUB SDK implemented
- [ ] Corsair iCUE SDK implemented
- [ ] Razer Chroma SDK implemented

### UI/UX & Tray
- [ ] Unified status header on dashboard
- [ ] Tray menu actions complete
- [ ] Empty‑state panels for charts/diagnostics
- [ ] Visual polish (gauges, sparklines, curve markers)

### Linux
- [ ] Low‑overhead monitoring mode parity
- [ ] Tray integration for Linux GUI
- [ ] Sensor auto‑reprobe with clear missing‑sensor states

### Hardware Updates (Optional)
- [ ] Settings panel links to HP Support Assistant + driver page
- [ ] Optional BIOS/driver update check (opt‑in only)

## 7) v2.7.0 Checklist

### Standalone & Stability
- [ ] Standalone audit shows no external dependencies required
- [ ] PawnIO‑only mode works end‑to‑end (temps + fan control + basic telemetry)
- [ ] Monitoring health status visible and accurate

### OMEN‑Specific Features
- [ ] Per‑model capability probe gating UI actions
- [ ] WMI heartbeat health + recovery visible
- [ ] Fan RPM verification scoring integrated
- [ ] GPU power/thermal policy shown in UI
- [ ] BIOS query reliability improvements in UI

### Desktop Safe Mode
- [ ] OMEN desktops forced to monitor‑only mode
- [ ] UI explains safety lock

### RGB & SDKs
- [ ] Unified RGB abstraction enabled
- [ ] Logitech G HUB SDK implemented
- [ ] Corsair iCUE SDK implemented
- [ ] Razer Chroma SDK implemented

### UI/UX & Tray
- [ ] Unified status header on dashboard
- [ ] Tray menu actions complete
- [ ] Empty‑state panels for charts/diagnostics
- [ ] Visual polish (gauges, sparklines, curve markers)

### Hardware Updates (Optional)
- [ ] Settings panel links to HP Support Assistant + driver page
- [ ] Optional BIOS/driver update check (opt‑in only)

## 4) Release Planning (Suggested)

### v2.7.0 Milestones
1) Monitoring health indicators + stale-data UI
2) Fan diagnostics scripted test + pass/fail
3) Update flow improvements (platform selection + hash validation)
4) GPU mode switching reboot workflow

### Proposed QA Focus
- Long-run monitoring test (6–12 hours) to validate timeouts and restarts
- Fan diagnostic script on dual-fan systems
- Update flow on clean Windows install
- GPU mode detection consistency (hybrid vs discrete)

---

## 5) Summary
v2.7.0 should emphasize reliability, clarity, and safe user-facing workflows. The core service architecture is strong; the biggest wins will come from surfacing health state, improving diagnostics, and removing ambiguity in mode switching and updates.