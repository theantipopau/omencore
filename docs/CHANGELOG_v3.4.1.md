# OmenCore v3.4.1 - Release Notes

**Version:** 3.4.1
**Release Date:** 2026-04-30
**Release Status:** Release-ready - code, tests, Windows installer, Windows portable package, and Linux package verified
**Previous Release:** v3.4.0 (2026-04-27)
**Type:** Targeted hotfix / stability release

---

## Overview

v3.4.1 is a targeted hotfix for regressions reported against v3.4.0, focused on fan-control correctness, profile/UI synchronization, keyboard hotkey filtering, RGB/peripheral reliability, and model-specific support gaps reported through GitHub.

The April 28 logs from an OMEN 16-xd0xxx system showed two high-risk fan behaviors: Max fan mode could issue the expected command and then fail verification or roll back, while Auto/Default transitions could write `SetFanLevel(0, 0)` and temporarily collapse RPM before firmware recovered.

---

## Fixed in Code

### Linux GUI Startup Diagnostics, KDE Rendering Triage, and UI Resource Use
**Issue:** https://github.com/theantipopau/omencore/issues/119
**Reporter:** davix-03
**Platform:** Fedora 43 KDE Spin
**Status:** Improved in code; Fedora KDE validation still required

**Symptom:**
The Linux Avalonia GUI opens to a black screen on Fedora 43 KDE Spin since v3.1.1.

**3.4.1 change:**
The Linux GUI now writes startup breadcrumbs to `gui-startup.log` throughout the launch path, including framework initialization and main-window creation. This gives black-screen reports useful diagnostics even when Avalonia does not throw a startup exception. Dashboard sensor updates are now marshalled onto the Avalonia UI thread, the session uptime timer uses a UI dispatcher timer instead of a background loop, and Linux hardware polling is non-overlapping with a calmer interval to reduce render contention and background resource usage.

The Linux UI also avoids emoji-dependent navigation/status glyphs in the main dashboard, fan-control, settings, and system-control views. This makes the first screen more deterministic on KDE installs with limited color emoji/font fallback support.

A `v3.1.1` to `v3.2.0` tag comparison identified the Linux RGB capability probe as the most likely regression candidate in that window: v3.2.0 added a synchronous recursive `/sys/class/leds` scan during GUI capability loading. v3.4.1 keeps RGB detection but makes it bounded to known top-level LED entries and multicolor metadata files so startup cannot be delayed by a deep sysfs walk.

**Validation target:**
On Fedora 43 KDE Spin, the GUI should either render normally or leave a clear startup trace under the OmenCore log directory for renderer/session triage. If the compositor still shows a black window, the new log should confirm whether the app reached `app.main-window.created` and `app.framework-init.completed`.

---

### Runtime Dependency Hardening
**Severity:** Medium
**Affects:** Windows installer packaging and Linux GUI telemetry
**Status:** Improved in code; installer artifact validation required

**Goal:**
OmenCore should not require external helper applications for normal operation. On Windows, core control should use HP WMI BIOS, native Windows APIs, the bundled hardware worker, and optional bundled PawnIO for EC/MSR access. OMEN Gaming Hub, WinRing0, LibreHardwareMonitor.exe, and other standalone tools should not be runtime requirements.

**3.4.1 change:**
The Linux Avalonia GUI no longer shells out to `nvidia-smi`, `lspci`, `prime-select`, `pkexec`, `/bin/sh`, or `tee` from the hardware service. GPU telemetry now uses sysfs/DRM/hwmon paths only, and distro-specific GPU mode switching reports as unsupported instead of invoking external tools.

The Windows first-run driver prompt now describes PawnIO as an optional bundled installer component for advanced EC/MSR features rather than sending users to an external download page. The installer build script now fails release packaging if `installer\PawnIO_setup.exe` is missing, ensuring installer builds include the only intended driver dependency.

**Clarification:**
The bundled hardware worker is an OmenCore component, not an external dependency. PowerShell, `schtasks`, `taskkill`, `shutdown`, and Explorer launches may still be used for explicit Windows maintenance workflows such as startup-task configuration, bloatware cleanup, reboot prompts, or opening exported files; they are operating-system utilities, not required third-party runtime dependencies for core fan/profile/RGB monitoring.

---

### Max Fan Mode No Longer Persists a Failed Max Preset
**Severity:** High
**Affects:** v3.4.0 users on WMI BIOS fan-control systems
**Reported:** Community report, April 2026
**Status:** Fixed in code; hardware validation required

**Symptom:**
Selecting Max fan mode attempts to ramp the fans, but RPM then drops back toward idle instead of holding maximum cooling.

