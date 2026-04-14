# OmenCore v3.3.0 Roadmap

**Version:** 3.3.0 (Planning)  
**Release Date:** 2026-04-09  
**Previous Release:** v3.2.5 (2026-03-30)  
**Status:** In Planning Phase

---

## 📋 Overview

v3.3.0 focuses on **bug fixes from v3.2.5**, **quality improvements** across core features, and **optimization enhancements** to the System Optimizer, Bloatware Manager, and RAM Cleaner modules.

This is now planned as a **larger stability-and-polish release** rather than a narrow maintenance update. The goal is to ship **all currently identified bug fixes, UX fixes, and visual polish items** collected from Discord, Reddit, GitHub issues, and the internal audit pass.

### Key Focus Areas
1. **Critical Bug Fixes** – Address OSD positioning, fan control, and UI freezing issues
2. **Optimizer Suite Enhancements** – Improve detection, safety, and user control
3. **Performance Tuning** – Refine memory cleaning strategies and bloatware detection
4. **UX Improvements** – Fix hotkey interactions and settings UI
5. **RGB and Lighting Improvements** – Strengthen external-device support, sync reliability, and capability visibility

### Release Direction
1. **All identified fixes are in scope for 3.3.0**
2. **Bug-fix depth takes precedence over net-new experimental features**
3. **High-risk areas need stronger diagnostics, visibility, and verification**
4. **GUI consistency and polish are first-class release goals, not stretch work**

### 3.3.0 Success Criteria
- Resolve the full currently known bug list from user reports and audit findings
- Eliminate major “unclear behavior” UX gaps, especially around fans, performance mode, OSD, and GPU tuning
- Standardize visual quality across the main Windows UI surfaces
- Ship stronger diagnostics for resume, model identity, optimizer actions, and removal flows
- Improve confidence that 3.3.0 feels meaningfully more solid than 3.2.5 in daily use

---

## 🐛 Bugs from v3.2.5 (GitHub Issue #100)

> **Note:** Four additional bugs surfaced from community reports during v3.2.5 and were patched as a hotfix (v3.2.6). Those fixes are included in 3.3.0 — see [CHANGELOG_v3.3.0.md](CHANGELOG_v3.3.0.md) for full detail.
> - ✅ **Fan curve stops working after first save** (v3.2.6 hotfix)
> - ✅ **Fan verification service destroys fan state on failure** (v3.2.6 hotfix)
> - ✅ **UI freeze when switching fan presets** (v3.2.6 hotfix)
> - ✅ **Fan names show as "G" / "GP" on AMD OMEN laptops** (v3.2.6 hotfix)

### **CRITICAL** 🔴

#### Bug #1: OmenCore Freezes on "Restore Defaults" Button ✅ Fixed in 3.3.0
**Reported by:** OsamaBiden  
**Affected Device:** HP OMEN 16 xd0xxx  
**Severity:** CRITICAL (UI freeze, requires end task)  
**Description:**
- When pressing "Restore Defaults" in System Optimizer left sidebar
- OmenCore becomes unresponsive, user must kill process in Task Manager
- PowerShell window spawns briefly, then freezes

**Root Cause Analysis (Confirmed):**
- `KeyboardLightingService.RestoreDefaults()` used `.GetAwaiter().GetResult()` on async V2 calls from the WPF UI thread, causing a synchronization-context deadlock.
- `SystemOptimizerService.RevertAllAsync()` had no cancellation/timeout path and no per-step status updates.
- `StatusChanged` UI updates were not dispatcher-marshaled.

**Fix Strategy:**
- [x] Add proper async flow for V2 keyboard restore path (remove UI-thread blocking call)
- [x] Add `CancellationToken` support to `RevertAllAsync()`
- [x] Add timeout (60s) with graceful cancel handling in ViewModel
- [x] Marshal status updates via `Dispatcher.BeginInvoke()`
- [x] Add per-step status progress messages for each optimizer stage

**Files Modified:**
- `src/OmenCoreApp/Services/KeyboardLightingService.cs` (remove deadlock pattern)
- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs` (timeout + dispatcher status updates)
- `src/OmenCoreApp/Services/SystemOptimizer/SystemOptimizerService.cs` (`CancellationToken` + status progression)
- `src/OmenCoreApp/Services/SystemOptimizationService.cs` (`CreateNoWindow=true` for scheduler-tweak PowerShell)

**Estimated Effort:** 4 hours
**Priority:** P0 (Release blocker)

---

### **HIGH** 🟠

#### Bug #2: Brightness Hotkey Triggers OmenCore to Open ✅ Fixed in 3.3.0
**Reported by:** Legion  
**Affected Device:** HP OMEN 16 xd0xxx  
**Severity:** HIGH (false hotkey trigger)  
**Description:**
- Brightness control hotkey (Fn+F6 / Fn+F7) mistakenly opens OmenCore window
- Should only adjust display brightness without launching application
- Suggests hotkey capture is too broad or conflict in event handler

**Root Cause Analysis (Confirmed):**
- On HP OMEN 16 xd0xxx (AMD), Fn+brightness can emit `VK_LAUNCH_APP1`/`VK_LAUNCH_APP2` with scan code `0xE046`.
- `OmenKeyService.IsOmenKey()` treated this scan code as OMEN-button input and launched OmenCore.

**Fix Strategy:**
- [x] Add explicit exclusion for scan code `0xE046` in `VK_LAUNCH_APP1` path
- [x] Add explicit exclusion for scan code `0xE046` in `VK_LAUNCH_APP2` path
- [x] Log rejection reason for diagnostics (`brightness-key-conflict-scan-e046`)

**Files Modified:**
- `src/OmenCoreApp/Services/OmenKeyService.cs` (`IsOmenKey()` scan-code guard)

**Estimated Effort:** 2 hours
**Priority:** P1 (High user impact)

---

#### Bug #3: OSD Positioning Goes Off-Screen ✅ Fixed in 3.3.0
**Reported by:** Legion  
**Affected Device:** HP OMEN 16 xd0xxx (16:9 aspect ratio, varied resolutions)  
**Severity:** HIGH (broken feature visibility)  
**Description:**
- **TopRight & BottomRight:** OSD completely off-screen, invisible
- **BottomLeft & BottomCenter:** OSD ~90% off-screen, only small portion visible
- **TopCenter:** Not exactly centered horizontally

**Root Cause:** `GetCurrentMonitorWorkArea()` returned physical pixel coordinates from `GetMonitorInfo()`, but WPF `Left`/`Top` are in logical pixels. On a 1.5× DPI display (1920px physical = 1280px logical), this placed the OSD 640 logical pixels off the right edge of the screen.

**Fix (merged):** `GetCurrentMonitorWorkArea()` now divides physical pixel values by the per-monitor DPI scale from `PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice`. See `OsdOverlayWindow.xaml.cs`.

**Files Modified:**
- `src/OmenCoreApp/Views/OsdOverlayWindow.xaml.cs` — `GetCurrentMonitorWorkArea()` DPI conversion

**Estimated Effort:** 3 hours
**Priority:** P1 (Feature regression)

---

### **MEDIUM** 🟡

#### Bug #4: Fan Doesn't Drop to 0 RPM in Auto Mode ✅ Fixed in 3.3.0
**Reported by:** OsamaBiden  
**Affected Device:** HP OMEN 16 xd0xxx  
**Severity:** MEDIUM (minor annoyance, device still works)  
**Description:**
- When switching to "Auto" fan mode, fans remain at idle speed (200-300 RPM)
- Should drop to 0 RPM or near-zero when in idle/cool state
- Indicates potential noise even at rest

**Root Cause Analysis (Confirmed):**
- On V1 BIOS systems, the Performance→Default transition kick `SetFanLevel(20,20)` could leave a manual floor active.
- After returning to auto mode, fans stayed above idle floor instead of allowing BIOS-controlled 0 RPM behavior.

**Fix Strategy:**
- [x] Keep the V1 transition kick for Performance exit reliability
- [x] Clear manual floor after auto-mode handoff by sending `SetFanLevel(0,0)` on V1
- [x] Apply the same floor-clear in both preset switching and restore-auto code paths

**Files Modified:**
- `src/OmenCoreApp/Hardware/WmiFanController.cs` (`ApplyPreset()` + `RestoreAutoControl()`)

**Estimated Effort:** 2.5 hours
**Priority:** P2 (Non-critical)

---

### **NEW USER REPORTS (APR 2026)** 🆕

#### Bug #6: Linux GUI Black Screen on Startup (Wayland/X11 renderer)
**Reported by:** cremita, Michelle (Discord)  
**Affected Platform:** Linux desktop sessions (Wayland and some X11 driver stacks)  
**Severity:** HIGH (app opens but UI is unusable)

**Description:**
- App launches with title bar visible but content surface remains black
- Seen more frequently on Wayland or mixed GPU driver environments

**Root Cause Hypothesis:**
- GPU renderer initialization path fails silently (EGL/GLX) and falls into unusable state before software fallback is applied

**Fix Strategy:**
- [x] Add startup fallback retry: if first renderer init fails, relaunch with software mode automatically
- [x] Persist last-known-good render mode per host/session
- [x] Improve startup diagnostics with explicit `OMENCORE_GUI_RENDER_MODE=software` guidance in user-facing error text
- [x] Add sudo/DBus startup hardening for Linux GUI: recover `DBUS_SESSION_BUS_ADDRESS` from invoking user runtime when possible, otherwise disable AT-SPI bridge to avoid null-DBus startup failures (Issue #100)

**Files to Modify:**
- `src/OmenCore.Avalonia/Program.cs`
- `src/OmenCore.Avalonia/README.md`
- `INSTALL.md`

**Priority:** P1

---

#### Bug #7: Undervolt Apply Appears Non-Functional / AMD Curve Optimizer Confusion ✅ Fixed in 3.3.0
**Reported by:** Reddit users  
**Affected Platform:** Windows (Intel + AMD systems)  
**Severity:** MEDIUM-HIGH (feature trust issue)

**Description:**
- Clicking "Apply Undervolt" appears to do nothing for some users
- AMD users expect Curve Optimizer controls but do not see a clear path in UI
- Users report little/no actionable status in visible logs

**Root Cause (Confirmed):**
- Undervolt UX did not clearly expose backend/capability state near the action controls.
- AMD systems still used generic "Apply Undervolt" wording, causing Curve Optimizer confusion.
- Apply/reset actions did not provide persistent, visible in-panel result feedback.

**Fix Applied:**
- [x] Added vendor-aware labels and guidance (`Undervolting` vs `Curve Optimizer`)
- [x] Added explicit backend line (Intel/PawnIO or AMD/SMU backend) in both System Control and Tuning views
- [x] Added visible in-panel action result status for apply/reset success/failure
- [x] Added support gating and clearer unavailable-state reasoning in the System Control panel

**Files Modified:**
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/Views/SystemControlView.xaml`
- `src/OmenCoreApp/Views/TuningView.xaml`

