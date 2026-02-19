# OmenCore v3.0.0 — Work in progress (Unreleased)

**Status:** In development — do not publish. 3.0.0 will collect stability and regression fixes discovered in v2.9.1 and follow-ups.

---

## Completed fixes (working branch / PRs)

The following regressions have been implemented in the working branch and are covered by unit tests and view-level checks where applicable:

- Hotkeys / WMI reconciliation — fixed Fn+Brightness (F2/F3) false-positives by preferring the low-level keyboard hook and suppressing overlapping WMI events. Files: `OmenKeyService.cs`. Tests: Hotkey/WMI unit checks added.

- Monitoring & telemetry stabilization — transient `0W`/`0°C` spikes are suppressed and a "last sample age" health indicator exposed. Files: `WmiBiosMonitor.cs`, `HardwareMonitoringService.cs`. Tests: unit coverage added.

- Fan control & quick-profiles — seeded last‑seen RPMs, added confirmation counters for large RPM deltas, prevented preset application during diagnostics, and added atomic preset verification + rollback when controller state does not match expected behavior.
  - Files: `FanService.cs`, `WmiFanController.cs`, `FanControllerFactory.cs`.
  - Tests: `FanSmoothingTests`, `FanPresetVerificationTests` (unit).

- Model capability database — added `OMEN MAX 16 (ak0003nr)` entry with ThermalPolicy V2 handling; recommend WMI‑only fan control for this series (avoid legacy EC writes). Files: `ModelCapabilityDatabase.cs`, `HpWmiBios.cs`.
  - Tests: `ModelCapabilityDatabaseTests` (unit)

- Keyboard lighting model DB — added ProductId `8BD5` (HP Victus 16, 2023) and `8A26` (HP Victus 16, 2024) to ensure per‑zone ColorTable is applied instead of generic Victus fallback. Files: `KeyboardModelDatabase.cs`, `KeyboardLightingServiceV2.cs`.
  - Tests: `KeyboardModelDatabaseTests` (updated)

- Diagnostics & reporting UX — added Monitoring Diagnostics panel and `Report model` flow (creates diagnostics ZIP and copies model info to clipboard). Implemented `ModelReportService` and added unit + view-binding checks. Files: `DiagnosticsView.xaml`, `MainViewModel.cs`, `Services/ModelReportService.cs`. Tests: `ModelReportServiceTests` + diagnostics view‑binding assertion.

---

## Notes
- Fix: Installer now skips embedded PawnIO installer when PawnIO is already present on the target system to avoid redundant installs and incorrect task-switch behavior.
- Do not build or publish a 3.0.0 installer yet — this file tracks in-progress fixes and will be finalized for RC once core regressions are closed.
- Maintain incremental changelog entries here for each PR that targets 3.0.0.

---

## Recent commits included in this work-in-progress
- Suppress WMI OMEN event watcher when low-level keyboard hook is active
- Ignore WMI OMEN events if low-level hook present to prevent brightness key false positives
- Force UI updates when CPU/GPU power changes to avoid transient 0W displays
- Add CI integration test for quick‑profile switching (prevents transient RPM/0‑RPM UI regressions)

(Will append PR references and test results as fixes are validated.)

---

## Regression analysis (2.8.x → 2.9.1)
- Fn+Brightness (F2/F3) false positives — cause: overlapping WMI event handling + a fail‑open extraction path in the WMI handler. Status: fixed (prefer low‑level hook + fail‑closed WMI), added unit + integration checks.
- Transient 0W / 0°C telemetry — cause: an overly broad early‑exit guard in `WmiBiosMonitor` plus brittle fallback confirmation logic; also missing RAPL/MSR wiring in some paths. Status: stabilized (retain-last-on‑transient, added RAPL MSR reads where available) and improved health degradation reporting.
- Fan RPM spikes / incorrect MaxFanLevel mapping — cause: heuristic-based MaxFanLevel auto-detection + aggressive immediate acceptance of readback values. Status: fixed (seed last‑seen RPMs, require multi‑sample confirmation for large non‑zero changes, removed unsafe auto-detection heuristics).
- Diagnostic tool being overridden by presets — cause: ApplyPreset executed while diagnostics active. Status: fixed (preset apply ignored during diagnostic mode) and covered by unit tests.
- Keyboard zones not applied on some Victus/OMEN models — cause: missing ProductId/model DB entries. Status: fixed (added ProductId `8BD5`) and expanded model‑DB validation.
- PawnIO bundled installer annoyance — cause: installer task always attempted PawnIO install. Status: hardened installer to skip PawnIO when present.