**3.4.1 change:**
`FanService.ApplyPreset` now returns whether the hardware apply path actually succeeded. `FanControlViewModel` only updates `ActiveFanMode` and persists the last preset when the service confirms success, so a failed Max verification is surfaced instead of silently saving a misleading Max state.

**Validation target:**
On affected hardware, Max should either hold a firmware-confirmed maximum fan state or fail visibly without saving Max as the last preset.

---

### Auto Fan Mode Avoids Explicit Zero-Level V1 WMI Writes
**Severity:** High
**Affects:** v3.4.0 users switching back to Auto/Default fan control
**Reported:** Community report, April 2026
**Status:** Fixed in code; hardware validation required

**Symptom:**
Switching to Auto fan mode causes fan RPM to drop to 0, then recover to idle RPM after a delay.

**3.4.1 change:**
V1 WMI auto/default handoff paths no longer call `SetFanLevel(0, 0)`. OmenCore now lets BIOS auto mode resume control without sending an avoidable zero-duty command.

**Validation target:**
Auto/Default transitions should not create a transient 0 RPM dip. RPM may settle naturally under firmware control.

---

### Performance Profile and Fan Sidebar State Stay Synchronized
**Severity:** High
**Affects:** v3.4.0 users switching Balanced, Performance, or other performance profiles
**Reported:** Community report, April 2026
**Status:** Fixed in code; UI/hardware validation required

**Symptom:**
Selecting Balanced or another performance profile updates the fan mode shown in the left sidebar, but the Omen tab does not reflect the same profile state.

**3.4.1 change:**
Balanced profile application now consistently uses the `Balanced` mode name instead of mapping through `Default`, and `MainViewModel` updates the current performance mode even when the dashboard view model is not present. Tray quick-profile actions now flow through the same profile naming path.

**Validation target:**
Sidebar, Omen/System tab, fan tab, and persisted config state should agree after apply, rollback, startup restore, and hardware readback refresh.

---

### Brightness Keys No Longer Match the OMEN Launch Path
**Severity:** High
**Affects:** Models where Fn brightness keys emit LaunchApp-style key events
**Reported:** Community report, April 2026
**Status:** Fixed in code; Transcend 14 validation required

**Symptom:**
Fn+F2 and Fn+F3 still open or toggle OmenCore on affected systems.

**3.4.1 change:**
Strict hotkey detection now rejects ambiguous `VK_LAUNCH_APP1`/`VK_LAUNCH_APP2` events with the dedicated OMEN scan code, and always rejects brightness function keys even if they arrive with an OMEN-like scan signature. This keeps brightness handling with Windows instead of treating those keys as app launch.

**Validation target:**
Fn+F2 and Fn+F3 must adjust brightness only and must not toggle OmenCore.

---

### Transcend 14 Fn+F12 and Fn+P Hotkey Support
**Severity:** Medium
**Affects:** OMEN Transcend 14 with RTX 5060 / Intel Core Ultra 7
**Reported:** Community report, April 2026
**Status:** Fixed in code; model validation required

**Symptom:**
On a new OMEN Transcend 14 system, Fn+F2 and Fn+F3 still open OmenCore. The user also expects Fn+F12 to open OmenCore, matching OMEN Gaming Hub behavior, and Fn+P to cycle performance profiles.

**3.4.1 change:**
Fn+F12 with the dedicated OMEN scan signature is now accepted as the app-launch/toggle hotkey. Firmware Fn+P profile-cycle support is enabled by default, and the WMI listener now remains active for the narrow Fn+P firmware event even when the low-level keyboard hook is active.

**Validation target:**
Fn+F2/F3 pass through to brightness, Fn+F12 opens/toggles OmenCore when exposed as the OMEN key, and Fn+P cycles profiles if the firmware emits the expected WMI event.

---

### GitHub #120 - HP OMEN 15-en0038ur Model Support
**Issue:** https://github.com/theantipopau/omencore/issues/120
**Reporter:** sashaflyer
**Model:** HP OMEN Laptop 15-en0038ur
**Product ID / Baseboard:** 8787
**Status:** Fixed in code; fan RPM readback still pending hardware verification

**3.4.1 change:**
Added dedicated capability coverage for product/baseboard `8787`, including legacy WMI fan control, performance modes, MUX support, GPU power boost, and 4-zone RGB support. Added a matching keyboard database entry for 2020 OMEN 15 AMD 4-zone ColorTable lighting.