**Priority:** P1

---

#### Bug #8: Game Library Scan Finds Nothing; No Manual Add Path
**Reported by:** Reddit users  
**Affected Platform:** Windows  
**Severity:** MEDIUM

**Status:** ✅ Fixed in 3.3.0

**Fix Applied:**
- Added `Add Game` action in Game Library toolbar to manually select executable(s)
- Preserved manually added entries across rescans in the same session

**Files Modified:**
- `src/OmenCoreApp/ViewModels/GameLibraryViewModel.cs`
- `src/OmenCoreApp/Views/GameLibraryView.xaml`

---

#### Bug #9: App Closes Unexpectedly on Power Disconnect ✅ Fixed in 3.3.0
**Reported by:** Reddit users  
**Affected Platform:** Windows laptops  
**Severity:** HIGH (unexpected app termination)

**Description:**
- App reportedly closes immediately after unplugging AC power

**Root Cause (Confirmed):**
- AC/Battery automation callbacks had gaps where UI-dispatched handlers could throw without local guards.
- Power profile transitions did not provide transition-scoped rollback/context logging, making transient handler failures harder to isolate and recover from.

**Fix Applied:**
- [x] Added transition-scoped structured logging for AC/Battery verification and profile apply path (`transitionId` correlation)
- [x] Hardened power transition event dispatch and UI sync callbacks with non-fatal guards
- [x] Added rollback attempts for fan preset and performance mode when apply steps fail during power automation
- [ ] Extended rapid AC plug/unplug stress verification in the field (post-merge monitoring)

**Files Modified:**
- `src/OmenCoreApp/Services/PowerAutomationService.cs`
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`

**Priority:** P1

---

#### Enhancement #32: Optional Lite Mode (Beginner-Friendly UI)
**Requested by:** Reddit users  
**Severity:** UX enhancement

**Description:**
- Add a simplified UI mode that hides advanced controls for less technical users

**Implementation Plan:**
- [ ] Add Settings toggle for Lite/Advanced mode
- [ ] Hide advanced tuning/system-maintenance sections in Lite mode
- [ ] Keep quick controls and core monitoring visible

**Priority:** P3

---

## ⚡ Quick Access Popup Improvements

### **Bug #5: Fan Mode Button Order is Non-Intuitive** ✅ Fixed in 3.3.0
**Reported by:** snowfall hateall (Discord)  
**Severity:** UX (misleading visual hierarchy)  
**Description:**
- Fan mode buttons in Quick Access were ordered: `Auto | Max | Quiet | Custom`
- This order has no power-ascending logic — Quiet and Max are adjacent, Auto is first which implies it is the "off" or fastest option
- Users expected the same ascending pattern as the Performance Mode row (Quiet → Balanced → Performance)

**Fix (merged):** Buttons reordered to `Quiet 🤫 | Auto ⚡ | Curve 🎛️ | Max 🔥` — ascending from quietest/lowest power to loudest/highest. The "Custom" label was also renamed to **Curve** to communicate that it applies the user's configured fan curve from the OMEN Fan tab.  
**File:** `src/OmenCoreApp/Views/QuickPopupWindow.xaml`

---

### **Enhancement: Quick Access OMEN Fan Curve Integration** 📋 Planned
**Requested by:** Discord community  
**Priority:** MEDIUM | **Effort:** 3 hours

**Current Behavior:** The "Curve" button in Quick Access sends the tag `Custom` to `FanService`, which tries to match an existing preset by name. If no preset is named exactly "Custom", this may do nothing or activate an unexpected fallback.

**Desired Behavior:** Pressing **Curve** should activate the most recently saved named fan curve preset from the OMEN Fan Control tab (e.g. "Gaming Profile", "Silent Night").

**Implementation Plan:**
- [ ] Expose `FanService.ActivePresetName` (or the last saved curve preset name) as a public property
- [ ] Pass the active preset name to `QuickPopupWindow` via `UpdateFanMode()` or a dedicated `UpdateCurvePresetName()` method
- [ ] Show a tooltip on the Curve button: "Curve: Gaming Profile" (or whatever the active preset is)
- [ ] When Curve is clicked, call `FanService.ApplyPresetByName(activePresetName)` rather than a fixed "Custom" tag

**Files to Modify:**
- `src/OmenCoreApp/Services/FanService.cs` (expose last active preset name)
- `src/OmenCoreApp/Views/QuickPopupWindow.xaml.cs` (tooltip + preset name routing)
- `src/OmenCoreApp/Views/MainWindow.xaml.cs` or wherever `QuickPopupWindow` is updated

---

## 🔧 System Optimizer Improvements (Audit Pass)

## 🌈 RGB and Lighting Improvements (Audit Pass)

### Current Implementation Status

The RGB stack is **partially implemented, but not fully complete across brands**.

- **Corsair:** strongest current integration. Supports direct HID first, with iCUE fallback, plus static/breathing/spectrum/wave and some DPI work. Still has clear gaps around macro support, per-key depth, and device-specific capability certainty.
- **Logitech:** functional direct HID path exists, with G HUB fallback, and static/breathing/spectrum/flash/wave commands are present. However, the implementation is still heuristic and marked WIP in places, with DPI control not actually implemented and status data mostly defaulted.
- **Razer:** not fully implemented. The current path depends on Synapse + Chroma Connect, the UI explicitly calls it placeholder functionality, and discovered devices are category placeholders rather than verified physical-device enumeration.
- **OpenRGB / generic system RGB:** useful as a broad fallback for desktop-class RGB devices, but it should be treated as a separate backend rather than proof that branded integrations are complete.

### **Enhancement #13: RGB Provider Capability Matrix + Backend Transparency**
**Priority:** HIGH | **Effort:** 4 hours

**Current Issue:**
- Users cannot clearly tell whether a device is using Direct HID, iCUE, G HUB, Synapse/Chroma, or OpenRGB
- The app does not clearly distinguish between verified physical devices and synthetic/provider-level placeholders
- Capability differences between providers are hidden, which makes RGB support appear more complete than it really is

**Improvements:**
1. Add a provider/backend status panel in Lighting showing active backend, dependency status, device count, and tested capability set
2. Show per-provider capability badges: Static, Breathing, Spectrum, Wave, Reactive, Per-key, DPI, Macros
3. Mark placeholder or limited integrations explicitly in the UI instead of implying parity
4. Add diagnostics text for dependency failures: iCUE not running, G HUB not available, Synapse running without Chroma Connect, OpenRGB server not reachable

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/Views/LightingView.xaml`
- `src/OmenCoreApp/Services/Rgb/RgbManager.cs`