## Planned scope for v3.0.0 (prioritized)
1. Critical fixes (remaining)
   - CI integration tests: quick‑profile switching stress test (exercise EC/WMI readback race conditions across backends).
   - CI integration tests: diagnostics export + UI flow (end‑to‑end validation for `Report model` and clipboard behavior).
   - Hardware QA validation for Victus/OMEN variants (Fn keys, per‑key RGB, fan quick profiles) — coordinate verified test units.

2. High priority features (value for stability & support)
   - Guided Fan Diagnostics (scripted PASS/FAIL flow + exportable results) — improves QA & user troubleshooting.
   - Model DB expansion (ongoing) — 'Report model' UX implemented; continue to expand verified entries from community submissions.
   - Add hardware QA checklist & automated integration tests for quick‑profile switching, thermal protection, and keyboard backlight on verified models.

3. Medium priority (nice-to-have for v3.0.0)
   - Persisted telemetry export: one‑click diagnostics upload package for Support (logs + sample capture). 
   - Safety hardening: further EC write rate‑limit tuning + simulated EC stress tests in CI (emulator/mocks).
   - UX: `—°C` consistency, clearer "monitoring health" tooltips, and more granular OSD controls.

4. Deferred (post‑3.0.0 / research)
   - Full privilege separation (remove requireAdministrator): architectural change requiring service/agent design and audit acceptance.
   - Expanded hardware auto‑promotion (more EC register maps) until we collect more validated hardware dumps.

## Tests & acceptance criteria to add for v3.0.0
- Unit (ADDED): keyboard hook + WMI filtering; fan MonitorLoop confirmation counters; diagnostics mode guard; power-read stabilization logic; Model report export (`ReportModelCommand`).
- Integration (CI): quick‑profile switch stress test (ADDED) — verifies no transient 0 RPM or single‑sample spikes during rapid preset switching; remaining: simulated RAPL power telemetry simulation, keyboard model detection matrix.
- Hardware QA: checklist for Victus/OMEN variants (Fn keys, per‑key RGB, fan quick profiles, thermal protection release behavior).

## Work in progress (active TODOs)
- [in‑progress] **EC write watchdog & rate‑limit** — prevent EC hammering / ACPI Event 13; add IEcAccess mocks + CI stress tests. (Files: `FanController.cs`, `FanService.cs`, `PawnIOEcAccess.cs`)
- [not-started] **WMI V2 verification & `ak0003nr` support** — parse V2 fan commands and verify readbacks (Files: `HpWmiBios.cs`, `WmiFanController.cs`, `ModelCapabilityDatabase.cs`).
- [not-started] **Fix Fan RPM parsing (krpm → RPM)** — add unit tests for byte‑order/parsing edge cases (Files: `HpWmiBios.cs`).
- [not-started] **Harden WMI "success but ineffective" fallback + rollback** — ensure preset verification cannot be bypassed by estimated readbacks (Files: `WmiFanController.cs`, `FanService.cs`).
- [not-started] **Global hotkey conflicts** — remove/adjust `Ctrl+S` global registration; window‑focused hotkeys (Files: `HotkeyService.cs`).
- [not-started] **Diagnostics export E2E + clipboard CI test** — end‑to‑end validation for `Report model` flow (Files: `ModelReportService.cs`, `MainViewModel.cs`).

> Changelog will be updated to mark items as **completed** as each task and its tests are merged.

## Next steps (pick one)
- Prepare a private alpha (3.0.0-alpha) non‑installer test build for hardware QA (Victus/OMEN reproducer machines). **Do NOT build or publish a 3.0.0 installer yet.**
  - Acceptance criteria: all unit tests green; CI quick‑profile & diagnostics export tests passing; model DB additions included; hardware QA checklist items verified on at least one device per family.
- Open a draft PR bundling all regression fixes + new unit/integration tests for v3.0.0 (after alpha verification).
- Expand `KeyboardModelDatabase` with additional community‑sourced model entries and add a "report your model" button in Diagnostics.

Recommended next action: prepare the 3.0.0-alpha non‑installer build and run hardware QA (private testers).
