# OmenCore v3.5.0 - Release Changelog and Validation Notes

**Version:** 3.5.0
**Release Date:** 2026-05-05
**Release Status:** Released; post-release hotfix snapshot refreshed 2026-05-07
**Previous Release:** v3.4.1 (2026-04-30)
**Type:** Reliability, hardware-control correctness, resource optimization, and UI polish release

---

## Overview

v3.5.0 was prepared as a larger stabilization pass after v3.4.1. The priority was to turn the remaining fan and RGB reports into explicit, testable fixes instead of continuing to stack narrow hotfixes around symptoms.

Forward-looking scope has been moved to `docs/ROADMAP_v3.6.0.md` so this document stays focused on v3.5.0 implementation and validation status.

This changelog is now a release record rather than a planning draft. Code-level fixes and package artifacts are complete for v3.5.0, while hardware-specific fan/RGB/Linux validation caveats remain explicitly called out below.

OmenCore should remain an independent complete package. External OMEN/Linux projects are research inputs for hardware behavior, sysfs paths, daemon/watchdog patterns, and UX lessons only. OmenCore should not depend on, wrap, vendor, or require those projects at runtime.

The only accepted third-party-style exception remains the bundled PawnIO driver path for Windows EC/MSR access. Longer term, the cleaner alternative would be an OmenCore-owned, signed, minimal hardware-access driver or deeper use of native HP WMI BIOS paths where supported, but that is a larger driver/signing project and should not block 3.5.0.

The v3.5.0 targets were:

- Keep Max fan mode genuinely held at maximum cooling, or fail visibly with precise diagnostics.
- Stop fan/profile UI state from reporting a different mode than the firmware/app backend is actually holding.
- Make Linux performance/fan mode persistence first-class so users do not need external `while true; sleep 30` scripts.
- Fix 4-zone OMEN keyboard lighting on systems where WMI ColorTable writes report success but physical LEDs turn off or stay under OMEN Light Studio ownership.
- Continue reducing idle CPU/RAM use by coalescing timers, avoiding duplicate provider probes, and making optional subsystems lazy.
- Improve the app's premium feel with better first-party visual assets, clearer hardware-state cards, and less noisy status surfaces.

---

## Implemented From v3.4.1 Field Reports