---

### **Enhancement #14: Razer Integration Hardening**
**Priority:** HIGH | **Effort:** 5 hours

**Current Issue:**
- Razer support currently requires Synapse 3 + Chroma Connect and is not truly full-featured
- Device discovery is category-based, not real hardware enumeration
- The current UI already labels support as “placeholder functionality”

**Improvements:**
1. Replace placeholder discovery language with explicit session/dependency diagnostics
2. Only show device categories as available when the corresponding Chroma endpoints actually respond
3. Add better failure states for “Synapse running, Chroma session failed” vs “Synapse not installed/running”
4. Validate static/breathing/spectrum/wave behavior by endpoint and report unsupported effects cleanly
5. Add a small compatibility matrix for tested Razer device categories

**Files to Modify:**
- `src/OmenCoreApp/Razer/RazerService.cs`
- `src/OmenCoreApp/Services/Rgb/RazerRgbProvider.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/Views/LightingView.xaml`

---

### **Enhancement #15: Logitech Direct HID Reliability Pass**
**Priority:** HIGH | **Effort:** 6 hours

**Current Issue:**
- Logitech direct HID uses generalized HID++ assumptions that may not hold across all devices
- Service comments still mark parts of the implementation as WIP
- DPI control is exposed in the service surface but the direct HID backend does not implement it
- Battery, firmware, and richer device status are mostly default placeholders today

**Improvements:**
1. Implement real DPI set support where supported, or hide/disable it when unsupported
2. Add better HID++ feature probing per device instead of assuming one RGB command path fits all
3. Improve effect fallback behavior and expose when the device falls back to static color
4. Populate real battery/connection/status data where the device supports it
5. Add a tested-device list and safe capability gating for unsupported models

**Files to Modify:**
- `src/OmenCoreApp/Services/Logitech/LogitechHidDirect.cs`
- `src/OmenCoreApp/Services/LogitechDeviceService.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

---

### **Enhancement #16: Corsair Capability Depth Pass**
**Priority:** MEDIUM | **Effort:** 5 hours

**Current Issue:**
- Corsair is the most mature provider, but macro support is still not implemented in direct HID
- Some keyboard/per-key handling is still stub-like rather than model-verified
- Capability support varies by PID but the UI does not expose that clearly

**Improvements:**
1. Add device-specific capability reporting so users can see what each PID supports
2. Tighten per-key/per-zone handling on supported keyboards and remove stub-like behavior where not verified
3. Surface direct HID write failures and fallback state in diagnostics/export
4. Clarify macro support status in UI instead of leaving placeholder workflow exposed

**Files to Modify:**
- `src/OmenCoreApp/Services/Corsair/CorsairHidDirect.cs`
- `src/OmenCoreApp/Services/CorsairDeviceService.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

---

### **Enhancement #17: Cross-Provider RGB Sync Reliability + Scene Expansion**
**Priority:** HIGH | **Effort:** 6 hours

**Current Issue:**
- The current “Sync All RGB” flow applies colors directly to Corsair/Logitech/Razer/OMEN and then also routes through `RgbManager`, which risks duplicate writes and inconsistent results
- Cross-brand sync currently focuses mostly on static color parity rather than richer scenes/effects
- There is no strong per-provider feedback when one backend succeeds and another partially fails

**Improvements:**
1. Refactor sync paths so each provider is applied exactly once per action
2. Add structured sync results: succeeded, partial, failed, unsupported
3. Expand cross-brand scenes to cover static, breathing, spectrum, off, and selected performance-mode/temperature scenes
4. Add a “preview what will sync” summary so users know which backends will participate
5. Add tests for provider registration, sync deduplication, and partial-failure reporting

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/Services/Rgb/RgbManager.cs`
- `src/OmenCoreApp/Services/Rgb/CorsairRgbProvider.cs`
- `src/OmenCoreApp/Services/Rgb/LogitechRgbProvider.cs`
- `src/OmenCoreApp/Services/Rgb/RazerRgbProvider.cs`
- `src/OmenCoreApp.Tests/ViewModels/LightingViewModelTests.cs`

---

### **Enhancement #18: Lighting UI Cleanup and Trust Signals**
**Priority:** MEDIUM | **Effort:** 4 hours

**Current Issue:**
- Lighting UI mixes mature features with placeholder features without enough distinction
- Discovery/apply actions do not always tell the user whether a backend is dependency-blocked, unsupported, or simply found no devices
- RGB settings need the same polish/consistency pass already planned for the rest of the app

**Improvements:**
1. Replace placeholder notes with precise status messaging and actionable troubleshooting
2. Add “last applied backend” and “last successful sync” indicators
3. Improve empty states for no-device, dependency-missing, and unsupported-device scenarios
4. Fold RGB sections into the broader visual-polish pass so cards, spacing, and badges are consistent with the rest of 3.3.0

**Files to Modify:**
- `src/OmenCoreApp/Views/LightingView.xaml`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

---

## 🧩 Other Incomplete / Partial Areas (Audit Pass)

### **Enhancement #19: Independent CPU/GPU Fan Curve Completion**
**Priority:** HIGH | **Effort:** 5 hours

**Current Issue:**
- The fan UI exposes the concept of separate CPU and GPU curves, but the feature is explicitly disabled and the GPU editor logic is still stubbed
- GPU curve commands currently reuse simplified placeholder behavior instead of fully independent control logic

**Improvements:**
1. Either fully implement independent CPU/GPU curve behavior end-to-end or remove/fully hide the unfinished path
2. Wire separate thermal inputs, validation rules, persistence, and apply logic for each curve
3. Add clear verification messaging for dual-fan vs single-fan hardware

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`
- `src/OmenCoreApp/Views/FanControlView.xaml`
- `src/OmenCoreApp/Services/FanService.cs`

---

### **Enhancement #20: OMEN Per-Key Keyboard Lighting Backend**
**Priority:** HIGH | **Effort:** 6 hours

**Current Issue:**
- KeyboardLightingServiceV2 still logs that the HID per-key backend is not implemented
- This means some per-key-capable OMEN keyboards are still limited to coarser control paths

**Improvements:**
1. Implement the HID per-key backend for supported OMEN keyboard models
2. Add capability gating so unsupported models do not imply per-key control exists
3. Expose backend/method choice and success diagnostics in the UI and diagnostic export

**Files to Modify:**
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

---

### **Enhancement #21: Fan Calibration Workflow Completion**
**Priority:** MEDIUM | **Effort:** 4 hours

**Current Issue:**
- Fan calibration service and data model exist, but parts of the control workflow are still placeholder-grade
- Saving calibration from the UI currently shows a placeholder message instead of a complete persistence/apply flow

**Improvements:**
1. Complete save/load/apply/clear behavior from the calibration UI
2. Ensure calibrated results actually feed fan-control decisions consistently
3. Improve status text so users know whether calibration is loaded, stale, or only partially applied

**Files to Modify:**
- `src/OmenCoreApp/Controls/FanCalibrationControl.xaml.cs`
- `src/OmenCoreApp/Services/FanCalibration/FanCalibrationService.cs`
- `src/OmenCoreApp/Services/FanCalibrationStorageService.cs`

---

### **Enhancement #22: AMD Tuning Path Maturity Pass**
**Priority:** HIGH | **Effort:** 6 hours

**Current Issue:**
- AMD tuning support exists, but the initialization path is still described as placeholder-grade and capability depends heavily on driver/API availability
- The UX around AMD availability, partial support, and failed apply behavior still needs to be clarified and hardened

**Improvements:**
1. Replace placeholder-grade initialization with explicit capability detection and user messaging
2. Improve partial-apply reporting for AMD clocks, memory, power, and thermal limits
3. Tighten driver prerequisite checks and expose them clearly in the tuning UI
4. Add tested-model guidance for AMD-specific tuning support

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/Hardware/AmdGpuService.cs`
- `src/OmenCoreApp/Views/TuningView.xaml`

---

### **Enhancement #23: Rule-Based Automation Completeness**
**Priority:** MEDIUM | **Effort:** 4 hours

**Current Issue:**
- AutomationService includes action types that are not fully implemented, including performance-mode actions
- This creates a gap between configured rules and what the engine can actually carry out reliably

**Improvements:**
1. Implement performance-mode actions through the existing performance service path
2. Add unsupported-action reporting so rules fail loudly instead of silently under-delivering
3. Validate action support at rule creation/load time

**Files to Modify:**
- `src/OmenCoreApp/Services/AutomationService.cs`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- `src/OmenCoreApp/Models/AppConfig.cs`

---

### **Enhancement #24: Audio-Reactive RGB Implementation or Removal**
**Priority:** LOW | **Effort:** 4 hours

**Current Issue:**
- The audio-reactive RGB stack still uses a stubbed WASAPI loopback capture path with simulated data instead of a real audio capture implementation
- If this is exposed or planned for users, it needs to become real; if not, it should be clearly marked experimental/internal

**Improvements:**
1. Replace the stub capture path with a proper implementation
2. If kept experimental, gate it behind explicit labeling and diagnostics
3. Add provider compatibility notes for audio-driven effects

**Files to Modify:**
- `src/OmenCoreApp/Services/AudioReactiveRgbService.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

