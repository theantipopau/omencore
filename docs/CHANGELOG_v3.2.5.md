# OmenCore v3.2.5 - Stability, Model Support, and UX Improvements

**Release Date:** 2026-03-30
**Release Status:** ✅ Released (v3.2.5)
**Type:** Minor+ stability, hardware compatibility, and UX improvement release
**Base Version:** v3.2.1

---

## Summary

v3.2.5 closes a bundle of regressions accumulated since v3.2.1, adds hardware model support for previously misidentified HP product IDs, decouples fan control from performance mode selection, hardens the auto-updater, and introduces improvements to the Quick Access fan curve controls and performance mode button ordering.

This changelog uses a split format:
- **Top section** — proactive fixes, improvements, and new features from the dev team.
- **Bottom section** — targeted fixes directly sourced from user feedback (Discord reports and GitHub issues).

---

## 📦 Downloads & Artifacts

| File | Platform | SHA256 |
|------|----------|--------|
| `OmenCoreSetup-3.2.5.exe` | Windows Installer | `9BA9A36111358F24912174D341932DE1666260F8A5140A73418E7EB472EA8072` |
| `OmenCore-3.2.5-win-x64.zip` | Windows Portable | `01CF69CE5BB6A8A435C6816265029E14F9A24EB651E0098B4E86436AECA7C0D7` |
| `OmenCore-3.2.5-linux-x64.zip` | Linux (CLI + GUI) | `768F94CB97A8B684728E3C619C490AA10DE4F0541A4640A30E5CAFDD7F342AB0` |