- OMEN MAX 16t-ah000 community tester: Fan diagnostics now wait long enough for slow EC/RPM telemetry to catch up before scoring a fan-speed apply. The guided sequence also uses the verifier's adaptive pass/fail result instead of a hard 15% deviation rule.
- OMEN MAX 16t-ah000 community tester: Max fan preset apply no longer rolls back to Auto/previous preset just because immediate readback is stale. OmenCore keeps Max requested and logs the delayed-verification condition.
- OMEN MAX 16t-ah000 community tester and ZeroMentu: Tray fan quick actions now update visible state only after `FanService.ApplyPreset` succeeds; failed Quiet/Auto/Max applies surface a warning instead of pretending the mode changed.
- 3.5.0 fan cleanup: Canonicalized fan-mode alias/label resolution across tray actions, startup restore, and fan-tab card state so `Max`/`Quiet`/`Auto`/`Custom` synonyms no longer drift between UI surfaces.
- 3.5.0 fan cleanup: Quick popup and tray checkmark highlighting now use the same canonical alias resolver, so external updates like `Silent`/`Default` no longer desync active fan indicators.
- 3.5.0 fan cleanup: Tray fan quick actions now resolve performance aliases (`Performance`/`Turbo`/`Extreme`/`Gaming`/`Boost`) to performance-capable presets instead of silently falling back to Auto.
- 3.5.0 fan control hardening: WMI manual-mode countdown keepalive now throttles repeated same-duty reassert writes and uses countdown extension between writes, reducing random high-RPM hold behavior after brief temperature spikes.
- 3.5.0 model identity fix: Added exact Hades `8A43` OMEN 16-n0xxx capability + keyboard ProductId entries so diagnostics no longer misreport it as low-confidence pattern-inferred `8A44` or unknown keyboard.
- 3.5.0 model identity fix: Added exact `8C76` OMEN 16-wf1xxx Intel capability + keyboard ProductId entries for HP OMEN 16-WF1015ns / 9U8J3EA, replacing the prior low-confidence sibling inference. The exact profile records its observed WMI V1 / `MaxFanLevel=55` fan behavior and 4-zone keyboard so status/diagnostics no longer show inferred model + unknown keyboard on this hardware.
- 3.5.0 fan diagnostics accuracy: WMI fan verification now accepts matching fan-level readback as secondary proof when firmware reports the requested level but RPM telemetry differs from the generic expected curve. This reduces false FAIL/Poor results on V1 / 55-level laptops whose real RPM curve is more nonlinear than the shared verifier baseline.
- 3.5.0 RGB UX fix: Per-key RGB status now distinguishes "hardware supports per-key" from "per-key editor currently unavailable on active backend" to avoid false "this keyboard does not support per-key" messaging on OMEN Max models.
- 3.5.0 fan calibration fix: Resolved calibration wizard launch failures by removing a stale DI/service-locator path that expected `IFanVerificationService` to be container-registered; wizard now receives the active verifier directly from `FanControlViewModel`.
- 3.5.0 fan calibration diagnostics: Calibration status now reports why verification is unavailable (missing runtime service vs inactive backend with active fan backend context), with regression tests to prevent silent calibration-gating failures.
- 3.5.0 fan calibration cleanup: Removed duplicated model-ID normalization logic by centralizing it in `FanCalibrationStorageService.NormalizeModelId(...)`, then reusing it in both `FanControlViewModel` and `FanCalibrationControl` to prevent calibration key drift between wizard writes and dashboard reads.
- 3.5.0 fan keepalive cleanup: Countdown keepalive for non-manual preset modes is now throttled (mode reassert only on interval, countdown extension in-between) to reduce repeated `SetFanMode(...)` churn.
- 3.5.0 fan mode ownership fix: Switching to Performance/Cool policy mode now clears stale manual percentage reapply state so keepalive no longer re-sends old `SetFanLevel(...)` writes after policy-mode activation.
- Genosse_Dark_Lord: Unknown Victus keyboard models now default to backlight-only instead of RGB zone control, while known RGB Victus product IDs still keep their explicit database entries.
- 3.5.0 CPU undervolt Test Apply flow: CPU undervolt now has a Test Apply → Keep flow matching GPU OC. Direct Apply saves offset values to config without enabling startup reapply. Test Apply runs the undervolt for 30 seconds and auto-reverts unless the user presses Keep, at which point values are confirmed and startup reapply is enabled. Added `ApplyOnStartup` to `UndervoltPreferences`; startup restore only runs when this flag is set. Added regression tests for all new preference and startup-gating behavior.
- 3.5.0 RGB ownership visibility: Added `RgbOwnershipSummary`, `HpKeyboardActiveBackend`, `HasRgbConflictWarning`, and `RgbConflictWarningText` properties to `LightingViewModel`. The ownership summary lists every active RGB controller with its backend path (e.g. `HP Keyboard (WMI BIOS)`, `Corsair (2 devices)`). Conflict detection checks for OMEN Light Studio and OMEN Gaming Hub processes and surfaces a warning when they are running alongside OmenCore. Properties refresh after every RGB sync. Regression tests verify consistent conflict/no-conflict state and the no-hardware ownership summary.
- 3.5.0 timer coalescing and log volume: Added `BackgroundTimerRegistry.UpdateDescription` to allow services to update the cadence description string without Unregister+Register overhead. `HardwareMonitoringService.UpdateCadenceTelemetry` now calls `UpdateDescription` for description-only changes and only does Unregister+Register when the interval itself changes. Fan keepalive writes in `WmiFanController` no longer log on every successful write; instead a summary Info log fires every 50 writes, eliminating high-frequency Debug-level keepalive noise. Regression tests verify UpdateDescription mutates description in-place, is a no-op for unknown names, and that Register/Unregister remain correct.
- 3.5.0 System Optimizer preflight and risk tiers: Added `SystemOptimizerService.GeneratePreflightReportAsync(...)` plus `PreflightReport`/`PreflightItem` models so callers can preview optimizer operations before applying changes. The preflight now includes operation ID, category, risk tier (`Low`/`Medium`/`High`), warning text, reboot requirement, and recommended flag. Added high-risk filtering (`includeHighRisk: false`) so UI flows can present a safer default profile and require explicit opt-in for aggressive operations. The System Optimizer view now surfaces the preflight summary (risk counts + warning rollup), highlights High-risk presence, and includes a dedicated Preflight refresh action before apply flows. Regression tests verify high-risk inclusion by default, high-risk exclusion when requested, and risk/warning summary consistency.
- 3.5.0 tuning state split (Requested/Applied/Verified): Added shared `TuningStatusFormatter` and updated tuning status surfaces to report requested values separately from applied readback and verification outcome. CPU undervolt/AMD CO status now includes Requested, Applied, and Verified segments (including external-controller/warning/error paths). NVIDIA GPU OC apply status now emits Requested/Applied/Verified based on requested values vs NVAPI readback, including mismatch and partial-write outcomes. Added regression tests for matched readback, external-controller blocking, and GPU mismatch verification.
- ZeroMentu: OMEN-key interception now rejects the Transcend false-positive signature seen in logs (`VK=0xFF`, scan `0x002B`) so Fn+F2/F3 brightness keys do not toggle OmenCore.
- v3.4.1 RGB field reports: Failed WMI ColorTable RGB applies now restore the previously read color table when verification fails or a visible-color write reads back as all black.
- v3.4.1 cleanup planning: OMEN Gaming Hub, OMEN Light Studio, OmenCap, HP hotkey components, Xbox/Game Pass components, and security components now receive dependency metadata/risk notes before removal decisions.
- 3.5.0 System Optimizer drift explanation: `SystemOptimizerService.GetDriftExplanations(expected, actual)` returns an `OptimizationDriftSummary` with one `OptimizationDriftItem` per setting that has reverted from its expected state. Each item includes an `Explanation` field describing what likely caused the drift (e.g. "SysMain (Superfetch) was re-enabled — Windows Update commonly restores this service") and a `Suggestion` field pointing to the remediation action. `OptimizationDriftSummary.OneLinerSummary` provides a status-bar-friendly string such as "3 optimizations drifted from expected state". Regression tests verify SysMain, power plan, Nagle, HAGS, and combined drift scenarios. No-drift and beneficial-extra-state cases are verified to produce empty summaries.
- 3.5.0 System Optimizer export report: `SystemOptimizerService.ExportOptimizationReportAsync(expectedState?, exportDirectory?)` writes a plain-text diagnostic report to `%LOCALAPPDATA%\OmenCore\Reports\optimizer-report-<timestamp>.txt`. The report includes: current state grid (all six optimizer categories, active/inactive per setting), active count summary, and — when an expected baseline is provided — a full drift analysis section with per-setting explanations and suggestions. Method returns the export path on success or null on failure; all exceptions are caught and logged.
- 3.5.0 RAM cleaner game-aware quiet window: Added `MemoryOptimizerService.IsGameLikelyInForeground()` static P/Invoke heuristic that detects a fullscreen exclusive window covering the primary monitor. `AutoCleanCallback` now checks this before auto-clean; when a game is detected and memory pressure is not critical (load <95%, available >512 MB), the clean is limited to `WorkingSets` only (process working-set trim) to avoid the stutter-inducing standby list purge and page-combine operations. At critical pressure the full safe clean still runs regardless of foreground state. `SetGameAwareQuietWindowEnabled(bool)` allows disabling the feature; `GameAwareQuietWindowEnabled` property exposes the current state. Regression tests verify toggle round-trip and that the static heuristic executes without throwing.
- 3.5.0 tuning conflict pre-apply detection: Added `TuningConflictGuard` static class and `TuningConflictReport`/`TuningConflictEntry`/`TuningConflictKind` models in `OmenCore.Models`. `TuningConflictGuard.Check(TuningConflictKind)` scans running processes and the registry for XTU, ThrottleStop, Intel DTT (`esif_uf`), MSI Afterburner, and NVIDIA App; returns a `TuningConflictReport` with the conflict list, banner text, and `HasHighRiskConflict` flag. `SystemControlViewModel` now runs the conflict scan before `StartCpuUndervoltTestApplyAsync` and `StartGpuOcTestApplyAsync`; when a high-risk conflict is detected a confirmation dialog lists the specific tools and their mitigation suggestions before proceeding. VM properties `TuningConflictBannerText`, `TuningConflictBannerVisible`, `TuningConflictHighRisk`, and `TuningConflictDetails` expose the live scan result for UI binding. Regression tests verify all structural contracts, the flag API, and empty-conflict (no-process) behavior.
- 3.5.0 minimized/tray ultra-low cadence: `HardwareMonitoringService` now supports a third polling tier: tray-only ultra-low cadence (10 s). The `_trayOnlyCadenceInterval` field and `SetTrayOnlyMode(bool)` method allow the UI to signal that the app is minimized to tray with no active fan curve or hold. `GetEffectiveCadenceInterval()` returns this cadence when `_trayOnlyMode && !_uiWindowActive`, falling back to the standard idle cadence (5 s) if a fan curve is active. `App.xaml.cs` wires `SetTrayOnlyMode` from the same `UpdateMonitorCadence()` callback that drives `SetUiWindowActive`, checking `FanService.IsCurveActive` to suppress the tray cadence when fan curve work is running. `MainViewModel.FanService` (internal) and `IsFanCurveActive` (public) are exposed to support this wiring.
- 3.5.0 System Optimizer VM/UI integration for drift + report export: `SystemOptimizerViewModel` now consumes `SystemOptimizerService.GetDriftExplanations(...)` in background verification and exposes `DriftSummaryText` / `DriftWarningRollup` plus `ExportOptimizationReportCommand`. The optimizer header now shows drift explanation rollups when drift is detected and includes a new toolbar action (`Export Report`) that writes diagnostics via `ExportOptimizationReportAsync(...)`. The last exported report path is surfaced in the preflight/status region for quick copy/reference in support tickets.
- 3.5.0 GPU OC AC-power safety gate: Added `GpuOcSafetyGuard.IsIncreaseRequest(...)` (pure model helper) and integrated it into `SystemControlViewModel` before direct GPU OC Apply and GPU OC Test Apply. If the requested OC values are an increase over currently applied values and the system is on battery, the operation is blocked with an AC-power warning message and status text (`GPU OC increase blocked: connect AC power first`). The guard is intentionally increase-only: reductions/reset values remain allowed on battery so users can safely back down unstable settings at any time. Regression tests validate all increase/non-increase combinations for core/memory/power/voltage detection.
- 3.5.0 tuning startup recovery metadata + safe reset: Added persisted recovery metadata to tuning preferences (`PendingTestApply`, `StartupPendingConfirmation`, `LastStartupHadUnconfirmedState`, and last-confirmed profile/time fields for CPU undervolt and GPU OC). New `TuningStartupRecoveryGuard` centralizes policy: if startup detects an interrupted/unconfirmed test session from the previous run, it automatically applies a safe reset (CPU undervolt -> zero offsets + startup reapply off; GPU OC -> 0/0/100/0 + startup reapply off) before any startup restore paths run. `SystemControlViewModel` now marks pending test flags at test start, clears them on revert/confirm, and records confirmed profile metadata on Keep. Regression tests verify safe-reset trigger conditions and reset outputs for both CPU undervolt and GPU OC.
- 3.5.0 tuning conflict visibility: `TuningView` now surfaces a top-level conflict banner bound to `SystemControl.TuningConflictBannerText` / `TuningConflictDetails` / `TuningConflictBannerVisible`, so pre-apply conflict scans (XTU, ThrottleStop, Intel DTT, MSI Afterburner, NVIDIA App) are visible in the tuning UI before users run Apply/Test flows.
- 3.5.0 optimizer report UX completion: Added report action guards and commands so exported optimizer reports are immediately usable from UI. `SystemOptimizerViewModel` now exposes `OpenExportedReportCommand` and `CopyExportedReportPathCommand` with explicit can-execute gating (`CanOpenExportedReport`, `CanCopyExportedReportPath`) via `SystemOptimizerReportActionGuard`. The System Optimizer header now shows `Open` and `Copy Path` actions next to the last export path.
- 3.5.0 startup recovery integration hardening: Added `TuningStartupRecoveryCoordinator` to run startup tuning safety decisions at full `AppConfig` scope (CPU undervolt + GPU OC in one pass), returning a structured outcome (`CpuUndervoltReset`, `GpuOcReset`, `ConfigChanged`). `SystemControlViewModel.RecoverUnconfirmedTuningProfilesAtStartup()` now uses the coordinator instead of duplicating guard checks inline, reducing drift risk between startup logic and test coverage.
- 3.5.0 regression coverage extension: Added `SystemOptimizerReportActionGuardTests` and `TuningStartupRecoveryCoordinatorTests` to validate report command guard behavior (copy/open availability, path normalization) and config-level startup recovery mutation behavior (CPU-only reset, GPU-only/combined reset, unchanged no-pending paths, null GPU section path).
- 3.5.0 startup recovery UX visibility: Added `StartupRecoveryNoticeVisible` + `StartupRecoveryNoticeText` to `SystemControlViewModel`, driven from `TuningStartupRecoveryCoordinator` outcome after startup recovery runs. `TuningView` now surfaces this as a dedicated safety banner so users are explicitly informed when OmenCore auto-reset unconfirmed tuning settings from a previous interrupted session.
- 3.5.0 cleanup: Removed obsolete `GetDriftedOptimizations(...)` from `SystemOptimizerViewModel` after migration to service-level drift summaries (`SystemOptimizerService.GetDriftExplanations(...)`). This reduces duplicate drift logic and eliminates maintenance drift risk between VM and service implementations.
- 3.5.0 fan max fallback fix: Removed remaining hardcoded `SetFanLevel(55,55)` fallback paths in `WmiFanController`. When `SetFanMax(true)` is rejected, fallback now sends the protocol ceiling (`SetFanLevel(100,100)`) and lets BIOS clamp to hardware max (for example fan level `63` on OMEN 16-xd0xxx), preventing false ~5500 RPM caps on custom/performance max requests.
- 3.5.0 OSD telemetry stabilization: `MainViewModel.NormalizeMonitoringSample(...)` now suppresses transient one-sample `0°C` temperature dropouts and clamps large single-step temp jumps (>10°C) to an 8°C per-sample step for CPU/GPU active telemetry. This reduces brief OSD spikes and zero flashes without masking sustained thermal trends.
- 3.5.0 OSD cadence responsiveness: Added `HardwareMonitoringService.SetOverlayRealtimeMode(bool)` and wired `OsdService.VisibilityChanged` through `MainViewModel` so visible in-game OSD forces active monitoring cadence (1s) even when the main window is minimized to tray. When OSD is hidden, cadence returns to normal tray/idle policy.
- 3.5.0 tray cadence hold-awareness: Minimized/tray ultra-low cadence gating now checks active fan hold/keepalive state in addition to active fan curves. This prevents telemetry from dropping to 10s cadence while WMI countdown hold is actively maintaining fan ownership.
- 3.5.0 diagnostics export enhancement: Added explicit monitoring cadence reason/transition snapshots and fan hold-state transition evidence in diagnostics export (`monitoring-cadence-hold.txt`), including current cadence reason flags (`ui active`, `tray-only`, `overlay realtime`, `low-overhead`) and recent hold transition records from fan command history.
- 3.5.0 tray fan-state agreement hardening: Tray fan quick actions are now treated as requested state until confirmed backend state arrives. The tray header shows `requested: <mode>` without flipping active state/checkmarks optimistically, reducing request/confirmed desync during delayed or failed apply flows.
- 3.5.0 fan-state mapping cleanup: Consolidated fan mode alias matching into `FanModeNameResolver` token-aware matching (handles names like "Turbo Max Profile" / "Balanced_Default") and reused it in `GeneralViewModel` profile detection, reducing duplicated string-contains mapping drift across views.
- 3.5.0 quick-profile confirmation path: Tray quick-profile applies now publish confirmed fan state from `FanService` (`ActivePresetName` / `GetCurrentFanMode`) to UI/notifications instead of always echoing the requested preset label.
- 3.5.0 Linux status clarity: `omencore-cli status` now reports explicit access context (root state, detected control method, ec/hp-wmi path presence, hwmon fan-control exposure, profile path visibility) in both JSON and human-readable output, with clearer write-requirement hints.
- 3.5.0 regression coverage expansion: Added tests for overlay cadence override behavior (`HotkeyAndMonitoringTests`) and mixed telemetry normalization transitions (`MainViewModelNormalizeTests`) to ensure transient glitch suppression does not hide normal sustained thermal movement.
- 3.5.0 logging and diagnostics: `TrayIconService` now logs tray submenu styling failures to the logging service instead of Debug.WriteLine only, making styling issues visible in support diagnostics. All tray context menu styling exceptions (Fan, Performance, Display, GPU Power, Keyboard, Advanced submenus) now appear in the debug logs with full error context.
- 3.5.0 fan diagnostics clarity: fan verification now reports a non-zero mismatch when expected RPM is 0 but measured RPM is still high (for example 3100 RPM), removing misleading `0.0% deviation` entries during delayed spin-down cases.
- 3.5.0 fan mismatch detection: added a sanity-warning path for prolonged `requested 0% duty` with sustained high RPM, surfacing likely firmware/external-ownership override scenarios directly in UI warning banners and logs.
- 3.5.0 Linux perf clarity: `omencore-cli perf` now verifies mode readback and emits explicit backend capability warnings when `--power-limit` cannot be applied because direct EC thermal-power writes are unavailable on the active backend (for example profile-only hp-wmi/acpi paths).
- 3.5.0 fan/profile wording clarity: fan-policy linkage copy now explicitly states linked vs independent behavior so profile changes are not interpreted as fan apply failures when fan and performance are decoupled.
- 3.5.0 compact tuning diagnostics panel: `TuningView` now includes a screenshot-friendly quick diagnostics strip (CPU UV state, GPU OC state, conflict presence, startup recovery state, GPU backend availability).
- 3.5.0 hotfix (post-release): Curve-based fan presets now always preserve the currently active thermal policy mode (including `Auto` when a curve payload is present), preventing fan-preset changes from implicitly collapsing GPU power behavior on OMEN MAX systems.
- 3.5.0 hotfix (post-release): `PerformanceModeService` now ignores non-positive model-override TDP values and skips EC power-limit writes when both resolved limits are non-positive, preventing accidental `0W` policy writes that can hard-cap dGPU power.
- 3.5.0 hotfix (post-release): Quick fan-mode actions now no-op cleanly while fan diagnostics mode is active, preventing misleading "Applied Gaming/Quiet" UI/log state when diagnostics intentionally block preset changes.
- 3.5.0 hotfix (post-release): `WmiFanController` preset keepalive now yields while any fan diagnostic session is active, so diagnostic fan-level verification is not overwritten by background preset mode reassertion.
- 3.5.0 hotfix (post-release): GPU power `SetGpuPower` now performs a post-success read-back via `GetGpuPower()` and surfaces a clear UI warning when the BIOS accepts the command (return code 0) but the hardware power-limit bits do not change — this addresses a field-reported case where WMI reliability was degraded (49% success rate) and BIOS silently ignored GPU power commands while logging false success.
- 3.5.0 hotfix (post-release): Fan speed verification (`IsLevelReadbackMatch`) no longer treats over-spinning as a failure when ramping down — a level readback showing higher than requested is normal mechanical inertia on slow-ramping hardware; only a readback below the requested level (wrong direction) is now counted as a mismatch. This resolves false verification failures on the Quiet preset when transitioning from higher fan speeds.
- 3.5.0 hotfix (post-release): On WMI BIOS systems, decoupled performance-mode changes now retain the required WMI thermal-policy hold path when EC power limits are unavailable or resolve to non-positive values. This fixes the field-reported case where GPU boost stayed capped at ~80-90W unless users manually toggled Max fan and then returned to Auto/Quiet.
- 3.5.0 hotfix (post-release): `FanControlViewModel` now serializes preset apply operations so `Max`, `Auto`, and `Quiet` requests cannot overlap while WMI max-fan verification is still running. This removes an apply-race seen in logs where later preset clicks landed before `VerifyMaxAppliedWithRetries()` finished, causing inconsistent Max verification failures and rollback.
- 3.5.0 regression coverage: Added targeted tests for Auto curve preset policy preservation and non-positive TDP override guarding to lock in the above fixes.