---

## ⚡ Runtime Performance and Resource Usage (Audit Pass)

### Current Performance Summary

The app already has some good foundations for lower overhead:

- Hardware monitoring supports configurable polling intervals and a low-overhead mode
- Monitoring sample history is bounded
- Dashboard metric history is trimmed to the last 24 hours
- Quick popup refresh timer stops when the popup hides

However, there are still several areas where CPU wake-ups, UI redraw frequency, and view-specific refresh loops can be reduced further while the app is open.

### **Enhancement #25: Monitoring Pipeline CPU Overhead Pass**
**Priority:** HIGH | **Effort:** 5 hours

**Current Issue:**
- The core monitoring service already throttles UI updates, but downstream UI surfaces still add their own refresh loops on top
- This creates avoidable CPU wake-ups even when telemetry is stable

**Improvements:**
1. Audit all consumers of monitoring samples and remove duplicate refresh work where event-driven updates are enough
2. Make low-overhead mode more comprehensive so it reduces secondary UI timers as well as sensor polling
3. Add a lightweight “idle / minimized” monitoring profile that automatically slows non-critical UI updates

**Files to Modify:**
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`

---

### **Enhancement #26: Dashboard Timer Consolidation**
**Priority:** HIGH | **Effort:** 4 hours

**Current Issue:**
- The hardware dashboard runs its own 2-second metrics timer and 5-second chart timer, even though the app already has a monitoring event stream
- The dashboard also performs extra work such as battery-health refreshes and sparkline queue updates on its own cadence

**Improvements:**
1. Replace timer-driven dashboard refreshes with coalesced event-driven updates where possible
2. Throttle expensive secondary metrics separately from fast telemetry text updates
3. Pause or sharply reduce dashboard refresh work when the dashboard is not visible
4. Cache expensive battery-health queries more explicitly and reduce redundant WMI work

**Files to Modify:**
- `src/OmenCoreApp/Controls/HardwareMonitoringDashboard.xaml.cs`
- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`

---

### **Enhancement #27: Chart Rendering and Collection Snapshot Optimization**
**Priority:** HIGH | **Effort:** 5 hours

**Current Issue:**
- Thermal/load/GPU VC charts repeatedly materialize full sample snapshots with `ToList()` during redraws
- These redraw paths are throttled, but they still create avoidable allocation churn and UI-thread work, especially with larger history counts

**Improvements:**
1. Move chart rendering toward incremental updates instead of full collection snapshots on every refresh
2. Reduce redraw frequency further when samples are unchanged or when the chart is off-screen
3. Add a lighter rendering mode for high history counts or low-overhead mode
4. Reuse buffers/point collections more aggressively to cut GC pressure

**Files to Modify:**
- `src/OmenCoreApp/Controls/ThermalChart.xaml.cs`
- `src/OmenCoreApp/Controls/LoadChart.xaml.cs`
- `src/OmenCoreApp/Controls/GpuVcChart.xaml.cs`

---

### **Enhancement #28: Memory Optimizer View Idle Overhead Reduction**
**Priority:** MEDIUM | **Effort:** 4 hours

**Current Issue:**
- The Memory Optimizer view refreshes every 2 seconds and repopulates top-process lists each cycle
- That is useful when the user is actively looking at the page, but expensive as a steady-state background behavior

**Improvements:**
1. Only run frequent refresh when the Memory Optimizer view is visible/active
2. Slow refresh cadence when the page is in the background
3. Diff top-process results instead of clearing/rebuilding the whole collection every cycle
4. Avoid recomputing cleanup preview every refresh unless the selected profile changes

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs`
- `src/OmenCoreApp/Views/MemoryOptimizerView.xaml.cs`

---

### **Enhancement #29: Ambient / Screen-Sampling Efficiency Pass**
**Priority:** MEDIUM | **Effort:** 4 hours

**Current Issue:**
- Ambient RGB sampling can run as fast as 100 ms and samples multiple screen-edge points on each tick
- This is reasonable for an active effect, but it should have stronger adaptive behavior to limit CPU use when the screen is static or the app is minimized

**Improvements:**
1. Add adaptive backoff when sampled colors are stable
2. Reduce sampling rate when the app is minimized or when ambient mode is not foreground-visible
3. Allow a lower-power ambient preset optimized for laptops/battery
4. Avoid unnecessary device writes when the color delta is below a stricter stability threshold

**Files to Modify:**
- `src/OmenCoreApp/Services/ScreenSamplingService.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

---

### **Enhancement #30: Background Service Timer Governance**
**Priority:** MEDIUM | **Effort:** 5 hours

**Current Issue:**
- The app has several independent background timers and polling services: monitoring, watchdog, process monitoring, RTSS polling, OpenRGB/ambient features, memory auto-clean, automation, and others
- Individually these are manageable, but together they increase idle CPU wake-ups and make performance harder to reason about

**Improvements:**
1. Inventory all always-on timers and classify them as critical, visible-only, or optional
2. Pause or defer optional timers when their feature is disabled or no UI surface depends on them
3. Add a centralized runtime-performance diagnostic view or export section showing active loops/timers
4. Introduce a more consistent minimum cadence for non-critical polling work

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/Services/ProcessMonitoringService.cs`
- `src/OmenCoreApp/Services/HardwareWatchdogService.cs`
- `src/OmenCoreApp/Services/RtssIntegrationService.cs`
- `src/OmenCoreApp/Services/AutomationService.cs`

---

### **Enhancement #31: Dispatcher and UI Update Coalescing**
**Priority:** MEDIUM | **Effort:** 4 hours

**Current Issue:**
- Monitoring updates are fanned out to MainViewModel, tray/popup surfaces, dashboard, and tuning UI
- Some of these updates are already throttled, but there is still room to coalesce property updates and reduce UI-thread churn

**Improvements:**
1. Batch related telemetry property changes together instead of firing many discrete UI invalidations
2. Reduce duplicate sample propagation where one view model can relay a shared snapshot
3. Add profiling hooks so future runtime regressions are easier to spot before release

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/Utils/TrayIconService.cs`

---

## 🔧 System Optimizer Improvements (Audit Pass)

### **Enhancement #1: Risk Assessment Safety Improvements** ✅ Done
**Priority:** HIGH | **Effort:** 3 hours

**Current State:**
- Optimizations are categorized as Low/Medium/High risk, but no granular control
- Users cannot see what each optimization actually changes before applying
- No "preview" mode to show registry keys or settings that will be modified

**Proposed Changes:**
1. Add detailed "What will be changed" descriptions to each OptimizationItem
2. Implement read-only preview of registry keys that will be modified
3. Add "Show Details" expandable panel in UI

**Code Changes:**
```csharp
public class OptimizationItem : INotifyPropertyChanged
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string? DetailedDescription { get; set; }  // NEW: Full explanation
    public List<string>? AffectedRegistryKeys { get; set; }  // NEW: Registry keys
    public List<string>? ServiceNamesAffected { get; set; }  // NEW: Service names
    public string? UndoInstructions { get; set; }  // NEW: How to manually undo
}
```

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs`
- `src/OmenCoreApp/Models/OptimizationModels.cs`
- `src/OmenCoreApp/Views/SystemOptimizerView.xaml`

**Shipped behavior:**
- Added expandable `What changes` details for every optimizer toggle.
- Surfaced affected registry keys, services, commands, and manual undo guidance inline before apply.
- Replaced generic risk labels with concrete per-toggle impact descriptions sourced from the optimizer implementation.

---

### **Enhancement #2: Revert All Operation Stability** ✅ Done
**Priority:** CRITICAL | **Effort:** 2.5 hours

**Current Issue:**
- RevertAllAsync() causes UI freeze (see Bug #1 above)
- No timeout or cancellation mechanism
- All reversions run sequentially without parallel batching

**Improvements:**
1. [x] Implement CancellationToken with 60s timeout
2. [x] Add fine-grained error handling per optimizer
3. [x] Show progress bar/status updates for multi-step reversion
4. [x] Batch compatible reversions in parallel

**Code Pattern:**
```csharp
private async Task RevertAllAsync()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try
    {
        IsLoading = true;
        var results = await _optimizerService.RevertAllAsync(cts.Token);
        // Process results...
    }
    catch (OperationCanceledException)
    {
        StatusMessage = "Revert operation timed out. Please check system state.";
    }
    // ... exception handling
}
```

---

### **Enhancement #3: Optimization State Verification** ✅ Done
**Priority:** MEDIUM | **Effort:** 4 hours

**Current Issue:**
- OptimizationVerifier exists but may not be fully integrated
- No real-time polling to detect if optimizations become un-applied
- External tools (Windows updates, OGH) can revert OmenCore changes silently

**Improvements:**
1. Periodic (hourly) background state check
2. Alert user if optimization state drifts
3. Auto-correct minor drift (service re-stop) without user intervention
4. Add "Last Verified" timestamp to each optimization

**Files to Modify:**
- `src/OmenCoreApp/Services/SystemOptimizer/OptimizationVerifier.cs`
- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs`

