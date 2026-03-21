# OmenCore v3.2.0 - Stability, Performance & Quality Release

**Release Date:** 2026-03-19
**Release Status:** Release Candidate (RC1)
**Type:** Minor release — stability, resource optimization, and UX improvements
**Base Version:** v3.1.1

---

## Summary

v3.2.0 is a broad-scope stabilization and quality release responding directly to post-3.1.1 community reports of high background CPU usage, stuck telemetry, and Victus fan lockups. It extends the fan safety system introduced in v3.1.1, hardens the hardware worker's monitoring loop, and introduces user-facing improvements: adaptive polling profiles, an explicit telemetry health banner on the dashboard, and completed Avalonia GUI workflows. All changes were validated with Release builds across all five projects with zero errors.

---

## Community Reports Addressed

- **Discord:** `OmenCore.HardwareWorker.exe` sustained high CPU usage until force-quit.
- **Discord:** CPU temperature stuck around 83°C until restart.
- **Discord:** 0 RPM/silent custom fan behavior unstable; fans cycling or app becoming unresponsive.
- **GitHub #85:** Defender detection `VulnerableDriver:WinNT/Winring0` on `OmenCore.HardwareWorker.sys` (risk acknowledged; documentation hardening in scope, driver bypass is a follow-up item).
- **GitHub #86:** Victus fan control unusable outside Auto/Max; fan lockups reported.
- **GitHub #89:** Victus 16-r0xxx keyboard light control not working — missing model in keyboard support database.
- **GitHub #90:** Microsoft Defender flagged OmenCore as trojan — false positive due to kernel driver presence.
- **GitHub #91:** OMEN MAX Gaming Laptop 16-ah0xxx (board '8D41') cannot set GPU power limit; appears set in UI but actual limit stays at 95W.
- **GitHub #92:** Avalonia GUI crashes on Ubuntu — `System.OperatingSystem.IsWindows()` not available in .NET version used.

---

## Fixed

### 1. HardwareWorker High CPU Usage Under Idle and Orphan Conditions
- **Issue:** `OmenCore.HardwareWorker.exe` consumed sustained CPU even with the main app disconnected or in background.
- **Impact:** Unnecessary battery drain and background resource usage visible in Task Manager.
- **Root Cause:** Worker polled sensors at full rate regardless of whether any client was actively requesting telemetry.
- **Fix Deployed:**
  - Added adaptive polling delay tiers based on client activity state:
    - Active client / recent activity: 500 ms
    - Idle (no recent client requests, ≥15 s): 1,500 ms
    - Orphaned + idle (parent process gone, ≥30 s idle): 3,000 ms
  - Worker now enters low-cadence mode automatically; resumes fast polling when a client reconnects.
- **File:** `src/OmenCore.HardwareWorker/Program.cs`
- **Status:** Fixed

### 2. Redundant Sensor Traversal in Worker Update Cycle
- **Issue:** Worker performed a global hardware visitor traversal in addition to per-device updates every poll cycle.
- **Impact:** Doubled sensor update work per cycle; increased CPU and allocation pressure in steady state.
- **Root Cause:** Vestigial `UpdateVisitor` global traversal not removed when per-device update paths were introduced.
- **Fix Deployed:**
  - Removed duplicate global visitor traversal; per-device update path is now the sole update mechanism.
- **File:** `src/OmenCore.HardwareWorker/Program.cs`
- **Status:** Fixed

### 3. CPU Temperature Stuck at Permanently High Value Until Restart
- **Issue:** CPU temperature displayed an implausibly high value that did not change despite low CPU load.
- **Impact:** Fan curve over-responds to phantom load; no recovery without app restart.
- **Root Cause:** Sensor freeze scenario where a stale "hot" reading persisted at zero CPU activity.
- **Fix Deployed:**
  - Added frozen-temperature heuristic: if CPU temp remains unchanged at ≥75°C while CPU load is ≤25% for 120 consecutive cycles (~60 s), the sample is marked stale and forced through the existing sensor recovery path.
  - Added a 120 s cooldown between recovery attempts to prevent thrash.
- **File:** `src/OmenCore.HardwareWorker/Program.cs`
- **Status:** Fixed