---

## Reported Bugs Pulled Into v3.5.0

### Max Fan Mode Does Not Stay at Maximum Cooling
**Severity:** High
**Affects:** Windows WMI BIOS fan-control systems, especially OMEN 16-xd0xxx / product `8BCD`
**Reported:** OMEN MAX 16t-ah000 community tester, 2026-05-01 v3.4.1 logs
**Status:** Reproduced in logs as a persistence/state-ownership risk; fix design required

**Symptom:**
Performance profile shows Max fans in the left sidebar, but the fans do not continue ramping. In the OMEN tab, the same state can still look like default/auto fan mode. Selecting Max in the OMEN tab briefly tries to ramp RPM, then drops back toward normal RPM.

**Current evidence:**
The attached v3.4.1 log detects WMI V1 fan control, product `8BCD`, max fan level `55`, OGH running, and 4-zone lighting support. Max apply can verify at fan level `44`, then subsequent preset/profile changes reset out of Max. The same log also shows Auto briefly reported as `Mode: Performance`, which is a likely contributor to UI sync confusion.

**Regression provenance found (3.2.5 -> 3.3.x path):**
- The 3.3-era startup restore path can synthesize built-in fan presets with a `Mode` but no explicit curve payload.
- `FanService.ApplyPreset` verification accepted Max and curve presets, but could reject non-curve policy presets (for example Performance/Turbo/Quiet variants) based on name-token checks, causing false verification failure and rollback to the previous preset.
- This made some restored/quick-applied presets look like they were accepted, then silently revert.

