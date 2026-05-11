# OmenCore v3.6.0 Roadmap

## Scope Source
This roadmap captures all forward-looking and deferred items moved out of the v3.5.0 changelog so v3.5.0 remains implementation/evidence focused.

## Planning Kickoff
- Planning branch: `v3.6.0-planning`.
- Primary theme: make OmenCore feel genuinely lightweight when idle, minimized, or used only for tray/OSD monitoring, without weakening active fan, tuning, RGB, or diagnostics reliability.
- First rule for 3.6 work: measure before claiming savings. Every resource optimization should include before/after evidence for visible-window idle, tray-only idle, OSD-visible, fan-hold active, and lighting/tuning pages opened.

## Bug Fix Progress
- Implemented: Custom curve delete command now requeries when the selected saved preset changes, fixing disabled delete buttons for removable curve presets.
- Implemented: WMI `Auto` presets that preserve a non-default thermal policy now keep the countdown hold alive, and `FanService` no longer overwrites explicit Auto curve payloads with a BIOS-default restore after `Max` -> `Auto` transitions.
- Implemented: Explicit Auto curve payloads now keep the curve engine active instead of leaving fan ownership ambiguous, improving fan ramp-down after Max/policy transitions.
- Implemented: GPU power boost writes now use distinct payload/readback validation for `Minimum`, `Medium`, `Maximum`, and `Extended`; accepted-but-ignored BIOS writes now fail instead of reporting false success.
- Implemented: Custom curve and manual fan `100%` writes now use the same protocol ceiling as Max mode so classic WMI systems are not capped at the detected `55` level when BIOS can clamp higher.
- Implemented: Built-in Auto/Gaming/Extreme fan curves are rebalanced so moderate 70-80C operation does not ramp into near-Max behavior; explicit Max remains the 100% full-cooling mode.
- Implemented: Removed the stale runtime Extreme override that forced 100% fans at 75C despite the rebalanced curve definition.
- Implemented: Stale Extreme preset UI copy now matches the rebalanced curve and no longer advertises 100% fan duty at 75C.
- Implemented: Game profile fan/performance actions now apply through services even when Fan Control/System Control pages are still lazy-unloaded, preventing profile no-ops from the 3.6 startup deferral work.
- Implemented: Custom curves now recover when fan-level writes report success but RPM remains at zero by issuing a bounded one-shot wake pulse, replacing the manual "switch curves to wake fans" workaround reported against v3.5.0.
- Implemented: Release-gate hygiene cleanup replaced newly surfaced bare catches in Settings and WMI monitoring cleanup paths with typed/logged exception handling.
- Validation pending: Physical RTX 4050 / OMEN 16-xd0xxx and Victus fan/RPM confirmation under sustained load.
- [X] GitHub #125 "Fans either go max or zero": Custom fan curves now detect accepted-write-zero-RPM failure and send bounded wake pulse instead of requiring manual mode switching.

## Carry-Over Reliability Work

### Fan and Profile State
- [X] Single desired fan-state owner across fan service/controller paths with explicit Auto, Performance, MaxHold, ManualCurve, Transitioning states - implemented via Fan Control ownership panel and state tracking.
- [~] Continue tightening requested vs confirmed behavior across sidebar, tray, fan page, OMEN/system page, startup restore, and hotkey flows - initial fan ownership clarification complete; ongoing validation.
  - Continued by hardening global hotkey registration de-duplication and conflicting-chord rejection, reducing startup/retry timing cases where hotkeys could become flaky or ambiguous.
- [X] Additional regression coverage for profile transitions with fan/performance link on and off - lazy game-profile fan/performance apply coverage implemented.
- [X] Rebalance aggressive built-in fan curves (especially Gaming/Extreme) so moderate thermals do not ramp to near-max fan speed unnecessarily - Auto/Gaming/Extreme rebalance implemented and validated via FanControlViewModelTests.
- [~] Fix remaining `Auto`/thermal-policy handoff regressions on affected WMI BIOS laptops so `Max` -> `Silent`/`Custom` -> `Auto` transitions do not collapse GPU power back to low limits - Auto handoff regression fixed for curve payloads; continued physical validation pending.
- [~] Validate that `Minimum`, `Medium`, `Maximum`, and `Extended` GPU power modes produce distinct confirmed behavior on affected RTX 4050 laptop hardware - distinct payload/readback validation implemented; physical RTX 4050 validation pending.
- [X] Resolve custom curve profile management bugs, including the reported disabled delete action for removable curve presets - custom curve delete command requery fix implemented.
- [~] Improve spin-down/state-release behavior after custom curves and preset holds so fans can ramp back down - Auto-with-curve ownership fix implemented; continued physical validation pending.

