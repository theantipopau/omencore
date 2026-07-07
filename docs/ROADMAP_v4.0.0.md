# OmenCore 4.0 Roadmap

## Vision

Version 3.9.x proved OmenCore can ship real fixes fast, but it also exposed how much the project runs on one person's manual judgment: every hardware confirmation goes through Discord, every new SKU gets a hand-written database entry, the app runs full-admin because a privilege-separation plan was scoped and never built, and the codebase has one god-object (`MainViewModel`) holding ~46 manually-wired services. 4.0's job is to make the *next* three years of this project sustainable rather than adding more features on the same foundation: reduce how much depends on the maintainer personally being in the loop (privilege separation, semi-automated evidence-gating, a community-contributable model database), finish or retire the half-built surfaces (Corsair/Razer/Logitech RGB, the Avalonia desktop shell, Linux tray/config), close the two real security gaps found in this audit (unsigned binaries, an auto-update hash sourced from the same channel as the binary it verifies), and pay down the specific, named technical debt below rather than doing a vague "refactor pass."

## How This Document Is Organized

- **Community-Requested Features** — direct Discord asks (Balanced fan mode, Linux tray, i18n).
- **Owner-Identified Priorities: RGB and OC/UV Hardening** — gaps in third-party lighting and overclock/undervolt persistence, cross-referenced against a prior roadmap that already scoped several of these and was never acted on.
- **Strategic Priorities: Architecture and Process** — the five highest-leverage structural changes (privilege separation, RGB provider architecture, evidence-gate automation, community model database, first-run capability disclosure).
- **Security and Trust Hardening** — code signing, auto-update trust chain, firmware-write test coverage.
- **4.0.0 Sweep: Performance/Tech-Debt and Feature/UX Findings** — the god-object, timer sprawl, allocation hotspots, and the feature/UX gaps found by reading the live code.
- **Tray Menu Friction and OSD/FPS Sync Gaps** — a live user report, traced through the code, with one item still needing a fresh repro log before it can be fixed.
- **How To Tackle This / Execution Checklist** — read this before starting any item below; it defines the phase order and the verification bar.
- **Appendix: Original Drafting Brief** — the prompt this document was originally commissioned from, kept for provenance. Not itself roadmap content.

--------------------------------------------------

## Community-Requested Features (Post-3.9.0)

Separate from the architectural cleanup brief in the Appendix — these are user-facing feature requests from Discord, tracked here as 4.0-cycle candidates since none are quick fixes. See [docs/CHANGELOG_v3.9.0.md](CHANGELOG_v3.9.0.md) ("Post-Release Feedback / Follow-Ups") for full context on the first two.

### Dedicated Balanced Fan Mode (decoupled from Auto/BIOS-controlled)

- **Request:** `OsamaBiden` (O16 - xd0010ax), Discord `#design-feature-requests`. Add a distinct Balanced *fan* mode tied to the Balanced performance profile instead of silently mapping it to Auto fan mode, so users don't see 0 RPM and mistake it for a fault.
- **Why not a quick fix:** `FanPerformanceLinkMapper.MapPerformanceModeToFanMode` falls back to Auto deliberately — most models in `ModelCapabilityDatabase.cs` don't have a field-verified safe non-Auto duty/curve. This project's evidence-gate rule treats unverified fan-behavior changes as needing per-model field validation before shipping (see `AllowV1AutoModeFloorClear` and the `UserVerified` notes pattern).
- **Scope:** New `FanMode.Balanced` (or reuse of a verified low-duty curve) gated per-model in `ModelCapabilityDatabase.cs`, plus `FanPerformanceLinkMapper` changes. Needs field evidence from priority boards before it can graduate past experimental.

### Linux: Tray Minimize + Config Persistence + Background Service

- **Request:** Discord feedback — keep `omencore-gui` running in the background on Linux with saved config, the way the Windows app does.
- **Current state:** `omencore-gui` (`src/OmenCore.Avalonia`) has no tray icon or config persistence today. Tray only exists as an external shell script (`src/OmenCore.Linux/scripts/omencore-tray.sh`, using `yad`/`libappindicator`/`pystray`) that isn't integrated into the GUI binary. Config persistence (TOML, schema-versioned) exists only in the separate CLI/daemon (`src/OmenCore.Linux/Config/OmenCoreConfig.cs`).
- **Scope:** (1) Avalonia `TrayIcon`/`NativeMenu` integration in the GUI itself — `src/OmenCore.Desktop`'s `CloseToTray` toggle (`SettingsView.axaml.cs`) is a usable reference pattern; (2) GUI-side config load/save wired to the same TOML store the daemon uses; (3) systemd unit/packaging decisions for running headless. Medium-large — real new work, not a bug fix.

### UI Localization / Translations (Spanish first)

- **Request:** Discord feedback — add translations (Spanish suggested first) given the program's Spanish-speaking user base.
- **Current state:** No localization infrastructure exists in the codebase — all UI strings are hardcoded English literals directly in XAML (`GeneralView.xaml`, `SettingsView`, etc.) and C# code-behind/viewmodels. There is no resource-file (`.resx`)/`x:Uid` scaffolding, no culture-switching mechanism, and no translation-contribution process.
- **Scope:** This is a foundational, cross-cutting change, not a per-string patch:
  1. Introduce a resource-based string system (WPF `.resx` + `x:Static`/binding, or a lightweight custom `ILocalizationService` keyed lookup) and migrate hardcoded UI strings across every view — General, OMEN/Fan, Lighting, Tuning, Settings, tray menus, OSD, notifications, and diagnostics export labels.
  2. Add a language/culture selector in Settings, persisted to config, with a safe fallback to English for missing keys.
  3. Produce and maintain the initial translation set (Spanish first); decide on a contribution workflow for community-submitted translations (PR-based `.resx`/JSON files) since maintaining every language in-house won't scale.
  4. Watch for length/layout breakage — several views (profile cards, stat tiles, tray tooltips) are tuned tightly around current English string lengths.
