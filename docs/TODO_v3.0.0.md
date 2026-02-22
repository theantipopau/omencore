# TODO for v3.0.0 (current)

- [x] WMI V2 verification + add ak0003nr support (P0)
- [x] EC write watchdog & rate-limit (P0)
- [x] Fix Fan RPM parsing (krpm → RPM) + tests (P1)
- [x] Harden WMI "success but ineffective" fallback + rollback (P1)
- [x] Global hotkey conflicts (remove Ctrl+S; window-focused hotkeys)
- [x] Diagnostics export E2E + clipboard CI test (added STA clipboard check)
- [x] Guided Fan Diagnostics UI + exportable results (roadmap)
- [x] EC write-rate simulator + CI stress tests (roadmap)
- [x] NVAPI/graphics updates & Strix Point CPU detection
- [x] Persisted telemetry export + one-click diagnostics upload (telemetry button added)
- [x] Privilege-separation design spike (remove requireAdministrator)
- [x] Open draft PR & prepare non-installer 3.0.0-alpha for private QA (artifacts + workflow added)

Notes:
- `alpha` workflow created: `.github/workflows/alpha.yml` (produces portable ZIP artifacts and creates a draft pre-release `v3.0.0-alpha`).
- Local helper: `package-alpha.ps1` to produce portable ZIPs under `artifacts/` for private tester distribution.