**Deferred follow-up:**
Additional remediation ideas for this issue are tracked in `docs/ROADMAP_v3.6.0.md`.

**Validation target:**
Max should remain audibly/telemetrically high for at least 2 minutes on affected hardware, and all UI surfaces should show the same active state. If firmware or OGH resets the state, the UI should say so and keep the last requested mode separate from the confirmed hardware mode.

---

### Linux Performance Mode Resets After About 30 Seconds
**Issue:** https://github.com/theantipopau/omencore/issues/114#issuecomment-4356627831
**Reporter:** PizzaCaviar
**Platform:** Arch Linux, KDE Plasma Wayland, OMEN 16 Slim Gaming Laptop, board `8D40`
**Status:** Code-level hold workflow implemented; target-host field validation pending

**Symptom:**
In v3.4.1, the original Wayland GUI rendering issue is reported as resolved, but GUI performance-mode changes do not appear to work reliably. CLI performance/fan settings reset after roughly 30 seconds.

**User workaround / commands to integrate properly:**
The reporter created a root `systemd` service that repeatedly runs OmenCore's CLI:

```bash
/opt/omencore/omencore-cli perf -m performance --power-limit 5
```

every 30 seconds. It is described as brutal but effective.

The implied compatibility feature is not the shell script itself, but the behavior: OmenCore needs a supported daemon-level hold loop that owns the requested profile, verifies whether firmware/kernel state has drifted, and reapplies only when necessary.

