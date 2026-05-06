# OmenCore v3.6.0 Roadmap

## Scope Source
This roadmap captures all forward-looking and deferred items moved out of the v3.5.0 changelog so v3.5.0 remains implementation/evidence focused.

## Planning Kickoff
- Planning branch: `v3.6.0-planning`.
- Primary theme: make OmenCore feel genuinely lightweight when idle, minimized, or used only for tray/OSD monitoring, without weakening active fan, tuning, RGB, or diagnostics reliability.
- First rule for 3.6 work: measure before claiming savings. Every resource optimization should include before/after evidence for visible-window idle, tray-only idle, OSD-visible, fan-hold active, and lighting/tuning pages opened.

## Carry-Over Reliability Work

### Fan and Profile State
- Single desired fan-state owner across fan service/controller paths with explicit Auto, Performance, MaxHold, ManualCurve, Transitioning states.
- Continue tightening requested vs confirmed behavior across sidebar, tray, fan page, OMEN/system page, startup restore, and hotkey flows.
- Additional regression coverage for profile transitions with fan/performance link on and off.
- Rebalance aggressive built-in fan curves (especially Gaming/Extreme) so moderate thermals do not ramp to near-max fan speed unnecessarily; keep Max as the explicit full-cooling mode.

### Linux Hold and Capability UX
- Continue daemon hold hardening for board and kernel variance.
- Improve Linux capability diagnostics for root/write path, hp-wmi/ec_sys/debugfs, and package/service prerequisites.
- Complete first-class service status/install/remove guidance in Linux packaging and docs.

### RGB Control Robustness
- Expand backend matrix and fallback sequencing for affected 4-zone systems.
- Add explicit ownership state for OmenCore vs OMEN Light Studio/OmenCap contention.
- Add a dedicated restore action for keyboard lighting fallback/recovery.

## Resource and Optimization Tracks

### Idle CPU and RAM Efficiency
- Add a lightweight resource snapshot to diagnostics export: OmenCore process CPU percent, private bytes, working set, handle/thread count, hardware-worker process footprint, active timer registry entries, current monitoring cadence reason, and optional provider load state.
- Establish explicit resource budgets after baseline capture. Track at minimum: cold startup to first usable UI, visible dashboard idle, minimized tray-only idle, OSD-visible cadence, fan curve/hold active, and Lighting/Tuning page activation.
- Audit startup eagerness. `App.xaml.cs` currently triggers Dashboard and SystemControl construction during startup; 3.6 should move synchronous GPU/tuning/provider work behind first-use or a cheap capability summary where possible.
- Revisit hardware-worker lifecycle. Prefer one shared worker and cached hardware sample state, but avoid keeping expensive sensors hot when no UI, OSD, fan curve, fan hold, logging export, or active tuning workflow needs realtime telemetry.
- Consolidate timer ownership through `BackgroundTimerRegistry` where practical. Candidates include hardware monitoring, memory optimizer refresh, settings schedule enforcement, thermal chart redraw throttles, RGB/audio-reactive loops, and tray status refresh.
- Expand provider lazy-load boundaries. RGB/peripheral providers, OpenRGB/Razer/Logitech/Corsair process checks, NVAPI/Afterburner telemetry, optimizer verification, and tuning conflict scans should run on page entry, explicit action, or scheduled low-frequency refresh rather than unconditional startup.
- Reduce steady-state allocations. Cap in-memory event/log/chart buffers by count and age, reuse monitoring sample DTOs where safe, and avoid repeated LINQ-heavy projections in high-cadence paths.
- Make "low overhead mode" visible and testable: show the current cadence tier, active blockers that prevent ultra-low cadence, and last reason a subsystem woke up.

### 3.6 Lightweight Milestones
- [x] M0 - Measurement: add resource diagnostics export and a repeatable manual benchmark checklist.
  - Started with `resource-footprint.txt` in diagnostics export: app/worker process footprint, monitoring cadence, fan blockers, active timers, GC/runtime state, and optional subsystem load hints.
- [~] M1 - Startup diet: defer nonessential Dashboard/SystemControl/provider initialization and document any features that must remain eager for safety.
  - Started by removing tray-startup `Dashboard`/`SystemControl` forced lazy loads and preventing Dashboard/General from constructing SystemControl as a side effect.
- [~] M2 - Tray idle: make tray-only idle settle into the lowest safe cadence when no fan curve, hold, OSD, or diagnostics work is active.
  - Started by adding a fan curve/hold activity signal so minimized cadence is recalculated when fan ownership starts or stops, not only when the window is hidden/restored.
- [ ] M3 - Provider laziness: ensure optional RGB, tuning, optimizer, and peripheral integrations do not probe until the user opens or invokes those areas.
- [ ] M4 - Worker and cache policy: keep one authoritative hardware sample pipeline, but allow lower-frequency or suspended expensive sensors when only static tray status is needed.
- [ ] M5 - Regression guardrails: add tests for cadence blockers and diagnostic evidence, plus a release checklist row for CPU/RAM before/after measurements.

### Fan and Performance Reliability
- Expand readback-first verification for fan and power-limit paths.
- Extend bounded command-history and external-reset evidence/reporting.
- Add deeper tests around V1 WMI transitions, max hold, and default handoff behavior.

### RGB Reliability
- Add per-backend control-path diagnostics in UI/export.
- Continue provider lazy-load and startup probe minimization.
- Expand ownership visibility for HP keyboard and external RGB controllers.

## Optimizer and Cleanup Tracks

### System Optimizer
- Persist restore manifest/backup flow explicitly across restarts.
- Expand preflight to include exact operation-level impact and reversibility details.
- Keep risk-tier profile separation explicit and user-driven.
- Improve drift explanation coverage and export/report fidelity.
- Add hardware-aware recommendations (battery state, storage class, build/edition).

### Bloatware Manager
- Continue dependency metadata enrichment and risk hints.
- Add startup/scheduled-task quarantine path before destructive removal.
- Improve post-removal verification granularity (current user, all users, provisioning).
- Continue curated preset refinement for standalone prep workflows.

### Memory Optimizer
- Expand pressure-aware triggers and cooldown tuning.
- Continue game-aware quiet-window refinement to reduce stutter risk.
- Add richer before/after metrics for commit/standby/cache/paging impact.
- Improve process exclusion suggestions and guidance text.

## Tuning Safety and Verification
- Continue requested/applied/verified separation for all tuning surfaces.
- Extend conflict detection matrix and mitigation guidance.
- Add additional test-mode rollback triggers and event-based safety checks.
- Continue model-aware defaults and write/readback capability gating.
- Expand exportable tuning safety report coverage.

## UX and Visual Polish
- Add compact screenshot-friendly diagnostics surfaces where needed.
- Continue fan/profile wording and status clarity improvements.
- Deliver first-party visual asset updates (dashboard, fan, performance, RGB, installer).
- Expand status iconography for confirmed/degraded/blocked/overwritten states.

## Release Readiness Dependencies
- Physical fan/RGB validation on affected OMEN hardware.
- Linux long-hold validation on target board/kernel combinations.
- Tray/minimized cadence before/after measurement evidence.
