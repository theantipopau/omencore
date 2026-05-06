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
- 3.6.0 bug fix: Fixed Auto handoff regression where `Auto` presets with explicit curve payloads could be overwritten by BIOS-default restore after `Max`/policy transitions. `FanService` now preserves controller-applied Auto payloads instead of forcing `RestoreAutoControl()` when a curve payload is present.
- 3.6.0 bug fix: Fixed custom curve delete command requery behavior so selecting a removable preset immediately re-enables Delete without requiring additional UI activity.
- 3.6.0 bug fix: Hardened GPU power boost verification by splitting payload/readback semantics for `Minimum`, `Medium`, `Maximum`, and `Extended`. Accepted-but-ignored writes now fail verification instead of reporting false success.
- 3.6.0 bug fix: Added explicit `Extended` GPU mode mapping in both WMI and OGH paths, including startup detection/readback and restore flows.
- 3.6.0 bug fix: Improved WMI fan `100%` behavior for custom/manual paths by mapping full-speed requests to protocol ceiling, allowing firmware clamp to true hardware max instead of capping to detected classic max level.
- 3.6.0 optimization: Continued M2/M5 tray-only cadence work so low-overhead + tray-only sessions can settle to the 10s ultra-low cadence when no fan/OSD blockers are active, with explicit diagnostic reason text and transition telemetry coverage.
- 3.6.0 optimization: Deferred conflict/tuning software scan loops until Monitoring, OMEN, Tuning, or Optimizer tabs are actually opened, avoiding unconditional startup scans for Afterburner/RTSS/XTU/FanControl detection.
- 3.6.0 optimization: Added adaptive static-tray sampling so the shared monitoring pipeline can keep core telemetry alive while reducing expensive GPU telemetry refreshes during low-overhead tray-only sessions.
- 3.6.0 cleanup: Removed the obsolete runtime polling-interval path and retired the old polling-profile / polling-interval settings UX. Settings now reflects the real automatic cadence policy while still normalizing legacy config fields for compatibility.
- 3.6.0 diagnostics/UX: Settings now shows a live monitoring cadence card with the current tier, cadence reason, and the active blockers preventing tray-only ultra-low cadence, using the existing unified cadence telemetry instead of adding parallel state.
- 3.6.0 regression coverage: Added/expanded tests across WMI verification, fan preset/Auto handoff behavior, fan smoothing/diagnostic mode state cleanup, fan command UI command-state requery, GPU power semantics, fan-level mapping, and monitoring cadence telemetry.
- 3.6.0 regression coverage: Added tests for deferred conflict monitoring startup, adaptive static-tray sampling policy, compatibility persistence for legacy monitoring config values after the settings cleanup, and the new live cadence tier/blocker summaries in Settings.

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
```

Observed outcomes:
- `DiagnosticExportSnapshotTests`: PASS, 4/4.
- `MainViewModelTests`: PASS, 5/5.
- Combined targeted 3.6 regression slice (`FanPresetVerificationTests|MainViewModelTests|DiagnosticExportSnapshotTests`): PASS, 20/20.
- Focused optimization regression slice (`MainViewModelTests|HotkeyAndMonitoringTests`): PASS, 38/38.
- `SettingsViewModelTests`: PASS, 6/6.
- Full `OmenCoreApp.Tests` suite (`--no-restore`): PASS, 447/447.
- Windows app Release build: PASS.

Known validation caveat:
- Full solution build with `--no-restore` currently fails on the Linux project assets file missing a `net8.0/win-x64` target. The Windows app, hardware worker, and tests compile before that Linux restore-assets failure. A restored full solution pass should be refreshed before release.

---

## Remaining Planned Work

- Capture before/after resource measurements using `resource-footprint.txt`.
- Continue startup deferral for optional providers and page-only subsystems.
- Complete tray/minimized cadence hardening and tests for cadence blockers.
- Continue RGB/provider lazy-load improvements.
- Revisit hardware-worker lifecycle and sensor cache policy for tray-only/static status use.
- Add release-gate checklist rows for CPU/RAM measurements and Linux restored build validation.