### Linux Hold and Capability UX
- [X] Continue daemon hold hardening for board and kernel variance - performance-hold readback now treats Default/Balanced as equivalent and only re-applies on true drift.
- [X] Improve Linux capability diagnostics for root/write path, hp-wmi/ec_sys/debugfs, and package/service prerequisites - diagnose output for systemd availability, service unit state, config presence, and bundle extraction readiness implemented.
- [X] Improve Linux kernel/firmware diagnostics for NVIDIA/SBIOS ACPI platform-request failures - issue #123-style `nvidia-powerd` Dynamic Boost disable detection and board `8D41` guidance for OMEN Max 16-ah0xxx RTX 50-series TGP caps added.
- [X] Complete first-class service status/install/remove guidance in Linux packaging and docs - systemd unit generation centralized, `daemon --status` expanded with performance-hold settings, manual README service instructions replaced with built-in daemon commands.

### RGB Control Robustness
- [X] Expand backend matrix and fallback sequencing for affected 4-zone systems - keyboard lighting now retries model-specific fallback backends when the active 4-zone backend fails.
  - Completed follow-up hardening by serializing backend operations to prevent concurrent apply/brightness/backlight races and by adding brightness/backlight fallback retries with active-backend swap on success.
- [X] Add explicit ownership state for OmenCore vs OMEN Light Studio/OmenCap contention - Lighting ownership header now reports HP keyboard backend and provider status.
- [X] Add a dedicated restore action for keyboard lighting fallback/recovery - Lighting restore action implemented with same backend zone ordering as manual Apply and startup restore.

## Resource and Optimization Tracks

### Idle CPU and RAM Efficiency
- Add a lightweight resource snapshot to diagnostics export: OmenCore process CPU percent, private bytes, working set, handle/thread count, hardware-worker process footprint, active timer registry entries, current monitoring cadence reason, and optional provider load state.
- Establish explicit resource budgets after baseline capture. Track at minimum: cold startup to first usable UI, visible dashboard idle, minimized tray-only idle, OSD-visible cadence, fan curve/hold active, and Lighting/Tuning page activation.
- Audit startup eagerness. `App.xaml.cs` currently triggers Dashboard and SystemControl construction during startup; 3.6 should move synchronous GPU/tuning/provider work behind first-use or a cheap capability summary where possible.
- Revisit hardware-worker lifecycle. Prefer one shared worker and cached hardware sample state, but avoid keeping expensive sensors hot when no UI, OSD, fan curve, fan hold, logging export, or active tuning workflow needs realtime telemetry.
- Consolidate timer ownership through `BackgroundTimerRegistry` where practical. Candidates include hardware monitoring, memory optimizer refresh, settings schedule enforcement, thermal chart redraw throttles, RGB/audio-reactive loops, and tray status refresh.
  - Continued by making the tray quick-popup telemetry refresh a visible-only registered timer that starts only while the popup is shown and unregisters on hide/close.
  - Continued by registering the main tray icon 2s refresh loop, with diagnostics text that distinguishes live temperature-badge redraws from static-icon tooltip/menu refreshes.
  - Continued by registering OSD overlay stats and network refresh timers as visible-only work, so OSD-visible benchmark exports show the overlay's 1s stats loop and optional 5s network polling explicitly.
  - Continued by registering tuning safety loops for CPU undervolt status polling and EDP throttling mitigation, making page-loaded tuning monitors visible in resource exports.
  - Continued by registering temperature-reactive RGB polling as optional background work and tightening its cancellation-token disposal on stop.
- Expand provider lazy-load boundaries. RGB/peripheral providers, OpenRGB/Razer/Logitech/Corsair process checks, NVAPI/Afterburner telemetry, optimizer verification, and tuning conflict scans should run on page entry, explicit action, or scheduled low-frequency refresh rather than unconditional startup.
- Reduce steady-state allocations. Cap in-memory event/log/chart buffers by count and age, reuse monitoring sample DTOs where safe, and avoid repeated LINQ-heavy projections in high-cadence paths.
  - Started by count-capping dashboard hardware metrics history at 7,200 entries while retaining the existing 24h age cap, and replacing the per-sample power-trend `TakeLast().ToList().Average()` allocation with a bounded reverse loop.
  - Continued by avoiding per-render sample `ToList()` copies in Load, Thermal, and GPU voltage/current charts when the bound sample source is already indexable, and by replacing GPU voltage/current min/max LINQ projections with a single bounded scan.
