# OmenCore v3.2.5 Roadmap - Premium Stability and Feature Upgrade

**Draft Date:** 2026-03-25
**Target Release:** v3.2.5
**Release Type:** Minor+ (quality, platform hardening, UX premiumization)
**Scope:** Windows app, Linux CLI/Avalonia, core hardware and RGB subsystems

---

## 1. Why 3.2.5

v3.2.5 should be a confidence release and a quality leap:

- Fix user-facing regressions from 3.2.1 (including Linux packaging/version confusion and key-trigger behavior).
- Improve hardware capability detection and graceful degradation on unsupported firmware paths.
- Elevate UI and RGB from "functional" to "premium" with cleaner interactions, stronger visual polish, and reliable persistence.
- Tighten release engineering so binaries, versions, manifests, and telemetry diagnostics are always aligned.

---

## 2. Audit Inputs

This roadmap is based on:

- Codebase review across hotkeys/OMEN key interception, Linux packaging, Linux diagnostics, monitoring, UI and RGB service layers.
- Community reports and issue triage:
  - GitHub #96
  - GitHub #97
  - User report: "F11 activates OmenCore" on 3.2.1
  - Discord 2026-03-26 (Serg): CPU temp random drop to 40°C on OMEN 17-ck1xxx
  - Discord 2026-03-26 (Serg): Performance mode button order feedback (Quiet→Balanced→Perform)
  - Discord 2026-03-26 (Serg): Quick Access OMEN fan curve mode request

---

## 3. Bug Burn-Down (Must-Have in 3.2.5)

### 3.1 GitHub #96 - Linux package/version inconsistency

**Observed risk:** Linux CLI version output can diverge from release artifact version.

**Evidence from code:** `src/OmenCore.Linux/Program.cs` currently hardcodes `Version = "3.1.0"`.

**Roadmap actions:**

1. Move Linux versioning to a single source of truth (shared build metadata, not hardcoded const strings).
2. Add packaging verification step that confirms:
   - archive name version,
   - CLI reported version,
   - GUI reported version,
   are identical before publish.
3. Add CI guard that fails release when version mismatch is detected.
4. Emit release-time manifest (`version.json`) into artifacts for support diagnostics.

**Acceptance criteria:**

- `omencore-cli --version` and Linux GUI About/version endpoint both report 3.2.5 for 3.2.5 artifacts.
- Release pipeline fails if any packaged binary reports a non-target version.
- Issue reproduction path from #96 no longer reproduces on clean Ubuntu extraction.

### 3.2 GitHub #97 - Victus Linux fan control/GUI/GPU telemetry reliability

**Observed risk:** On some Victus boards, hp-wmi exposes partial interfaces (missing fan output/target or thermal controls), causing confusing behavior and feature expectations mismatch.

**Roadmap actions:**

1. Expand Linux capability classification:
   - full control,
   - profile-only,
   - telemetry-only,
   - unsupported-control.
2. Tie CLI/Avalonia UI affordances directly to classified capabilities (disable/hide unsupported controls with clear reason text).
3. Strengthen diagnostics output and support bundle for board-specific triage.
4. Add additional GPU telemetry fallback chain for Linux (where available by driver/hwmon path).
5. Improve GUI startup fault reporting path (startup exception to user-readable diagnostic screen/log summary).

**Acceptance criteria:**

- On partial hp-wmi boards, unsupported controls are not shown as actionable.
- `diagnose` clearly states why fan control is unavailable (firmware path, kernel exposure, permissions, or board profile).
- GUI launch failures produce actionable message/log instead of silent crash.

### 3.3 F11 unexpectedly activates OmenCore (3.2.1 user report)

**Observed risk:** Function-key usage in games/apps can trigger OMEN-key actions via low-level interception heuristics.

**Roadmap actions:**

1. Add stricter OMEN key matching mode:
   - default conservative filtering,
   - model-specific allowlist profile,
   - optional advanced mode for discovery.
2. Add explicit "never intercept" key exclusions (including F11 path) and structured telemetry for false-positive reports.
3. Add setting: "OMEN key interception strict mode" enabled by default.
4. Add integration test coverage around key routing and false-positive suppression.

**Acceptance criteria:**

- Pressing F11 never opens/toggles OmenCore in strict mode.
- OMEN key continues to work on supported models.
- Key-interception logs can identify model and key data for future tuning without spamming logs.

### 3.4 3.2.1 Regression Intake Pack (New user reports)

**Priority target:** close all P0 and P1 items below as part of 3.2.5 stabilization gate.

#### 3.4.1 P0 - Updater downloads "invalid executable" while manual update works

**User symptom**

- In-app update to 3.2.1 fails with invalid/corrupt executable message.
- Manual download/install from GitHub works.

**Evidence anchor**

- `src/OmenCoreApp/Services/AutoUpdateService.cs` validates downloaded headers and emits "Downloaded file is not a valid Windows executable" during install path.

**Likely root-cause buckets**

1. Asset selection mismatch (installer vs portable asset chosen unexpectedly).
2. Non-binary response body from download endpoint (rate limit/error page/redirect edge case).
3. Downloaded filename/asset metadata mismatch with install path assumptions.

**Likely files to edit**

- `src/OmenCoreApp/Services/AutoUpdateService.cs`
- `src/OmenCoreApp/ViewModels/MainViewModel.cs` (update banner/UX text and retry flow)
- `build-installer.ps1` (release artifact naming consistency)

**Implementation mechanics**

1. Enforce installer-only asset contract for installed builds and reject non-`.exe` candidate assets before download.
2. Add response content-type and redirect-chain diagnostics to updater logs.
3. Add a post-download SHA256 cross-check against release metadata before install button is enabled.
4. Add clear user-facing fallback when binary validation fails (single-click open release page + cached log snippet).

**Done boxes**

- [x] Installed-mode updater only accepts installer `.exe` assets.
- [x] Updater logs include URL, final redirect target, content-type, and header signature.
- [x] SHA256 mismatch or missing hash blocks install and shows actionable reason.
- [ ] Repro flow from current user report no longer fails on 3 consecutive test runs.

#### 3.4.2 P1 - Bloatware Manager appears to remove nothing

**User symptom**

- Remove actions complete but apps/startup/task state appears unchanged.

**Evidence anchor**

- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs` currently treats many Win32 uninstaller exits as success without post-state verification.

**Likely root-cause buckets**

1. Success reported without post-uninstall verification.
2. Missing elevation context during runtime despite UI flow proceeding.
3. Silent failures in PowerShell/AppX/schtasks branches not surfaced to user state panel.

**Likely files to edit**

- `src/OmenCoreApp/Services/BloatwareManager/BloatwareManagerService.cs`
- `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`

**Implementation mechanics**

1. Add per-type post-action verification (AppX present check, registry/startup check, scheduled task enabled state).
2. Require explicit admin preflight gate before any bulk remove operation starts.
3. Replace optimistic "success" status with "requested/verified failed/verified success" states.
4. Persist a removal result record per item for support export.

**Done boxes**

- [x] No removal path reports success without post-state verification.
- [x] Bulk remove clearly reports per-item outcomes and failure reasons. *(v3.2.5 — RemovalStatus enum on BloatwareApp; detailed N-succeeded/M-failed summary with failed names in status bar)*
- [x] Non-admin session blocks removal with explicit reason before operation start. *(early-return guard added to RemoveSelectedAsync / RemoveAllLowRiskAsync)*
- [x] User can export bloatware action result log from UI. *(v3.2.5 — Export Log button; ExportRemovalLog in service writes timestamped txt with pass/fail per item and failure reasons)*

#### 3.4.3 P0 - Quiet performance mode overrides manual fan mode

**User symptom**

- Selecting Quiet profile changes/locks fan behavior unexpectedly and overrides prior fan-mode choice.

**Evidence anchor**

- `src/OmenCoreApp/Services/PerformanceModeService.cs` applies fan policy as part of performance-mode apply, which can supersede fan preset intent.

**Likely root-cause buckets**

1. Performance mode and fan mode are coupled in the same apply transaction.
2. No policy layer deciding ownership priority when both controls are changed close together.
3. Tray/quick actions can trigger competing writes without explicit arbitration.

**Likely files to edit**

- `src/OmenCoreApp/Services/PerformanceModeService.cs`
- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`

**Implementation mechanics**

1. Decouple fan policy from performance profile by default (opt-in linkage setting).
2. Introduce control-ownership state: performance policy vs manual fan preset.
3. Add conflict-resolution policy and user-visible indicator when one control supersedes another.
4. Ensure tray and hotkey paths respect the same arbitration logic.

**Done boxes**