**Shipped behavior:**
- Switched optimizer refresh and verification flows to use the dedicated verifier as the authoritative state source.
- Added hourly background verification with visible `Last verified` and verification summary status in the optimizer UI.
- Detects drift against the last known optimizer state and automatically re-applies low-risk service toggles (`SysMain`, `WSearch`, `DiagTrack`) when they are silently reverted.

---

### **Enhancement #4: Batch Apply Safe Mode** ✅ Done
**Priority:** LOW | **Effort:** 3 hours

**Current State:**
- Only "Gaming Max" and "Balanced" presets
- No way to apply subset of category (e.g., "Power optimizations only")

**Proposed Feature:**
- Add per-category apply buttons (Apply ALL Power, Apply ALL Services, etc.)
- Implement "Custom Profile" builder where user selects individual toggles to apply

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs`
- `src/OmenCoreApp/Views/SystemOptimizerView.xaml`

**Shipped behavior:**
- Added per-category `Apply All` actions for Power, Services, Network, Input, Visual, and Storage sections.
- Added staged category-apply progress/status feedback using the existing optimizer status surface.
- Category buttons now auto-disable when that section is already fully applied or while another optimizer operation is active.

---

## 🧹 Bloatware Manager Improvements (Audit Pass)

### **Enhancement #1: Appx Package Detection Expansion** ✅ Done
**Priority:** MEDIUM | **Effort:** 2.5 hours

**Current State:**
- Detects common Microsoft bloatware (Solitaire, Zune, Tips, etc.)
- May miss OEM-specific bloatware unique to HP OMEN lineup
- Database is static; new bloatware requires code update

**Improvements:**
1. Add HP OMEN-specific bloatware detection:
   - OMEN Gaming Hub (if user wants to disable)
   - HP Armor (optional)
   - HP Analytics (optional)
   - OMEN Hub (if duplicate with Gaming Hub)
2. Implement dynamic bloatware signature loading from JSON

**New Detections to Add:**
```xml
Microsoft.GetHelp -> Help tips
Microsoft.Print3D -> 3D printing (rarely needed)
Microsoft.OneConnect -> Backup (user may not want)
GamingApp (Xbox?) -> Gaming service conflicts
Microsoft.StorePurchaseApp -> Unnecessary
```

**Files to Modify:**
- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs` (IsKnownBloatware)
- Create `config/bloatware_database.json` for signature management

**Shipped behavior:**
- Added JSON-backed signature loading for bloatware detection so new matches can be added without recompiling the service.
- Expanded OMEN and HP-specific detections for overlapping control apps and analytics packages.
- Added additional AppX signatures for legacy Microsoft helper, gaming, and companion apps while preserving the existing protected-app safety filters.

---

### **Enhancement #2: Staged Removal with Rollback** ✅ Done
**Priority:** HIGH | **Effort:** 3 hours

**Current State:**
- Removes all selected apps in parallel with minimal recovery mechanism
- If removal fails mid-operation, user has partial state

**Improvements:**
1. [x] Implement staged removal: remove one at a time with checkpoint
2. [x] Add atomic rollback: if N apps fail, rollback previous N-1 (from backup)
3. [x] Show removal progress/status during each step
4. [x] Auto-backup before batch operations

**Code Changes:**
```csharp
private async Task RemoveAppsWithRollbackAsync(List<BloatwareApp> apps)
{
    var removed = new List<BloatwareApp>();
    
    foreach (var app in apps)
    {
        try
        {
            var success = await RemoveAppAsync(app);
            if (!success)
            {
                // Rollback all previously removed
                foreach (var prev in removed)
                    await RestoreAppAsync(prev);
                throw new Exception($"Removal failed at {app.Name}, rolled back");
            }
            removed.Add(app);
        }
        catch (Exception ex)
        {
            // Log failure and offer continue/cancel
        }
    }
}
```

**Files to Modify:**
- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs`
- `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`

---

### **Enhancement #3: Startup Item & Scheduled Task Scanning Improvements** ✅ Done
**Priority:** MEDIUM | **Effort:** 2 hours

**Current Issue:**
- Scheduled task detection may miss newer bloatware
- Startup scanning via Registry only; doesn't check folder StartUp directories
- No detection of Windows Run keys (user-specific)

**Improvements:**
1. Scan additional startup locations:
   - `C:\Users\[User]\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup`
   - `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup`
   - Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
2. Enhance scheduled task filtering to catch renamed/variant tasks
3. Add friendly name display for schtasks

**Files to Modify:**
- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs` (ScanStartupItems, ScanScheduledTasks)

**Shipped behavior:**
- Added Startup folder scanning for both per-user and common Windows startup locations.
- Added safe backup/remove/restore handling for file-based startup entries detected from those folders.
- Improved scheduled-task matching and display names so renamed or path-heavy task entries are easier to detect and understand.

---

### **Enhancement #4: Removal Result Logging & Report Export** ✅ Done
**Priority:** LOW | **Effort:** 2 hours

**Current State:**
- Result log only exports for items removed in that session
- No historical tracking of what was removed/restored over time

**Improvements:**
1. Maintain persistent removal history (JSON file in %LocalAppData%)
2. Enhanced export report with:
   - Removal timestamp
   - Restoration status
   - Administrator context
   - Device info (model, BIOS version)
3. Add "View Report" link to UI after removal

**Files to Modify:**
- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs` (ExportRemovalLog)

**Shipped behavior:**
- Added persistent bloatware removal/restore history in `%LocalAppData%\OmenCore\Logs\bloatware-history.json`.
- Exported reports now include timestamped history context plus admin/device/OS metadata.
- Added an in-app `View Report` action that opens the latest generated report after removal workflows.

---

### **Enhancement #5: Removal Preview Mode** ✅ Done
**Priority:** MEDIUM | **Effort:** 2 hours

**Current Issue:**
- Bulk remove confirmation was binary and did not provide a selectable preflight list.
- Users could not review estimated operation cost or deselect edge-case items before execution.

**Improvements:**
1. Added a read-only preview panel before removal execution for selected and low-risk workflows.
2. Added per-item selection toggles so users can deselect apps before confirming.
3. Added estimated removal time and dependency-hint column per preview entry.
4. Added explicit preview actions (`Toggle All`, `Cancel`, `Remove Selected`).

**Files Modified:**
- `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`

### **Enhancement #6: Pre-Removal Restore Point** ✅ Done
**Priority:** MEDIUM | **Effort:** 2 hours

**Current Issue:**
- Bloatware removal previously started immediately after user confirmation with no integrated system-restore checkpoint option.
- Exported logs could not show whether a restore point was created before destructive changes.

**Improvements:**
1. Added a pre-removal prompt to create a Windows restore point before execution.
2. Added session reuse for a recent pre-removal restore point to avoid creating duplicates back-to-back.
3. Added restore-point metadata (timestamp/description/sequence/status) to exported bloatware removal logs.

**Files Modified:**
- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs`
- `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`

---

## 💾 RAM Cleaner Improvements (Audit Pass)

### **Enhancement #1: Adaptive Auto-Clean Profiles** ✅ Done
**Priority:** HIGH | **Effort:** 3 hours

**Current State:**
- Auto-clean: threshold-based (fixed 80% default, 30s check interval)
- Interval-clean: fixed 10-minute period
- No adaptive behavior based on usage patterns

**Proposed Profiles:**

| Profile | Check Interval | Threshold | Strategy |
|---------|---|---|---|
| **Aggressive** | 10s | 75% | Gaming/streaming optimized |
| **Balanced** (default) | 30s | 80% | General use |
| **Conservative** | 60s | 85% | Battery/low-power mode |
| **Off-Peak** | 5min | 90% | Idle machine only |