- Make "low overhead mode" visible and testable: show the current cadence tier, active blockers that prevent ultra-low cadence, and last reason a subsystem woke up.

### 3.6 Lightweight Milestones
- [X] M0 - Measurement: add resource diagnostics export and a repeatable manual benchmark checklist - `resource-footprint.txt` implemented in diagnostics export with qa/v3.6.0-checklist.md.
- [X] M1 - Startup diet: defer nonessential Dashboard/SystemControl/provider initialization - tray-startup lazy loads removed, Dashboard/General no longer force SystemControl construction.
- [X] M2 - Tray idle: make tray-only idle settle into the lowest safe cadence when no fan curve, hold, OSD, or diagnostics work is active - FanActivityStateChanged event added, tray-only ultra-low cadence reaches 10s.
- [X] M3 - Provider laziness: ensure optional RGB, tuning, optimizer, and peripheral integrations do not probe until the user opens or invokes those areas - Corsair/Logitech/Razer/OpenRGB startup discovery removed, conflict scans deferred to tab open.
- [X] M4 - Worker and cache policy: keep one authoritative hardware sample pipeline, but allow lower-frequency or suspended expensive sensors when only static tray status is needed - adaptive static-tray sampling implemented, obsolete polling-interval runtime paths removed.
- [X] M5 - Regression guardrails: add tests for cadence blockers and diagnostic evidence, plus a release checklist row for CPU/RAM before/after measurements - BackgroundTimerRegistry coverage added, live cadence card in Settings, v3.6.0-checklist.md created.

### Fan and Performance Reliability
- Expand readback-first verification for fan and power-limit paths.
- Extend bounded command-history and external-reset evidence/reporting.
- Add deeper tests around V1 WMI transitions, max hold, and default handoff behavior.
- Audit custom fan curve percentage semantics versus firmware fan-level semantics on WMI/V1 systems so UI labels, diagnostics, and applied writes all agree on whether points are true percentages, raw fan levels, or model-scaled ceilings.
- Verify that `Max` and custom-curve `100%` requests can reach model-appropriate CPU/GPU RPM ceilings where supported, and surface explicit firmware/backend clamp messaging when BIOS accepts the request but caps RPM lower than expected.
- Add targeted validation for the reported Victus/OMEN cases where `Max` fan mode does not engage correctly, custom curves stop around ~5500 RPM instead of full hardware max, or diagnostics show misleading `100% requested` results with low measured RPM.
- Improve fan curve editor drag handling so nodes remain responsive near dense points and boundary constraints. Initial canvas-level capture and point-reference tracking fix is implemented; continue hands-on UX validation.
- Revisit fan-curve safety clamp UX: consider replacing hard clamping with explicit warnings/confirmation where safe, while keeping thermal-protection guardrails for dangerous curves. Initial requested-vs-effective preview and warning refresh fix is implemented so safety floors are visible when active.

### RGB Reliability
- Add per-backend control-path diagnostics in UI/export. Initial diagnostics export coverage is implemented via `rgb-control-path.txt` without forcing lazy provider initialization.
- Continue provider lazy-load and startup probe minimization.
- Expand ownership visibility for HP keyboard and external RGB controllers. Initial Lighting header panel now shows active RGB ownership, HP keyboard backend, provider status details, and OMEN Light Studio/Gaming Hub conflict warnings.
- Continue RGB loop/resource visibility. Temperature-reactive RGB polling is now registered in diagnostics while enabled and stops/disposes its cancellation token cleanly.

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
  - Continued by adding a persisted Auto Clean minimum-gap override while keeping `0` as the profile-default cooldown path.
- Continue game-aware quiet-window refinement to reduce stutter risk.
  - Continued by making auto-clean's quiet-window path monitor-aware for fullscreen/borderless foreground windows and by limiting those sessions to working-set trims unless memory pressure is critical.
  - Continued by exposing the quiet-window behavior in the Auto Clean UI with persisted config, so users can opt out of the game-safe path when they intentionally want full cleanup during foreground games.