- [x] Switching performance mode no longer silently overwrites manual fan mode. *(v3.2.5 — `LinkFanToPerformanceMode` flag, default off)*
- [x] If linked mode is enabled, UI explicitly indicates linked behavior. *(new linked/decoupled indicator added across sidebar, dashboard card, quick popup, and tray tooltip/menu)*
- [x] Tray, dashboard, and quick popup always show the same effective fan/perf state. *(all now bound/synced from `MainViewModel.IsFanPerformanceLinked` with App→Tray propagation and runtime refresh on settings toggle)*

#### 3.4.4 P0 - Fan behavior/diagnostics regressions (high fan lock, boost/diagnostic ineffective)

**User symptoms**

- Fans can remain high after load until restart.
- Fan boost/diagnostic commands appear to have no effect on some systems.
- Diagnostic UX is confusing about what is actually being tested and restored.

**Evidence anchors**

- `src/OmenCoreApp/Services/FanService.cs` contains thermal-protection, diagnostic mode, and curve engine coordination.
- `src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs` performs apply/verify and restore flows.
- `src/OmenCoreApp/Services/FanVerificationService.cs` can report command success even when hardware response is weak/inconsistent by model.

**Likely root-cause buckets**

1. Thermal-protection and diagnostic/manual flows interacting without a single state machine.
2. Verification thresholds and backend assumptions not model-calibrated.
3. Incomplete restore path on certain failure sequences.

**Likely files to edit**

- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/Services/FanVerificationService.cs`
- `src/OmenCoreApp/ViewModels/FanDiagnosticsViewModel.cs`
- `src/OmenCoreApp/Views/FanDiagnosticsView.xaml`

**Implementation mechanics**

1. Formalize fan-control state machine (auto, preset, thermal override, diagnostic, recovering).
2. Add deterministic timeout-based escape from sticky high-fan states and explicit auto-restore path.
3. Calibrate verification tolerance/profile by detected model family.
4. Improve diagnostics UX text to show requested vs applied vs verified results and backend used.

**Done boxes**

- [x] Fan state machine prevents persistent high-fan lock after load spikes.
- [x] Diagnostic run always restores expected pre-test state (preset or BIOS auto). *(already implemented in FanDiagnosticsViewModel finally blocks since v2.7.1)*
- [x] "No effect" paths include model/backend guidance in UI and logs.
- [x] Guided diagnostics output includes backend, deviation, and pass/fail confidence.

#### 3.4.5 P1 - Fan Diagnostics UI clipping and unclear copy

**User symptom**

- Text/buttons clip in some layouts; "Guided Diagnostic" wording is confusing.

**Evidence anchor**

- `src/OmenCoreApp/Views/FanDiagnosticsView.xaml` uses fixed-width/compact horizontal layout blocks that can clip at smaller widths or DPI scaling.

**Likely files to edit**

- `src/OmenCoreApp/Views/FanDiagnosticsView.xaml`
- `src/OmenCoreApp/Styles/ModernStyles.xaml`

**Implementation mechanics**

1. Replace rigid horizontal cluster with responsive layout breakpoints/wrapping.
2. Update wording to task-first labels (for example: "Run Fan Verification Sequence").
3. Add explicit result legend for pass thresholds and data source abbreviations.

**Done boxes**

- [x] No clipping at 100%, 125%, and 150% Windows DPI.
- [x] Diagnostic labels are self-explanatory without external docs.
- [x] Result panel remains readable on minimum supported window width. *(v3.2.5 — wrapped in ScrollViewer, removed MaxWidth=500 from history TextBlock)*

#### 3.4.6 P1 - Optimizer tab toggles appear unresponsive

**User symptom**

- Toggle actions in optimizer tab appear to not apply or appear to flip back unexpectedly.

**Evidence anchor**

- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs` refreshes full state after every toggle and inverts item state optimistically before refresh, which can look like no-op under timing/permission failures.

**Likely root-cause buckets**

1. Toggle UX race between optimistic switch animation and authoritative refresh.
2. Missing per-toggle operation lock/spinner and clear failure feedback.
3. Partial admin/permission failures collapsing into generic status text.

**Likely files to edit**

- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs`
- `src/OmenCoreApp/Views/SystemOptimizerView.xaml`
- `src/OmenCoreApp/Services/SystemOptimizer/SystemOptimizerService.cs`

**Implementation mechanics**

1. Move toggles to command-first transaction state (pending, applied, failed) instead of direct boolean inversion.
2. Add per-item loading indicator and disable only the active item during apply.
3. Surface precise failure reasons inline (permission, registry denied, unsupported key).

**Done boxes**

- [x] Toggle feedback shows pending/success/failure deterministically.
- [x] Failed toggle remains visually failed with reason instead of snapping silently.
- [ ] Optimizer actions are test-covered for non-admin and admin modes.

#### 3.4.7 P0 - Victus 15-fa1xxx (Product 8BB1) classification uncertainty

**User symptom**

- Device appears as unknown/inconsistent capability profile despite Product ID being detected.

**Evidence anchors**

- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs` maps Product ID `8BB1` as OMEN 17.
- `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` does not provide a matching Victus capability profile for `8BB1`.

**Likely root-cause buckets**

1. Product ID collision across lines or stale assumption in model database.
2. Insufficient tie-break logic between product ID and board/model-name signals.
3. Keyboard capability DB and hardware capability DB out of sync.

**Likely files to edit**

- `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`

**Implementation mechanics**

1. Add model-resolution precedence rules: board/model name signature before raw product ID when conflicts exist.
2. Add explicit profile entry for reported Victus 15-fa1xxx variant and mark confidence level.
3. Align keyboard and core capability records under shared model identity key.

**Done boxes**

- [x] 8BB1 conflict is resolved to the correct family/model at runtime. *(v3.2.5 — model-name disambiguation in both DBs)*
- [x] Capability, fan, and RGB gating all use the same resolved identity. *(Victus 15-fa1xxx gets correct BacklightOnly + Victus capability profile)*
- [ ] Diagnostic export includes identity resolution trace for support.

#### 3.4.8 Regression test gate for this bug pack

- [x] Add a "3.2.1 Regression Pack" checklist to pre-release sign-off. *(see `docs/REGRESSION_PACK_v3.2.5.md`)*
- [ ] Run 30-minute stress scenario (mode switches + diagnostics + update check) without fan lock or stale state.
- [ ] Validate updater path with both installer and portable installations.
- [ ] Capture and archive logs/screenshots from Victus test machine in release QA bundle.

---

## 4. Product Upgrade Tracks for 3.2.5

## 4.1 Core Functionality and Hardware Control

1. Capability-first control model across Windows/Linux.
2. Clear split between "applied", "requested", and "unsupported" control states.
3. Improve fan curve safety rails:
   - slope/rate-of-change guard,
   - firmware-safe 0% handling consistency,
   - profile rollback on invalid command feedback.
4. Better GPU power feature gating by board/model capability metadata.
5. Automatic safe fallback to profile mode when direct control paths fail repeatedly.
6. Per-feature health state in dashboard (Fan, Thermal, GPU, RGB, Keyboard).
7. Improved board profile update mechanism for quick support additions.

### 4.1.1 Core Functionality and Hardware Control - Detailed Expansion

#### A. Capability-first control model

**Intent**

Unify the way OmenCore decides whether a feature is supported, partially supported, degraded, or unavailable. Today, some of that logic is split across hardware services, view models, tray behavior, and Linux diagnostics. The roadmap goal is to make capability state explicit and reusable.

**Likely files to edit**

- `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
- `src/OmenCoreApp/Views/DashboardView.xaml`
- `src/OmenCoreApp/App.xaml.cs`
- `src/OmenCoreApp/Utils/TrayIconService.cs`
- `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`

**How it would happen**

1. Introduce a capability model shared across feature surfaces.
2. Compute that model as part of hardware initialization and refresh when device state changes.
3. Bind all UI entry points to that model instead of reimplementing support logic ad hoc.
4. Ensure tray popup, dashboard, settings, and system-control views consume the same support flags and user-facing explanation strings.

**Validation**

- Confirm a capability state change updates dashboard, tray, and settings consistently.
- Confirm unsupported features do not remain actionable in side paths such as tray and hotkeys.

#### B. Requested vs applied vs unsupported state model

**Intent**

Avoid situations where the UI implies something was applied when it was only requested, silently ignored, or unsupported.

**Likely files to edit**

- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
- `src/OmenCoreApp/Views/DashboardView.xaml`
- `src/OmenCoreApp/Hardware/WmiFanController.cs`
- `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs`
- `src/OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs` if present, otherwise related control view models

**How it would happen**

1. Represent control transitions explicitly:
   - requested,
   - applying,
   - applied and verified,
   - unsupported,
   - failed with reason.
2. Update controls and notifications to reflect verified state rather than assumed success.
3. Where readback is possible, use it to confirm final state.

**Validation**

- Force failure and unsupported scenarios and verify the UI never overstates success.
- Confirm notifications and tray state match verified result.

#### C. Fan curve safety rails

**Intent**

Make custom fan curves safer, smoother, and more predictable under firmware edge cases.

**Likely files to edit**

- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/Hardware/WmiFanController.cs`
- `config/default_config.json`
- Potential Linux fan-curve files if parity work is included

