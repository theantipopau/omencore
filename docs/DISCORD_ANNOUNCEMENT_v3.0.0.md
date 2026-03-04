# v3.0.0 — Architecture Overhaul & Stability Release

The biggest OmenCore release since v2.0.0! 🎉

## What's New
- **Architecture Overhaul** — Driver-free monitoring (WMI BIOS + NVAPI, no WinRing0/LHM)
- **Fan Diagnostics** — Guided 30/60/100% test with PASS/FAIL results per fan
- **Memory Optimizer** — Smart/Deep clean + configurable auto-clean intervals
- **Keyboard** — Native brightness + LED animations (Breathing, ColorCycle, Wave)
- **V2 Keyboard Engine** — PawnIO EC-direct backend for verified models
- **Headless Mode** — `--headless` for server/HTPC operation

## Critical Fixes (7 Regressions)
- GPU telemetry lost after NVAPI error → 60s auto-recovery
- OMEN 16-wf1xxx fan control broken → model DB + WMI path fixed
- Fan auto mode 0 RPM after profile switch → debounce reset
- Monitor loop exits permanently on errors → 10s backoff + restart
- Startup freeze → WinRing0 registry check (~17s → <1ms) + background PerformanceCounter
- All sensors 0°C → individual sources now work independently

## GUI Polish
- 1m / 5m / 15m / 30m temperature chart time-range selector
- Settings search bar, profile scheduler, onboarding wizard
- Fan curve ghost overlay (preset preview on hover)
- System Optimizer overhaul + Bloatware Manager risk filter + bulk progress

## Downloads
**Installer:** https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCoreSetup-3.0.0.exe
**Win Portable:** https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCore-3.0.0-win-x64.zip
**Linux:** https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCore-3.0.0-linux-x64.zip

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.0.md
GitHub: https://github.com/theantipopau/omencore
Discord: https://discord.gg/9WhJdabGk8