- Add richer before/after metrics for commit/standby/cache/paging impact.
  - Continued by extending cleanup results and the UI copyable last-clean summary with physical used/available, standby, cache, commit, page file, and modified-page deltas.
- Improve process exclusion suggestions and guidance text.
  - Completed by adding dynamic exclusion guidance that recommends currently high-memory processes not yet excluded, then automatically updates as exclusions are added/removed.
- Implemented first refresh-loop cleanup: the live top-process table no longer resolves every executable path every 2s, and cleanup preview estimates no longer perform a second memory/process snapshot during the same tick.
- Implemented first exclusion-guidance improvement: top-memory processes can be added directly to working-set exclusions from the context menu, avoiding manual copy/type flows for games, capture tools, chat apps, and anti-cheat helpers.

## Tuning Safety and Verification
- Continue requested/applied/verified separation for all tuning surfaces.
- Extend conflict detection matrix and mitigation guidance.
- Add additional test-mode rollback triggers and event-based safety checks.
- Continue model-aware defaults and write/readback capability gating.
  - Continued by making exact ProductId capability profiles win over broad model-name patterns unless the ProductId is explicitly ambiguous, with regression coverage for `8A43` OMEN 16-n0xxx vs sibling `8A44` and shared `8BB1` Victus/OMEN disambiguation.
  - Continued by adding a safer ProductId-missing `16-am0xxx` Intel Core Ultra fallback for GitHub #124, while preserving exact `8D2F` AMD resolution.
- Expand exportable tuning safety report coverage.
  - Continued by adding `tuning-safety.txt` to diagnostics export with saved startup restore gates, CPU undervolt/Curve Optimizer pending-test recovery state, GPU OC pending-test recovery state, last-confirmed values, and AMD STAPM/Tctl limits without waking hardware tuning providers.
  - Continued by registering active undervolt polling and EDP mitigation loops so tuning safety exports/resource snapshots can show the background monitors that are actually running.

## UX and Visual Polish
- Add compact screenshot-friendly diagnostics surfaces where needed.
  - Continued by clarifying Model Identity diagnostics: OmenCore now labels baseboard ProductId separately from HP support/catalog SKU so Discord/GitHub reports can include both without confusing capability lookup.
- Continue fan/profile wording and status clarity improvements. Continued by adding a Fan Control ownership panel that explains whether firmware, OmenCore Max, constant duty, or a managed curve currently owns fan behavior.
- Continue fan calibration safety and recovery polish.
  - Continued by restoring BIOS auto fan control in a calibration cleanup path after completion, cancellation, or failure so the final 100% step does not leave fans pinned at Max.
  - Continued by adding a manual Restore Auto action to the calibration wizard for one-click recovery if fans still sound pinned high.
- Deliver first-party visual asset updates (dashboard, fan, performance, RGB, installer). Initial 3.6 visual pass added shared vector motifs and regenerated installer wizard bitmaps from the refreshed red/blue generator.
- Expand status iconography for confirmed/degraded/blocked/overwritten states. Initial implementation adds reusable status glyph/badge styles and wires them into Dashboard health, Fan Control ownership, RGB sync/ownership, and Performance/GPU power surfaces.

## Release Readiness Dependencies
- [~] Physical fan/RGB validation on affected OMEN hardware - code-level fixes in place; physical testing on Victus/OMEN 16-xd0xxx/8A43/8D41 models pending.
- [~] Linux long-hold validation on target board/kernel combinations - daemon hold hardening ongoing.
- [~] Tray/minimized cadence before/after measurement evidence - v3.6 QA checklist defines required CPU/RAM and `resource-footprint.txt` evidence rows; physical measurement data capture pending.
- [X] Windows Release build validation after final merge.
- [ ] Linux build restore and validation (`net8.0/win-x64` target asset issue resolved).
- [X] Full test suite pass with `dotnet test OmenCore.sln --no-restore`.
- [X] Documentation sync: CHANGELOG.md, INSTALL.md, README.md version references.
- [X] Installer build and smoke test.
- [~] Portable zip extraction and launch test on clean environment - archive extraction verified; real desktop smoke exposed a Settings live-cadence binding crash and stale `3.5.0` metadata in packaged binaries. Both are now fixed in source; rerun against a rebuilt portable package is still pending.