**How it would happen**

1. Add curve validation rules:
   - no invalid temperature ordering,
   - guard against abrupt unsafe deltas,
   - reject impossible or firmware-risky points.
2. Add rate-of-change controls to avoid rapid oscillation.
3. Preserve safe behavior around 0% and BIOS auto restore semantics.
4. Add rollback path when a curve apply fails validation or writeback.

**Validation**

- Test steep curves, flat curves, and invalid imports.
- Confirm no unsafe 0% path reappears.

#### D. Better GPU power capability gating

**Intent**

Prevent GPU power controls from appearing on hardware that cannot actually honor them.

**Likely files to edit**

- `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/Utils/TrayIconService.cs`
- `src/OmenCoreApp/App.xaml.cs`

**How it would happen**

1. Expand model capability metadata.
2. Prefer metadata-driven gating before probing unsupported firmware.
3. Feed the same capability status into tray, settings, and system-control view.

**Validation**

- Verify unsupported Victus models never expose confusing GPU power actions.
- Verify supported hardware still shows and applies controls correctly.

#### E. Safe fallback to profile mode

**Intent**

When direct writes are unstable or repeatedly failing, the app should degrade gracefully into safer profile-based behavior.

**Likely files to edit**

- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/Hardware/WmiFanController.cs`
- `src/OmenCore.HardwareWorker/Program.cs`
- `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs`

**How it would happen**

1. Track repeated control failures over a bounded window.
2. Escalate from retry to cooldown to safe profile fallback.
3. Surface fallback status to user instead of silently changing behavior.

**Validation**

- Simulate repeated apply failures and confirm fallback path engages predictably.

#### F. Per-feature health state

**Intent**

Expose health independently for fan control, monitoring, GPU control, RGB, and keyboard control instead of treating overall app state as a single boolean.

**Likely files to edit**

- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
- `src/OmenCoreApp/Views/DashboardView.xaml`
- `src/OmenCoreApp/Utils/TrayIconService.cs`
- `src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs`
- `src/OmenCore.Avalonia/Views/DashboardView.axaml`

**How it would happen**

1. Represent each subsystem with a health enum plus explanation text.
2. Show status badges and drill-down guidance in the dashboard.
3. Surface summary health in tray and quick popup.

**Validation**

- Confirm mixed health states are visible and understandable.

#### G. Faster board profile updates

**Intent**

Make it easier to add support for newly reported models without invasive logic changes.

**Likely files to edit**

- `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs`
- Related support tables on Linux if maintained separately

**How it would happen**

1. Normalize model profile structure.
2. Add clearer metadata fields for support notes and known limitations.
3. Document how new models should be added and validated.

**Validation**

- Add at least one new or revised board entry and confirm support surfaces update predictably.

## 4.2 Systems, Reliability, and Performance

1. Unified versioning strategy for all apps (Windows app, worker, Linux CLI, Avalonia GUI).
2. Artifact integrity manifest generation and verification in CI.
3. Better startup diagnostics and self-test summary (driver, WMI, hp-wmi/hwmon, telemetry stream).
4. Worker/client resiliency improvements:
   - bounded retry with jitter,
   - clearer stale/degraded transitions,
   - reduced hot-path allocations.
5. Structured crash reports with safe redaction and one-click export.
6. Hardened config migration between minor versions (schema version + migration tests).
7. Log hygiene pass:
   - reduce repeated warnings,
   - clearer severity levels,
   - correlation IDs for startup/session.
8. Add synthetic smoke tests for tray startup, hotkey routing, and worker reconnect behavior.

### 4.2.1 Systems, Reliability, and Performance - Detailed Expansion

#### A. Unified versioning strategy

**Intent**

Eliminate version drift between binaries, installers, archives, and UI version displays.

**Likely files to edit**

- `VERSION.txt`
- `build-installer.ps1`
- `build-linux-package.ps1`
- `src/OmenCore.Linux/Program.cs`
- Relevant project files and about/settings version surfaces across Windows and Linux

**How it would happen**

1. Decide one source of truth.
2. Flow that version into assembly metadata and packaging.
3. Add build-time verification and fail fast on mismatch.

**Validation**

- Confirm all shipped artifacts report the same version.

#### B. Artifact integrity manifest

**Intent**

Improve supportability and release confidence by publishing a machine-readable description of what was built.

**Likely files to edit**

- `build-installer.ps1`
- `build-linux-package.ps1`
- Artifacts generation scripts under the repo root

**How it would happen**

1. Emit manifest with version, runtime, hashes, filenames, build date.
2. Validate manifest before release promotion.

**Validation**

- Ensure manifest and generated hashes match produced binaries.

#### C. Better startup diagnostics and self-test summary

**Intent**

Reduce mystery failures on app start by making the startup pipeline visible and diagnosable.

**Likely files to edit**

- `src/OmenCoreApp/App.xaml.cs`
- `src/OmenCore.HardwareWorker/Program.cs`
- `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs`
- `src/OmenCore.Linux/Program.cs`
- `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`

**How it would happen**

1. Add startup self-test stages:
   - config load,
   - worker bootstrap,
   - hardware route selection,
   - telemetry readiness,
   - tray/UI readiness.
2. Summarize success/failure in a compact startup diagnostic object.
3. Surface useful user-facing messages only when needed.

**Validation**

- Simulate startup failures and confirm the right layer reports the problem.

#### D. Worker/client resiliency improvements

**Intent**

Make monitoring and worker communication resistant to transient failures without thrash.

**Likely files to edit**

- `src/OmenCore.HardwareWorker/Program.cs`
- `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs`
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs`

**How it would happen**

1. Tighten retry, cooldown, and backoff policy.
2. Improve stale/degraded/recovering state transitions.
3. Reduce hot-path allocations and reconnect churn.

**Validation**

- Inject worker disconnect/restart scenarios and confirm client recovery behavior.

#### E. Structured crash reports

**Intent**

Turn crash data into actionable support evidence without leaking sensitive information.

**Likely files to edit**

- `src/OmenCoreApp/App.xaml.cs`
- `src/OmenCore.HardwareWorker/Program.cs`
- Logging/export helpers if present

**How it would happen**

1. Capture exception context, version, model, runtime path, and subsystem state.
2. Redact or avoid sensitive filesystem/user information where not needed.
3. Provide export path or support bundle attachment flow.

**Validation**

- Trigger controlled exception and inspect produced crash report for usefulness and hygiene.

#### F. Config migration hardening

**Intent**

Ensure upgrades from older versions do not silently lose or misinterpret settings.

**Likely files to edit**

- `src/OmenCoreApp/Services/ConfigurationService.cs`
- `src/OmenCoreApp/Models/AppConfig.cs`
- `src/OmenCoreApp/Models/MonitoringPreferences.cs`
- Related settings models
- New or extended tests in `src/OmenCoreApp.Tests`

**How it would happen**

1. Add schema versioning if not already explicit.
2. Add targeted migration steps for older config shapes.
3. Add regression tests with representative 3.1.x configs.

**Validation**

- Load old config samples and confirm behavior/settings remain correct.

#### G. Log hygiene pass

**Intent**

Make logs more actionable by reducing warning spam and making severity consistent.

**Likely files to edit**

- `src/OmenCore.HardwareWorker/Program.cs`
- `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs`
- `src/OmenCoreApp/Services/*` logging-heavy services

**How it would happen**

1. Downgrade expected/recoverable noise from warning to info/debug where appropriate.
2. Add cooldowns and deduplication for repeated messages.
3. Introduce correlation/session IDs for startup and worker recovery flows.

**Validation**

- Compare pre/post logs during a noisy monitoring session.

#### H. Synthetic smoke tests

**Intent**

Catch regressions in tray startup, hotkeys, and worker reconnect earlier.

**Likely files to edit**

- `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs`
- Additional test files in `src/OmenCoreApp.Tests`

**How it would happen**

1. Add non-flaky smoke tests around initialization and state transitions.
2. Prefer deterministic simulated service behavior over environment-dependent tests.

**Validation**

- CI smoke pack should detect regressions in hotkey registration, worker reconnect, and settings persistence.

## 4.3 Visual and GUI Premiumization

