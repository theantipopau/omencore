# [Release] OmenCore v3.3.0 - Monitoring Fix, Fan Stability, GPU OC, and Major UI Polish

Release:
https://github.com/theantipopau/omencore/releases/tag/v3.3.0

OmenCore v3.3.0 is now live. This is the largest stability-and-polish release since v3.1.0. It ships every identified bug fix from v3.2.x community reports, a comprehensive visual quality pass across OSD and Quick Access, RGB hardening, runtime performance optimizations, and several long-requested features.

---

## Critical Fixes

**Monitoring halt → watchdog failsafe fans (fans suddenly at 100%)**
The most common unexplained "fans stuck at 100%" report: a cross-thread exception in the lighting sub-system caused the hardware monitoring loop to stop delivering temperature data to the watchdog, which then triggered its failsafe. Fixed with a dispatcher marshal in LightingViewModel and subscriber isolation in the monitoring fan-out so one bad subscriber can never starve others.

**Fan curve stops working after first save**
Custom fan curve presets became permanently non-functional after the first apply when OGH was running alongside OmenCore, or when fans were already at their target duty cycle. The internal RPM-change verification check was false-failing and rolling back the curve engine. Fixed by short-circuiting the RPM check for curve-based presets where no immediate RPM jump is expected.

**Fan verification service silently destroying fan state**
FanVerificationService was calling SetFanMode(Default) on its own diagnostic check failure — overwriting whatever curve state was active. Fixed by removing the destructive reset from the failure path.

**Restore Defaults app freeze (required End Task)**
KeyboardLightingService was calling .GetAwaiter().GetResult() on async methods from the WPF UI thread, causing a permanent deadlock with the WPF SynchronizationContext. Fixed with fire-and-forget Task.Run dispatch — also adds a 60s timeout and per-step progress to Revert All.

**Bloatware Manager Remove / Restore buttons never enabled**
The SelectedApp setter raised PropertyChanged on computed bool properties but never raised CanExecuteChanged on the ICommand bindings. WPF buttons only re-evaluate enabled state on CanExecuteChanged. Fixed with RaiseCommandStates() in the setter.

---

## Fan and Thermal

- Post-apply curve verification kick now re-enables the curve instead of leaving it parked
- V1 BIOS transition kick no longer floors fans above idle after switching out of Performance
- V1 BIOS auto-mode floor correctly cleared so fans can spin down to 0 RPM at idle
- Fan names normalize from LibreHardwareMonitor internal labels ("G", "GP") to "CPU Fan" / "GPU Fan"
- CPU temp fallback IPC timeout increased (250ms → 500ms) to reduce false fallback-disabled warnings under heavy load

---

## OSD and Quick Access

- OSD: gradient background, refined drop shadow, corner radius 10
- OSD: horizontal layout toggle in Settings
- OSD: Compact / Balanced / Comfortable density modes
- OSD: metric group toggles (Thermals, Performance, Network, System)
- OSD: GPU hotspot now uses real junction temperature sensor instead of estimated offset
- Quick Access: fan buttons reordered Quiet → Auto → Curve → Max
- Quick Access: "Custom" renamed to "Curve"; active curve preset name shown in tooltip even when button is selected/disabled
- Fan/performance decoupling: visible "Fan linked / independent" badges in Fan Control, System Control, and Quick Access with one-time explainer

---

## UI Polish

- Disabled buttons now show their tooltip across all ~157 buttons in the app (ToolTipService.ShowOnDisabled on base styles)
- OGH unsupported-command warnings (Fan:GetData error 2, etc.) permanently silenced to debug after 5 occurrences — eliminates repeating WARN log entries every 60s on models that don't support those commands
- ViewModel event subscription cleanup on app shutdown

---

## Features

- **GPU OC**: detected core/memory/power range labels alongside NVIDIA sliders; adaptive Safe/Balanced/Max presets; 30s Test Apply auto-revert; explicit Power Limit Only messaging for laptop GPUs
- **Resume recovery**: shared timeline recorded per boot cycle; post-resume status card in Settings showing last recovery outcome
- **Model identity card** in Settings: resolution source, confidence, keyboard profile, raw identity inputs with one-click copy
- **Game Library**: manual "Add Game" action for titles not found by launcher scans
- **Lite Mode**: optional toggle in Settings to hide advanced tuning surfaces
- **Startup restore safety**: hardware restore is now disabled by default and explicitly blocked on OMEN 16 / Victus models pending further investigation of CMOS checksum / firmware-loop reports (GitHub #106)
- **MSI Afterburner coexistence**: fixed CPU/GPU load freezing at stale values; GPU engine counter now uses peak 3D/Compute engine instead of broad sum
- Bloatware Manager: bulk cancel, timeout guardrails, staged removal with rollback, restore point creation, removal preview, startup/task scan coverage, HP OMEN-specific signatures, removal report history
- System Optimizer: per-step Revert All progress, batch apply by category, drift detection with hourly re-verification, risk details view

---

## Downloads

Windows:
- OmenCoreSetup-3.3.0.exe (recommended)
- OmenCore-3.3.0-win-x64.zip (portable)

Linux:
- OmenCore-3.3.0-linux-x64.zip

## SHA256

```
483D4CAEB66DF3923F6152ECE98B128F3F9A0B3A2E0A5CE42403C43BB9F12D9E  OmenCoreSetup-3.3.0.exe
F07B9435DCCB2771672BDE0E44CC2A2B980859AC0A576B8E6BCCC93A61D064C9  OmenCore-3.3.0-win-x64.zip
AD5A2C37583EB9D8E486431B8AD9F7BEFBA2847325F5F21C4ABFB0B63C04AA1C  OmenCore-3.3.0-linux-x64.zip
```

If you hit a regression, please open an issue with your laptop model, BIOS version, and OmenCore logs so we can reproduce:
https://github.com/theantipopau/omencore/issues

Thanks to everyone who reported issues, tested builds, and shared logs on Discord, Reddit, and GitHub.