**Code Implementation:**
```csharp
public enum AutoCleanProfile
{
    Aggressive,      // Gaming/streaming
    Balanced,        // Default
    Conservative,    // Laptop battery
    OffPeakOnly,     // Night mode / idle only
    Manual           // Disabled
}

public void SetAutoCleanProfile(AutoCleanProfile profile)
{
    var (interval, threshold) = profile switch
    {
        AutoCleanProfile.Aggressive => (10, 75),
        AutoCleanProfile.Balanced => (30, 80),
        AutoCleanProfile.Conservative => (60, 85),
        AutoCleanProfile.OffPeakOnly => (300, 90),
        _ => throw new ArgumentException()
    };
    
    SetAutoClean(true, threshold);
    SetCheckInterval(TimeSpan.FromSeconds(interval));
}
```

**Files to Modify:**
- `src/OmenCoreApp/Services/MemoryOptimizerService.cs` (SetAutoCleanProfile)
- `src/OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs` (UI bindings)

**Status update:**
- [x] Added adaptive auto-clean profile support (`Aggressive`, `Balanced`, `Conservative`, `OffPeakOnly`, `Manual`) in `MemoryOptimizerService`.
- [x] Auto-clean now uses profile-defined threshold plus check interval (10s/30s/60s/300s).
- [x] Added profile selection + check-interval visibility in Memory Optimizer UI.
- [x] Persisted selected auto-clean profile in app config and restored it on startup.

---

### **Enhancement #2: Per-Process Memory Exclusion List** ✅ Done
**Priority:** MEDIUM | **Effort:** 3 hours

**Current Issue:**
- RAM cleaner applies to ALL processes equally
- Critical background services might have working sets trimmed
- No way to exclude important processes (antivirus, driver services)

**Improvements:**
1. Add exclusion list: processes to NOT trim working sets
2. Pre-populate with critical services:
   - `svchost.exe` (Windows services)
   - `dwm.exe` (Desktop Window Manager)
   - `explorer.exe` (File Explorer)
   - Antivirus processes
   - OMEN services (OmenCommandCenterBackground, OmenCap)
3. UI to add/remove custom exclusions

**Code Changes:**
```csharp
public class MemoryCleanFlags
{
    // ... existing flags
    public List<string> ExcludedProcessNames { get; set; } = new()
    {
        "svchost",
        "dwm",
        "explorer",
        "OmenCommandCenterBackground"
    };
}

private bool EmptyWorkingSets(MemoryCleanFlags flags)
{
    var processes = Process.GetProcesses()
        .Where(p => !flags.ExcludedProcessNames
            .Any(ex => p.ProcessName.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0))
        .ToList();
    // ... trim only non-excluded
}
```

**Files to Modify:**
- `src/OmenCoreApp/Services/MemoryOptimizerService.cs`
- `src/OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs`

**Status update:**
- [x] Added exclusion-aware working-set trim path using per-process `EmptyWorkingSet` instead of global trim when exclusions are configured.
- [x] Added default protected exclusions (`svchost`, `dwm`, `explorer`, `MsMpEng`, `OmenCommandCenterBackground`, `OmenCap`).
- [x] Added Memory Optimizer UI controls to add/remove exclusions and persist the list.
- [x] Persisted excluded process names in config and restored them on startup.

---

### **Enhancement #3: Real-Time Memory Compression** ✅ Done
**Priority:** LOW | **Effort:** 4 hours

**Current State:**
- Only reactive cleaning (trigger at threshold or interval)
- No memory compression; just trimming/flushing

**Enhancement:**
- Windows 10+ supports memory compression (MemoryCompression service)
- Could add compressed memory info to dashboard
- Offer option to enable/disable compression

**Note:** This is a system-wide setting; OmenCore would just provide UI toggle.

**Files to Modify:**
- `src/OmenCoreApp/Services/MemoryOptimizerService.cs` (new method SetMemoryCompression)

**Status update:**
- [x] Added memory compression state query support via `Get-MMAgent`.
- [x] Added memory compression enable/disable apply support via `Enable-MMAgent` / `Disable-MMAgent`.
- [x] Added Memory Optimizer UI status + toggle action with user-visible operation feedback.

---

### **Enhancement #4: Enhanced Memory Statistics Dashboard** ✅ Done
**Priority:** MEDIUM | **Effort:** 2.5 hours

**Current State:**
- Shows basic stats: Total, Used, Available, Load %
- Missing: Standby list, Modified page list, Compressed memory stats

**Improvements:**
1. Display breakdown:
   - Working Set vs Standby List vs File Cache
   - Compressed Memory (if enabled)
   - Virtual Memory / Page File usage
2. Historical graph (last 30 minutes) of memory trend
3. Top memory hogs list with context menu

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs`
- `src/OmenCoreApp/Views/MemoryOptimizerView.xaml`

**Status update:**
- [x] Added richer memory breakdown stats for standby list, modified page list, cache, commit/page-file summary, and compressed-memory readout when available.
- [x] Added a 30-minute RAM usage trend with a sparkline and summary statistics.
- [x] Added visible top memory hogs table with context actions to copy process name or open the executable location.

---

## 🎯 Additional Fixes Found (Audit Delta)

### **Fix #5: OSD DPI Scale vs Position Mismatch**
**Priority:** HIGH | **Effort:** 2 hours

**What was found:**
- OSD applies `LayoutTransform` scaling in `ApplyDpiAwareScale()`.
- `PositionWindow()` still calculates placement from unscaled `ActualWidth`/`DesiredSize`.
- On high DPI or large display scaling, calculated `Left/Top` can drift and clip edges.

**Action Plan:**
1. Compute effective scaled bounds before clamping (`effectiveWidth = width * scale`, same for height).
2. Reposition after transform settles (`Dispatcher.BeginInvoke` with Render priority).
3. Add telemetry logging for final bounds in debug mode.

**Files to Modify:**
- `src/OmenCoreApp/Views/OsdOverlayWindow.xaml.cs`

---

### **Fix #6: OSD Multi-Monitor Target Consistency** ✅ Done
**Priority:** MEDIUM | **Effort:** 2 hours

**What was found:**
- Monitor selection uses `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)`.
- During startup or after display topology changes, OSD may resolve to unintended monitor.

**Action Plan:**
1. Add explicit monitor target option in settings (`Primary`, `ActiveWindow`, `MouseCursor`).
2. Fallback chain: active display -> primary -> `SystemParameters.WorkArea`.
3. Re-evaluate target on display settings change events.

**Files to Modify:**
- `src/OmenCoreApp/Views/OsdOverlayWindow.xaml.cs`
- `src/OmenCoreApp/Models/AppConfig.cs`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- `src/OmenCoreApp/Views/SettingsView.xaml`

**Status update:**
- [x] Added explicit OSD monitor target selection: `Primary`, `ActiveWindow`, `MouseCursor`.
- [x] Persisted the target mode in `OsdSettings` and exposed it in Settings.
- [x] Updated OSD work-area resolution to use the selected target monitor instead of anchoring from the overlay window itself.
- `src/OmenCoreApp/Models/OsdSettings.cs`

---

### **Fix #7: Optimizer Restore Progress Visibility** ✅ Done
**Priority:** MEDIUM | **Effort:** 1.5 hours

**What was found:**
- Long-running optimizer operations only expose text status.
- No per-step progress percentage while restoring/applying all categories.

**Action Plan:**
1. Add operation-step progress model (`currentStep`, `totalSteps`, `stepName`).
2. Bind progress bar in `SystemOptimizerView` header and overlay.
3. Surface partial-failure summary with per-category status.

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs`
- `src/OmenCoreApp/Views/SystemOptimizerView.xaml`

**Shipped behavior:**
- Parsed staged optimizer status messages into concrete step progress in the view model.
- Added determinate progress bars and step text in both the optimizer header and busy overlay.
- Preserved indeterminate loading feedback for operations that do not emit staged progress.

---

### **Fix #8: Bloatware Bulk Remove Cancellation** ✅ Done
**Priority:** MEDIUM | **Effort:** 2 hours

**What was found:**
- `RemoveAllLowRiskAsync()` loops sequentially without user cancel.
- If one app hangs uninstall, user is blocked until completion.

**Action Plan:**
1. Add cancel token + `Cancel` button for bulk remove/restore.
2. Time-box each removal process and mark timeout reason.
3. Preserve already completed removals and log remaining pending items.

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`
- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs`

**Shipped behavior:**
- Added cancel support for bulk remove and bulk restore workflows in the Bloatware Manager UI.
- Time-boxed external uninstall and restore processes so hung operations are terminated and reported clearly.
- Preserved already completed items on cancel and marked remaining items as skipped in exported result logs.

---

### **Fix #9: Fan/Performance Decoupling Discoverability + Sticky Custom-Curve UX** ✅ Fixed in 3.3.0
**Priority:** HIGH | **Effort:** 2.5 hours

**Why this matters:**
- Reddit feedback confirms the decoupling fix solved a real pain point, but users only understand it after being burned by the old behavior.
- The feature exists, yet its state and consequences are still easy to miss.