1. Design refresh pass for premium feel:
   - stronger typography hierarchy,
   - cleaner spacing rhythm,
   - more intentional component states.
2. Unified visual tokens (color, spacing, corner radius, shadow, motion timing).
3. Better dashboard hierarchy:
   - status first,
   - controls second,
   - diagnostics third.
4. Improve hotkey/OSD polish:
   - cleaner card layout,
   - less visual jitter,
   - configurable compact and accent modes.
5. Settings UX overhaul:
   - searchable settings,
   - clearer grouping,
   - inline capability hints and tooltips.
6. High-DPI/scale validation and visual consistency pass.
7. Better empty/error/unsupported states (no dead-end controls).
8. Performance-friendly UI updates (less redraw churn on unchanged data).

### 4.3.1 Additional GUI and Visual Opportunities

These are high-value roadmap additions that would materially improve perceived quality, usability, and brand identity.

#### A. Shared Design System Across WPF and Avalonia

1. Define a common visual token system for both Windows and Linux surfaces:
   - accent palette,
   - semantic status colors,
   - spacing scale,
   - corner radius scale,
   - typography roles,
   - motion timing.
2. Align WPF `ModernStyles.xaml` and Avalonia themes so both apps feel like the same product family.
3. Add a documented visual language section for future UI work to prevent style drift.

**Why this matters:** The project already has a good WPF styling foundation, but the Linux Avalonia shell still feels more utilitarian than premium. Design-system parity would raise quality immediately.

#### B. Replace Emoji-Led UI With a Consistent Icon Language

1. Replace emoji-heavy navigation/status visuals with a proper icon set for:
   - dashboard,
   - fan control,
   - system control,
   - settings,
   - thermal warnings,
   - performance states,
   - connectivity.
2. Keep emoji only where intentionally user-facing and playful, not as structural UI.
3. Use state-aware icon variants for idle/warning/degraded/critical conditions.

**Why this matters:** Emoji are quick, but they weaken the premium feel and can render inconsistently across platforms.

#### C. Quick Popup / Tray Control Center Redesign

1. Turn the tray popup into a compact premium control center:
   - current mode snapshot,
   - fan and performance quick-switch,
   - monitoring health,
   - battery/power source,
   - recent action feedback.
2. Add a compact "safe actions only" path so unsupported controls never appear in popup context.
3. Add optional compact and expanded tray modes.

**Why this matters:** For many users, the tray popup becomes the app. It needs to feel fast, trustworthy, and polished.

#### D. First-Run Onboarding and Capability Summary

1. Add a first-run welcome flow that explains:
   - detected model,
   - supported features,
   - limited/unsupported features,
   - recommended setup steps.
2. Add a capability summary card in Settings or Dashboard that clearly explains the machine's control surface.
3. Provide guided next steps for missing prerequisites (driver/module/install path).

**Why this matters:** A premium product explains itself. This is especially important for mixed support across OMEN/Victus boards.

#### E. Dashboard Modes and Information Density Options

1. Add dashboard density modes:
   - Compact,
   - Standard,
   - Advanced.
2. Allow users to choose between overview-first and telemetry-first layouts.
3. Add card customization so users can pin the metrics that matter most to them.

**Why this matters:** Power users and casual users need different information density. One fixed dashboard layout will always be a compromise.

#### F. Micro-Interactions and Motion Governance

1. Standardize which actions animate and which do not.
2. Add subtle, meaningful motion only for:
   - panel entry,
   - state transition confirmation,
   - warning emphasis,
   - successful apply confirmation.
3. Add reduced-motion mode across both platforms.

**Why this matters:** The project already contains multiple animations. A motion system will prevent the UI from feeling inconsistent or noisy.

#### G. Accessibility and Input Quality Pass

1. Improve focus visibility, keyboard navigation order, and screen-readability semantics.
2. Ensure warning banners and unsupported states are understandable without relying on color alone.
3. Validate contrast ratios and text scaling on high-DPI systems.
4. Add an accessibility-focused settings subsection:
   - reduced motion,
   - compact density,
   - larger values/text,
   - stronger focus ring.

**Why this matters:** Accessibility improvements usually also improve perceived refinement for everyone.

#### H. Premium RGB Studio Experience

1. Add a keyboard or zone preview canvas with more faithful live representation.
2. Show pending vs applied RGB state clearly.
3. Add richer preset browsing with categories like:
   - Minimal,
   - Competitive,
   - Studio,
   - Ambient,
   - Battery saver.
4. Add profile thumbnails or visual chips so RGB profiles feel curated rather than raw configuration.

**Why this matters:** RGB is one of the strongest emotional surfaces in the product. It should feel crafted, not just configurable.

#### I. Smarter Empty, Error, and Unsupported States

1. Replace generic blank areas with purposeful explanation panels.
2. Use explicit state cards for:
   - unsupported feature,
   - feature not available on this model,
   - missing privilege/driver/module,
   - telemetry unavailable,
   - recovery in progress.
3. Give each state a clear next action where possible.

**Why this matters:** Premium feel comes from graceful handling of limits, not just pretty normal-state screens.

#### J. Settings Experience Upgrade Beyond Search

1. Add "Recommended" and "Advanced" grouping.
2. Show inline rationale under risky settings.
3. Add change summaries for settings that require restart/reconnect/reapply.
4. Add a small "recently changed settings" area for easier rollback.

**Why this matters:** The app has grown in scope. Settings need stronger information architecture and better self-documentation.

#### K. Visual Brand Layer

1. Define a clearer OmenCore visual signature:
   - distinctive accent behavior,
   - premium surface gradients,
   - restrained neon/glow usage,
   - consistent numerics/telemetry typography.
2. Add consistent brand treatment to splash/about/update/release messaging surfaces.
3. Create a stronger separation between utility screens and high-impact highlight panels.

### 4.3.2 Dedicated GUI Wishlist

This section isolates the GUI/design wishlist into delivery tiers so it is easier to decide what must land in 3.2.5 versus what should remain a stretch or longer-term redesign effort.

#### Must Ship in 3.2.5

These are the GUI improvements most likely to materially improve product quality, trust, and polish within the current release window.

1. Design-system parity between Windows and Linux
   - Shared color semantics for success, warning, degraded, unsupported, and active states.
   - Matching card hierarchy, spacing rhythm, section headers, and visual density.
   - Consistent typography roles for labels, values, warnings, and section titles.

2. Tray popup / quick control center refresh
   - Cleaner compact layout with mode summary, health status, battery/power state, and safe quick actions.
   - Better action feedback after mode changes.
   - Clear disabled-state messaging for unsupported actions.

3. Dashboard clarity upgrade
   - Stronger separation between summary metrics, active control states, and warnings.
   - Better handling of stale/degraded telemetry so the user always knows whether the numbers are live.
   - Better prioritization of important information over decorative density.

4. Smarter unsupported/error/empty states
   - No blank or ambiguous panels.
   - Every unavailable feature should explain why it is unavailable and what the user can do next.
   - Recovery states should feel intentional instead of broken.

5. Settings experience upgrade
   - Search/filter support.
   - Better grouping into Recommended vs Advanced.
   - Inline warnings for risky settings.
   - Better explanation of restart/reconnect/reapply requirements.

6. OSD and notification polish
   - Improve layout, motion, border treatment, and timing.
   - Make mode-change feedback feel premium rather than debug-like.
   - Ensure compact mode still feels designed, not merely reduced.

7. Accessibility and input quality pass
   - Better focus states.
   - Better keyboard navigation.
   - Reduced motion mode.
   - Better readability at high DPI and with large text scaling.

8. First-run capability summary
   - Show what the detected laptop supports.
   - Explain unavailable features early.
   - Reduce confusion before the user reaches unsupported panels.

#### Strong Stretch Goals for 3.2.5

These are high-value additions that may fit if the stabilization work lands early enough.

1. Replace emoji-led structural UI with a proper icon system
   - Navigation icons.
   - Status icons.
   - Health-state glyphs.
   - Consistent visual language across WPF and Avalonia.

2. Dashboard density modes
   - Compact mode for laptop users.
   - Standard mode for balanced use.
   - Advanced mode for telemetry-heavy users.

3. Customizable dashboard cards
   - Pin/unpin cards.
   - Reorder important metrics.
   - Save preferred layout per user.

4. Premium RGB studio shell
   - Better keyboard/zone preview surface.
   - More curated preset browsing.
   - Clear requested vs applied state.

5. About/update/release surfaces brand pass
   - Better About screen.
   - Better update UX.
   - Better release-note presentation inside app.

6. Stronger tray-to-main-window continuity
   - Same visual identity between tray popup, OSD, dashboard, and settings.
   - Shared action confirmations and status treatment.