**Deferred follow-up:**
Remaining Linux hold/capability hardening scope is tracked in `docs/ROADMAP_v3.6.0.md`.

**Validation target:**
On board `8D40`, performance mode should remain applied for at least 10 minutes through the daemon without an external shell loop. The GUI should communicate when root/write access is missing instead of appearing to apply a setting that cannot hold.

**Implemented in initial 3.5.0 work:**
- Added `[performance] hold_enabled`, `hold_interval_seconds`, and optional `thermal_power_limit` config fields.
- Added daemon-side performance hold verification. The daemon checks the active mode on a bounded cadence and reapplies only when the profile drifts.
- Added optional thermal power-limit reassertion for systems where the power limit cannot be read back reliably.
- Added `omencore-cli perf --mode performance --power-limit 5 --hold --hold-interval 30`.
- Fixed `perf` command multi-option handling so `--mode` no longer prevents `--power-limit` from being applied in the same invocation.
- Added hold state to JSON status output.

---

### Performance Profile and OMEN/Fan Tab State Can Desynchronize
**Severity:** High
**Affects:** Windows quick profile, tray, sidebar, and OMEN/Fan tabs
**Reported:** OMEN MAX 16t-ah000 community tester and ZeroMentu, 2026-05-01 v3.4.1 reports
**Status:** Code-level requested/confirmed state fixes implemented; cross-surface hardware validation pending

