# OmenCore v3.4.0 — Release Notes

**Version:** 3.4.0  
**Release Date:** TBD  
**Release Status:** 🚧 In development  
**Previous Release:** v3.3.1 (2026-04-16)  
**Type:** Feature / Bug fix release

---

## Overview

v3.4.0 delivers a collection of correctness fixes reported by the community via Discord (April 2026) and ongoing internal audit. The headline changes are:

- **Custom fan curves no longer lock CPU power to 25 W** — a silent regression since the fan mode mapping code incorrectly applied the HP BIOS "Cool" thermal policy to all manual/custom curve presets.
- **Fan profile selector (Max/Extreme/Gaming/Auto/Silent/Custom) is visible again** — the six profile cards were rendered behind the curve editor since v3.3.x due to two UI elements sharing the same grid row.
- **Print Screen / Snipping Tool now works correctly** — `VK_SNAPSHOT` is explicitly guarded in the low-level keyboard hook so OmenCore can never accidentally interfere with Windows 11's Snipping Tool activation.
- **Fn+F2/F3 brightness keys no longer toggle OmenCore on affected models** — LaunchApp key detection now only accepts a dedicated OMEN scan signature and rejects known brightness-conflict scan patterns.
- **Fan curve drag no longer crashes on boundary temperatures** — the snap-then-clamp ordering was reversed, allowing rounding to push a temperature back out of its valid range.
- **Bitdefender AV false positive documented** — FAQ and README updated with the specific `Gen:Application.Venus.Cynthia.Winring` detection name and per-vendor exclusion steps.

---

## 🐛 Bug Fixes

### Custom Fan Curve Locks CPU to ~25 W ✅ Fixed
**Severity:** High  
**Affects:** All users running a custom fan curve preset  
**Reported:** Discord, April 2026 (user reported CPU TDP cap while on "Custom" fan profile)

**Root cause:**  
`WmiFanController.MapPresetToFanMode()` fell through to a `default` branch for `FanMode.Manual`. That branch examined the average fan-speed percentage of the applied curve: if the average was below 40 % it chose `HpWmiBios.FanMode.Cool`, which instructs the HP WMI BIOS to cap CPU TDP to approximately 25 W. A typical quiet custom curve with low fan targets would consistently hit this threshold, silently locking CPU power regardless of what the user had configured.

**Fix:**  
Added an explicit `case FanMode.Manual:` in `MapPresetToFanMode()` that returns `HpWmiBios.FanMode.Default` (neutral thermal policy). Custom-curve presets now drive fan levels exclusively through the curve monitoring engine; no BIOS thermal policy override is applied.

**File:** `src/OmenCoreApp/Hardware/WmiFanController.cs`

---

### Fan Profile Selector Hidden Behind Curve Editor ✅ Fixed
**Severity:** High  
**Affects:** All users of the Fan Control tab since v3.3.x  
**Reported:** Discord, April 2026

**Root cause:**  
In `FanControlView.xaml` the Fan Profile Selection panel and the Fan Curve Editor section both declared `Grid.Row="2"` in a three-row grid. WPF rendered both at the same position — the curve editor (declared last) painted over the profile cards, making them invisible even though they were still in the visual tree.

**Fix:**  
Added a fourth `RowDefinition Height="Auto"` to the grid and moved the Fan Curve Editor section to `Grid.Row="3"`. Profile cards remain at `Grid.Row="2"` and are now fully visible.

**File:** `src/OmenCoreApp/Views/FanControlView.xaml`

---

### Fan Curve Drag Crashes at Temperature Boundary ✅ Fixed
**Severity:** High  
**Affects:** Users dragging curve points to the maximum or minimum temperature on the graph  
**Reported:** Internal (v3.3.x regression)

**Root cause:**  
`FanCurveEditor.xaml.cs` applied snap (`Math.Round(temp / step) * step`) before clamping to neighbour bounds. With `DragTemperatureSnapStep = 2` and a neighbour constraint of, e.g., `maxTemp = 87`, rounding 87 ÷ 2 = 43.5 via banker's rounding gives 44, and 44 × 2 = 88 — one step above the allowed maximum. Passing 88 into `CurvePoints.Move(index, 88, …)` threw `ArgumentException: '90' cannot be greater than 87`.

A secondary crash occurred when a `MouseMove` event fired after `MouseUp` had already cleared `_draggedPointIndex`, causing an index-out-of-range access.

**Fixes:**
1. Snap ordering reversed: clamp first, then snap — `Math.Clamp(Math.Round(temp / step) * step, minTemp, maxTemp)`.
2. Added stale-index guard at the top of `Point_MouseMove`: return early if `_draggedPointIndex` is out of range.

**File:** `src/OmenCoreApp/Controls/FanCurveEditor.xaml.cs`

---

### Print Screen / STAMP Key Does Not Open Snipping Tool ✅ Fixed
**Severity:** High  
**Affects:** Windows 11 users where "Use Print Screen key to open screen snipping" is enabled  
**Reported:** Discord, April 2026 (user spooky, OMEN 16-wf1001nl, v3.2.5)

**Root cause (investigation):**  
`OmenKeyService`'s `WH_KEYBOARD_LL` hook and `HotkeyService`'s `RegisterHotKey` registrations were confirmed to **not** intercept `VK_SNAPSHOT` (0x2C) in the current code. However, on certain HP OMEN models the BIOS WMI key event fires coincidentally when Print Screen is pressed. Without an explicit guard, a future code change (or a subtle timing window) could treat that WMI event as an OMEN key press and interfere with the Windows Snipping Tool activation.