#### Long-Term Visual Redesign Ideas

These items are likely beyond 3.2.5 but are worth preserving because they can define the next major quality jump.

1. Full component library and design token catalog
   - Shared specification for cards, buttons, toggles, badges, banners, dialogs, and telemetry widgets.
   - Documented usage rules for future contributors.

2. Full iconographic language and visual brand kit
   - Unique OmenCore identity independent of temporary emoji/icon choices.
   - Exportable asset set for installer, docs, site, tray, and in-app visuals.

3. Dashboard as a modular workspace
   - User-selectable modules.
   - Saved layouts.
   - Quick profile panels.
   - Optional advanced telemetry strips.

4. Rich RGB studio experience
   - Full scene browser.
   - Visual thumbnails.
   - Community gallery integration.
   - Animation authoring tools where hardware allows.

5. Guided first-run tuning wizard
   - Detect model.
   - Classify capabilities.
   - Recommend fan/performance/RGB defaults.
   - Provide safe initial configuration path.

6. Cross-platform shell unification
   - Windows and Linux should ultimately feel like the same flagship product, not a primary app plus a secondary utility.

#### GUI Wishlist Prioritization Principles

1. Prefer changes that increase clarity before changes that add spectacle.
2. Prefer consistency across Windows/Linux before adding niche visual treatments.
3. Any premium visual work must preserve responsiveness and readability.
4. Unsupported-state UX is part of premium quality and should be treated as first-class UI work.
5. Tray, OSD, dashboard, and settings should feel like one coherent experience rather than separate mini-products.

**Why this matters:** Premium feel is partly identity. The app should look intentional and memorable, not only functional.

## 4.4 RGB and Lighting Improvements

1. RGB capability matrix by model and backend path (WMI/EC/HID/OpenRGB where applicable).
2. Better RGB reliability:
   - command retry + timeout control,
   - fallback to last-known-safe profile,
   - explicit "applied vs queued" status.
3. Lighting profile portability:
   - export/import,
   - conflict-safe merge behavior,
   - validation on load.
4. Scene and effect quality upgrades:
   - smoother transitions,
   - independent zone sequencing where supported,
   - richer effect presets tuned for common keyboard layouts.
5. Premium RGB UX:
   - live preview confidence indicator,
   - profile pinning/favorites,
   - safer apply/cancel workflow.
6. Add diagnostics for RGB path failures (device detection, backend route, write response).

### 4.4.1 RGB and Lighting Improvements - Detailed Expansion

#### A. RGB capability matrix

**Intent**

Make lighting support explicit per model, per backend, and per feature class.

**Likely files to edit**

- `src/OmenCoreApp/Services/KeyboardLightingService.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/Views/LightingView.xaml`

**How it would happen**

1. Split support into categories such as single-zone, four-zone, per-key, brightness-only, unsupported.
2. Track backend route and fallback route.
3. Surface capability and limitation text in the lighting UI.

**Validation**

- Confirm lighting UI reflects detected hardware capability accurately.

#### B. RGB reliability improvements

**Intent**

Reduce uncertainty around whether a lighting command actually applied.

**Likely files to edit**

- `src/OmenCoreApp/Services/KeyboardLightingService.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

**How it would happen**

1. Add bounded retry and timeout behavior.
2. Distinguish queued/requested/applied/failed state.
3. Fallback safely when preferred backend path fails.

**Validation**

- Simulate backend failure and confirm user sees accurate state.

#### C. Lighting profile portability

**Intent**

Let users save, restore, and share lighting setups more safely.

**Likely files to edit**

- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/Views/LightingView.xaml`
- Supporting models for profile serialization

**How it would happen**

1. Define export/import schema.
2. Validate imported content against hardware capability.
3. Preserve compatibility for profiles created on different but related layouts.

**Validation**

- Export and re-import profiles on the same machine and on a different capability tier.

#### D. Scene/effect quality upgrades

**Intent**

Improve the quality of lighting effects so they feel curated rather than raw.

**Likely files to edit**

- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/Views/LightingView.xaml`

**How it would happen**

1. Improve effect naming and category structure.
2. Add better defaults and safer apply behavior.
3. Where hardware supports it, allow richer sequencing and pacing.

**Validation**

- Confirm effects remain stable and understandable across supported models.

#### E. Premium RGB UX

**Intent**

Make lighting feel like a premium creative surface rather than a raw settings panel.

**Likely files to edit**

- `src/OmenCoreApp/Views/LightingView.xaml`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

**How it would happen**

1. Add a clearer live preview surface.
2. Add favorites/pinned presets.
3. Separate preview/apply/cancel more clearly.

**Validation**

- Confirm the user can see whether a lighting change is staged or applied.

#### F. RGB diagnostics

**Intent**

Make backend and model path failures diagnosable instead of opaque.

**Likely files to edit**

- `src/OmenCoreApp/Services/KeyboardLightingService.cs`
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`

**How it would happen**

1. Capture backend route, capability tier, and write outcome.
2. Surface concise user-facing explanation for failures.

**Validation**

- Confirm logs and UI reveal enough information to debug unsupported/failing RGB paths.

## 4.5 Linux Experience Upgrades

1. Stronger Linux compatibility messaging by board/kernel path.
2. Improved `diagnose` report completeness and readability.
3. Better feature gating in Avalonia Linux UI based on runtime capabilities.
4. Packaging consistency checks for self-contained binaries and dependency completeness.
5. Reduced friction startup guidance (permissions/modules/path checks surfaced in UI).

### 4.5.1 Linux Experience Upgrades - Detailed Expansion

#### A. Compatibility messaging by board/kernel path

**Intent**

Set realistic expectations early for Linux users based on board, kernel exposure, and available control route.

**Likely files to edit**

- `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`
- `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`
- `src/OmenCore.Avalonia/Views/SettingsView.axaml`

**How it would happen**

1. Detect board ID and control route.
2. Map route to clear support wording.
3. Present that wording in both CLI diagnose output and UI.

**Validation**

- Confirm support text matches actual detected access route.

#### B. Diagnose report completeness/readability

**Intent**

Turn `diagnose` into a support-ready report rather than just a technical dump.

**Likely files to edit**

- `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`

**How it would happen**

1. Group output into system info, capability summary, detected routes, missing prerequisites, recommendations.
2. Add more precise recommendations based on actual failure mode.
3. Make markdown/plain output easier to share in GitHub issues.

**Validation**

- Confirm the report is understandable without reading source code.

#### C. Avalonia feature gating based on runtime capabilities

**Intent**

Ensure the Linux GUI is honest about what is actually possible on the current machine.

**Likely files to edit**

- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`
- `src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`
- `src/OmenCore.Avalonia/Views/DashboardView.axaml`
- `src/OmenCore.Avalonia/Views/SystemControlView.axaml`
- `src/OmenCore.Avalonia/Views/FanControlView.axaml`

**How it would happen**

1. Feed capability data into relevant view models.
2. Bind view visibility and enablement to those flags.
3. Add informative explanation cards for unavailable features.

**Validation**

- Confirm every Linux control path behaves consistently with detected capability state.

#### D. Packaging consistency checks

**Intent**

Make Linux package output deterministic and supportable.

**Likely files to edit**

- `build-linux-package.ps1`
- `src/OmenCore.Linux/Program.cs`
- Potential helper/version files for build metadata

**How it would happen**

1. Verify binary identity before zipping.
2. Ensure self-contained output includes exactly what is needed and excludes stale sidecars.
3. Add package manifest and version consistency checks.

**Validation**

- Rebuild package from clean state and verify identical logical output.

#### E. Startup guidance for permissions and modules

**Intent**

Reduce Linux setup friction by surfacing missing prerequisites directly in the app.

**Likely files to edit**

- `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`
- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`
- `src/OmenCore.Avalonia/Views/SettingsView.axaml`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`

**How it would happen**

1. Detect common setup gaps:
   - root privileges,
   - `hp-wmi` missing,
   - `ec_sys` unavailable or write support disabled,
   - missing hwmon nodes.
2. Present concise next steps and commands where relevant.

**Validation**

- Test typical failure setups and confirm guidance is accurate and actionable.

---

## 5. Milestone Plan

### Milestone A - Stabilize (Critical)

- Fix #96 version and packaging mismatch class.
- Fix F11 false-trigger class.
- Capability-gated UX for #97 class devices.
- Add startup diagnostics and failure surfacing.

### Milestone B - Strengthen (System Quality)

- Unified versioning + artifact manifest checks in CI.
- Config migration hardening and smoke tests.
- Logging and worker resiliency improvements.

### Milestone C - Premiumize (UX + RGB)

- Settings search/restructure and premium visual pass.
- OSD and dashboard polish.
- RGB profile portability and reliability improvements.

---

## 6. Deliverables (v3.2.5)

1. Regression fixes for #96 and F11 activation report.
2. Clear behavior and UX handling for #97-style capability limitations.
3. Unified release/versioning guardrails.
4. Premium UI polish release notes with before/after highlights.
5. RGB reliability and profile quality upgrades.

---

## 7. Definition of Done for 3.2.5

1. No known blocker regressions from 3.2.1 remain open in the targeted bug class.
2. Release artifact/version mismatch class is prevented by pipeline checks.
3. Unsupported hardware paths are clearly communicated without misleading controls.
4. UI and OSD feel materially improved in responsiveness and presentation.
5. RGB operations are reliable, stateful, and diagnosable.
6. Validation pass complete on representative Windows + Linux test matrix.

---

## 8. Backlog Candidates (Post-3.2.5)

1. Community lighting preset feed and curation pipeline.
2. Advanced thermal prediction and proactive suggestions.
3. Rich profile sharing ecosystem (fan/perf/rgb bundles).
4. Deeper per-model tuning wizard for first-run setup.
5. Full icon-system migration replacing emoji-led structural UI.
6. Advanced dashboard personalization with drag/reorder/pin support.
7. RGB studio gallery with preview thumbnails and community bundles.
8. Cross-platform design token documentation and component catalog.

---

## 9. Notes

- This roadmap intentionally prioritizes trust and predictability before feature breadth.
- Every user-facing control should communicate three states clearly: supported, unavailable, and degraded.
- Premium feel is not only visual: it is correctness, feedback quality, and confidence in outcomes.

---

## 10. Execution Board (Phases, Owners, Estimates)

This section turns the roadmap into an implementation board for v3.2.5 delivery.

### 10.1 Owner Lanes

| Lane | Scope |
|------|-------|
| Core Systems | Worker, versioning, packaging, diagnostics, resilience |
| Platform Windows | Hotkeys/OMEN key path, tray, startup behavior |
| Platform Linux | CLI, Avalonia, capability gating, Linux telemetry |
| UX/UI | Dashboard, settings UX, OSD polish, visual consistency |
| RGB/Lighting | Capability matrix, profile flow, reliability, diagnostics |
| QA/Release | Test matrix, regression packs, artifact validation, release checks |

### 10.2 Estimation Scale

- S: 0.5-1.5 engineering days
- M: 2-4 engineering days
- L: 5-8 engineering days
- XL: 9+ engineering days

### 10.3 Phase Plan

#### Phase 0 - Foundation and Triage (Week 1)

| ID | Item | Owner Lane | Size | Dependencies | Exit Criteria |
|----|------|------------|------|--------------|---------------|
| P0-1 | Unify version source for Linux CLI/Avalonia | Core Systems + Platform Linux | M | None | One authoritative version source consumed by all Linux binaries |
| P0-2 | Add release artifact version verifier in pipeline | QA/Release | M | P0-1 | Build fails on any binary/archive version mismatch |
| P0-3 | Repro harness for #96/#97/F11 bug class | QA/Release + Platform Windows/Linux | S | None | Repro scripts/checklists committed and repeatable |
| P0-4 | Capability model spec (full/profile-only/telemetry-only/unsupported) | Platform Linux + UX/UI | S | None | Approved capability contract for CLI + Avalonia + settings messaging |

#### Phase 1 - Critical Stabilization (Weeks 2-3)

| ID | Item | Owner Lane | Size | Dependencies | Exit Criteria |
|----|------|------------|------|--------------|---------------|
| P1-1 | Fix F11 false-trigger class in OMEN interception strict path | Platform Windows | M | P0-3 | F11 never activates OmenCore in strict mode on validated targets |
| P1-2 | Add strict interception mode default + false-positive telemetry | Platform Windows + Core Systems | M | P1-1 | Conservative key matching defaulted; actionable key diagnostics available |
| P1-3 | Implement Linux capability-gated control visibility | Platform Linux + UX/UI | M | P0-4 | Unsupported controls are disabled/hidden with explicit reason text |
| P1-4 | Linux GPU telemetry fallback chain hardening | Platform Linux | M | P0-4 | GPU telemetry reports source path and degrades cleanly when unavailable |
| P1-5 | Startup failure surfacing (GUI + CLI diagnostics hook) | Core Systems + Platform Linux | M | None | Failures shown with actionable message and log reference |

#### Phase 2 - System Hardening (Weeks 4-5)

| ID | Item | Owner Lane | Size | Dependencies | Exit Criteria |
|----|------|------------|------|--------------|---------------|
| P2-1 | Artifact integrity manifest and verification stage | QA/Release + Core Systems | M | P0-2 | Checksums and manifest validated pre-release |
| P2-2 | Worker/client resilience pass (retry/backoff/state transitions) | Core Systems | L | None | No crash-loop thrash; stale/degraded transitions deterministic |
| P2-3 | Config migration test suite for 3.1.x -> 3.2.5 | Core Systems + QA/Release | M | None | Migration tests pass for representative legacy configs |
| P2-4 | Log hygiene pass with severity cleanup and correlation IDs | Core Systems | M | None | Reduced warning noise; startup/session tracing is coherent |
| P2-5 | Smoke tests: tray startup, hotkey routing, worker reconnect | QA/Release + Platform Windows | M | P1-1, P2-2 | Automated smoke pack green in CI |

#### Phase 3 - Premium UX and RGB Lift (Weeks 6-7)

| ID | Item | Owner Lane | Size | Dependencies | Exit Criteria |
|----|------|------------|------|--------------|---------------|
| P3-1 | Settings information architecture + search/filter | UX/UI | L | P1-3 | Faster discoverability; settings grouped and searchable |
| P3-2 | Dashboard and OSD polish pass (layout, motion, visual rhythm) | UX/UI | M | None | Measurable reduction in jitter and improved readability |
| P3-3 | RGB capability matrix and backend route diagnostics | RGB/Lighting + Platform Linux/Windows | M | P1-3 | Per-model capability status displayed and logged |
| P3-4 | RGB profile portability (export/import + validation) | RGB/Lighting | L | P3-3 | Profiles can be exported/imported safely with schema checks |
| P3-5 | RGB reliability pass (apply/queued state + fallback behavior) | RGB/Lighting + Core Systems | M | P3-3 | User can distinguish requested vs applied state reliably |

#### Phase 4 - Release Readiness (Week 8)

| ID | Item | Owner Lane | Size | Dependencies | Exit Criteria |
|----|------|------------|------|--------------|---------------|
| P4-1 | Full regression matrix (Windows + Linux representative hardware) | QA/Release | L | Phases 1-3 | All blocker-class regressions closed |
| P4-2 | Changelog, upgrade notes, compatibility notes refresh | QA/Release + UX/UI | S | P4-1 | Release notes align with shipped behavior and known limits |
| P4-3 | RC bake + telemetry/log review | Core Systems + QA/Release | M | P4-1 | RC accepted with no critical telemetry anomalies |

### 10.4 Priority Queue (Top 10 Implementation Order)

1. P0-1 Unify version source
2. P0-2 Version verifier in pipeline
3. P0-3 Repro harness for #96/#97/F11
4. P1-1 F11 false-trigger fix
5. P1-3 Linux capability-gated controls
6. P1-5 Startup failure surfacing
7. P1-4 Linux GPU telemetry fallback hardening
8. P2-2 Worker/client resilience pass
9. P2-5 Smoke tests for hotkey/tray/worker reconnect
10. P3-3 RGB capability matrix and diagnostics

### 10.5 Suggested Staffing Model

- 1 engineer in Core Systems lane (full-time through phases 0-4)
- 1 engineer in Platform lane split Windows/Linux (full-time through phases 0-3)
- 1 engineer in UX/UI + RGB lane (full-time phases 1-3, support in phase 4)
- 1 QA/release owner (part-time phase 0, full-time phases 2-4)

### 10.6 Effort Summary (Ballpark)

- Phase 0: 4-6 engineering days
- Phase 1: 10-15 engineering days
- Phase 2: 12-18 engineering days
- Phase 3: 12-18 engineering days
- Phase 4: 6-10 engineering days
- Total estimated effort: 44-67 engineering days

### 10.7 Board Operating Rules

1. No feature enters Phase 3 unless blocker regressions from Phase 1 are closed.
2. Any hardware capability uncertainty must degrade to explicit "unsupported" UX, never silent failure.
3. Every release candidate must pass version/artifact validation before test signoff.
4. Bug fixes tied to community issues must include repro notes and post-fix validation evidence.
5. Any new hotkey/interception logic requires regression checks against common function-key workflows.

### 10.8 Implementation Detail and File Map

This section translates the roadmap into likely code edits, delivery mechanics, and validation paths.

#### A. #96 Linux package/version consistency

**What would happen**

1. Remove the hardcoded CLI version constant and replace it with a shared version source.
2. Ensure Avalonia Linux surfaces display the same version source.
3. Add a packaging verification step that reads back the built binaries and compares their reported version to `VERSION.txt` and the archive name.
4. Emit a tiny release manifest into the package so users and maintainers can confirm what was built.

**Likely files to edit**

- `src/OmenCore.Linux/Program.cs`
- `build-linux-package.ps1`
- `src/OmenCore.Avalonia/Views/SettingsView.axaml`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`
- Potentially shared project metadata in the relevant `.csproj` files if versioning is moved to assembly metadata