**What was found:**
- `PerformanceModeService.LinkFanToPerformanceMode` is already opt-in and default-off.
- Status text exists in tray/dashboard, but the main fan/performance workflow still lacks a strong explanation of what happens when switching modes.
- Users with custom presets need clearer reassurance that mode switches will not silently replace their curve.

**Fix Applied:**
1. Added visible `Fan independent` / `Fan linked to performance` badges in Fan Control and System Control.
2. Added a one-time dismissible callout explaining that performance-mode changes no longer replace the active fan curve by default.
3. Added explicit copy in System Control and Quick Access clarifying when performance changes leave the fan preset untouched.
4. Added Quick Access tooltip/status messaging that preserves and names the active curve preset when decoupled mode is active.

**Files Modified:**
- `src/OmenCoreApp/Views/FanControlView.xaml`
- `src/OmenCoreApp/Views/SystemControlView.xaml`
- `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/Views/QuickPopupWindow.xaml.cs`
- `src/OmenCoreApp/Models/AppConfig.cs`

---

### **Fix #10: GPU OC Limit Visibility + Model-Specific Guardrails**
**Priority:** HIGH | **Effort:** 3 hours

**Why this matters:**
- Reddit request asked for `+250 MHz` core offset support on some RTX 5080 systems.
- Backend already supports higher caps and device-specific ranges via NVAPI, but the UI does not make the detected limit obvious enough.

**What was found:**
- `NvapiService.MaxCoreOffset` is already higher than `+250` in code.
- `SystemControlViewModel` starts with a conservative default max and updates when NVAPI initializes.
- Current UI copy still describes generic tuning guidance, not the detected per-GPU maximum/stability envelope.

**Improvements:**
1. Show detected GPU-specific OC limits directly next to sliders (`Detected max core offset: +300 MHz`, etc.). ✅ Shipped
2. Replace generic guidance with device-aware recommendations based on GPU family. ✅ Shipped
3. Add `Test Apply` mode that auto-reverts after timeout unless user confirms stability. ✅ Shipped
4. Add per-GPU presets (`Safe`, `Balanced`, `Max Experimental`) with guardrails for RTX 50-series laptops. ✅ Shipped
5. Ensure slider max updates clearly after NVAPI initialization to avoid the impression that the app is capped lower than the hardware allows. ✅ Shipped

**Status update:**
- Shipped in current pass: detected range labels in both NVIDIA tuning views, model-aware guardrail copy, explicit `Power Limit Only` messaging for NVAPI power-only systems, guardrail feedback when restored/profile-loaded values exceed the detected device range, a 30-second `Test Apply` auto-revert flow with explicit `Keep` confirmation, and generated `Safe` / `Balanced` / `Max Experimental` built-in profiles tuned to the detected GPU range.
- Fix #10 is now implementation-complete.

**Files to Modify:**
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/Views/SystemControlView.xaml`
- `src/OmenCoreApp/Views/TuningView.xaml`
- `src/OmenCoreApp/Hardware/NvapiService.cs`

---

### **Fix #11: Resume Recovery Verification + Diagnostics Timeline**
**Priority:** MEDIUM | **Effort:** 2.5 hours

**Why this matters:**
- Sleep/resume fan weirdness is a recurring class of regressions, even when individual bugs are fixed.
- Users need clearer evidence of what OmenCore did after wake if something still feels off.

**What was found:**
- Resume handling already exists across `HardwareMonitoringService`, `HardwareWatchdogService`, and `FanService`.
- Current behavior is safer than before, but there is no concise resume timeline surfaced to the user.

**Improvements:**
1. Add internal resume event timeline logging: suspend, watchdog disarm, monitoring resume, grace period start/end, fan preset reapply. ✅ Shipped
2. Expose a `Post-resume recovery status` card in diagnostics/settings. ✅ Shipped
3. Add a lightweight self-check 10-20 seconds after resume: verify fans are not pinned unexpectedly and telemetry resumed. ✅ Shipped
4. Include resume timeline in diagnostic export bundles. ✅ Shipped

**Status update:**
- Shipped in current pass: a shared resume recovery diagnostics timeline, service-level suspend/resume step recording across monitoring/watchdog/fan recovery, a 15-second post-resume self-check, a Settings Status card showing the latest recovery summary and timeline, and a `resume-recovery.txt` artifact in exported diagnostics bundles.
- Fix #11 is now implementation-complete.

**Files to Modify:**
- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- `src/OmenCoreApp/Services/HardwareWatchdogService.cs`
- `src/OmenCoreApp/Services/Diagnostics/DiagnosticExportService.cs`

---

### **Fix #12: Model Identity Confidence + Ambiguity Transparency ✅ Done**
**Priority:** MEDIUM | **Effort:** 2 hours

**Why this matters:**
- The 8BB1 conflict fix landed, but users only see the benefit indirectly.
- When model resolution depends on ambiguous IDs or pattern matching, support should be able to see the confidence level quickly.

**What was found:**
- Capability and keyboard DBs already disambiguate shared product IDs like `8BB1`.
- Diagnostic export includes trace output, but the UI doesn’t communicate whether identity resolution was exact, inferred, or ambiguous.

**Fix shipped:**
1. Added a shared identity-resolution summary builder so Settings and diagnostics export use the same model-capability and keyboard-profile resolution rules.
2. Added `Resolved Model`, `Resolution Source`, `Confidence`, keyboard profile details, and raw identity inputs to the Settings Status tab.
3. Added warning badge/callout states for inferred or fallback capability matches, plus a one-click `Copy Summary` action for issue reports.

**Files to Modify:**
- `src/OmenCoreApp/Services/Diagnostics/ModelIdentityResolutionSummary.cs`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- `src/OmenCoreApp/Views/SettingsView.xaml`
- `src/OmenCoreApp/Services/Diagnostics/DiagnosticExportService.cs`

---

## 🎨 GUI Visual Polish Backlog

### **Polish #1: Unify Typography + Icon Language ✅ Done**
**Priority:** HIGH | **Effort:** 2 hours

**What was found:**
- Mix of icon glyphs/emojis (example: memory cleaner buttons) and vector icons.
- Inconsistent typographic weight/size rhythm between Optimizer, Memory, and Bloatware views.

**Fix shipped:**
1. Replaced Memory Optimizer emoji labels with vector icon + text compositions for primary actions, section headers, and copy-result affordances.
2. Added shared utility typography, compact action button, icon button, and stat card styles in `ModernStyles.xaml` so Memory and Bloatware use the same visual rhythm.
3. Aligned status and stat badge presentation across Memory and Bloatware by reusing shared badge primitives instead of per-view one-off styling.

**Files to Modify:**
- `src/OmenCoreApp/Views/MemoryOptimizerView.xaml`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`
- `src/OmenCoreApp/Styles/ModernStyles.xaml`

---

### **Polish #2: Consolidate Duplicate Button/Toggle Styles ✅ Done**
**Priority:** HIGH | **Effort:** 2.5 hours

**What was found:**
- Local styles in individual views duplicate global theme styles (`PresetButton`, toggles, cards).
- Causes subtle color/spacing drift and harder maintenance.

**Fix shipped:**
1. Moved the remaining duplicated optimizer button, toggle, section-header, and card styles into `ModernStyles.xaml` as shared `Omen.*` keys.
2. Removed local button/toggle/card/header style definitions from `SystemOptimizerView.xaml`, leaving only view-specific templates local.
3. Switched System Optimizer, Memory Optimizer, and Bloatware Manager to the shared `Omen.Button.*`, `Omen.Toggle.*`, `Omen.Card.*`, and `Omen.Text.*` naming convention.

**Files to Modify:**
- `src/OmenCoreApp/Styles/ModernStyles.xaml`
- `src/OmenCoreApp/Views/SystemOptimizerView.xaml`
- `src/OmenCoreApp/Views/MemoryOptimizerView.xaml`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`

---

### **Polish #3: OSD Card Legibility + Density Modes ✅ Done**
**Priority:** MEDIUM | **Effort:** 2 hours

**What was found:**
- OSD can become dense with many enabled metrics and fixed font sizes.
- Border/background contrast is good, but line spacing is tight on scaled modes.

**Improvements:**
1. Add `Compact`, `Balanced`, `Comfortable` density modes.
2. Introduce metric grouping toggles (Thermals, Performance, Network, System).
3. Increase row spacing in `Comfortable` mode for readability.

**Files Modified:**
- `src/OmenCoreApp/Views/OsdOverlayWindow.xaml`
- `src/OmenCoreApp/Views/OsdOverlayWindow.xaml.cs`
- `src/OmenCoreApp/Models/AppConfig.cs` (`OsdSettings`)

---

### **Polish #4: Loading/Busy State Consistency ✅ Done**
**Priority:** MEDIUM | **Effort:** 2 hours

**What was found:**
- Different busy-state patterns across views (overlay, inline status text, progress bars).
- Inconsistent copy tone and visibility behavior.

**Improvements:**
1. Create reusable busy overlay style component.
2. Standardize status copy format (`Action...`, `Done`, `Failed: reason`).
3. Add non-blocking toast for success/failure where appropriate.

**Files Modified:**
- `src/OmenCoreApp/Styles/ModernStyles.xaml`
- `src/OmenCoreApp/Views/SystemOptimizerView.xaml`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`
- `src/OmenCoreApp/Views/MemoryOptimizerView.xaml`