→ **[View on GitHub Releases](https://github.com/theantipopau/omencore/releases/tag/v3.2.5)**

---

---

## ✨ New Features & Improvements

### 1. Performance Mode Buttons Reordered (Quiet → Balanced → Perform)
- **Change:** Performance mode button order in Quick Access and the main window now reads left-to-right as Quiet → Balanced → Perform, matching the natural low-to-high intensity progression users expect.
- **Previously:** Order was Balanced → Perform → Quiet (arbitrary/non-intuitive).
- **Status:** ✅ Fixed

### 2. OMEN-Tab Fan Curve Mode in Quick Access
- **Change:** Quick Access fan control now includes an additional "Custom" mode that applies the active OMEN-tab fan curve preset instead of forcing one of the three fixed modes (Auto / Max / Quiet).
- **Benefit:** Users who tune fan curves in the main OMEN tab can now activate them directly from the tray popup without navigating to the full window.
- **Status:** ✅ Fixed

### 3. Fan Control Decoupled from Performance Mode
- **Change:** Switching performance modes (Silent/Balanced/Turbo/Fans+Power) no longer silently overwrites a manually set fan mode or fan curve.
- **Detail:** Fan policy is now decoupled by default. Performance modes only control power plan and EC power limits. An optional `LinkFanToPerformanceMode` config setting (default `false`) restores the previous coupled behavior for users who prefer it.
- **Files:** `PerformanceModeService.cs`, `AppConfig.cs`, `MainViewModel.cs`
- **Status:** ✅ Fixed

---

---

## 🐛 Fixes & Improvements

### 4. Model Capability DB: Added OMEN 17 (2021) Intel (8BB1) Entry
- **Issue:** Product ID `8BB1` was absent from `ModelCapabilityDatabase`. Systems with this board ID silently fell back to `DefaultCapabilities`, which assumes full OMEN feature support — causing incorrect capability flags on affected hardware.
- **Fix:** Added explicit `8BB1` entry for OMEN 17 (2021) Intel with correct fan/RGB/power capability flags.
- **File:** `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- **Status:** ✅ Fixed

### 5. 8BB1 Collision: Victus 15-fa1xxx Misidentified as OMEN 17
- **Issue:** HP product ID `8BB1` is shared between OMEN 17 (2021) Intel laptops and Victus 15-fa1xxx (2022) laptops. Previously, any system with `8BB1` would receive the OMEN 17 keyboard and capability profile, causing incorrect RGB backend selection and wrong capability flags on Victus hardware.
- **Fix:** Added model-name pattern disambiguation logic:
  - `ModelCapabilityDatabase` — new virtual entry `8BB1-VICTUS15` with `ModelNamePattern = "15-fa1"` resolves before the raw product ID hit via `GetCapabilitiesByModelName()`.
  - `KeyboardModelDatabase` — new virtual entry `8BB1-VICTUS15` with `ModelNamePattern = "fa1"`, resolved via `TryDisambiguateByModelName()` when product ID is in `_ambiguousProductIds`.
  - `KeyboardLightingServiceV2.DetectModelConfig()` — now passes WMI model name to `GetConfig(productId, wmiModelName)` enabling disambiguation at detection time.
- **Result:** Victus 15-fa1xxx users with board ID `8BB1` now correctly receive `BacklightOnly` keyboard profile and Victus-family capability flags instead of OMEN 17 4-zone RGB profile.
- **Files:** `ModelCapabilityDatabase.cs`, `KeyboardModelDatabase.cs`, `KeyboardLightingServiceV2.cs`



## 🎯 Targeted Bug Fixes (User Feedback)
### 6. Auto-Updater: Hardened Asset Selection and SHA256 Enforcement
- **Issue:** In-app updater could fail with "invalid executable" even though the manual GitHub download succeeded. Root cause: three compounding bugs:
  1. `SelectPlatformAwareAsset()` only detected installer assets named with `"setup"`, and the fallback for installed-mode builds could incorrectly return the portable `.zip` archive — which cannot be executed as an installer.
  2. No response diagnostics were logged on download (Content-Type, final URL), so redirect-loop and error-page failures were opaque.
  3. Downloads with missing SHA256 in release notes were allowed to proceed without verification.
- **Fixes applied:**
  - Installer detection now also accepts assets containing `"installer"` in the filename (covers any future naming changes).
  - For installed-mode builds, asset selection is strict: only an installer `.exe` is returned; portable archives are explicitly excluded from the fallback path.
  - Response diagnostics logged on every download attempt: HTTP status code, Content-Type, and final URL after redirects.
  - A Content-Type warning is emitted if the download response is not a binary type (e.g., returns HTML — error page detected before saving the file).
  - Downloads are now **blocked** (return `null`) before any network call if no SHA256 hash is present in the release notes, preventing unverifiable installs. MainViewModel already shows "Update requires manual download (missing SHA256)" with the Open GitHub Release button active in this case.
- **Files:** `AutoUpdateService.cs`, `AboutWindow.xaml.cs`


---

### 7. Fan Diagnostics: RPM Calibration, Backend Reporting, and UX Clarity
- **Issues:**
  1. `FanVerificationService` used `MaxRpm = 4500` as the baseline for expected RPM calculations. On high-end OMEN 16/17 models where fans reach 5500–6500 RPM at full speed, this caused verification to report false negatives ("commands have no effect") even though the fan had responded correctly.
  2. The guided diagnostic result contained no information about which backend was used (WMI/EC) or the RPM source, making support triage difficult.
  3. The fan diagnostics view used a fixed-width horizontal `StackPanel` that clipped panels at smaller window widths or high DPI scaling.
  4. The "Guided Diagnostic" button label was unclear about what the test does.
- **Fixes applied:**
  - `MaxRpm` increased from 4500 to 6000. Budget HP/Victus fans (3800–4500 RPM) continue to pass via the 500 RPM absolute floor tolerance in `VerifyRpm()`.
  - Guided diagnostic result now includes backend name and RPM source on the first line.
  - `FanService.FanControlStateDescription` property added — exposes the current fan control state (thermal/diagnostic/curve/preset/auto) for diagnostic UIs.
  - Outer horizontal layout changed from rigid `StackPanel` to `WrapPanel` for responsive wrapping at smaller widths.
  - Left control column changed from fixed `Width="240"` to `MinWidth="200" MaxWidth="280"`.
  - "Guided Diagnostic" renamed to "Fan Verification Sequence" with descriptive subtitle.
  - "Run Full Test" button renamed to "Run Verification Sequence".
  - RPM source legend added to the current state panel (EC=Direct · HWMon=LHM · MAB=Afterburner · Est=Estimated).
- **Files:** `FanVerificationService.cs`, `FanDiagnosticsViewModel.cs`, `FanService.cs`, `FanDiagnosticsView.xaml`
- **Status:** ✅ Fixed

---

### 8. Post-Sleep High Fan RPM Regression: Watchdog Sleep/Wake Race
- **Issue:** After long sleep periods, the hardware watchdog could misinterpret suspend time as a frozen monitoring pipeline and force fans to 90% shortly after wake. On recovery, a race between watchdog recovery and monitoring-health recovery could leave repeated 90% writes latched until the user manually switched fan modes.
- **Root causes:**
  1. `HardwareWatchdogService` remained active across suspend, so a long sleep could satisfy the 90-second freeze threshold immediately after wake.
  2. The watchdog only set `_isWatchdogArmed = false` inside an async `Task.Run`, leaving a race window where duplicate timer ticks could queue extra 90% failsafe writes.
- **Fixes applied:**
  - Added explicit watchdog suspend/resume handlers and wired them into `MainViewModel` system power events.
  - On suspend: watchdog now disarms, clears freeze-breach state, and resets its heartbeat baseline.
  - On resume: watchdog re-arms with a 120-second grace period so sensor reattachment after wake is not treated as a hard monitoring freeze.
  - Closed the async race by setting `_failsafeActive` and `_isWatchdogArmed` before scheduling the failsafe write, preventing duplicate queued 90% writes after recovery.
- **Files:** `HardwareWatchdogService.cs`, `MainViewModel.cs`
- **Status:** ✅ Fixed

---

### 9. System Optimizer: Toggle Responsiveness and Per-Item Pending State
- **Issue:** Optimizer toggles could appear to do nothing or flip back unexpectedly. The root cause was a UI binding race: the toggle was bound two-way, so `OptimizationItem.IsEnabled` changed before the async action ran, and `SystemOptimizerViewModel` interpreted the new value as the previous state, calling apply/revert in the wrong direction.
- **Fixes applied:**
  - Changed optimizer toggles to one-way state binding so the UI no longer mutates the model before the async operation starts.
  - `SystemOptimizerView.xaml.cs` now captures the user’s intended state explicitly and snaps the visual toggle back to the last authoritative value until the apply/revert completes.
  - `SystemOptimizerViewModel.ToggleOptimizationAsync()` now takes `desiredState` directly, so apply vs revert is based on actual user intent rather than transient bound state.
  - Single-item toggles now use `OptimizationItem.IsApplying` instead of the full-page loading overlay.
  - Added an inline `Applying...` indicator per toggle and disabled only the active toggle while it runs.
  - Single-item toggles now refresh optimizer state without showing the global blocking overlay.
- **Files:** `SystemOptimizerViewModel.cs`, `SystemOptimizerView.xaml`, `SystemOptimizerView.xaml.cs`
- **Status:** ✅ Fixed

---

### 10. Bloatware Manager: Win32 Uninstall No-Op and False Success
- **Issue:** Some Win32 removals appeared to succeed while the application remained installed. The service treated most Win32 uninstallers as success regardless of exit code and never verified whether the app actually disappeared from uninstall registry keys.
- **Root causes:**
  1. MSI uninstall strings such as `msiexec /I {GUID}` were preserved as `/I`, which opens modify/repair flows instead of uninstall.
  2. Generic Win32 uninstall commands were only parsed correctly in a few cases, so unquoted `.exe` uninstallers with arguments could fail to launch cleanly.
  3. The service returned success without any post-uninstall verification, so the UI could report removal even when the app was still present.
- **Fixes applied:**
  - Added robust Win32 uninstall command parsing for quoted and unquoted `.exe` uninstallers.
  - MSI uninstall strings now rewrite `/I` to `/X` before execution so they actually uninstall.
  - Silent flags are appended only when they are missing.
  - Win32 uninstall now polls uninstall registry keys after execution and only reports success when the app is no longer detected.
  - Non-zero uninstaller exit codes are no longer treated as blanket success; the final outcome is based on actual post-uninstall state.
- **Files:** `BloatwareManagerService.cs`
- **Status:** ✅ Fixed

---
### [Discord - 2026-03-26] CPU Temperature Random Drops to 40°C on OMEN 17-ck1xxx
**Reported by:** Serg (Discord)
**System:** v3.2.1 · Windows 11 · OMEN 17-ck1xxx · Secure Boot enabled · HP background services running

**Symptom:**
> "On load temp is still 40. Few seconds later it becomes normal and keeps updating. Still from time to time it randomly drops to 40 for several seconds."

**Notes:** User observed improvement over previous version (previous: persistent stuck+spikes). Current behavior: transient ~40°C drops under load before reading normalizes, with occasional recurrence.

**Root-cause hypothesis:**
- The 40°C floor is characteristic of a WMI/ACPI CPU temperature path returning a stale or platform-clamped value during sensor arbitration.
- On OMEN 17-ck1xxx, the worker-backed sensor source can briefly lag behind or drop out, causing the pipeline to fall back to a stale WMI sample before the next poll cycle catches up.
- Similar to the fix applied for OMEN 16-xd0xxx in v3.2.1 (fix #3) and OMEN MAX 16-ah0000 in v3.1.0 (GitHub #78) — model-specific CPU temperature source override is the likely resolution path.

**Planned fix:**
- Added OMEN 17-ck1xxx to the model-specific CPU temperature source override list in `WmiBiosMonitor.ShouldPreferWorkerCpuTemp()`, alongside OMEN 17-ck2xxx, 16-xd0, and OMEN MAX 16. Worker-backed CPU temperature is now prioritized for affected models, preventing the WMI fallback from surfacing stale/clamped 40°C readings during active load.
- **Files:** `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- **Status:** ✅ Fixed

---

### [Discord - 2026-03-26] Quick Access Performance Mode Buttons — Wrong Order

**Reported by:** Serg (Discord) — feature request
**Symptom:** Performance mode buttons in Quick Access show Balanced → Perform → Quiet, which feels backwards. Request: reorder to Quiet → Balanced → Perform (low to high intensity, left to right).
**Status:** ✅ Fixed

---

### [Discord - 2026-03-26] Quick Access — Add OMEN Fan Curve as Fan Mode Option

**Reported by:** Serg (Discord) — feature request
**Symptom:** Quick Access fan section only offers Auto / Max / Quiet. Users who maintain a custom OMEN-tab fan curve have no way to activate it from the popup without opening the full window.
**Request:** Add a "Custom" or fan-curve option that activates the current OMEN-tab preset.
**Status:** ✅ Fixed

---

---

### 11. RTSS/Overlay Render-Thread Crash (UCEERR_RENDERTHREADFAILURE)
- **Issue:** Users with RivaTuner Statistics Server (RTSS) or MSI Afterburner overlay running would experience repeated WPF `UCEERR_RENDERTHREADFAILURE` (`0x88980406`) crashes shortly after launch, causing OmenCore to shut down. Root cause: RTSS injects D3D/DXGI hooks that corrupt the WPF hardware render channel.
- **Previous behaviour:** App treated RTSS as low-impact ("usually OK"), provided no targeted guidance, and shut down on the crash.
- **Fixes applied:**
  - RTSS conflict severity raised from **Low → High** with an accurate description of the render-thread risk.
  - On startup, OmenCore now detects RTSS processes (`RTSS`, `RTSSHooksLoader64`, `RTSSHooksLoader`) and automatically switches WPF to software rendering (`RenderMode.SoftwareOnly`) before any window is created, preventing the crash entirely.
  - `AppConfig.UseSoftwareRendering` flag added (default `false`) — users can also opt in permanently via config (Settings → General) without needing RTSS present.
  - `App.EnableSoftwareRendering()` is now a static helper callable from settings UI during an active session.
  - The `DispatcherUnhandledException` handler now intercepts `UCEERR_RENDERTHREADFAILURE` specifically: marks it as handled (no shutdown), activates software rendering for the rest of the session, and shows a targeted warning that identifies RTSS as the cause and explains how to permanently fix it.
- **Net result:** RTSS users no longer crash. The app warns once and continues running in software rendering mode.
- **Files:** `ConflictDetectionService.cs`, `App.xaml.cs`, `AppConfig.cs`
- **Status:** ✅ Fixed

---

### 12. Linux version was hardcoded to 3.1.0
- **Issue:** `OmenCore.Linux/Program.cs` had `public const string Version = "3.1.0"` regardless of the built assembly version, so `omencore-cli --version` always reported 3.1.0.
- **Fix:** Removed the hardcoded constant. Version is now read from `Assembly.GetName().Version` at runtime, driven by `<Version>3.2.5</Version>` in `OmenCore.Linux.csproj`. Also added `<AssemblyVersion>` and `<FileVersion>` for completeness.
- **Files:** `OmenCore.Linux.csproj`, `Program.cs`
- **Status:** ✅ Fixed

---

### 13. OMEN key false triggers from F24-sending software
- **Issue:** `IsOmenKey()` in `OmenKeyService` accepted VK_F24 (0x87) and VK_OMEN_157 (0x9D) unconditionally, without checking the scan code. Any software (game macro, input remapper) that sent a VK_F24 event could accidentally trigger OmenCore's OMEN key actions.
- **Fix:** Added `AppConfig.StrictOmenKeyMode` (default `true`). When strict mode is enabled, VK_F24 and VK_OMEN_157 are only accepted if their scan code is also present in `OmenScanCodes`. If the scan code does not match, the key event is rejected with a Debug log entry. Users experiencing hardware OMEN key non-detection can set `StrictOmenKeyMode: false` in config.
- **Files:** `AppConfig.cs`, `OmenKeyService.cs`
- **Status:** ✅ Fixed

---

### 14. Bloatware removal allowed without admin in non-admin sessions
- **Issue:** The bloatware manager showed a warning status message when not running as administrator, but both `RemoveSelectedAsync()` and `RemoveAllLowRiskAsync()` still proceeded to attempt removal, leading to silent failures or confusing errors deeper in the call stack.
- **Fix:** Both remove methods now have an early-return admin guard at the top: if `BloatwareManagerService.IsRunningAsAdmin` is false, `StatusMessage` is set to a clear user-facing explanation and the method returns immediately before any removal is attempted.
- **Files:** `BloatwareManagerViewModel.cs`
- **Status:** ✅ Fixed

---

### 15. LinkFanToPerformanceMode and UseSoftwareRendering not exposed in Settings UI
- **Issue:** Both `AppConfig.LinkFanToPerformanceMode` and `AppConfig.UseSoftwareRendering` existed in config since earlier fixes (fan-decoupling and RTSS crash fix respectively), but neither was wired into `SettingsViewModel` or `SettingsView.xaml`. Users had no way to toggle them from the UI.
- **Fix:**
  - `SettingsViewModel`: Added `LinkFanToPerformanceMode` and `UseSoftwareRendering` properties with proper backing fields, `LoadSettings()`, and `SaveSettings()` wiring. `UseSoftwareRendering` setter also calls `App.EnableSoftwareRendering()` so the session immediately switches to software rendering when toggled on.
  - `SettingsView.xaml`: Added **Software rendering mode** toggle in the General tab (after Headless mode) with a note that a restart is required for full effect. Added **Link fan to performance mode** toggle at the bottom of the Power Automation section.
- **Files:** `SettingsViewModel.cs`, `SettingsView.xaml`
- **Status:** ✅ Fixed

---

### 16. Fan curve apply now uses a verification kick
- **Issue:** Some HP laptops follow the diagnostics tab's one-shot `Apply & Verify` flow more reliably than a plain custom-curve apply, especially immediately after switching into Custom mode.
- **Fix:** Applying a custom fan curve now keeps the normal preset/curve engine path, then runs a one-shot post-apply verification kick using `FanVerificationService` at the current curve target percentage. The curve is reapplied after verification so ongoing automatic control is preserved. The same verification kick is now also used when loading saved custom presets from the fan tab preset dropdown. The fan control UI now shows the verification/apply result directly.
- **Files:** `FanControlViewModel.cs`, `FanControlView.xaml`, `MainViewModel.cs`
- **Status:** ✅ Fixed

---

### 17. Linked fan/performance mode is now explicitly visible across UI surfaces
- **Issue:** `LinkFanToPerformanceMode` existed and controlled behavior, but users could not reliably tell whether they were in linked or decoupled mode from key surfaces. Dashboard/main sidebar/quick popup/tray could appear ambiguous during mode changes.
- **Fix:**
  - Added shared state in `MainViewModel`: `IsFanPerformanceLinked` + `FanPerformanceLinkStatus` with runtime `RefreshLinkFanState()` synchronization.
  - Settings toggle for `LinkFanToPerformanceMode` now updates runtime behavior immediately (no restart needed), including `PerformanceModeService` linkage state.
  - Added explicit linked/decoupled indicator text in:
    - Main sidebar status card
    - Dashboard compact status strip
    - Quick popup header
    - Tray tooltip and fan-mode menu header suffix (`[linked]`)
  - App tray bridge now propagates `MainViewModel.IsFanPerformanceLinked` changes to `TrayIconService` so tray and popup stay in lockstep with dashboard/main UI.
- **Files:** `MainViewModel.cs`, `DashboardViewModel.cs`, `SettingsViewModel.cs`, `MainWindow.xaml`, `DashboardView.xaml`, `QuickPopupWindow.xaml`, `QuickPopupWindow.xaml.cs`, `TrayIconService.cs`, `App.xaml.cs`
- **Status:** ✅ Fixed

---

### 18. Bloatware bulk remove reports per-item outcomes and failure reasons
- **Issue:** After a bulk low-risk removal run, the status bar only showed a total count like "Removed 8 bloatware items" with no indication of which items succeeded or failed. Failed removals appeared silently and were indistinguishable in the list.
- **Fix:**
  - Added `RemovalStatus` enum (`NotAttempted`, `Pending`, `Succeeded`, `VerifiedSuccess`, `Failed`) and `LastFailureReason` string to `BloatwareApp`.
  - `RemoveAppAsync` in `BloatwareManagerService` now sets `LastRemovalStatus` and `LastFailureReason` with specific reasons (AppX stderr, Win32 exit-code + post-verify, exception message).
  - AppX path captures and maps "Access denied" (`0x80070005`) to a user-readable admin-escalation message.
  - Bulk remove summary in the status bar now shows e.g. "Completed: 6 succeeded, 2 failed — App A, App B. Export log for details."
  - Status column in the list view now shows **FAILED** (highlighted red) in addition to INSTALLED/REMOVED.
  - Added `ExportResultLogCommand` — writes a timestamped `.txt` file to `%LocalAppData%\OmenCore\Logs\` with pass/fail per item and failure reasons, then opens Explorer to the file. Button appears in the status bar after any removal attempt.
- **Files:** `BloatwareManagerService.cs`, `BloatwareManagerViewModel.cs`, `BloatwareManagerView.xaml`
- **Status:** ✅ Fixed

---

### 19. Fan Diagnostics result panel now readable at minimum window width
- **Issue:** The result history and guided test result panels could overflow or clip at narrow window widths, making failure reasons unreadable without horizontal scrolling.
- **Fix:**
  - Wrapped the entire diagnostics panel content in a `ScrollViewer` (`VerticalScrollBarVisibility=Auto`, `HorizontalScrollBarVisibility=Disabled`) so content reflows vertically rather than being cut off.
  - Removed the `MaxWidth="500"` constraint from the history error-message `TextBlock`, allowing it to use the full available width with `TextWrapping="Wrap"`.
- **Files:** `FanDiagnosticsView.xaml`
- **Status:** ✅ Fixed

---

### 20. Diagnostic export now includes model identity resolution trace
- **Issue:** Support diagnostics did not include the actual model-resolution decision path (raw identifiers, DB candidates, and final resolved profile), which made Product ID collisions and model-name disambiguation issues harder to triage remotely.
- **Fix:**
  - Added `CollectModelIdentityTraceAsync()` to diagnostics export flow.
  - Diagnostics bundles now include `identity-resolution-trace.txt` with:
    - Raw identity inputs (`Manufacturer`, WMI `Model`, `ProductName`, `SystemSku`, BIOS version)
    - Capability detection output (`ProductId`, `ModelName`, `ModelFamily`, known-model state)
    - Model capability DB candidate comparison (name-pattern candidate vs ProductId candidate vs effective resolved profile)
    - Explicit resolution path classification (name-pattern match, ProductId match, family fallback, default fallback)
    - Keyboard model resolution/disambiguation details (including ambiguous Product ID signal and matched keyboard profile)
- **Files:** `DiagnosticExportService.cs`
- **Status:** ✅ Fixed

---

### 21. Optimizer now has deterministic admin preflight and admin/non-admin tests
- **Issue:** Optimizer operations could proceed into low-level apply/revert paths without an explicit admin preflight gate, and roadmap coverage for non-admin/admin behavior was still missing.
- **Fix:**
  - Added explicit admin preflight checks in `SystemOptimizerService` for:
    - `ApplyGamingMaximumAsync`
    - `ApplyBalancedAsync`
    - `RevertAllAsync`
    - `ApplyOptimizationAsync`
    - `RevertOptimizationAsync`
  - Added injectable admin checker seam (`Func<bool>? isAdminChecker`) to make privilege-dependent behavior unit-testable.
  - Added `SystemOptimizerServiceAdminTests` covering:
    - non-admin single-action apply returns admin-preflight error,
    - admin path does not falsely fail preflight,
    - non-admin balanced preset returns deterministic preflight failure result.
- **Files:** `SystemOptimizerService.cs`, `SystemOptimizerServiceAdminTests.cs`
- **Status:** ✅ Fixed

---

### 22. Linux packaging now enforces VERSION.txt and emits version manifest
- **Issue:** Linux packaging/version consistency still had drift risk: Avalonia project metadata remained at `3.1.0`, UI had a stale fallback version literal, and build packaging did not emit a machine-readable release manifest.
- **Fix:**
  - `build-linux-package.ps1` now treats `VERSION.txt` as canonical and injects:
    - `-p:Version=$version`
    - `-p:AssemblyVersion=$assemblyVersion`
    - `-p:FileVersion=$assemblyVersion`
    into both Linux GUI and CLI publish steps.
  - Added `version.json` generation in `artifacts/` with version, assembly version, runtime, package filename, SHA256, and UTC timestamp.
  - Added packaging guardrails to fail when package naming does not include expected version/runtime or manifest generation fails.
  - Updated Avalonia baseline metadata to `3.2.5` and removed stale UI fallback version literal (`unknown` fallback now used until assembly version is loaded).
- **Files:** `build-linux-package.ps1`, `OmenCore.Avalonia.csproj`, `SettingsViewModel.cs`
- **Status:** ✅ Fixed

---

### 23. Linux packaging now includes executable version verification (archive vs CLI vs GUI)
- **Issue:** Packaging metadata checks existed, but there was no executable-level verifier proving runtime-reported CLI and GUI versions matched the archive/version metadata.
- **Fix:**
  - Added `qa/verify-linux-package.ps1`.
  - Verifier checks:
    - archive filename/version/runtime contract,
    - `artifacts/version.json` consistency,
    - CLI `--version` output,
    - GUI `--version` output.
  - Added `--version` support to Avalonia GUI entry point (`Program.cs`) so GUI version can be validated in pre-release checks.
  - Wired verifier into `build-linux-package.ps1` as a required step (with `-SkipBinaryVersionCheck` opt-out for local environments without Linux/WSL execution).
- **Files:** `qa/verify-linux-package.ps1`, `build-linux-package.ps1`, `src/OmenCore.Avalonia/Program.cs`
- **Status:** ✅ Fixed

---

### 24. Added updater regression automation and 30-minute stress harness scripts
- **Issue:** Regression gate items for updater-path validation and the 30-minute stress scenario required manual execution and lacked repeatable automation artifacts.
- **Fix:**
  - Added `qa/run-updater-regression.ps1`:
    - runs installer vs portable asset selection checks,
    - validates HTTP metadata and signature bytes (`MZ` for installer, `PK` for zip),
    - supports 3-run regression loops,
    - emits JSON/TXT reports under `artifacts/`.
  - Added `qa/run-stress-harness.ps1`:
    - runs timed stress sessions (default 30 minutes),
    - samples process health,
    - performs periodic updater checks,
    - scans logs for known regression signals,
    - emits session summary JSON/TXT artifacts.
  - Added commands and outputs to regression documentation.
- **Files:** `qa/run-updater-regression.ps1`, `qa/run-stress-harness.ps1`, `docs/REGRESSION_PACK_v3.2.5.md`
- **Status:** ✅ Fixed

---

### 25. Linux capability classification now distinguishes full/profile-only/telemetry-only boards
- **Issue:** On partial hp-wmi boards, Linux surfaces could still imply manual fan control even when firmware only exposed thermal profiles or telemetry paths.
- **Fix:**
  - Added a shared Linux capability classifier covering `full-control`, `profile-only`, `telemetry-only`, and `unsupported-control` states.
  - Wired the classifier into `omencore-cli status` and `omencore-cli diagnose`, including explicit reason text for missing manual fan control.
  - Avalonia Linux capabilities now expose the same classification so manual Fan Control navigation is hidden on non-manual boards while profile control remains available.
- **Files:** `src/OmenCore.Linux/Hardware/LinuxCapabilityClassifier.cs`, `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`, `src/OmenCore.Linux/Commands/StatusCommand.cs`, `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`, `src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs`, `src/OmenCore.Avalonia/Views/MainWindow.axaml`
- **Status:** ✅ Fixed

### 26. Linux GPU telemetry now reports its active fallback source
- **Issue:** Partial hp-wmi and newer Linux boards could degrade from hwmon GPU telemetry to EC or no telemetry at all without making the active source visible in `status` or `diagnose` output.
- **Fix:**
  - Added a shared Linux telemetry resolver that prefers hwmon and thermal-zone sensors, then falls back to EC reads only when they remain valid.
  - `omencore-cli status` JSON/human output now includes the active GPU telemetry source and path.
  - `omencore-cli diagnose` now records whether GPU telemetry is using fallback or is fully unavailable.
- **Files:** `src/OmenCore.Linux/Hardware/LinuxTelemetryResolver.cs`, `src/OmenCore.Linux/Hardware/LinuxHwMonController.cs`, `src/OmenCore.Linux/Commands/StatusCommand.cs`, `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`, `src/OmenCore.Linux/JsonContext.cs`
- **Status:** ✅ Fixed

### 27. Linux Avalonia startup now falls back more predictably and emits an actionable failure log
- **Issue:** Some Fedora/X11 users could launch the CLI successfully but see the GUI fail during Avalonia startup with GLX initialization errors and blacklisted `llvmpipe`, leaving little guidance beyond terminal noise.
- **Fix:**
  - Made Linux X11 rendering preference explicit as `EGL -> GLX -> Software` instead of relying on opaque platform defaults.
  - Added `OMENCORE_GUI_RENDER_MODE` override support for `software`, `egl`, `glx`, or `vulkan` when support needs a deterministic launch mode.
  - Disabled X11 session-management opt-in for the GUI startup path so `SESSION_MANAGER` noise does not distract from the actual rendering fault.
  - Wrapped startup in a failure reporter that prints an actionable stderr summary and writes a startup log with session/display/render context.
- **Files:** `src/OmenCore.Avalonia/Program.cs`, `docs/LINUX_INSTALL_GUIDE.md`
- **Status:** ✅ Fixed

---

### 28. F11/function-key false-trigger path is now explicitly blocked in OMEN key interception
- **Issue:** Users reported F11 and other function-key workflows could still be misclassified as OMEN key events in strict-mode edge cases and during nearby WMI event windows.
- **Fix:**
  - Added explicit never-intercept guard handling for F11/function-key class keys in OMEN key detection.
  - Added recent never-intercept key suppression window to reduce WMI false-positive follow-on events.
  - Added structured rejection logging for tuning (`reason` payload in candidate rejection traces).
  - Added regression coverage for F11 rejection, strict F24 scan mismatch rejection, and never-intercept WMI suppression.
- **Files:** `src/OmenCoreApp/Services/OmenKeyService.cs`, `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs`
- **Status:** ✅ Fixed

---

### 29. Worker startup/recovery logs now include session and correlation IDs
- **Issue:** Worker reconnect/startup diagnostics were hard to stitch together in noisy logs because startup and recovery messages had no operation-level correlation context.
- **Fix:**
  - Added per-process worker session ID in `HardwareWorkerClient` log formatting.
  - Added operation correlation IDs for startup and recovery/reconnect flows.
  - Routed startup/reconnect/restart log paths through a shared formatter to keep context consistent.
  - Added regression test coverage to ensure correlation/session tags remain present in formatted worker logs.
- **Files:** `src/OmenCoreApp/Hardware/HardwareWorkerClient.cs`, `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs`
- **Status:** ✅ Fixed

---

### 30. Linux Transcend 14-fb1xxx board 8E41 now gets the same safety classification and diagnostics as 8C58
- **Issue:** Community report (GitHub #99) showed OMEN Transcend 14-fb1xxx (`Board ID 8E41`) exposing hp-wmi telemetry but missing thermal/fan control paths; prior Linux guardrails and tailored diagnostics only matched board `8C58`.
- **Fix:**
  - Added `8E41` to the Linux unsafe EC board list so legacy EC writes stay blocked on this board family.
  - Updated Linux diagnose guidance to treat `8C58` and `8E41` consistently with Transcend 14 targeted recommendations.
  - Aligned Avalonia Linux unsafe-model detection to include `8E41` in the same safety path.
- **Files:** `src/OmenCore.Linux/Hardware/LinuxEcController.cs`, `src/OmenCore.Linux/Commands/DiagnoseCommand.cs`, `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`
- **Status:** ✅ Fixed

---

### 31. Added issue #99 follow-up diagnostic checklist for faster Linux triage closure
- **Issue:** Reporter follow-up for board 8E41 needs consistent command output and interface snapshots to confirm final capability-class behavior on real hardware.
- **Fix:** Added a ready-to-post issue response checklist with exact commands and expected verification targets.
- **Files:** `docs/ISSUE_99_FOLLOWUP_CHECKLIST.md`, `docs/LINUX_INSTALL_GUIDE.md`, `qa/collect-linux-triage.sh`, `docs/ROADMAP_v3.2.5.md`
- **Status:** ✅ Added

---

*This changelog is updated continuously as fixes land on the `dev/v3.2.5` branch.*