**Implementation mechanics**

- Use one canonical version source:
   - `VERSION.txt`, or
   - assembly informational/file version populated during build.
- Expose the version through a small helper/service rather than duplicating string constants in app entry points.
- Add a release script step that runs the built Linux CLI with `--version` and captures the value before packaging.
- Add package manifest output such as `version.json` with:
   - app version,
   - build date,
   - target runtime,
   - artifact names,
   - hash metadata.

**Validation**

- Run clean package extraction on Linux.
- Confirm CLI version output matches archive name and manifest.
- Confirm Avalonia About/settings version text matches the same value.

#### B. #97 Linux capability classification and user-facing behavior

**What would happen**

1. Split Linux support into explicit capability tiers.
2. Drive CLI behavior, diagnostics, and Avalonia UI enablement from that same tier model.
3. Replace confusing no-op controls with clear disabled states and reasons.
4. Expand GPU telemetry fallbacks and startup-failure reporting.

**Likely files to edit**

- `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
- `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`
- `src/OmenCore.Linux/Program.cs`
- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`
- `src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs`
- `src/OmenCore.Avalonia/Views/DashboardView.axaml`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`
- `src/OmenCore.Avalonia/Views/SettingsView.axaml`
- Potentially `src/OmenCore.Avalonia/Views/SystemControlView.axaml`

**Implementation mechanics**

- Introduce a capability model object with fields such as:
   - `CanControlFans`
   - `CanSetProfiles`
   - `CanReadGpuTelemetry`
   - `ControlLimitReason`
   - `TelemetrySource`
- Populate it in Linux hardware service/controller startup.
- Surface this model to view models and bind UI visibility/disable states to it.
- Add explanation panels instead of leaving controls visible but ineffective.
- Add a startup diagnostic banner or dedicated startup failure card when the GUI cannot initialize expected services.

**Validation**

- Simulate or test the four capability tiers.
- Confirm CLI diagnose output matches the UI explanation.
- Confirm unsupported controls are hidden or disabled with explicit text.

#### C. F11 activates OmenCore

**What would happen**

1. Tighten OMEN key detection so function-key workflows are not falsely classified as OMEN-key actions.
2. Add a conservative strict mode as default.
3. Add targeted tests for false positives.
4. Optionally expose advanced interception controls in settings for field debugging.

**Likely files to edit**

- `src/OmenCoreApp/Services/OmenKeyService.cs`
- `src/OmenCoreApp/Models/FeaturePreferences.cs`
- `src/OmenCoreApp/Models/AppConfig.cs`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- `src/OmenCoreApp/Views/SettingsView.xaml`
- `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs`
- `src/OmenCoreApp.Tests/ViewModels/SettingsViewModelTests.cs`

**Implementation mechanics**

- Add explicit never-intercept exclusions for known false-positive workflows.
- Prefer model allowlists over broad heuristic matching where possible.
- Keep a strict mode default with an advanced/manual discovery mode only for diagnostic use.
- Record enough structured debug data to identify keycode/scan-code issues without spamming normal logs.

**Validation**

- Manual validation of F11 behavior in fullscreen workflows.
- Regression tests for OMEN key success on supported models.
- Confirm settings persist strict-mode behavior.

#### D. Design-system parity across WPF and Avalonia

**What would happen**

1. Standardize visual tokens across both clients.
2. Align core surface styles, typography, spacing, and status color semantics.
3. Reduce the gap between Windows “premium” feel and Linux “utility” feel.

**Likely files to edit**

- `src/OmenCoreApp/Styles/ModernStyles.xaml`
- `src/OmenCoreApp/App.xaml`
- `src/OmenCore.Avalonia/App.axaml`
- `src/OmenCore.Avalonia/Themes/OmenTheme.axaml`
- `src/OmenCoreApp/Views/MainWindow.xaml`
- `src/OmenCore.Avalonia/Views/MainWindow.axaml`

**Implementation mechanics**

- Define a token table for both stacks covering:
   - background/surface layers,
   - border strengths,
   - semantic colors,
   - typography roles,
   - corner radii,
   - elevation/shadow treatment,
   - motion durations.
- Refactor duplicated inline colors/styles into reusable resources.
- Bring Avalonia closer to the same card/status hierarchy already present in WPF.

**Validation**

- Compare equivalent screens side by side on Windows and Linux.
- Confirm status banners, cards, values, and headings feel visually related.
- Validate readability at common scale factors.

#### E. Tray popup / quick control center refresh

**What would happen**

1. Rework the quick popup into a compact premium control center.
2. Improve current-state summary, action feedback, and control safety.
3. Remove visual duplication/drift between tray popup and rest of app.

**Likely files to edit**

- `src/OmenCoreApp/Views/QuickPopupWindow.xaml`
- `src/OmenCoreApp/Views/QuickPopupWindow.xaml.cs`
- `src/OmenCoreApp/Utils/TrayIconService.cs`
- `src/OmenCoreApp/App.xaml.cs`
- Potentially `src/OmenCoreApp/Styles/ModernStyles.xaml`

**Implementation mechanics**

- Replace local popup-specific styling with shared design tokens where possible.
- Reorganize popup sections into:
   - health and live summary,
   - quick fan/performance actions,
   - safe status indicators,
   - exit/open/full app actions.
- Hide unsupported actions using the same capability-aware rules as the main UI.
- Add clearer immediate feedback after action selection.

**Validation**

- Ensure popup opens quickly and stays readable at common DPI scales.
- Confirm unsupported features do not appear as active actions.
- Confirm tray and popup visuals feel consistent with the dashboard.

#### F. Dashboard clarity upgrade

**What would happen**

1. Rebalance dashboard layout around what is most important.
2. Make live vs stale vs degraded telemetry unmistakable.
3. Reduce visual clutter without reducing useful information.

**Likely files to edit**

- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
- `src/OmenCoreApp/Views/DashboardView.xaml`
- `src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs`
- `src/OmenCore.Avalonia/Views/DashboardView.axaml`
- Potentially `src/OmenCoreApp/Styles/ModernStyles.xaml`

**Implementation mechanics**

- Separate dashboard into zones:
   - primary health summary,
   - active control state,
   - temperatures/fans/loads,
   - detailed secondary stats.
- Add explicit badges/banners for telemetry freshness.
- Simplify card hierarchy so the user can parse state in seconds.

**Validation**

- Validate normal, stale, degraded, and throttling states.
- Confirm the most important warning is always visible above secondary telemetry.

#### G. Settings experience upgrade

**What would happen**

1. Make settings easier to discover and safer to use.
2. Add stronger grouping, search, inline explanation, and risk signaling.
3. Bring Linux and Windows settings closer together conceptually.

**Likely files to edit**

- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- `src/OmenCoreApp/Views/SettingsView.xaml`
- `src/OmenCoreApp/Views/SettingsView.xaml.cs`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`
- `src/OmenCore.Avalonia/Views/SettingsView.axaml`

**Implementation mechanics**

- Add grouping layers such as Recommended, Advanced, Diagnostics.
- Add settings search/filter model in the view model.
- Add per-setting metadata:
   - restart required,
   - reapply required,
   - risk level,
   - unsupported on current hardware.
- Expose recent changes and reset affordances where useful.

**Validation**

- Search for common terms like fan, rgb, hotkey, telemetry.
- Confirm risky settings clearly communicate consequences.
- Confirm unsupported options are explained rather than silently absent when appropriate.

#### H. OSD and notification polish

**What would happen**

1. Bring hotkey OSD and notifications up to the same standard as the rest of the UI.
2. Improve layout, animation pacing, and visual intent.
3. Make action feedback feel more productized and less debug-like.

**Likely files to edit**

