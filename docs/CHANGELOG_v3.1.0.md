# OmenCore v3.1.0 - Telemetry Integrity and Idle Performance

**Release Date:** 2026-03-10  
**Type:** Minor release (stability and correctness focused)  
**Base Version:** v3.0.2-hotfix

## Summary

v3.1.0 is a root-cause-focused telemetry reliability release. It resolves stale and misleading readings in the monitoring pipeline, introduces explicit sensor-state semantics (including inactive dGPU handling), removes synthetic fan RPM fallback values, and reduces avoidable idle overhead in dashboard and monitor update paths.

## Fixed

### 1. Linux Performance Profile Detection on Newer Kernels (Bazzite/Fedora Variants)
- **Issue:** Some Linux systems with `hp-wmi` loaded could not switch to the high-power thermal profile, limiting GPU boost behavior.
- **Root Cause:** Profile write logic assumed `performance` was always available and did not fully account for kernel variants exposing `balanced-performance` and alternative `*_choices` paths.
- **Fix:** Added profile alias resolution and expanded choices-path detection across ACPI and hp-wmi variants so performance mode selection resolves to supported kernel strings.
- **Files:**
  - `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
  - `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`
  - `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`

### 2. CPU Power Staleness on No-MSR Systems
- **Issue:** On systems without MSR power source availability, CPU package power could stick to an early non-zero fallback value.
- **Root Cause:** Fallback lifecycle was not refreshed continuously once a non-zero value had been captured.
- **Fix:** CPU power fallback is now re-evaluated continuously when MSR power is unavailable, preventing stale lock-in.
- **Files:**
  - `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### 3. Incorrect GPU Temperature During dGPU Inactivity
- **Issue:** Users could see random/incorrect GPU temperature values while the dedicated GPU was inactive.
- **Root Cause:** Inactive state was not represented explicitly in telemetry and UI formatting paths.
- **Fix:** Added explicit inactive detection and sanitization; inactive state is now surfaced intentionally to UI instead of forcing misleading numeric output.
- **Files:**
  - `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
  - `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
  - `src/OmenCoreApp/ViewModels/MainViewModel.cs`
  - `src/OmenCoreApp/Controls/HardwareMonitoringDashboard.xaml.cs`

### 4. Fabricated Fan RPM Values
- **Issue:** RPM readback fallback could generate estimated values that were not directly measured.
- **Root Cause:** Synthetic RPM estimation path executed when direct readback was unavailable.
- **Fix:** Removed synthetic estimation fallback; unavailable readback now reports non-fabricated values (0/unknown source semantics).
- **Files:**
  - `src/OmenCoreApp/Hardware/WmiFanController.cs`

## Improved

### 5. Explicit Telemetry State Modeling
- Added `TelemetryDataState` and per-sensor state fields for CPU temperature, CPU power, GPU temperature, Fan1 RPM, and Fan2 RPM.
- Added helper semantics for inactive GPU interpretation.
- UI and summaries now consume state instead of assuming all values are directly valid numerics.
- **Files:**
  - `src/OmenCoreApp/Models/MonitoringSample.cs`
  - `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
  - `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
  - `src/OmenCoreApp/ViewModels/MainViewModel.cs`

### 6. Dashboard Clarity for Inactive/Unavailable States
- Dashboard now displays intentional labels such as `Inactive` instead of misleading values for inactive GPU periods.
- CPU availability/state messaging improved for unavailable readings.
- Added explicit stale/invalid/unavailable status rendering in dashboard metric cards for both CPU and GPU.
- **Files:**
  - `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
  - `src/OmenCoreApp/Controls/HardwareMonitoringDashboard.xaml.cs`

## Optimized

### 7. Reduced Monitoring Loop and UI Churn
- Removed repetitive debug logging in hot monitoring paths.
- Replaced broad property invalidation with targeted property notifications for telemetry updates.
- Increased high-frequency dashboard metric timer interval from 1s to 2s to reduce idle update pressure.
- Reduced dashboard no-data placeholder logging noise by switching repetitive warnings to transition-based debug logging.
- Added guardrails in dashboard power estimation to avoid divide-by-zero/NaN propagation when RAM total is temporarily unavailable.
- **Files:**
  - `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
  - `src/OmenCoreApp/ViewModels/MainViewModel.cs`
  - `src/OmenCoreApp/Controls/HardwareMonitoringDashboard.xaml.cs`

### 8. Summary Consistency Across Views
- Main and dashboard summaries now consistently reflect telemetry states (`Unavailable`, `Stale`, `Invalid`, `Inactive`) instead of mixed numeric-only fallbacks.
- Removed unused GPU telemetry timestamp tracking internals to keep monitor state flow deterministic and maintainable.
- **Files:**
  - `src/OmenCoreApp/ViewModels/MainViewModel.cs`
  - `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`
  - `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### 9. Windows Monitoring Messaging and WinRing0 Opt-Out Clarity
- Added explicit `OMENCORE_DISABLE_LHM=1` support in monitor fallback initialization to enforce WMI/NVAPI/PawnIO-only telemetry paths when desired.
- Updated hardware worker fallback log text to remove misleading WinRing0-specific wording and reflect PawnIO-first recovery behavior.
- Updated app/settings/diagnostics user-facing messaging to label WinRing0 as legacy/optional and keep PawnIO+WMI as the recommended default path.
- Removed obsolete app-side LibreHardwareMonitor installer helper path so startup guidance cannot regress to outdated driver instructions.
- Final consistency sweep aligned dependency-audit labels, settings backend text, and antivirus FAQ wording with real runtime behavior (PawnIO/WMI default; legacy WinRing0 optional).
- **Files:**
  - `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
  - `src/OmenCore.HardwareWorker/Program.cs`
  - `src/OmenCoreApp/App.xaml.cs`
  - `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`
  - `src/OmenCoreApp/ViewModels/MainViewModel.cs`
  - `src/OmenCoreApp/Services/Diagnostics/DiagnosticExportService.cs`
  - `src/OmenCoreApp/Services/SystemInfoService.cs`
  - `docs/ANTIVIRUS_FAQ.md`

