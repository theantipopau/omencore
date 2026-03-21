# OmenCore Roadmap — v3.2.0

Release target: v3.2.0
Planned scope: address outstanding regressions from v3.0.x→v3.1.x, platform Linux stability, update UX, fan-curve UX, and runtime CPU/memory optimizations.

Overview
- Goal: Resolve high-severity stability and safety regressions reported by users; improve UX for fan curves; harden Linux packaging/runtime; reduce monitoring CPU/memory load.
- Timebox: milestone-based; split into triage, fixes, UI/UX, testing, release.

Priority legend
- P0: Safety or crash (must fix before broad rollout)
- P1: High-impact correctness or widely-reported functional bugs
- P2: Platform-specific or less common regressions
- P3: QoL, polish, telemetry, docs

P0 — Blockers
- Updater/auto-update failure (Download → Install fails; installer UX falls back to manual). Actions: reproduce end-to-end, capture installer exit codes, add robust logging and retry/rollback. Area: updater + `build-installer.ps1` + in-app updater.
- Linux GUI crash on startup (MissingMethodException: System.OperatingSystem.IsWindows()). Actions: reproduce on target distros, confirm packaging/runtime rid, publish self-contained Linux GUI or build proper RID. Area: `src/OmenCore.Avalonia` packaging.
- Fan mode transition thermal spike (edge cases remain). Actions: instrument ResetFromMaxMode flow, add stronger handoff guard, add telemetry & rollback safety. Area: `WmiFanController.cs`.
- Victus Windows fan lockup (GitHub #86): custom/manual fan control can leave fans stuck at 0 RPM or pinned max. Actions: treat manual 0% as BIOS-auto restore, add V2 safety floor for mixed 0% requests, expand Victus model QA matrix. Area: `WmiFanController.cs`.

P1 — High priority
- Fan curve editor UX: small draggable UI prevents precise CPU/GPU independent settings. Actions: implement numeric table editor, keyboard input for points, zoom/scale, and ensure independent curves are respected by backend. Area: `FanCurveEditor`, `FanCurveEngine`.
- Incorrect CPU/GPU temps on some Victus/OMEN (stuck or implausible values). Actions: extend fallback heuristics, add targeted sensor-sweep logs, add safe-guard thermal protection adjustments. Area: `WmiBiosMonitor`, `LibreHardwareMonitorImpl`.
- About/version mismatch (shows v3.1.0). Actions: consolidate single-source `VERSION.txt` usage at runtime and packaging. Area: VERSION.txt, assembly metadata.

P2 — Linux & platform
- Linux board support (8BA9, 8BBE, others): missing `ec_sys`/thermal_profile, partial `hp-wmi` nodes. Actions: improve `diagnose` guidance, expand board DB, add safe automated modprobe suggestions with clear warnings. Area: `LinuxEcController`, `LinuxHardwareService`, `DiagnoseCommand`.
- RPM readback variants: continue expanding board-specific calibrations (8C77 done; survey remaining boards). Area: `LinuxEcController`.
- Packaging/runtime sidecars: ensure Linux zip contains correct runtime libs and avoids MissingMethodException. Add CI test to run the GUI binary in a container. Area: `build-linux-package.ps1`.

P3 — QoL and polish
- Preset/chosen curve ignored intermittently: audit apply flow and race conditions. Area: `FanService`.
- Updater UX: surfaced error messaging and a clear manual-install flow. Area: updater UI and docs.
- Diagnostics & telemetry: improve logs for updater, fan transitions, and Linux packaging errors.
- HardwareWorker transparency: explain in-app and docs that `OmenCore.HardwareWorker.exe` is hardware monitoring/isolation (not telemetry), and provide a troubleshooting toggle for reduced monitoring frequency.

Community feedback intake (2026-03-19)
- Discord report: `OmenCore.HardwareWorker.exe` high CPU for extended periods until killed.
- Discord report: CPU temp stuck at 83C until app restart.
- Discord report: 0 RPM/silent custom profile becomes unresponsive and fans ramp randomly.
- GitHub #85: Defender detection `VulnerableDriver:WinNT/Winring0` on `OmenCore.HardwareWorker.sys`.
- GitHub #86: Victus fan control unusable when not in Auto/Max.

Implemented from feedback (code-complete, 2026-03-19)
- `OmenCore.HardwareWorker`: reduced idle/orphan polling cadence (adaptive 500ms/1500ms/3000ms) to cut background CPU when no active client is connected.
- `OmenCore.HardwareWorker`: removed redundant global update traversal before per-device updates to avoid duplicate sensor work every cycle.
- `OmenCore.HardwareWorker`: added frozen-high CPU temp self-recovery heuristic (detect prolonged high+idle stuck temps, force sensor re-probe and fallback path).
- `OmenCore.HardwareWorker`: added periodic pruning of error-rate cache entries to prevent unbounded dictionary growth in long-running sessions.
- `WmiFanController`: map manual `0%` requests to `RestoreAutoControl()` for safer firmware behavior.
- `WmiFanController`: on V2 percentage firmware, prevent single-channel manual `0%` writes by clamping to 1% safety floor when mixed values are requested.
- `Linux FanCurveEngine`: cache sorted curve points to avoid per-poll sorting and allocation churn in daemon hot path.

Additional core-system improvements identified (next implementation wave)
- Stability:
  - Add a worker heartbeat watchdog in main app that detects high CPU sustained over threshold and auto-restarts worker with diagnostic snapshot.
  - Add explicit stale-sensor status badge in UI so users can distinguish live vs recovered telemetry.
- Resource usage:
  - Replace repeated `File.AppendAllText` in high-activity paths with buffered async writer and periodic flush.
  - Batch `ObservableCollection` refreshes in `MainViewModel`/dashboard to reduce UI-thread churn on large updates.
  - Add adaptive polling profile presets (Performance/Balanced/Low overhead) exposed in settings.
- Functional correctness:
  - Add per-model parameter profile loading (advanced JSON overrides) for fan/temperature edge cases requested by power users.
  - Add stronger end-to-end verification for custom curve application with rollback to Auto when verification fails.
- Visual polish:
  - Implement fan preset save dialog in Avalonia UI and wire update-check action in settings.
  - Apply consistent spacing/typography tokens in fan/settings/dashboard panes for a more professional, unified look.

Follow-up actions not yet complete
- Driver-path hardening for #85: add optional "no vulnerable driver path" startup mode and document model-specific tradeoffs.
- Expose advanced per-model sensor/fan tuning profile file for power users (requested by community).
- Add dedicated regression tests for Victus `0%` curve transitions and worker high-CPU watchdog alerts.

GUI updates (planned)
- Fan Curve UX (P1):
  - Add numeric/table editor next to curve canvas for precise values.
  - Allow keyboard edit of selected point (arrow keys + numeric entry).
  - Add zoom/scale and point snapping toggle.
  - Add a compact preset import/export JSON UI.
- About dialog (P1): read `VERSION.txt` at runtime; show release tag and checksum links.
- Update banner/update flow (P0/P3): clearer progress and error codes; copy installer path and manual-install steps.

CPU & Memory optimizations (planned)
- Reduce monitoring loop pressure:
  - Re-evaluate high-frequency timers; keep adaptive polling but tighten guards for steady-state.
  - Consolidate sensor queries to avoid duplicate reads (merge NVAPI/LHM/WMI scheduling where possible).
- Reduce allocations in hot paths:
  - Reuse objects (PerformanceCounter, sample buffers) instead of re-allocating per poll.
  - Use pooled buffers for telemetry formatting and logging.
- EC and firmware write rate-limiting:
  - Maintain existing 15s dedupe and extend to any high-frequency code paths discovered during triage.
- Memory usage:
  - Audit long-lived caches and large collections; lazily load optional modules.
  - Reduce per-sensor history retention default (configurable) and provide a low-memory profile.

Milestones
- M1 — Triage & repro (3 days): repro P0 issues locally or collect reproducible steps from users; add failing tests.
- M2 — Core fixes (7 days): implement P0 fixes + CI packaging updates for Linux.
- M3 — UI/UX & optimizations (5 days): fan curve editor, about/version sync, CPU/memory optimizations.
- M4 — QA & Canary (3 days): internal canary builds, diagnostics validation, updater end-to-end test.
- M5 — Release prep & docs (2 days): update changelog, release notes, Discord post.

Testing & validation
- Create reproducible test matrix: Windows (Win10/11 secure boot), Linux (CachyOS/Arch/Ubuntu 22.04), variety of board IDs (8BAD, 8BA9, 8C77, 8BBE).
- Add CI step: run `build-linux-package.ps1` and execute `./omencore-gui --version` in a container to detect MissingMethodException early.
- Add updater E2E test: download artifact + launch installer in sandbox; capture exit codes; ensure UI surfaces clear failure reasons.

Owners & reviewers (suggested)
- Release owner: @Matt (repo admin)
- Linux packaging: `src/OmenCore.Linux` team
- Fan UX: `src/OmenCoreApp/Controls` UI team
- Monitoring & perf: `WmiBiosMonitor` owner
- QA: run canary builds and gather logs

Checklist (ready-to-open issues)
- [ ] Create issue: updater E2E failure (attach logs + repro)
- [ ] Create issue: Linux GUI crash (attach publish logs + runtime stderr)
- [ ] Create issue: Fan curve editor numeric mode (UI enhancement)
- [ ] Create issue: Packaging CI smoke-test + distro matrix

Code TODOs & GUI feature gaps (discovered in source)
- Fan control: save preset dialog not implemented (`FanControlViewModel`).
- Settings: persistent config load/save and theme/accent handling (`SettingsView.axaml.cs`).
- Hardware sync: apply fan profile and manual speeds via hardware service (`FanControlView.axaml.cs`).
- Lighting: apply key lighting via service (`KeyboardView.axaml.cs`).
- Update check: settings view has TODO for update check handler.

Notes
- Keep changelogs short; use this roadmap as the single source for v3.2.0 planning.
- For any patch that affects firmware/EC writes, add an acceptance test and explicit rollback/hard-safe defaults.

**Prioritized Improvements — Additions for v3.2.0**
- **Build reliability & CI**: disable `PublishSingleFile` in build scripts and CI, keep PE/header validation and fail-fast checks to prevent corrupted installers. Update: `build-installer.ps1`, `build-linux-package.ps1`, `.github/workflows/*.yml`.
- **Packaging consistency (Linux & Windows)**: avoid single-file Linux publishes; add distro/runtime checks, a CI packaging matrix, and a lightweight container smoke-test that runs `./omencore-gui --version` to detect MissingMethodException early.
- **Monitoring hot-path optimizations**: reuse a singleton `PerformanceCounter`, reuse sample buffers, and pool frequently-used objects to reduce allocations and GC pressure in `UpdateReadings()` and monitoring service loops.
- **Configurable polling & low-overhead mode**: expose per-policy polling intervals in config and UI; add a documented low-overhead mode (e.g., 5s) and an adaptive steady-state throttle.
- **UI performance & memory**: batch `ObservableCollection` updates, enable virtualization for heavy lists, reduce per-frame allocations (animations, timers), and audit long-lived subscriptions in views.
- **Fan-curve UX improvements**: implement a numeric/table editor and keyboard navigation for curve points, add import/export JSON presets, point snapping, and explicit auto/manual handoff indicators in `FanCurveEditor` and `FanService`.
- **Linux hardware reliability**: centralize board-specific EC calibration table, improve diagnostics for missing `ec_sys`/hwmon nodes, and add safe modprobe suggestions + clear warnings in `LinuxEcController`.
- **Updater & installer robustness**: add updater E2E tests, richer installer exit-code logging, retry/rollback strategies, and clearer UI messages for failure cases.
- **Telemetry & lightweight profiling**: add opt-in, low-overhead runtime probes (sampling-based) to capture allocation/CPU hotspots; collect anonymized metrics to prioritize optimizations.
- **Testing & CI expansion**: add focused unit/integration tests for updater and Linux GUI runtime, and expand CI to run packaging + container smoke-tests across targeted RIDs/distros.
- **Developer experience & cleanup**: triage and close TODO/FIXME items, apply null-forgiving (`= null!`) only where deferred initialization is intended, and add issue links for follow-ups.
- **Docs & release ergonomics**: add a QA canary checklist, an automated SHA256 embed step in packaging scripts, and a contributor packaging guide.

Checklist (ready-to-open issues)
- [ ] Create issue: updater E2E failure (attach logs + repro)
- [ ] Create issue: Linux GUI crash (attach publish logs + runtime stderr)
- [ ] Create issue: Fan curve editor numeric mode (UI enhancement)
- [ ] Create issue: Packaging CI smoke-test + distro matrix

---

Generated: 2026-03-19