**Symptom:**
The left side can show Max fans while the OMEN tab still shows default/auto, or a profile apply can appear successful even when fan policy was intentionally left unchanged.

**Current evidence:**
v3.4.1 logs show `LinkFanToPerformanceMode is off`, then profile code separately applies max cooling for the "Performance profile" path. Later, Auto preset logging shows `Applied preset: Auto (Mode: Performance)` immediately before default restore logs, which suggests preset mapping and transition logging are still too ambiguous.

**Deferred follow-up:**
Additional fan/profile state-alignment follow-ups are tracked in `docs/ROADMAP_v3.6.0.md`.

---

### Fan Diagnostics End Before Slow Hardware Ramps
**Severity:** Medium
**Affects:** Windows fan diagnostics and verification sequence
**Reported:** OMEN MAX 16t-ah000 community tester and ZeroMentu, 2026-05-01 v3.4.1 reports
**Status:** Implemented in initial 3.5.0 work; hardware timing validation still pending

**Symptom:**
Full fan verification could finish RPM checks before the fans had time to ramp. ZeroMentu also reported fan diagnostic errors followed by fans staying at 100% until normal profiles were manually retried.

**Implemented in initial 3.5.0 work:**
- Added a longer post-apply fan response delay before RPM sampling.
- Added retry spacing for slow EC/RPM telemetry.
- Guided diagnostics now consume the verifier's adaptive pass/fail result instead of layering on a second rigid deviation check.
- Failed tray fan applies now keep the prior visible state and surface a warning instead of showing a successful Quiet/Auto/Max transition.

---

### Fn Brightness Keys Trigger OmenCore Window
**Severity:** Medium
**Affects:** OMEN Transcend 14-fb1xxx hotkey detection
**Reported:** ZeroMentu, 2026-05-01 v3.4.1 logs
**Status:** Implemented in initial 3.5.0 work

**Symptom:**
Fn+F2/F3 brightness keys still opened or toggled OmenCore. Fn+F12 did not trigger OmenCore, and Fn+P profile switching remained a requested follow-up rather than a confirmed implemented hotkey.

**Implemented in initial 3.5.0 work:**
- OMEN-key detection now rejects the false-positive signature seen in ZeroMentu's logs (`VK=0xFF`, scan `0x002B`).
- Regression coverage was added for the rejected brightness-key signature.

---

### Victus Backlight-Only Keyboards Shown As RGB-Capable
**Severity:** Low
**Affects:** Victus notebooks with keyboard lights that are only on/off
**Reported:** Genosse_Dark_Lord, 2026-05-01 v3.4.1 screenshot/report
**Status:** Implemented in initial 3.5.0 work

**Symptom:**
Keyboard Lighting Diagnostics detected an HP Omen integrated keyboard on Victus hardware even when the physical keyboard only supported backlight on/off, not RGB zones.

**Implemented in initial 3.5.0 work:**
- Unknown Victus keyboard models now default to backlight-only capabilities.
- Known RGB Victus product IDs remain explicitly enabled in the model capability database.
- Regression coverage was added for the backlight-only Victus default.

---

### 4-Zone RGB Turns Off or Only Works While OMEN Light Studio Is Running
**Severity:** High
**Affects:** Windows OMEN 16-xd0xxx / product `8BCD` 4-zone ColorTable keyboards
**Reported:** v3.4.1 RGB field report on OMEN 16-xd0xxx / product `8BCD`
**Status:** Physical output still not validated