### 4. Fan Lockup on 0% / Manual Target Commands — Victus & OMEN (GitHub #86)
- **Issue:** On some Victus/OMEN firmware variants, setting fans to 0% via WMI left fans pinned in an unresponsive manual-zero state.
- **Impact:** Fans stop and cannot be recovered without a full restart or BIOS auto-restore.
- **Root Cause:** Affected firmware interprets manual 0% as a persistent zero-duty lock; it does not treat it as silent auto mode.
- **Fix Deployed:**
  - `SetFanSpeed(0)` now calls `RestoreAutoControl()` instead of writing manual 0%.
  - `SetFanSpeeds(0, 0)` now calls `RestoreAutoControl()`.
  - Custom curve points at 0% are remapped to BIOS auto restore instead of manual zero.
  - On V2 percentage-scale firmware, mixed single-channel 0% requests are safety-clamped to 1% to avoid the firmware lockup state.
- **File:** `src/OmenCoreApp/Hardware/WmiFanController.cs`
- **Status:** Fixed

### 5. HardwareWorker Error Cache Growing Unbounded in Long Sessions
- **Issue:** Error rate-limiting cache could grow without bound across multi-day sessions.
- **Impact:** Gradual memory increase in very long-running worker sessions.
- **Root Cause:** Cache entries were never evicted; growth relied solely on key reuse.
- **Fix Deployed:**
  - Added periodic pruning of entries older than 6 hours, executed every 15 minutes — keeping the cache flat in long-running sessions.
- **File:** `src/OmenCore.HardwareWorker/Program.cs`
- **Status:** Fixed

### 6. Linux Fan Curve Daemon — Unnecessary Allocations Per Poll Cycle
- **Issue:** Fan curve point list was re-sorted on every daemon poll cycle.
- **Impact:** Unnecessary heap allocations and CPU overhead in the Linux daemon hot path.
- **Root Cause:** `OrderBy()` was called inline on every `CalculateSpeedFromCurve()` invocation.
- **Fix Deployed:**
  - Added cached sorted curve points with a content-hash invalidation key; sorting only occurs when the curve actually changes.
- **File:** `src/OmenCore.Linux/Daemon/FanCurveEngine.cs`
- **Status:** Fixed

### 13. HP Victus GPU TGP Controls Incorrectly Available — "API Error" on Apply (Community Report)
- **Issue:** On HP Victus laptops (e.g., Victus 16 with RTX 3060 + Ryzen 5 5600H), the GPU Power Boost/TGP controls were accessible in the UI and appeared interactive. Attempting to apply a TGP level resulted in an opaque "API error" status message.
- **Impact:** Confusing UX — the user has no indication this is a hardware limitation rather than a software fault; the NVAPI power-policy call fails silently on Victus firmware.
- **Root Cause (three contributing factors):**
  1. `ModelCapabilityDatabase`: Victus 15 AMD (88DA) and Victus 16 (88DB) entries were missing `SupportsGpuPowerBoost = false` (field defaults to `true`).
  2. `DetectGpuPowerBoost()` never consulted the capability database. On Victus hardware, WMI BIOS is available (used for fan control) and can return a non-null GPU power value, incorrectly setting `GpuPowerBoostAvailable = true`.
  3. On apply, `NvAPI_GPU_ClientPowerPoliciesSetStatus` returns a non-OK result code on Victus firmware (BIOS does not expose custom TGP/PPAB), which surfaces as "an API error" in the UI.
- **Fix Deployed:**
  - `ModelCapabilityDatabase.cs`: Added `SupportsGpuPowerBoost = false` and `SupportsUndervolt = false` to both 88DA (Victus 15 AMD) and 88DB (Victus 16) entries, making them consistent with the explicit 88D9 (Victus 15 Intel) entry.
  - `SystemControlViewModel.DetectGpuPowerBoost()`: Added early-exit guard at the top of the method — if `SystemInfoService.IsHpVictus` is true, immediately sets `GpuPowerBoostAvailable = false` and status `"Not supported — HP Victus BIOS does not expose custom TGP/PPAB control"`, preventing any hardware probe.
  - `README.md`: Added HP Victus note to the Compatibility section: fan control/monitoring/backlight work; GPU TGP/PPAB and CPU undervolting unavailable.
- **Files:** `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`, `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`, `README.md`
- **Status:** Fixed

---

## Improved

