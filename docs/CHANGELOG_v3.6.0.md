# OmenCore v3.6.0 - Draft Changelog

**Version:** 3.6.0
**Release Date:** TBD
**Release Status:** Active development on `v3.6.0-planning`
**Previous Release:** v3.5.0 (2026-05-05, hotfix snapshot refreshed 2026-05-06)
**Type:** Lightweight resource usage, startup deferral, tray idle behavior, reliability follow-up

---

## Overview

v3.6.0 begins the lightweight-resource pass planned after v3.5.0. The theme is to make OmenCore quieter at idle, minimized, and tray-only without weakening active fan ownership, OSD telemetry, tuning safety, RGB control, or diagnostics.

The first rule for this release is measurement before claims. Resource improvements should be backed by diagnostics evidence and before/after checks for visible-window idle, tray-only idle, OSD-visible, fan curve/hold active, and page activation scenarios.

---

## Implemented So Far

- 3.6.0 release-blocker fix: The Settings live monitoring cadence status card now binds its read-only status properties (`MonitoringCadenceTier`, `MonitoringCadenceReason`, `MonitoringCadenceBlockers`) as explicit `OneWay` bindings, fixing the crash seen during real-device smoke runs when WPF attempted a writeback against read-only `SettingsViewModel` properties.
- 3.6.0 release hygiene: Updated app, hardware-worker, Avalonia, and installer version metadata from stale `3.5.0` values to `3.6.0` so startup logs, assembly metadata, installer identity, and packaged binaries report the same release version.
- 3.6.0 measurement baseline: Added `resource-footprint.txt` to diagnostics export. The snapshot captures OmenCore process footprint, hardware-worker process footprint, monitoring cadence reason, recent cadence transitions, fan curve/hold blockers, active background timers, managed runtime/GC state, and optional subsystem assembly-load hints. Regression tests verify the file is always included and contains the lightweight-baseline sections.
- 3.6.0 startup diet: Removed tray-startup forced lazy loads of `Dashboard` and `SystemControl`. Tray startup now uses lightweight `MainViewModel` state and cheap model capability data instead of constructing heavier dashboard/tuning/GPU-power view-model paths.
- 3.6.0 startup diet: `Dashboard` no longer constructs `SystemControl` as a side effect when it initializes sidebar/status values. It uses already-loaded `SystemControl` state if available, otherwise falls back to `MainViewModel`'s current fan/performance state.
- 3.6.0 startup diet: `GeneralViewModel` no longer forces `SystemControl` construction during its own initialization. If `SystemControl` later loads through the OMEN/Tuning path, `MainViewModel` wires the existing `GeneralViewModel` to it at that point.
- 3.6.0 regression coverage: Added `MainViewModelTests` coverage proving `Dashboard` and `General` do not force `SystemControl` lazy-load.
- 3.6.0 tray idle cadence: Added `FanService.FanActivityStateChanged` so the app can recalculate minimized/tray-only monitoring cadence when a custom curve or backend fan hold starts/stops. This prevents tray-only cadence from staying too slow while OmenCore is actively maintaining fan ownership, and allows it to return low when ownership ends.
- 3.6.0 regression coverage: Added fan activity event tests for custom curve start/stop and backend hold transitions between fan commands.
- 3.6.0 provider laziness: RGB/peripheral startup work is now first-use. `MainWindow_Loaded` no longer triggers Corsair discovery, and `MainViewModel` no longer starts Corsair/Logitech/Razer/OpenRGB manager setup from its constructor. The RGB tab now calls `EnsureLightingInitializedAsync()` when opened, and explicit lighting actions initialize the lighting stack on demand.
- 3.6.0 startup diet: Opening the General tab no longer constructs the advanced `FanControlViewModel` just to sync preset selection state. It wires to `FanControl` only if that VM was already loaded, while profile apply still goes directly through `FanService`.
- 3.6.0 regression coverage: Extended `MainViewModelTests` to verify constructor-time Lighting remains unloaded and General does not force either `SystemControl` or `FanControl` lazy-load.
- 3.6.0 bug fix (Discord: OsamaBiden / BEAM): Fixed Auto handoff regression where `Auto` presets with explicit curve payloads could be overwritten by BIOS-default restore after `Max`/policy transitions. `FanService` now preserves controller-applied Auto payloads instead of forcing `RestoreAutoControl()` when a curve payload is present.
- 3.6.0 bug fix (Discord: OsamaBiden / BEAM): Auto presets with explicit curve payloads now keep the curve engine active instead of leaving fan ownership ambiguous, improving ramp-down behavior after Max/policy transitions while still preserving higher-power thermal policy.
- 3.6.0 bug fix: Game profile fan/performance actions now apply through `FanService` and `PerformanceModeService` even when the deferred Fan Control and System Control pages have not been opened, preventing silent no-ops after the 3.6 startup diet.
- 3.6.0 bug fix (Discord: OsamaBiden / BEAM): Fixed custom curve delete command requery behavior so selecting a removable preset immediately re-enables Delete without requiring additional UI activity.
- 3.6.0 bug fix (Discord: OsamaBiden / BEAM): Hardened GPU power boost verification by splitting payload/readback semantics for `Minimum`, `Medium`, `Maximum`, and `Extended`. Accepted-but-ignored writes now fail verification instead of reporting false success.
- 3.6.0 bug fix (Discord: OsamaBiden / BEAM): Added explicit `Extended` GPU mode mapping in both WMI and OGH paths, including startup detection/readback and restore flows.
- 3.6.0 bug fix (Discord: OsamaBiden / BEAM): Improved WMI fan `100%` behavior for custom/manual paths by mapping full-speed requests to protocol ceiling, allowing firmware clamp to true hardware max instead of capping to detected classic max level.
- 3.6.0 bug fix (Discord: Hades / snowfall hateall): Rebalanced built-in Auto/Gaming/Extreme fan curves so moderate 70-80C operation no longer ramps into near-Max behavior; explicit Max remains the 100% full-cooling mode.
- 3.6.0 bug fix (Discord: Hades / snowfall hateall): Removed the stale runtime Extreme override that forced 100% fans at 75C, so the rebalanced Extreme curve is respected by the actual fan service path.
- 3.6.0 bug fix (Discord: Hades / snowfall hateall): Updated stale Extreme preset UI copy so the card no longer claims 100% fan duty at 75C after the 3.6 curve rebalance.
- 3.6.0 bug fix (Discord: snowfall hateall / GitHub #125): Custom fan curves now detect the v3.5.0 "accepted positive write, RPM stays at 0" failure mode and send a bounded one-shot wake pulse (35-60% bounded, 60s cooldown) instead of requiring users to switch curves manually to wake the EC. This addresses the reported issue where fans would either go to max or zero and require manual mode switching to recover.
- 3.6.0 UX fix (Discord: snowfall hateall): Fan curve editor drag now uses canvas-level mouse capture and tracks the dragged `FanCurvePoint` by reference instead of by fragile sorted index, making dense/boundary nodes more responsive when the cursor moves quickly.
- 3.6.0 UX fix (Discord: snowfall hateall): Fan curve safety floors are now visible as requested-vs-effective preview state. Temperature changes refresh the effective percentage and warning immediately, and custom-curve verification status now says when the thermal guard raised a low requested duty.
- 3.6.0 Linux daemon hardening: Performance-hold readback comparison now treats Default/Balanced as equivalent and only re-applies on real drift, reducing unnecessary reasserts on board/kernel combinations that normalize the same thermal mode differently.
- 3.6.0 RGB fallback robustness: Keyboard lighting apply paths now retry model-specific fallback backends when the active 4-zone backend fails, improving resilience on affected OMEN/Victus systems with brittle preferred RGB paths.
- 3.6.0 release hygiene: Replaced newly surfaced bare `catch {}` blocks in Settings and WMI monitoring cleanup paths with typed/logged exception handling so the release gate remains actionable.
- 3.6.0 optimization: Continued M2/M5 tray-only cadence work so low-overhead + tray-only sessions can settle to the 10s ultra-low cadence when no fan/OSD blockers are active, with explicit diagnostic reason text and transition telemetry coverage.
- 3.6.0 optimization: Deferred conflict/tuning software scan loops until Monitoring, OMEN, Tuning, or Optimizer tabs are actually opened, avoiding unconditional startup scans for Afterburner/RTSS/XTU/FanControl detection.
- 3.6.0 optimization: Added adaptive static-tray sampling so the shared monitoring pipeline can keep core telemetry alive while reducing expensive GPU telemetry refreshes during low-overhead tray-only sessions.
- 3.6.0 optimization: Memory Optimizer page telemetry now pauses its 2s UI refresh timer when the user leaves the Memory tab, and Settings schedule enforcement now starts its 30s timer only while schedule rules exist.
- 3.6.0 optimization: Memory Optimizer's visible-page refresh loop now reuses the current memory snapshot, avoids duplicate process-count/memory-counter reads for cleanup previews, and defers executable-path resolution for top-process rows until the user explicitly opens a process location.
- 3.6.0 Memory Optimizer tuning: Auto Clean now exposes a persisted minimum-gap override so users can reduce repeated cleanup cycles under sustained pressure while leaving `0` as the selected profile default cooldown.
- 3.6.0 functionality: Memory Optimizer top-process rows can now be added directly to working-set exclusions from the context menu, with normalized process names and duplicate prevention.
- 3.6.0 functionality: Memory Optimizer now shows dynamic exclusion guidance that suggests currently high-memory processes not yet excluded, then updates those suggestions as exclusions are added or removed.
- 3.6.0 Memory Optimizer metrics: Cleanup results now capture and display before/after deltas for physical used/available memory, standby list, system cache, commit, page file, and modified page list so users can see what a cleanup actually changed beyond a single "freed MB" value.
- 3.6.0 Memory Optimizer stutter guard: Auto-clean's game-aware quiet window now uses monitor-aware fullscreen/borderless detection and a testable clean-flag selector, limiting foreground game sessions to working-set trims unless memory pressure is critical.
- 3.6.0 Memory Optimizer UX: Auto Clean now exposes a persisted game-aware quiet-window toggle, letting users choose whether foreground fullscreen/borderless games receive the stutter-safe working-set-only path or the full safe cleanup profile.
- 3.6.0 Linux diagnostics (Discord: Loco Motivo): `omencore-cli diagnose` now samples recent kernel logs with a short timeout and reports known NVIDIA/SBIOS ACPI platform-request failures plus stale `i2c` udev-group warnings in human, JSON, and issue-report output.
- 3.6.0 Linux service UX: `daemon --install` and `daemon --generate-service` now share one systemd unit generator, `daemon --status` reports service/config state plus the active performance-hold mode/interval/power-limit settings, and the Linux README now points users to the built-in service commands instead of a stale manual unit.
- 3.6.0 Linux diagnostics: `omencore-cli diagnose` now reports service/package readiness details for systemd availability, OmenCore unit state, system/user config presence, and the single-file bundle extraction directory.
- 3.6.0 RGB UX: Lighting now surfaces control ownership, HP keyboard backend, provider status details, and OMEN Light Studio/Gaming Hub conflict warnings directly in the page header.
- 3.6.0 RGB recovery: Lighting now includes a dedicated Restore Keyboard action that reapplies saved HP keyboard colors through the active backend, and startup restore now uses the same backend zone ordering as manual Apply.
- 3.6.0 RGB reliability: Keyboard lighting writes are now serialized across profile/zone/test/brightness/backlight operations, preventing concurrent backend-switch/dispose races when manual apply, scenes, startup restore, and temperature RGB updates overlap.
- 3.6.0 RGB backlight reliability: Brightness and backlight operations now use model-aware fallback retries (with active-backend swap on success) instead of failing hard on a single unstable backend.
- 3.6.0 RGB diagnostics: Diagnostics export now includes `rgb-control-path.txt` with HP keyboard availability/backend/per-key state, external RGB provider connection details, and known HP/OMEN conflict processes without forcing lazy RGB providers to initialize.
- 3.6.0 tuning diagnostics: Diagnostics export now includes `tuning-safety.txt` with saved startup restore gates, CPU undervolt/Curve Optimizer pending-test metadata, GPU OC pending-test metadata, last-confirmed values, and AMD STAPM/Tctl limits without waking System Control or hardware tuning providers.
- 3.6.0 fan UX: Fan Control now shows a fan ownership panel explaining whether firmware, OmenCore Max, constant duty, or a managed curve currently owns fan behavior, including backend/status context.
- 3.6.0 visual refresh: Added shared first-party status glyphs and badges for confirmed, degraded, blocked, and overwritten states across Dashboard monitoring health, Fan Control ownership, RGB sync/ownership, and Performance/GPU power surfaces.
- 3.6.0 installer branding: Refreshed the Inno wizard image generator and regenerated `wizard-large.bmp` / `wizard-small.bmp` with the red/blue OmenCore control-suite art direction.
- 3.6.0 model identity fix (Discord: Hades / 8A43): Exact capability ProductId matches now win over broad WMI model-name patterns unless the ProductId is explicitly ambiguous, preventing Hades `8A43` OMEN 16-n0xxx systems from resolving as sibling `8A44`.
- 3.6.0 model diagnostics (Discord: Hades / 8A43): Identity summaries now distinguish the baseboard ProductId used by OmenCore (`8A43`) from HP's public support product number/SKU (`6G103EA`), and the 8A43 capability/keyboard notes include the HP support name `OMEN Gaming Laptop 16-n0002ni`.
- 3.6.0 model support (GitHub #124): Added a safer fallback for ProductId-missing OMEN 16-am0xxx / 16-am0168ng Intel Core Ultra + RTX 5070 reports, disabling direct EC writes while preserving WMI fan/performance, GPU boost, and 4-zone keyboard detection. Exact `8D2F` still resolves to the older AMD am0xxx profile.
- 3.6.0 fan calibration fix (Discord: ZeroMentu): The calibration wizard now always restores BIOS auto fan control after completion, cancellation, or failure so the final 100% calibration step cannot leave fans pinned at Max until reboot.
- 3.6.0 fan calibration UX (Discord: ZeroMentu): The calibration wizard now includes a manual Restore Auto action and clearer notes so users can release calibration/manual fan ownership without restarting if fans still sound pinned high.
- 3.6.0 Linux diagnostics (GitHub #123): Added OMEN Max 16-ah0xxx board `8D41` guidance for RTX 50-series TGP caps where `nvidia-powerd` reports Dynamic Boost disabled by SBIOS/client request, including `nvidia-powerd` log capture in diagnostics guidance.
- 3.6.0 cleanup: Removed the obsolete runtime polling-interval path and retired the old polling-profile / polling-interval settings UX. Settings now reflects the real automatic cadence policy while still normalizing legacy config fields for compatibility.
- 3.6.0 cleanup: Replaced stale magic tab-index checks with named tab constants, corrected conflict-monitor startup to the actual OMEN/Tuning/Monitoring/Optimizer tabs, and avoided waking tuning conflict scans from Diagnostics.
- 3.6.0 hotkey reliability: Global hotkey registration now de-duplicates queued actions and rejects conflicting chord mappings before Win32 registration, reducing startup/retry timing cases that previously produced ambiguous or flaky hotkeys.
- 3.6.0 diagnostics/UX: Settings now shows a live monitoring cadence card with the current tier, cadence reason, and the active blockers preventing tray-only ultra-low cadence, using the existing unified cadence telemetry instead of adding parallel state.
- 3.6.0 release readiness: Added `qa/v3.6.0-checklist.md` with explicit CPU/RAM before/after resource measurement gates, `resource-footprint.txt` evidence requirements, cadence blocker checks, startup/provider-laziness checks, and restored Linux build validation rows.
- 3.6.0 optimization: Tray quick-popup telemetry refresh is now a visible-only registered timer. The 1s popup refresh no longer starts at object construction and unregisters when the popup is hidden or closed, so diagnostics reflect it only while the popup is actually visible.
- 3.6.0 optimization: The main tray icon refresh loop now registers with `BackgroundTimerRegistry` and updates its diagnostic description when the tray temperature badge is disabled, making tray tooltip/menu refresh and live badge redraw overhead visible in resource exports.
- 3.6.0 optimization: OSD overlay stats and network refresh timers now register as visible-only work while the overlay is active. OSD-visible resource exports now show the 1s stats refresh and optional 5s network polling separately, and OSD update failures are debug-logged instead of silently swallowed.
- 3.6.0 tuning diagnostics: CPU undervolt status polling and EDP throttling mitigation loops now register with `BackgroundTimerRegistry`, so tuning page activation/resource exports show those active safety/readback monitors instead of hiding them behind untracked `Task.Delay` loops.
- 3.6.0 RGB optimization: Temperature-reactive keyboard RGB polling now registers with `BackgroundTimerRegistry` while enabled, unregisters and disposes its cancellation token on stop, and uses exception-free hex parsing instead of a stale bare catch.
- 3.6.0 optimization: Dashboard hardware metrics history is now capped by both age and count, and the monitor-loop power trend calculation no longer allocates a temporary `TakeLast().ToList()` snapshot on every sample.
- 3.6.0 optimization: Load, Thermal, and GPU voltage/current charts now render directly from indexable bound sample collections instead of copying them with `ToList()` on every render; the GPU voltage/current chart also computes min/max ranges in a single pass without temporary projection lists.
- 3.6.0 regression coverage: Added/expanded tests across WMI verification, fan preset/Auto handoff behavior, fan smoothing/diagnostic mode state cleanup, fan command UI command-state requery, GPU power semantics, fan-level mapping, and monitoring cadence telemetry.
- 3.6.0 regression coverage: Added tests for deferred conflict monitoring startup, Memory/Settings timer lifecycle, adaptive static-tray sampling policy, compatibility persistence for legacy monitoring config values after the settings cleanup, and the new live cadence tier/blocker summaries in Settings.
- 3.6.0 regression coverage: Added hardware monitoring tests that verify dashboard metric history is pruned by count and age.

---

## Validation Snapshot

Commands run during early 3.6.0 development:

```powershell
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter DiagnosticExportSnapshotTests --verbosity quiet
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter MainViewModelTests --verbosity quiet
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FanPresetVerificationTests|MainViewModelTests|DiagnosticExportSnapshotTests" --verbosity quiet
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "MainViewModelTests|HotkeyAndMonitoringTests" --verbosity minimal
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter SettingsViewModelTests --verbosity quiet
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --verbosity minimal
dotnet build src\OmenCoreApp\OmenCoreApp.csproj -c Release --no-restore
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FullyQualifiedName~FanControlViewModelTests|FullyQualifiedName~PowerAutomationServiceTests"
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FullyQualifiedName~MainViewModelTests"
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FullyQualifiedName~ReleaseGateCodeHygieneTests"
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FullyQualifiedName~FanSmoothingTests|FullyQualifiedName~FanPresetVerificationTests|FullyQualifiedName~FanControlViewModelTests|FullyQualifiedName~PowerAutomationServiceTests"
dotnet build src\OmenCoreApp\OmenCoreApp.csproj -c Release --no-restore
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FullyQualifiedName~MemoryOptimizerServiceAutoCleanTests|FullyQualifiedName~MemoryOptimizerViewModelTests"
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FullyQualifiedName~FanControlViewModelTests"
dotnet build src\OmenCoreApp\OmenCoreApp.csproj -c Release --no-restore
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FullyQualifiedName~MemoryOptimizerViewModelTests"
dotnet build src\OmenCore.Linux\OmenCore.Linux.csproj --no-restore
```

Observed outcomes:
- `DiagnosticExportSnapshotTests`: PASS, 4/4.
- `MainViewModelTests`: PASS, 5/5.
- Combined targeted 3.6 regression slice (`FanPresetVerificationTests|MainViewModelTests|DiagnosticExportSnapshotTests`): PASS, 20/20.
- Focused optimization regression slice (`MainViewModelTests|HotkeyAndMonitoringTests`): PASS, 38/38.
- `SettingsViewModelTests`: PASS, 6/6.
- Full `OmenCoreApp.Tests` suite (`--no-restore`): PASS, 467/467.
- Windows app Release build: PASS.
- Focused fan-curve regression slice after the built-in curve rebalance (`FanControlViewModelTests|PowerAutomationServiceTests`): PASS, 9/9.
- `MainViewModelTests` after lazy game-profile apply fix: PASS, 8/8.
- `MainViewModelTests|SettingsViewModelTests` after timer lifecycle cleanup: PASS, 19/19.
- `ReleaseGateCodeHygieneTests` after typed/logged catch cleanup: PASS, 4/4.
- Focused fan regression slice after zero-RPM curve wake recovery (`FanSmoothingTests|FanPresetVerificationTests|FanControlViewModelTests|PowerAutomationServiceTests`): PASS.
- Windows app Release build after Fan Curve Editor drag cleanup: PASS.
- Focused Memory Optimizer refresh-efficiency tests after process-path laziness and top-process identity cleanup: PASS, 8/8.
- `ReleaseGateCodeHygieneTests` after Memory Optimizer refresh cleanup: PASS, 4/4.
- Focused Memory Optimizer metrics slice after cleanup before/after delta reporting: PASS, 10/10.
- Focused Memory Optimizer quiet-window slice after monitor-aware fullscreen/borderless detection: PASS, 12/12.
- Focused Memory Optimizer view-model/auto-clean slice after persisted quiet-window and cooldown UI controls: PASS, 14/14.
- Focused model/keyboard/fan verification slice after GitHub #124 fallback and calibration cleanup: PASS, 39/39.
- Windows app Release build after GitHub #124 fallback and calibration cleanup: PASS.
- Linux CLI build after GitHub #123 nvidia-powerd Dynamic Boost diagnostics: PASS.
- `FanControlViewModelTests` after fan-curve safety preview refresh and stale Extreme UI copy cleanup: PASS, 10/10.
- Windows app Release build after fan-curve safety preview XAML cleanup: PASS.
- `MemoryOptimizerViewModelTests` after top-process exclusion shortcut: PASS, 2/2.
- Windows app Release build after Memory Optimizer exclusion shortcut XAML cleanup: PASS.
- Linux CLI build after diagnose kernel-log hint collector: PASS.
- Linux CLI build after daemon service/status UX cleanup: PASS.
- Linux CLI build after diagnose service/package readiness checks: PASS.
- Windows app Release build after Lighting ownership and Fan Control ownership UI: PASS.
- `FanControlViewModelTests` after fan ownership status panel: PASS, 11/11.
- Focused model identity/database slice after the 8A43 exact-match and HP support SKU diagnostics cleanup: PASS, 27/27.
- `DiagnosticExportSnapshotTests` after tuning safety export coverage: PASS, 8/8.
- Documentation-only release checklist update: PASS by inspection; no code build required.
- Windows app Release build after tray quick-popup visible-only timer cleanup: PASS.
- Windows app Release build after tray icon timer registry coverage: PASS.
- `ReleaseGateCodeHygieneTests` after tray icon timer registry and touched bare-catch cleanup: PASS, 4/4.
- Windows app Release build after OSD overlay timer registry coverage: PASS.
- `ReleaseGateCodeHygieneTests` after OSD overlay silent-catch cleanup: PASS, 4/4.
- `BackgroundTimerRegistryTests` after undervolt/EDP monitor registry coverage: PASS, 6/6.
- `ReleaseGateCodeHygieneTests` after tuning timer registry coverage: PASS, 4/4.
- Windows app Release build after tuning timer registry coverage: PASS.
- `BackgroundTimerRegistryTests` after Temperature RGB timer registry coverage: PASS, 7/7.
- `ReleaseGateCodeHygieneTests` after Temperature RGB bare-catch cleanup: PASS, 4/4.
- Windows app Release build after Temperature RGB timer cleanup: PASS.

Known validation caveat:
- Full solution build with `--no-restore` currently fails on the Linux project assets file missing a `net8.0/win-x64` target. The Windows app, hardware worker, and tests compile before that Linux restore-assets failure. A restored full solution pass should be refreshed before release.
- Full test suite has been refreshed after the latest fan/profile/zero-RPM recovery/release-gate fixes.
- Focused `DiagnosticExportSnapshotTests` validation after adding `rgb-control-path.txt` is pending because the local dotnet invocation is currently blocked by the sandbox/user-profile usage-limit gate.

---

## Remaining Planned Work

- Capture before/after resource measurements using `resource-footprint.txt`.
- Continue startup deferral for optional providers and page-only subsystems.
- Complete tray/minimized cadence hardening and tests for cadence blockers.
- Continue RGB/provider lazy-load improvements.
- Continue expanding RGB backend fallback coverage from physical 4-zone/per-key reports.
- Revisit hardware-worker lifecycle and sensor cache policy for tray-only/static status use.
- Fill the new v3.6.0 release checklist with CPU/RAM measurements and restored Linux build validation evidence.
