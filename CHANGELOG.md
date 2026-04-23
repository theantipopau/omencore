# Changelog

All notable changes to OmenCore will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [3.4.0] - TBD — Fan Curve Fix, Profile Selector, PrtSc Hook Guard, AV FAQ

### Scope Freeze

- v3.4.0 treats Bloatware Manager, Memory Optimizer, System Optimizer, and Game Library as bug-fix-only surfaces.
- New feature work for those areas is deferred to post-3.4.0 planning.

### Fixed

- **[HIGH] Custom fan curve locks CPU to ~25 W** (`WmiFanController.cs`) — `FanMode.Manual` fell through to `default` branch in `MapPresetToFanMode`, which selected `HpWmiBios.FanMode.Cool` for quiet curves, imposing a ~25 W TDP cap. Now maps explicitly to `FanMode.Default`.
- **[HIGH] Fan profile selector (Max/Extreme/Gaming/Auto/Silent/Custom) hidden** (`FanControlView.xaml`) — Profile cards and curve editor both at `Grid.Row="2"` in a 3-row grid. Added 4th row; curve editor moved to Row 3.
- **[HIGH] Fan curve drag crashes at temperature boundary** (`FanCurveEditor.xaml.cs`) — Snap before clamp caused rounding to exceed neighbour constraint, throwing `ArgumentException`. Fix: clamp first, then snap. Also added stale-index guard.
- **[HIGH] Print Screen / STAMP key does not open Snipping Tool** (`OmenKeyService.cs`) — Added explicit `VK_SNAPSHOT` (0x2C) guard in `TryGetNeverInterceptReason` so the hook never interferes with the key and any coincident WMI BIOS event is suppressed.
- **[HIGH] Fn+F2/F3 brightness keys can still toggle OmenCore on some models (GitHub #74)** (`OmenKeyService.cs`, `HotkeyAndMonitoringTests.cs`) — `VK_LAUNCH_APP1`/`VK_LAUNCH_APP2` detection now uses a strict dedicated scan whitelist (`0xE045`) and explicitly rejects known brightness-conflict scans (`0xE046`, `0x0046`, `0x009D`), with regression tests for both reject and allow paths.
- **[HIGH] Bloatware removal could appear as a silent no-op on Victus models (GitHub #107)** (`BloatwareManagerService.cs`, `BloatwareManagerViewModel.cs`, `BloatwareManagerView.xaml`, `BloatwareManagerServiceTests.cs`, `BloatwareManagerViewModelOutcomeTests.cs`) — Removal now reports explicit per-item `removed`/`skipped`/`failed` outcomes with detail text, AppX removal verifies pre/post presence across current/all/provisioned scopes, no-op targets are surfaced as skipped instead of silent success, bulk summaries separate skipped items from true removals, and the UI now shows a visible per-item result detail column.
- **[HIGH] Ryzen AI 9 undervolt path now fails safe with explicit unsupported messaging (GitHub #103)** (`RyzenControl.cs`, `AmdUndervoltProvider.cs`, `SystemControlViewModel.cs`, `RyzenControlTests.cs`) — Added family/model guard (`Family 0x1A`, `Model 0x40+`) for Ryzen AI 9 Curve Optimizer path, returns explicit "not yet supported" status, disables apply controls through capability gating, and adds regression coverage for decimal/hex CPU signature parsing.
- **[HIGH] Max fan mode sawtooth RPM pattern reduced on affected firmware (GitHub #37)** (`WmiFanController.cs`, `FanService.cs`, `WmiV2VerificationTests.cs`) — Replaced timer-tick `SetFanMax(true)` spam with max-mode keepalive + sustained-drop detection; max is now re-applied only after confirmed low telemetry, and WMI max apply path no longer sends a redundant immediate `SetFanSpeed(100)` pulse.
- **[MEDIUM] Quick Status Bar overlaps telemetry banner on Dashboard** (`DashboardView.xaml`) — Both at `Grid.Row="1"`; status bar moved to unused Row 2.
- **[MEDIUM] `RgbNetSystemProvider.InitializeAsync` blocks thread** (`RgbNetSystemProvider.cs`) — `Task.Delay(250).Wait()` replaced with `await Task.Delay(250)`; method now truly `async Task`.
- **[HIGH] OMEN 16-xd0xxx RGB turns off after applying presets** (`KeyboardLightingServiceV2.cs`, `WmiBiosBackend.cs`) — Keyboard apply sequence reordered (brightness first, colors last) and WMI backlight is now explicitly enabled before color-table writes.
- **[HIGH] Hybrid iGPU+dGPU GPU power display corrected** (`WmiBiosMonitor.cs`, `DashboardViewModel.cs`) — Improved dGPU inactive detection on Optimus systems and explicit `GPU: inactive (Optimus)` dashboard messaging instead of misleading wattage.
- **[MEDIUM] Additional sync-over-async hardening** (`PerformanceModeService.cs`, `ThermalSensorProvider.cs`, `LibreHardwareMonitorImpl.cs`, `WmiBiosMonitor.cs`, `OmenGamingHubCleanupService.cs`, `RazerService.cs`) plus dispose deadlock hardening in `AudioReactiveRgbService.cs` and `ScreenColorSamplingService.cs` — Removed `ContinueWith`/`t.Result` patterns, replaced more bounded `.Result` reads, and centralized timeout-safe async bridging for Razer effect/session paths.
- **[QUALITY] Release-gate hygiene compliance maintained** (`KeyboardLightingServiceV2.cs`, `OmenKeyService.cs`, `WmiBiosMonitor.cs`) — Replaced bare `catch { }` with explicit exception handling/logging so `NoBareCatchBraces` gate remains green.
- **[CRITICAL/CI] Release-gate baseline test failing** (`ReleaseGateCodeHygieneTests.cs`) — 8 stale line numbers in `KnownBareCatchViolations` updated after v3.3.1 line shifts.
- **[HIGH] Model support matrix gaps (HP #3) closed** (`ModelCapabilityDatabase.cs`, `KeyboardModelDatabase.cs`) — Added missing Product IDs `8A44`, `8A3E`, `8A26`, `8C58`, `8E41` with conservative `UserVerified=false` profiles and regression tests.
- **[HIGH] OMEN MAX 16-ak0xxx model/keyboard fallback (GitHub #117)** (`ModelCapabilityDatabase.cs`, `KeyboardModelDatabase.cs`, `ModelCapabilityDatabaseTests.cs`, `KeyboardModelDatabaseTests.cs`) — Added Product ID `8D87` mappings so model capability and keyboard profile selection no longer fall back to generic defaults on this platform.

### Documentation

- **AV FAQ** (`docs/ANTIVIRUS_FAQ.md`) — Added Bitdefender `Gen:Application.Venus.Cynthia.Winring` section with exclusion steps and false-positive submission link.
- **README** (`README.md`) — Antivirus note expanded with Bitdefender detection name.
- **README** (`README.md`) — Synced stale `3.3.0` release references to `3.4.0` in architecture/build/docs sections, and clarified wording as "No outbound telemetry".
- **Release QA** (`qa/v3.4.0-checklist.md`) — Added a focused v3.4.0 manual checklist for core thermal/fan, telemetry, model identity, packaging, and updater safety validation.

- **[HIGH] Dangerous default undervolt config** (`default_config.json`) — `coreMv`/`cacheMv` reset from `-90`/`-60` to `0`; `HP Omen Background` bloatware toggle disabled by default with corrected description (HP #18).
- **[HIGH] Fan RPM drops to 0 during preset/mode switch** (`FanService.cs`, `FanControlViewModel.cs`) — Added 5-second transition window; monitor holds last non-zero RPM during BIOS handoff. `Dispatcher.Invoke` on startup changed to `BeginInvoke` (HP #27).
- **[HIGH] `async void` non-event-handler methods crash process** (`MainViewModel.cs`, `SystemOptimizerViewModel.cs`, `GameProfileManagerViewModel.cs`, `LightingViewModel.cs`) — 8 `async void` methods converted to `async Task` + fire-and-forget or `AsyncRelayCommand` (HP #10).
- **[MEDIUM] Dialog windows render un-themed** (`InputPromptWindow.xaml`, `GameProfileManagerView.xaml`, `GameLibraryView.xaml`) — All hardcoded hex colors replaced with `StaticResource` app theme brushes (HP #13).
- **[CI] Release-gate baseline** (`ReleaseGateCodeHygieneTests.cs`) — `FanService.cs:1816` baseline entry updated to `1846` after HP #27 line shift.
- **[MEDIUM] CI/CD pipeline fixes** (`ci.yml`, `release.yml`, `linux-qa.yml`, `alpha.yml`) — Stale `v2.0-dev` branch removed; `wmi-v2-tests` and `integration-tests` jobs now build independently to fix `--no-build` on fresh runners; `release.yml` Linux step now uses `build-linux-package.ps1` (version injection, SHA256, `.zip`); `linux-qa.yml` `PublishTrimmed` corrected to `false`; all actions updated from v3 to v4 (HP #14).
- **[MEDIUM] Windows build version injection** (`build-installer.ps1`, `installer/OmenCoreInstaller.iss`) — Added `-p:Version`, `-p:AssemblyVersion`, `-p:FileVersion` to the Windows app and hardware-worker publish steps; ISS fallback version updated to `3.4.0` (HP #15).
- **[MEDIUM] Avalonia/Linux performance-mode consistency** (`IHardwareService.cs`, `SystemControlViewModel.cs`, `SystemControlView.axaml`) — Removed unsupported `Custom` mode, replaced fragile index-to-enum cast with explicit mapping, and kept Linux low-power (`low-power`/`cool`/`quiet`) translation under `Quiet` (HP #16).
- **[MEDIUM] Avalonia Linux fan-curve contract alignment** (`FanControlViewModel.cs`, `FanControlView.axaml`) — Fan-curve controls are now capability-gated, unsupported classes show explicit warnings, and apply action is labeled as one-shot (`Apply Once`) with tooltip guidance to avoid implying continuous control behavior (Critical #2 Option B).
- **[HIGH] Linux EC/sysfs concurrency protection** (`LinuxEcController.cs`, `LinuxHardwareService.cs`) — Added synchronization for EC byte I/O and serialized Avalonia polling/control sysfs access to prevent overlapping read/write operations during fan/performance changes (HP #17).
- **[HIGH] Auto-update download concurrency + cache cleanup hardening** (`AutoUpdateService.cs`, `AutoUpdateServiceTests.cs`) — Added serialized download/check guards, replaced async timer lambda with safe callback wrapping, introduced `.partial` staging + atomic finalize, cleanup of stale files under `%TEMP%\OmenCore\Updates`, and regression tests for stale-file/prune-preserve behavior (HP #13).
- **[MEDIUM] Release packaging cleanup** (`VERSION.txt`, `OmenCoreApp.csproj`, `OmenCore.HardwareWorker.csproj`, `OmenCore.Linux.csproj`, `OmenCore.Avalonia.csproj`, `build-installer.ps1`, `OmenCoreInstaller.iss`, `src/OmenCore.Desktop/README.md`, `OmenCore.Desktop.csproj`) — Active version sources bumped to `3.4.0`; `build-installer.ps1` is now explicitly Windows-only; the empty `installer/download-librehw.ps1` helper was removed; obsolete Inno Setup directives and per-user uninstall cleanup were removed to match the current runtime storage model; desktop prototype scope is now explicitly marked archived and excluded from release-version maintenance.

→ Full details: [docs/CHANGELOG_v3.4.0.md](docs/CHANGELOG_v3.4.0.md)

---

## [3.3.1] - 2026-04-16 — Hotfix: Background-Thread Crash, RGB Backlight, Model Support

> **Hotfix for v3.3.0.** This release fixes the root cause of crashes introduced in 3.3.0
> that affected all locales. Users on 3.3.0 should update immediately.

### Fixed

- **[CRITICAL] App crashes on startup on all Windows locales (GH #109, #110 — Italian, Korean confirmed; likely all non-English)**
  Root cause was in `HardwareMonitoringService.GetEffectiveCadenceInterval()`, newly introduced in 3.3.0.
  The method accessed `Application.Current.MainWindow.IsVisible` and `window.WindowState` — both WPF
  `DependencyObject` properties that may only be read from the thread that owns them (the UI thread).
  `GetEffectiveCadenceInterval()` was called from the background monitor loop (`Task.Run`), which runs on a
  thread-pool thread. Accessing these properties from the wrong thread throws
  `InvalidOperationException: The calling thread cannot access this object because a different thread owns it.`
  On English Windows the unobserved-task-exception filter in `App.xaml.cs` suppressed it via a message
  substring match (`"different thread owns it"`). On localised Windows (Italian, Korean, German, …) the
  WPF exception message is translated, so the English-only filter never fired and the crash dialog appeared.
  **Fix (root cause removed):** `GetEffectiveCadenceInterval()` no longer accesses WPF objects. A new
  `volatile bool _uiWindowActive` field is written exclusively from the UI thread via a new
  `SetUiWindowActive(bool)` method, which is wired to `MainWindow.IsVisibleChanged` and
  `MainWindow.StateChanged` events in `App.xaml.cs`. The background thread reads only the primitive field —
  no WPF thread-affinity violation. The English string match (`ex.Message.Contains("different thread")`)
  in `OnUnobservedTaskException` was **removed** (STEP-03). Two locale-safe checks remain as the safety net:
  a stack-trace check (`System.Windows.Threading.Dispatcher`) and a declaring-type check
  (`ex.TargetSite?.DeclaringType?.FullName?.StartsWith("System.Windows.")`), both locale-independent.

- **Calibration Wizard failed to open — "Failed to open calibration wizard: LoggingService not available"**
  `FanCalibrationControl` resolved `LoggingService` exclusively from the DI container and threw
  `InvalidOperationException` if the container returned null (startup-order regression from 3.3.0).
  The control now falls back to `App.Logging` (the always-present static singleton) when DI resolution fails.

- **RGB scene applied from the Lighting tab permanently turned off keyboard backlight**
  `RgbSceneService.ApplyToOmenKeyboardAsync` mapped `scene.Brightness` (0–100 scale) through a 0–3 level
  switch before calling `KeyboardLightingService.SetBrightness()`, which already expects 0–100 and performs
  its own WMI range mapping internally. The double conversion meant a 100% scene passed `SetBrightness(3)`,
  i.e. 3%, which the WMI backend translated to raw byte 103 — just above the OFF threshold of 100. Colours
  were written correctly but the backlight was effectively dark. The 0–3 mapping is removed; `scene.Brightness`
  (0–100) is now passed directly to `SetBrightness()`.

### Added — Model Support

| Product ID | Model | Note |
|---|---|---|
| `8D24` | OMEN 16-ap0xxx (2025) AMD — Ryzen AI 9 365 + RTX 5060 | GH community report |
| `8D2F` | OMEN 16-am0xxx (2024) AMD — OMEN Gaming Laptop 16-am0xxx | GH #111 |
| `8C2F` | Victus 16-r0xxx (2024+) Ryzen AMD | GH #110 (capability DB entry, keyboard DB already present) |

*Models 8D24 and 8D2F are added to both `ModelCapabilityDatabase` and `KeyboardModelDatabase`.
Model 8C2F is added to `ModelCapabilityDatabase` only (keyboard entry was already present from GH #89).*

### Issue Context

| Issue | Status |
|---|---|
| **GH #108** — Linux black screen | Insufficient reproduction info; tracking for follow-up in 3.3.2 |
| **GH #109** — Crash on Italian Windows (Victus 15-fa2xxx) | Fixed by root-cause fix above |
| **GH #110** — Victus 16-r0xxx not in model database | Fixed — 8C2F added to capability DB |
| **GH #111** — OMEN 16-am0xxx (8D2F) not in model database | Fixed — 8D2F added to both DBs |

### Release Artifacts

| File | SHA256 |
|------|--------|
| `OmenCoreSetup-3.3.1.exe` | `48BF5F11B30523BE4A39FFE47462A04A1844869B40DC7747143A9143C3C636B1` |
| `OmenCore-3.3.1-win-x64.zip` | `8558E8E84868CE7AA381CA0B781B4600BB80AA4D4231E653E744670AF81A6FF2` |
| `OmenCore-3.3.1-linux-x64.zip` | `7211703D295CBA08494D6F14D4930C3B71DFC0453B3CB7438D857F5187128894` |

---
## [3.3.0] - 2026-04-09 - Fan Curve Stability, Thermal Recovery, and UI Responsiveness

### Fixed
- **Fan curve stops working after first save (critical)**: Custom fan curve presets would become permanently non-functional after applying them for the first time on models where OGH (OMEN Gaming Hub) is running alongside OmenCore, or where fans are already near the curve target speed at idle. Root cause: the internal preset verification check required the fan RPM to change by ≥50 RPM within 800ms of applying the preset. If fans were already running at the target duty cycle (e.g. 40% curve target ≈ current idle 40%), no RPM change was detected → false "verification failure" → rollback → curve engine disabled permanently. Reported by Ryua (HP OMEN Gaming Laptop 16-ap0xxx, v3.2.5).
- **Bloatware tab failed to load after utility style consolidation**: shared utility stat cards introduced a `StaticResource` forward reference in `ModernStyles.xaml`, where `UtilityMetricLabel` was based on `Label` before `Label` had been declared. WPF resolves `StaticResource` eagerly, so the Bloatware tab could fail during XAML load. The shared label styles are now ordered so the base `Label` style exists before derived utility styles are created.
- **Worker-backed CPU/GPU load could stay pinned at 0% even while temperatures updated**: on some systems LibreHardwareMonitor exposes temperature sensors but not the exact load-sensor names the worker expected (`CPU Total`, `GPU Core`). The worker now logs load/power sensors to its file log and matches a broader set of CPU/GPU utilization names, while `LibreHardwareMonitorImpl` backfills missing worker load values with local Windows performance-counter fallbacks instead of leaving the UI stuck at 0%.
- **Fan control state destroyed on verification failure**: `FanVerificationService` was calling `SetFanMode(Default)` when its post-apply RPM check failed, overwriting whatever fan curve state was active. This is what caused the "curve freezes and stops working" symptom — the verification diagnostic would run, fail due to RPM telemetry lag or OGH interference, and then reset the fan to BIOS auto mode. The service now only reports the diagnostic warning without touching fan control state.
- **UI freeze / lag when switching fan presets** (multiple users): `FanService.ApplyPreset()` was called synchronously on the WPF UI thread from the preset dropdown setter and hotkey handler. `WmiFanController` contains multiple `Thread.Sleep()` calls during fan command application and verification, blocking the UI thread for 200–800ms per switch. All `ApplyPreset()` calls from `FanControlViewModel` are now dispatched to a background thread via `Task.Run`, with UI state updates posted back via Dispatcher.
- **CPU temperature frozen after wake from sleep (GitHub #102)**: On models using the worker-backed CPU temperature source (OMEN 17-ck1xxx, ck2xxx, 16-xd0, OMEN MAX 16), the hardware worker process can exit during sleep due to the 5-minute orphan timeout or Windows suspending background processes. `TryRestartAsync()` — called by `HardwareMonitoringService.RecoverAfterResumeAsync()` — was only resetting NVAPI failure state, leaving the cached `LibreHardwareMonitorImpl` instance pointing at the dead worker's IPC channel. Subsequent `GetCpuTemperature()` calls returned the last stale pre-sleep value indefinitely. On resume, the fallback monitor is now disposed and re-initialized, and the hardware worker is relaunched. Reported by xenon205 (HP OMEN 17-ck1xxx, v3.2.5).
- **Fans remain at 100% after switching from Performance to Balanced/Quiet on V1 BIOS systems (GitHub #102)**: On OMEN models with V1 WMI thermal policy (MaxFanLevel=55, krpm scale — e.g. OMEN 17-ck1xxx), switching from a Performance fan preset back to Balanced or Quiet sent `SetFanMode(Default)` which the BIOS accepted but did not act on, leaving fans at full speed. A direct `SetFanLevel(20,20)` transition kick is now emitted after `SetFanMode(Default)` exclusively for V1 systems transitioning out of Performance mode, giving the EC a concrete duty-cycle target to ramp from. Reported by xenon205 (HP OMEN 17-ck1xxx, v3.2.5).
- **Restore Defaults freezes the app and requires "End Task" (GitHub #100 Bug #1)**: `KeyboardLightingService.RestoreDefaults()` was calling `.GetAwaiter().GetResult()` on async V2 service methods from the WPF UI thread. With the WPF SynchronizationContext in place, async continuations needed the UI thread to complete — but the UI thread was blocked waiting for them — causing a permanent deadlock. The V2 restore path is now dispatched as a fire-and-forget `Task.Run` with `ConfigureAwait(false)`. Additionally, the legacy `ApplySchedulerTweak()` helper now sets `CreateNoWindow = true` to suppress a briefly-visible PowerShell console window during restore, and `RevertAllAsync` now accepts a 60-second `CancellationToken` with per-step status messages so the UI stays responsive and correctly shows progress. All step callbacks are marshaled to the UI dispatcher to prevent cross-thread `NotifyPropertyChanged` errors.
- **Fn+brightness keys (F6/F7) incorrectly open OmenCore on HP OMEN 16 xd0xxx (AMD) (GitHub #100 Bug #2)**: On affected models, Fn+brightness sends a `VK_LAUNCH_APP1` or `VK_LAUNCH_APP2` key event with hardware scan code `0xE046`. Because `0xE046` is in `OmenScanCodes` (the known OMEN Button scan set), `IsOmenKey()` was matching these events and triggering the OMEN key action — opening OmenCore. An explicit `scanCode == 0xE046` early-exit guard is now inserted before the OMEN scan-code check for both APP1 and APP2 paths, with a specific rejection reason logged for diagnostics.
- **Fans won't drop below ~2000 RPM in Balanced/Quiet auto mode on V1 BIOS systems (GitHub #100 Bug #4)**: The `SetFanLevel(20, 20)` transition kick (added for Issue #102) temporarily set a manual duty-cycle floor on V1 systems. Because the BIOS held this value after `SetFanMode(Default)` was sent, fans could not spin down below the ~2000 RPM level that corresponds to duty 20 on the V1 krpm scale. A `SetFanLevel(0, 0)` call is now issued immediately after `SetFanMode(Default)` on V1 systems (in both `ApplyPreset()` and `RestoreAutoControl()`), clearing the manual floor and letting the BIOS EC regulate freely to 0 RPM at idle.
- **Unexpected app exit on AC unplug during power automation (community report / Bug #9)**: Power-transition automation callbacks could surface UI-dispatch exceptions without local guards on some profile/device combinations, causing app-level unhandled exception handling to terminate the process. AC/Battery transition dispatch and UI synchronization callbacks are now wrapped with non-fatal guards, and power-profile applies now include transition-scoped structured logging plus rollback attempts for fan preset/performance mode on partial failures.

### Improved
- Post-apply verification kick (`RunCurveVerificationKickAsync`) now correctly re-enables the curve on completion even on models where the RPM readback temporarily shows low values due to telemetry confirmation-counter delays.
- Fan names from LibreHardwareMonitor are now normalized to "CPU Fan" / "GPU Fan" before being displayed. On AMD OMEN laptops (Ryzen AI / Strix Point), LHM can surface ACPI-internal sensor labels such as "G" (GPU die) and "GP" (GPU Package) as visible fan names, which was confusing. These are now mapped to human-readable labels based on fan slot position.
- Game Library now supports manual executable import via an `Add Game` action, so users can create profiles for titles not discovered by launcher scans.
- Undervolt controls now present vendor-aware wording and guidance (`CPU Undervolting` on Intel, `CPU Curve Optimizer` on AMD), surface active backend/capability context inline, and provide persistent in-panel apply/reset result feedback.
- Fan/performance decoupling is now surfaced directly in Fan Control, System Control, and Quick Access with visible linked/independent badges, a one-time explainer, and explicit copy that performance-mode changes leave the active fan preset untouched when decoupled mode is enabled.
- NVIDIA GPU tuning now includes a 30-second `Test Apply` auto-revert workflow with explicit `Keep` confirmation, so users can validate unstable offsets without committing them immediately.
- NVIDIA GPU tuning now surfaces detected per-device core, memory, and power ranges directly in the UI, distinguishes `Power Limit Only` systems from full clock-offset support, and shows model-aware guardrail guidance for laptop and RTX 50-series GPUs.
- Built-in NVIDIA GPU OC profiles are now generated as `Safe`, `Balanced`, and `Max Experimental` tiers using the detected device limits instead of static one-size-fits-all presets.
- GPU OC profile loads now apply their own voltage offset instead of reusing the previously active voltage slider state, and NVAPI power-only systems now restore saved power limits on startup.
- Resume diagnostics now record a user-visible suspend/resume recovery timeline, run a post-resume self-check for telemetry/fan recovery, and include the resulting `resume-recovery.txt` report in exported diagnostics bundles.
- Settings now surfaces a `Model Identity Resolution` card with resolved model, resolution source, confidence, keyboard-profile details, warning states for inferred/fallback matches, and a one-click copy summary for issue reports.
- Memory Optimizer now uses vector icon + text actions instead of emoji labels, and both Memory and Bloatware now share the same utility stat-card, section-heading, and badge rhythm from `ModernStyles.xaml`.
- System Optimizer no longer carries duplicated local preset-button, toggle, section-header, and card styles; System Optimizer, Memory, and Bloatware now share named `Omen.*` styles from `ModernStyles.xaml`.
- OSD overlay now supports `Compact` / `Balanced` / `Comfortable` density modes plus metric-group toggles (`Thermals`, `Performance`, `Network`, `System`), with group-aware visibility and more readable spacing in Comfortable mode.
- Busy/loading feedback across System Optimizer, Bloatware, and Memory now uses shared `Omen.Busy.*`/`Omen.Toast` styles, and status copy in those workflows is standardized to `Action...`, `Done`, or `Failed: reason`.
- Utility accessibility polish now includes a shared keyboard focus ring (`Omen.FocusVisual`) across button/toggle/radio style primitives, explicit automation metadata for icon-only actions, and improved contrast for low-visibility utility text.
- System Optimizer `Revert All` now uses stage-isolated error handling plus parallel-safe revert batching (network/input/visual/storage), so one failed category no longer aborts the entire restore-to-default flow.
- Bloatware low-risk bulk removal now runs as a staged transaction with rollback: on first failure, previously removed restorable items are automatically restored and rollback results are summarized in status/log output.
- Memory Optimizer auto-clean now supports adaptive profiles (`Aggressive`, `Balanced`, `Conservative`, `OffPeakOnly`, `Manual`) that tune both RAM threshold and check cadence, with profile persistence across restarts.
- Memory Optimizer now supports per-process working-set exclusions with persisted allowlist editing in the UI, so critical background services can be skipped during trim operations.
- Memory Optimizer now surfaces memory compression status and one-click toggle control (via MMAgent), with inline operation feedback.
- Memory Optimizer now includes a richer dashboard with standby/modified/compressed memory breakdowns, a 30-minute RAM trend sparkline, and a visible top-process table with context actions.
- OSD overlay now supports explicit target-monitor selection (`Primary`, `ActiveWindow`, `MouseCursor`) so placement follows the intended display on multi-monitor setups.
- OSD now supports horizontal layout mode from Settings, and GPU hotspot display prefers the real hotspot sensor value when available (with estimated fallback only when unavailable).
- System Optimizer now turns staged restore/apply status updates into visible determinate progress in both the page header and busy overlay, while keeping indeterminate feedback for non-staged actions.
- System Optimizer toggles now include expandable `What changes` details with affected registry keys, services, command-based changes, and manual undo guidance before apply.
- System Optimizer now performs hourly state verification against the live system, shows `Last verified` drift status in the UI, and auto-corrects a small set of low-risk service reversions.
- System Optimizer now supports per-category `Apply All` actions (Power/Services/Network/Input/Visual/Storage) with staged progress feedback and command gating when a section is already applied.
- Bloatware Manager bulk remove and restore workflows now support user cancellation, time-box hung external uninstall/restore processes, and export skipped remaining items clearly in the result log.
- Bloatware detection now supports JSON-backed signature loading and expanded HP OMEN/AppX coverage, while keeping protected driver and Windows-component exclusions ahead of those matches.
- Bloatware Manager now keeps persistent removal/restore history with richer report metadata (admin/device/OS context and recent actions) and surfaces a direct `View Report` action after operations.
- Quick Access refresh-rate toggling now supports per-display targeting on multi-monitor setups by cycling through detected displays instead of always acting on the first display.
- Quick Access `Curve` now resolves and reapplies the active saved custom fan preset (with safe fallback to another saved custom curve) instead of using a generic custom-mode toggle.
- Bloatware scanning now includes Startup folder entries, preserves those file-based startup items for safe restore, and shows friendlier scheduled-task names while matching more task variants.
- Linux GUI startup diagnostics now explicitly guide users to force software rendering (`OMENCORE_GUI_RENDER_MODE=software`) when Wayland/X11 GPU renderer initialization fails.

---

## [3.1.0] - 2026-03-10 - Telemetry Integrity, UI Polish, and Worker Reliability

Durable monitoring correctness update focused on explicit sensor-state semantics,
removal of fabricated fan telemetry, and lower UI/monitor loop churn while preserving
existing fan-control and profile workflows.

This entry includes all pre-release bugfix work that was previously tracked as internal
`hotfix1`/`hotfix2`/`hotfix3` notes before first public deployment of 3.1.0.

### Fixed
- Linux thermal profile selection now supports kernel variants exposing `balanced-performance` and alternative hp-wmi `*_choices` paths, improving high-power profile activation reliability on newer Bazzite/Fedora kernels.
- CPU power no longer pins to an initial stale value on systems where MSR power reads are unavailable.
- GPU temperature handling now marks inactive dGPU periods explicitly instead of surfacing random placeholders.
- Fan RPM fallback no longer fabricates estimated values when real readback is unavailable.
- GitHub #77: standby/sleep fan behavior was hardened so suspend handling is always active (not gated by Settings tab initialization), fan writes are paused during suspend, and BIOS auto control is restored to prevent max-RPM spikes while sleeping.
- GitHub #78: OMEN MAX 16-ah0000 CPU temperature handling now prefers worker-backed sensor reads via model-scoped override to avoid incorrect CPU temperature reporting on affected Intel systems.

### Improved
- Added explicit per-sensor telemetry state modeling (valid/zero/inactive/unavailable/stale/invalid) in monitoring samples.
- Dashboard/Main summaries now render intentional inactive/unavailable states for clearer user-facing diagnostics.
- GPU inactive status is surfaced consistently in dashboard value and status fields.
- CPU/GPU dashboard cards now surface stale/invalid states explicitly for clearer troubleshooting.
- Linux diagnostics now report additional hp-wmi platform/thermal profile and choices paths for faster model-specific triage.
- Runtime messaging now consistently treats WinRing0 as legacy/optional and keeps PawnIO+WMI as the primary recommended path.
- Removed obsolete LibreHardwareMonitor driver-install helper flow from startup code to avoid outdated backend guidance.
- Dependency audit, settings backend label, and antivirus FAQ wording were aligned to explicit PawnIO/WMI-default semantics with legacy WinRing0 clearly marked optional.
- Tuning telemetry placeholders now consistently render `--W` and placeholder-aware GPU clock values when live data is unavailable.
- Sidebar temperature state text was compacted to reduce clipping in narrow layouts.
- Diagnostics tab now shows an explicit monitoring status line for model-specific CPU temperature override activation (GitHub #78), making affected-model behavior visible during troubleshooting.

### Optimized
- Reduced monitoring hot-path log verbosity to cut avoidable background work.
- Replaced broad telemetry property-change invalidation with targeted notifications to reduce UI churn.
- Increased high-frequency dashboard metric refresh interval from 1s to 2s to lower idle CPU overhead.
- Added defensive RAM-power estimation math to avoid no-data NaN propagation.
- Reduced repetitive no-sample placeholder logging from warning spam to transition-based debug output.
- Added `OMENCORE_DISABLE_LHM=1` monitor fallback opt-out and removed misleading WinRing0-specific worker fallback wording.
- Dashboard battery-health polling cache increased to 5 minutes and noisy battery logs moved to debug to reduce background overhead.
- Tray version loading now uses streaming reads to avoid startup-time full-file allocation.
- Tray right-click menu reliability was restored by removing problematic custom context-menu style-key override behavior.
- Tray menu visual consistency was improved by removing default left-gutter artifacts and hover/icon underlay bleed in the custom dark template.
- Hardware worker startup was hardened: bootstrap at app startup, prewarm fallback path, and additional startup diagnostics for launch outcomes.

### Release Artifacts and SHA256

| File | Size | SHA256 |
|------|------|--------|
| `OmenCoreSetup-3.1.0.exe` | 101.09 MB | `D92548E4E3698A2B71D11A02ED64D918746C3C3CB06EC2035E8602D57C50AD8C` |
| `OmenCore-3.1.0-win-x64.zip` | 104.32 MB | `1EA65E7BA857285A01A896FC2A7BF8418D1B8D9723DCB9EE4A350E6BA87A06F6` |
| `OmenCore-3.1.0-linux-x64.zip` | 43.55 MB | `276686F92EB289B3196BDCD02CFC93E95F676D269515740060FB7B5A585D9D0F` |

Full release notes: [CHANGELOG_v3.1.0.md](docs/CHANGELOG_v3.1.0.md)

---

## [3.0.2-hotfix] - 2026-03-07 - Telemetry and Linux Reliability Patch

Post-release hotfixes for v3.0.2 targeting Windows power/temperature reliability and Linux runtime compatibility.

### Highlights
- Windows power fallback now uses LibreHardwareMonitor when primary sensors report 0W under active load.
- ACPI CPU outlier rejection reduces bogus temperature jumps that can destabilize fan control.
- Windows crash-hardening added for hotkey focus churn and overlapping fan countdown re-apply callbacks.
- Linux packaging now guarantees known-good `omencore-cli` payload composition and strips stale sidecar files.
- Linux fan support expanded for `hp-wmi` `fan1_target`/`fan2_target`; keyboard RGB now supports `multi_intensity`.
- Avalonia Linux version display aligned to 3.0.2 metadata.

Full hotfix notes: [CHANGELOG_v3.0.2-hotfix.md](docs/CHANGELOG_v3.0.2-hotfix.md)

---

## [3.0.2] - 2026-03-04 - Stability & Compatibility Patch 🔧

Cumulative stability release incorporating all hotfix1 and hotfix2 work plus additional
fixes (H–J) identified through community testing on OMEN 17-ck2xxx and Victus 16-r0xxx.
Ten fix series (A–J) resolving 22+ individual issues.

### 🐛 Bug Fixes (Fixes A–J)
- **(A)** XAML `StaticResourceExtension` startup crash — five undefined resource keys resolved
- **(B)** Secure Boot status displayed inverted when PawnIO is available
- **(C)** Ctrl+Shift+O global hotkey dead after window deactivation (issue #70)
- **(D)** `CapabilityWarning` false positive for PawnIO users
- **(E)** Five missing event unsubscriptions in `MainViewModel.Dispose()`
- **(F)** `_amdGpuService` field race condition (`volatile` fix)
- **(G)** GUI polish — 18 tooltips, 5 hardcoded colors, Gaming Mode disabled state
- **(H)** CpuClock log format error, `GetSystemInfo()` thread safety, PawnIO probe
- **(I)** Keyboard lighting null `SystemInfoService`, Dashboard INFO spam, `WmiBiosMonitor` dispose exception
- **(J)** MSI Afterburner garbage temperature false thermal emergency, thermal protection sanity guard, COM STA reentrancy in `GetSystemInfo()`

See [CHANGELOG_v3.0.2.md](docs/CHANGELOG_v3.0.2.md) for full details.

### 🎯 Release Artifacts

**Version:** 3.0.2 (Release/win-x64)  
**Build Date:** 2026-03-04 06:50 UTC

| File | Size | SHA256 |
|------|------|--------|
| **OmenCoreSetup-3.0.2.exe** | 101.08 MB | `2B9CCCD8F28E1661632B48C24A91FA6A1BD0D12A365460FBA9B458718A0C68AC` |
| **OmenCore-3.0.2-win-x64.zip** | 104.31 MB | `F644999BC88D55067E7E7DA8E7A7B8EE7AA76356EC4908561D69EBB09A1F2E5B` |

**Enhancements in this build:**
- ✨ Memory cleaning profiles (Conservative/Balanced/Aggressive presets)
- ✨ Process memory ranking (top 10 memory consumers with real-time updates)
- ✨ Memory cleanup preview (estimate freed memory before operation)
- ✨ Bloatware bulk restore (mirrors bulk remove for complete parity)
- All enhancements fully backward-compatible, zero breaking changes
- Build: 0 errors, 0 warnings

---

## [3.0.0] - 2026-03-01 - Major Architecture Overhaul 🏗️

v3.0.0 is the most substantial release since v2.0.0. The core monitoring pipeline was
rebuilt around a self-sustaining architecture (WMI BIOS + NVAPI + PerformanceCounter +
PawnIO MSR — no kernel drivers required). Seven critical regressions that affected
real users post-2.9.x are resolved, four additional reliability improvements were
made to the hardware monitoring layer, and a broad set of new features was added:
guided fan diagnostics, a memory optimizer, keyboard model expansion, diagnostics
reporting, and Linux CLI improvements.

### 🐛 Critical Bug Fixes (P0/P1/P2)

- **GPU Telemetry Permanently Lost After NVAPI Error (RC-1)** — GPU temperature and load
  now recover automatically after transient NVAPI failures. After 10 consecutive errors the
  source is suspended for 60 seconds then retried, instead of being permanently disabled
  for the session. Resolves stuck 28 °C / 0 W GPU readings (#67, #68) until app restart.
  (`WmiBiosMonitor.cs`)

- **OMEN 16-wf1xxx Fan Control Non-Functional (RC-2)** — ProductId `8BAB` (Board 8C78,
  OMEN 16-wf1xxx 2024 Intel) was missing from `ModelCapabilityDatabase`. The Transcend
  fallback silently disabled WMI fan control (`SupportsFanControlWmi = false`). Added with
  `MaxFanLevel = 100`, `UserVerified = false` pending community confirmation. Resolves #68.
  (`ModelCapabilityDatabase.cs`)

- **Fan Auto Mode Shows 0 RPM After Preset Switch (RC-3)** — `RestoreAutoControl()` skipped
  the `ResetFromMaxMode()` sequence unless already in Max mode, leaving a 3-second RPM
  debounce window active with no reset after Quiet/Extreme → Auto transitions. Fixed:
  `ResetFromMaxMode()` is now called unconditionally and `_lastProfileSwitch` is reset to
  clear the debounce window immediately. (`WmiFanController.cs`)

- **Linux CLI Performance Mode Silently Fails (RC-4)** — `SetPerformanceMode()` only called
  `WriteByte(REG_PERF_MODE)`, which requires `HasEcAccess`. On hp-wmi-only boards
  `IsAvailable` returned `true` but the write produced no effect. Fixed: full priority
  routing added — hp-wmi thermal_profile → ACPI platform_profile → EC register write.
  (`LinuxEcController.cs`)

- **Secure Boot Warning Shown Alongside Green PawnIO Badge (RC-5)** — `LoadSystemStatus()`
  unconditionally set `SecureBootEnabled` from the registry regardless of PawnIO availability.
  Fixed: `SecureBootEnabled = rawSecureBoot && !PawnIOAvailable`. PawnIO is Secure
  Boot-compatible; its presence fully resolves the constraint. (`SettingsViewModel.cs`)

- **Clean Install Shows "Standalone = Degraded" (RC-6)** — The dependency audit counted
  any 2+ missing optional components as Degraded. Clean installs without OGH or HP System
  Event Utility hit this threshold immediately. Fixed: Degraded threshold raised from `≥ 2`
  to `≥ 3`; `LibreHardwareMonitor` check changed to `IsOptional = false`; summary text
  clarified to show OGH/HP-SEU are not required for core operation. (`SystemInfoService.cs`)

- **Monitor Loop Permanently Exits on 5 Consecutive Errors (RC-7)** — `MonitorLoopAsync`
  broke out of its loop after 5 consecutive exceptions, permanently stopping all telemetry
  with no recovery or notification. Fixed: on hitting the error limit the loop now resets
  the counter, waits 10 seconds, and continues — recovering from transient driver resets,
  sleep/wake glitches, and brief WMI stalls without requiring an app restart.
  (`HardwareMonitoringService.cs`)

### 🔧 Reliability & Quality Improvements

- **CPU PerformanceCounter Singleton — Eliminates 100 ms Polling Stall** — `UpdateReadings()`
  previously created a new `PerformanceCounter` instance, called `NextValue()` twice with
  `Thread.Sleep(100)` between calls, then disposed it — on every single 2-second poll cycle.
  The counter is now a persistent field initialised (and warmed up) once in the constructor.
  Each poll calls `NextValue()` once with no sleep; the elapsed interval between calls provides
  the correct average CPU load automatically. Removes ~100 ms of blocking + GC pressure per
  cycle. (`WmiBiosMonitor.cs`)

- **`TryRestartAsync()` Now Resets NVAPI Failure State** — Previously a no-op that only
  logged a message and returned `true`. Now resets `_nvapiMonitoringDisabled`,
  `_nvapiConsecutiveFailures`, and `_nvapiDisabledUntil`, giving the `HardwareMonitoringService`
  restart path a genuinely clean slate for GPU telemetry on the next poll cycle.
  (`WmiBiosMonitor.cs`)

- **Linux `GetPerformanceMode()` Now Consistent with `SetPerformanceMode()`** — `GetPerformanceMode()`
  read exclusively from the EC register, which is unavailable on hp-wmi-only boards. It now
  follows the same priority chain as the now-fixed setter: hp-wmi `thermal_profile` string →
  ACPI `platform_profile` → EC register fallback. Reported mode now always reflects the
  active backend. (`LinuxEcController.cs`)

- **Dashboard Metrics Use Real Hardware Data** — `UpdateDashboardMetrics()` used hardcoded
  placeholder values: `BatteryHealthPercentage = 100`, `BatteryCycles = 0`,
  `EstimatedBatteryLifeYears = 3.0`, `FanEfficiency = 70.0`. Battery health now reads from
  `sample.BatteryChargePercent`; fan efficiency is computed from `Fan1Rpm`/`Fan2Rpm`
  (average RPM / 50 → 0-100 scale). Battery health thresholds (< 70% warning) and fan
  speed visualisations now react to real hardware data. (`HardwareMonitoringService.cs`)

### ✨ New Features & Enhancements

- **Guided Fan Diagnostics** — New UI section runs sequential fan tests at 30/60/100% for
  CPU and GPU fans, showing live progress and a PASS/FAIL summary. Results can be copied to
  clipboard for support reports. Current preset is preserved and restored on completion.
  (`FanDiagnosticsViewModel.cs`, `FanDiagnosticsView.xaml`)

- **Memory Optimizer Tab** — Dedicated "Memory" tab with real-time RAM monitoring and
  one-click memory cleaning via Windows Native API (`NtSetSystemInformation`). Smart Clean
  (working sets + low-priority standby + page combining) and Deep Clean (all operations
  including full standby purge, file cache flush). Per-operation risk indicators. Auto-clean
  with configurable threshold (50–95%). Results include a copy-to-clipboard button.
  (`MemoryOptimizerService.cs`, `MemoryOptimizerViewModel.cs`, `MemoryOptimizerView.xaml`)

- **Diagnostics & Model Reporting** — "Monitoring Diagnostics" panel and `Report Model`
  flow: creates a diagnostics ZIP (logs + sample capture) and copies model info to clipboard.
  `ModelReportService` coordinates collection and export. One-click "Export telemetry"
  button on the Diagnostics panel. (`DiagnosticsView.xaml`, `ModelReportService.cs`,
  `TelemetryService.cs`)

- **Keyboard Model Database Expansion** — Added ProductId `8BD5` (HP Victus 16, 2023) and
  `8A26` (HP Victus 16, 2024) to ensure per-zone ColorTable is applied instead of the
  generic Victus fallback. (`KeyboardModelDatabase.cs`, `KeyboardLightingServiceV2.cs`)

- **Model Database Additions** — `OMEN MAX 16 (ak0003nr)` with ThermalPolicy V2 handling
  (WMI-only fan control recommended, avoid legacy EC writes); `OMEN 16-wf1xxx` (8BAB,
  Board 8C78). (`ModelCapabilityDatabase.cs`)

- **Fan Control Reliability** — Seeded last-seen RPMs; added multi-sample confirmation for
  large RPM deltas; preset apply now ignored while diagnostics are active; atomic preset
  verification + rollback when controller state does not match expected behaviour.
  (`FanService.cs`, `WmiFanController.cs`, `FanControllerFactory.cs`)

- **Fn+Brightness False-Positive Fix** — `OmenKeyService` now prefers the low-level
  keyboard hook and suppresses overlapping WMI OMEN events when a brightness-key sequence
  is in progress, eliminating false OMEN-key triggers on Fn+F2/F3.

- **Monitoring Health: Last-Sample-Age Indicator** — Exposed a "last sample age" health
  indicator on the dashboard. Transient 0 W / 0 °C spikes suppressed with retain-last-valid
  logic; RAPL MSR reads wired where PawnIO is available.

- **Strix Point CPU Detection** — `SystemInfoService` now flags Intel 14th-gen "Strix Point"
  chips; NVAPI service gracefully handles missing DLLs during initialisation.

- **Installer Improvement** — Installer now skips the embedded PawnIO sub-installer when
  PawnIO is already present, avoiding redundant installs and incorrect task-switch behaviour.

- **CI Coverage** — Unit and integration tests added for: keyboard hook + WMI filtering;
  fan `MonitorLoop` confirmation counters; diagnostics mode guard; power-read stabilisation;
  model report export; quick-profile switch stress test (verifies no transient 0 RPM spikes
  during rapid preset switching).

### 📁 Key Files Updated
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- `src/OmenCoreApp/Hardware/WmiFanController.cs`
- `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- `src/OmenCoreApp/Hardware/KeyboardModelDatabase.cs`
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- `src/OmenCoreApp/Services/SystemInfoService.cs`
- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/Services/MemoryOptimizerService.cs` *(new)*
- `src/OmenCoreApp/Services/ModelReportService.cs` *(new)*
- `src/OmenCoreApp/Services/TelemetryService.cs`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- `src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs` *(new)*
- `src/OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs` *(new)*
- `src/OmenCoreApp/Views/FanDiagnosticsView.xaml` *(new)*
- `src/OmenCoreApp/Views/MemoryOptimizerView.xaml` *(new)*
- `src/OmenCoreApp/Views/DiagnosticsView.xaml`
- `src/OmenCore.Linux/Hardware/LinuxEcController.cs`

---

## [2.9.0] - 2026-02-13 - Stability & Telemetry Recovery Update 🛠️

### 🐛 Bug Fixes
- **App Freezes After Tray Profile/Fan Changes**: Tray quick actions are now serialized and run non-blocking to prevent UI thread lockups when fan/performance backends take longer to respond.
- **Quick Menu Can "Break" Until Reboot**: Added tray action gating with timeout + recovery path so overlapping quick menu actions no longer stack into a stuck state.
- **Fn+F2/F3 Brightness Opens OmenCore**: Added a brightness-event guard window so HP WMI OMEN-key events immediately following brightness key activity are ignored.
- **OMEN Key Detection Noise**: Reduced key-hook logging verbosity and tightened F-key exclusion logic while preserving explicit F24 OMEN-key support.
- **CPU/GPU Temp Freeze False Positives**: CPU freeze detection now uses idle-aware thresholds and WMI confirmation reset logic, reducing false "stuck" detection when temps are legitimately stable.
- **CPU/GPU Power Stuck at 0W**: Improved transient-zero power smoothing (longer hold of last valid readings with lower activity thresholds) to avoid rapid drop-to-zero glitches.
- **Telemetry Update Contention**: WMI BIOS monitor reads are now serialized with an async gate to prevent overlapping hardware update calls that could produce stale/jittery samples.
- **Fan Curve Editor Drag Lag**: Curve redraw during point drag is now frame-throttled to improve responsiveness and reduce stutter when moving nodes.

### 📁 Key Files Updated
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/Services/OmenKeyService.cs`
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- `src/OmenCoreApp/Controls/FanCurveEditor.xaml.cs`

## [2.8.6] - 2026-02-11 - Community Bug Fix Patch 🐛

### 🐛 Bug Fixes
- **CPU Temperature 0°C (OMEN MAX 8D41)**: Intel Core Ultra / Arrow Lake CPUs now use fallback sensor sweep when primary sensor returns 0 — added "CPU DTS" to priority list + auto-tries all temperature sensors when named search fails after having valid prior readings
- **Fn+F2/F3 Steals System Shortcuts**: Bare function key OSD hotkeys (e.g. `F2`) now automatically get Ctrl+Shift modifier added — prevents stealing of laptop Fn+F-key brightness/volume shortcuts
- **RPM Glitch / False MaxFanLevel=100**: Removed unreliable current-fan-level auto-detection heuristic from `DetectMaxFanLevel()` — fan levels elevated by OMEN Hub at startup caused false positive detection, leading to inflated RPM display and fan slider allowing levels above actual hardware max
- **Quick Profile UI Desync**: Switching quick profiles (Performance/Balanced/Quiet) now syncs the OMEN tab performance mode display via `SystemControlViewModel.SelectModeByNameNoApply()`
- **Game Library Buttons Disabled**: Fixed Launch/Create Profile/Edit buttons staying disabled after selecting a game — `SelectedGame` setter now triggers `RaiseCanExecuteChanged()` on button commands
- **GPU Temp Frozen at Idle**: Fixed false positive freeze detection for idle GPUs — idle-aware threshold (120 readings when GPU load <10%), WMI confirmation resets freeze counter instead of rejecting, NVML 60s auto-recovery cooldown instead of permanent disable
- **EC Direct Fan Control System Crash**: Aggressive EC register polling for RPM (40+ reads every 1.5s) overwhelmed the embedded controller, triggering ACPI Event 13 ("EC did not respond within timeout") and crashing the system — `ReadFanSpeeds()` now skips all EC register reads in self-sustaining mode (no LHM), using WmiBiosMonitor's safe WMI BIOS command 0x38 for RPM instead
- **Garbage RPM from Alt EC Registers**: Removed reads of EC registers 0x4A-0x4D which returned invalid values (144, 0, 256 RPM) on OMEN 17-ck2xxx — `ReadActualFanRpm()` now only uses primary registers 0x34/0x35 with retries reduced from 5 to 2
- **Fan Curve Evaluating Wrong Temperature**: `ThermalSensorProvider` was creating a standalone `HpWmiBios` instance returning raw WMI 50°C instead of using `WmiBiosMonitor`'s ACPI-enhanced 97°C — now reads cached ACPI thermal zone temperatures from the shared `WmiBiosMonitor`
- **EC Backend Not Activating Without LHM**: `TryCreateEcController()` was gated behind `_libreHwMonitor != null` check — EC direct fan control now works in self-sustaining mode without LibreHardwareMonitor
- **ACPI Temperature Float Noise**: Raw ACPI thermal zone conversion produced values like 97.05000000000001°C — added `Math.Round(..., 1)` to `WmiBiosMonitor` temperature conversion
- **Max Fan Verify EC Overload**: `SetMaxFanSpeed()` did up to 6 EC RPM reads across 3 retry attempts — simplified to single verification read to prevent EC contention
- **EC Overwhelm → False Battery Critical Shutdown**: OmenCore's EC register operations (writes + readback reads) overwhelmed the Embedded Controller, causing ACPI Event 13 ("EC did not respond within timeout"). The EC also handles battery monitoring — when overwhelmed, Windows lost battery status and triggered false "Critical Battery Trigger Met" (Event 524) emergency shutdowns, even with charger plugged in. Multiple users reported this. Fixes:
  - **Removed EC readback verification**: `WriteDuty()` did 4 EC reads after every write (REG_OMCC, REG_XFCD, REG_FAN1_SPEED_PCT, REG_FAN_BOOST) purely for logging — these caused "EC output buffer not full" errors. Removed from `WriteDuty()`, `SetMaxSpeed()` (3 reads), and `SetFanSpeeds()` (2 reads). Saves 9 EC reads per write operation.
  - **Removed compatibility register writes**: `WriteDuty()` wrote to extra `_registerMap` registers of unknown validity — removed to reduce EC write count
  - **Added EC write deduplication**: `WriteDuty()` now tracks last written percent + timestamp and skips identical writes within 15 seconds. Prevents thermal protection from hammering EC with 7+ writes every poll cycle when fans are already at target speed.
  - **Rate-limited thermal protection EC writes**: `CheckThermalProtection()` called `SetFanSpeed(100)` every poll cycle (1-5s) when in emergency mode — now only re-applies as keepalive every 15 seconds after initial write
  - **Removed fan curve retry loop**: Curve application retried failed `SetFanSpeed()` up to 3 times with 300ms delays (21+ EC writes) — now single attempt with deduplication

### ✨ Enhancements
- **Memory Optimizer Tab**: New dedicated "Memory" tab with real-time RAM monitoring and one-click memory cleaning via Windows Native API (`NtSetSystemInformation`). Features Smart Clean (safe: working sets + low-priority standby + page combining) and Deep Clean (aggressive: all operations including full standby purge, file cache flush, modified page flush). 5 individual operations with risk indicators. Auto-clean with configurable threshold (50-95%). Real-time dashboard with color-coded RAM usage bar, system cache, commit charge, page file, and process/thread/handle counts.
- **Self-Sustaining Monitoring Architecture**: OmenCore no longer depends on LibreHardwareMonitor/WinRing0/NVML — monitoring uses WMI BIOS (temps, fans) + NVAPI (GPU load, clocks, VRAM, power) + PerformanceCounter (CPU load) + PawnIO MSR (throttling, optional) — same approach as OmenMon, no external kernel drivers
- **Monitoring Source Indicator**: Dashboard now shows active monitoring source (WMI BIOS + NVAPI, etc.) next to health status
- **Desktop Experimental Support**: OMEN desktops now show a warning dialog with option to continue instead of hard-blocking startup
- **RPM Debounce**: 3-second debounce window filters phantom RPM readings during profile transitions
- **V1/V2 Fan Restore**: `RestoreAutoControl()` now differentiates V1 (kRPM) and V2 (percentage) fan systems
- **Worker Reliability**: Cooldown reduced 30→5min, reconnect resets retry counter, auto-fallback to in-process on worker disable
- **Model Database**: Added `MaxFanLevel` property, OMEN 16 xd0xxx (2024) AMD entry, OMEN MAX models with MaxFanLevel=100

### Files Changed
| File | Change |
|------|--------|
| `OmenCore.HardwareWorker/Program.cs` | Added "CPU DTS" sensor name, fallback sweep for CPU temp 0°C |
| `OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs` | Added "CPU DTS" sensor name, in-process temp fallback, `MonitoringSource` property, NVML 60s auto-recovery cooldown, WMI fallback on NVML disable |
| `OmenCoreApp/Hardware/HpWmiBios.cs` | Removed current-fan-level MaxFanLevel auto-detection |
| `OmenCoreApp/Hardware/HardwareMonitorBridge.cs` | `MonitoringSource` on `IHardwareMonitorBridge` interface |
| `OmenCoreApp/Hardware/WmiFanController.cs` | RPM debounce, V1/V2 RestoreAutoControl |
| `OmenCoreApp/Hardware/HardwareWorkerClient.cs` | Cooldown 30→5min, reconnect recovery |
| `OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` | MaxFanLevel, xd0xxx + MAX models |
| `OmenCoreApp/Services/OsdService.cs` | Bare F-key hotkeys get Ctrl+Shift modifier |
| `OmenCoreApp/Services/HardwareMonitoringService.cs` | MonitoringSource passthrough, idle-aware GPU freeze threshold, WMI confirmation logic, freeze warning dedup |
| `OmenCoreApp/ViewModels/SettingsViewModel.cs` | OSD hotkey setter validates bare F-keys, default Ctrl+Shift+F12 |
| `OmenCoreApp/ViewModels/GeneralViewModel.cs` | Quick profiles now sync SystemControlViewModel |
| `OmenCoreApp/ViewModels/MainViewModel.cs` | Wires SystemControlViewModel into GeneralViewModel |
| `OmenCoreApp/ViewModels/GameLibraryViewModel.cs` | SelectedGame setter triggers CanExecuteChanged |
| `OmenCoreApp/ViewModels/DashboardViewModel.cs` | MonitoringSourceText + PropertyChanged |
| `OmenCoreApp/Views/DashboardView.xaml` | Monitoring source in health status row |
| `OmenCoreApp/Hardware/FanController.cs` | `ReadFanSpeeds()` EC safety guard when `_bridge==null`, nullable bridge, estimated RPM fallback; `ReadActualFanRpm()` removed alt regs 0x4A-0x4D, retries 5→2; `WriteDuty()` removed 4 readback reads + compatibility register writes + added deduplication; `SetMaxSpeed()` removed 3 readback reads + added deduplication; `SetFanSpeeds()` removed 2 readback reads |
| `OmenCoreApp/Hardware/FanControllerFactory.cs` | Removed LHM guard from `TryCreateEcController()`, removed dead `ReadFanRpmFromEc()`, simplified `SetMaxFanSpeed()` to 1 verify read |
| `OmenCoreApp/Hardware/ThermalSensorProvider.cs` | Uses `WmiBiosMonitor` ACPI-enhanced cached temps instead of standalone raw `HpWmiBios` |
| `OmenCoreApp/Hardware/WmiBiosMonitor.cs` | `Math.Round` on ACPI thermal zone temp conversion |
| `OmenCoreApp/Services/FanService.cs` | `CheckThermalProtection()` rate-limited EC writes (15s keepalive instead of every poll), removed curve retry loop (3 attempts → 1) |
| `OmenCoreApp/Services/MemoryOptimizerService.cs` | **New** — Memory optimizer using `NtSetSystemInformation` P/Invoke: working set trim, standby purge, file cache flush, modified page flush, page combining, auto-clean |
| `OmenCoreApp/ViewModels/MemoryOptimizerViewModel.cs` | **New** — Real-time memory stats (2s refresh), clean commands, auto-clean toggle |
| `OmenCoreApp/Views/MemoryOptimizerView.xaml` | **New** — Memory tab UI with RAM bar, stats, operation buttons, auto-clean settings |
| `OmenCoreApp/Views/MemoryOptimizerView.xaml.cs` | **New** — Code-behind for memory bar width |
| `OmenCoreApp/Views/MainWindow.xaml` | Added Memory tab |
| `App.xaml.cs` | Desktop experimental support dialog |

---

## [2.8.5] - 2026-02-10 - Community Bug Fix Update 🐛

### 🐛 Bug Fixes
- **Bloatware Removal Not Working**: Fixed AppX package name vs. PackageFullName mismatch — `Get-AppxPackage` was passed the full name instead of short name, so removal always found zero packages
- **Fan Diagnostic 30% Fail**: Adaptive RPM tolerance scaling with minimum 500 RPM absolute tolerance for low-speed tests (was too tight at 15% tolerance)
- **Fans Stuck at Max After Test**: `RestoreAutoControl()` now called when exiting diagnostics with no preset active (user was in auto/BIOS mode)
- **Fan Test Text Truncated**: Added text wrapping and max width to diagnostic error message display
- **Fn+F2/F3 Still Toggles Window**: Non-extended scan codes now require known OMEN VK code to prevent false positives from Fn+brightness keys
- **Keyboard Not Detected (xd0xxx)**: Fixed keyboard diagnostics init order (was created before keyboard service) + added series-pattern regex fuzzy matching for model names
- **OMEN Hub Not Detected**: Added OmenLightingService, OmenLighting, HP.OMEN.GameHub to conflict detection process names
- **Startup on Shutdown+Start**: XML task with LogonTrigger + StartWhenAvailable=true for Windows Fast Startup compatibility
- **Update Checker Stale**: Fixed hardcoded version in UpdateCheckService (was stuck at 2.7.1)

---

## [2.8.1] - 2026-02-09 - Community Bug Fix Update 🐛

### 🐛 Bug Fixes
- **Fn+F2/F3 Opens OmenCore**: WMI event handler changed from fail-open to fail-closed — brightness keys no longer treated as OMEN key press when eventId/eventData extraction fails
- **Auto Fan Mode Stuck**: `RestoreAutoControl()` no longer unconditionally calls `ResetFromMaxMode()` — fixes fans stuck at ~1000rpm on Victus models with MaxFanLevel=100
- **Quiet Profile = Max Fans**: Added ThermalPolicy-aware mode mapping — V0/Legacy BIOS models (e.g., Transcend 14) now receive correct `LegacyCool (0x02)` instead of invalid V1 code `0x50`
- **Phantom Fan RPM**: `GetFanRpmDirect()` (CMD 0x38) now gated behind ThermalPolicy ≥ V2 — fixes phantom 4200-4400 RPM readings on V0/V1 systems where the command returns garbage data
- **Fan Level-to-Percent**: Fixed hardcoded `/55` to use auto-detected `_maxFanLevel` — correct percentage on MaxFanLevel=100 models
- **Linux GUI Missing**: Avalonia GUI (`omencore-gui`) now built and bundled in Linux ZIP packages
- **OSD Horizontal Layout**: Setting layout to "Horizontal" now correctly applies — `MainPanel.Orientation` is set from `settings.Layout` at render time
- **OSD Network Values Stuck**: Upload/download stats timer now starts when any network metric is enabled, not just latency
- **OSD FPS Shows GPU%**: When RTSS is unavailable the FPS field now shows "N/A" instead of falling back to GPU activity percentage
- **Linux Diagnose Truncation**: Terminal output box widened from 61→90 chars with word-wrapping for notes/recommendations
- **Linux Fan Speeds Stuck/Wrong**: Sysfs reads now use unbuffered `FileStream` to prevent stale cached data; added hwmon RPM-to-percent estimation fallback
- **Linux Keyboard Zones**: Per-key RGB detection via DMI product name — known per-key models (16-wf0, 16-wf1, Transcend, etc.) now report correct keyboard type
- **EC Timeout / Dead Battery Crash**: Dead battery auto-detection stops battery WMI/EC polling after 3 consecutive 0% reads on AC; battery query cooldown increased from 500ms to 10s; EC-safe `IsOnAcPower()` via `SystemInformation.PowerStatus`; new `Battery.DisableMonitoring` config option; IPC coordination with out-of-process HardwareWorker
- **Auto-Update "16-Bit Application" Error**: SHA256 hash extraction regex fixed to handle backtick-wrapped and per-file hash formats; downloaded files now validated for PE header (MZ) and minimum size (100KB) before execution; actual GitHub asset filename used instead of hardcoded pattern

---

## [2.8.0] - 2026-02-08 - Feature & Safety Update 🚀🔒

### ✨ New Features
- **AMD GPU Overclocking**: Full RDNA/RDNA2/RDNA3 GPU OC via ADL2/Overdrive8 API — core clock, memory clock, and power limit offsets with hardware-reported range clamping and reset-to-defaults
- **Display Overdrive Toggle**: Panel overdrive control via HP WMI BIOS (CMD 0x35/0x36) with auto-detection and UI in Advanced view
- **Game Library Tab**: New lazy-loaded Game Library tab integrating GameLibraryService
- **OSD: Battery %**: Color-coded battery level with AC/battery icon indicator
- **OSD: CPU Clock Speed**: Average core clock in GHz/MHz, auto-formatted  
- **OSD: GPU Clock Speed**: Real-time GPU clock from monitoring sample
- **Logitech HID++ 2.0 Effects**: Full breathing, spectrum, flash, and wave effects with speed control and HID++ 1.0 fallback
- **Corsair HID Effects**: Full breathing, spectrum cycle, and wave effects via direct HID with retry logic
- **Fan Curve Save/Load UX**: Delete presets, import/export fan curves as JSON files, saved presets filter, validation, and auto-apply on save
- **Conflict Detection**: ConflictDetectionService now active at startup with 60s background monitoring

### 🐛 Bug Fixes
- **GPU OC Store-on-Failure**: NvapiService no longer stores offset values when hardware rejects the change — UI now accurately reflects actual applied state
- **Undervolt Safety Clamping**: Intel MSR clamped to [-250, 0] mV; AMD CO clamped to [-30, +30] — prevents accidental extreme values
- **Thermal Protection Debounce**: Added 5s activation / 15s release debounce to prevent fan speed yo-yo; raised thresholds to 90°C/95°C with 10°C hysteresis based on community feedback
- **MaxFanLevel Auto-Detection**: Fixed fan speed mapping on models using 0-100 percentage range instead of 0-55 krpm — auto-detects at startup
- **Game Library Scroll**: Fixed game library not scrolling smoothly — added pixel-based scrolling and proper scroll bar visibility

### 🎨 UI Improvements
- **Tab UI Overhaul**: Scrollable tab headers with animated accent underline, compact padding, and hover effects
- **Corsair HID Brightness**: New brightness slider scaling all RGB values 0-100%

### 🧹 Code Cleanup
- Removed 4 dead-code files (~1,525 lines): SettingsRestorationService, WinRing0MsrAccess, HpCmslService, ConfigBackupService

### 📋 Includes All v2.7.2 Fixes
- Window not showing after reinstall, undervolt silent failure, fan curves reset, settings restoration, OSD FPS fix, Linux EC safety, MIT license, CI YAML fix

### 📦 SHA256 Checksums
```
OmenCoreSetup-2.8.0.exe:      ADD02976B8AE5FF4151E169876D50A54DF60F74B02A8BA2D5FAA11BCB2213173
OmenCore-2.8.0-win-x64.zip:   7DC97B96316FFF37444AB16D60170AF04DC7709D4BEA204CE3545289E70FAAC5
OmenCore-2.8.0-linux-x64.zip: D45942DE957781B9C36C7659778465E597C6788AF0BC86A28331195A8C6A45E9
```

---

## [2.7.2] - 2026-02-07 - Community Bug Fixes & Safety 🐛🔒

### 🐛 Bug Fixes
- **Window Not Showing After Reinstall**: Fixed main window never appearing when config with `StartMinimized=true` survived uninstall. Added `ForceShowMainWindow()` bypassing session suppression for tray actions. Installer now cleans `%APPDATA%\OmenCore` on uninstall.
- **Undervolt Apply Does Nothing**: Fixed `ApplyOffsetAsync()` silently returning success without MSR access. Now throws clear error when PawnIO unavailable. `_lastApplied` only updated after confirmed write.
- **Fan Curves Reset on AC/Battery**: Fixed `PowerAutomationService` discarding custom curves. New `LookupFanPreset()` preserves user curves from config, falls back to built-in definitions.
- **Settings Not Restored on Startup**: GPU Power Boost, fan preset, TCC offset now restored on Windows startup via `RestoreSettingsOnStartupAsync()` with retry logic.
- **OSD FPS Showing GPU Load**: Fixed "Est. FPS" displaying GPU load percentage instead of actual FPS. Integrated RTSS for real FPS data with automatic fallback.
- **Linux EC Panic on OMEN Max**: Blocked EC writes on 2025 OMEN Max 16t/17t models. Added ACPI platform profile and hwmon PWM fan control as safe alternatives.
- **MIT LICENSE 404**: Added MIT LICENSE file to repository.
- **CI YAML Parse Error**: Fixed invisible whitespace in CI workflow file.

---

## [2.7.1] - 2026-02-04 - Desktop Detection Hotfix 🔧

### 🐛 Bug Fixes
- **Desktop Detection Fix (Critical)**: Fixed issue where OmenCore would immediately close on ANY desktop PC, not just OMEN desktops
- **Non-HP Systems Allowed**: App now only blocks confirmed HP OMEN desktop systems (25L, 30L, 35L, 40L, 45L, Obelisk)
- **Improved Chassis Check**: Chassis type verification now only runs for HP systems with "OMEN" in the model name
- **Better Logging**: Added clearer log messages for desktop detection debugging

### 🔧 Technical Details
- Refactored `IsOmenDesktop()` in App.xaml.cs to prevent false positives on non-HP desktops
- Non-HP desktop PCs now launch normally (with monitoring-only mode if fan control unavailable)

---

## [2.7.0] - 2026-02-04 - Reliability & Diagnostics Overhaul 🔧🔍

**Focus:** Hardware monitoring reliability, per-model capability detection, enhanced diagnostics, and fan curve UX improvements

### 🔧 Hardware Monitoring Reliability
- **Worker Auto-Restart**: LibreHardwareMonitor worker process now auto-restarts after 3 consecutive read timeouts
- **Degraded Mode Warning**: Toast notification when monitoring enters degraded mode (2+ timeouts)
- **Graceful Failover**: Seamless fallback to in-process monitoring if worker restart fails
- **TryRestartAsync Interface**: New `IHardwareMonitorBridge.TryRestartAsync()` method for bridge implementations

### ⚙️ PawnIO-Only Mode
- **New Setting**: Optional "PawnIO-Only Mode" to skip WinRing0 entirely for Secure Boot compatibility
- **Settings UI**: Toggle in Settings → Hardware section for users who want exclusive PawnIO access
- **Config Persistence**: Setting saved to user configuration for persistence across sessions
- **WinRing0 Removed**: Completely removed WinRing0 fallback from MsrAccessFactory - no more antivirus false positives

### 🔍 Guided Fan Diagnostic
- **Step-by-Step Testing**: New guided diagnostic that tests 30% → 60% → 100% fan speeds
- **Pass/Fail Summary**: Clear results showing which fan zones responded correctly
- **Progress Tracking**: Real-time status updates during diagnostic execution
- **UI Integration**: "Run Guided Diagnostic" button in Fan Diagnostics view

### 📈 Fan Curve Preview & Validation
- **Live Curve Preview**: Real-time display of predicted fan percentage based on current temperature
- **Curve Validation**: Warnings for dangerous configurations (fan off above 60°C, flat curves above 80°C)
- **Interpolation Logic**: Linear interpolation between curve points with safety clamping
- **Visual Feedback**: Preview panel shows "At X°C → Y%" with color-coded validation messages

### 🎯 Per-Model Capability Database
- **ModelCapabilityDatabase**: Comprehensive database of known OMEN/Victus model capabilities
- **Model-Specific Features**: Per-model flags for fan control, MUX switch, RGB, undervolt support
- **Known Models**: OMEN 15/16/17 (2020-2024), Transcend 14/16, Victus 15/16, Desktop 25L-45L
- **UI Visibility Helpers**: Properties like `ShowFanCurveEditor`, `ShowMuxSwitch`, `ShowRgbLighting` for conditional UI
- **Runtime Probing**: `ProbeRuntimeCapabilities()` validates detected capabilities against actual hardware
- **Model Warnings**: User-facing notes for unknown models or unverified configurations
- **Family Fallbacks**: Unknown models inherit defaults from their detected family (OMEN16, Victus, etc.)

### 🖼️ UI/UX Polish
- **Sidebar Temperature Display**: Fixed CPU/GPU temps showing "--" - now displays live temperatures correctly
- **Quick Actions Disabled Styling**: Buttons now grey out (40% opacity) when unavailable with neutral icon backgrounds
- **Version Display Fixed**: Settings → About now correctly shows v2.7.0 instead of v2.6.1

### 🐛 Bug Fixes
- **Temperature History Not Recording**: Fixed bug where temperature charts showed no data when temps were stable (ShouldUpdateUI optimization was incorrectly skipping history updates)
- **Monitoring Timeout Handling**: Fixed consecutive timeout detection logic for accurate degraded mode triggering
- **Fan Curve Temperature Bounds**: Curve preview properly handles temperatures outside defined points
- **Sidebar Temp Bindings**: Added CpuTemperature/GpuTemperature properties to DashboardViewModel for proper sidebar display
- **Assembly Version Mismatch**: Updated AssemblyVersion and FileVersion from 2.6.1 to 2.7.0 in csproj

### 📋 Technical Details
- **New Files**: `ModelCapabilityDatabase.cs` with 400+ lines of model configurations
- **Interface Changes**: `IHardwareMonitorBridge` extended with `TryRestartAsync()`
- **Config Schema**: Added `FeaturePreferences.PawnIOOnlyMode` boolean property
- **MsrAccessFactory**: Removed WinRing0 fallback, PawnIO-only for MSR/undervolt access
- **DashboardViewModel**: Added `CpuTemperature`, `GpuTemperature` properties with PropertyChanged notifications
- **MainWindow.xaml**: Added IsEnabled triggers for Quick Actions disabled styling

---

## [2.6.0] - 2026-01-18 - Monitoring Dashboard Overhaul 📊🔧

**Complete rewrite of hardware monitoring tab with direct MainViewModel integration**

### 📊 Monitoring Dashboard Overhaul
- **Direct MainViewModel Integration**: Removed complex service injection, dashboard now binds directly to MainViewModel properties
- **Real-time Current Metrics**: Live display of CPU/GPU temperatures, power consumption, battery health, and efficiency metrics
- **Auto-display Charts Grid**: All historical charts (Power, Temperature, Battery, Fan) display simultaneously without button clicks
- **Enhanced Data Binding**: Uses LatestMonitoringSample and HardwareMonitoringService properties for reliable data flow
- **Improved Chart Rendering**: Canvas-based charts with filled areas, grid lines, and statistical summaries

### 🔧 Technical Improvements
- **Service Initialization Fix**: Dashboard loads after MainViewModel is fully initialized, preventing null reference issues
- **Simplified Architecture**: Eliminated dependency injection complexity in favor of direct ViewModel binding
- **Async Operation Handling**: Proper await patterns for all monitoring operations
- **Debug Logging**: Comprehensive logging for troubleshooting monitoring data flow

### 🐛 Bug Fixes
- **Empty Tables Issue**: Fixed monitoring tab showing empty current metrics tables
- **Chart Display**: Charts now display historical data immediately on tab load
- **Data Binding**: Reliable connection between monitoring service and UI controls
- **Fan Reset on Exit**: Fans now properly reset to BIOS/Windows auto control when app closes instead of staying at last manual setting

### 📋 Known Issues
- Windows Defender may flag LibreHardwareMonitor/WinRing0 (known false positive - use PawnIO for Secure Boot)
- MSI Afterburner shared memory conflicts (detection added, resolution planned for v2.6.0)
- Some older Linux kernels may have limited HP WMI integration

---

## [2.5.0] - 2026-01-17 - Advanced RGB Lighting & Hardware Monitoring 🎨📊

**Comprehensive RGB lighting integration, temperature-responsive effects, power monitoring, and fan curve visualization**

---

## [2.5.1] - 2026-01-21 - Fan "Max" Reliability & Diagnostics 🔧🌀

**Focus:** Make the "Max" fan preset robust and observable, avoid misleading UI state (showing 100% when fans aren't actually at max), and improve diagnostics for support.

### Highlights
- **Verify Max Applied**: After applying Max, the app now verifies fan RPM/duty and only shows 100% when verified.
- **Retry + Fallbacks**: Added short retry loops and alternative sequences (SetFanMax / SetFanLevel) to handle vendor BIOS quirks.
- **Diagnostics**: Diagnostic export can optionally run a quick fan Max verification and include the results for faster triage.
- **Logging**: Improved debug + verification logs to help identify hardware/driver failures.

### 🌀 Fan Control Improvements
- **Fan Reset on Exit**: Fans now properly reset to BIOS/Windows auto control when app closes instead of staying at last manual setting
- **Max Mode Verification**: Real-time verification that Max fan preset is actually applied with RPM feedback
- **Enhanced Diagnostics**: Comprehensive fan diagnostics with hardware worker integration for accurate RPM readings
- **BIOS Compatibility**: Improved handling of different BIOS versions and vendor-specific fan control quirks

### 🔧 Technical Improvements
- **PawnIO CPU Temperature Fallback**: HardwareWorker now falls back to PawnIO for CPU temperature if WinRing0/LibreHardwareMonitor fails
- **Service Provider Access**: Added public ServiceProvider property for better service access patterns
- **Shutdown Sequence**: Explicit fan auto-control restoration during application exit
- **Error Handling**: Improved error handling for fan control operations with detailed logging

### 🐛 Bug Fixes
- **Fan State Persistence**: Fixed issue where fans would stay at manual speeds after application exit
- **Max Preset Reliability**: Ensured Max fan preset actually achieves maximum fan speeds with verification
- **UI State Accuracy**: Fan percentage display now accurately reflects actual fan speeds, not just intended settings

### 📋 Known Issues
- Windows Defender may flag LibreHardwareMonitor/WinRing0 (known false positive - use PawnIO for Secure Boot)
- MSI Afterburner shared memory conflicts (detection added, resolution planned for v2.6.0)
- Some older Linux kernels may have limited HP WMI integration

### 📦 Installer Hashes (SHA256)

**OmenCore v2.5.1 Release Artifacts**

| File | SHA256 Hash |
|------|-------------|
| `OmenCoreSetup-2.5.1.exe` | `FB7391404867CABCBAE14D70E4BD9D7B31C76D22792BB4D9C0D9D571DA91F83A` |
| `OmenCore-2.5.1-win-x64.zip` | `05055ABAC5ABBC811AF899E0F0AFEE708FE9C28C4079015FAFE85AA4EFE1989F` |
| `OmenCore-2.5.1-linux-x64.zip` | `AD07B9610B6E49B383E5FA33E0855329256FFE333F4EB6F151B6F6A3F1EBD1BC` |

**Verification Instructions:**
- Windows: `Get-FileHash -Algorithm SHA256 OmenCoreSetup-2.5.1.exe`
- Linux: `sha256sum OmenCore-2.5.1-linux-x64.zip`
- Cross-platform: Compare against the hashes above to verify download integrity

### 🎨 Advanced RGB Lighting System
- **Temperature-Responsive Lighting**: Keyboard and RGB devices change colors based on CPU/GPU temperatures with configurable thresholds
- **Performance Mode Sync**: RGB lighting automatically syncs with performance modes (Performance/Balanced/Silent)
- **Throttling Indicators**: Flashing red lighting alerts when thermal throttling is detected
- **6 New Lighting Presets**: Wave Blue/Red, Breathing Green, Reactive Purple, Spectrum Flow, Audio Reactive
- **Multi-Vendor Support**: Enhanced integration with HP OMEN, Corsair, Logitech, and Razer devices

### 📊 Hardware Monitoring Enhancements
- **Power Consumption Tracking**: Real-time power monitoring with efficiency metrics and trend analysis
- **Battery Health Monitoring**: Comprehensive battery assessment with wear level and cycle count tracking
- **Live Fan Curve Visualization**: Interactive charts showing temperature vs fan speed relationships
- **Cross-Device RGB Sync**: Hardware state changes sync across all connected RGB peripherals

### 🛡️ Reliability & Verification
- **Power Limit Verification**: Reads back EC registers to verify power limits applied successfully
- **Enhanced Diagnostics**: Improved logging and conflict detection for better troubleshooting
- **GPU Power Boost Integration**: Combined WMI BIOS + NVAPI control for accurate power management

### 🔧 Technical Improvements
- **Fan Control Stability**: Enhanced hysteresis and GPU power boost integration prevents oscillation
- **Accurate RPM Reading**: Realistic temperature-based fan curve estimation for auto/BIOS mode
- **PawnIO Promotion**: Better Secure Boot compatibility guidance and automatic driver selection

### 📋 Known Issues
- Windows Defender may flag LibreHardwareMonitor/WinRing0 (known false positive - use PawnIO for Secure Boot)
- MSI Afterburner shared memory conflicts (detection added, resolution planned for v2.6.0)
- Some older Linux kernels may have limited HP WMI integration

### 📦 Installer Hashes (SHA256)

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

---

## [2.3.2] - 2026-01-14 - Critical Safety & Bug Fix Release 🛡️

**Desktop safety protection + Linux GUI fix + Multiple bug fixes**

### 🛡️ CRITICAL: Desktop PC Safety
- **Desktop systems now require explicit user confirmation before enabling fan control**
- OmenCore is designed for OMEN LAPTOPS - desktop EC registers are completely different
- Warning upgraded from "experimental" to "NOT SUPPORTED - USE AT YOUR OWN RISK"
- Fan control disabled by default on desktop systems
- Added blocking confirmation dialog for desktop users

### 🐧 Linux GUI Crash Fix
- **Fixed**: GUI crashed on startup with "StaticResource 'DarkBackgroundBrush' not found"
- Changed `StaticResource` to `DynamicResource` in all Avalonia XAML views
- Fixes resource loading order issue on Debian 13 and Ubuntu 24.04

### 🔧 Bug Fixes
- **OSD Mode Update**: OSD now properly updates when switching performance/fan modes
- **Fan Control Fallback**: Improved V2 command fallback for OMEN Max/17-ck models
- **Window Corners**: Improved rounded corner rendering with explicit clip geometry
- **Window Size**: Reduced minimum size from 900×600 to 850×550

### 📋 Known Issues
- FPS counter is estimated from GPU load (accurate FPS via D3D11 hook in v2.4.0)
- Some OMEN Max/17-ck models may still have fan control issues - we need more testing data

---

## [2.3.1] - 2026-01-12 - Critical Bug Fix Release 🔥

**Thermal Shutdown Fix + Fan Control Improvements + OSD Enhancements**

### 🔴 Critical: Battlefield 6 Thermal Shutdown Fix
- **Fixed**: Storage drive sleep causing SafeFileHandle disposal crash → thermal shutdown during gaming
- RTX 4090 at 87°C: when storage drives slept, temp monitoring crashed, fans couldn't respond → shutdown
- Added per-device exception isolation: storage failures no longer affect CPU/GPU monitoring
- Prevents thermal shutdowns during extended gaming sessions

### 🔴 Critical: Fan Drops to 0 RPM Fix
- **Fixed**: Fans would boost high then drop to 0 RPM at 60-70°C after thermal protection
- Affected Victus 16 and OMEN Max 16 laptops with aggressive BIOS fan control
- Added "safe release" temperature (55°C) and minimum fan floor (30%)
- Prevents BIOS from stopping fans when system is still gaming-warm

### 🌡️ Adjustable Thermal Protection Threshold
- **NEW**: Thermal protection threshold now configurable 70-90°C (default 80°C)
- Advanced users can increase if their laptop handles heat better
- Setting in: Settings → Fan Hysteresis → Thermal Protection Threshold

### 📊 OSD Network Traffic Monitoring
- **Upload speed** display in Mbps (blue arrow ↑)
- **Download speed** display in Mbps (green arrow ↓)
- Auto-detects active network interface (Ethernet/WiFi)
- Updates every 5 seconds alongside ping monitoring

### 📐 OSD Horizontal Layout Option
- Added layout toggle in Settings → OSD → Horizontal Layout
- Stores preference in config (full XAML implementation coming in v2.3.2)

### 📐 Window Sizing for Multi-Monitor
- Reduced minimum window size from 1100×700 to 900×600
- Works better with smaller/vertical secondary monitors

### 🐧 Linux Kernel 6.18 Notes
- Added documentation for upcoming HP-WMI driver improvements
- Better native fan curve control via sysfs

[Full v2.3.1 Changelog](docs/CHANGELOG_v2.3.1.md)

---

## [2.3.0] - 2025-01-12 - Major Feature Release 🚀

**Safety, Diagnostics, and Enhanced Linux Support**

### 🛡️ Fan Curve Safety System
- **Real-time validation** in fan curve editor detects dangerous configurations
- **Hardware watchdog** monitors for frozen temperature sensors (auto-sets 100% if frozen)
- **Curve recovery** system auto-reverts to last-known-good preset on sustained overheating
- Visual warning banners with specific recommendations

### 📦 Profile Import/Export
- **Unified `.omencore` format** for complete configuration backup
- Export fan presets, performance modes, RGB presets, and settings
- Selective import (choose which components to merge)

### 🔋 Custom Battery Thresholds
- **Adjustable charge limit slider** (60-100%, previously fixed at 80%)
- Recommendations: 60-70% for longevity, 80% for daily use, 100% for travel
- Real-time threshold application via HP WMI BIOS

### 🔄 Auto-Update Check
- **Non-intrusive GitHub Releases API check** (once per session)
- Privacy-respecting, no telemetry
- Shows update availability in status bar (UI integration pending)

### 📊 Diagnostics Export
- **One-click ZIP bundle** with logs, config, system info, hardware status
- Ready to attach to GitHub issues

### 🐧 Linux Improvements
- **Enhanced 2023+ OMEN support** with HP-WMI thermal profile switching
- `omencore-cli diagnose --report` generates pasteable GitHub issue templates
- Direct fan control via `fan1_output`/`fan2_output` (when available)
- Improved detection: hp-wmi only reports available when control files exist
- Thermal profiles work even without direct fan PWM access

[Full v2.3.0 Changelog](docs/CHANGELOG_v2.3.0.md)

---

## [2.2.3] - Not Released (Merged into v2.3.0)

### 🐛 Bug Fixes
- **Critical: Fan Speed Drops to 0 RPM** - Fixed fans dropping to minimum when temp exceeded curve
  - Curve fallback now uses highest fan% instead of lowest as safety measure
  - Prevents thermal shutdowns when temperature exceeds defined curve points
- **Fan Diagnostics: Curve Override** - Test speeds no longer get overridden by curve engine
  - Added diagnostic mode that suspends curve during fan testing
- **Fan Diagnostics: 100% Not Max** - Setting 100% now uses SetFanMax for true maximum RPM
- **Fan Diagnostics: UI Not Updating** - Fixed display not refreshing after test completion

### 🎨 Linux GUI Overhaul
- **Theme System** - Comprehensive new OmenTheme.axaml with 300+ style definitions
  - Card styles: `.card`, `.cardInteractive`, `.surfaceCard`, `.statusCard`
  - Button variants: `.primary`, `.secondary`, `.danger`, `.ghost`, `.iconButton`
  - Text hierarchy: `.pageHeader`, `.sectionHeader`, `.subsectionHeader`, `.caption`
  - Navigation styles with active state tracking
  - Smooth hover transitions on interactive elements
- **Dashboard** - Complete redesign matching Windows version
  - Session uptime tracking with peak temperature display
  - Quick status bar showing fans, performance mode, power source
  - Hardware summary cards for CPU, GPU, Fans, Memory
  - Throttling warning banner when thermal throttling detected
- **Fan Control** - Enhanced fan curve editor
  - Real-time status cards with centered layout
  - Save preset and Emergency Stop buttons
  - Hysteresis setting for curve stability
- **System Control** - Visual performance mode selector
  - 4-column button grid with emoji icons and descriptions
  - Current mode indicator with accent highlight
- **Settings** - Reorganized with card-based dark panels
  - Section icons with emojis
  - About section with version and GitHub link
- **MainWindow** - Improved sidebar navigation
  - System status panel showing Performance/Fan modes
  - Version display and connection indicator
  - "Linux Edition" branding

### 🐧 Linux CLI
- **New: Diagnose command** - `omencore-cli diagnose` prints kernel/modules/sysfs status and next-step recommendations
- **Improved: 2023+ model detection** - More accurate `hp-wmi` detection and fewer misleading EC availability reports

### 📝 Documentation
- **Smart App Control** - Added workarounds for Windows 11 Smart App Control blocking installer

---

## [2.2.2] - 2026-01-10

### 🐛 Bug Fixes
- **Critical: Temperature Monitoring Freezes (#39, #40)** - Fixed temps getting stuck causing fan issues
  - Added staleness detection to HardwareWorker and client
  - Worker tracks consecutive identical readings and marks sample as stale
  - Client auto-restarts worker when stale data detected for 30+ seconds
  - Prevents fans from staying at high RPM due to frozen temperature readings
  - Prevents thermal throttling caused by fans not responding to heat

### ⚠️ Known Issues
- **OMEN 14 Transcend** - Power mode and fan behavior may be erratic (under investigation)
- **2023 XF Model** - Keyboard lighting requires OMEN Gaming Hub installed
- **Windows Defender** - May flag as `Win32/Sonbokli.A!cl` (ML false positive, common for GitHub projects)

---

## [2.2.1] - 2026-01-08

### ✨ New Features
- **EC Reset to Defaults** - Added option to reset EC to factory state
  - New "Reset EC" button in Settings → Hardware Driver section
  - Resets fan speed overrides, boost mode, BIOS control flags, and thermal timers
  - Use this if BIOS displays show stuck/incorrect fan values after using OmenCore
  - Shows confirmation dialog with explanation of what will be reset

### 🐛 Bug Fixes
- **Thermal Protection Logic (#32)** - Fixed thermal protection reducing fan speed instead of boosting
  - No longer drops from 100% to 77% when temps hit warning threshold
  - Correctly restores original fan mode/preset after thermal event (not always "Quiet")
  - Remembers pre-thermal state: Max mode stays Max, custom presets stay custom
- **Tray Menu Max/Auto Not Working (#33)** - Fixed system tray fan mode buttons
  - "Max" now correctly enables SetFanMax for true 100% fan speed
  - "Auto" properly enables BIOS-controlled automatic fan mode
  - "Quiet" correctly applies quiet/silent mode
- **OMEN Max 16 Light Bar Zone Order** - Fixed inverted RGB zones
  - Added "Invert RGB Zone Order" setting in Settings → Hardware
  - Enable for OMEN Max 16 where light bar zones run right-to-left
  - Zone 1 = Right, Zone 4 = Left when inverted
- **CPU Temp Stuck at 0°C (#35)** - Improved temperature sensor fallback
  - Better detection of alternative temperature sensors when primary fails
  - Auto-reinitialize hardware monitor after consecutive zero readings
- **CPU Temp Always 96°C (#36)** - Fixed TjMax being displayed instead of current temp
  - Added validation to detect stuck-at-TjMax readings
  - Automatically switches to alternative sensor when primary reports TjMax
- **Temperature Freeze When Drives Sleep** - Fixed temps freezing after SafeFileHandle error
  - Storage drives going to sleep no longer freeze all temperature monitoring
  - HardwareWorker now catches disposed object errors at visitor level
  - Other sensors continue updating when one hardware device fails

### ⚠️ Known Issues
- **OMEN Max 16 Keyboard Lighting** - The RGB controls only affect the front light bar, not the keyboard
  - Hardware limitation: OMEN Max 16 keyboard uses single-color white/amber backlight (Fn+F4)
  - RGB section in OmenCore controls the 4-zone front light bar only
- **Linux: Fedora 43+ ec_sys module missing** - Use `hp-wmi` driver as alternative or build module from source

---

## [2.2.0] - 2026-01-07

### 📦 Downloads
| File | SHA256 |
|------|--------|
| OmenCoreSetup-2.2.0.exe | `B4982315E979D8DE38471032A7FE07D80165F522372F5EFA43095DE2D42FF56B` |
| OmenCore-2.2.0-win-x64.zip | `542D65C5FD18D03774B14BD0C376914D0A7EE486F8B12D841A195823A0503288` |
| OmenCore-2.2.0-linux-x64.zip | `ADBF700F1DA0741D2EE47061EE2194A031B519C5618491526BC380FE0370F179` |

### ✨ New Features
- **GPU OC Profiles** - Save and load GPU overclock configurations
  - Create named profiles with core clock, memory clock, and power limit settings
  - Quick profile switching via dropdown
  - Profiles persist across app restarts
  - Delete unwanted profiles with one click
- **Fan Profile Persistence** - Custom fan curves now save automatically
  - Custom curves persist to config file when applied
  - Restored on app startup with "Custom" or "Independent" presets
- **Linux Auto Mode Fix** - Improved automatic fan control restoration
  - Full EC register reset sequence (BIOS control, fan state, boost, timer)
  - HP-WMI driver support as fallback for newer models
  - Proper cleanup of manual speed registers
- **Dashboard UI Enhancements** - Improved monitoring dashboard with at-a-glance status
  - Quick Status Bar: Fan RPMs, Performance Mode, Fan Mode, Power status
  - Session Uptime tracking (updates every second)
  - Peak Temperature tracking (highest CPU/GPU temps this session)

### ⚡ Performance
- **Lazy-Load Peripheral SDKs** - Corsair, Logitech, and Razer SDKs only load when explicitly enabled
  - Faster startup for users without these peripherals
  - Enable in Settings → Features when you have Corsair/Logitech/Razer devices
  - Reduces memory footprint when peripherals are disabled

### 🐛 Bug Fixes
- **Fan Always On Fix** - Auto mode now properly lets BIOS control fans
  - Fixed issue where fans never stopped even at idle/low temperatures
  - Auto/Default presets now call RestoreAutoControl() to reset fan levels to 0
  - BIOS can now spin down fans when system is cool (fixes Reddit report: OMEN 17 13700HX fans always running)
- **Fan Curve Editor Crash** - Fixed `ArgumentException` when dragging points beyond chart bounds (Issue #30)
- **Fan Curve Mouse Release** - Fixed cursor not releasing drag point when moving outside chart area or releasing mouse button
- **Per-Core Undervolt Crash** - Fixed missing `SurfaceBrush` resource causing XAML parse exception (Issue #31)
- **Animation Parse Error** - Fixed invalid `FanSpinStoryboard` that caused XAML parse errors on startup
- **OMEN Key False Trigger** - Fixed window opening when launching Remote Desktop or media apps (VK_LAUNCH_APP1 scan code validation)

### ⚡ Performance & Memory
- **QuickPopup Memory Leak** - Fixed timer event handlers not unsubscribed on window close
- **Thermal Sample Trimming** - Optimized O(n²) removal loop to single-pass calculation
- **Exception Logging** - Added logging to 10+ empty catch blocks for better debugging

### 🔍 Known Issues Under Investigation
- **Fan Max Mode Cycling** - Fan speed cycling between high/low in Max mode (needs more logs to diagnose)
- **dGPU Sleep Prevention** - Constant polling may prevent NVIDIA GPU sleep causing battery drain
- **Fan Speed Throttling** - Max fan speed may decrease under heavy load (6300→5000 RPM reported)

### 🔐 Security & Stability
- **Named Pipe Security** - Added `PipeOptions.CurrentUserOnly` to prevent unauthorized IPC access
- **Async Exception Handling** - Fixed `async void` in worker initialization for proper exception propagation
- **Improved Logging** - Added meaningful logging to previously bare catch blocks in HardwareWorkerClient
- **Installer Verification** - SHA256 hash verification for LibreHardwareMonitor downloads

### 🎨 User Interface Improvements
- **System Tray Menu Overhaul**
  - Consolidated Quick Profiles with descriptive labels (e.g., "🚀 Performance — Max cooling + Performance mode")
  - Grouped Fan Control, Power Profile, and Display under new "Advanced" submenu
  - Monospace font for temperature/load readings for better alignment
  - Clearer menu item descriptions throughout
- **New Animations** - Added 5 new smooth animation presets:
  - FadeInFast, SlideInFromBottom, ScaleIn, Breathing, FanSpin
- **Installer Wizard Images** - Updated to feature-focused design (no hardcoded version numbers)

### 📋 Details
See [CHANGELOG_v2.2.0.md](docs/CHANGELOG_v2.2.0.md) for full details.

---

## [2.1.2] - 2026-01-06

### 🐛 Bug Fixes
- **Temperature Freeze** - Fixed CPU/GPU temps freezing when storage drives go to sleep
- **OMEN Max V2 Detection** - Added model-name-based V2 thermal policy detection for OMEN Max 2025+ models

### 📋 Details
See [CHANGELOG_v2.1.2.md](docs/CHANGELOG_v2.1.2.md) for full details.

---

## [2.1.1] - 2026-01-05

### 🐛 Bug Fixes
- **Desktop Detection** - Block startup on OMEN Desktop (25L/30L/35L/40L/45L) to prevent BIOS corruption
- **Fan Speed Reset** - Fixed fans resetting to auto when starting games/stress tests
- **Quick Popup** - G-Helper style - left-click tray for quick controls
- **Reduced Polling** - Default 2000ms for better performance

### 📋 Details
See [CHANGELOG_v2.1.1.md](docs/CHANGELOG_v2.1.1.md) for full details.

---

## [2.0.0-beta] - 2025-12-28

### 🚀 Major Architecture Changes

#### Out-of-Process Hardware Monitoring
- **HardwareWorker** - New separate process for hardware monitoring
  - Eliminates stack overflow crashes from LibreHardwareMonitor recursive GPU queries
  - JSON-based IPC over named pipes for parent-child communication
  - Automatic restart with exponential backoff if worker crashes
  - Parent process monitoring - worker exits cleanly if main app closes
  - Log rotation: 5MB max file size, 3 backup files retained
  - Graceful shutdown with CancellationToken support

#### Self-Contained Deployment
- **Both executables now embed .NET runtime** - No separate .NET installation required
  - OmenCore.exe: Full WPF app with embedded runtime
  - OmenCore.HardwareWorker.exe: Worker process with embedded runtime
  - Single-file executables with native libraries extracted at first run

### ✨ New Features

#### Logitech SDK Improvements
- **Spectrum/Flash Effects** - New effect types added to ILogitechSdkProvider interface
  - `ApplySpectrumEffectAsync(device, speed)` - Rainbow color cycling
  - `ApplyFlashEffectAsync(device, color, duration, interval)` - Strobe/alert effect
- **80+ Device Support** - Massively expanded device PID database
  - G502 X, G502 X PLUS, G502 X Lightspeed
  - G PRO X 60 (LIGHTSPEED, wired variants)
  - G309 LIGHTSPEED gaming mouse
  - PRO X 2 LIGHTSPEED headset
  - ASTRO A30, A50 Gen 4 headsets
  - G915 X TKL, G915 X Full-size keyboards
  - All 2024/2025 product releases covered
  - Organized by device series (G5xx, G3xx, G9xx, PRO, ASTRO)

#### Linux CLI Enhancements (OmenCore.Linux)
- **ConfigCommand** - Full configuration management
  - `omencore config --show` - Display current settings
  - `omencore config --set key=value` - Update individual settings
  - `omencore config --get key` - Query specific setting
  - `omencore config --reset` - Restore defaults
  - `omencore config --apply` - Apply configuration changes
  - Config stored at `~/.config/omencore/config.json`
- **DaemonCommand** - Systemd service management
  - `omencore daemon --install` - Install as systemd service
  - `omencore daemon --start/--stop/--status` - Control service
  - `omencore daemon --generate-service` - Output service unit file
  - Automatic dependency installation (polkit rules, etc.)
- **JSON Output** - Machine-readable output for scripting
  - `omencore status --json` - JSON formatted temps, fans, perf mode
  - Global `--json` flag available on all commands

#### System Optimizer (Windows)
- **6 Optimization Categories** with individual toggle controls:
  - **Power**: Ultimate Performance plan, Hardware GPU Scheduling, Game Mode, Foreground Priority
  - **Services**: Telemetry, SysMain/Superfetch, Windows Search, DiagTrack
  - **Network**: TCP NoDelay, TCP ACK Frequency, Delivery Optimization, Nagle Algorithm
  - **Input**: Mouse Acceleration, Game DVR, Game Bar, Fullscreen Optimizations
  - **Visual**: Transparency, Animations, Shadows, Best Performance preset
  - **Storage**: SSD TRIM, Last Access Timestamps, 8.3 Names, Prefetch
- **Risk Indicators** - Low/Medium/High risk badges on each optimization
- **Preset Buttons** - "Gaming Maximum" and "Balanced" one-click presets
- **Registry Backup** - Automatic backup before changes, restore on revert
- **System Restore Points** - Creates restore point before applying optimizations

### 🐛 Bug Fixes

- **Stack Overflow Prevention** - Out-of-process architecture eliminates LibreHardwareMonitor crashes
- **SafeFileHandle Disposal** - Fixed "Cannot access a disposed object" errors in HardwareWorker
- **Version Consistency** - All assemblies now report 2.0.0 correctly
- **Log Rotation** - HardwareWorker logs no longer grow unbounded

### 🔧 Technical Changes

- **ILogitechSdkProvider Interface** - Extended with spectrum and flash effect methods
- **LogitechHidDirect** - Reorganized PID database by device series
- **LogitechRgbProvider** - Added `RgbEffectType.Spectrum` to supported effects
- **HardwareWorker IPC** - JSON protocol: `{"type":"temps"|"fans"|"ping"|"quit"}`
- **Build Configuration** - Both csproj files now have explicit SelfContained=true

### 📦 Dependencies

- .NET 8.0 (embedded in single-file executables)
- LibreHardwareMonitorLib 0.9.4
- RGB.NET.Core 3.1.0
- CUE.NET 1.2.0.1 (Corsair SDK)

---

## [1.6.0-alpha] - 2025-12-25

### Added
- **🎨 System RGB provider (experimental)** - `RgbNetSystemProvider` uses RGB.NET to control supported desktop RGB devices; supports static color application via `color:#RRGGBB`.
- **✨ Corsair preset application via providers** - `CorsairRgbProvider` now supports `preset:<name>` applying presets saved in configuration (`CorsairLightingPresets`).
- **🔁 RgbManager wiring & provider stack** - Providers are registered at startup in priority: Corsair → Logitech → Razer → SystemGeneric, enabling a single entrypoint to apply system-wide lighting effects.
- **🧪 Unit tests** - Added `CorsairRgbProviderTests` and preliminary tests for RGB provider wiring and behavior.
- **📄 Docs & dev notes** - Updated `docs/V2_DEVELOPMENT.md` and `CHANGELOG` to reflect Phase 3 design and spike work.

### Changed
- **Lighting subsystem** - `LightingViewModel` now accepts an `RgbManager` instance to expose provider actions to the UI and tests.

---

## [1.5.0-beta] - 2025-12-17

### Added
- **🔍 OmenCap.exe Detection** - Detects HP OmenCap running from Windows DriverStore
  - This component persists after OGH uninstall and blocks MSR access
  - Shows warning with detailed removal instructions
  - Prevents false "XTU blocking undervolt" errors
- **🧹 DriverStore Cleanup Info** - OGH cleanup now detects OmenCap in DriverStore
  - Provides pnputil commands for complete removal
  - Logs detailed instructions for manual cleanup
- **🔧 Experimental EC Keyboard Setting** - Now always visible in Settings > Features
  - Previously was hidden until Keyboard Lighting was enabled

### Fixed
- **💥 System Tray Crash** - Fixed crash when right-clicking tray icon
  - Bad ControlTemplate tried to put Popup inside Border
  - Replaced with simpler style-based approach
- **📐 Sidebar Width** - Increased sidebar from 200px to 230px for better readability
- **🔢 Version Display** - Updated to v1.5.0-beta throughout app
- **🔄 Tray Icon Update Crash** - Fixed "Specified element is already the logical child" error
  - Issue in UpdateFanMode/UpdatePerformanceMode when updating menu headers
  - Now creates fresh UI elements instead of reusing existing ones
  - Fixed SetFanMode, SetPerformanceMode, and UpdateRefreshRateMenuItem methods

### Changed
- Process kill list now includes OmenCap.exe
- Undervolt provider detects OmenCap as external controller
- SystemControl view shows specific OmenCap removal instructions

See [CHANGELOG_v1.5.0-beta.md](docs/CHANGELOG_v1.5.0-beta.md) for full details.

---

## [1.4.0-beta] - 2025-12-16

### Added
- **🎨 Interactive 4-Zone Keyboard Controls** - Visual zone editor with hex color input and presets
- **🚀 StartupSequencer Service** - Centralized boot-time reliability with retry logic
- **🖼️ Splash Screen** - Branded OMEN loading experience with progress tracking
- **🔔 In-App Notification Center** - Extended notification service with read/unread tracking
- **Fan Profile UI Redesign** - Card-based preset selector with visual icons
- **OSD TopCenter/BottomCenter** - New overlay position options
- **Undervolt Status Messages** - Informative explanations when undervolting unavailable

### Fixed
- **TCC Offset Persistence** - CPU temp limit now survives reboots
- **Thermal Protection Thresholds** - More aggressive fan ramping (80°C warning, 88°C emergency)
- **Auto-Start Detection** - Correctly detects existing startup entries
- **SSD Sensor 0°C** - Storage widget hides when no temperature data available
- **Overlay Hotkey Retry** - Hotkey registration retries when starting minimized
- **Tray Refresh Rate Display** - Updates immediately after changing
- **Undervolt Section Visibility** - Hides on unsupported AMD systems

See [CHANGELOG_v1.4.0-beta.md](docs/CHANGELOG_v1.4.0-beta.md) for full details.

---

## [1.3.0-beta2] - 2025-12-15

### Fixed
- **Fan presets now work** - All presets (Auto, Quiet, Max) function correctly on all models
- **GPU Power Boost persists** - TGP settings survive Windows restart with multi-stage retry
- **OSD overlay fixed** - Works correctly when starting minimized to tray
- **OMEN key interception fixed** - Settings UI now properly controls the hook
- **Start minimized reliable** - Consistent tray-only startup behavior
- **Intel XTU false positive** - Now uses ServiceController for accurate detection

### Added
- Built-in "Quiet" fan preset with gentle curve
- "ShowQuickPopup" as default OMEN key action
- Temperature smoothing (EMA) for stable UI display
- Real CPU/GPU load values in OSD overlay

See [CHANGELOG_v1.3.0-beta2.md](docs/CHANGELOG_v1.3.0-beta2.md) for full details.

---

## [1.3.0-beta] - 2025-12-14

See [CHANGELOG_v1.3.0-beta.md](docs/CHANGELOG_v1.3.0-beta.md) for full details.

---

## [1.2.0] - 2025-12-14 (Major Release)

### Added
- **🔋 Power Automation** - Auto-switch profiles on AC/Battery change
  - Configurable presets for AC and Battery power states
  - Settings UI in Settings tab
  - Event-driven, minimal resource usage
- **🌡️ Dynamic Tray Icon** - Color-coded temperature display in system tray
  - Green: Cool (<60°C), Yellow: Warm (60-75°C), Red: Hot (>75°C)
  - Real-time temperature updates
- **🔒 Single Instance Enforcement** - Prevents multiple copies from running (mutex-based)
- **🖥️ Display Control** - Quick refresh rate switching from tray menu
  - Toggle between high/low refresh rates
  - "Turn Off Display" option for background tasks
- **📌 Stay on Top** - Keep main window above all other windows (toggle in tray menu)
- **⚠️ Throttling Detection** - Real-time throttling status in dashboard header
  - Detects CPU/GPU thermal throttling
  - Detects CPU/GPU power throttling (TDP limits)
  - Warning indicator appears when system is throttling
- **⏱️ Fan Countdown Extension** - Automatically re-applies fan settings every 90s to prevent HP BIOS 120-second timeout
- **📊 Configurable Logging** - Log verbosity setting in config (Error/Warning/Info/Debug)
  - Empty log lines filtered in non-Debug mode

### Fixed
- **🔧 .NET Runtime Embedded** - App is now fully self-contained, no separate .NET installation required
- **🌀 Fan Mode Reverting (GitHub #7)** - Improved WMI command ordering, fan modes now persist correctly
  - Added countdown extension timer to prevent BIOS timeout
- **⚡ High CPU Usage** - 5x slower polling in low overhead mode (5s vs 1s)
- **⚡ DPC Latency** - Extended cache lifetime to 3 seconds in low overhead mode

### Changed
- Installer simplified - removed .NET download logic
- Self-contained single-file build with embedded runtime
- Performance optimizations for reduced system impact
- Tray menu reorganized with Display submenu

### Technical Notes
- Build: `dotnet publish --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true`
- New: `PowerAutomationService.cs` for AC/Battery switching
- New: `DisplayService.cs` for refresh rate and display control
- New: `TrayIconService.CreateTempIcon()` for dynamic temperature icons
- New: `WmiFanController._countdownExtensionTimer` - 90-second interval timer
- New: `LogLevel` enum and configurable verbosity
- New: `StayOnTop` config option
- Low overhead cache: 3000ms (was 100ms)

See [CHANGELOG_v1.2.0.md](docs/CHANGELOG_v1.2.0.md) for full details.

---

## [1.1.2] - 2025-12-13

### Added
- **Task Scheduler Startup** - Windows startup now uses scheduled task with elevated privileges (fixes startup issues)
- **Gaming Fan Preset** - New aggressive cooling preset using Performance thermal policy for gaming
- **GPU Power Boost Persistence** - Last used GPU power level saved to config and restored on startup
- **Fan Curve Editor Guide** - "How Fan Curves Work" explanation box with examples
- **Auto Hardware Reinit** - LibreHardwareMonitor auto-reinitializes when CPU temp stuck at 0°C

### Fixed
- **Startup Issues** - OmenCore now properly starts with Windows (Task Scheduler with HIGHEST privileges)
- **CPU Temp 0°C on AMD** - Extended sensor detection for Ryzen 8940HX, Hawk Point, and other AMD CPUs
- **Auto Fan Mode** - Clarified that "Auto" uses BIOS default; added "Gaming" preset for aggressive cooling

### Changed
- **Secure Boot Banner** - Now shows specific limitation and actionable solution (install PawnIO)
- **GPU Mode Switching UI** - Added hardware limitation warning and BIOS fallback guidance
- **GPU Power Boost UI** - Added warning about potential reset after sleep/reboot (BIOS behavior)
- **Fan Presets** - Added detailed tooltips explaining each preset's behavior

### Technical Notes
- Task name: `OmenCore` with `onlogon` trigger and `highest` run level
- New config properties: `LastGpuPowerBoostLevel`, `LastFanPresetName`
- New `FanMode` values: `Performance`, `Quiet`
- Extended AMD CPU sensor fallbacks (15+ patterns including CCD variants, SoC, Socket)
- `LibreHardwareMonitorImpl.Reinitialize()` method for sensor recovery

See [CHANGELOG_v1.1.2.md](docs/CHANGELOG_v1.1.2.md) for full details.

---

## [1.1.1] - 2025-12-13

### Added
- **Smooth Scrolling** - New `SmoothScrollViewer` style with pixel-based scrolling for improved UX
- **SystemControlView Scrolling** - Added ScrollViewer wrapper so long content scrolls correctly
- **Modern Scrollbar Style** - Thin scrollbars with hover-fade effect

### Changed
- Applied smooth scrolling to all major views: Dashboard, Settings, Lighting, SystemControl, MainWindow
- Improved scroll responsiveness throughout the application

### UI/UX Improvements
- Eliminated chunky item-based scrolling that felt slow and unintuitive
- Consistent brand imagery (Corsair/Logitech) in RGB & Peripherals tab
- Verified typography and color palette consistency across all views

---

## [1.0.0.7] - 2025-12-XX

### Fixed
- **Multi-instance game detection** - Fixed crash when multiple instances of the same game are running (dictionary key collision)
- **Thread-safe process tracking** - Switched from `Dictionary` to `ConcurrentDictionary` for lock-free concurrent access
- **Process.StartTime exception** - Added try-catch for processes that exit during WMI scan
- **Resource cleanup** - Added missing `Dispose()` calls for `ProcessMonitoringService` and `GameProfileService` in `MainViewModel`
- **Fire-and-forget save errors** - Profile saves now have proper error handling with logging instead of silent failures

### Technical Notes
- `ActiveProcesses` now keyed by Process ID (int) instead of name (string) to support multiple game instances
- `ConcurrentDictionary<int, ProcessInfo>` eliminates race conditions in multi-threaded process monitoring
- Robust `StartTime` access wrapped in try-catch to handle process exit during enumeration
- Added `IDisposable` pattern enforcement for monitoring services

---

## [1.0.0.6] - 2025-12-XX

### Added
- **Game Profile System** - Complete auto-switching profiles for games with fan presets, performance modes, RGB lighting, and GPU settings
- **Game Profile Manager UI** - Full-featured window with profile list, search, editor panel, import/export (JSON)
- **Process Monitoring Service** - Background WMI-based process detection with 2-second polling
- **Manual Update Check** - "Check for Updates" button in update banner for on-demand update checks
- **Profile Statistics** - Launch count, total playtime tracking per game profile

### Changed
- Updated README to reflect game profile feature availability

---

## [1.0.0.5] - 2025-12-10

### Added
- **First-run detection** - WinRing0 driver prompt now only appears once on initial startup
- **Enhanced tray tooltips** - Now displays CPU, GPU, RAM usage with better formatting and emojis
- **Config validation** - Automatically repairs invalid or missing config properties with sensible defaults
- **Better disabled button states** - Improved visual feedback for disabled buttons with grayed background

### Fixed
- **Driver guide path resolution** - Now correctly finds WINRING0_SETUP.md in installed location or falls back to online docs
- **Monitoring tab scrollbar** - Added ScrollViewer wrapper so content scrolls properly when window is resized
- **Button hover animations** - Smoother scale transitions and better hover color feedback
- **Config persistence** - FirstRunCompleted flag saved after showing driver prompt

### Changed
- **Visual polish** - Enhanced button styles with better disabled states and cursor feedback
- **Tray tooltip format** - Multi-line with emoji icons, version display, and clearer system stats
- **Config loading** - More robust error handling with automatic fallback to defaults
- **Logging** - Version string improved to show both app version and assembly version

### Technical Notes
- Added `FirstRunCompleted` boolean to `AppConfig` model
- `ValidateAndRepair()` method ensures all config collections are initialized
- Driver guide checks three paths: bundled docs, dev location, then GitHub URL
- Monitoring interval validated to be between 500-10000ms
- Button disabled state uses `TextMutedBrush` for better contrast

---

## [1.0.0.4] - 2025-12-10

### Added
- **Live CPU temperature badge** on system tray icon updates every 2 seconds with gradient background and accent ring
- **EC write address allowlist** in `WinRing0EcAccess` prevents accidental writes to dangerous registers (VRM, battery charger)
- **Chart gridlines** in thermal and load monitoring with temperature labels for better readability
- **Sub-ViewModel architecture** with `FanControlViewModel`, `DashboardViewModel`, `LightingViewModel`, and `SystemControlViewModel`
- **Async peripheral services** with proper factory pattern (`CorsairDeviceService.CreateAsync`, `LogitechDeviceService.CreateAsync`)
- **Version logging** on application startup for easier debugging
- **Unit test stubs** for hardware access, auto-update, and Corsair/Logitech services

### Fixed
- **Logging service shutdown flush** - writer thread now joins with 2-second timeout to ensure tail logs are written before exit
- **Cleanup toggle mapping** - "Remove legacy installers" checkbox now correctly maps to `CleanupRemoveLegacyInstallers` option
- **Garbled UI glyphs** - replaced mojibake characters (â¬†, âš ) with ASCII "Update" and "!" in MainWindow update banner
- **Auto-update safety** - missing SHA256 hash now returns null with warning instead of crashing, blocks install button with clear messaging
- **TrayIconService disposal** - properly unsubscribes timer event handler to prevent memory leak
- **FanMode backward compatibility** - defaults to `Auto` for existing configurations without mode property
- **Installer version** - updated to 1.0.0.4 (was incorrectly 1.0.0.3)

### Changed
- **Color palette refresh** - darker backgrounds (#05060A), refined accents (#FF005C red, #8C6CFF purple)
- **Typography improvements** - Segoe UI Variable Text with better text rendering
- **Card styling** - unified `SurfaceCard` style with 12px corners, consistent padding, drop shadows
- **Tab design** - modern pill-style tabs with purple accent underlines
- **ComboBox polish** - enhanced dropdown with chevron icon, better hover states
- **Chart backgrounds** - subtle gradients with rounded corners for visual depth
- **HardwareMonitoringService** - added change detection threshold (0.5°C/%) to reduce unnecessary UI updates
- **Test expectations** - updated `AutoUpdateServiceTests` to match new null-return behavior

### Technical Notes
- Sub-ViewModels reduce `MainViewModel` complexity (future: integrate UserControl views in MainWindow tabs)
- EC allowlist includes fan control (0x44-0x4D), keyboard backlight (0xBA-0xBB), performance registers (0xCE-0xCF)
- Tray icon uses 32px `RenderTargetBitmap` with centered FormattedText rendering
- SHA256 extraction regex: `SHA-?256:\s*([a-fA-F0-9]{64})`

### Migration Notes
- **No breaking changes** for existing configurations
- `FanPreset` objects without `Mode` property will default to `Auto`
- Future releases **must** include `SHA256: <hash>` in GitHub release notes for in-app updater to function

---

## [1.0.0.3] - 2025-11-19

### Initial Stable Release
- Fan curve control with custom presets
- CPU undervolting via Intel MSR
- Performance mode switching (Balanced/Performance/Turbo)
- RGB keyboard lighting profiles
- Hardware monitoring with LibreHardwareMonitor integration
- Corsair iCUE device support (stub implementation)
- Logitech G HUB device support (stub implementation)
- HP OMEN Gaming Hub cleanup utility
- System optimization toggles (animations, services)
- GPU mux switching (Hybrid/Discrete/Integrated)
- Auto-update mechanism via GitHub releases
- System tray integration with context menu

---

## Release Links

- **v1.0.0.4**: https://github.com/theantipopau/omencore/releases/tag/v1.0.0.4
- **v1.0.0.3**: https://github.com/theantipopau/omencore/releases/tag/v1.0.0.3

---

## Versioning Policy

**Major.Minor.Patch**
- **Major**: Breaking changes, architecture overhaul, config incompatibility
- **Minor**: New features, service additions, UI redesign
- **Patch**: Bug fixes, polish, performance, security

## Future Roadmap

**v1.1.0 (Planned)**
- Complete MainWindow tab refactor with UserControl integration
- LightingView and SystemControlView implementation
- Per-game profile switching
- Custom EC address configuration
- Macro recording for peripherals

**v1.2.0 (Planned)**
- Full iCUE SDK integration (replace stub)
- Logitech G HUB SDK integration
- Network QoS controls
- On-screen overlay for FPS/temps

---

For detailed upgrade guidance, see `docs/UPDATE_SUMMARY_2025-12-10.md`