---

### **Polish #5: Accessibility Pass (Keyboard + Focus + Contrast) ✅ Done**
**Priority:** MEDIUM | **Effort:** 3 hours

**What was found:**
- Some controls rely on color-only state cues.
- Focus visuals vary across custom buttons/toggles.
- Needs keyboard tab-order validation in utility-heavy pages.

**Improvements:**
1. Ensure visible focus ring on all interactive elements.
2. Add accessible names/tooltips for icon-only controls.
3. Validate contrast ratios for badges and tertiary text.

**Files Modified:**
- `src/OmenCoreApp/Styles/ModernStyles.xaml`
- `src/OmenCoreApp/Views/MemoryOptimizerView.xaml`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`

---

## 📊 Release Summary

### Release Scope
**Target:** Ship every item listed in this roadmap as part of 3.3.0.

**Out of scope only if blocked by hardware/API constraints:**
- Anything that proves impossible due to firmware or platform limitations after implementation attempts
- Items that require unsafe defaults without a clear guarded fallback

**Default assumption:**
- If it is in this document, it is intended for 3.3.0 and should be treated as release work, not backlog parking.

### Critical Fixes (Must Ship)
- [x] Bug #1: System Optimizer Freeze on Restore Defaults
- [x] Bug #2: Brightness Hotkey False Trigger
- [x] Bug #3: OSD Positioning Off-Screen

### High Priority (Must Ship)
- [x] Bug #4: Fan Auto Mode RPM Floor
- [x] Fix #5: OSD DPI Scale vs Position Mismatch
- [x] Bug #7: Undervolt/Curve Optimizer UX clarity
- [x] Fix #9: Fan/Performance Decoupling Discoverability + Sticky Custom-Curve UX
- [x] Fix #10: GPU OC Limit Visibility + Model-Specific Guardrails
- [x] Polish #1: Unify Typography + Icon Language
- [x] Polish #2: Consolidate Duplicate Button/Toggle Styles
- [x] Optimizer Enhancement #2: Revert All Stability
- [x] Bloatware Enhancement #2: Staged Removal with Rollback
- [x] RAM Cleaner Enhancement #1: Adaptive Auto-Clean Profiles

### Medium Priority (Still In Scope for 3.3.0)
- [x] Optimizer Enhancement #1: Risk Assessment
- [x] Optimizer Enhancement #3: State Verification
- [x] Bloatware Enhancement #1: AppX Detection Expansion
- [x] Bloatware Enhancement #3: Startup/Task Scanning
- [x] Bloatware Enhancement #5: Removal Preview Mode
- [x] Bloatware Enhancement #6: Pre-Removal Restore Point
- [x] Fix #6: OSD Multi-Monitor Target Consistency
- [x] Fix #7: Optimizer Restore Progress Visibility
- [x] Fix #8: Bloatware Bulk Remove Cancellation
- [x] Fix #11: Resume Recovery Verification + Diagnostics Timeline
- [x] Fix #12: Model Identity Confidence + Ambiguity Transparency
- [x] Polish #3: OSD Card Legibility + Density Modes
- [x] Polish #4: Loading/Busy State Consistency
- [x] Polish #5: Accessibility Pass
- [x] RAM Cleaner Enhancement #2: Process Exclusion List
- [x] RAM Cleaner Enhancement #4: Statistics Dashboard

### Lower Priority (Still Targeted for 3.3.0)
- [x] Optimizer Enhancement #4: Custom Profile Builder
- [x] Bloatware Enhancement #4: Removal Report History
- [x] RAM Cleaner Enhancement #3: Memory Compression UI

### Release Management Rule
- Lower priority here means implementation order, not release exclusion.
- These items only slip if a late blocker forces a scope tradeoff.

---

## 🗓️ Implementation Timeline

**Phase 1 (Week 1-2):** Core bug burn-down
- OSD positioning fix
- Fan control RPM floor
- Brightness hotkey filtering
- Restore Defaults UI freeze
- OSD DPI/scale placement validation
- Resume recovery verification groundwork

**Phase 2 (Week 3):** Fan, tuning, optimizer, and removal reliability
- Revert All async/await overhaul
- Staged bloatware removal
- Auto-clean profiles update
- UI typography and icon consistency pass
- Fan/performance decoupling UX clarification
- GPU OC limit messaging and test-apply flow

**Phase 3 (Week 4):** Remaining feature-quality pass
- Risk assessment UI
- AppX detection expansion
- Memory stats dashboard
- State verification
- Accessibility and busy-state consistency
- Resume diagnostics timeline and model-confidence UX

**Phase 4 (Week 5):** Finish remaining in-scope items
- Custom profile builder
- Removal report history
- Memory compression UI
- OSD density modes and multi-monitor consistency edge cases
- Final UI consistency sweep

**Phase 5 (Buffer / Validation):** Full regression and polish validation
- Cross-device regression testing
- Sleep/resume validation pass
- GPU OC validation on multiple NVIDIA generations
- Optimizer and bloatware rollback testing
- Final release-note/changelog prep

---

## ✅ Ship Criteria

3.3.0 should not ship until all of the following are true:

- All Critical and High Priority items are implemented and verified
- Medium and Lower Priority roadmap items are either implemented or explicitly marked blocked with documented reason
- No known UI freeze remains in optimizer, removal, resume, or tuning flows
- OSD placement and scaling issues are verified across common DPI/resolution combinations
- Fan/performance behavior is obvious in the UI and no longer surprising to users
- GPU tuning UI reflects actual hardware capabilities and safe-apply behavior
- Visual polish work is complete enough that the release feels cohesive, not partially refreshed

---

## 📝 Testing Checklist

### Bug Fixes Testing
- [ ] OSD visible on 1920x1080, 1920x1200, 2560x1440 displays
- [ ] OSD doesn't go off-screen in TopRight, BottomRight positions
- [ ] OSD TopCenter is horizontally centered
- [ ] OSD remains correctly placed after DPI scale changes (100%, 125%, 150%, 175%)
- [ ] OSD placement is correct on secondary monitor and after monitor unplug/replug
- [ ] Restore Defaults completes without freeze (time < 10s)
- [ ] Brightness hotkey doesn't launch OmenCore
- [ ] Fan auto mode goes to 0 RPM at idle temps
- [ ] Performance-mode switch leaves custom fan curve untouched when decoupled
- [ ] UI clearly indicates whether fan/performance is linked or decoupled
- [ ] GPU OC sliders show detected device-specific max values after NVAPI initialization
- [ ] RTX 50-series systems can use supported offsets above +200 when hardware allows
- [ ] Post-resume diagnostics timeline records watchdog/monitoring/fan recovery sequence
- [ ] Test on HP OMEN 16 xd0xxx with BIOS F.31+

### Optimizer Testing
- [ ] Revert All timeout triggers after 60s
- [ ] Partial reverts gracefully handle errors
- [ ] Registry backup/restore doesn't corrupt keys
- [ ] State verification detects external changes

### Bloatware Testing
- [ ] New HP OMEN apps detected in database
- [ ] Staged removal rolls back on failure
- [ ] Removal logs persist between sessions
- [ ] Scheduled task scanning finds bloatware tasks

### RAM Cleaner Testing
- [ ] Auto-clean profiles apply correct intervals/thresholds
- [ ] Process exclusion prevents dwm.exe trim
- [ ] Historical memory graph displays correctly
- [ ] Memory compression toggle works (if Win10+)

### Visual Polish Testing
- [ ] No emoji glyph fallback in action buttons (vector icon + text only)
- [ ] Shared button/toggle styles render consistently across Optimizer, Memory, and Bloatware views
- [ ] Keyboard navigation reaches all controls with visible focus indicators
- [ ] Loading overlays/status messages follow unified copy and behavior

---

## 📚 References

- Issue #100: https://github.com/theantipopau/omencore/issues/100
- Previous Roadmap: docs/ROADMAP_v2.0.md
- Optimization Opportunities: docs/OPTIMIZATION_OPPORTUNITIES_v3.0.1.md

---

**Document Version:** 1.1  
**Last Updated:** 2026-03-31  
**Author:** OmenCore Team  
**Status:** DRAFT → Ready for Review
