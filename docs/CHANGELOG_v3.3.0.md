# OmenCore v3.3.0 — Release Notes

**Version:** 3.3.0  
**Release Date:** 2026-04-14  
**Release Status:** ✅ Released  
**Previous Release:** v3.2.5 (2026-03-30)  
**Hotfix Backport:** Includes all v3.2.6 fan-stability fixes

---

## 📦 Artifacts

| File | Platform | SHA256 |
|------|----------|---------|
| `OmenCoreSetup-3.3.0.exe` | Windows Installer | `483D4CAEB66DF3923F6152ECE98B128F3F9A0B3A2E0A5CE42403C43BB9F12D9E` |
| `OmenCore-3.3.0-win-x64.zip` | Windows Portable | `F07B9435DCCB2771672BDE0E44CC2A2B980859AC0A576B8E6BCCC93A61D064C9` |
| `OmenCore-3.3.0-linux-x64.zip` | Linux (CLI + GUI) | `AD5A2C37583EB9D8E486431B8AD9F7BEFBA2847325F5F21C4ABFB0B63C04AA1C` |

→ **[View on GitHub Releases](https://github.com/theantipopau/omencore/releases/tag/v3.3.0)**

---

## Overview

v3.3.0 is the largest stability-and-polish release since v3.1.0. It ships every identified bug fix from v3.2.5 community reports, a comprehensive visual quality pass across OSD and Quick Access, RGB subsystem hardening, runtime performance optimizations, and several long-requested features from Discord and GitHub.

All items in this changelog are **✅ shipped** in the v3.3.0 release.

---

## 🚨 Critical Bug Fixes

### Monitoring Halt / Watchdog Failsafe Via Cross-Thread Exception ✅ Done
**Severity:** CRITICAL | **Found via runtime log analysis (v3.3.0 pre-release)**

OmenCore could halt hardware monitoring and trigger the watchdog failsafe fan speed (100%) mid-session. The symptom was fans ramping to 100% suddenly, log entry `🚨 WATCHDOG: Temperature monitoring frozen for Ns — applying failsafe fan speed`, and repeated `UnobservedTaskException` → `InvalidOperationException: The calling thread cannot access this object because a different thread owns it` in the log from 10–30 s earlier.

**Root Cause — two compounding issues:**

1. **`LightingViewModel.OnMonitoringSampleUpdated` called without dispatcher marshal.** The `SampleUpdated` event fires on the hardware monitoring background thread. `OnMonitoringSampleUpdated` immediately called `ApplyTemperatureBasedLighting(sample)` and `ApplyThrottlingLighting(sample)` without dispatching to the UI thread first. Both are `async void` methods that access WPF-bound properties (`CpuTempThresholdHigh`, `TempHighColorHex`, `_keyboardLightingService.IsAvailable`, etc.) before their first `await` — all requiring the dispatcher thread. This threw `InvalidOperationException` on every monitoring tick when temperature-responsive or throttling lighting was enabled.

2. **`SampleUpdated` multicast invoke was not subscriber-isolated.** The event was fired as `SampleUpdated?.Invoke(this, sample)` — a single multicast call. An exception thrown by any subscriber (here `LightingViewModel`) propagated up through the monitoring loop's catch block, which swallowed it but also meant `MainViewModel`'s subscriber — the one that calls `_watchdogService.UpdateTemperature()` — was never reached. With watchdog heartbeat starved, the default 90 s freeze threshold triggered failsafe fans.

**Enhancement #31 gap:** The 3.3.0 dispatcher-coalescing work (`QueueMonitoringUiSample` in `MainViewModel`, `BeginInvoke` in `DashboardViewModel` and `SystemControlViewModel`) correctly patched three of the four `SampleUpdated` subscribers. `LightingViewModel` was inadvertently missed in that pass.

**Fixes:**
- `LightingViewModel.OnMonitoringSampleUpdated` now wraps its body in `Application.Current?.Dispatcher?.BeginInvoke(...)`, matching the pattern used by all other subscribers. WPF-bound property accesses and service availability checks now execute on the dispatcher thread before any hardware I/O awaits.
- `HardwareMonitoringService` `SampleUpdated` fan-out now iterates `GetInvocationList()` with an individual try/catch per handler. A failed subscriber logs `[MonitorLoop] SampleUpdated subscriber failed (ClassName.MethodName)` and is skipped; the remaining subscribers — including `MainViewModel`'s watchdog heartbeat — always execute regardless of any one handler's outcome.

**Files:** `ViewModels/LightingViewModel.cs` — `OnMonitoringSampleUpdated`; `Services/HardwareMonitoringService.cs` — `SampleUpdated` fan-out

---

### Fan Curve Stops Working After First Save ✅ Done
**Severity:** CRITICAL | **Reported by:** Ryua (HP OMEN Gaming Laptop 16-ap0xxx, v3.2.5)

Custom fan curve presets became permanently non-functional after the first application when OGH was running alongside OmenCore, or when fans were already near the target speed at idle. Root cause: `VerificationPasses()` required a ≥50 RPM change within 800 ms of applying a preset. If fans were already at-target duty cycle, no detectable RPM change → false failure → rollback → curve engine disabled forever.

**Fix:** `FanService.VerificationPasses()` now short-circuits the RPM-change check for curve-based presets. Static threshold logic is only applied to absolute-mode presets where immediate RPM movement is expected.

---

### Fan Verification Service Destroys Fan State on Failure ✅ Done
**Severity:** CRITICAL | **Root cause of the "curve freezes" symptom**

`FanVerificationService` was calling `SetFanMode(Default)` when its post-apply diagnostic check failed, forcibly resetting the fan to BIOS auto mode regardless of what the user had configured. Any telemetry lag or OGH interference caused the verification to fail silently and silently kill the active curve.

**Fix:** Removed the destructive `SetFanMode(Default)` call from the failure path. On diagnostic failure, the service now logs a warning only; it no longer touches fan control state.

---

### OmenCore Freezes on "Restore Defaults" ✅ Done
**Severity:** CRITICAL | **Reported by:** OsamaBiden (HP OMEN 16 xd0xxx) — GitHub #100 Bug #1

Pressing "Restore Defaults" in System Optimizer causes OmenCore to become fully unresponsive, requiring a Task Manager end-task. A PowerShell window spawns briefly then freezes.

**Root Causes:**
1. `KeyboardLightingService.RestoreDefaults()` — when the V2 keyboard service is active — called `.GetAwaiter().GetResult()` on async V2 methods from the WPF UI thread. With the WPF SynchronizationContext, async continuations scheduled after `await` need the UI thread to resume, but the UI thread was blocked in the `.GetResult()` call → permanent deadlock.
2. `ApplySchedulerTweak()` launched a PowerShell subprocess without `CreateNoWindow = true`, causing a briefly-visible console window.
3. `RevertAllAsync()` had no timeout, no per-step progress, and `StatusChanged` callbacks fired directly without dispatcher marshaling (potential cross-thread INPC fault).

**Fix:**
- V2 keyboard restore path is now dispatched as a fire-and-forget `Task.Run(async () => { await ... .ConfigureAwait(false); })`, removing the blocking call from the UI thread entirely.
- `ApplySchedulerTweak()` adds `CreateNoWindow = true` to suppress the PowerShell console.
- `RevertAllAsync()` accepts a `CancellationToken` with a 60-second timeout in `SystemOptimizerViewModel`.
- Per-step `StatusChanged` events emitted between each of the six optimizer stages with accurate "Reverting power settings…" / "Re-enabling services…" etc. messages.
- All `StatusChanged` callbacks are marshaled via `Application.Current?.Dispatcher?.BeginInvoke()` to prevent cross-thread property-change exceptions.

**Files:** `Services/KeyboardLightingService.cs`, `Services/SystemOptimizationService.cs`, `Services/SystemOptimizer/SystemOptimizerService.cs`, `ViewModels/SystemOptimizerViewModel.cs`

---

### Bloatware Manager Remove/Restore Buttons Non-Functional ✅ Done
**Severity:** CRITICAL | **Found during v3.3.0 pre-release review**

The **Remove** and **Restore** buttons in Bloatware Manager were permanently disabled after a scan completed, even after selecting an app. Clicking them had no effect.

**Root Cause:** `BloatwareManagerViewModel.SelectedApp` setter raised `OnPropertyChanged(nameof(CanRemoveSelected))` and `OnPropertyChanged(nameof(CanRestoreSelected))` — property-change notifications on computed bool properties. WPF `ICommand`-bound buttons (`RemoveSelectedCommand`, `RestoreSelectedCommand`) only re-evaluate their enabled state when `ICommand.CanExecuteChanged` fires; `PropertyChanged` on a separate property is ignored for button enablement. The commands never received a `CanExecuteChanged` signal after scan completion, so the buttons stayed disabled regardless of selection state.

**Fix:** `SelectedApp` setter now calls `RaiseCommandStates()`, which invokes `RaiseCanExecuteChanged()` on all relevant commands — the same pattern used by `IsScanning`, `IsProcessing`, and `IsRestoring` setters in the same ViewModel.

**File:** `ViewModels/BloatwareManagerViewModel.cs` — `SelectedApp` property setter

---

## 🔴 High-Priority Bug Fixes

### UI Freeze When Switching Fan Presets ✅ Done
**Severity:** HIGH | **Multiple user reports**

`FanService.ApplyPreset()` was called synchronously on the WPF UI thread. `WmiFanController` contains multiple `Thread.Sleep()` calls during fan command delivery and verification, stalling the UI thread for 200–800 ms per switch. This caused the dropdown to visibly freeze and the whole window to become unresponsive during preset changes.

**Fix:** All `ApplyPreset()` calls from `FanControlViewModel` are now dispatched to a background thread via `Task.Run()`, with UI state updates posted back via `Dispatcher`. The hotkey handler and preset dropdown setter are both updated.

---

### Additional UI-Thread Fan Control Paths Blocking ✅ Done
**Severity:** HIGH | **Found during pre-release audit**

Three further synchronous-on-UI-thread fan control paths were identified after the initial 3.3.0 fix pass:

1. **`SaveCustomPreset()`** — the "Save & Apply" button called `_fanService.ApplyPreset()` directly on the UI thread immediately after setting `SelectedPreset`. Since setting `SelectedPreset` already queues the async apply via `ApplyPresetAsync → Task.Run`, this was a duplicate blocking WMI call that raced the background task. Removed.

2. **`ReapplySavedPresetAsync()`** — despite the name, every code path (`ApplyPreset`, `ApplyMaxCooling`, `ApplyAutoMode`, `ApplyQuietMode`) called the fan service synchronously and returned `Task.CompletedTask`. `WmiFanController.SetFanSpeed()` has up to 1.5 s of `Thread.Sleep` retry logic, so this could freeze the UI on the "Reapply" button. Converted to a true `async Task` with `await Task.Run(...)` wrapping every service call.

3. **`ApplyConstantSpeed()`** — `_fanService.ForceSetFanSpeed()` was called synchronously both from the Constant mode button and from the `ConstantFanPercent` property setter on every slider tick. Added `Task.Run` dispatch with a `volatile bool _isApplyingConstantSpeed` guard to drop in-flight duplicates during slider drag.

**File:** `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`

---

### CPU Temperature Frozen After Wake From Sleep ✅ Done
**Severity:** HIGH | **Reported by:** xenon205 (OMEN 17-ck1xxx, v3.2.5) — GitHub #102

On models that use the worker-backed CPU temperature source (OMEN 17-ck1xxx, ck2xxx, 16-xd0, OMEN MAX 16), CPU temperature became frozen at the last pre-sleep reading (typically 44°C) immediately after waking from sleep. The only workaround was to fully restart OmenCore via Task Scheduler.

**Root Cause:** The hardware worker process (OmenCore.HardwareWorker.exe) can exit during sleep — either from the 5-minute orphan timeout or because Windows terminates background processes during suspend. `HardwareMonitoringService.RecoverAfterResumeAsync()` calls `TryRestartAsync()`, but `TryRestartAsync()` was only resetting NVAPI failure state. The cached `LibreHardwareMonitorImpl` instance kept returning data from the dead IPC channel (the stale pre-sleep value), and `EnsureTempFallbackMonitor()` returned the broken cached instance instead of creating a fresh one.

**Fix:** `TryRestartAsync()` now:
1. Disposes and nulls `_tempFallbackMonitor` — forces a fresh IPC connection on next poll
2. Resets `_tempFallbackInitAttempted = false` — allows `EnsureTempFallbackMonitor()` to re-initialize
3. Resets `_workerPrelaunchAttempted = 0` and calls `TryPrelaunchHardwareWorker()` — relaunches the worker process if it died

**File:** `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` — `TryRestartAsync()`

---

### Fans Remain at 100% After Switching from Performance to Balanced/Quiet ✅ Done
**Severity:** HIGH | **Reported by:** xenon205 (OMEN 17-ck1xxx, v3.2.5) — GitHub #102

On V1 BIOS models (MaxFanLevel=55, krpm scale — e.g. OMEN 17-ck1xxx), switching the fan preset from Performance back to Balanced or Quiet left fans running at full speed. The fan diagnostics page workaround (manually set to level ≥15) confirmed the WMI hardware path works but the preset-switch code path did not.

**Root Cause:** The Max-preset path uses a full `ResetFromMaxMode()` multi-step sequence: `SetFanMax(false)` → `SetFanMode(Default)` → `SetFanLevel(20,20)` (V1 transition hint). The Performance preset only sent `SetFanMode(mode)`. On V1 systems, `SetFanMode(Default)` is accepted by the BIOS but doesn't actually trigger a hardware fan ramp-down from a Performance state without an explicit duty-cycle write following it.

**Fix:** After `SetFanMode(Default)` succeeds for a transition from Performance to Default/Auto mode, a `SetFanLevel(20,20)` kick is now emitted on V1 systems only (guarded by `_maxFanLevel < 100`). This mirrors `ResetFromMaxMode()`'s step-3 hint and gives the EC a concrete fan target to ramp from.

**File:** `src/OmenCoreApp/Hardware/WmiFanController.cs` — `ApplyPreset()`

---

### OSD Positioned Off-Screen at TopRight / BottomRight / BottomLeft ✅ Done
**Severity:** HIGH | **Reported via Discord and internal testing**

OSD overlays positioned at TopRight or BottomRight were fully off-screen on systems with non-100% DPI scaling (e.g. 125%, 150%). BottomLeft and BottomCenter were ~90% off-screen.

**Root Cause:** `GetCurrentMonitorWorkArea()` called `GetMonitorInfo()` which returns physical pixel coordinates (e.g. 1920 × 1080 at 1.5× DPI). However, WPF's `Left` and `Top` properties work in logical units (1280 × 720 at 1.5× DPI). The mismatch placed the OSD 640 logical pixels off the right edge of the screen.

**Fix (shipped):** `GetCurrentMonitorWorkArea()` now divides physical pixel coordinates by the per-monitor DPI scale obtained from `PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice`. Fallback to `SystemParameters.WorkArea` (which is already DPI-aware) is retained.

---

### Brightness Hotkey Triggers OmenCore to Open ✅ Done
**Severity:** HIGH | **Reported by multiple users** — GitHub #100 Bug #2

On HP OMEN 16 xd0xxx (AMD / Ryzen AI), pressing Fn+brightness (F6/F7) caused OmenCore to open or switch modes, exactly as if the OMEN button had been pressed.

**Root Cause:** On affected models, Fn+brightness emits a `VK_LAUNCH_APP1` or `VK_LAUNCH_APP2` key event with hardware scan code `0xE046`. Because `0xE046` is in `OmenScanCodes` — the set of known OMEN hardware button scan codes — `OmenKeyService.IsOmenKey()` matched these events and triggered the OMEN key action. The `F1–F23` VK-based never-intercept path does not cover `VK_LAUNCH_APP1/APP2`.

**Fix:** An explicit `scanCode == 0xE046` early-exit guard is now inserted before the `OmenScanCodes` membership check for both the `VK_LAUNCH_APP1` and `VK_LAUNCH_APP2` branches inside `IsOmenKey()`. The rejection is logged under reason `brightness-key-conflict-scan-e046` for diagnostics. `0xE046` remains in `OmenScanCodes` for extended-OMEN-button detection which uses a different code path.

**File:** `Services/OmenKeyService.cs` — `IsOmenKey()`

---

### Fan Doesn't Reach 0 RPM in Auto Mode ✅ Done
**Severity:** MEDIUM | **Reported via Discord** — GitHub #100 Bug #4

On V1 BIOS OMEN models (MaxFanLevel=55, krpm scale), fans stayed at a minimum of ~2000 RPM in Balanced/Quiet auto mode even at full idle, despite the BIOS EC being capable of 0 RPM when no manual duty is set.

**Root Cause:** The `SetFanLevel(20, 20)` transition kick added for Issue #102 (fans stuck after Performance→Default) left a manual duty-cycle floor registered with the EC. After `SetFanMode(Default)` was issued, the EC entered auto-thermal-management mode but honored the residual manual level 20 as a minimum — fans could not spin below ~2000 RPM regardless of load.

**Fix:** A `SetFanLevel(0, 0)` call is now emitted immediately after `SetFanMode(Default)` succeeds on V1 systems (guarded by `_maxFanLevel < 100`). Duty level 0 in auto mode is interpreted by the V1 EC as "BIOS decides" rather than "hard stop to 0 RPM", clearing the manual floor. This is applied in both `ApplyPreset()` (for preset switching) and `RestoreAutoControl()` (for the diagnostics-panel manual reset path). A 25–50 ms delay is added before the `SetFanLevel(0,0)` call to let the EC process the preceding `SetFanMode(Default)` command.

**File:** `Hardware/WmiFanController.cs` — `ApplyPreset()`, `RestoreAutoControl()`

---

## 🟡 Fan and Thermal Improvements

### Fan Name Normalization (G/GP Labels) ✅ Done
**Affected hardware:** AMD OMEN laptops (Ryzen AI / Strix Point)

LibreHardwareMonitor surfaces ACPI-internal sensor labels such as `G` (GPU die) and `GP` (GPU Package) as visible fan names in OmenCore's UI. These are opaque internal identifiers, not user-facing labels.

**Fix:** `WmiFanController.NormalizeFanName()` maps known ACPI shorthand labels to human-readable equivalents (`G` → `CPU Fan`, `GP` → `GPU Fan`, etc.) before they reach any UI surface.

---

### Fan Curve Post-Apply Kick Re-enabled on Slow Telemetry ✅ Done

`RunCurveVerificationKickAsync()` now correctly re-enables the curve on completion even on models where RPM readback temporarily shows low values due to BIOS telemetry confirmation-counter delays. Previously the kick could be suppressed by the same false-failure condition as the primary curve bug.

---

## 🖥️ OSD (In-Game Overlay) Improvements

### OSD Positioning DPI Fix ✅ Done
Shipped as part of the positioning overhaul described in Bugs above. All six OSD anchor positions (TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight) now land correctly on displays with any DPI scale including 125%, 150%, and 175%.

### OSD Visual Polish ✅ Done
The OSD overlay has received a visual quality pass to bring it in line with the Hotkey OSD's premium appearance:

- **Background:** Replaced solid `#DD0A0A14` with a directional `LinearGradientBrush` (`#F0141416` → `#F01A1A20`) that avoids the flat "screenshot" look
- **Border:** Increased opacity from `#88FF005C` to `#BBFF005C` for a crisper OMEN-red accent ring; thickness reduced from `2px` to `1.5px` for cleaner rendering at all DPI
- **Corner radius:** Increased from `8` to `10` for a softer, more modern card shape
- **Drop shadow:** Moved from a flat 10-blur depth-2 shadow to a softer 24-blur depth-4 elevation shadow that lifts the OSD off the game background more convincingly; opacity increased to 0.65

### OSD Multi-Monitor Target Selection ✅ Done

The overlay can now target a specific monitor resolution mode instead of always resolving from its own current window placement.

**Shipped behavior:**
- Added monitor target modes: `Primary`, `ActiveWindow`, and `MouseCursor`.
- Added persistent target-monitor selection in Settings.
- Updated monitor work-area resolution so the overlay follows the selected target mode on startup and after settings changes.

### Optimizer Restore Progress Visibility ✅ Done

System Optimizer restore/apply workflows now show real step progress instead of only a generic busy state when the service emits staged status updates.

**Shipped behavior:**
- Parsed staged optimizer messages like `[1/6]` and `[3-6/6]` into determinate progress state.
- Added visible step progress text and determinate progress bars in the optimizer header and loading overlay.
- Kept the existing indeterminate loading state for actions that do not report staged progress.

### Bloatware Bulk Cancel + Timeout Guardrails ✅ Done

Bloatware bulk actions no longer force users to wait indefinitely when an uninstall or restore process hangs.

**Shipped behavior:**
- Added cancel support for both bulk remove and bulk restore flows directly in the Bloatware Manager toolbar.
- Time-boxed external uninstall and restore processes, terminating hung commands after a fixed timeout and surfacing the reason in results.
- Preserved already completed items on cancellation and exported skipped remaining items with explicit reasons in the removal log.

### OSD Horizontal Layout Support ✅ Done
Users can now explicitly switch the in-game OSD between vertical and horizontal card flow from Settings.

**Shipped behavior:**
- Exposed OSD layout selection in Settings via the `Horizontal Layout` toggle.
- Persists layout mode in config and applies it live to the OSD overlay panel orientation.

### OSD Metrics: GPU Hotspot Real Sensor ✅ Done
GPU hotspot now prefers the real junction temperature sensor from monitoring samples instead of always using an estimated offset.

**Shipped behavior:**
- OSD hotspot metric now uses `MonitoringSample.GpuHotspotTemperatureC` when available.
- Retains estimated fallback (`GPU core + 12°C`) only when a hotspot sensor is unavailable.

---

## ⚡ Quick Access Popup Improvements

### Fan Mode Button Order Corrected ✅ Done
**Requested by:** snowfall hateall (Discord)

Fan mode buttons in the Quick Access popup were in an arbitrary order: `Auto | Max | Quiet | Custom`. They now follow an intuitive ascending-power order matching the Performance Mode row's pattern:

**New order:** `Quiet 🤫 | Auto ⚡ | Curve 🎛️ | Max 🔥`

This matches the visual language of competitor tools (g-helper, ASUS Armoury) and is immediately readable — left is coolest/quietest, right is hottest/fastest.

### "Custom" Renamed to "Curve" ✅ Done
The Custom fan mode button in Quick Access is renamed to **Curve** to make clear it applies the user's configured fan curve from the OMEN Fan tab rather than an unnamed custom mode. The Tag stays `Custom` for backward compatibility with the fan mode routing logic.

### Quick Access: Active Curve Preset Tooltip ✅ Done
The **Curve** button in Quick Access now shows a tooltip with the name of the currently active fan curve preset, e.g. "Curve: Gaming Profile". When no named preset is active, the tooltip reads "Apply saved fan curve". `ToolTipService.ShowOnDisabled="True"` is set so the tooltip is visible even when the button is in its disabled/selected state.

### Fan/Performance Decoupling UX Clarity ✅ Done
Fan control was already decoupled from performance switching by default, but that state was too easy to miss in the main workflow.

**Fix shipped:**
- Added visible `Fan independent` / `Fan linked to performance` badges in Fan Control and System Control
- Added a one-time dismissible migration/info callout explaining that performance-mode changes no longer replace the active fan curve by default
- Added explicit System Control and Quick Access copy clarifying when performance-mode changes leave the fan preset untouched
- Quick Access now reuses the active curve preset name in decoupled-mode messaging so users know what stays active

### GPU OC Limit Visibility + Guardrails ✅ Done
GPU tuning already respected device-specific NVAPI caps internally, but the UI still looked like it was hard-capped to conservative defaults and did not clearly distinguish full clock-offset tuning from power-only systems.

**Fix shipped:**
- Added detected core, memory, and power-limit range labels directly alongside NVIDIA sliders in both System Control and Tuning
- Added device-aware guardrail copy that adjusts messaging for laptop GPUs and RTX 50-series laptop models
- Added explicit `Power Limit Only` capability messaging when NVAPI exposes power tuning but clock offsets remain blocked
- Added guardrail feedback when restored startup values or saved profiles exceed the currently detected device range
- Added a 30-second `Test Apply` flow that automatically restores the prior GPU state unless the user presses `Keep`
- Replaced the static built-in GPU profiles with generated `Safe`, `Balanced`, and `Max Experimental` tiers based on the detected per-device range
- GPU OC profiles now carry voltage offsets explicitly, and NVAPI power-only systems now restore saved power limits on startup instead of silently skipping the restore path

### Resume Recovery Verification + Diagnostics Timeline ✅ Done
Sleep/resume hardening existed across monitoring, watchdog, and fan control, but there was no single recovery timeline visible to the user and no concise post-resume verdict.

**Fix shipped:**
- Added a shared resume recovery timeline that records suspend detection, watchdog grace-window state, monitoring resume/bridge refresh, and fan reapply actions
- Added a `Post-resume recovery` status card to Settings so users can see the latest recovery summary and raw timeline without opening logs
- Added a 15-second post-resume self-check that flags stale telemetry recovery or unexpectedly pinned fans
- Added `resume-recovery.txt` to diagnostics bundle exports for issue reports

### Model Identity Confidence + Ambiguity Transparency ✅ Done
Capability and keyboard databases already handled shared or inferred matches such as the `8BB1` conflict, but that resolution quality was only visible indirectly in logs and diagnostic exports.

**Fix shipped:**
- Added a shared identity-resolution summary builder so Settings and exported diagnostics use the same model-capability and keyboard-profile resolution logic
- Added a `Model Identity Resolution` card to Settings with resolved model, source, confidence, keyboard profile details, and raw identity inputs
- Added inferred/fallback warning states and a one-click `Copy Summary` action for issue reports
- Expanded the exported identity trace with explicit confidence and attention text so support can see exact vs inferred vs fallback paths quickly

### Unify Typography + Icon Language ✅ Done
Memory and Bloatware had drifted into a mix of emoji labels, private utility button styles, and inconsistent stat/badge typography even though the app already had a shared visual language available.

**Fix shipped:**
- Replaced Memory Optimizer emoji action labels and section headings with vector icon + text compositions
- Added shared utility stat-card, compact icon-button, action-button, and section-title styles in `ModernStyles.xaml`
- Updated Bloatware stat tiles and badge containers to use the same typography and badge rhythm as the refreshed utility views

### Consolidate Duplicate Button/Toggle Styles ✅ Done
System Optimizer still carried its own local toggle, preset-button, section-header, and card styles even after the utility views had started moving toward shared styling, which kept spacing and interaction behavior drifting over time.

**Fix shipped:**
- Added shared `Omen.Button.*`, `Omen.Toggle.*`, `Omen.Card.*`, and `Omen.Text.*` style keys in `ModernStyles.xaml` for optimizer and utility surfaces
- Removed duplicated local button/toggle/card/header styles from `SystemOptimizerView.xaml`
- Switched System Optimizer, Memory Optimizer, and Bloatware Manager to the shared named styles so the three views now inherit the same control rhythm from one place

### OSD Card Legibility + Density Modes ✅ Done
The overlay already had stronger contrast, but dense metric stacks still felt tight at higher information density and there was no way to quickly hide entire metric categories.

**Fix shipped:**
- Added `Compact`, `Balanced`, and `Comfortable` OSD density modes and exposed them in Settings
- Added metric group toggles for `Thermals`, `Performance`, `Network`, and `System` in OSD settings
- Updated overlay row/separator spacing and panel scale to reflect selected density, with a readability-first spacing bump in `Comfortable`
- Updated OSD visibility logic so metric rows and separators respect both per-metric toggles and active group toggles

### Loading/Busy State Consistency ✅ Done
Utility-heavy pages had drifted into different loading overlays and status copy conventions, which made long operations feel inconsistent and harder to parse.

**Fix shipped:**
- Added shared `Omen.Busy.*` and `Omen.Toast` styles in `ModernStyles.xaml` for consistent in-view loading overlays and non-blocking status feedback
- Switched System Optimizer, Bloatware Manager, and Memory Optimizer to the shared busy overlay visuals
- Standardized operation/status copy across the three related viewmodels to a common pattern: `Action...`, `Done`, and `Failed: reason`

### Accessibility Pass (Keyboard + Focus + Contrast) ✅ Done
Utility views had inconsistent keyboard focus affordances and a few low-contrast/metadata gaps that reduced accessibility confidence.

**Fix shipped:**
- Added a shared `Omen.FocusVisual` focus ring style and wired it into core button/toggle/radio style primitives in `ModernStyles.xaml`
- Added explicit automation metadata for icon-only utility actions (for example, Memory Optimizer's copy-last-result button)
- Improved utility-view text contrast in low-visibility areas (including search hint text and metric-label styling)

### Quick Access: OMEN Fan Curve Integration ✅ Done
Pressing **Curve** in Quick Access now resolves to a real saved fan curve preset instead of treating `Custom` as a generic mode label.

**Shipped behavior:**
- Routed Quick Access `Custom` clicks through preset resolution and the same preset-apply path used by fan presets.
- Prefers the currently active named custom preset first, then falls back to the most recent available saved custom curve.
- Preserves clear Quick Access tooltip/status messaging so users know which curve preset will be applied.

### Quick Access: Refresh Rate Button Per-Display ✅ Done
Quick Access refresh-rate control no longer assumes a single primary-only target on multi-monitor systems.

**Shipped behavior:**
- Added per-display refresh-rate operations in `DisplayService` using display device targeting.
- Quick Access now cycles the refresh-rate target across connected displays after each click.
- The button text/tooltip now shows which display target is currently active for the next toggle.

---

## 🌈 RGB and Lighting Improvements

### Enhancement #13: RGB Provider Transparency Improvements ✅ Done
The RGB settings UI currently shows a flat list of providers with no indication of connection state. Users cannot tell if a provider is silently failing.

**Shipped behavior:**
- Added `RgbProviderConnectionStatus` enum (`Connected`, `NoDevices`, `Disabled`, `Error`) and `StatusDetail` string to `IRgbProvider` interface.
- Implemented both properties in all six providers (`RazerRgbProvider`, `LogitechRgbProvider`, `CorsairRgbProvider`, `OpenRgbProvider`, `SystemRgbProvider`, `RgbNetSystemProvider`), with `_initFailed`/`_initError` tracking in init catch blocks.
- Added `GetProviderStatus()` / `StatusToBrush()` helpers to `LightingViewModel` exposing `CorsairStatusBrush`, `CorsairStatusDetail`, `LogitechStatusBrush`, `LogitechStatusDetail`, `RazerStatusBrush`, `RazerStatusDetail` computed properties.
- Updated all connection status badges in `LightingView.xaml` (header row + per-provider section headers) to bind to the new brush/detail properties and show for all enabled providers, not just connected ones.

### Enhancement #14: Razer Chroma Provider Hardening ✅ Done
The Razer Chroma SDK connection can silently fail if Synapse is not running at startup.

**Shipped behavior:**
- Added exponential back-off reconnect to `RazerService`: delays `[1 s, 2 s, 5 s, 30 s, 5 min]` on consecutive failures.
- `SendHeartbeatAsync()` now calls `OnSessionLost()` on failure, which schedules the reconnect timer.
- `TryReconnectAsync()` attempts `InitializeSessionAsync()`, restarts the heartbeat on success, increments back-off index on failure.
- Reconnect timer is properly disposed in `Dispose()`.
- `ConnectionStatus` / `StatusDetail` on `RazerRgbProvider` now surface reconnect state to the UI badges (Enhancement #13).

### Enhancement #15: Logitech GHUB Reliability Pass ✅ Done
GHUB's plugin API occasionally drops connection during profile switches, causing lighting to revert to GHUB default.

**Shipped behavior:**
- Added `EnsureConnectionAsync()` helper to `LogitechRgbProvider`.
- Before each effect apply, if more than 30 s have passed since the last health check, `DiscoverAsync()` is re-run; if all devices disappear, a full `InitializeAsync()` reconnect is triggered.
- The reconnect-in-progress flag prevents re-entrant reconnect storms.

### Enhancement #16: Corsair iCUE Depth Improvements ✅ Done
Corsair support previously leaned on static color commands with limited mode-specific depth.

**Shipped behavior:**
- Added `ApplyPerformanceModePatternAsync(modeName, primaryHex)` to `CorsairDeviceService`.
- During performance-mode-synced lighting, Corsair keyboards now receive a mode-synced gradient pattern (`LightingEffectType.Wave`) while non-keyboard Corsair devices receive static color to avoid unsupported effect fallbacks.
- Added per-mode pattern speed tuning (quiet slower, balanced default, performance faster) and automatic secondary gradient color derivation from the selected mode color.
- Enhanced iCUE backend (`CorsairICueSdk.ApplyLightingAsync`) to honor preset effect type and render per-key gradients for wave mode, alternating dual-color patterns for breathing mode, and spectrum gradients for color-cycle mode.

### Enhancement #17: Lighting Sync Reliability Audit ✅ Done
Mode-switch lighting updates can desynchronize when multiple providers are active if one provider is slower than others.

**Shipped behavior:**
- Added optional `PrepareEffectAsync(string effectId)` default-implementation method to `IRgbProvider` interface (C# default interface member, noop by default).
- `RgbManager.ApplyEffectToAllAsync()` now runs a two-phase commit: Phase 1 calls `PrepareEffectAsync` on all providers in parallel (providers may pre-serialise payloads), then Phase 2 calls `ApplyEffectAsync` on all providers simultaneously.
- `SyncStaticColorAsync` updated with the same pattern.
- Providers that support staging can override `PrepareEffectAsync` to gain tighter hardware-write synchronization.

### Enhancement #18: RGB Settings UI Cleanup ✅ Done
The Lighting settings page had accumulated redundant command aliases.

**Shipped behavior:**
- Removed unused `DiscoverCorsairCommand` and `DiscoverLogitechCommand` property declarations and constructor assignments from `LightingViewModel` (XAML binds to `DiscoverCorsairDevicesCommand` / `DiscoverLogitechDevicesCommand` only).
- Per-provider status badges in the header and per-section headers now always show when a provider is enabled (not just when connected), using the status-aware brush from Enhancement #13.

---

## 🔧 Incomplete Feature Completions

### Enhancement #19: GPU Fan Curve Support ✅ Done
The GPU fan curve UI previously implied full per-fan write support on all models, which could mislead users on systems that only honor unified fan writes.

**Shipped behavior:**
- Disabled the "Independent CPU/GPU" toggle path in `FanControlView` until model-level capability detection is finalized.
- Added an in-panel support-status card explaining that GPU fan curve writes are model-dependent and unified fan duty remains the safe default path.
- Added live telemetry summary in `FanControlViewModel` showing observed GPU/CPU duty ratio when telemetry is available.
- Preserved existing curve editor workflows without introducing unsafe per-fan write assumptions.

### Enhancement #20: Per-Key Keyboard Lighting ✅ Done
The Per-Key lighting surface is available on OMEN Max models but OmenCore does not expose it. Add a per-key color grid editor and map it to the OMEN BIOS keyboard color WMI interface where available.

**Shipped behavior:**
- Added `IsPerKey` property to `KeyboardLightingService` — delegates to `KeyboardLightingServiceV2.IsPerKey` (true only when the HID per-key backend is active).
- Added `ApplyPerKeyGridAsync(Color[] flatColors)` to `KeyboardLightingService` — routes to V2 when a per-key backend is active; graceful no-op otherwise.
- Added `PerKeyCell` class to `LightingViewModel.cs` with `INotifyPropertyChanged`, `KeyName`, `KeyLabel`, `IsVisible`, `ColorHex`, and `KeyBrush` (computed brush).
- Added 84-cell (6 × 14) per-key grid (`PerKeyGrid`) initialized from a standard TKL layout (Esc–F12, number row, QWERTY, home row, shift row, bottom row). Spacer cells have `IsVisible = false`.
- Added `IsPerKeyLightingAvailable`, `PerKeyCapabilitySummary`, `SelectedPerKeyColorHex`, `SelectedPerKeyColorBrush`, `PaintKeyCommand`, `FillAllPerKeyCommand`, `ApplyPerKeyLightingCommand` to `LightingViewModel`.
- Added Per-Key RGB section inside the HP OMEN Keyboard card in `LightingView.xaml`: shows an explanation card on 4-zone models; reveals interactive color grid + Fill All + Apply when `IsPerKeyLightingAvailable` is true.
- Grid cells respond to left-click to paint with the selected brush color via `PaintKeyCommand`.

### Enhancement #21: Fan Speed Calibration Wizard ✅ Done
Users on models with inconsistent RPM readback (especially AMD Strix Point) cannot tell if their fan curve is being applied correctly. Add a guided calibration routine that ramps fans from 0% to 100% duty in steps, records observed RPM at each step, and builds a duty-to-RPM map for better diagnostics.

**Shipped behavior:**
- Surfaced the existing fan calibration wizard directly from the main Fan Control page via a new calibration card and `OpenFanCalibrationWizardCommand`.
- Added model-aware calibration status text in `FanControlViewModel`, using the persisted `FanCalibrationStorageService` data for the current machine.
- Added a live duty-to-RPM summary table to the Fan Control telemetry panel, showing stored CPU/GPU RPM values by calibrated duty percentage.
- Re-opened the existing `FanCalibrationControl` in a dedicated modal window and refresh the main fan page status/map when the wizard closes.
- Calibration remains safely gated on `IFanVerificationService.IsAvailable`; unsupported systems now show a clear unavailable message instead of implying diagnostics exist.

### Enhancement #22: AMD CPU Power Tuning ✅ Done
Intel CPU power limit sliders are implemented. Expose equivalent controls for AMD platforms via `ryzenadj` integration or direct WMI, where supported model detection passes.

**Shipped behavior:**
- Promoted AMD Ryzen STAPM and Tctl controls to a first-class tuning surface in `TuningView.xaml`, alongside the existing Intel PL1/PL2 controls.
- Added backend-aware capability gating in `SystemControlViewModel`: AMD power controls now require an active AMD SMU backend instead of showing purely on CPU vendor detection.
- Added `AmdPowerLimitsAvailable`, `ShowAmdPowerUnavailableMessage`, and `AmdPowerLimitsStatus` so unsupported AMD systems show a clear PawnIO/admin requirement instead of enabled-but-nonfunctional controls.
- Tightened `ApplyAmdPowerLimitsCommand` and `ResetAmdPowerLimitsCommand` to respect actual backend availability.
- Updated `NoTuningAvailable` logic so AMD CPU power tuning counts as an available tuning feature when the backend is active.

### Enhancement #23: Automation Rule Completeness ✅ Done
The Automation tab had several partially-implemented trigger types. The most stable shipped slice is now promoted with guard rails around unsupported rules.

**Shipped behavior:**
- Added schema validation for promoted automation triggers: `Time`, `Battery`, and `ACPower`.
- `AutomationService` now skips unsupported or malformed rules with explicit log messages instead of attempting partial execution.
- `SetPerformanceMode` actions now execute through `PerformanceModeService` instead of remaining a stub.
- Wired `AutomationService` into `MainViewModel` startup and disposal so automation rules actually run at runtime.
- Added an Automation Rules editor to the Settings Scheduler tab for supported triggers plus fan preset and performance mode actions.

### Enhancement #24: Audio-Reactive RGB ✅ Done
Ambient RGB now includes a shipped audio-reactive mode driven by real system output capture.

**Shipped behavior:**
- Replaced simulated audio input with real WASAPI loopback capture using NAudio.
- Extended scene effects with `AudioReactive` and added a built-in "Audio Reactive" scene.
- Wired dynamic effect orchestration in `RgbSceneService` so switching scenes starts/stops ambient sampling and audio capture cleanly.
- Added Lighting UI controls for audio-reactive mode toggle, visualization mode selection, and sensitivity.
- Registered active RGB providers and OMEN keyboard output with `AudioReactiveRgbService` so audio-driven colors apply across devices.

---

## ⚙️ Runtime Performance Optimizations

### Enhancement #25: Monitoring Pipeline Unified ✅ Done
Hardware telemetry now runs through one unified pipeline with explicit active/idle cadence control and shared sample fan-out.

**Shipped behavior:**
- `HardwareMonitoringService` now enforces a unified cadence policy: `1s` when UI is active and `5s` when minimized/idle.
- Low-overhead mode now uses the idle cadence path directly (`5s`) instead of separate mixed intervals.
- Added cadence transition tracking + timer registry updates so diagnostics reflect current active vs idle sampling state.
- Clarified and hardened fan-out delivery via `SampleUpdated`, ensuring consumers receive one shared sample stream instead of duplicate sensor polling.

### Enhancement #26: Dashboard Timer Consolidation ✅ Done
CPU/GPU metric consumers now rely on the shared monitoring sample stream instead of local polling timers.

**Shipped behavior:**
- Removed independent metric/chart polling timers from `HardwareMonitoringDashboard`; updates are now sample-driven from the shared monitoring pipeline.
- Added lightweight throttling in the dashboard subscriber path (alerts every 2 s, chart redraw every 5 s) while still using one upstream sample source.
- Tray telemetry now subscribes directly to `MainViewModel.LatestMonitoringSample`, removing dependency on dashboard-local update loops.
- Dashboard, System Control, Tuning surfaces, and tray all consume shared `HardwareMonitoringService` output rather than running separate CPU/GPU sampling loops.

### Enhancement #27: Chart Rendering Throttle ✅ Done
Temperature history charts redraw every second even when the main window is minimized or hidden. Gate chart invalidation on visible page state to avoid unnecessary GPU composition work when the user isn't looking at the dashboard.

**Shipped behavior:**
- Added `_chartsSuppressed` gate field to `HardwareMonitoringDashboard`.
- Subscribed to `IsVisibleChanged` and `MainWindow.StateChanged`; `UpdateChartSuppression()` sets the flag when the window is minimized or the page is not visible.
- Both `ChartUpdateTimer_Tick` and `UpdateTimer_Tick` return immediately when `_chartsSuppressed` is true.

### Enhancement #28: Memory Optimizer Idle Overhead Reduction ✅ Done
The Memory Optimizer view refreshes every 2 seconds and rebuilds the top-process list on every cycle, even when the page is not visible.

**Shipped behavior:**
- `MemoryOptimizerViewModel` now has a `SetPageActive(bool)` method that switches the refresh timer between 2 s (visible) and 30 s (hidden).
- `MemoryOptimizerView` wires `Loaded`, `Unloaded`, and `IsVisibleChanged` to call `SetPageActive`; an immediate refresh fires when the page becomes active.
- `UpdateTopMemoryHogs()` replaced with a diff-based update that removes stale entries, inserts/moves/updates in sorted order via `ObservableCollection.Move()`, and trims extras — no `Clear()` + rebuild each tick.

### RAM Cleaner Enhancement #1: Adaptive Auto-Clean Profiles ✅ Done

Memory Optimizer auto-clean now supports named adaptive profiles instead of a fixed threshold/check cadence.

**Shipped behavior:**
- Added `Aggressive`, `Balanced`, `Conservative`, `OffPeakOnly`, and `Manual` auto-clean profiles.
- Profiles now define both memory threshold and auto-check interval (10s/30s/60s/300s).
- Added profile selection UI and visible auto-check interval text in Memory Optimizer.
- Persisted selected profile in config and restore-on-startup flow.

### RAM Cleaner Enhancement #2: Per-Process Memory Exclusion List ✅ Done

Working-set cleanup now supports explicit process exclusions so critical services can be skipped.

**Shipped behavior:**
- Added exclusion-aware working-set trimming path using per-process `EmptyWorkingSet` when exclusions are configured.
- Added default protected exclusions for critical OS and OMEN helper processes.
- Added Memory Optimizer UI for adding/removing exclusions.
- Persisted exclusions in config and restored them on startup.

### RAM Cleaner Enhancement #3: Real-Time Memory Compression ✅ Done

Memory Optimizer now surfaces memory compression control directly in the utility panel.

**Shipped behavior:**
- Added memory compression state detection via `Get-MMAgent`.
- Added one-click enable/disable action via `Enable-MMAgent` / `Disable-MMAgent`.
- Added in-panel status display and standardized success/failure feedback.

### RAM Cleaner Enhancement #4: Enhanced Memory Statistics Dashboard ✅ Done

Memory Optimizer now exposes the broader memory-state dashboard that was still missing from the original tab.

**Shipped behavior:**
- Added standby-list, modified-page-list, cache, commit, page-file, and compressed-memory breakdown stats.
- Added a 30-minute RAM usage trend sparkline with low/high/average summary.
- Added a visible top-process table with context actions for copying process name and opening executable location.

### Enhancement #29: Ambient Screen Sampling Efficiency ✅ Done
Ambient RGB sampling runs as fast as 100 ms. Add adaptive back-off when sampled colors are stable across consecutive frames, and reduce sampling rate when OmenCore is minimized.

**Shipped behavior:**
- `ScreenSamplingService` gained a `SetHostMinimized(bool)` method; when minimized the timer slows to 2 000 ms and skips color application entirely.
- Adaptive back-off: after 10 consecutive stable frames (diff below threshold) the timer extends to 500 ms; resets to the configured interval immediately on any significant color change.
- `LightingViewModel.NotifyHostMinimized(bool)` forwards the window state to the service.
- `MainWindow_StateChanged` calls `NotifyHostMinimized(WindowState == Minimized)` so the service is always in sync with the window state.

### Enhancement #30: Background Timer Governance ✅ Done
Timer inventory is now explicitly governed by a three-tier registry model and surfaced in diagnostics.

**Shipped behavior:**
- Added `BackgroundTimerTier` (`Critical`, `VisibleOnly`, `Optional`) to `BackgroundTimerRegistry` and required all timer registrations to declare tier intent.
- Classified existing background loops by tier (for example: monitoring/watchdog/curve recovery = `Critical`, ambient/RTSS/MSI provider polling = `VisibleOnly`, automation/process/update/memory auto-clean/scene schedule = `Optional`).
- Updated diagnostic export (`background-timers.txt`) to include timer tier per entry so support snapshots clearly show governance intent.
- Updated `MemoryOptimizerViewModel` refresh loop to true visible-only behavior: refresh timer now pauses when the page is inactive and unregisters from timer diagnostics until re-activated.
- Hardened optional timer lifecycle handling so disabled background update checks immediately unregister their timer entry from diagnostics.

### Enhancement #31: Dispatcher and UI Update Coalescing ✅ Done
Monitoring fan-out now uses coalesced dispatcher updates so bursty telemetry does not generate incremental UI churn across surfaces.

**Shipped behavior:**
- `MainViewModel` now queues incoming monitoring samples and applies only the latest pending sample per dispatcher pass (`QueueMonitoringUiSample`), reducing redundant top-level telemetry updates for tray/popup/status bindings.
- `DashboardViewModel` now batches sample ingestion, chart history updates, fan-curve point updates, and derived summary notifications into a single coalesced UI-dispatch loop.
- Coalescing logic drains queued samples safely while preserving newest-data-wins behavior, preventing dispatcher backlog under rapid sample bursts.
- Added fail-safe coalescing cleanup so dashboard UI update state cannot get stuck if a dispatched update throws.
- **Post-release fix:** `LightingViewModel.OnMonitoringSampleUpdated` — the one missed subscriber in the original pass — now marshals to dispatcher via `BeginInvoke` (see Critical Bug Fixes above).

### Enhancement #31a: Monitoring Subscriber Isolation ✅ Done
`HardwareMonitoringService` `SampleUpdated` fan-out is now subscriber-isolated. Previously a single multicast `?.Invoke(this, sample)` meant one throwing subscriber could prevent all downstream handlers from receiving the sample. Now each handler in `GetInvocationList()` is invoked inside its own try/catch; failures are logged and skipped without affecting other subscribers.

### Enhancement: CPU Temp Fallback Timeout Increase ✅ Done
`CpuFallbackReadTimeoutMs` increased from 250 ms to 500 ms. Runtime logs showed the LibreHardwareMonitor IPC read was consistently exceeding 250 ms under heavy CPU load, triggering repeated `CPU temp fallback timed out — disabling fallback for 30s` warnings and leaving CPU temperature unreported for 30 s intervals. 500 ms provides headroom for IPC scheduling jitter without materially affecting sample cadence (monitoring loop runs at 1 s / 5 s cadence).

**File:** `Hardware/WmiBiosMonitor.cs` — `CpuFallbackReadTimeoutMs`

---

## 🛠️ System Optimizer Improvements

### Enhancement: Restore Defaults Reliability ✅ Done
Covered under Critical Bug Fixes above. Additional hardening shipped for `Revert All`:
- stage-level fault isolation so one optimizer failure does not abort the whole revert flow,
- explicit staged progress/status markers (`[n/6]`) during multi-step revert,
- compatible stage batching (network/input/visual/storage) in parallel after power/services complete.

### Enhancement: Risk Assessment Detail View ✅ Done
Each optimizer entry now includes a `What changes` expandable details section so users can review the real system impact before applying a tweak.

**Shipped behavior:**
- Added detailed per-toggle explanations sourced from the optimizer implementation.
- Listed the affected registry keys, Windows services, and command-based changes directly in the UI.
- Added manual undo guidance so advanced users can understand how to reverse a specific tweak outside OmenCore if needed.

### Enhancement: Optimization State Drift Detection ✅ Done
System Optimizer now performs scheduled verification against live system state so silent external reversions are surfaced instead of going unnoticed.

**Shipped behavior:**
- Integrated the dedicated optimizer verifier into refresh and verification flows as the authoritative state source.
- Added hourly background verification with `Last verified` and verification summary status in the optimizer summary area.
- Detects drift relative to the last known optimizer state and automatically re-applies low-risk service toggles when they are silently re-enabled.

### Enhancement: Batch Apply by Category ✅ Done
Users can now bulk-apply optimizer categories without committing to the full Gaming Max preset.

**Shipped behavior:**
- Added `Apply All` actions for each optimizer category section (Power, Services, Network, Input, Visual, Storage).
- Added staged category-apply progress feedback in the existing optimizer status/progress surface.
- Category actions auto-disable when a category is already fully applied or while another optimizer operation is in progress.

---

## 🗑️ Bloatware Manager Improvements

### Enhancement: AppX Detection Expansion ✅ Done
Bloatware detection is no longer limited to the hardcoded signature list compiled into the service.

**Shipped behavior:**
- Added JSON-backed signature loading from `config/bloatware_database.json` so new AppX/OEM signatures can be maintained without editing the scanner code.
- Expanded detection coverage for overlapping HP OMEN control apps, HP analytics components, and additional Microsoft helper/gaming packages.
- Kept the existing protected-app guardrails in front of the new signatures so critical drivers and Windows components are still never flagged.

### Enhancement: Staged Removal with Rollback ✅ Done
Bulk low-risk removal now executes as a staged transaction:
- items are removed one-by-one with progress callbacks,
- on first failure, OmenCore attempts rollback of previously removed restorable items,
- final status summarizes rollback restored/failed/skipped counts so users can export actionable logs.

### Enhancement: Startup and Task Scan Coverage ✅ Done
Bloatware scanning now covers more of the real auto-start surface area instead of relying on registry Run keys alone.

**Shipped behavior:**
- Added Startup folder scanning for both per-user and machine-wide startup locations.
- Added safe backup/remove/restore support for file-based startup items found there.
- Improved scheduled-task matching and friendly-name display so renamed HP/telemetry tasks are easier to detect and review.

### Enhancement: Removal Report History ✅ Done
Removal reporting now persists beyond a single session and can be opened directly from the Bloatware Manager workflow.

**Shipped behavior:**
- Added persistent history tracking for remove/restore operations in `%LocalAppData%\OmenCore\Logs\bloatware-history.json`.
- Enhanced exported logs with admin context, machine/OS details, and recent action history for affected apps.
- Added a `View Report` action in the Bloatware Manager UI to open the latest generated report immediately.

### Enhancement: HP OMEN-Specific Bloatware Detection ✅ Done
Bloatware signatures now separate OMEN Gaming Hub itself from optional OMEN companion components so users can choose narrower cleanup targets.

**Shipped behavior:**
- Added explicit OMEN Gaming Hub variant signatures (`stable`, `dev`, and `beta`).
- Added companion-app signatures for OMEN lighting and monitoring components (for example OMEN Light Studio and OMEN Install Monitor).
- Added additional HP companion optimizer/capability signatures used by bundled OMEN software.

### Enhancement: Removal Preview Mode ✅ Done
Before removal execution, Bloatware Manager now stages a selectable preview list so users can review scope and timing before committing.

**Shipped behavior:**
- Added preview panel for both single-item remove and bulk low-risk remove workflows.
- Added per-item selection toggles so users can deselect entries before confirm.
- Added per-item estimated removal time plus heuristic dependency hints based on related publisher packages.
- Added explicit preview actions: `Toggle All`, `Cancel`, and `Remove Selected`.

### Enhancement: Restore Point Creation ✅ Done
Before removal execution, Bloatware Manager now offers a preflight Windows restore point flow and carries restore metadata into the exported removal report.

**Shipped behavior:**
- Added pre-removal restore-point prompt before both single-item and bulk preview-confirmed removals.
- Added create-or-reuse behavior for a recent restore point in the same session to avoid redundant restore-point spam.
- Added restore-point timestamp, description, sequence, and status metadata to exported bloatware removal logs.

---

## 🧪 Developer and Diagnostic Improvements

### Monitoring Health Surface in Quick Access ✅ Already Shipped
Quick Access already shows a "Monitoring: Healthy/Degraded/Stale" indicator from `UpdateMonitoringHealth()`. v3.3.0 adds tooltip detail text explaining each state for users who need to triage their sensor setup.

### Runtime Performance Diagnostic View ✅ Done
Diagnostics export now includes a full runtime performance snapshot and a background timer registry to reduce ambiguity when triaging performance regressions.

**Shipped behavior:**
- Added `runtime-performance.txt` to diagnostics bundles.
- Captures process runtime metrics (CPU time, thread/handle counts, working set, private memory).
- Captures CLR runtime state (GC collection counts, heap/load stats, ThreadPool availability/min/max).
- Captures a quick inventory of Omen-related running processes with CPU and memory usage.
- Added `background-timers.txt` to diagnostics bundles via `BackgroundTimerRegistry`.
- All major recurring background loops now self-register on start and self-unregister on stop: hardware monitor loop, hardware watchdog, curve recovery monitor, automation rule evaluation, process monitor, RGB scene scheduler, auto-update check, memory auto-clean/interval-clean, MSI Afterburner poll, RTSS frame-data poll, and ambient screen sampler.
- Each timer entry reports its owner service, description, cadence, and how long it has been running.

### Error Telemetry Improvements ✅ Done
Structured telemetry context is now available in core logging and applied across exception-heavy application paths.

**Shipped behavior:**
- Added structured context-capable logging methods with standardized fields: `component`, `operation`, `model`, and `os`.
- Added automatic caller-based context enrichment for all `Error(..., ex)` logs so existing exception paths emit structured telemetry without per-callsite boilerplate.
- Main startup now seeds default logging telemetry context from detected system model and OS.
- Migrated global unhandled exception pathways (dispatcher/appdomain/task scheduler) to structured telemetry logging.
- Migrated diagnostics export failure logging to structured telemetry logging.
- Expanded structured telemetry logging coverage to core operational paths in Auto Update, Settings, System Control, Fan Control, and MainViewModel flows.

---

## 🧪 Additional Triage (April 2026)

### Linux GUI black screen on startup (Wayland/X11 GPU path) ✅ Done
User reports indicate a black-window startup in some Linux sessions, especially under Wayland or mixed X11/GL driver stacks.

**Current mitigation (already available):** set `OMENCORE_GUI_RENDER_MODE=software` to force software rendering.

**Newly shipped hardening (Issue #100):**
- Linux startup now detects sudo/root launch with missing session DBus and auto-recovers `DBUS_SESSION_BUS_ADDRESS` from `/run/user/$SUDO_UID/bus` when available.
- If no session bus can be recovered, startup now disables AT-SPI bridge initialization (`NO_AT_BRIDGE=1`) to avoid DBus-address null startup failure paths.
- Added startup fallback retry: if renderer initialization fails on Linux, OmenCore now retries startup once in software mode automatically.
- Added persisted last-known-good render mode state (`render-startup-state.json`) so repeated renderer startup failures can auto-promote software mode for subsequent launches.
- Startup failure guidance now prefers non-sudo GUI launch and includes a `sudo --preserve-env=...` command when elevated launch is unavoidable.
- Startup diagnostics log now records user/sudo/runtime/DBus context for faster triage.

**Newly shipped (in-app notification banner):**
- `MainWindowViewModel` detects `OMENCORE_GUI_RENDER_RETRY=1` on Linux and sets `ShowRenderFallbackBanner = true`.
- Amber notification banner added to `MainWindow.axaml` (DockPanel above main content): shows "⚠️ GPU rendering failed — running in software mode."
- "Persist software mode" button writes `LastKnownGoodRenderMode = "software"` to `render-startup-state.json` so all subsequent launches auto-select software rendering.
- Dismiss button hides the banner for the current session without persisting.

### Undervolt / Curve Optimizer capability messaging confusion ✅ Done
Users reported uncertainty about whether undervolt should work on Intel and whether Curve Optimizer is available on AMD.

**Fix shipped:**
- Vendor-aware labels and guidance are now shown (`CPU Undervolting` on Intel, `CPU Curve Optimizer` on AMD)
- Active backend details are surfaced directly in the panel so users can see the control path in use
- Apply/Reset now show persistent in-panel result status text (success/failure) instead of relying on background logs
- Unsupported/blocked states are surfaced inline with clearer remediation context

### Unexpected app exit on AC unplug event ✅ Done
One report indicates OmenCore closes when power is disconnected.

**Fix shipped:**
- Added defensive exception guards in power-state transition event dispatch and UI synchronization callbacks
- Added structured transition-scoped logging for AC/Battery verification and profile apply branches
- Added rollback attempts for fan preset/performance mode when an automation apply step fails

### Game Library manual add for non-detected titles ✅ Done
Added direct "Add Game" flow in Game Library so users can select an executable when launcher scans find nothing.

**Files:** `ViewModels/GameLibraryViewModel.cs`, `Views/GameLibraryView.xaml`

### Optional "Lite Mode" UI scope ✅ Done
Users requested a simplified mode that hides advanced tuning controls.

**Shipped behavior:**
- Added a persisted `Lite mode (beginner UI)` toggle in Settings.
- Added shell-level advanced-tab gating in the main window so Lite mode hides advanced surfaces by default.
- Added runtime synchronization between Settings and `MainViewModel` so enabling/disabling Lite mode updates visible tabs immediately without restart.

### GPU/CPU load frozen or unchanging under MSI Afterburner coexistence ✅ Done
Users reported that CPU and GPU load values in the OmenCore dashboard would appear correct initially but then stop changing, remaining pinned at the same percentage across multiple polling cycles. The issue only reproduced when MSI Afterburner was running alongside OmenCore.

**Root causes:**
1. `LibreHardwareMonitorImpl.cs` contained a stale-cache reuse block that re-applied the last non-zero CPU/GPU load values whenever the worker subprocess returned 0. Under Afterburner coexistence the worker returns 0 intermittently (shared hardware access contention), so the stale value was pinned indefinitely.
2. The Windows GPU Engine performance counter fallback in both `WmiBiosMonitor.cs` and `LibreHardwareMonitorImpl.cs` was summing all engine type counters together. Engine types like `VideoDecode`, `VideoEncode`, and `VidPnMgr` dominate the sum during media playback but are not representative of 3D/compute load, producing a misleadingly high or stuck aggregate.
3. When NVAPI load reads returned 0 under Afterburner interference, the fallback chain fell through to the stale-cache path above.

**Fix:**
- Removed the stale-cache reuse block from `LibreHardwareMonitorImpl.cs`. Zero or missing worker load now passes through as-is instead of being masked by the previous non-zero reading.
- Both `WmiBiosMonitor.cs` and `LibreHardwareMonitorImpl.cs` now select the **peak 3D / Compute / CUDA engine** value from the GPU Engine counters rather than summing all engine types. The broad sum is used only as a fallback when no preferred engine type counter is present.
- Added a power-inferred load floor in `WmiBiosMonitor.TryApplyLoadFallback()`: if GPU load reads below 15% but GPU power draw is ≥ 45 W, load is inferred from the power-to-TDP ratio and tagged as `PowerInferred` to avoid showing 0% during sustained workloads.
- Periodic telemetry log cadence increased from every 30 samples to every 10 samples to surface coexistence state changes faster during troubleshooting.

**Files:** `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`, `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### Startup hardware restore guardrails to prevent firmware-loop risk ✅ Done
User reports (GitHub #106 and Discord) indicated that on HP OMEN 16 and some Victus models, OmenCore's startup hardware restore sequence — which reapplies the saved Performance Mode, GPU Power Boost state, and TCC offset at launch — could trigger a CMOS checksum error or BIOS boot loop. This is consistent with EC / BIOS firmware sensitivity to concurrent WMI BIOS writes across cold-boot, particularly on models with stricter EC firmware validation.

**Risk assessment:** The startup restore sequence that existed before this fix issued up to three BIOS/EC write commands unconditionally at every OmenCore launch. On sensitive firmware this can corrupt the EC working state stored in CMOS, triggering a checksum error on the next boot. Recovery requires a hardware-level BIOS recovery or EC drain reset.

**Fix:**
- Added `EnableStartupHardwareRestore` (default `false`) and `AllowStartupRestoreOnOmen16OrVictus` (default `false`) to `AppConfig.cs`. Both default to `false`, disabling the startup restore sequence entirely on fresh installs and existing users who haven't explicitly opted in.
- Added `ShouldRunStartupHardwareRestore()` to `SystemControlViewModel.cs`: checks the config flag first, then checks the resolved system model string for `OMEN 16` or `Victus` substrings. On matched models the second config flag must also be explicitly set to `true` before startup restores are allowed.
- Startup restores for Performance Mode, GPU Power Boost, and TCC offset are all gated behind this check with warning-level log entries on skip.
- Users who depended on startup restore can re-enable it by setting `EnableStartupHardwareRestore: true` in their config.

**Files:** `src/OmenCoreApp/Models/AppConfig.cs`, `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`

---

### Disabled Buttons Did Not Show Tooltips ✅ Done
**Found during v3.3.0 pre-release UI review**

Buttons that were disabled (greyed out) did not display their tooltip on hover, making it impossible for users to understand why an action was unavailable without looking elsewhere.

**Root Cause:** WPF suppresses tooltips on disabled controls by default. The `ModernButton` and `PresetButton` base styles did not set `ToolTipService.ShowOnDisabled`.

**Fix:** Added `<Setter Property="ToolTipService.ShowOnDisabled" Value="True"/>` to both `ModernButton` and `PresetButton` base styles in `ModernStyles.xaml`. All derived styles (`SecondaryButton`, `CompactIconButton`, `DangerButton`, etc.) inherit this automatically, covering all ~157 button usages across the app.

**File:** `Styles/ModernStyles.xaml` — `ModernButton` and `PresetButton` base styles

---

### OGH Unsupported-Command Warnings Logged Indefinitely ✅ Done
**Found during v3.3.0 pre-release log review**

On models that do not support certain OGH hardware commands (e.g. `Fan:GetData`), OmenCore logged a `WARN` entry approximately every 60 seconds indefinitely — once per throttle window — generating persistent log noise with no actionable signal after the first occurrence.

**Root Cause:** `OghServiceProxy.LogThrottledWarning` throttled repeated warnings to once per 60 s but had no upper bound; the warning recurred every minute for the entire session. Additionally, the `ExecuteOghSetCommand` success path logged at `Info` level on every call, generating high-frequency chatter during normal fan and GPU mode switching.

**Fix:**
- `ExecuteOghSetCommand` success log downgraded from `Info` → `Debug`. Normal mode-switch operations no longer appear in production logs.
- `LogThrottledWarning` now tracks occurrence counts per command. After 5 throttled occurrences, the command is permanently demoted to `Debug`-only with a single one-time `Info` entry: `"[OGH] Silencing WARN for <command> — unsupported on this model"`. Subsequent calls produce only a `Debug` trace.

**File:** `Hardware/OghServiceProxy.cs` — `LogThrottledWarning`, `ExecuteOghSetCommand`

---

### ViewModel Event Subscriptions Not Released on Shutdown ✅ Done
**Found during v3.3.0 pre-release review**

`LightingViewModel`, `SystemControlViewModel`, and `FanCalibrationViewModel` subscribed to service events (monitoring samples, audio data, EDP mitigation, fan calibration callbacks) but never unsubscribed. On shutdown, `MainViewModel.Dispose()` did not call cleanup on these child ViewModels, leaving dangling event subscriptions that could delay GC of the ViewModel graph and its referenced services.

**Fix:** Added `Cleanup()` methods to all three ViewModels:
- `LightingViewModel.Cleanup()` — unsubscribes `AudioDataProcessed`, `SampleUpdated`, `ModeApplied`, `SceneChanged`, `ScenesListChanged`, `ColorChanged`
- `SystemControlViewModel.Cleanup()` — unsubscribes `SampleUpdated`, `ThrottlingDetected`, `MitigationApplied`, `MitigationRemoved`
- `FanCalibrationViewModel.Cleanup()` — unsubscribes `CalibrationStepCompleted`, `CalibrationCompleted`, `CalibrationError`; cancels and disposes `_cts`

`MainViewModel.Dispose()` now calls `Lighting?.Cleanup()` and `_systemControl?.Cleanup()` before releasing other resources.

**Files:** `ViewModels/LightingViewModel.cs`, `ViewModels/SystemControlViewModel.cs`, `ViewModels/FanCalibrationViewModel.cs`, `ViewModels/MainViewModel.cs`

---

## 📋 Summary Table

| Category | Shipped |
|---|---|
| Critical bug fixes | 5 |
| High-priority bug fixes | 6 |
| Fan and thermal improvements | 2 |
| OSD improvements | 7 |
| Quick Access and UI polish | 14 |
| RGB / Lighting hardening | 6 |
| Feature completions (#19–#24) | 6 |
| Runtime performance (#25–#31) | 11 |
| System Optimizer | 4 |
| Bloatware Manager | 7 |
| Diagnostics and developer tools | 3 |
| Triage and community fixes | 11 |
| **Total** | **80** |

---

## Files Changed (Done Items)

| File | Change |
|---|---|
| `src/OmenCoreApp/Services/FanService.cs` | `VerificationPasses()` now skips RPM-change check for curve presets |
| `src/OmenCoreApp/Services/FanVerificationService.cs` | Removed destructive `SetFanMode(Default)` call on verification failure |
| `src/OmenCoreApp/ViewModels/FanControlViewModel.cs` | `ApplyPreset()` dispatched to background via `Task.Run`; `ApplyCustomCurveAsync` wrapped; `SaveCustomPreset` redundant UI-thread call removed; `ReapplySavedPresetAsync` converted to true async; `ApplyConstantSpeed` dispatched with overlap guard |
| `src/OmenCoreApp/Hardware/WmiFanController.cs` | Added `NormalizeFanName()` (G/GP mapping), V1 transition kick reliability, and V1 auto-mode floor clear via `SetFanLevel(0,0)` |
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | `TryRestartAsync()` now disposes and restarts `_tempFallbackMonitor` and relaunches HardwareWorker on resume |
| `src/OmenCoreApp/Services/KeyboardLightingService.cs` | Fixed Restore Defaults deadlock by removing UI-thread `.GetAwaiter().GetResult()` path for V2 async calls |
| `src/OmenCoreApp/Services/SystemOptimizer/SystemOptimizerService.cs` | Added `CancellationToken` support and per-step status progression in `RevertAllAsync()` |
| `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs` | Added 60-second timeout and dispatcher-marshaled status updates for Revert All flow |
| `src/OmenCoreApp/Services/SystemOptimizationService.cs` | Set `CreateNoWindow=true` for scheduler tweak PowerShell process |
| `src/OmenCoreApp/Services/OmenKeyService.cs` | Excluded scan code `0xE046` from OMEN-key matching for `VK_LAUNCH_APP1/APP2` brightness conflict |
| `src/OmenCoreApp/Views/OsdOverlayWindow.xaml.cs` | `GetCurrentMonitorWorkArea()` now converts physical → logical pixels via per-monitor DPI scale |
| `src/OmenCoreApp/Views/OsdOverlayWindow.xaml` | Gradient background, refined border, 24px drop shadow, corner radius 10 |
| `src/OmenCoreApp/Views/QuickPopupWindow.xaml` | Fan buttons reordered Quiet → Auto → Curve → Max; "Custom" renamed to "Curve" |
| `src/OmenCoreApp/Views/QuickPopupWindow.xaml.cs` | Added `UpdateCurvePresetName()` to show active curve preset tooltip text |
| `src/OmenCoreApp/Utils/TrayIconService.cs` | Added curve preset tooltip sync API and popup refresh wiring |
| `src/OmenCoreApp/ViewModels/MainViewModel.cs` | Exposed `ActiveCurvePresetName` and raised property change with fan mode updates |
| `src/OmenCoreApp/App.xaml.cs` | Wired tray popup updates for active curve preset during initial sync and runtime changes |
| `src/OmenCoreApp/ViewModels/GameLibraryViewModel.cs` | Added manual executable import command and preserved manual games across rescans |
| `src/OmenCoreApp/Views/GameLibraryView.xaml` | Added `Add Game` toolbar button for manual game registration |
| `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs` | Added vendor-aware undervolt/curve-optimizer UX text, backend visibility, support gating, and in-panel apply/reset result status |
| `src/OmenCoreApp/Views/SystemControlView.xaml` | Updated undervolt section to show backend/status context, unsupported-state guidance, and vendor-aware apply label |
| `src/OmenCoreApp/Views/TuningView.xaml` | Updated tuning undervolt card with vendor-aware labels and persistent apply/reset result feedback |
| `src/OmenCoreApp/Services/PowerAutomationService.cs` | Hardened AC/Battery transition handling, added structured transition logging, and added rollback behavior on partial automation apply failures |
| `src/OmenCoreApp/ViewModels/MainViewModel.cs` | Added non-fatal guards around fan/performance UI sync callbacks triggered by power automation events |
| `src/OmenCoreApp/ViewModels/FanControlViewModel.cs` | Added fan/performance link status text and one-time decoupling info-banner state |
| `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs` | Added fan/performance link status text and explicit performance-mode fan-policy guidance |
| `src/OmenCoreApp/Views/FanControlView.xaml` | Added visible fan/performance link badge and dismissible decoupling explainer |
| `src/OmenCoreApp/Views/SystemControlView.xaml` | Added performance-mode fan link badge, decoupling explainer, and sticky fan-policy guidance copy |
| `src/OmenCoreApp/Views/QuickPopupWindow.xaml.cs` | Added decoupled-mode Quick Access copy/tooltips that clarify the active fan preset stays in place |
| `src/OmenCoreApp/Models/AppConfig.cs` | Added persisted dismissal state for the one-time fan/performance decoupling explainer; added `EnableStartupHardwareRestore` and `AllowStartupRestoreOnOmen16OrVictus` config properties (both default `false`) |
| `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs` | Removed stale-cache load reuse block; GPU engine counter fallback now uses peak 3D/Compute/CUDA engine instead of sum |
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | GPU engine fallback peak-engine logic; power-inferred load floor in `TryApplyLoadFallback()`; telemetry cadence 30→10 samples |
| `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs` | Added `ShouldRunStartupHardwareRestore()` model-aware guard; gated Performance Mode, GPU Power Boost, and TCC offset startup restores behind the guard |
| `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs` | `SelectedApp` setter now calls `RaiseCommandStates()` instead of `OnPropertyChanged(nameof(CanRemoveSelected/CanRestoreSelected))` — fixes Remove/Restore buttons permanently disabled after scan |
| `src/OmenCoreApp/Styles/ModernStyles.xaml` | Added `ToolTipService.ShowOnDisabled="True"` to `ModernButton` and `PresetButton` base styles; all derived button styles inherit automatically |
| `src/OmenCoreApp/Hardware/OghServiceProxy.cs` | `ExecuteOghSetCommand` success log downgraded `Info` → `Debug`; `LogThrottledWarning` permanently silences unsupported commands to Debug after 5 throttled occurrences |
| `src/OmenCoreApp/ViewModels/LightingViewModel.cs` | Added `Cleanup()` method unsubscribing 6 service events |
| `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs` | Added `Cleanup()` method unsubscribing 4 service events |
| `src/OmenCoreApp/ViewModels/FanCalibrationViewModel.cs` | Added `Cleanup()` method unsubscribing 3 calibration events and cancelling `_cts` |
| `src/OmenCoreApp/ViewModels/MainViewModel.cs` | `Dispose()` now calls `Lighting?.Cleanup()` and `_systemControl?.Cleanup()` |

---

*Changelog format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/). Version follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).*