**Fix:**  
Added `VK_SNAPSHOT = 0x2C` constant to `OmenKeyService` and added an explicit guard in `TryGetNeverInterceptReason()` returning reason `"never-intercept-printscreen"`. This ensures:
- The hook passes Print Screen through immediately via `CallNextHookEx` with no side effects.
- Any WMI BIOS event firing at the same time as Print Screen is suppressed by `ShouldSuppressWmiEventFromRecentNeverInterceptKey`.
- The intent is documented in code, preventing future regressions.

**File:** `src/OmenCoreApp/Services/OmenKeyService.cs`

---

### Fn+F2/F3 Brightness Keys Still Trigger OmenCore on Some Models (GitHub #74) ✅ Fixed
**Severity:** High  
**Affects:** HP OMEN/Victus models where Fn brightness emits `VK_LAUNCH_APP1`/`VK_LAUNCH_APP2`  
**Reported:** GitHub #74

**Root cause:**  
The earlier `0xE046` guard fixed one known variant, but `IsOmenKey()` still treated `VK_LAUNCH_APP1`/`VK_LAUNCH_APP2` as OMEN when scan code matched broad legacy values that can overlap brightness/Fn events (`0x0046`, `0x009D`) on some keyboards.

**Fix:**
1. Added a strict LaunchApp allow-list (`OmenLaunchAppScanCodes = { 0xE045 }`) so only the dedicated OMEN launch signature is accepted for APP1/APP2.
2. Added explicit rejection for known brightness-conflict scans (`0xE046`, `0x0046`, `0x009D`) in both LaunchApp paths.
3. Added regression tests for both reject and allow behavior:
- reject LaunchApp + brightness-conflict scans
- accept LaunchApp + dedicated OMEN scan

**Files:**
- `src/OmenCoreApp/Services/OmenKeyService.cs`
- `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs`

---

### Bloatware Removal Silent No-op on Victus Models (GitHub #107) ✅ Fixed
**Severity:** High  
**Affects:** Bloatware Manager remove flows where target items were already absent or removal produced no state change  
**Reported:** GitHub #107 (Victus 15)

**Root cause:**  
Removal paths could return ambiguous success/failure outcomes without a user-visible, per-item detail trail. On no-op paths (already-absent AppX/Win32/startup/task targets), users could see "nothing happened" even though a code path executed. Bulk reporting also treated all `true` returns as equivalent success, which obscured skipped/no-op outcomes.

**Fixes:**
1. `BloatwareManagerService` now classifies every removal attempt as explicit `VerifiedSuccess`, `Skipped`, or `Failed` with per-item detail text (`LastRemovalDetail`).
2. AppX removal now verifies pre/post presence across current-user, all-users, and provisioned scopes; no-state-change outcomes are surfaced as explicit failures with reason.
3. Startup/scheduled-task/Win32 no-op paths now return explicit skipped details instead of silent outcomes.
4. Bulk removal now separates skipped/no-op items from true removals so rollback logic and summaries reflect real outcomes.
5. `BloatwareManagerViewModel` now emits detailed single and bulk status messages (including skip/failure details).
6. `BloatwareManagerView` now shows a dedicated per-item `Result Detail` column and status tooltip so outcomes are visible in-list without exporting logs.
7. Added regression coverage for no-op classification and outcome-message composition.

**Files:**
- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs`
- `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`
- `src/OmenCoreApp.Tests/Services/BloatwareManagerServiceTests.cs`
- `src/OmenCoreApp.Tests/ViewModels/BloatwareManagerViewModelOutcomeTests.cs`

---

### Ryzen AI 9 Undervolt Support Gap Now Fails Safe (GitHub #103) ✅ Short-term Guard Added
**Severity:** High  
**Affects:** Ryzen AI 9 systems where Curve Optimizer writes are not yet validated in OmenCore  
**Reported:** GitHub #103

**Root cause:**  
Ryzen AI 9 (Zen 5 path) systems could appear tunable in UI but fail to apply stable Curve Optimizer writes in practice, leading to confusing "apply" failures rather than explicit capability messaging.

**Fixes:**
1. Added runtime signature guard for Ryzen AI 9 family/model (`Family 0x1A`, `Model 0x40+`) in CPU detection.
2. AMD undervolt provider now returns explicit message: `CPU Curve Optimizer is not yet supported on Ryzen AI 9 processors.` and blocks apply attempts.
3. Undervolt capability gating now treats this message as unsupported so apply controls are disabled while reason text remains visible.
4. Added regression tests for guard parsing with both decimal and hex family/model formats.

**Files:**
- `src/OmenCoreApp/Hardware/RyzenControl.cs`
- `src/OmenCoreApp/Hardware/AmdUndervoltProvider.cs`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp.Tests/Hardware/RyzenControlTests.cs`

---

### Max Fan Mode Sawtooth RPM Pattern Reduced (GitHub #37) ✅ Fixed
**Severity:** High  
**Affects:** Systems where max fan mode periodically dips RPM before countdown re-apply  
**Reported:** GitHub #37

**Root cause:**  
In WMI max mode, countdown-extension maintenance could repeatedly issue hard max re-apply writes. On affected firmware this creates visible RPM oscillation (high RPM, dip, then re-apply).

