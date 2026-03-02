# v3.0.0 — Architecture Overhaul & Stability Release

The biggest OmenCore release since v2.0.0! 🎉

## What's New
- **Architecture Overhaul** — Driver-free self-sustaining hardware monitoring (WMI BIOS + NVAPI)
- **Fan Diagnostics** — Guided sequential testing at 30/60/100% with PASS/FAIL results
- **Memory Optimizer** — Real-time monitoring + Smart/Deep clean + persistent settings
- **Keyboard Lighting Enhancements** — Native brightness control + LED animations (Breathing, ColorCycle, Wave)
- **V2 Keyboard Engine** — PawnIO-native EC direct backend with auto-promotion for verified models
- **Headless Mode** — `--headless` flag for server/background operation (no GUI)
- **Diagnostics Reporting** — One-click model report export for support + telemetry export

## Critical Fixes (7 Regressions Resolved)
- **GPU telemetry lost after NVAPI error** — 60-second auto-recovery instead of permanent loss
- **OMEN 16-wf1xxx fan control broken** — Model DB entry + WMI path fixed
- **Fan auto mode 0 RPM after profile switch** — Debounce window reset + unconditional recovery
- **Linux perf mode silent failure** — Priority routing: hp-wmi → ACPI → EC register
- **Monitor loop hangs permanently** — 10-second backoff + loop restart instead of exit
- **Startup freeze** — WinRing0 registry check (~17s → <1ms) + PerformanceCounter background init
- **All sensors read 0°C** — Early-exit guard removed; individual sources now independent

**Plus 3 additional reliability improvements: NVAPI recovery, TryRestartAsync reset, dashboard real metrics**

## GUI Polish
- Temperature charts now support 1m / 5m / 15m / 30m time-range selector
- Settings search bar with instant results across all tabs
- Profile scheduler with time-of-day automation rules
- Keyboard zone visual schematic with proportional diagram
- Fan curve ghost overlay (preset preview on hover)
- Three-step onboarding wizard for first-time users
- Zero-temperature "—°C" indicator (no more false 0°C displays)
- System Optimizer visual overhaul (emoji → Path icons, theme colors)
- Bloatware Manager: risk level filter, bulk remove progress, fixed status badges, BETA removed

## Linux
- `DOTNET_BUNDLE_EXTRACT_BASE_DIR` env var now auto-configured + systemd service fix
- Performance mode thermal throttle watchdog (opt-in) — re-applies mode after cooldown
- Per-fan RPM bounds + backend guardrails

## Downloads
**Windows Portable:** https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCore-3.0.0-win-x64.zip
**Windows Installer:** https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCoreSetup-3.0.0.exe
**Linux Portable:** https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCore-3.0.0-linux-x64.zip

```
8D65569532D887AC96AEC084A5A73E467844CE57F51B975A6A2811171C9A078D  OmenCoreSetup-3.0.0.exe
DC8C95688FFDBB4A1BB7232B2E0DFA1D2BEF5A88C0BB8A18CDED1ADA375ED3C1  OmenCore-3.0.0-win-x64.zip
605335229F5C403D915E99184CC20C1A047EB709B6F33817464DF88DAA5858D4  OmenCore-3.0.0-linux-x64.zip
```

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.0.md
GitHub: https://github.com/theantipopau/omencore
Discord: https://discord.gg/9WhJdabGk8
