# OmenCore v3.0.2 Hotfix - Telemetry and Linux Reliability

**Release Date:** 2026-03-07  
**Type:** Hotfix  
**Base Version:** v3.0.2

---

## Summary

This hotfix focuses on reliability regressions reported after the v3.0.2 rollout.

Priority areas:
- Windows telemetry resilience (CPU/GPU power and temperature reliability)
- Linux launch/package correctness (CLI + GUI payload integrity)
- Linux fan and keyboard compatibility on hp_wmi-only and custom-driver systems
- Version metadata consistency in Avalonia UI

---

## Fixed

### Windows

1. CPU/GPU power fallback when primary source reports 0W under active load
- Added fallback to LibreHardwareMonitor power sensors when load/temperature indicates active usage.
- Prevents stuck `0W` display on affected systems.
- File: `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- File: `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`

2. ACPI CPU temperature outlier rejection
- ACPI values are now ignored when they are implausible outliers versus current trusted readings.
- Reduces sudden incorrect jumps that could destabilize auto fan behavior.
- File: `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### Linux

3. Linux package launcher/runtime correctness
- Packaging flow now isolates GUI and CLI publish outputs.
- Final artifact always includes the known-good single-file `omencore-cli` binary.
- Removes stale framework-dependent `omencore-cli.dll`, `.deps.json`, and `.runtimeconfig.json` sidecars from final package.
- File: `build-linux-package.ps1`

4. Linux fan control compatibility improvements
- Added direct support for hp-wmi fan target nodes:
  - `/sys/devices/platform/hp-wmi/hwmon/hwmon*/fan1_target`
  - `/sys/devices/platform/hp-wmi/hwmon/hwmon*/fan2_target`
- Retains fallback mapping to platform performance profiles when direct fan writes are unavailable.
- File: `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`

5. Linux keyboard RGB compatibility improvements
- Added preferred support for `multi_intensity` (`R G B`) when exposed by hp-wmi custom-driver stacks.
- Keeps legacy `color` fallback for older interfaces.
- File: `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`

6. Avalonia Linux version labeling fixed
- Removed stale fallback version strings and aligned assembly/file versions to `3.0.2`.
- Fixes incorrect GUI version display on Linux builds.
- File: `src/OmenCore.Avalonia/OmenCore.Avalonia.csproj`
- File: `src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs`
- File: `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`

### UX/Behavior

7. Fan preset apply behavior improvement
- Selecting a preset in fan controls now applies the curve immediately.
- File: `src/OmenCore.Avalonia/ViewModels/FanControlViewModel.cs`

8. Fan curve preset fallback safety
- Preset fallback now returns cloned lists to avoid shared-reference mutation side effects.
- File: `src/OmenCore.Avalonia/Services/FanCurveService.cs`

9. Watchdog false-positive hardening and safer failsafe behavior
- Fixed a liveness gap where unchanged telemetry could suppress heartbeat updates and trigger a false watchdog alarm.
- Watchdog now requires confirmed consecutive freeze checks before failsafe activation.
- Failsafe now uses a high non-max fan target to avoid sticky max-countdown behavior and auto-attempts recovery when monitoring resumes.
- File: `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- File: `src/OmenCoreApp/Services/HardwareWatchdogService.cs`

10. Diagnostic mode fan-action guard
- Fan preset/curve/manual speed apply actions are now blocked while diagnostic mode is active to prevent confusing "applied" UX when writes are intentionally suppressed.
- File: `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`

11. Logs folder alignment
- Application logs now write to `%LocalAppData%/OmenCore/logs` so the dedicated logs folder is populated consistently.
- Settings "Open Log Folder" now points to that same path.
- Diagnostic export now checks both current and legacy log locations.
- File: `src/OmenCoreApp/Services/LoggingService.cs`
- File: `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
- File: `src/OmenCoreApp/Services/Diagnostics/DiagnosticExportService.cs`

12. Linux profile/fan fallback expansion for unsupported board paths (Issue #76)
- Expanded Linux thermal profile detection to include additional hp-wmi profile path variants.
- `SupportsFanControl` now correctly reports true when direct fan target/output controls exist even if platform profile files are missing.
- Added best-effort `fan_always_on` enable before manual fan writes to improve persistence on firmware-guarded systems.
- Fan fallback profile apply no longer hard-fails when profile interfaces are absent.
- File: `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`

13. Linux board-ID-aware safety and diagnostics (Issue #76)
- Linux EC controller now reads DMI board ID and applies board-level safety gating.
- Added board `8C58` handling to avoid unsafe/unreliable legacy EC write paths and prefer hp-wmi/acpi-based control.
- `omencore-cli diagnose` now reports Board ID, `fan*_target` presence, and tailored recommendations for Transcend 14-class systems.
- File: `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
- File: `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`

14. Windows crash-hardening for hotkey lifecycle churn (log-reported)
- Made hotkey initialization idempotent to prevent duplicate setup on repeated initialization paths.
- Added guards so focus-driven hotkey register/unregister only runs on real state transitions.
- Prevents repeated activate/deactivate hotkey churn from escalating into unstable registration state.
- File: `src/OmenCoreApp/ViewModels/MainViewModel.cs`

15. Windows crash-hardening for fan countdown re-apply loop (log-reported)
- Countdown extension timer is now synchronized for start/stop operations.
- Added non-reentrant timer callback guard to prevent overlapping fan re-apply callbacks under load.
- Reduces concurrent firmware write pressure during prolonged Max/Performance operation.
- File: `src/OmenCoreApp/Hardware/WmiFanController.cs`

---

## Additional Build Optimizations

The Linux packaging script was further hardened for reproducibility:
- `Set-StrictMode -Version Latest` enabled
- Staging directory is fully cleaned before packaging
- Publish output validation for `omencore-gui` and `omencore-cli`
- `.sha256` sidecar file generated next to the zip artifact

File: `build-linux-package.ps1`

---

## Artifacts and SHA256

Latest verified rebuilds for this hotfix cycle:

| File | Size | SHA256 |
|------|------|--------|
| `OmenCoreSetup-3.0.2.exe` | 101.09 MB | `954AA7C608D36D6CDD99E1599A7BB4CA7F39DB5876241436CDF822BA2DA8FEC0` |
| `OmenCore-3.0.2-win-x64.zip` | 104.31 MB | `760FDC6D02B4872128383EA2E74FB86BEBFB62EABC74BB57FCE431AF4953B406` |
| `OmenCore-3.0.2-linux-x64.zip` | 43.55 MB | `582461B475C3C712B669395C58152A688735DE2521F8F4D7B31D2A950CE43ED5` |

Generated sidecar:
- `artifacts/OmenCore-3.0.2-linux-x64.zip.sha256`

Current artifact hashes are tracked in:
- `README.md`
- `CHANGELOG.md`
- `docs/CHANGELOG_v3.0.2.md`
- `docs/discord_v3.0.2_post.md`

Use `build-linux-package.ps1` to regenerate Linux artifact hash and the matching `.sha256` file after each rebuild.