**Fixes:**
1. Updated `WmiFanController.CountdownExtensionCallback()` to use quieter max-mode maintenance:
- run maintenance on a wider interval instead of every timer tick,
- use level/RPM readback to determine whether max is still healthy,
- use `ExtendFanCountdown()` keepalive while healthy,
- only re-issue `SetFanMax(true)` after sustained low telemetry detection.
2. Updated `WmiFanController.SetMaxFanSpeed(bool)` to keep manual/max state and countdown behavior consistent with preset-driven max mode.
3. Updated `FanService.ApplyMaxCooling()` to skip redundant immediate `SetFanSpeed(100)` for WMI backends (still retained for non-WMI backends).
4. Added regression tests verifying:
- healthy max maintenance avoids hard max re-apply,
- max re-apply occurs only after sustained drop detection.

**Files:**
- `src/OmenCoreApp/Hardware/WmiFanController.cs`
- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp.Tests/Hardware/WmiV2VerificationTests.cs`

---

### Quick Status Bar Overlaps Telemetry Banner on Dashboard ✅ Fixed
**Severity:** Medium  
**Affects:** Users whose telemetry data is stale or degraded (the banner appears on top of the status bar)  
**Reported:** Internal audit

**Root cause:**  
In `DashboardView.xaml` the telemetry stale banner and the Quick Status Bar both occupied `Grid.Row="1"`. When the banner was visible both controls rendered on the same row.

**Fix:**  
Moved the Quick Status Bar to the existing unused `Grid.Row="2"`. No new row was needed — the grid already had six rows (0–5).

**File:** `src/OmenCoreApp/Views/DashboardView.xaml`

---

### `RgbNetSystemProvider.InitializeAsync` Blocks Thread with Synchronous Wait ✅ Fixed
**Severity:** Medium  
**Affects:** Users with RGB.NET-compatible devices (Corsair, Logitech, generic USB RGB)  
**Reported:** Internal audit (HP #11)

**Root cause:**  
`RgbNetSystemProvider.InitializeAsync()` declared a `Task` return type but was synchronous internally, calling `Task.Delay(250).Wait()` — a blocking wait that could cause a deadlock if called from a `SynchronizationContext`-bearing thread (e.g., the WPF UI thread).

**Fix:**  
Method signature changed to `async Task`. `Task.Delay(250).Wait()` replaced with `await Task.Delay(250)`. Removed trailing `return Task.CompletedTask` (now redundant in an `async Task` method).

**File:** `src/OmenCoreApp/Services/Rgb/RgbNetSystemProvider.cs`

---

### OMEN 16-xd0xxx (8BCD) RGB Turns Off After Applying Color Preset ✅ Fixed
**Severity:** High  
**Affects:** OMEN 16-xd0xxx users on affected BIOS revisions (including F.31 reports)  
**Reported:** Community report (Discord / roadmap HP #0b)

**Root cause:**  
The V2 keyboard apply path sent color-table updates first and brightness second. On affected BIOS revisions, sending brightness after the color command could reset visible keyboard state, making RGB appear off despite successful color writes. In parallel, the WMI color-table path did not explicitly re-arm backlight state before writing colors.

**Fix:**
1. In `KeyboardLightingServiceV2.ApplyProfileAsync()`, brightness is now applied first, followed by a short settle delay, and color/effect write is sent last.
2. In `WmiBiosBackend.SetZoneColorsAsync()`, backlight is explicitly enabled before `SetColorTable()` with a short activation delay.

This sequence avoids the post-color brightness clobber pattern and ensures color writes are visible on reset-prone firmware.

**Files:**
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/WmiBiosBackend.cs`

---