**Validation target:**
Model detection should no longer fall back to `Unknown Legacy Model`, and lighting/fan capabilities should be selected from the dedicated `8787` entries. RPM telemetry remains marked as not verified until hardware confirms reliable readback.

---

### RGB and Peripheral Provider Reliability
**Severity:** Medium
**Affects:** Cross-brand RGB sync, OpenRGB optional peripherals, diagnostics for 4-zone follow-up
**Status:** Improved in code; physical RGB validation still required

**3.4.1 change:**
RGB provider failures are no longer silently swallowed by the cross-brand sync manager. Sync completion now tracks attempted, succeeded, and failed provider counts, and provider status summaries include structured connection state plus human-readable details for the UI and logs.

OpenRGB integration has been hardened as an optional peripheral bridge: packet payloads are size-checked, response headers are validated, controller discovery is bounded, OpenRGB string parsing validates field lengths, LED updates are clamped to a safe maximum, and partial device write failures are recorded without taking down the rest of RGB sync.

Follow-up items pulled forward from the older RGB reliability roadmap are also included: temperature-responsive and throttling indicator lighting now coalesce repeated monitor ticks so OmenCore does not write the same RGB state to every provider on every telemetry sample, performance-mode lighting events are marshalled to the UI thread before touching bound state, and OpenRGB packet reads/writes have explicit async I/O timeouts so an optional peripheral server cannot leave a sync operation hanging indefinitely.

The RGB page now shows a control-center style sync status strip with active target count, last sync outcome, and timestamp/detail tooltip. The "Sync All RGB" path now routes registered providers through `RgbManager` once, then applies the OMEN keyboard separately, avoiding duplicate provider writes while giving users clearer success/partial-failure feedback.

The HP WMI BIOS ColorTable path now retries readback verification after a short firmware settle delay. This keeps stale immediate readback from incorrectly marking a successful 4-zone write as failed while still preserving failure details when verification continues to disagree.

The OpenRGB UI copy now describes it as optional generic peripheral support. Core OMEN controls and HP keyboard lighting do not require OpenRGB or vendor RGB applications.

**Validation target:**
On systems with 4-zone OMEN keyboards and optional peripherals, one unavailable or failing provider should not block other RGB targets. Status details should make it clear whether the issue is HP keyboard output, optional OpenRGB server availability, or a device-level write failure.

---

## Physical Validation Watchlist

### 4-Zone RGB Physical Output Validation
**Severity:** High
**Affects:** v3.4.0 systems with 4-zone keyboard lighting
**Status:** Code improved; physical output validation still requested from affected hardware

The April 28 OMEN 16-xd0xxx logs show 4-zone detection and V2 WMI BIOS ColorTable activation, but the reported physical lighting output still needs validation on affected hardware. No broad 4-zone write-path change is included in this pass beyond the new `8787` keyboard database entry.

---

### GitHub #119 - Fedora KDE Physical Validation
**Issue:** https://github.com/theantipopau/omencore/issues/119
**Reporter:** davix-03
**Platform:** Fedora 43 KDE Spin
**Status:** Code improved; Fedora KDE validation still requested from affected desktop hardware

The user reports that OmenCore has shown only a black screen since v3.1.1 despite trying multiple fixes. v3.4.1 adds startup breadcrumbs, safer UI-thread updates, calmer Linux polling, and less font-fragile visible UI text, but this still needs validation on Fedora 43 KDE Spin.

**Expected outcome:**
Confirm whether the blank screen is resolved. If not, collect `gui-startup.log` and determine whether the remaining cause is renderer selection, display/session detection, missing dependency, or Avalonia compositor behavior.

---

## Validation Checklist

