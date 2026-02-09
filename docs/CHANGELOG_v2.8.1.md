# OmenCore v2.8.1 - Community Bug Fix Update

**Release Date:** February 9, 2026  
**Branch:** feature/v2.7.0-development

---

## Highlights

v2.8.1 is a hotfix release addressing critical community-reported bugs from v2.8.0 testing. The major fixes target fan control reliability on Victus and Transcend models, phantom RPM readings on V0/V1 ThermalPolicy systems, an OMEN key detection bug that caused brightness keys (Fn+F2/F3) to toggle the OmenCore window, a missing Avalonia GUI in Linux builds, and EC timeout crashes on systems with dead/removed batteries.

---

## 🐛 Bug Fixes

### Fix: Fn+F2/F3 (Brightness) Opens OmenCore Window
- **Root cause**: WMI `OnWmiEventArrived` handler had a fail-**open** design — if `eventId`/`eventData` extraction threw an exception, both values remained `null` and the null-guards (`if (x.HasValue && x != 29)`) passed through, treating **any** `hpqBEvnt` WMI event (including brightness Fn keys) as an OMEN key press
- **Fix**: Changed to fail-**closed** — extraction errors now `return` immediately; guards changed to `if (!x.HasValue || x != 29)` requiring exact match on both `eventId=29` AND `eventData=8613`
- Reported by: OsamaBiden (Discord)

### Fix: Auto Fan Mode — Fans Stuck at ~1000rpm Then Spike to Max
- **Root cause**: `RestoreAutoControl()` unconditionally called `ResetFromMaxMode()`, which sends `SetFanLevel(0, 0)` — on Victus models with `MaxFanLevel=100`, this puts the EC into manual-0% mode, overriding BIOS automatic fan control entirely; fans stay at minimum (~1000rpm) until emergency thermal protection kicks in at 80°C, then spike to maximum
- **Fix**: `ResetFromMaxMode()` is now only called when `_isMaxModeActive || IsManualControlActive`; countdown extension is also stopped and all control flags properly reset when restoring auto
- Reported by: Victus 16-s0xxx user (Discord)

### Fix: Quiet Profile = Max Fans (Inverted Behavior)
- **Root cause**: `MapPresetToFanMode()` always sent V1 mode bytes (`Cool = 0x50`) regardless of `ThermalPolicyVersion`; on V0/Legacy BIOS models (e.g., OMEN Transcend 14 2025), `0x50` is not a valid mode and gets interpreted as max performance by the firmware
- **Fix**: Added ThermalPolicy-aware mapping — V0/Legacy systems now correctly receive `LegacyDefault (0x00)`, `LegacyPerformance (0x01)`, `LegacyCool (0x02)` instead of V1 codes; the `CountdownExtensionCallback` inherits correct mode via `_lastMode`
- Reported by: cargocat — HP OMEN Transcend 14 2025 (Discord)

### Fix: Phantom Fan RPM Readings (4200-4400rpm on Quiet Fans)
- **Root cause**: `GetFanRpmDirect()` (CMD `0x38`) is a V2-only BIOS command (OMEN Max 2025+), but was called unconditionally on all models including V0/V1 Victus systems where it returns garbage data — fan level values like 42 get misinterpreted as 4200 RPM and pass the 0-8000 validation range
- **Fix**: All `GetFanRpmDirect()` calls in `GetFanSpeeds()` and `ReadFanSpeeds()` are now gated behind `ThermalPolicy >= V2`; also fixed circular dependency in `GetFanLevel()` V2 fallback path that caused phantom data to round-trip and self-reinforce
- Reported by: Victus 16-s0xxx user (Discord)

### Fix: Hardcoded Fan Level-to-Percent Conversion
- **Root cause**: `ReadFanSpeeds()` used `fanLevel / 55` instead of `fanLevel / _maxFanLevel` — on `MaxFanLevel=100` models, a level of 42 produced 76% instead of the correct 42%
- **Fix**: Changed to use `_maxFanLevel` (auto-detected per model) for correct percentage calculation on both classic krpm (55) and percentage-based (100) models