### Hybrid iGPU+dGPU GPU Power Display Shows Misleading Values ✅ Fixed
**Severity:** High  
**Affects:** Hybrid Optimus systems where dGPU is parked/inactive  
**Reported:** Reddit (OMEN 15 en-1036ax, roadmap HP #28)

**Root cause:**  
The inactive dGPU heuristic treated small non-zero GPU power readings as proof of active discrete telemetry. On hybrid systems this could keep the dGPU marked active and surface misleading wattage when the system was actually in Optimus power-saving mode.

**Fix:**
1. Updated `WmiBiosMonitor.SanitizeGpuTelemetry()` to require stronger discrete-activity signals before treating dGPU telemetry as active (temperature, clocks, load, VRAM usage, or materially high power).
2. When dGPU is classified inactive for consecutive reads, GPU load/power/clock/VRAM-used are now zeroed in cache to prevent stale or cross-source leakage.
3. Updated dashboard power summary to explicitly show `GPU: inactive (Optimus)` when GPU telemetry state is inactive.

**Files:**
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`

---

### Sync-over-Async Hardening (additional HP #11 progress) ✅ Improved
**Severity:** Medium  
**Affects:** General reliability and deadlock risk reduction  
**Reported:** Internal audit (HP #11)

**Changes:**
1. `PerformanceModeService` now uses an async helper (`VerifyPowerLimitsAndLogAsync`) instead of `ContinueWith` + `t.Result` for verification logging.
2. Replaced bounded `.Wait(...)+.Result` reads with `.GetAwaiter().GetResult()` after completion checks in:
- `ThermalSensorProvider`
- `LibreHardwareMonitorImpl`
- `WmiBiosMonitor` (CPU fallback read path)
3. Replaced timeout-gated `.Result` output reads with awaited reads in:
- `OmenGamingHubCleanupService`
4. Avoided potential UI-thread dispose deadlocks by running async shutdown on thread-pool with timeout in:
- `AudioReactiveRgbService.Dispose()`
- `ScreenColorSamplingService.Dispose()`
5. Refactored Razer session/effect sync wrappers to timeout helper calls (`WaitAsync` + explicit error handling), removing repeated `Wait(...)`/`.Result` usage in effect paths and initialization.
6. Replaced newly-detected bare `catch { }` blocks with explicit exception handling/logging in keyboard parsing, OMEN key WMI diagnostics, and worker-path detection helpers.

These updates reduce fragile continuation patterns and remove additional `.Result` usage while preserving existing behavior.

**Files:**
- `src/OmenCoreApp/Services/PerformanceModeService.cs`
- `src/OmenCoreApp/Hardware/ThermalSensorProvider.cs`
- `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- `src/OmenCoreApp/Services/OmenGamingHubCleanupService.cs`
- `src/OmenCoreApp/Services/AudioReactiveRgbService.cs`
- `src/OmenCoreApp/Services/ScreenColorSamplingService.cs`
- `src/OmenCoreApp/Razer/RazerService.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`
- `src/OmenCoreApp/Services/OmenKeyService.cs`
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`

---

### Release-Gate Baseline Test Failing After v3.3.1 Edits ✅ Fixed
**Severity:** Critical (blocks CI)  
**Affects:** Development / CI pipeline  
**Reported:** Internal — test suite

**Root cause:**  
Eight line numbers in the `KnownBareCatchViolations` HashSet in `ReleaseGateCodeHygieneTests.cs` referred to pre-v3.3.1 positions in `FanControllerFactory.cs` and `WmiBiosMonitor.cs`. After edits landed in v3.3.1, those methods shifted by 8–36 lines each, causing the baseline to flag legitimate existing bare-catch blocks as new violations and fail the gate.

**Fix:**  
Updated eight entries in `KnownBareCatchViolations` to their current line numbers:
- `FanControllerFactory.cs`: 1157→1165, 1189→1197, 1207→1215
- `WmiBiosMonitor.cs`: 329→332, 560→563, 971→1007, 1442→1478, 1485→1521

**File:** `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs`

---

## 📚 Documentation

### Antivirus FAQ — Bitdefender Section Added
**File:** `docs/ANTIVIRUS_FAQ.md`

Added a dedicated section for the Bitdefender `Gen:Application.Venus.Cynthia.Winring` detection:
- Explains why PawnIO triggers the `Venus.Cynthia.Winring` heuristic family
- Step-by-step Bitdefender exclusion instructions (Protection → Antivirus → Settings → Exclusions → Manage Exclusions)
- Lists the exact file paths to add as exclusions
- Links to Bitdefender's false-positive submission portal

**Reported:** Discord, April 2026 (user Logos, Bitdefender flagging `OmenCore.sys` as `Gen:Application.Venus.Cynthia.Winring.17ay1@auVvKTci`)

### README — Antivirus Note Updated
**File:** `README.md`

The generic "WinRing0 flagged by Windows Defender" note now includes the Bitdefender detection name and links to `ANTIVIRUS_FAQ.md` for all vendors.

### README — Release Reference Sync
**File:** `README.md`

Updated stale `3.3.0` references in architecture/build/docs sections to `3.4.0`, added a `v3.4.0` version-history entry, and normalized messaging to "No outbound telemetry".

### v3.4.0 QA Checklist Added
**File:** `qa/v3.4.0-checklist.md`

Added a focused manual release checklist covering fan/thermal flows, telemetry resilience, model identity, packaging verification, and updater safety checks.

---

## 🧩 Support Matrix Completion

### Model Capability Database Finalization (HP #3) ✅ Fixed
**Severity:** High  
**Affects:** Model detection accuracy, capability fallback behavior, and keyboard lighting method selection  
**Reported:** Community issue batch + v3.4.0 roadmap audit

**What was missing:**  
The v3.4 support matrix still lacked explicit capability entries for several community-reported Product IDs (`8A44`, `8A3E`, `8E41`) and did not actually include `8C58` despite roadmap verification notes. On those systems, detection could fall back to generic family defaults and expose controls that are suboptimal or unsafe for the specific hardware profile.

**Fix:**
1. Added missing entries to `ModelCapabilityDatabase`:
- `8A44` — OMEN 16-n0xxx (2022) AMD
- `8A3E` — Victus 15-fb0xxx (2022) AMD
- `8A26` — Victus 16-d1xxx
- `8C58` — OMEN Transcend 14-fb1xxx (board-family entry)
- `8E41` — OMEN Transcend 14-fb1xxx
2. Added matching keyboard entries to `KeyboardModelDatabase` for:
- `8A44` (FourZone / ColorTable2020)
- `8A3E` (BacklightOnly)
- `8E41` (PerKeyRgb / NewWmi2023)
3. Added regression tests to lock behavior:
- Capability lookups for all newly added IDs
- Transcend safety expectations (`SupportsFanControlEc = false`, `SupportsFanCurves = false`)
- Keyboard lookup coverage for `8A44`, `8A3E`, `8E41`

**Safety choices:**  
All new entries are marked `UserVerified = false` and use conservative defaults. Transcend board-family entries (`8C58`, `8E41`) explicitly disable direct EC fan control in the capability profile to align with known firmware/EC mapping risk and existing Linux safety guidance.

### GitHub #117 — OMEN MAX 16-ak0xxx (8D87) Model + Keyboard Mapping ✅ Fixed
**Severity:** High  
**Affects:** Model capability detection and keyboard profile selection on OMEN MAX 16-ak0xxx  
**Reported:** GitHub #117

**What was missing:**  
Product ID `8D87` was not explicitly mapped in either `ModelCapabilityDatabase` or `KeyboardModelDatabase`. On affected systems, OmenCore could resolve to generic fallback behavior, which weakens capability accuracy and increases the chance of incorrect keyboard feature assumptions.

**Fix:**
1. Added Product ID `8D87` entry to `ModelCapabilityDatabase` with conservative `UserVerified = false` defaults and MAX-family notes for follow-up hardware verification.
2. Added Product ID `8D87` entry to `KeyboardModelDatabase` so keyboard method selection does not rely on family fallback.
3. Added regression tests to lock both capability and keyboard lookup coverage for `8D87`.

---

## 🗂 Files Changed

| File | Change |
|------|--------|
| `src/OmenCoreApp/Hardware/WmiFanController.cs` | Added `case FanMode.Manual` → `FanMode.Default` in `MapPresetToFanMode` |
| `src/OmenCoreApp/Views/FanControlView.xaml` | Added 4th grid row; moved curve editor to `Grid.Row="3"` |
| `src/OmenCoreApp/Controls/FanCurveEditor.xaml.cs` | Snap-after-clamp fix; stale-index guard in `Point_MouseMove` |
| `src/OmenCoreApp/Services/OmenKeyService.cs` | Added LaunchApp scan-code hardening (`0xE045` allow-list; rejects `0xE046`/`0x0046`/`0x009D`) plus `VK_SNAPSHOT` guard for PrtSc |
| `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs` | Added LaunchApp brightness-conflict regressions and dedicated OMEN scan allow-path tests |
| `src/OmenCoreApp/Views/DashboardView.xaml` | Quick Status Bar moved to `Grid.Row="2"` |
| `src/OmenCoreApp/Services/Rgb/RgbNetSystemProvider.cs` | `Task.Delay(250).Wait()` → `await Task.Delay(250)`; method is now `async Task` |
| `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs` | Reordered profile apply sequence (brightness first, colors/effect last) |
| `src/OmenCoreApp/Services/KeyboardLighting/WmiBiosBackend.cs` | Ensures backlight is enabled before WMI color-table writes |
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | Improved hybrid dGPU inactivity heuristic; clears misleading GPU metrics when inactive |
| `src/OmenCoreApp/ViewModels/DashboardViewModel.cs` | Shows `GPU: inactive (Optimus)` in power summary when dGPU telemetry is inactive |
| `src/OmenCoreApp/Services/PerformanceModeService.cs` | Replaced `ContinueWith`/`t.Result` verification pattern with async helper |
| `src/OmenCoreApp/Hardware/ThermalSensorProvider.cs` | Replaced bounded `.Result` read with `GetAwaiter().GetResult()` |
| `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs` | Replaced bounded `.Result` read with `GetAwaiter().GetResult()` |
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | Replaced timeout-bounded `.Result` read with `GetAwaiter().GetResult()` in fallback path |
| `src/OmenCoreApp/Services/OmenGamingHubCleanupService.cs` | Replaced timeout-gated `.Result` reads with awaited output/error reads |
| `src/OmenCoreApp/Services/AudioReactiveRgbService.cs` | Dispose now stops service on thread-pool context to avoid UI-thread deadlock |
| `src/OmenCoreApp/Services/ScreenColorSamplingService.cs` | Dispose now stops service on thread-pool context to avoid UI-thread deadlock |
| `src/OmenCoreApp/Razer/RazerService.cs` | Added timeout helper for async session/effect calls; removed repeated `Wait(...)`/`.Result` wrappers |
| `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs` | Replaced bare catch in color parser with explicit exception handling |
| `src/OmenCoreApp/Services/OmenKeyService.cs` | Replaced bare catch in WMI property-dump path with explicit exception handling |
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | Replaced bare catches in worker-launch helper paths with explicit exception handling |
| `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs` | Updated 8 stale line numbers in `KnownBareCatchViolations` |
| `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` | Added Product ID entries: `8A44`, `8A3E`, `8A26`, `8C58`, `8E41`, `8D87` |
| `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs` | Added keyboard entries for `8A44`, `8A3E`, `8E41`, `8D87` |
| `src/OmenCoreApp.Tests/Hardware/ModelCapabilityDatabaseTests.cs` | Added lookup and safety regression tests for new Product IDs including `8D87` |
| `src/OmenCoreApp.Tests/Services/KeyboardModelDatabaseTests.cs` | Added lookup tests for `8A44`, `8A3E`, `8E41`, `8D87` |
| `docs/ANTIVIRUS_FAQ.md` | Bitdefender `Gen:Application.Venus.Cynthia.Winring` section |
| `README.md` | Antivirus note expanded; stale release references synced to v3.4.0; telemetry wording clarified as "No outbound telemetry" |
| `CHANGELOG.md` | Added v3.4.0 scope-freeze note for non-core utility surfaces (bug-fix only) |
| `qa/v3.4.0-checklist.md` | Added focused manual release checklist for v3.4.0 validation |
| `config/default_config.json` | Reset `coreMv`/`cacheMv` to 0; `HP Omen Background` disabled by default; description corrected |
| `src/OmenCoreApp/Services/FanService.cs` | 5-second transition window holds RPM during BIOS mode handoff; `Dispatcher.Invoke` → `BeginInvoke` on startup |
| `src/OmenCoreApp/ViewModels/FanControlViewModel.cs` | `ApplyPresetAsync` shows "(transitioning)" status during mode handoff |
| `src/OmenCoreApp/ViewModels/MainViewModel.cs` | `InitializeServicesAsync`/`InitializeGameProfilesAsync` changed from `async void` to `async Task` |
| `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs` | `OptimizationItem.Toggle()` changed from `async void` to `async Task` |
| `src/OmenCoreApp/Views/SystemOptimizerView.xaml.cs` | `OnToggleClicked` awaits `Toggle()` as `async void` event handler |
| `src/OmenCoreApp/ViewModels/GameProfileManagerViewModel.cs` | 4 `async void` methods converted to `async Task`; wired via `AsyncRelayCommand` |
| `src/OmenCoreApp/ViewModels/LightingViewModel.cs` | 3 `async void` methods renamed and converted to `async Task`; fired as fire-and-forget |
| `src/OmenCoreApp/Views/InputPromptWindow.xaml` | Applied app dark theme (`SurfaceDarkBrush`, `ModernTextBox`, `ModernButton`) |
| `src/OmenCoreApp/Views/GameProfileManagerView.xaml` | Replaced hardcoded hex colors in local `Button`/`TextBox`/`ComboBox` styles with `StaticResource` theme brushes |
| `src/OmenCoreApp/Views/GameLibraryView.xaml` | Replaced hardcoded hex `Background`/`BorderBrush` with `StaticResource` theme brushes |
| `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs` | Updated `FanService.cs:1816` → `1846` after HP #27 line shift |
| `.github/workflows/ci.yml` | Removed stale `v2.0-dev` branch; dependent jobs now build independently; actions updated to v4 |
| `.github/workflows/release.yml` | Rewritten to a single valid Linux job; Linux uses `build-linux-package.ps1`; artifact publishing updated to `.zip` + `.sha256`; actions updated to v4 |
| `.github/workflows/linux-qa.yml` | `PublishTrimmed` corrected from `true` to `false`; hardcoded framework output paths replaced with a stable publish directory |
| `.github/workflows/alpha.yml` | Rewritten as split Windows/Linux build jobs; actions updated to v4; alpha artifacts renamed consistently; Linux uses `build-linux-package.ps1` |
| `VERSION.txt` | Bumped active release version source from `3.3.1` to `3.4.0` |
| `src/OmenCoreApp/OmenCoreApp.csproj` | Bumped embedded app version metadata to `3.4.0` |
| `src/OmenCore.HardwareWorker/OmenCore.HardwareWorker.csproj` | Bumped embedded worker version metadata to `3.4.0` |
| `src/OmenCore.Linux/OmenCore.Linux.csproj` | Bumped embedded CLI version metadata to `3.4.0` |
| `src/OmenCore.Avalonia/OmenCore.Avalonia.csproj` | Bumped embedded GUI version metadata to `3.4.0` |
| `build-installer.ps1` | Added Windows publish version injection; removed dead Linux packaging branches; removed empty helper-script invocation |
| `installer/OmenCoreInstaller.iss` | ISS fallback `MyAppVersion` updated from `3.3.1` to `3.4.0`; obsolete directive removed; uninstall no longer manages per-user AppData |
| `src/OmenCore.Desktop/README.md` | Added archival guidance declaring project out of shipping/release scope |
| `src/OmenCore.Desktop/OmenCore.Desktop.csproj` | Added explicit archived-project marker comment to prevent release-cycle version bumping |
| `src/OmenCore.Avalonia/Services/IHardwareService.cs` | Removed unsupported `PerformanceMode.Custom` to align with real Linux backend modes |
| `src/OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs` | Replaced raw int-to-enum cast with explicit mapping and validation for performance mode selection |
| `src/OmenCore.Avalonia/Views/SystemControlView.axaml` | Removed unsupported "Custom" performance button; now shows Quiet/Balanced/Performance only |
| `src/OmenCore.Avalonia/ViewModels/FanControlViewModel.cs` | Added capability-aware fan-curve gating, one-shot apply messaging, and profile-only/telemetry warning state |
| `src/OmenCore.Avalonia/Views/FanControlView.axaml` | Added capability warning banner, hid manual curve controls when unsupported, relabeled apply action to `Apply Once` |
| `src/OmenCore.Linux/Hardware/LinuxEcController.cs` | Added synchronized EC byte I/O critical section for `ReadByte()` / `WriteByte()` |
| `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs` | Added serialized Linux sysfs polling/control I/O lock and lock-safe performance-mode fallback path |
| `src/OmenCoreApp/Services/AutoUpdateService.cs` | Added download/check serialization, safe timer callback wrapper, `.partial` staging, and stale update-cache cleanup |
| `src/OmenCoreApp.Tests/Services/AutoUpdateServiceTests.cs` | Added regression tests for stale-download cleanup and preserve-path behavior |

---

### Avalonia Performance Mode Mismatch (Custom vs Linux backend) ✅ Fixed
**Severity:** Medium
**Affects:** Linux Avalonia GUI performance mode selection
**Reported:** Roadmap HP #16

**Root cause:**
Avalonia exposed a `Custom` performance mode that had no backend mapping in Linux profile control. Selecting it flowed through a raw `(PerformanceMode)value` cast path, and then fell through to balanced behavior in kernel profile resolution, causing UI intent and backend action to diverge.

**Fixes:**
1. Removed `PerformanceMode.Custom` from Avalonia service enum.
2. Removed the `Custom` mode action from `SystemControlView.axaml`.
3. Replaced fragile index casting in `SystemControlViewModel` with explicit, validated mappings for index-to-mode and name-to-mode conversion.
4. Preserved Linux backend compatibility by keeping low-power profile variants (`low-power` / `cool` / `quiet`) mapped to Avalonia `Quiet` through `LinuxHardwareService` translation.

**Files:**
- `src/OmenCore.Avalonia/Services/IHardwareService.cs`
- `src/OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs`
- `src/OmenCore.Avalonia/Views/SystemControlView.axaml`

---

### Avalonia Linux Fan-Curve UX Contract Alignment (Option B) ✅ Improved
**Severity:** Medium
**Affects:** Linux Avalonia fan-control clarity on profile-only/telemetry-only systems
**Reported:** Roadmap Critical #2 follow-up

**Root cause:**
Fan-curve controls were presented with language implying ongoing/continuous control even though `FanCurveService.ApplyAsync()` performs a one-shot apply from current temperatures. On systems without manual fan target interfaces, curve controls remained visible and users had no explicit explanation for why controls would not behave as expected.

**Fixes:**
1. Added capability-aware gating in `FanControlViewModel` using `SystemCapabilities.FanControlCapabilityClass` and `SupportsFanControl`.
2. Added explicit warning messaging for `profile-only`, `telemetry-only`, and `unsupported-control` capability classes.
3. Hid manual curve editing/apply controls when direct fan control is unavailable.
4. Relabeled the action button to `Apply Once` and added tooltip guidance clarifying one-shot behavior.

**Files:**
- `src/OmenCore.Avalonia/ViewModels/FanControlViewModel.cs`
- `src/OmenCore.Avalonia/Views/FanControlView.axaml`

---

### Linux EC/sysfs Concurrency During Polling and Control Operations ✅ Fixed
**Severity:** High
**Affects:** Linux fan/performance control reliability under concurrent polling + user actions
**Reported:** Roadmap HP #17

**Root cause:**
Linux EC/sysfs access paths were unsynchronized across concurrent operations. EC byte seek/read/write calls could interleave, and Avalonia polling could overlap with profile/fan writes, creating avoidable race windows and inconsistent control behavior on sensitive firmware paths.

**Fixes:**
1. Added a shared EC lock in `LinuxEcController` around `ReadByte()` and `WriteByte()` file-stream operations.
2. Added a serialized `SemaphoreSlim` guard in `LinuxHardwareService` for `GetStatusAsync` and all control writes (`SetPerformanceModeAsync`, `SetCpuFanSpeedAsync`, `SetGpuFanSpeedAsync`, keyboard lighting updates).
3. Refactored performance profile write path to a lock-safe core method (`SetPerformanceModeCoreAsync`) so fan fallback does not deadlock under the serialized lock.

**Files:**
- `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`

---

### Auto-update Download Concurrency and Temp Cache Cleanup ✅ Fixed
**Severity:** High
**Affects:** Update reliability and `%TEMP%\OmenCore\Updates` disk usage
**Reported:** Internal roadmap audit (HP #13)

**Root cause:**
`AutoUpdateService` allowed overlapping update flows across timer-triggered checks and manual update actions. `DownloadUpdateAsync` had no serialization guard, so concurrent calls could race on the same target file path. The timer callback was wired using an async lambda over `Elapsed`, and stale installer files in `%TEMP%\OmenCore\Updates` were never reclaimed.

**Fixes:**
1. Added serialized guards:
- `_downloadSemaphore` around the full `DownloadUpdateAsync` path
- `_scheduledCheckSemaphore` around scheduled check execution
2. Replaced async timer lambda wiring with explicit callback methods (`CheckTimerElapsed` and `RunScheduledCheckAsync`) with centralized exception handling.
3. Switched download write path to `.partial` staging and atomic finalize (`File.Move(..., overwrite: true)`) only after hash verification.
4. Added stale cache cleanup (`CleanupStaleDownloads`) on service startup and after successful download, including partial-file cleanup and retention-based pruning.
5. Updated disposal path to unsubscribe timer handler and dispose synchronization primitives.
6. Added targeted unit tests validating stale-file cleanup and preserve-path behavior in `AutoUpdateServiceTests`.

**Files:**
- `src/OmenCoreApp/Services/AutoUpdateService.cs`
- `src/OmenCoreApp.Tests/Services/AutoUpdateServiceTests.cs`

---

### Dangerous Undervolt Defaults in `default_config.json` ✅ Fixed
**Severity:** High
**Affects:** New installations (fresh config generation)
**Reported:** Internal audit (HP #18)

**Root cause:**
`default_config.json` shipped with `undervolt.coreMv: -90` and `cacheMv: -60` as starting values. On a fresh install, any user who applied defaults without reviewing the undervolt section could silently apply an aggressive CPU voltage offset. On sensitive silicon this can cause instability (BSODs, random hangs) without obvious connection to the configuration change.

Additionally, `HP Omen Background` was listed in the bloatware database with `enabledByDefault: true` and the description "OEM telemetry" — an inaccurate description of the HP OMEN Gaming Hub background service, and an unexpectedly aggressive default that would disable it on first run.

**Fixes:**
1. `undervolt.defaultOffset.coreMv` and `cacheMv` reset to `0`. Users who want to undervolt must explicitly choose a value.
2. `HP Omen Background` `enabledByDefault` changed to `false`. Description updated to "HP OMEN Gaming Hub background service (OEM)".

**File:** `config/default_config.json`

---

### Fan RPM Drops to Zero During Preset/Mode Switch ✅ Fixed
**Severity:** High
**Affects:** All users who switch fan presets or apply a custom curve
**Reported:** Internal audit (HP #27)

**Root cause:**
During a WMI BIOS fan mode handoff, the BIOS briefly drives both RPM and duty cycle to 0 while transitioning between control modes. The existing zero-RPM guard in the fan monitor loop only caught cases where `rpm == 0 && duty > 0`, leaving the BIOS handoff window (where both are 0) unguarded. This caused the UI to momentarily show 0 RPM and 0% duty, and could trigger spurious fan-fault heuristics.

Additionally, `FanService.Start()` called `App.Current.Dispatcher.Invoke(...)` (blocking) for initial telemetry population, which could stall the calling thread on startup.

**Fixes:**
1. Added a 5-second transition window (`_fanModeTransitioning` flag, `_fanTransitionUntil` timestamp) set in `ApplyPreset()` before the WMI call. During this window, the monitor loop holds the last known non-zero RPM instead of surfacing 0.
2. `ApplyPresetAsync` in `FanControlViewModel` now sets `CurveApplyStatus = "Applying '…' (transitioning)"` before calling `ApplyPreset()`.
3. `FanService.Start()` initial telemetry dispatch changed from `Dispatcher.Invoke` to `Dispatcher.BeginInvoke`.

**Files:**
- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`

---

### `async void` Non-Event-Handler Methods Crash Process on Unhandled Exception ✅ Fixed
**Severity:** High
**Affects:** All users — unhandled exceptions in async void methods terminate the process without a crash dialog
**Reported:** Internal audit (HP #10)

**Root cause:**
Eight `async void` methods across four ViewModels were not event handlers and therefore had no exception sink. An unhandled exception in any of them would propagate to the `TaskScheduler.UnobservedTaskException` handler (which only logs) or directly crash the process via `UnhandledException`.

**Fixes:**
All eight methods converted from `async void` to `async Task`, with call sites updated to fire-and-forget or `AsyncRelayCommand`:

- `MainViewModel.InitializeServicesAsync()` / `InitializeGameProfilesAsync()` → `async Task`, called as `_ = Method()`
- `SystemOptimizerViewModel.OptimizationItem.Toggle()` → `async Task`; `SystemOptimizerView.xaml.cs` `OnToggleClicked` awaits it
- `GameProfileManagerViewModel`: `DeleteProfile`, `ImportProfiles`, `ExportProfiles`, `Save` → renamed to `*Async`, wired via `AsyncRelayCommand`
- `LightingViewModel`: `ApplyTemperatureBasedLighting`, `ApplyThrottlingLighting`, `ApplyPerformanceModeLighting` → renamed to `*Async`, called as fire-and-forget

**Files:**
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs`
- `src/OmenCoreApp/Views/SystemOptimizerView.xaml.cs`
- `src/OmenCoreApp/ViewModels/GameProfileManagerViewModel.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

---

### Dialog Windows Render with White/Light Flash (Un-themed) ✅ Fixed
**Severity:** Medium
**Affects:** All users — dialogs briefly show light-theme background before painting
**Reported:** Internal audit (HP #13)

**Root cause:**
Three views were rendered without app theme resources:
- `InputPromptWindow.xaml`: plain WPF default light-theme window (white background, default controls)
- `GameProfileManagerView.xaml`: `Window.Resources` local `Button`/`TextBox`/`ComboBox` styles used hardcoded hex colors (`#2D2D30`, `#3F3F46`, `#3E3E42`, `#FFFFFF`) instead of `StaticResource` theme brushes
- `GameLibraryView.xaml`: UserControl `Background="#1a1a1a"` and card styles using `#252525`, `#3a3a3a`, `#2d2d2d` instead of theme brushes

**Fixes:**
1. `InputPromptWindow.xaml`: Background → `{StaticResource SurfaceDarkBrush}`, Foreground → `{StaticResource TextPrimaryBrush}`, BorderBrush → `{StaticResource BorderBrush}`, TextBox → `{StaticResource ModernTextBox}`, Buttons → `{StaticResource ModernButton}`.
2. `GameProfileManagerView.xaml`: All hardcoded hex colors in local style block replaced with `{StaticResource SurfaceMediumBrush}`, `{StaticResource SurfaceLightBrush}`, `{StaticResource BorderBrush}`, `{StaticResource TextPrimaryBrush}`.
3. `GameLibraryView.xaml`: `Background="#1a1a1a"` → `{StaticResource SurfaceDarkBrush}`, card background → `{StaticResource SurfaceMediumBrush}`, hover → `{StaticResource SurfaceLightBrush}`, badge background → `{StaticResource SurfaceLightBrush}`.

**Files:**
- `src/OmenCoreApp/Views/InputPromptWindow.xaml`
- `src/OmenCoreApp/Views/GameProfileManagerView.xaml`
- `src/OmenCoreApp/Views/GameLibraryView.xaml`

---

## 🔖 Upgrade Notes

- No breaking changes. Drop-in replacement for v3.3.1.
- Users who experienced CPU thermal throttling on custom fan curves (approximately 25 W cap) should see correct TDP after updating.
- No configuration migration required.
