# v2.9.0 — Stability & Telemetry Recovery Patch

Thanks to the community for the bug reports! 🙏

## Core Features
- **Headless Mode** — `--headless` flag for server operation (no GUI)
- **Hardware Worker Timeout** — Configurable orphan timeout (1-60 min) with UI controls
- **Self-Sustaining Monitoring** — WMI BIOS + NVAPI only (no LHM/WinRing0/NVML)
- **Memory Optimizer** — Real-time RAM monitoring + Smart/Deep clean
- **Afterburner Coexistence** — GPU data from shared memory (zero driver contention)
- **Enhanced Keyboard Lighting** — V2 engine, 4-zone RGB, breathing/wave effects

## Bug Fixes (9 Community Reports)
- **App Freeze** — Tray actions now use last-write-wins queue
- **Fn+F2/F3 Steals Hotkeys** — Stronger brightness key exclusion
- **Temperature Freeze** — Idle-aware recovery thresholds
- **Power 0W Dropouts** — Fallback TDP tables + RAPL MSR
- **Profile UI Desync** — Quick profiles sync OMEN tab display
- **Game Library Buttons** — Fixed disabled state after selection
- **GPU Temp Frozen** — NVML auto-recovery + idle thresholds
- **Afterburner Coexistence** — Fixed MAHM offset bug
- **EC Safety** — Reduced writes from 15-33/sec to ~0.5/sec

## Downloads
**Windows Portable:** <https://github.com/theantipopau/omencore/releases/download/v2.9.0/OmenCore-2.9.0-win-x64.zip>
**Linux Portable:** <https://github.com/theantipopau/omencore/releases/download/v2.9.0/OmenCore-2.9.0-linux-x64.zip>

```
37265CEF  OmenCore-2.9.0-win-x64.zip
EB59465D  OmenCore-2.9.0-linux-x64.zip
```

Full changelog: <https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.9.0.md>