### Fix: Linux GUI Not Bundled
- **Root cause**: Linux build script only published `OmenCore.Linux` (CLI); the Avalonia GUI project (`OmenCore.Avalonia`) was never built or included in the Linux package
- **Fix**: Added Avalonia GUI publish step to `build-installer.ps1` for `linux-*` runtimes — `omencore-gui` binary is now bundled alongside `omencore-cli` in the Linux ZIP
- Reported by: Community (Discord)

### Fix: OSD Horizontal Layout Not Working
- **Root cause**: The `Layout` setting was stored in config and exposed in the ViewModel, but never consumed by `OsdOverlayWindow` — the XAML `StackPanel` had no `x:Name` and `ApplySettings()` never read `settings.Layout`
- **Fix**: Named the StackPanel `MainPanel` and added orientation switching in `ApplySettings()` based on the layout setting
- Reported by: Community (Discord)

### Fix: OSD Network Upload/Download Values Stuck at Zero
- **Root cause**: `UpdateNetworkTraffic()` runs inside `PingTimer_Tick()`, but the ping timer only started when `_showNetworkLatency` was enabled — if only upload/download were enabled, the timer never started and values stayed at 0
- **Fix**: Timer now starts when any of `_showNetworkLatency`, `_showNetworkUpload`, or `_showNetworkDownload` is enabled
- Reported by: Community (Discord)

### Fix: OSD Shows GPU Activity % Instead of FPS
- **Root cause**: When RTSS is not installed, the code deliberately showed GPU load percentage in the FPS field with a "GPU" label — users enabling "Show FPS" expected actual frame rate, not GPU utilization
- **Fix**: FPS field now shows "N/A" when RTSS is unavailable, with label "FPS" (not "GPU"). Default values changed from `"GPU"/"0%"` to `"FPS"/"--"`
- Reported by: Community (Discord)

### Fix: Linux Diagnose Output Truncated
- **Root cause**: Terminal box was 61 chars wide with `Truncate(note, 57)` — long messages like kernel recommendations and notes were aggressively clipped with "…"
- **Fix**: Widened box to 90 chars and added word-wrapping (`WrapText()`) so notes and recommendations wrap across multiple lines instead of being truncated
- Reported by: Community — OMEN 16-wf0xxx / CachyOS (Discord)

### Fix: Linux Fan Speeds Wrong and Stuck
- **Root cause**: `GetHwmonFanSpeeds()` used `File.ReadAllText()` which can return stale page-cached sysfs content on some kernels; `GetFanSpeedPercent()` had no hwmon path and returned (0,0) for hwmon-only models
- **Fix**: Replaced with unbuffered `FileStream` reads (`ReadSysfsFile()`) to force fresh values; added hwmon RPM-to-percent estimation for models without EC access
- Reported by: Community — OMEN 16-wf0xxx / CachyOS (Discord)

### Fix: Linux Keyboard Reports 4-Zone for Per-Key Models
- **Root cause**: `ShowKeyboardStatus()` hardcoded "4 (WASD, Left, Right, Far)" with no model detection; `LinuxKeyboardController` had no concept of per-key RGB
- **Fix**: Added DMI product name detection against a per-key model database; keyboard status now correctly shows "Per-Key RGB" for known models (16-wf0xxx, Transcend, Max, etc.) and notes that USB HID per-key control is not yet available on Linux
- Reported by: Community — OMEN 16-wf0xxx / CachyOS (Discord)