### 7. Buffered Async Logging in HardwareWorker
- **Change:** Replaced repeated synchronous `File.AppendAllText` calls in the worker with a queue-backed background writer thread that flushes in batches up to 128 lines.
- **Impact:** Worker main loop is no longer blocked by disk writes during busy monitoring periods; I/O overhead is reduced significantly under noisy sensor conditions.
- **File:** `src/OmenCore.HardwareWorker/Program.cs`

### 8. UI Collection Refresh De-duplication (MainViewModel)
- **Change:** Added `SyncCollection<T>` helper that skips clear/re-add when the incoming collection is identical to the current one.
- **Impact:** Reduced UI-thread churn and visual flicker during recurring config and device refresh events.
- **Applied to:** Fan presets, performance modes, lighting profiles, system toggles, device lists, macro profiles, and fan curve loading.
- **File:** `src/OmenCoreApp/ViewModels/MainViewModel.cs`

### 9. User-Selectable Monitoring Polling Profiles
- **Change:** Added a **Polling profile** selector in Settings → Monitoring with four options:
  - **Performance** — 500 ms interval, live charts always enabled
  - **Balanced** *(default)* — 1,000 ms interval, live charts enabled
  - **Low overhead** — 2,000 ms interval, live charts hidden; ideal for battery use
  - **Custom** — reflects any manually configured interval or low-overhead combination
- Profile selection is saved across restarts and takes effect immediately at runtime.
- **Files:** `src/OmenCoreApp/Models/MonitoringPreferences.cs`, `src/OmenCoreApp/Services/HardwareMonitoringService.cs`, `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`, `src/OmenCoreApp/Views/SettingsView.xaml`

### 10. Explicit Stale/Degraded Telemetry Banner on Dashboard
- **Change:** Dashboard now shows a warning banner when monitoring enters `Stale` or `Degraded` health state.
  - Banner includes a plain-language description and confirms OmenCore is attempting automatic recovery.
  - Banner sits in its own dedicated layout row — no overlap with the quick-status card.
- **Files:** `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`, `src/OmenCoreApp/Views/DashboardView.xaml`

### 11. Avalonia Fan Preset Save Workflow (Linux GUI)
- **Change:** Implemented a working `Save Preset` command in the Avalonia fan control view.
  - Custom curves can now be saved under a name derived from the active preset (with a time-based suffix to avoid collisions).
  - Saved presets appear immediately in the preset selector and are applied after saving.
- **Files:** `src/OmenCore.Avalonia/Services/FanCurveService.cs`, `src/OmenCore.Avalonia/ViewModels/FanControlViewModel.cs`

### 12. Linux RGB Capability Detection and Desktop Settings Persistence
- **Changes:**
  - Linux `GetCapabilitiesAsync()` now probes sysfs (`/sys/class/leds`) for HP zone-based and per-key RGB indicators instead of a single hard-coded path, enabling accurate capability detection across more OMEN lighting variants.
  - Avalonia desktop Settings view now loads and saves user preferences to `settings.desktop.json` and performs a live update check against GitHub Releases.
- **Files:** `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`, `src/OmenCore.Desktop/Views/SettingsView.axaml.cs`

### 14. Post-RC Quick Wins: Tray UX and Worker IPC Efficiency
- **Changes:**
  - Tray GPU Power submenu is now hidden on unsupported hardware (for example HP Victus), preventing no-op actions and confusing API errors from tray paths.
  - Tray version fallback string updated from `3.1.0` to `3.2.0` for VERSION-file-missing edge cases.
  - `HardwareWorkerClient.SendRequestAsync()` now uses `ArrayPool<byte>` for 64 KB pipe buffers to reduce per-request allocations in the monitoring hot path.
  - Worker restart cooldown now uses progressive backoff (2s → 5s → 10s → 20s → 30s), improving resilience during repeated startup failures.
- **Files:** `src/OmenCoreApp/App.xaml.cs`, `src/OmenCoreApp/Utils/TrayIconService.cs`, `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs`

### 15. Hotfixes for GitHub Issues #89–#92 (Post-Release)

#### 15a. GUI Crash on Ubuntu / .NET Compatibility (GitHub #92)
- **Issue:** Avalonia GUI crashes on Ubuntu with `System.MissingMethodException: Method Not Found: 'Boolean System.OperatingSystem.IsWindows()'`.
- **Root Cause:** `System.OperatingSystem.IsWindows()` is a .NET 5+ API not available in earlier versions. Code referenced Windows-only checks that use this newer API.
- **Fix Deployed:**
  - `DriverInitializationHelper.cs`: Replaced `OperatingSystem.IsWindows()` with `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`, which is available in .NET Standard 2.1+.
  - `GameLibraryService.cs`: Applied same replacement for Windows platform check.