- [x] Max fan mode fails visibly without persisting a misleading Max preset when verification fails.
- [x] Auto/Default fan transitions avoid `SetFanLevel(0, 0)` writes on V1 WMI systems.
- [x] Balanced/Performance profile naming and UI state synchronization paths are corrected in code.
- [x] Brightness keys are blocked from the OMEN launch hotkey path in strict detection.
- [x] Fn+F12 OMEN launch and Fn+P firmware profile-cycle support are wired in code.
- [x] Linux GUI now records startup breadcrumbs for black-screen triage.
- [x] Linux RGB capability detection no longer performs recursive `/sys/class/leds` scans during GUI startup.
- [x] Linux GUI hardware telemetry no longer depends on `nvidia-smi`, `lspci`, or distro GPU-switching utilities.
- [x] Windows installer build fails if bundled PawnIO installer payload is missing.
- [x] Linux dashboard updates are marshalled to the UI thread, and hardware polling no longer overlaps itself.
- [x] Linux visible navigation/status glyphs avoid emoji font fallback.
- [x] RGB sync manager records provider success/failure counts instead of silently swallowing optional peripheral errors.
- [x] Optional OpenRGB peripheral bridge validates packets, bounds discovery, and reports partial write failures.
- [x] Temperature/throttling RGB automation coalesces repeated hardware writes to reduce provider churn and background resource usage.
- [x] OpenRGB SDK packet I/O is timeout-bounded so optional peripheral sync cannot hang indefinitely.
- [x] HP WMI BIOS 4-zone ColorTable verification retries once after firmware settle time before reporting mismatch.
- [x] RGB page exposes active target count and last sync outcome, and Sync All avoids duplicate provider writes.
- [x] Application, tray, installer, hardware worker, and packaged `VERSION.txt` metadata now report v3.4.1 consistently.
- [x] Logitech direct HID no longer throws during static initialization when no Logitech device is present.
- [x] RGB provider startup reuses existing Corsair/Logitech device services instead of double-probing optional peripherals.
- [x] Release logs no longer point users to external PawnIO downloads for bundled-driver flows.
- [x] OMEN 15-en0038ur issue #120 has model and keyboard database coverage.
- [x] Windows release build succeeds.
- [x] Test suite rerun after RGB/peripheral hardening passes.
- [x] Windows installer artifact builds successfully.
- [x] Windows portable artifact builds successfully.
- [x] Linux package artifact builds and passes package verification.

Non-blocking hardware follow-up:

- [ ] Hardware validation confirms Fn+F2/F3 pass through, Fn+F12 opens OmenCore, and Fn+P cycles profiles on OMEN Transcend 14.
- [ ] 4-zone RGB produces visible output on the reported systems, not just successful detection logs.
- [ ] Fedora KDE black-screen issue #119 is validated on affected desktop hardware or captured with the new startup trace.
- [ ] Hardware validation is recorded for fan Max, Auto, profile sync, brightness hotkeys, and 4-zone RGB.

---

## Release Verification

| Metric | Result |
|---|---|
| Release build | Passed: `dotnet build OmenCore.sln -c Release --no-restore` |
| Test suite | Passed after release-log and RGB/peripheral hardening: `dotnet test src\OmenCoreApp.Tests\OmenCoreApp.Tests.csproj --no-restore --verbosity minimal` (238 passed) |
| Windows installer | Built: `artifacts\OmenCoreSetup-3.4.1.exe` |
| Windows portable package | Built: `artifacts\OmenCore-3.4.1-win-x64.zip` |
| Linux package | Built and package-verified: `artifacts\OmenCore-3.4.1-linux-x64.zip` |
| Packaged version metadata | Passed: `VERSION.txt` = 3.4.1; app/worker `FileVersion` = 3.4.1.0; `ProductVersion` = 3.4.1 |
| Latest Windows log review | Improved: v3.4.1 startup, no Logitech direct-HID exception, no stale PawnIO URL, monitoring healthy, clean shutdown |
| OMEN 16-xd0xxx fan validation | Pending hardware validation |
| Transcend 14 hotkey validation | Pending hardware validation |
| 4-zone RGB validation | Pending hardware validation |
| Linux Fedora KDE startup validation | Improved in code; pending Fedora KDE validation |
| OMEN 15-en0038ur model validation | Pending hardware validation |

---

## Release Artifacts

| Artifact | SHA256 |
|---|---|
| `OmenCoreSetup-3.4.1.exe` | `41E54DA4A25E38496BE22C64A6B899F2645B984AF5CCB56E794C2B99584F08D2` |
| `OmenCore-3.4.1-win-x64.zip` | `F3E6AAC73DD44EB52B231EAF7413B98518DDAF749BFF38AE04ABC5861FFC9028` |
| `OmenCore-3.4.1-linux-x64.zip` | `7783DF9D0B0A877CA180D4B6FDE234646AFD355D61DD6EAB8898D39577EC019A` |

---

## Source Inputs

- Community v3.4.0 regression report, April 2026.
- Community OMEN Transcend 14 hotkey report, April 2026.
- `C:\Users\matthew.hurley\Downloads\OmenCore_20260428_073558.log`
- `C:\Users\matthew.hurley\Downloads\OmenCore_20260428_165729.log`
- `C:\Users\matthew.hurley\AppData\Local\OmenCore\logs\OmenCore_20260430_154412.log`
- `C:\Users\matthew.hurley\AppData\Local\OmenCore\HardwareWorker.log`
- GitHub #119: https://github.com/theantipopau/omencore/issues/119
- GitHub #120: https://github.com/theantipopau/omencore/issues/120