**Symptom:**
When OMEN Light Studio is running, the keyboard lights blink and then continue its animation. If Light Studio is ended from Task Manager and OmenCore tries to control RGB, the keyboard turns off and does not restore to normal.

**Current evidence:**
The attached log shows the WMI BIOS ColorTable backend initializes, writes `#E6002E` to all zones, turns the backlight on, then fails readback verification and falls back to per-zone WMI. The fallback reports success, but the reported physical behavior is lights off/no visible color.

**Deferred follow-up:**
Remaining backend-matrix and ownership-hardening follow-ups are tracked in `docs/ROADMAP_v3.6.0.md`.

**Validation target:**
On affected 4-zone systems, selecting a static color should visibly update the keyboard with OMEN Light Studio closed. Failed applies should not leave the keyboard dark without a restore option.

**Implemented in initial 3.5.0 work:**
- WMI ColorTable writes now snapshot the previous color table before applying a new static color.
- If `SetColorTable` fails, verification fails, or a visible-color write reads back as all black, OmenCore restores the previous color table and logs the restore result.
- The restore path is code-level validated; physical validation is still required on affected 4-zone hardware.

### OMEN MAX 16 ak0003nr: HX 375 Undervolt Not Applying + Temporary High Fan RPM at Low Temp
**Severity:** Medium
**Affects:** OMEN MAX 16 `ak0003nr` (AMD Ryzen AI 9 HX 375)
**Reported:** Community report, 2026-05-02
**Status:** Reproduced by user symptom report; logs pending

**Symptom:**
- CPU undervolt/Curve Optimizer changes do not appear to apply on HX 375 hardware, even though comparable controls are visible in OMEN Gaming Hub.
- Fans were observed around ~4000 RPM at low temperature, then later dropped without manual intervention.

**Initial code-level fix:**
- Removed the hard "Ryzen AI 9 not yet supported" software block in OmenCore's AMD Curve Optimizer path so HX 375 can attempt provider-level SMU apply/verify instead of being rejected preemptively.
- Ryzen AI 9 is now treated as experimental in status messaging rather than unsupported, while keeping guardrails/clamps in place.
- Added a focused diagnostics export snapshot (`tuning-fan-focus.txt`) that captures CPU/undervolt provider status, probe readback, and WMI fan ownership/reset markers for faster HX 375 triage.

**Next validation target:**
- Collect fresh logs from affected hardware with a single controlled test sequence: baseline status -> apply conservative CO offset -> readback/probe -> monitor fan RPM and thermal telemetry for at least 2 minutes.
- Confirm whether high idle RPM correlates with Max hold ownership, firmware/OGH overwrite/recovery, or stale telemetry during a transition window.

---

## Deferred Roadmap Scope

All deferred and forward-looking items previously tracked here (resource optimization targets, additional tuning hardening targets, Linux research/takeaways, UI/visual polish goals, and future optimizer/cleanup expansion) have been moved to:

- `docs/ROADMAP_v3.6.0.md`

---

## Proposed 3.5.0 Validation Checklist

- [ ] Max fan mode holds for 2+ minutes on OMEN 16-xd0xxx / product `8BCD`.
- [x] Max fan mode reports a clear external reset if OGH/firmware reverts it.
- [x] Auto/Default transitions still avoid `SetFanLevel(0, 0)` on V1 WMI systems.
- [ ] Sidebar, OMEN tab, fan tab, tray, and persisted config agree after Max, Auto, Balanced, Performance, and startup restore.
- [x] Fan subsystem keeps a bounded command history suitable for diagnostics export.
- [ ] Linux board `8D40` performance mode can be held by OmenCore daemon without external shell scripts.
- [ ] Linux GUI reports missing root/write access clearly when performance/fan writes cannot persist.
- [ ] 4-zone RGB static color visibly applies on product `8BCD` with OMEN Light Studio closed.
- [x] Failed WMI ColorTable RGB applies restore the prior lighting state at the code level; physical hardware validation remains pending.
- [x] Optional RGB/peripheral providers do not probe twice during startup.
- [x] Bloatware Manager flags OMEN Gaming Hub, OMEN Light Studio, and OmenCap as conflict-sensitive before removal decisions.
- [x] Bloatware Manager dry-run export includes package/uninstall/startup/task targets, risk reasons, dependency notes, backup status, and expected restore paths.
- [x] HP/OMEN removal preview includes an Independence Readiness checklist warning before conflict-sensitive removals.
- [ ] Idle minimized/tray-only CPU and RAM use are measured before and after optimization.
- [x] Windows core test project passes after current 3.5.0 changes (`dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj --no-build`).
- [x] Full solution test pass refreshed (`dotnet test OmenCore.sln --no-restore`) on 2026-05-05 (`EXIT_CODE=0`).
- [ ] Linux package build and daemon validation pass.

---

## 3.5.0 Release Gate (Current Snapshot)

This section records the state used for the v3.5.0 release and the remaining evidence needed before claiming broad hardware confidence. Status labels are intentionally binary.

### Gate A - Code and Regression Safety

- Status: PASS
- Criteria:
	- New fan, Linux perf-hold, optimizer, tuning, and RGB changes compile in the current workspace.
	- Core Windows test project is green with current 3.5.0 deltas.
	- Full solution test run is green for current workspace state.
- Evidence:
	- `dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj --no-build` exit code 0 in latest runs.
	- `dotnet test OmenCore.sln --no-restore --verbosity quiet` on 2026-05-05 with `EXIT_CODE=0`.

### Gate B - Requested vs Confirmed State Surfaces