- **Files:** `src/OmenCoreApp/Hardware/DriverInitializationHelper.cs`, `src/OmenCoreApp/Services/GameLibraryService.cs`
- **Impact:** Avalonia GUI now starts successfully on Ubuntu and Linux platforms.

#### 15b. Victus 16-r0xxx Keyboard Light Control (GitHub #89)
- **Issue:** Victus 16-r0xxx (Ryzen 2024+) keyboard light control not working — model not in keyboard support database.
- **Root Cause:** The Victus 16 Ryzen variant (product ID 8C2F) was missing from `KeyboardModelDatabase`.
- **Fix Deployed:**
  - Added Victus 16 (2024+) Ryzen r0xxx entry with product ID `8C2F` to KeyboardModelDatabase.
  - Configured for 4-zone RGB with ColorTable2020 preferred method and EC Direct fallback.
- **File:** `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs`
- **Impact:** Victus 16-r0xxx keyboards can now be configured with custom colors.

#### 15c. OMEN MAX 8D41 GPU Power Control Logging (GitHub #91)
- **Issue:** OMEN MAX Gaming Laptop 16 (8D41) GPU power limit appears to be set in the UI but actual GPU power stays at 95W default.
- **Investigation:** The model is correctly flagged to use WMI-only paths (not EC), since EC registers have a different layout on this 2025 model.
- **Partial Fix Deployed:**
  - Enhanced logging in `HpWmiBios.SetGpuPower()` to emit detailed debug output of GPU power command bytes and results.
  - Added detailed status messages to help diagnose whether the WMI command is being sent and what response is received.
  - This logging will help identify if the issue is upstream (command not being sent) or downstream (BIOS not responding to the command).
- **File:** `src/OmenCoreApp/Hardware/HpWmiBios.cs`
- **Status:** Partially addressed — full resolution requires further investigation and possibly BIOS-specific workarounds for the 8D41 model. Logging now enabled for user debugging.
- **Note:** Users experiencing this issue should check OmenCore logs for output from `SetGpuPower` to confirm the command is being sent.

#### 15d. Defender False Positive — Trojan Detection (GitHub #90)
- **Issue:** Microsoft Defender flags OmenCore as a trojan on initial download/install.
- **Root Cause:** Kernel driver presence is detected as suspicious behavior; this is expected and is a false positive.
- **Fix Deployed:**
  - Enhanced [ANTIVIRUS_FAQ.md](ANTIVIRUS_FAQ.md) with specific guidance for GitHub #90.
  - Added clear explanation that trojan/heuristic detection is a false positive due to legitimate kernel driver usage.
  - Provided whitelisting steps and reassurance that OmenCore is open-source and safe.
  - Added reference to VirusTotal scanning and reporting channels.
- **File:** `docs/ANTIVIRUS_FAQ.md`
- **Impact:** Users now have clear guidance on whitelisting OmenCore in Defender and understanding why the detection occurs.

---

## Known Issues / Open Risks

### GitHub #85 — Windows Defender Detection of WinRing0 Driver Path
- **Status:** Triaged — documentation and startup messaging improvements in v3.2.0; driver-path bypass mode is a follow-up item.
- **Notes:** Windows Defender may flag the legacy WinRing0 path as `VulnerableDriver:WinNT/Winring0`. OmenCore's default stack since v2.7.0 uses PawnIO, which does not trigger this alert. The detection appears when LibreHardwareMonitor's WinRing0 fallback is loaded. See [ANTIVIRUS_FAQ.md](ANTIVIRUS_FAQ.md) for steps to exclude the folder or verify PawnIO is active.
- **Workaround:** Ensure the PawnIO driver is installed via the OmenCore installer or Settings → Hardware.

### GitHub #91 — OMEN MAX 8D41 GPU Power Limit Not Applying
- **Status:** Under investigation — enhanced logging added for user diagnostics.
- **Notes:** Users report GPU power stays at 95W despite UI showing "set to performance". Logging is now enabled to capture the WMI command and response for debugging. This may require BIOS-specific workarounds or a different WMI method for 2025 OMEN MAX models.
- **Workaround:** Check logs for GPU power command output; report findings to help develop a firmware-specific fix.