- **Estimated complexity:** Large. Touches nearly every XAML file and is the kind of change best done once during a 4.0 UI pass rather than piecemeal, to avoid re-touching the same views twice.

--------------------------------------------------

## Owner-Identified Priorities: RGB and OC/UV Hardening

Raised directly by the project owner rather than a Discord report. Cross-referenced against `docs/ROADMAP_v3.3.0.md` (Enhancements #14–#17, #20 — never closed in any later changelog), `docs/3.8.0-CORE-CONTROLS-NEXT-STEPS.md`, and the 3.8.0/3.8.1 bug-report docs. These are real, previously-identified gaps, not new speculation — several were scoped two roadmaps ago and simply never got picked up.

### RGB: Third-Party Device Support

**Corsair (`Services/Corsair/ICorsairSdkProvider.cs`, `CorsairICueSdk`)** — Lighting/device discovery is real (RGB.NET-backed), but:
- `GetDeviceStatusAsync()` on the *real* SDK class (not just `CorsairSdkStub`) hardcodes `BatteryPercent = 100`, `PollingRateHz = 1000`, `FirmwareVersion = "Unknown"` behind a `// TODO: Query device status via iCUE SDK` — needs the native iCUE SDK (RGB.NET doesn't expose device telemetry), not a quick fix.
- `ApplyDpiStagesAsync` / `ApplyMacroAsync` explicitly log "not supported via RGB.NET" — macro upload and DPI stage config are unimplemented for the real path, would need a different SDK surface entirely.
- ROADMAP_v3.3.0.md Enhancement #16 already flagged per-key/per-zone handling as "still stub-like rather than model-verified."
- **Complexity:** medium (status readback) to large (macro/DPI).

**Razer (`Services/Rgb/RazerRgbProvider.cs`, `Razer/RazerService.cs`)** — The weakest of the three per ROADMAP_v3.3.0.md Enhancement #14: not fully implemented, depends on Synapse + Chroma Connect being installed/running, the UI itself calls it "placeholder functionality," and discovered devices are category placeholders rather than verified physical-device enumeration. No real per-key/reactive effect implementation confirmed anywhere in the codebase.
- **Complexity:** large — closest to a ground-up implementation of the three.

**Logitech (`Services/Rgb/LogitechRgbProvider.cs`, `Services/Logitech/LogitechHidDirect.cs`, `LogitechDeviceService.cs`)** — Direct-HID path is heuristic and marked WIP in places; DPI control is not actually implemented; status data is mostly defaulted. `SupportedEffects` is also thinner than Corsair/Razer (only Static/Breathing/Spectrum/Off — no Wave/Reactive).
- **Complexity:** medium.

### RGB: Cross-Cutting Architecture

- **"Sync All RGB" double-write risk (ROADMAP_v3.3.0.md Enhancement #17):** the Sync-All path applies colors both directly per-provider *and* through `RgbManager`, risking duplicate writes or inconsistent results across Corsair/Logitech/Razer/OMEN when synced together. Should be unified to a single write path before adding more third-party providers on top of this.
- **OMEN's own per-key HID backend (Enhancement #20, `KeyboardLightingServiceV2`)** is still not implemented for the laptop's own keyboard — separate from the third-party providers above. `docs/3.8.0-CORE-CONTROLS-NEXT-STEPS.md` confirms this is intentional: "keep the HID per-key backend write-only and conservative until real OMEN MAX PIDs and segment behavior are confirmed." This is the same evidence-gate pattern as fan control — needs field PIDs before it can move past conservative/write-only.
  - **[GitHub #151](https://github.com/theantipopau/omencore/issues/151) is exactly that evidence for board `8D41` (OMEN MAX 16-ah0xxx).** Exceptionally detailed report (Linux, but the finding is hardware-level and applies to the Windows path too): the keyboard RGB on this board is driven by an independent USB HID controller — **Darfon Electronics `0d62:54bf`** ("HP Gaming Keyboard II"), 5 HID report interfaces (`hidraw2`–`hidraw6`) — completely separate from the ACPI/WMI zone mechanism that correctly drives the chassis light bar (zones 0–3 confirmed working; zones 4–7, mapped to the keyboard, are accepted into the driver's internal state — readable back after ~0.3s — but never reach the physical keyboard, which stays in its factory rainbow-cycle). Reporter also confirmed this isn't Linux-specific: HP's own OMEN Light Studio (Windows, tested via a DMI-spoofed VM to bypass the VM-identity gate) recognizes the chassis but explicitly reports the keyboard/HID region as unsupported too — i.e. even HP's current shipping software doesn't have this exact keyboard controller in its compatibility list yet. Reporter has offered to capture USB HID feature reports (via `usbhid-dump`/Wireshark+usbmon while toggling effects in Light Studio) to reverse-engineer the real feature-report format if that would help. **This directly resolves the "RGB routing to light bar instead of keyboard" evidence gate already logged for 8D41 in `docs/CHANGELOG_v3.9.0.md`'s hardware-gated section (BUG-3810-005 item) — the missing `[HidPerKey] PID 0x????` is now known: `0d62:54bf`.** Next step: take the reporter up on the HID report capture offer before writing the `KeyboardLightingServiceV2` backend for this PID.
- **RGB "surface" ambiguity** (light bar vs. keyboard zones vs. single backlight) causing false failure reports is partially mitigated by the 3.8.0 observed-surface probe, but only for HP's own surface — third-party devices have no equivalent self-diagnosis today.

### Overclocking / Undervolting

- **AMD GPU (ADL2) has no persistence/startup-reapply path at all**, unlike NVIDIA which has (confusingly) four different save/apply mechanisms (Save Profile / Apply / Test-Apply-then-Keep). `docs/3.8.1-BUG-REPORTS.md` (UX-3810-001) — 3.8.1 clarified the UX labeling but explicitly deferred physical-hardware validation. Files: `GpuOcService`, `SystemControlViewModel.cs`, `GpuOc`/`GpuOcProfiles` config. **Complexity:** medium.
- **AMD CPU undervolt (`AmdUndervoltProvider.cs`) is manual-apply-only** — no Curve Optimizer/undervolt startup persistence, a real feature gap versus the NVIDIA/Intel paths. **Complexity:** medium.
- **CPU PL1/PL2 per-model readback gaps:** several 3.8.0 bug reports (Victus `8DCD`, OMEN `878C`, `8C30`) show PL1/PL2 overrides deliberately withheld because there's no diagnostics/readback proving correct values for that board — gated by policy, not a bug. The actionable fix is building a self-validating readback/telemetry loop (`PowerLimitController.cs`, `PerformanceModeService.cs`) instead of requiring a manual field report per model every time. **Complexity:** large, but concrete.
- **GPU TGP reported read-only/locked on some OMEN 16 boards** (3.8.1 items #5–8) — up to ~2x battery power draw vs. OMEN Gaming Hub because neither PL1/PL2 nor GPU TGP can be lowered. Firmware/NVAPI limitation, but OmenCore has no detection UX for it beyond a generic "unavailable" — better messaging (medium) is achievable even if the underlying lock (large/maybe impossible) isn't.
- **Per-model safety-gate string matching is fragile:** the `IsSensitiveModel()` bug fixed in 3.8.1 (matched only the literal substring "OMEN 16", missed real WMI strings) is good precedent for an audit pass across *all* OC/UV/fan safety gates that match on model-name substrings rather than ProductId, since the same fragile-match bug class could exist elsewhere undetected. **Complexity:** small, but should happen before any of the above ship.
- `docs/3.8.0-CORE-CONTROLS-NEXT-STEPS.md` already calls for "the next field pass to validate live rollback readback on Intel MSR, NVIDIA NVAPI, AMD SMU/ADL, and WMI fan-control systems" — the rollback bundle exists but is unvalidated in the field for AMD and several boards; this should gate any new OC/UV work rather than be an afterthought.

--------------------------------------------------

## Strategic Priorities: Architecture and Process

Broader, higher-leverage items beyond individual feature gaps — these change how the project scales rather than what any one feature does. Roughly in priority order.

### 1. Reduce the Admin Requirement / Privilege Separation

`src/OmenCoreApp/app.manifest` still hard-requires `requestedExecutionLevel level="requireAdministrator"`. A real spike already exists — `docs/PRIVILEGE_SEPARATION_SPIKE.md` — proposing that EC/WMI/NVAPI writes move into a limited-privilege background service (candidate: extend the existing `OmenCore.HardwareWorker` process, which today is used for update/kill-loop handling, not privileged fan/RGB writes) that the UI talks to over a named pipe, while the UI process itself drops to standard user rights. The spike was written, scoped with concrete next steps ("prototype a minimal SetFanSpeed RPC," "audit all `ManagementObjectSearcher`/PInvoke call sites requiring elevation"), and never executed.

**Why this matters more than a typical backlog item:** it's the most likely lever on the Defender/AV false-positive friction the project already maintains a dedicated FAQ for (`docs/ANTIVIRUS_FAQ.md`, `docs/DEFENDER_FALSE_POSITIVE.md`) — a full-admin process doing direct EC/WMI writes is exactly the profile AV heuristics flag. It's also a recurring trust objection for a third-party hardware-control tool in general. See also "Security and Trust Hardening" below — code signing and privilege separation attack the same underlying trust problem from two different angles and should be planned together.

**Complexity:** large — touches installer (service registration), auto-update (service lifecycle), and every direct hardware call site across the app. Should be scoped as its own multi-release effort, not a single 4.x milestone.

### 2. A Real Provider/Plugin Architecture for RGB

Rather than continuing to hand-build Corsair/Razer/Logitech integrations one at a time inside core (see "Owner-Identified Priorities: RGB and OC/UV Hardening" above — Razer near-stub, Corsair missing telemetry/macro surfaces, Logitech heuristic HID), define a common provider interface with isolated failure domains, so a crashing or missing third-party SDK degrades gracefully instead of risking the whole app. Isolating providers out-of-process (piggybacking on the privilege-separation work above, or the existing `HardwareWorker`) would also mean a vendor SDK crash can't bring down the main UI process.

**Benefit:** turns future vendor support into an additive, isolated change instead of another core-team integration project each time. Also directly de-risks Enhancement #17's double-write problem (`RgbManager` vs. direct per-provider writes) by forcing a single write path through the provider boundary.

**Complexity:** large — this is an architectural change to `Services/Rgb/*`, not a per-vendor patch.

### 3. Semi-Automate the Evidence-Gate Process

Nearly every fan/OC/UV/RGB fix across the last several changelogs ends with "needs field confirmation from the reporter" — a slow, manual, Discord-dependent loop that puts the project owner personally in the critical path for every hardware confirmation. A generalized self-validating pattern — apply a change, read back the actual hardware state, compare against the expected range, and surface a confidence level in diagnostics exports — would let a meaningful fraction of "please confirm this fixed it" cases resolve from the user's own diagnostics bundle instead of a back-and-forth conversation.

**Complexity:** medium to start (build the apply→readback→confidence pattern for one subsystem, e.g. fan mode, as a template) — large if generalized across fan/OC/UV/RGB uniformly.

### 4. Community-Contributable Model Database

`ModelCapabilityDatabase.cs` is hand-maintained per-ProductId, sourced from individual Discord reports — every new SKU currently requires the project owner personally writing an entry. A structured contribution path (a JSON/schema-validated capability file format, a PR template, and a validation script that checks a submitted entry's shape before it's reviewed) would let the community submit capability data directly instead of funneling everything through one person.

**Complexity:** medium — mostly tooling and process (schema + validator + contribution docs), not a runtime architecture change.

### 5. First-Run Capability Disclosure

A significant fraction of bug reports across the BUG-REPORTS docs amount to "why doesn't X work" where the honest answer is a hardware/firmware limitation (locked GPU TGP, no direct EC access, no RGB per-key support on that board) rather than an OmenCore defect. Surfacing what the detected model actually supports — and doesn't — on first run (or in a persistent "Capabilities" view) would pre-empt a chunk of these reports and set expectations before the user goes looking for a setting that isn't there for their hardware.

**Complexity:** small to medium — mostly a UI/UX addition on top of data (`ModelCapabilityDatabase.cs`) that already exists.

--------------------------------------------------

## Security and Trust Hardening

Found during the final 4.0.0 audit pass — distinct from the AV-friction/privilege-separation items above, though all three ultimately attack the same "why should I trust this app with admin rights" problem from different angles.

### Code Signing — Unsigned Today

`docs/ANTIVIRUS_FAQ.md` states plainly: OmenCore binaries are a "niche unsigned utility with low reputation," while PawnIO (the bundled kernel driver) is signed. No `signtool`/certificate references exist anywhere in `build-installer.ps1` or `installer/OmenCoreInstaller.iss`. This is the root cause the existing AV-friction FAQ is working around rather than fixing, and it also blocks closing the auto-update trust gap below (a signed installer plus a signature check before elevated execution is the real fix there, not just a hash check).

**Complexity:** large — cost of a code-signing certificate (an EV cert gets immediate SmartScreen reputation; a standard cert builds reputation slowly over time), plus a CI/build-pipeline signing step. This is a cost/process decision as much as an engineering one — flag for the project owner rather than just scheduling engineering time.

### Auto-Update Trust Gap — Hash Sourced From the Same Channel as the Binary

`Services/AutoUpdateService.cs` is otherwise solid: HTTPS to `api.github.com`, the update source is hardcoded/pinned to `theantipopau/omencore`, and the download is genuinely gated on SHA256 verification (`versionInfo.Sha256Hash` blocks the download if absent; `ComputeSha256Hash` compares post-download and throws a `SecurityException` on mismatch), plus PE/ZIP header sniffing and a minimum-size check. **The gap:** the expected SHA256 hash is extracted via regex from the GitHub release *notes body* (`ExtractHashForAsset`) — the same channel that serves the binary. Anyone who can edit or create a release (or a compromised maintainer GitHub account) controls both the artifact and the value that's supposed to verify it. This protects against transport corruption or a compromised CDN, but not a compromised release pipeline. There's also no Authenticode signature check on the installer before the elevated (`runas`) install step — only the MZ header is checked.

**Fix:** once code signing exists (above), add an Authenticode signature check before elevated install execution — that closes the gap even if the release-notes hash were somehow tampered with, since the binary itself would need a valid signature from the project's own cert.

**Complexity:** medium, but blocked on code signing existing first.

### Firmware/BIOS-Write Path Has Zero Test Coverage

Scanning `src/OmenCoreApp.Tests/Services/` for RGB providers finds real coverage (`CorsairHidDirectTests`, `CorsairHidDpiTests`, `CorsairRgbProviderTests`, `LogitechRgbProviderTests`, `RgbManagerTests`, `RgbNetSystemProviderTests`), consistent with Corsair/Logitech being partially-built rather than pure stubs. But there is no test file for `BiosUpdateService` — the firmware-write path — at all. This is the single highest-risk untested surface in the codebase: a bug here doesn't just misbehave in software, it can affect the laptop's BIOS. `installer/OmenCoreInstaller.iss` and `build-installer.ps1` also have no automated test/verification of installer correctness.

**Complexity:** medium — writing tests for a firmware-write path safely (mocking the actual write, testing the validation/guard logic around it) is more design work than typical unit tests, but is high-value given the risk.

### Telemetry/Privacy and Config Secrets — Already Handled Well

Checked and confirmed clean, no roadmap action needed beyond a documentation link: `docs/TELEMETRY.md` and `TelemetryService.cs` show telemetry is opt-in (off by default), stored locally only (`%LOCALAPPDATA%\OmenCore\telemetry.json`), with no network upload code anywhere in `TelemetryService.cs` (no `HttpClient`/`PostAsync`) and a Settings UI toggle backed by its own test (`SettingsViewModelTelemetryTests.cs`). `config/default_config.json` contains only hardware/fan/performance-mode data — no keys, tokens, or credentials. **Optional small polish:** link `TELEMETRY.md` from the README for discoverability, since the practice is already good but not surfaced where a privacy-conscious user would look first.

--------------------------------------------------

## 4.0.0 Sweep: Performance/Tech-Debt and Feature/UX Findings

A follow-up sweep across the live codebase (not just prior docs) to ground out the tech-debt brief in the Appendix and find feature/UX gaps beyond RGB/OC-UV. Confirmed against current code, not speculation.

### Performance / Tech Debt

- **`MainViewModel` is a god-object composition root.** `App.xaml.cs`'s DI container registers only 3 things (`RuntimeStateEngine`, `MainViewModel`, `MainWindow`) — it's barely used. `MainViewModel.cs` is ~5,300 lines and manually holds ~46 service-typed fields, wiring and lifetime-managing them itself instead of a real composition root doing it. This is the actual shape of the "service sprawl" the original tech-debt brief worried about. **Largest architectural finding in this sweep — large effort, high value.**
- **21 independent timers, no shared scheduler.** 10 `DispatcherTimer` (UI) + 11 `System.Threading.Timer` (background) instances across `TrayIconService`, `DashboardViewModel`, `OsdOverlayWindow` (2), `QuickPopupWindow`, `ProcessMonitoringService`, `MemoryOptimizerService` (2), `MsiAfterburnerService`, `RgbSceneService`, and others. At least 4 separate producers poll at ~1-2s cadence on uncoordinated schedules (tray, OSD stats, quick popup, process monitoring) instead of sharing one cadence source. **Medium/large** — a coalesced polling coordinator was already proposed in `docs/3.7.0-UI-PERFORMANCE-AUDIT.md` ("Proposed Next Simplifications #1") and never built.
- **Per-tick allocations in the fan monitoring loop.** `FanService.ApplyCurveIfNeededAsync`/`ForceApplyCurveNowAsync` (`Services/FanService.cs` ~lines 909-914, 1856-1875) allocate fresh lists via `.ToList()`/`.Select().ToList()`/`Enumerable.Repeat().ToList()` on every ~2s monitoring tick even when fan state hasn't changed. `WmiBiosMonitor.cs` (~line 1635) does similar `.Where().ToList()`/`.Except().ToList()` work per tick. **Small/medium** — cache reusable buffers, only rebuild on fan-count change.
- **Sync-over-async blocking calls.** Bounded-timeout `.Wait()`/`.Result` at `CapabilityDetectionService.cs:1112/1114`, `WmiBiosMonitor.cs:1100/1152`, `AudioReactiveRgbService.cs:535`, `ScreenColorSamplingService.cs:569`, `DiagnosticLoggingService.cs:95`. All have timeouts (lower deadlock risk than naked `.Wait()`), but still block a thread — worth auditing which run on the UI thread. **Medium.**
- **`WmiBiosMonitor.TryRestartAsync()`** — flagged in `docs/V3_ARCHITECTURE_REVIEW.md` (RC-7) as possibly a no-op stub that just resets an error counter rather than actually restarting the WMI session. Needs re-verification against current code. **Medium.**
- No illegitimate `async void` found (all matches are legitimate event handlers/`ICommand.Execute`) and no unconditional hot-path string-interpolated logging found — two suspected issues from the original brief that turned out **not** to be current problems. No action needed.

### Feature / UX Gaps

- **`OmenCore.Desktop` (newer Avalonia shell) has largely non-functional primary controls.** Dashboard/FanControl/Keyboard/Settings views under `src/OmenCore.Desktop/Views/*.axaml.cs` are literal stubs: `// TODO: Apply performance mode via hardware service`, `// TODO: Apply fan profile`, `// TODO: Apply manual fan speeds`, `// TODO: Call keyboard service`, `// TODO: Trigger driver reinstallation`. If this shell is exposed to any real users today, most of its primary buttons do nothing. **Large — this is "finish the Avalonia port," not a small fix. Needs a decision: is this shell shipped/visible today, or dormant?**
- **Linux GPU mode switch and keyboard brightness throw raw `NotSupportedException`** (`src/OmenCore.Avalonia/Services/LinuxHardwareService.cs:760,775`) with no UI distinction between "not supported on this board" and "not implemented yet." **Medium.**
- **Game-profile automation:** the core linkage (fan + performance + undervolt + GPU switch + lighting per profile) already exists, but known gaps per `docs/GAME_PROFILE_SYSTEM.md`: no window-title disambiguation for same-exe-different-game cases, ~2s polling delay instead of event-based (`WMI InstanceCreationEvent`) detection, and no restore-previous-profile handling when multiple tracked games are open at once (only restores defaults). Documented "future" items — profile chaining and Steam/Epic/GOG library auto-detection — don't exist yet. **Medium** each for the detection fixes, **large** for library auto-detection/chaining.
- **No undo/confirmation pattern for a few destructive actions** outside OC/UV (already covered elsewhere) — fan cleaning boost (`Services/FanCleaningService.cs`) and driver reinstall have no "are you sure"/rollback path. **Small.**
- **No in-app notification history.** Toast toggles exist (`ShowGameNotifications`, `ShowModeChangeNotifications`, etc.) but a missed toast is gone — no log/history view to check what fired. **Small/medium.**
- **Accessibility is effectively absent.** `AutomationProperties` appears in only 4 XAML files total (`MainWindow.xaml` once, `BloatwareManagerView.xaml`, `MemoryOptimizerView.xaml`, plus a style reference) — no automation labeling on FanControl, Keyboard, SystemControl, Dashboard, RGB, or Settings views; no high-contrast handling; no explicit keyboard-navigation work. **Large** for full coverage; **medium** for a "good enough" pass on the primary tabs.
- **Diagnostics export is buried.** The mature export pipeline (`DiagnosticExportService.cs`, `DiagnosticExportControl.xaml`) is only reachable via a Diagnostics tab gated behind `ShowAdvancedControls`/non-Lite mode, or a command buried deep in Settings (`SettingsView.xaml:3031`) — a non-technical user in default Lite mode never sees it, and there's no "Report a Problem" entry point that links the bundle to the issue tracker. **Medium** — add a persistent, always-visible entry point regardless of Lite/Advanced mode.

--------------------------------------------------

## Tray Menu Friction and OSD/FPS Sync Gaps (User Report)

Reported by a user testing 3.9.0: "sync issue with the tray menu and dashboard... getting performance mode and max fans using the tray menu is a hassle, I have to use the dashboard. And even at that the overlay still says fans on auto whereas it's on max. Also fps overlay still doesn't work."

### Tray menu depth for Max Fan — confirmed real friction

`Utils/TrayIconService.cs`'s context menu buries standalone "Max Fan" three levels deep: right-click tray icon → **🔧 Advanced ▶** → **🌀 Fan Control ▶** → Max (lines ~216–255). By contrast, the combined **⚡ Quick Profile ▶** submenu (Performance/Balanced/Quiet/Custom, lines ~170–213) is only two levels deep, but it always changes performance mode *and* fan curve together — there's no fast path to "just Max the fans" without either going three menu levels deep or opening the main window. This matches the report exactly. **Fix:** promote Max Fan (and arguably Performance mode) to a shallower position — e.g. directly on the Quick Profile submenu or as a top-level toggle item, mirroring how OGH exposes a single-click Max option. **Complexity:** small (menu restructuring only, no new logic).

### OSD showing stale "Auto" when fans are actually Max — traced, not yet resolved

Traced the full sync path and it *should* work: `FanService.ApplyPreset(...)` fires `PresetApplied` on success (`Services/FanService.cs:1145`) → `MainViewModel.OnFanPresetApplied` sets `CurrentFanMode` (`ViewModels/MainViewModel.cs:4805`) → `App.xaml.cs`'s `PropertyChanged` subscription (line ~487) republishes to `RuntimeStateEngine.PublishProjection` → `RuntimeStateEngine.StateChanged` → `OsdService.SetFanMode()` (`App.xaml.cs:425`) → `OsdOverlayWindow`. Both the tray's `SetFanModeFromTray()` (`MainViewModel.cs:3783`) and the General-tab fan selector fixed earlier in this cycle apply via `_fanService.ApplyPreset(...)` directly, which is the same path that correctly fires `PresetApplied`. **This means the stale-OSD report doesn't match what a static trace of the code predicts — there's a real gap somewhere in this chain that wasn't found by reading the code alone.** Candidates worth checking with a live repro + diagnostics log: whether `RuntimeStateEngine`'s internal value-equality check (used to decide whether `StateChanged` fires at all) is comparing exact-match strings that differ subtly between the tray path's mode-name and `ResolvePresetModeLabel`'s output for the Max preset specifically; or whether this report predates a fix already in this changelog cycle and needs re-testing on the latest rebuild. **Needs a fresh session log spanning the exact repro (open tray → Advanced → Fan Control → Max → open OSD) before further diagnosis** — do not guess-fix the sync chain without one, per this project's usual evidence practice for behavior that traces correctly in code but is reported broken in the field.

### FPS overlay "not working" — likely a discoverability gap, not a bug

`OsdOverlayWindow` has a real, explicit "unavailable" state (`OsdFpsDisplayFormatter.Unavailable(...)`, referenced at `Views/OsdOverlayWindow.xaml.cs:771`) — the FPS overlay is not broken/blank by accident, it's *designed* to show "unavailable" when its data source isn't present. The data source is RTSS (RivaTuner Statistics Server) shared memory — OmenCore reads FPS from RTSS, it does not capture frames itself (see the 3.9.0 "RTSS OSD" fix and a user session log showing `RTSS: Not running or shared memory disabled`). The user's "not sure if I am in the wrong" strongly suggests they don't have RTSS installed/running and the app isn't making that requirement obvious enough. **Fix:** make the "unavailable" FPS state visually explain *why* (e.g. "FPS: Install RTSS" with a tooltip or link) instead of a state a user could mistake for a bug, and/or surface this requirement during onboarding/first-run alongside the capability-disclosure item already on this roadmap. **Complexity:** small — this is a messaging fix on top of a formatter (`OsdFpsDisplayFormatter`) that already exists and already computes the right state.

--------------------------------------------------

## How To Tackle This: A Guide For Whoever Picks This Up (Human or Agent)

This roadmap file has accumulated a lot of independently-scoped items across several passes. Don't try to do all of it at once, and don't reorder by what's "interesting" — follow this sequence:

1. **Read before touching code.** Every item above cites specific files and/or docs. Re-read the cited doc/changelog section and re-grep the cited file before starting — this file was compiled across multiple research passes at different times, and code may have moved since. If a finding turns out to already be fixed or the cited file/line no longer matches, mark it done with a one-line note rather than silently skipping it.
2. **Respect the evidence-gate rule.** Anything touching fan/EC/thermal/OC/UV *behavior* (not UI/wiring) requires field validation before shipping — this project's established norm (see `docs/CHANGELOG_v3.9.0.md` "Notes For Release" and the per-model `UserVerified` pattern in `ModelCapabilityDatabase.cs`). Architecture, performance, and pure-UI items are not subject to this gate and can proceed on normal code-review + test confidence.
3. **Order of attack — do these in phases, not interleaved:**
   - **Phase A — safe, isolated, no hardware risk:** allocation hotspots, the `WmiBiosMonitor.TryRestartAsync()` re-verification, `NotSupportedException` messaging, notification history, diagnostics entry-point visibility, destructive-action confirmations, tray menu restructuring, FPS "unavailable" messaging, `BiosUpdateService` test coverage. These are self-contained and low-risk — good first picks. The one exception in this phase that is *not* a quick fix: the stale-OSD-fan-mode report needs a fresh repro log before any change, not a blind fix.
   - **Phase B — architecture, no behavior change:** timer consolidation, sync-over-async cleanup, and (the big one) breaking up `MainViewModel`/introducing a real composition root. Do the timer/async cleanup *before* the `MainViewModel` breakup — it's much easier to split a god-object once its threading model is already sane.
   - **Phase C — real feature work, needs scoping conversation first:** Avalonia `OmenCore.Desktop` shell completion (first confirm with the project owner whether it's live/shipped or dormant — this changes whether it's urgent or can be deferred/deleted), game-profile detection improvements, accessibility pass, privilege separation, RGB provider architecture, i18n, Linux tray/config, community model database, first-run capability disclosure, code signing (cost/process decision).
   - **Phase D — hardware-gated:** dedicated Balanced fan mode, AMD OC/UV persistence, PL1/PL2 self-validating readback, board `8D41` keyboard RGB — anything that needs real per-model field evidence per the evidence-gate rule. Don't start these without a plan for getting that evidence.
4. **One item at a time, verified before moving on.** For code changes: build clean, run the full test suite (913 tests as of 3.9.0 — expect this number to grow), and where feasible smoke-test the actual UI path per the project's own `/verify`-style practice (see this session's General-tab fan-selector work as a model: build → test → manual reasoning about the change → rebuild artifacts if user-facing). Don't batch unrelated items into one commit.
5. **Update the checklist below as you go.** Check off `[x]` only once a change is verified (built + tested, and hardware-confirmed where the evidence gate applies), not when code is merely written. Add a one-line note next to the checked item (what changed, file(s) touched) so the next person/agent doesn't have to re-derive it from git history.

## Execution Checklist

### Phase A — Safe, Isolated, No Hardware Risk

- [ ] Cache reusable buffers in `FanService.ApplyCurveIfNeededAsync`/`ForceApplyCurveNowAsync` instead of per-tick `.ToList()`/`.Select().ToList()` allocations
- [ ] Same allocation cleanup for `WmiBiosMonitor.cs` (~line 1635) stale-instance pruning
- [ ] Re-verify `WmiBiosMonitor.TryRestartAsync()` actually restarts the WMI session (not just resets an error counter)
- [ ] Replace raw `NotSupportedException` in `LinuxHardwareService.cs` (GPU mode switch, keyboard brightness) with a clear in-UI "not supported on this board" vs. "not implemented yet" distinction
- [ ] Add a notification history/log view for missed toasts
- [ ] Add a persistent, always-visible "Report a Problem"/"Generate Diagnostics" entry point independent of Lite/Advanced mode
- [ ] Add confirmation/undo for fan cleaning boost and driver reinstall actions
- [ ] Promote Max Fan (and Performance mode) to a shallower tray menu position
- [ ] Get a fresh session log for the stale-OSD-fan-mode report and re-diagnose the sync chain with real data before changing it
- [ ] Make the FPS "unavailable" OSD state explicitly explain the RTSS requirement instead of just reading as broken
- [ ] Add test coverage for `BiosUpdateService` (firmware-write path — currently zero coverage, highest-risk untested surface in the codebase)
- [ ] Link `docs/TELEMETRY.md` from the README for discoverability (telemetry practice is already good, just not surfaced)

### Phase B — Architecture Cleanup (No Behavior Change)

- [ ] Audit and consolidate the 21 independent timers into a shared polling coordinator (start with the ~1-2s cadence cluster: tray, OSD stats, quick popup, process monitoring)
- [ ] Replace bounded `.Wait()`/`.Result` sync-over-async call sites (`CapabilityDetectionService.cs`, `WmiBiosMonitor.cs`, `AudioReactiveRgbService.cs`, `ScreenColorSamplingService.cs`, `DiagnosticLoggingService.cs`) with proper async chains
- [ ] Introduce a real DI composition root and incrementally extract feature-scoped services out of `MainViewModel.cs` (~5,300 lines, ~46 manually-wired fields)
- [ ] Add an Authenticode signature check before elevated installer execution in `AutoUpdateService.InstallUpdateAsync` (blocked on code signing existing — see Phase C)

### Phase C — Feature Work (Scope With Project Owner First)

- [ ] Decide: is `OmenCore.Desktop` (Avalonia shell) live/shipped or dormant? If live, finish the stubbed controls (performance mode, fan profile/manual speeds, keyboard service, driver reinstall); if dormant, consider explicitly marking it experimental or removing it from release artifacts
- [ ] Game-profile automation: event-based process detection (replace ~2s polling) and window-title disambiguation for same-exe-different-game cases
- [ ] Game-profile automation: restore-previous-profile handling for multiple simultaneously-tracked games
- [ ] Accessibility pass: `AutomationProperties` labeling on FanControl, Keyboard, SystemControl, Dashboard, RGB, and Settings views (minimum "good enough" pass)
- [ ] Privilege separation: prototype the `HardwareWorker`-based limited-privilege service per `docs/PRIVILEGE_SEPARATION_SPIKE.md`
- [ ] RGB provider architecture: unify `RgbManager` vs. per-provider direct writes (fixes the Enhancement #17 double-write risk) as a prerequisite for any new vendor work
- [ ] i18n foundation: resource-based string system + language selector (Spanish first)
- [ ] Linux: `omencore-gui` tray icon + GUI-side config persistence
- [ ] Community model-database contribution pipeline (schema + validator + PR template)
- [ ] First-run capability disclosure view
- [ ] Decide on and budget for code signing (cert type, CI signing pipeline) — cost/process decision, not pure engineering

### Phase D — Hardware-Gated (Needs Field Evidence)

- [ ] Dedicated Balanced fan mode (per-model capability gating required)
- [ ] AMD GPU (ADL2) OC startup persistence
- [ ] AMD CPU undervolt (Curve Optimizer) startup persistence
- [ ] Self-validating PL1/PL2 readback loop to replace manual per-model field-report gating
- [ ] Audit all model-name string-matching safety gates for the `IsSensitiveModel()`-style fragile-match bug class
- [ ] Board `8D41` keyboard RGB via Darfon `0d62:54bf` HID controller — take up [GitHub #151](https://github.com/theantipopau/omencore/issues/151)'s offer to capture USB HID feature reports before writing the backend

--------------------------------------------------

## Appendix: Original Drafting Brief

The prompt this roadmap document was originally commissioned from. Superseded by the real content above — kept here for provenance only, not itself roadmap content.

<details>
<summary>Click to expand original brief</summary>

You are creating a long-term engineering roadmap document for OmenCore.

Repository:
https://github.com/theantipopau/omencore

Generate a professional markdown document:

/docs/ROADMAP_4.0.md

This document is intended to guide the future evolution of OmenCore beyond version 3.6.x.

The roadmap should focus on:
- architectural cleanup
- maintainability
- performance
- reliability
- modernization
- simplification of accumulated AI-generated complexity

--------------------------------------------------
IMPORTANT CONTEXT
--------------------------------------------------

The project has evolved through multiple years of development and multiple AI-assisted coding iterations.

Over time, this likely introduced:
- duplicated logic
- layered workaround fixes
- excessive defensive coding
- fragmented ownership patterns
- redundant abstractions
- legacy compatibility paths
- inconsistent threading approaches
- monitoring duplication
- excessive state management
- service sprawl
- unnecessary wrappers/helpers

The roadmap should identify areas where:
- systems can be unified
- complexity can be reduced
- architecture can become more deterministic
- resource usage can be reduced
- long-term maintainability can improve

--------------------------------------------------
DOCUMENT REQUIREMENTS
--------------------------------------------------

Generate a REAL engineering roadmap document.

The markdown should include:

# OmenCore 4.0 Roadmap

## Vision
High-level goals for the next major version.

## Current Technical Debt Summary
Summarize likely accumulated issues.

## Architectural Goals
Examples:
- simpler threading ownership
- unified monitoring pipeline
- reduced polling duplication
- deterministic state flow
- backend abstraction cleanup
- reduced WPF UI churn
- cleaner hardware access boundaries
- reduced service fragmentation

## Proposed Refactor Areas

For each major area include:
- current problems
- root causes
- proposed direction
- expected benefits
- migration risks
- estimated implementation complexity

Potential sections:
- Threading + Dispatcher ownership
- Monitoring architecture
- Hardware backend abstraction
- EC/WMI/OGH communication layers
- Logging architecture
- State management
- Event system cleanup
- Memory management
- Polling/timer consolidation
- MVVM cleanup
- UI update throttling
- Data immutability opportunities

## Performance Objectives
Define measurable targets where possible:
- idle CPU usage
- polling reduction
- memory reduction
- reduced allocations
- reduced thread count
- lower wake frequency

## Stability Objectives
Examples:
- fewer race conditions
- deterministic ownership
- reduced async complexity
- safer cancellation handling
- fewer cross-thread exceptions

## Technical Principles
Examples:
- prefer deletion over abstraction
- fix root causes instead of layering workarounds
- fewer moving parts
- deterministic systems
- measurable optimization over speculative optimization
- minimize hidden side effects

## Release Strategy
Recommend:
- staged refactors
- isolated subsystem rewrites
- telemetry-based validation
- regression testing requirements
- hardware compatibility validation

## Potential 4.x Milestones
Examples:
- 4.0 foundation cleanup
- 4.1 monitoring modernization
- 4.2 backend unification
- 4.3 UI/performance optimization

--------------------------------------------------
IMPORTANT STYLE REQUIREMENTS
--------------------------------------------------

The roadmap should:
- sound like a real senior engineering planning document
- avoid AI buzzwords
- avoid generic fluff
- be practical
- acknowledge hardware compatibility risks
- acknowledge legacy support realities
- focus on maintainability and reliability

Prefer:
- actionable guidance
- engineering realism
- measurable goals
- phased modernization

The document should feel like something a professional software team would actually use internally.

</details>
