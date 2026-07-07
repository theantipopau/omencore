# v3.9.0 - UX Polish, Silent-Failure Fixes, and Model Additions

Thanks OsamaBiden and everyone who reported issues on Discord/GitHub since 3.8.2!

## Bug Fixes

- **OMEN Key action setting was completely non-functional** - all four options in Settings silently failed to save and reverted to default on every relaunch
- **Game profiles could be lost on crash** - newly created or duplicated profiles weren't saved until another action triggered a write
- **GPU Power Boost now follows profile switches** - Performance/Balanced/Quiet on the General tab, the tray quick-profile menu, and the hotkey cycle all sync boost level again (it used to stay frozen at whatever was last set manually)
- **Custom tab showed the default white WPF theme** instead of the app's dark theme
- **OSD no longer drops behind borderless/windowed-fullscreen games** - now re-asserts topmost every second instead of only once. (True DXGI exclusive fullscreen still can't show any overlay window - that's a Windows compositor limit, not us)
- **OSD stale "Balanced" default** - performance-mode row no longer shows a wrong mode before the real one is confirmed at startup
- **Tray icon contrast** - temperature text is now black instead of white on the yellow/green badge (was eye-straining)
- **Auto-updater HardwareWorker shutdown** - one process failing to close no longer skipped the rest, and the installer now waits for confirmed exit
- **Automation Service** - fixed an idle-timer overflow bug (~24.9 days uptime) and a battery-percentage bug that could fire "above N%" rules constantly on desktops/sensor-failure systems
- **8C77 OMEN 16 (2024) wf1xxx Intel** - fixed a crash on the Custom Fan Curve tab caused by a V1/V2 profile mismatch
- **8C3F HP Victus 15-fa1xxx** - direct model entry fixes a 10-minute fan-control delay (GitHub #125)

## Enhancements

- **Quick Access popup** - new "Enable quick access popup" toggle for anyone who keeps hitting Display Off by accident
- **Crash reports now include full stack traces** - previously only the exception type/message was logged, making community bug reports nearly impossible to diagnose
- **Silent EC write failures now log a warning** instead of failing invisibly (no control-behavior change)
- Model additions/updates: HP Victus 15 2025 AMD family fallback (`fb3xxx`), OMEN Transcend 14 (`8C58`) capability alignment

## Still Tracked

- `8BCD` OMEN 16 fan reports (Balanced-switch oscillation, Quiet RPM floor, Quiet thermal ceiling, ramp-down stepping) - evidence-gated, no fan/thermal code touched without hardware proof
- GPU TGP lock and Quiet-mode CPU temp reports on other models - awaiting session logs
- OGH Eco equivalent and dedicated OMEN Max HID per-key editor

**Download:** <https://github.com/theantipopau/omencore/releases/tag/v3.9.0>

```text
F662E33F  OmenCoreSetup-3.9.0.exe
8E20A11E  OmenCore-3.9.0-win-x64.zip
54083720  OmenCore-3.9.0-linux-x64.zip
```

Full hashes + changelog: <https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.9.0.md>