### 10. Dashboard Accuracy and Overhead Polish
- Dashboard power metric now prefers measured CPU/GPU watt telemetry when available, then falls back to estimation.
- Thermal/efficiency averages now use state-aware valid temperatures to avoid skew from inactive/unavailable sensors.
- Battery health WMI query cache window extended (60s to 5m) and high-frequency battery logs downgraded to debug to reduce background overhead.
- Minor startup allocation reduction by switching tray version read from `ReadAllLines` to streaming `ReadLines`.
- **Files:**
  - `src/OmenCoreApp/Controls/HardwareMonitoringDashboard.xaml.cs`
  - `src/OmenCoreApp/Utils/TrayIconService.cs`

## Version Alignment

The following core version markers were updated to `3.1.0`:
- `VERSION.txt`
- `src/OmenCoreApp/OmenCoreApp.csproj`
- `src/OmenCore.HardwareWorker/OmenCore.HardwareWorker.csproj`
- `src/OmenCore.Avalonia/OmenCore.Avalonia.csproj`
- `src/OmenCore.Desktop/OmenCore.Desktop.csproj`
- `src/OmenCore.Linux/Program.cs`
- `src/OmenCoreApp/Services/ProfileExportService.cs`
- `src/OmenCoreApp/Utils/TrayIconService.cs`
- `src/OmenCore.Avalonia/ViewModels/SettingsViewModel.cs`
- `src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs`

## Regression-Safety Notes

The release was scoped to preserve existing behavior for:
- Hotkeys and quick profile switching
- Fan profile application and WMI fan control flow
- Existing telemetry cards and dashboard structure
- Startup and app-level monitoring orchestration

## Known Follow-up Validation Targets

Post-release verification should prioritize:
1. Long-run CPU temperature/power refresh stability
2. dGPU inactive to active transition correctness
3. Fan RPM zero/unavailable rendering correctness on boards with partial readback
4. Idle CPU usage comparison versus v3.0.2-hotfix baseline

## Pre-Release Bugfix Rollup (Included In Public 3.1.0)

The following items were developed as internal hotfix iterations before first public deployment of v3.1.0 and are included in the final 3.1.0 build:

### UI Telemetry Polish
- Normalized tuning wattage placeholders to `--W` to prevent malformed `-w` rendering in compact gauges.
- Updated tuning GPU clock gauge to placeholder-aware binding when clock telemetry is missing/zero.
- Reworded inactive GPU summaries to `dGPU idle` for clearer user interpretation.
- Tightened GPU inactive classification to require no live temp/clock/power signals, reducing false inactive reports.
- Compacted sidebar stale/unavailable temperature presentation to reduce clipping in narrow sidebar cards.

### Tray Reliability
- Removed problematic context-menu style-key override behavior to restore reliable tray right-click menu opening.
- Reworked dark menu templating to eliminate the left white gutter and icon hover underlay bleed on tray/context items.

### Hardware Worker Reliability
- Added startup worker bootstrap so hardware worker launches with OmenCore instead of waiting for deferred fallback paths.
- Kept fallback prewarm path as secondary recovery behavior.
- Added startup diagnostics for worker launch outcomes (started, disabled by env, executable not found, start failure).
- Kept worker bootstrap aligned with configured orphan-timeout settings.

### GitHub Bug Fixes Included In 3.1.0 Rollup
- **GitHub #77 — Fan speed going max in sleep mode:** moved suspend/resume hook registration into always-active startup flow (not Settings-tab lazy initialization), added suspend-aware fan service handling, and restored BIOS auto fan policy during suspend to avoid sleep-time max RPM behavior.
- **GitHub #78 — wrong temperature for OMEN MAX 16-ah0000 (Intel CPU):** added model-scoped CPU temperature source override so affected OMEN MAX 16/ah0000 systems prioritize worker-backed CPU sensor reads.
- Added a dedicated Monitoring Diagnostics status line that explicitly reports whether the model-specific CPU temperature override is active, improving field triage and user verification.

### Build Validation
- `dotnet build src/OmenCoreApp/OmenCoreApp.csproj -c Release` completed successfully (0 errors, 0 warnings).

### Final Release Artifacts and SHA256

| File | Size | SHA256 |
|------|------|--------|
| `OmenCoreSetup-3.1.0.exe` | 101.09 MB | `D92548E4E3698A2B71D11A02ED64D918746C3C3CB06EC2035E8602D57C50AD8C` |
| `OmenCore-3.1.0-win-x64.zip` | 104.32 MB | `1EA65E7BA857285A01A896FC2A7BF8418D1B8D9723DCB9EE4A350E6BA87A06F6` |
| `OmenCore-3.1.0-linux-x64.zip` | 43.55 MB | `276686F92EB289B3196BDCD02CFC93E95F676D269515740060FB7B5A585D9D0F` |

**End of Changelog v3.1.0**