### Fix: EC Timeout / Crash on Systems with Dead Battery
- **Root cause**: Multiple subsystems independently poll `Win32_Battery` WMI every 500ms (HardwareWorker, WmiBiosMonitor, PowerAutomationService). On dead/removed batteries, these queries traverse ACPI → EC → SMBus to the battery controller, which times out. Combined with HP WMI BIOS commands for fan/thermal control also hitting the EC bus, this causes cascading EC timeouts. Windows Power Management sees 0% charge and triggers "Critical Battery Action" (shutdown/hibernate), making OmenCore appear to crash the system
- **Fix**: Multi-layer dead battery protection:
  1. **Auto-detection** — all battery polling paths (HardwareWorker + WmiBiosMonitor) detect a dead battery after 3 consecutive 0% charge reads while on AC power and self-disable all further battery queries
  2. **Manual config** — new `Battery.DisableMonitoring: true` option in config.json immediately disables all battery WMI/EC queries at startup
  3. **Query cooldown** — WmiBiosMonitor battery queries throttled from 500ms to 10-second cooldown between calls
  4. **EC-safe AC detection** — `IsOnAcPower()` now uses `SystemInformation.PowerStatus` (kernel API, no EC access) before falling back to WMI
  5. **IPC coordination** — `DISABLE_BATTERY` command propagated to out-of-process HardwareWorker via named pipe IPC
- Reported by: Community — OMEN 15 (i7-9750H, RTX 2060, 2019) with dead battery (Discord)

---

## 📝 Technical Details

### Files Modified
- `src/OmenCoreApp/Services/OmenKeyService.cs` — WMI event handler fail-closed fix
- `src/OmenCoreApp/Hardware/WmiFanController.cs` — Auto restore, Quiet mapping, RPM gating, level-to-percent
- `src/OmenCoreApp/Hardware/HpWmiBios.cs` — V2 GetFanLevel fallback circular dependency fix
- `src/OmenCoreApp/Views/OsdOverlayWindow.xaml` — Named MainPanel for layout orientation
- `src/OmenCoreApp/Views/OsdOverlayWindow.xaml.cs` — Layout apply, net timer fix, FPS N/A fallback
- `src/OmenCore.Linux/Commands/DiagnoseCommand.cs` — Wider box, word-wrapping for notes
- `src/OmenCore.Linux/Commands/KeyboardCommand.cs` — Per-key detection in status display
- `src/OmenCore.Linux/Hardware/LinuxEcController.cs` — Unbuffered sysfs reads, hwmon percent fallback
- `src/OmenCore.Linux/Hardware/LinuxKeyboardController.cs` — Per-key RGB model detection
- `build-installer.ps1` — Linux Avalonia GUI build step
- `src/OmenCoreApp/Models/AppConfig.cs` — Added `Battery.DisableMonitoring` config option
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` — Dead battery auto-detection, query cooldown, EC-safe AC detection
- `src/OmenCore.HardwareWorker/Program.cs` — Dead battery auto-detection, DISABLE_BATTERY IPC command
- `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs` — SendDisableBatteryAsync() IPC method
- `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs` — Battery disable coordination with worker
- `src/OmenCoreApp/ViewModels/MainViewModel.cs` — Config-driven battery disable wiring

### Affected Models
- **Victus by HP Gaming Laptop 16-s0xxx** (Product ID 8BD5) — Auto mode, RPM readings
- **HP OMEN Transcend 14 2025** — Quiet = max fans
- **All models with Fn brightness keys** — Fn+F2/F3 window toggle
- **All V0/Legacy ThermalPolicy models** — Fan mode mapping
- **OMEN 15 (i7-9750H, 2019)** — Dead battery EC timeout / crash
- **Any model with dead/removed battery** — Battery monitoring auto-disable

---

## SHA256 Checksums
```
OmenCoreSetup-2.8.1.exe:        02EB81C7E1FBC232EBC5A07462494270B5E56020FB30FB4F5E4ACE9ECD649E54
OmenCore-2.8.1-win-x64.zip:     447925AE96940465FA9261D01FBB54E2056C8C5F501C2803C74D9BFBA3E96DC1
OmenCore-2.8.1-linux-x64.zip:   4D0445F29D5ED0FA10B5EC55EE86026AA5B137C49FE771976181EC4925A1526E
```