---

## Validation Checklist (Completed)

| Project | Build result |
|---------|-------------|
| `OmenCore.HardwareWorker` | ✅ 0 errors, 0 warnings |
| `OmenCoreApp` | ✅ 0 errors, 0 warnings |
| `OmenCore.Linux` | ✅ 0 errors, 0 warnings |
| `OmenCore.Avalonia` | ✅ 0 errors, 0 warnings |
| `OmenCore.Desktop` | ✅ 0 errors, 0 warnings |

---

## Files Changed

| File | Change area |
|------|-------------|
| `src/OmenCore.HardwareWorker/Program.cs` | Adaptive polling, no-dup traversal, frozen-temp recovery, error cache pruning, buffered logging (items 1–5, 7) |
| `src/OmenCoreApp/Hardware/WmiFanController.cs` | Fan safety — 0% remapping and V2 safety clamp (item 4) |
| `src/OmenCoreApp/ViewModels/MainViewModel.cs` | Collection sync helper (item 8) |
| `src/OmenCoreApp/ViewModels/SettingsViewModel.cs` | Polling profile logic and runtime apply (item 9) |
| `src/OmenCoreApp/ViewModels/DashboardViewModel.cs` | Stale/degraded banner state properties (item 10) |
| `src/OmenCoreApp/Views/SettingsView.xaml` | Polling profile ComboBox selector (item 9) |
| `src/OmenCoreApp/Views/DashboardView.xaml` | Stale banner and dedicated row (item 10) |
| `src/OmenCoreApp/Services/HardwareMonitoringService.cs` | Runtime polling interval update API (item 9) |
| `src/OmenCoreApp/Models/MonitoringPreferences.cs` | `PollingProfile` field (item 9) |
| `src/OmenCore.Linux/Daemon/FanCurveEngine.cs` | Cached sorted curve points (item 6) |
| `src/OmenCore.Avalonia/ViewModels/FanControlViewModel.cs` | Preset save command (item 11) |
| `src/OmenCore.Avalonia/Services/FanCurveService.cs` | `SavePreset` interface/implementation (item 11) |
| `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs` | sysfs RGB detection methods (item 12) |
| `src/OmenCore.Desktop/Views/SettingsView.axaml.cs` | Settings load/save + update check action (item 12) |
| `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` | Add `SupportsGpuPowerBoost = false` to Victus 88DA and 88DB entries (item 13) |
| `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs` | `DetectGpuPowerBoost()` early exit for HP Victus (item 13) |
| `README.md` | HP Victus compatibility note in Compatibility section (item 13) |
| `src/OmenCoreApp/App.xaml.cs` | Tray initialization sync now respects `GpuPowerBoostAvailable` and updates GPU submenu visibility (item 14) |
| `src/OmenCoreApp/Utils/TrayIconService.cs` | Added `SetGpuPowerAvailable`; updated version fallback to 3.2.0 (item 14) |
| `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs` | Pooled pipe read buffer and progressive worker restart cooldown (item 14) |

---

## Download & Integrity Verification

**Release Artifacts:** 

| Artifact | Size | SHA256 Hash |
|----------|------|----------|
| `OmenCore-3.2.0-win-x64.zip` (portable) | 104.3 MB | `5428BAE69931B62C7BB637452FDDC7FC2F4CEA3CE3F735A7EA4A575C89B99B9D` |
| `OmenCoreSetup-3.2.0.exe` (installer) | 101.1 MB | `05282E9EA6FDC73EA63558D90E747D3176DBD7E19D56722E552BDE6B2A9A077B` |
| `OmenCore-3.2.0-linux-x64.zip` (Linux) | 43.6 MB | `CBC49B7AEB0B8C2209C3D94B67C36F69517B6C1041B93B73793F9F4675B6883C` |

**Verification:** 
```bash
# Linux
sha256sum -c OmenCore-3.2.0-linux-x64.zip.sha256

# Windows (PowerShell)
(Get-FileHash -Path "OmenCore-3.2.0-win-x64.zip" -Algorithm SHA256).Hash
(Get-FileHash -Path "OmenCoreSetup-3.2.0.exe" -Algorithm SHA256).Hash
```