- `src/OmenCoreApp/Views/HotkeyOsdWindow.xaml`
- `src/OmenCoreApp/Views/HotkeyOsdWindow.xaml.cs`
- `src/OmenCoreApp/Services/NotificationService.cs`
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/Styles/ModernStyles.xaml`

**Implementation mechanics**

- Replace ad hoc styling with shared badge/card/motion tokens.
- Add better hierarchy for category, value, and trigger source.
- Add subtle motion only where it improves comprehension.
- Ensure compact mode remains intentionally designed.

**Validation**

- Test burst actions and repeated mode changes.
- Confirm no jitter or overlapping feedback states.
- Validate appearance at different DPI scales.

#### I. Accessibility and reduced-motion pass

**What would happen**

1. Add accessibility-conscious visual and interaction controls.
2. Ensure premium look does not come at the cost of usability.

**Likely files to edit**

- `src/OmenCoreApp/Styles/ModernStyles.xaml`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- `src/OmenCoreApp/Views/SettingsView.xaml`
- `src/OmenCore.Avalonia/App.axaml`
- `src/OmenCore.Avalonia/Themes/OmenTheme.axaml`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`
- `src/OmenCore.Avalonia/Views/SettingsView.axaml`

**Implementation mechanics**

- Add settings for:
   - reduced motion,
   - stronger focus ring,
   - larger values/text,
   - possibly compact/comfortable density.
- Reduce reliance on color-only state encoding.
- Audit keyboard navigation and focus order on major views.

**Validation**

- Keyboard-only navigation pass.
- Reduced-motion pass on both WPF and Avalonia.
- Contrast and readability review at common scales.

#### J. First-run capability summary and onboarding

**What would happen**

1. Add a first-run explanation layer so users immediately understand what their machine supports.
2. Reduce support burden caused by mismatched expectations on partially supported hardware.

**Likely files to edit**

- `src/OmenCoreApp/Views/MainWindow.xaml`
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/Views/SettingsView.xaml`
- `src/OmenCore.Avalonia/Views/MainWindow.axaml`
- `src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs`
- `src/OmenCore.Avalonia/Views/SettingsView.axaml`
- Supporting capability/data services on both platforms

**Implementation mechanics**

- Use capability detection results to generate a concise support summary.
- Show it once on first run, then make it available later in Settings/About/Diagnostics.
- Include recommended next actions when prerequisites are missing.

**Validation**

- Confirm first-run path does not block normal app use.
- Confirm capability summary matches actual enabled/disabled controls.

---

## 11. Detailed Done Tracker (Checkbox Board)

Use this section as the working completion board for v3.2.5.

### 11.1 Status Legend

- [ ] Not started
- [x] Done
- [~] In progress (use temporary marker during active implementation)

### 11.2 Global Release Done Box

- [ ] v3.2.5 release candidate accepted
- [ ] All blocker-class regressions closed
- [ ] Version and artifact checks green
- [ ] Windows and Linux validation matrix green
- [ ] Changelog and compatibility notes complete

### 11.3 Bug Burn-Down Done Boxes

#### #96 Linux package/version consistency

- [ ] Replace hardcoded Linux CLI version path with shared version source
- [ ] Ensure Linux GUI reports same version source
- [ ] Add pre-release version verifier (archive vs CLI vs GUI)
- [ ] Add release manifest output (`version.json`)
- [ ] Validate clean Ubuntu extraction reports expected version
- [ ] Attach evidence (logs/screenshots/command output) in release notes draft

Evidence notes:
- Owner:
- Branch/PR:
- Validation date:
- Validation command(s):

#### #97 Victus Linux capability/reliability

- [ ] Implement capability classification (full/profile-only/telemetry-only/unsupported)
- [ ] Wire capability classification into CLI behavior and messaging
- [ ] Wire capability classification into Avalonia control visibility and disable reasons
- [ ] Improve diagnostics report with board-specific guidance
- [ ] Harden Linux GPU telemetry fallback chain
- [ ] Validate on representative partial hp-wmi board scenario

Evidence notes:
- Owner:
- Branch/PR:
- Validation date:
- Test hardware/VM profile:

#### F11 activates OmenCore

- [ ] Add strict interception filtering mode and set as default
- [ ] Add explicit never-intercept function-key guard path including F11 workflow
- [ ] Add false-positive logging payload sufficient for tuning
- [ ] Add regression tests for key-routing and false-positive suppression
- [ ] Validate F11 workflow in at least one fullscreen app/game scenario

Evidence notes:
- Owner:
- Branch/PR:
- Validation date:
- Repro checklist result:

### 11.4 Phase Execution Done Boxes

#### Phase 0 Foundation and Triage

- [ ] P0-1 Unify version source for Linux CLI/Avalonia
- [ ] P0-2 Add release artifact version verifier in pipeline
- [ ] P0-3 Repro harness for #96/#97/F11 bug class
- [ ] P0-4 Capability model spec approved
- [ ] Phase 0 exit criteria signed off

Phase 0 sign-off:
- Owner:
- Date:
- Notes:

#### Phase 1 Critical Stabilization

- [ ] P1-1 Fix F11 false-trigger class in strict path
- [ ] P1-2 Strict interception default plus telemetry
- [ ] P1-3 Linux capability-gated control visibility
- [ ] P1-4 Linux GPU telemetry fallback hardening
- [ ] P1-5 Startup failure surfacing
- [ ] Phase 1 exit criteria signed off

Phase 1 sign-off:
- Owner:
- Date:
- Notes:

#### Phase 2 System Hardening

- [ ] P2-1 Artifact integrity manifest and verification
- [ ] P2-2 Worker/client resilience pass
- [ ] P2-3 Config migration test suite
- [ ] P2-4 Log hygiene pass with correlation IDs
- [ ] P2-5 Smoke tests for tray/hotkey/worker reconnect
- [ ] Phase 2 exit criteria signed off

Phase 2 sign-off:
- Owner:
- Date:
- Notes:

#### Phase 3 Premium UX and RGB Lift

- [ ] P3-1 Settings IA plus search/filter
- [ ] P3-2 Dashboard and OSD polish pass
- [ ] P3-3 RGB capability matrix and diagnostics
- [ ] P3-4 RGB profile portability (export/import)
- [ ] P3-5 RGB reliability pass
- [ ] P3-6 Visual design-system parity pass (WPF and Avalonia)
- [ ] P3-7 Tray popup/control center polish pass
- [ ] P3-8 Accessibility and reduced-motion pass
- [ ] Phase 3 exit criteria signed off

Phase 3 sign-off:
- Owner:
- Date:
- Notes:

#### Phase 4 Release Readiness

- [ ] P4-1 Full regression matrix pass (Windows plus Linux)
- [ ] P4-2 Changelog/upgrade/compatibility notes refresh
- [ ] P4-3 RC bake and telemetry/log review
- [ ] Phase 4 exit criteria signed off

Phase 4 sign-off:
- Owner:
- Date:
- Notes:

### 11.5 Quality Gates Done Boxes

#### Functional gates

- [ ] All must-have bug acceptance criteria validated
- [ ] Capability-gated UX behavior verified for unsupported-control paths
- [ ] No misleading actionable controls on unsupported hardware

#### Reliability gates

- [ ] No version mismatch between release tag and shipped binaries
- [ ] Startup diagnostics emitted on controlled failure scenarios
- [ ] Worker reconnect path stable under repeated restart simulation

#### UX and premium feel gates

- [ ] Settings discoverability improvements complete and reviewed
- [ ] OSD/dashboard visual polish reviewed at standard and high DPI
- [ ] Empty/error/unsupported states have clear user guidance
- [ ] Windows and Linux surfaces feel visually consistent as one product family
- [ ] Tray popup/control center matches premium UX standard
- [ ] Accessibility/reduced-motion/high-contrast considerations reviewed

#### RGB gates

- [ ] RGB capability matrix surfaced and accurate on validated models
- [ ] RGB apply flow exposes requested vs applied state
- [ ] RGB profile portability flow validated end-to-end

#### Release gates

- [ ] CI pipeline green on release branch
- [ ] Installer/package hash and integrity checks complete
- [ ] Release notes include fixed issues, known limits, and verification commands

### 11.6 Validation Matrix Done Boxes

#### Windows matrix

- [ ] OMEN model validation pass
- [ ] Victus model validation pass
- [ ] Hotkey/interception regression pass (including F11 workflow)
- [ ] Tray/startup/worker reconnect smoke pass

#### Linux matrix

- [ ] Ubuntu representative test pass
- [ ] Capability-class scenarios validated (full/profile-only/telemetry-only/unsupported)
- [ ] CLI diagnostics output review pass
- [ ] Avalonia startup and control gating pass

### 11.7 Final Ship Checklist

- [ ] Final go/no-go meeting complete
- [ ] RC promoted to stable
- [ ] Artifacts published and checksums posted
- [ ] Public changelog published
- [ ] Post-release monitoring window opened
- [ ] Post-release monitoring window closed without critical regressions