- Status: PASS (code-level), FIELD-VERIFY pending
- Criteria:
	- Fan/perf state paths no longer optimistically report success on failed apply paths.
	- Requested vs confirmed behavior is represented in tray/view-model flows and diagnostics output.
- Remaining field verification:
	- Confirm no cross-surface drift on affected real hardware across Max/Auto/Balanced/Performance/startup restore transitions.

### Gate C - Linux Hold and Capability Clarity

- Status: PASS (code-level), FIELD-VERIFY pending
- Criteria:
	- CLI supports daemon hold workflow and clear backend warnings for unsupported thermal-power writes.
	- Daemon hold loop reasserts only when drift is detected and logs unsupported write paths explicitly.
- Remaining field verification:
	- Validate board `8D40` long-hold behavior in target distro/kernel environments.
	- Validate Linux package/service flow end-to-end on target systems.

### Gate D - Physical Hardware Confidence

- Status: PENDING FIELD EVIDENCE
- Checks still needed for broad targeted-hardware confidence:
	- Max fan hold duration validation on `8BCD` class systems.
	- Physical 4-zone RGB apply/restore validation on affected OMEN hardware with OMEN Light Studio closed.
	- Tray/minimized cadence impact measurement (before/after CPU and RAM telemetry) on real systems.
	- Linux board `8D40` long-hold stability evidence in target distro/kernel environments.

Evidence workflow and reusable template:
- Use `docs/HARDWARE_VALIDATION_EVIDENCE_v3.5.0.md` for every physical run.
- Keep one completed evidence section per device/OS combination and attach command outputs verbatim.

### Gate E - Next Maintenance Signoff

- Status: PENDING FIELD EVIDENCE (depends on Gate D)
- Required before claiming "release-ready for all targeted hardware":
	- Refresh full-solution test run for any maintenance branch cut.
	- Attach physical validation evidence for Gate D items.
	- Mark all checklist blockers above as PASS.

Definition of done for release note wording:
- "All scoped 3.5.0 code fixes implemented" can be claimed for the released packages.
- "Release-ready for all targeted hardware" cannot be claimed until Gate D evidence is complete.

### Current Verification Evidence Snapshot (2026-05-05)

Commands executed in this workspace for release-gate evidence:

```powershell
dotnet test OmenCore.sln --no-restore --verbosity quiet
dotnet test OmenCore.sln --no-restore --verbosity quiet; Write-Output "EXIT_CODE=$LASTEXITCODE"
```

Observed outcome:
- Full solution test command returned `EXIT_CODE=0`.
- No failing test output was produced in the final run.

Scope caveats:
- This confirms current workspace test health, not physical device behavior.
- Linux package/service runtime validation is still pending target-host evidence.
- Hardware-specific fan/RGB/cadence checks remain tracked under Gate D.

---

## Source Inputs

- OMEN MAX 16t-ah000 community tester v3.4.1 fan-control report, 2026-05-01.
- ZeroMentu v3.4.1 OMEN Transcend 14-fb1xxx hotkey and fan-diagnostics reports, 2026-05-01.
- Genosse_Dark_Lord v3.4.1 Victus keyboard-lighting capability report, 2026-05-01.
- GitHub issue #114 and all current comments, including the 2026-04-30 workaround: https://github.com/theantipopau/omencore/issues/114
- `C:\Users\matthew.hurley\Downloads\OmenCore_20260501_014520.log`
- `C:\Users\matthew.hurley\Downloads\OmenCore_20260501_080332.log`
- `C:\Users\matthew.hurley\Downloads\OmenCore_20260501_080954.log`
- `C:\Users\matthew.hurley\Downloads\image.png`
- `C:\Users\matthew.hurley\Downloads\OmenCore_20260430_195503.log`
- `docs\CHANGELOG_v3.4.1.md`
- `docs\FAN_MAX_DIAGNOSTICS.md`
- `docs\HARDWARE_VALIDATION_EVIDENCE_v3.5.0.md`
- Fan implementation review: `FanService.cs`, `WmiFanController.cs`, `PerformanceModeService.cs`
- RGB implementation review: `WmiBiosBackend.cs`, `KeyboardLightingServiceV2.cs`, `HpWmiBios.cs`
- Linux implementation review: `OmenCoreDaemon.cs`, `PerformanceCommand.cs`
- Tuning implementation review: `UndervoltService.cs`, `CpuUndervoltProvider.cs`, `AmdUndervoltProvider.cs`, `NvapiService.cs`, `MsiAfterburnerService.cs`, `SystemControlViewModel.cs`

---

## Release Artifact SHA256 (v3.5.0)

These hashes correspond to the refreshed 2026-05-07 v3.5.0 artifact snapshot after all post-release hotfixes listed above.

- `OmenCoreSetup-3.5.0.exe`  
	`262A2FC7E4E48C63A862284D0620633B396501A84391210F0EBCB097CB4817A1`
- `OmenCore-3.5.0-win-x64.zip`  
	`6A1E801831D8AC69151678463152E1325AE75FB462E40127CD9FA5191F90E016`
- `OmenCore-3.5.0-linux-x64.zip`  
	`E7C064122F8227FAF7177D18A918A82BF2503DA61F55182BC40A1A4CC258E7D2`

Packaging notes:
- Windows installer + portable package built successfully via `build-installer.ps1`.
- Linux package built successfully via `build-linux-package.ps1 -SkipBinaryVersionCheck` on this Windows host (no Linux host/WSL available for direct binary execution verification in this environment).
