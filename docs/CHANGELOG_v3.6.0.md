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

---

## Validation Snapshot

Commands run during early 3.6.0 development:

```powershell
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter DiagnosticExportSnapshotTests --verbosity quiet
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter MainViewModelTests --verbosity quiet
dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --filter "FanPresetVerificationTests|MainViewModelTests|DiagnosticExportSnapshotTests" --verbosity quiet
dotnet build src\OmenCoreApp\OmenCoreApp.csproj -c Release --no-restore
```

Observed outcomes:
- `DiagnosticExportSnapshotTests`: PASS, 4/4.
- `MainViewModelTests`: PASS, 3/3.
- Combined targeted 3.6 regression slice (`FanPresetVerificationTests|MainViewModelTests|DiagnosticExportSnapshotTests`): PASS, 18/18.
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
