# v3.9.0 - UX Polish, Silent-Failure Fixes, and Model Additions

Thanks OsamaBiden and everyone who reported issues since 3.8.2!

## Bug Fixes

- **OMEN Key action** - all four options silently failed to save, reverted on every relaunch
- **Game profiles** - new/duplicated profiles could be lost on a crash before the next save
- **GPU Power Boost** - now follows Performance/Balanced/Quiet on General tab, tray, and hotkey cycle again
- **Custom tab** - was rendering the default white WPF theme instead of dark
- **OSD in fullscreen** - no longer drops behind borderless/windowed games (re-asserts topmost). Exclusive fullscreen still can't show any overlay - Windows limit
- **OSD stale mode** - no longer shows "Balanced" before the real mode is confirmed at startup
- **Tray icon contrast** - temp text is black instead of white on yellow/green badges
- **Auto-updater** - HardwareWorker shutdown no longer skips remaining processes on one failure
- **Automation Service** - fixed an idle-timer overflow (~24.9 days uptime) and a battery bug that could fire "above N%" rules constantly on desktops
- **8C77 OMEN 16 (2024) wf1xxx Intel** - fixed Custom Fan Curve crash (V1/V2 mismatch)
- **8C3F HP Victus 15-fa1xxx** - fixed 10-minute fan-control delay (#125)

## Enhancements

- Quick Access popup: new toggle to disable it entirely
- Crash reports now include full stack traces
- Silent EC write failures now log a warning
- Model updates: Victus 15 2025 AMD (`fb3xxx`), OMEN Transcend 14 (`8C58`)

## Still Tracked

- `8BCD` OMEN 16 fan reports - evidence-gated, no fan/thermal code touched without proof
- OGH Eco equivalent, OMEN Max HID per-key editor

**Download:** <https://github.com/theantipopau/omencore/releases/tag/v3.9.0>

```text
F662E33F  OmenCoreSetup-3.9.0.exe
8E20A11E  OmenCore-3.9.0-win-x64.zip
54083720  OmenCore-3.9.0-linux-x64.zip
```

Full hashes + changelog: <https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.9.0.md>
