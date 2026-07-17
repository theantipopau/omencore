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

### Configurable Thermal Emergency Override — Done

- **Request:** Discord user `snowfall hateall` (boards `8D87` + `88F7`) — "please consider adding an option to disable the thermal emergency → max fan thing, or perhaps an option to change at what temperature makes the fans go max?"
- **Investigated rather than assumed:** a "disable thermal protection" toggle (`ThermalProtectionEnabled`, `FanService.CheckThermalProtection`) already existed and already fully disabled the *Auto-mode* override — but a **separate, always-on safety clamp** (`ApplySafetyBoundsClamping`, applied to custom fan curves) forced 100% at a hardcoded 95°C regardless of that toggle. The toggle's own doc comment already promised "fans will NEVER be automatically overridden by thermal protection" when disabled — this was a real discrepancy between documented and actual behavior for anyone using a custom curve, not just a missing feature. The 95°C emergency trigger itself was also a hardcoded `const`, not configurable at all — only the earlier "start ramping" threshold (90°C, range 75-95°C) had a Settings control.
- **Fixed both halves of the request:**
  1. `ApplySafetyBoundsClamping` now also respects `ThermalProtectionEnabled` — disabling thermal protection now truly means no automatic override, in Auto mode *and* on custom curves, matching what was already documented.
  2. The emergency threshold is now configurable: `AppConfig.FanHysteresisSettings.ThermalEmergencyThreshold` (default 95°C, range 90-99°C, always kept ≥2°C above the ramp threshold so the two can't invert), exposed in Settings next to the existing ramp-threshold field.
- **Verified:** build clean, full suite (948/948, including 7 new targeted tests covering disable-clamp, custom-threshold, range-clamping, and threshold-ordering behavior), live-launched with the new "Thermal protection: Enabled (ramp=90°C, emergency=95°C)" log line confirming correct wiring. Full detail in `CHANGELOG_v4.0.0.md`.

### Linux: Tray Minimize + Config Persistence + Background Service

- **Request:** Discord feedback — keep `omencore-gui` running in the background on Linux with saved config, the way the Windows app does.
- **Current state:** `omencore-gui` (`src/OmenCore.Avalonia`) has no tray icon or config persistence today. Tray only exists as an external shell script (`src/OmenCore.Linux/scripts/omencore-tray.sh`, using `yad`/`libappindicator`/`pystray`) that isn't integrated into the GUI binary. Config persistence (TOML, schema-versioned) exists only in the separate CLI/daemon (`src/OmenCore.Linux/Config/OmenCoreConfig.cs`).
- **Scope:** (1) Avalonia `TrayIcon`/`NativeMenu` integration in the GUI itself — `src/OmenCore.Desktop`'s `CloseToTray` toggle (`SettingsView.axaml.cs`) is a usable reference pattern; (2) GUI-side config load/save wired to the same TOML store the daemon uses; (3) systemd unit/packaging decisions for running headless. Medium-large — real new work, not a bug fix.

### Linux: AUR Packaging

- **Request:** Project owner — "would be neat if Linux installation gets packaged into AUR in the future."
- **Feasibility, checked directly rather than assumed:** genuinely straightforward. `OmenCore.Linux.csproj` already builds `SelfContained=true` + `PublishSingleFile=true` + `PublishTrimmed=true` for `linux-x64`/`linux-arm64` — a `-bin` style AUR package (downloads the pre-built release, no `.NET` SDK as a build dependency) is the natural fit, and `build-linux-package.ps1` already produces exactly the archive shape such a package needs (CLI + GUI bundled, framework-dependent sidecar files stripped). A systemd unit already exists in substance too — `omencore-cli daemon --generate-service` (`DaemonCommand.cs`) prints one at runtime; packaging just needs a static copy pointed at a fixed install path. Initially assumed the Windows app's `AutoUpdateService` self-update mechanism would conflict with pacman-owned updates — **checked and that assumption was wrong**: neither `OmenCore.Linux` nor `OmenCore.Avalonia` has any self-update code at all, so there's nothing to disable.
- **Drafted this session** (`packaging/aur/`): a reviewed but **untested** `omencore-bin` PKGBUILD, a static systemd unit (kept in sync by hand with `DaemonCommand.cs`'s generator — nothing automates that yet), a `.desktop` entry, and a post-install/pre-remove `.install` hook. Full rationale and a submission checklist in `packaging/aur/README.md`. Genuinely can't be built/tested here — there's no Arch machine or `makepkg` available in this environment, so treat it as "ready for someone on Arch to test," not "ready to publish."
- **Real gaps before this ships, not just untested-but-fine:**
  1. **Icon.** No square/properly-sized brand asset exists anywhere in the repo — every logo file is either non-square or a low-res `.ico`. Placeholdered with `Assets/logo-small.png` (367×432, ~330KB) so the package is at least buildable; needs a real one from whoever owns the branding.
  2. **Checksums.** Every `sha256sums` entry is `SKIP` — needs real hashes against an actual published release before AUR will accept it.
  3. **Maintainer.** Someone has to own the AUR submission and keep it updated per release — a people/process decision, not something a PKGBUILD resolves by existing.
- **Complexity:** small once the three gaps above are closed — this is now genuinely close to submittable, not a research problem anymore.

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
- **Per-model safety-gate string matching is fragile:** the `IsSensitiveModel()` bug fixed in 3.8.1 (matched only the literal substring "OMEN 16", missed real WMI strings) is good precedent for an audit pass across *all* OC/UV/fan safety gates that match on model-name substrings rather than ProductId, since the same fragile-match bug class could exist elsewhere undetected. **Complexity:** small, but should happen before any of the above ship. **Audited this cycle — found and fixed a live recurrence of exactly this bug, see Execution Checklist below.**
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

### 4. Community-Contributable Model Database — Done (Execution Checklist Phase C)

`ModelCapabilityDatabase.cs` is hand-maintained per-ProductId, sourced from individual Discord reports — every new SKU currently requires the project owner personally writing an entry. A structured contribution path (a JSON/schema-validated capability file format, a PR template, and a validation script that checks a submitted entry's shape before it's reviewed) would let the community submit capability data directly instead of funneling everything through one person.

**Complexity:** medium — mostly tooling and process (schema + validator + contribution docs), not a runtime architecture change.

**Shipped this cycle** — see the Execution Checklist Phase C entry for the full file list. The database itself is unchanged; this only adds the intake pipeline around it.

### 5. First-Run Capability Disclosure — Done (Execution Checklist Phase C)

A significant fraction of bug reports across the BUG-REPORTS docs amount to "why doesn't X work" where the honest answer is a hardware/firmware limitation (locked GPU TGP, no direct EC access, no RGB per-key support on that board) rather than an OmenCore defect. Surfacing what the detected model actually supports — and doesn't — on first run (or in a persistent "Capabilities" view) would pre-empt a chunk of these reports and set expectations before the user goes looking for a setting that isn't there for their hardware.

**Complexity:** small to medium — mostly a UI/UX addition on top of data (`ModelCapabilityDatabase.cs`) that already exists.

**Shipped this cycle** as a persistent "Model Capabilities" panel on the Diagnostics tab rather than a first-run-only page — see the Execution Checklist Phase C entry for detail.

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

## Newly Reported (4.0 Cycle): Board `8574` Fan RPM Always Reads 0 / "--"

Reported by a user testing 3.9.0 on an OMEN 15-dc1xxx (2019, ProductId `8574`, `Family = Legacy`): "the fans still aren't responding to change I make on OmenCore (including when running the fan control diagnostics). Also the fans display at 0 RPM in the General tab and '--' if I mouse over the system tray icon." Session log (`OmenCore_20260710_170945.log`) traced end-to-end.

**Traced root cause — real, and applies to more than just this board:** `FanController.ReadFanSpeeds()` (`Hardware/FanController.cs:174`) has three tiers: (1) in-process LibreHardwareMonitor bridge (`_bridge?.GetFanSpeeds()`), (2) if the bridge is null, `ReadWmiBiosFanSpeeds()` — WMI BIOS command 0x2D, (3) EC register read, throttled and bridge-gated. The bridge (`_libreHwMonitor`) is populated via `hwMonitor as LibreHardwareMonitorImpl` in `FanControllerFactory.cs:104` — **and is `null` whenever `WmiBiosMonitor` is the active monitoring backend**, which every log from this session shows is the normal/default architecture (`"Self-sustaining monitoring active: WMI BIOS + NVAPI"`). So tier 1 is unavailable by construction in the common case, which routes straight to tier 2 (WMI BIOS fan-level query) — but board `8574`'s own model-database entry documents that its WMI BIOS command path is non-functional (confirmed repeatedly in the log: `heartbeat sequence... System data query returned empty result`, `HP WMI BIOS found but commands not functional`). With tier 1 unavailable and tier 2 broken for this board, there is **no path left to real fan RPM** — not a bug specific to this session, a structural gap for any board where WMI fan commands don't work and the LHM bridge isn't wired in.

This also explains the diagnostic's own output: every "PASS" in the Guided Fan Diagnostic for this board is tagged `Evidence=Level` or `Evidence=None` (never `Evidence=RPM`), and the RPM numbers shown are explicitly labelled `(fan-level estimate)` — the diagnostic's own scoring already knows it doesn't have real tachometer data for this board, it just doesn't surface that as clearly as it could to the user reading the summary.

**What this means for the two symptoms:**
- "0 RPM / --" — real, structural, confirmed cause above. Not a regression in 3.9.0.
- "fans aren't responding" — the underlying EC writes are actually succeeding in this exact log (`Fan preset 'Performance' applied via EC (PawnIO) (controller returned success) - verifying state...` → `Preset 'Performance' verified successfully`, and the diagnostic's own level-set/level-readback round-trips matched at every tested percentage). The likely explanation is the user has no way to *see* confirmation the fans changed (no RPM number moves, and audible fan-noise change is easy to miss/doubt without a number backing it up) — this reads as "not responding" even though the write path is working. Can't fully rule out a real behavioral issue without a repro that isolates "EC write succeeds but fan physically doesn't change speed" vs. "EC write succeeds and fan does change, user just can't see it" — worth asking the reporter to listen/feel for an actual RPM change (or use an external tachometer app) while toggling Max, since that would separate the two theories cleanly.

**Not fixed this session — this is fan/EC/telemetry-adjacent and the fix (wiring the already-running out-of-process `HardwareWorker` LHM fallback, which exists specifically for "resilient telemetry recovery," into `FanController`'s RPM tier for EC-only/WMI-broken boards) is a real architecture change to a subsystem this project's own evidence-gate norm treats carefully. Flagging here with the full trace so whoever picks it up doesn't have to re-derive it, and recommending it be scoped as its own Phase B/D item: wire `HardwareWorker`-based LHM RPM as a fourth tier in `FanController.ReadFanSpeeds()`, gated behind "WMI fan commands confirmed non-functional for this ProductId," with the diagnostic's "(fan-level estimate)" labeling made equally visible in the main General-tab/tray display so users on affected boards at least get an honest "estimated" number instead of a bare 0/--** in the meantime.

--------------------------------------------------

## Newly Reported (4.0 Cycle): Board OMEN MAX 16z-ak000 (AMD) — Fan Duty%/RPM Mapping, Calibration Wizard Timing, Memory Slider

Reported by Discord user "SprinkSponk" on v3.9.0, with a full startup log attached (`OmenCore_20260713_204255.log`). Follow-up clarified per-fan individual control (manual, via the verification step) works fully — the report concerns the automatic/curve-driven path.

**Memory tab Auto-Clean interval slider (1, 6, 11... instead of 1, 5, 10...): fixed and verified this session.** `Views/MemoryOptimizerView.xaml` slider `Minimum` changed from `1` to `0` — WPF's `IsSnapToTickEnabled` snaps to `Minimum + n*TickFrequency`, so `Minimum=1` produced the reported off-by-pattern. `CleanEveryMinutes`'s setter already clamps to a floor of 1, so this is a pure display fix with no behavior change. Full test suite (941 tests) green, live-launched and log-checked clean.

**Calibration Wizard timing — reporter's theory: BIOS-level fan smoothing the wizard doesn't wait long enough for. Partially addressed this session.** `FanCalibrationService.FanResponseDelayMs` was a single fixed 3000ms delay before taking one RPM sample per step (both in the level-sweep loop and in `ApplyAndVerifyAsync`'s closed-loop verification) — no adaptive polling until the reading stabilizes, which structurally matches the reporter's theory. Increased to 5000ms as a conservative mitigation. **Still open:** a real fix would poll until RPM stops changing (with a timeout) instead of trusting a single fixed-delay snapshot; that needs timing data from more boards to tune without overfitting to this one report. Worth asking the reporter to re-run the wizard on the 5000ms build and share whether the resulting profile looks more accurate.

**Fan duty% doesn't map linearly to RPM (10% commanded ≈ 1000 RPM against a claimed ~6000 RPM max; 0% floors around 300 RPM instead of off). Not fixed — needs field evidence.** This board's log shows "Fan RPM readback returned null," meaning every RPM number the reporter is judging against is a software estimate (`WmiFanController`'s `rpm = percent * 5500 / 100`-style formulas), not real tachometer telemetry — same structural gap as the board-`8574` case above, different symptom. Compounding factor: this board is force-switched from the BIOS-reported V1 (0-55/krpm) scale to V2 (percentage) fan commands via a broad name-substring match in `HpWmiBios.cs` (`Contains("MAX") && Contains("OMEN")`), not a per-ProductId table entry, and the model's capability profile is explicitly `UserVerified: No` ("inferred from adjacent MAX ak/ah generation," GitHub #117-adjacent). Changing the estimate formula blind, with no real telemetry to check it against, risks trading one wrong number for a different wrong number. Best path: get the reporter's Calibration Wizard output (now more likely to be accurate at 5000ms) to derive a verified board-specific curve, or find another 16z-ak000/close-sibling owner who can confirm real RPM via an external tachometer.

**Suggestions logged, not scoped yet:** wattage-aware predictive fan curve (react to power draw before heat, not after), raw-RPM toggle for custom curves (currently %-only), multiple saved custom fan profiles, periodic/idle deep-clean mode for Memory Optimizer. Candidates for the Phase C feature-scoping pass.

--------------------------------------------------

## How To Tackle This: A Guide For Whoever Picks This Up (Human or Agent)

This roadmap file has accumulated a lot of independently-scoped items across several passes. Don't try to do all of it at once, and don't reorder by what's "interesting" — follow this sequence:

1. **Read before touching code.** Every item above cites specific files and/or docs. Re-read the cited doc/changelog section and re-grep the cited file before starting — this file was compiled across multiple research passes at different times, and code may have moved since. If a finding turns out to already be fixed or the cited file/line no longer matches, mark it done with a one-line note rather than silently skipping it.
2. **Respect the evidence-gate rule.** Anything touching fan/EC/thermal/OC/UV *behavior* (not UI/wiring) requires field validation before shipping — this project's established norm (see `docs/CHANGELOG_v3.9.0.md` "Notes For Release" and the per-model `UserVerified` pattern in `ModelCapabilityDatabase.cs`). Architecture, performance, and pure-UI items are not subject to this gate and can proceed on normal code-review + test confidence.
3. **Order of attack — do these in phases, not interleaved:**
   - **Phase A — safe, isolated, no hardware risk:** allocation hotspots, the `WmiBiosMonitor.TryRestartAsync()` re-verification, `NotSupportedException` messaging, notification history, diagnostics entry-point visibility, destructive-action confirmations, tray menu restructuring, FPS "unavailable" messaging, `BiosUpdateService` test coverage. These are self-contained and low-risk — good first picks. The one exception in this phase that is *not* a quick fix: the stale-OSD-fan-mode report needs a fresh repro log before any change, not a blind fix.
   - **Phase B — architecture, no behavior change:** timer consolidation, sync-over-async cleanup, and (the big one) breaking up `MainViewModel`/introducing a real composition root. Do the timer/async cleanup *before* the `MainViewModel` breakup — it's much easier to split a god-object once its threading model is already sane.
   - **Phase C — real feature work, needs scoping conversation first:** game-profile detection improvements, accessibility pass, privilege separation, RGB provider architecture, i18n, Linux tray/config, code signing (cost/process decision). (The Avalonia-shell live/dormant question, community model database, and first-run capability disclosure were resolved/shipped this cycle — see the checklist below.)
   - **Phase D — hardware-gated:** dedicated Balanced fan mode, AMD OC/UV persistence, PL1/PL2 self-validating readback, board `8D41` keyboard RGB — anything that needs real per-model field evidence per the evidence-gate rule. Don't start these without a plan for getting that evidence.
4. **One item at a time, verified before moving on.** For code changes: build clean, run the full test suite (913 tests as of 3.9.0 — expect this number to grow), and where feasible smoke-test the actual UI path per the project's own `/verify`-style practice (see this session's General-tab fan-selector work as a model: build → test → manual reasoning about the change → rebuild artifacts if user-facing). Don't batch unrelated items into one commit.
5. **Update the checklist below as you go.** Check off `[x]` only once a change is verified (built + tested, and hardware-confirmed where the evidence gate applies), not when code is merely written. Add a one-line note next to the checked item (what changed, file(s) touched) so the next person/agent doesn't have to re-derive it from git history.

## Execution Checklist

### Phase A — Safe, Isolated, No Hardware Risk

- [x] Cache reusable buffers in `FanService.ApplyCurveIfNeededAsync`/`ForceApplyCurveNowAsync` instead of per-tick `.ToList()`/`.Select().ToList()` allocations — **re-verified, not a safe/isolated change as scoped.** `fanSpeeds`, `displayRpms`, and `rpmStates` (built ~line 1856-1957) are captured into an `App.Current.Dispatcher.BeginInvoke(...)` closure that runs asynchronously *and* `displayRpms`/`rpmStates` are aliased directly into `_lastFanSpeeds`/`_lastFanRpmStates`, which the *next* tick reads. Pooling/reusing these lists in place would race the dispatcher closure against the next monitor iteration. A correct fix needs real double-buffering, not a one-line pool — and at the ~2-5s monitor cadence the actual allocation volume is negligible. Left as-is; not worth the risk for the benefit. The `Enumerable.Repeat(...).ToList()` calls (~line 1868-1871) are already gated behind a fan-count-change check, so they're not a live per-tick cost either.
- [x] Same allocation cleanup for `WmiBiosMonitor.cs` (~line 1635) stale-instance pruning — **re-verified, non-issue.** `RefreshGpuEngineCountersIfNeeded()` is already throttled to once per `GpuEngineCounterRefreshInterval` (20s, line 151), and `instanceNames`/`staleInstances` are purely local (never escape the method). Allocation volume is a handful of short-lived lists every 20s over a single-digit instance count — not a real cost. No change made.
- [x] Re-verify `WmiBiosMonitor.TryRestartAsync()` actually restarts the WMI session (not just resets an error counter) — **re-verified, finding is stale.** Current implementation (line 381) does real recovery work: clears NVAPI failure/suspension state, disposes and nulls the stale worker-backed temp-fallback monitor (forcing reconnection to a freshly-started hardware worker — this is what fixes the "CPU temp frozen after resume" symptom seen repeatedly in field logs), and resets the worker-prelaunch guard to allow `TryPrelaunchHardwareWorker()` to restart a dead worker process. This was built out in the 3.8.1 patch (`a9b3cc3`, 2026-06-24), after the `V3_ARCHITECTURE_REVIEW.md` RC-7 concern was written. No longer a stub. No change needed.
- [x] Replace raw `NotSupportedException` in `LinuxHardwareService.cs` (GPU mode switch, keyboard brightness) with a clear in-UI "not supported on this board" vs. "not implemented yet" distinction — the underlying exception messages were already descriptive; the gap was `SystemControlViewModel.cs`'s catch blocks folding them under a generic "X failed." headline indistinguishable from a transient error. Added a `catch (NotSupportedException ex)` branch before the generic catch in both `SetGpuMode` and `ApplyKeyboardBrightnessAsync` with headlines that read as permanent/by-design ("...isn't available here" / "...isn't available on this board") instead of "failed." Build verified clean.
- [x] Add a notification history/log view for missed toasts — found that `NotificationService.cs` already had a *complete* in-app notification center (`InAppNotification` model, `AddInAppNotification`, `MarkAsRead`, `DismissNotification`, `ClearAllNotifications`, `UnreadCount`/`HasUnread`, events) built and totally unused — zero references anywhere else in the codebase, so it never populated and had no UI. Wired the ~15 `Show*` toast methods to also call the matching `Add*`/`AddInAppNotification` in-app method, so history now actually populates. Added a "🔔 Recent Notifications ▶" tray submenu (`TrayIconService.cs`, `NotificationService` now optionally injected via constructor) listing the last 10 with icon/title/time-ago/tooltip, a Clear All action, and click-to-mark-read — reuses the existing lazy-refresh-on-open pattern the other dynamic submenus already use. No new WPF window/view built; kept this Phase-A-sized by surfacing through the tray rather than a new XAML surface.
- [x] Add a persistent, always-visible "Report a Problem"/"Generate Diagnostics" entry point independent of Lite/Advanced mode — added a top-level "🩺 Report a Problem" tray menu item (event-based, matching the existing `FanModeChangeRequested`-style wiring) that triggers `SettingsViewModel.ExportDiagnosticsCommand` directly — same save-dialog + success/error messaging as the existing Settings-tab button, just reachable without opening the main window or navigating past the Lite/Advanced gate.
- [x] Add confirmation/undo for fan cleaning boost and driver reinstall actions — **fan cleaning boost already had a Yes/No confirmation dialog** (`SettingsViewModel.StartFanCleaningAsync`) before this session; that part of the finding was stale. What was missing was a way to abort mid-cycle: `_fanCleaningCts` already existed and was cancellable, but nothing in the UI called `.Cancel()` except app shutdown. Added `CancelFanCleaningCommand` and a Cancel button next to the progress bar (`SettingsView.xaml`), wired to the existing cancellation token — no new cancellation plumbing needed, it was already there and unused. **Driver reinstall does not exist as a real feature** — the only reference is a literal `// TODO: Trigger driver reinstallation` stub in `OmenCore.Desktop/Views/SettingsView.axaml.cs`, part of the Avalonia shell already flagged elsewhere in this roadmap as needing an owner decision on whether it's live or dormant before any further work. Adding confirmation to a no-op button would be meaningless; left untouched pending that decision.
- [x] Promote Max Fan (and Performance mode) to a shallower tray menu position — added a top-level "🔥 Max Fan" item directly on the tray context menu (`TrayIconService.cs`, right after the Quick Profile submenu), one click deep instead of three (Advanced ▶ Fan Control ▶ Max). Existing Advanced-menu entry left in place, not removed. Performance-mode promotion left as-is for now — Quick Profile already puts it two levels deep and bundling it with fan curve changes is intentional per the existing design; only Max Fan had the reported friction. Build verified clean.
- [ ] Get a fresh session log for the stale-OSD-fan-mode report and re-diagnose the sync chain with real data before changing it
- [x] Make the FPS "unavailable" OSD state explicitly explain the RTSS requirement instead of just reading as broken — changed the "RTSS unavailable" detail string to "FPS needs RTSS running (RivaTuner Statistics Server)" in `OsdOverlayWindow.xaml.cs:773`. Build verified clean; no test coupled to the old string.
- [x] Add test coverage for `BiosUpdateService` — **re-verified: this class does not write firmware at all**, contrary to how it was described in the original finding. It only checks HP's web API/support pages for a newer BIOS softpaq (`CheckForUpdatesAsync`/`TryHpCatalogLookupAsync`) and hands off to HP's own tools via browser links (`OpenSupportPage`/`OpenHpSupportAssistant`) for the user to install manually — no EC/WMI/SPI-flash write path exists anywhere in this file. The real (much lower) risk is silently misreporting version/update state. Added 19 tests (`BiosUpdateServiceTests.cs`, reflection-based per this codebase's existing pattern for private-method coverage) targeting `CompareBiosVersions`, `ExtractVersionNumbers`, `ExtractProductId`, and `ConstructSupportUrl` — numeric-vs-lexicographic version ordering, mismatched-component-count versions, non-numeric fallback, null/empty inputs — plus one test of `CheckForUpdatesAsync`'s early-return guard (no network dependency, fully deterministic). All 19 pass; corrected the risk-level record for this file in the process.
- [x] Link `docs/TELEMETRY.md` from the README for discoverability — added a `[details](docs/TELEMETRY.md)` link on the "Local first / no outbound telemetry" row in the "Why People Use It" table (`README.md`).
- [x] Fix Memory tab Auto-Clean interval slider snapping to 1, 6, 11... instead of 1, 5, 10... (SprinkSponk report) — `Views/MemoryOptimizerView.xaml` slider `Minimum` changed `1` → `0`; `CleanEveryMinutes` setter's existing floor-of-1 clamp makes this a display-only fix. Build + full suite (941/941) + live-launch clean.
- [x] Increase Calibration Wizard's fixed post-command RPM sample delay (SprinkSponk report, BIOS-smoothing theory) — `FanCalibrationService.FanResponseDelayMs` `3000ms` → `5000ms`. Conservative mitigation only; true fix (poll-until-stable) needs more field timing data before scoping. Build + full suite (941/941) clean.

**GitHub #151 (board `8D41` keyboard RGB, Darfon `0d62:54bf`)** — investigated ahead of its Phase D slot since a live user asked about it this session. Confirmed two concrete, distinct bugs beyond the "needs field evidence" framing: (1) `LinuxKeyboardController.cs` watches the wrong sysfs device (`hp-wmi/zoneN_color`) — this board's community driver actually exposes zones at `hp-rgb-lighting/zoneN` (different platform device, no `_color` suffix), so even the light-bar zones (0-3), which the reporter proved are writable via raw sysfs, silently fail through `omencore-cli`. This part is fixable now, no field evidence needed. (2) Keyboard zones 4-7 genuinely don't reach the Darfon HID controller at the kernel-module level (confirmed by the reporter's own read-back testing) — that part still needs the HID capture the reporter offered, consistent with the existing Phase D gating. (3) `HidPerKeyBackend.cs` (Windows path) hardcodes `HP_VID = 0x03F0` and would never detect this Darfon-VID (`0x0d62`) controller either — same underlying bug class on the Windows side. Not yet implemented — flagging here so the Linux sysfs-path fix (item 1) isn't blocked on the HID work (item 2/3) when this is picked up.

### Phase B — Architecture Cleanup (No Behavior Change)

- [x] Audit and consolidate the independent timers into a shared polling coordinator — **the roadmap's named 4-timer starting cluster is done; the rest of the 27-timer inventory is out of scope for this item going forward (see below).** Audit found the real count is **27 timer instances across three different timer APIs**, not "21 across two" — 10 `DispatcherTimer` (matches the original count exactly), **16** `System.Threading.Timer` (not 11), plus a previously-unmentioned 27th: `ProcessMonitoringService` uses `System.Timers.Timer`, a third distinct timer API nobody had flagged. Also found `Services/Diagnostics/BackgroundTimerRegistry.cs`, which looks like it could be the "shared coordinator" but isn't — it's a passive, opt-in, diagnostics-only self-report registry with no scheduling behavior. Confirmed the named starting cluster's exact cadences: Tray 2000ms, OSD stats 1000ms, OSD network ping 5000ms, Quick Popup 1000ms, `ProcessMonitoringService` starts at 2000ms and self-throttles to 10000ms idle.
  - **Built:** `Utils/PollingScheduler.cs` — pure, WPF-free due-time scheduling core (subscribe by name/interval/callback, per-subscriber fault isolation, disposable unsubscribe), fully unit tested with a fake clock (9 tests, `PollingSchedulerTests.cs`) since real `DispatcherTimer`s can't be driven deterministically in xunit. `Utils/UiPollingCoordinator.cs` — thin static wrapper owning one real `DispatcherTimer` at a 500ms base tick (evenly divides every cadence in the starting cluster, so no drift versus the timers it replaces — only up to 500ms of first-fire jitter after subscribing, the same order of jitter `DispatcherTimer` already has against the UI message queue).
  - **Migrated:** `TrayIconService`'s `_updateTimer` (the first and simplest candidate — single long-lived instance, one fixed 2000ms cadence, no dynamic interval, no complex lifecycle) now subscribes to `UiPollingCoordinator` instead of owning its own `DispatcherTimer`. Live-launched the built app (`OmenCore.exe`) and observed 20+ seconds of normal runtime with no errors, no exceptions, no coordinator fault-isolation warnings, and `Responding=True` (UI thread not deadlocked) — this is real runtime verification, not just code review.
  - **Also migrated (same session, follow-up pass):** `QuickPopupWindow`'s `_updateTimer` and `OsdOverlayWindow`'s `_updateTimer`/`_pingTimer` — these were more involved than `TrayIconService` since both windows start/stop their timers dynamically (visibility for the popup; visibility and per-feature settings for the OSD's network timer) rather than running continuously for the object's lifetime. Preserved this exactly: `Start*Timer()` now calls `UiPollingCoordinator.Subscribe(...)` (only if not already subscribed), `Stop*Timer()` disposes and nulls the subscription — same start/stop call graph as before, just backed by the coordinator instead of each window's own `DispatcherTimer`. All three of the roadmap's named UI-timer cluster are now on the shared coordinator.
  - **Deliberately not migrated:** `ProcessMonitoringService` (runs on a background thread today via `System.Timers.Timer` specifically to keep CPU-bound process-list polling off the UI thread — folding it into this UI-thread `DispatcherTimer`-backed coordinator would move that work onto the UI thread, a regression, not a consolidation; it needs a separate background-thread-flavored coordinator if it's ever unified with anything).
  - **Verification note:** `TrayIconService`'s migration was live-launch-verified (app run, log watched, confirmed responsive). `QuickPopupWindow`/`OsdOverlayWindow` are lazily constructed only when their window is first shown (tray left-click for the popup; OSD enabled in settings for the overlay, which was off on the test machine) — launching the app doesn't exercise those paths, so this pass relied on careful hand-verification of the exact before/after Subscribe/Dispose call graph plus the full test suite (941/941), not a live UI click-through. Worth a live check next time either window is opened during normal use.
- [x] Replace bounded `.Wait()`/`.Result` sync-over-async call sites — **audited all 5 sites; none had a safe, contained fix available, so none were changed.** Specifically: `CapabilityDetectionService`'s undervolt-probe `.Wait(5s)` **is a real, confirmed UI-thread-blocking risk** — traced its only caller to `MainViewModel`'s constructor, which runs synchronously on the UI thread during `App.OnStartup`, so a slow MSR/SMU probe blocks the whole app launch for up to 5s. Empirically it completes in ~1-1.2s in every field log reviewed this and the prior session, so it hasn't been reported as a hang, but the risk is real. The correct fix (moving capability probing out of the synchronous constructor) is inseparable from the `MainViewModel` decomposition below — flagging it as a concrete thing to fix *as part of* that work, not before it. `WmiBiosMonitor`'s two `Task.Run(...).Wait(timeout)` sites already run on a background thread (the whole `UpdateReadings()` loop is itself wrapped in `Task.Run`), so they don't block the UI — the pattern is a legitimate, if slightly wasteful (double thread-pool hop), way to bound an inherently-synchronous temperature-read call with a timeout; no async overload of the underlying `GetCpuTemperature()` exists to await instead. `AudioReactiveRgbService.Dispose()` and `ScreenColorSamplingService.Dispose()`'s `Task.Run(...).Wait(2s)` are deliberate (there's an explicit code comment explaining the UI-thread-deadlock avoidance) workarounds for `IDisposable.Dispose()` being inherently synchronous — not fixable without changing the class to `IAsyncDisposable`, which would ripple to every caller. `DiagnosticLoggingService.Disable()`'s `.Wait(2s)` is **dead code** — the whole class is never instantiated or registered anywhere in the app, zero runtime risk.
- [~] Introduce a real DI composition root and incrementally extract feature-scoped services out of `MainViewModel.cs` — **confirmed current size: 5,333 lines, ~40 `private readonly` service/manager fields** (roadmap's ~5,300/~46 estimate was accurate). Started, deliberately narrowly: this is the single largest, riskiest item on the whole roadmap short of privilege separation, so rather than moving business logic out of `MainViewModel` (hard to verify without live-clicking every UI surface), the first real step targets the actual "no composition root" problem directly — *who constructs MainViewModel's dependencies*, not what the dependencies do.
  - **Pattern:** each service gets an optional constructor parameter defaulting to `null`, with `_field = injected ?? new Service(_logging);` as the body — DI resolves and injects it in production once registered in `App.xaml.cs`'s `ConfigureServices`, while every existing parameterless `new MainViewModel()` call site (18+ in `MainViewModelTests.cs` alone) keeps working completely unchanged, falling back to the exact same manual construction that already existed. Zero business-logic risk; the only thing that changes is which code path builds the object.
  - **Migrated so far (four passes, same session):** `SystemRestoreService`, `OmenGamingHubCleanupService`, `NotificationService`, `SystemInfoService`, `AutoUpdateService`, `BiosUpdateService`, `TelemetryService`, `SystemOptimizationService`, `GpuSwitchService`, `ProcessMonitoringService`, `HotkeyService`, `OmenKeyService`, `GameProfileService` — **13 fields total**, all now registered in `ConfigureServices` via factories instead of being kept off the `App.Logging`/`App.Configuration`-are-static-singletons path DI doesn't otherwise know about. `TelemetryService` needed `ConfigurationService` as a second dependency, resolved by referencing the existing static `Configuration` singleton directly in its factory. `GameProfileService` is the first candidate to depend on *another migrated service* rather than just the two statics — its factory takes the DI `IServiceProvider` and calls `sp.GetRequiredService<ProcessMonitoringService>()`, which returns the exact same cached singleton `MainViewModel` itself receives, so both consumers end up sharing one `ProcessMonitoringService` instance rather than each independently constructing their own. `NotificationService` is a similar confirmation this scales correctly: it's the same instance `TrayIconService` reads via `mainViewModel.Notifications` (wired up in an earlier Phase A pass) — DI singleton caching means every consumer now shares the exact same instance, strictly more correct than before, not just equivalent.
  - **Skipped deliberately:** `NvapiService` — unlike every other candidate so far, it's *conditionally* constructed (either `new NvapiService(_logging)` or left `null` depending on a runtime check), so the simple "inject or fall back to `new`" pattern doesn't fit without either losing that conditional logic or complicating the pattern. `PowerAutomationService` and `AutomationService` were checked and also skipped — both depend on `FanService`, which is built with substantial hardware-detection-specific setup earlier in the constructor rather than being a simple singleton, making them poor fits for this mechanical pattern without much deeper restructuring.
  - **Verified:** full build clean, `MainViewModelTests.cs` (34/34) and full suite (941/941) pass after each batch, **and** the production DI path was live-launch-tested after every batch — launched `OmenCore.exe`, confirmed `GetRequiredService<MainViewModel>()` resolves all injected dependencies with zero errors/exceptions in the log, `Responding=True`, cleanly terminated each time. Beyond "didn't crash": `AutoUpdateService`'s log showed a real `Checking for updates...` → `You are running the latest version.` round-trip; `HotkeyService`/`OmenKeyService`/`ProcessMonitoringService`'s batch showed actual hotkey registration, OMEN key interception starting, and process monitoring starting; and `GameProfileService`'s batch showed `Game profile service initialized with 0 profile(s)` and `Game profile system initialized` — confirming the cross-service DI resolution (`GameProfileService` correctly receiving the shared `ProcessMonitoringService` singleton) actually works at runtime, not just in theory.
  - **Removed** the `TODO: Future refactoring - register all services here...` comment in `ConfigureServices` that anticipated exactly this work, since it's now in progress.
  - **Follow-up pass — 5 more fields migrated:** re-audited the remaining ~27 fields individually rather than trusting the "all hardware-entangled" summary above — it undersold what was actually left. `RuntimeEcOperationCoordinator`, `HpWmiBios`, `OghServiceProxy`, `ConflictDetectionService` all turned out to be plain `_logging`-only leaves with no try/catch and no conditional nulling — same shape as the first 13. `ThermalMonitoringService` depends on `_logging` plus the already-migrated `NotificationService`, following the exact `GameProfileService`-style `sp.GetRequiredService<NotificationService>()` cross-dependency factory. All five migrated with the identical `injected ?? new X(...)` pattern, registered in `ConfigureServices` (required adding a missing `using OmenCore.Hardware;` to `App.xaml.cs` for `HpWmiBios`/`OghServiceProxy`). **18 of ~40 fields now DI-seeded.**
  - **Re-confirmed as deliberately excluded, not re-litigated:** `NvapiService` and `AmdGpuService` both wrap construction in try/catch with conditional-null fallback (`AmdGpuService` additionally nulls itself later via a fire-and-forget async init) — an eager DI factory that throws during `ConfigureServices` resolution would crash startup instead of degrading gracefully, a real regression risk. `_trayActionDispatcher`/`_hotkeyCoordinator` bind closures to `this`. `_undervoltService` depends on a separate CPU-vendor-detection probe (`CpuUndervoltProviderFactory.Create()`) — self-contained but still a hardware probe, left for later.
  - **Not done / next candidates:** ~22 fields remain, all genuinely entangled with the four real hardware-bringup locals (`ec`, `capabilities`, `monitorBridge`, `fanController`) built via sequential EC/WMI/capability/fan-controller probing early in the constructor. Migrating `_fanService`, `_performanceModeService`, `_keyboardLightingService`, `_hardwareMonitoringService`, `_fanCleaningService`, and their downstream consumers safely needs a new injectable "hardware context" abstraction — bundling those four locals behind one DI-registered factory — that doesn't exist anywhere in the codebase today. That's a separate, larger, higher-risk design effort, not a mechanical continuation of this pattern. **Extracting actual business logic/bound properties out of `MainViewModel` and into feature-scoped ViewModels or coordinators is a separate, much larger piece of this item and has not been started** — that's the part that genuinely needs a dedicated session with real UI regression coverage, not something to fold in here.
- [ ] Add an Authenticode signature check before elevated installer execution in `AutoUpdateService.InstallUpdateAsync` (blocked on code signing existing — see Phase C)

### Phase C — Feature Work (Scope With Project Owner First)

- [x] Decide: is the Avalonia shell live/shipped or dormant? — **resolved by re-reading the repo, no owner conversation needed: this was a naming mix-up in the roadmap, not an open question.** There are two separate Avalonia projects, and the roadmap's own item conflated them:
  - **`src/OmenCore.Desktop`** — genuinely dormant, and already knows it: its `.csproj` has `<!-- Archived prototype: not part of OmenCore.sln shipping builds. Do not version-bump in release cycles. -->` at the top, and its own `README.md` states plainly "This project is archived and not part of shipping OmenCore builds... Not included in `OmenCore.sln` / Not used by release packaging / Kept only for historical reference." It's stuck at version 3.6.3 while the rest of the app is 3.9.0+. No action needed — it's already correctly marked, not silently rotting.
  - **`src/OmenCore.Avalonia`** — this is the actual live, shipped Linux GUI (assembly name `omencore-gui`), version-synced at 3.9.0, present in `OmenCore.sln`, built by `build-linux-package.ps1` and bundled into every Linux release zip, and referenced in the README/release notes as "Avalonia GUI." A full grep for `TODO`/`NotImplementedException`/stub markers across the whole project found **zero matches** — the "finish the stubbed controls (performance mode, fan profile/manual speeds, keyboard service, driver reinstall)" premise in the original roadmap wording doesn't apply to this project; that specific `driver reinstall` TODO stub referenced elsewhere in this roadmap lives in `OmenCore.Desktop/Views/SettingsView.axaml.cs` — the archived one, not the shipping one. `OmenCore.Avalonia`'s only two `NotSupportedException` sites (GPU mode switch, keyboard brightness) are legitimate platform-limitation messages, already fixed to read clearly in an earlier Phase A pass this cycle. No further "finish it or kill it" decision is needed for either project.
- [ ] Game-profile automation: event-based process detection (replace ~2s polling) and window-title disambiguation for same-exe-different-game cases
- [ ] Game-profile automation: restore-previous-profile handling for multiple simultaneously-tracked games
- [~] Accessibility pass: `AutomationProperties` labeling — expanded across four views, still not exhaustive. Scoped to interactive controls only (the actual screen-reader-relevant gap: a WPF control's default UIA name only resolves cleanly from plain-string `Content`, so any icon+text button, unlabeled `ComboBox`, or bare `ToggleButton`/`RadioButton` is effectively silent to a screen reader even though it's visibly labeled).
  - **Done, session 1:** `DashboardView.xaml` (Refresh button, 4 time-range radio buttons) and `AdvancedView.xaml` (3 performance-mode cards, GPU power boost level combo + apply button, GPU switch mode combo + apply button, display overdrive toggle) — 10 controls.
  - **Done, session 2:** `FanControlView.xaml` — both dismiss-banner buttons, Restore OEM Auto, all 7 fan preset radio cards (Max/Direct/Extreme/Gaming/Auto/Silent/Custom), the direct-level slider + Apply button, preset name field + Save/saved-presets combo/Delete/Import/Export, smoothing duration/step fields, the single-curve Add/Remove/Reset buttons, both CPU and GPU curve editors' +/−/↺ buttons (previously single-character `+`/`−`/`↺` content with zero context — the highest-value fix in this view, since a screen reader could not tell CPU from GPU curve buttons apart before this), the GPU copy-from-CPU icon button, and the calibration wizard button — ~30 controls. `LightingView.xaml` — the 8 OpenRGB and 9 HP-keyboard emoji quick-color swatch buttons (previously just a raw emoji as `Content`, which is not a reliable screen-reader announcement), all 7 card-enable toggles (Scene Quick Select, Ambient, Audio Reactive combo/slider, Corsair, Logitech, Razer, HP OMEN Keyboard), Sync All RGB, Restore Keyboard, and the RGB surface probe/save controls — ~28 controls. Session-2 total: ~58 controls across the two largest fan/RGB surfaces.
  - **Not done:** the remainder of `LightingView.xaml` (per-zone keyboard grid, Corsair DPI stage editor, several more card-internal toggles — this file is ~1,900 lines and the highest-value silent-button and unlabeled-toggle cases were prioritized over full coverage), plus `SettingsView.xaml` in full (~3,400 lines, dozens of controls, deliberately deferred rather than rushed).
  - No screen reader was available to verify end-to-end in this environment in either session; verification both times was build-clean + full test suite green + live-launch with no XAML/binding errors, which confirms nothing broke but not that the announced names are correct or well-phrased — flagging that gap rather than overclaiming, as before.
- [ ] Privilege separation: prototype the `HardwareWorker`-based limited-privilege service per `docs/PRIVILEGE_SEPARATION_SPIKE.md`
- [ ] RGB provider architecture: unify `RgbManager` vs. per-provider direct writes (fixes the Enhancement #17 double-write risk) as a prerequisite for any new vendor work
- [ ] i18n foundation: resource-based string system + language selector (Spanish first)
- [ ] Linux: `omencore-gui` tray icon + GUI-side config persistence
- [~] Linux: AUR packaging — PKGBUILD/systemd unit/desktop entry drafted this session (`packaging/aur/`), untested on real Arch hardware; blocked on a real icon asset, real release checksums, and a maintainer decision before actual submission (see `packaging/aur/README.md`)
- [x] Community model-database contribution pipeline (schema + validator + PR template) — `docs/model-database/model-capabilities.schema.json` (JSON Schema mirroring every `ModelCapabilities` C# field 1:1), `docs/model-database/validate_model_submission.py` (dependency-free stdlib validator: schema checks + semantic consistency checks like independent-fan-curves-implies-fan-curves + duplicate-ProductId warning against the live database), `docs/model-database/CONTRIBUTING_MODEL_DATABASE.md` (process doc), `.github/PULL_REQUEST_TEMPLATE/model_database.md`, and a `model-database-submissions` CI job (`.github/workflows/ci.yml`) that validates any files under `docs/model-database/submissions/` on every PR. Deliberately did not wire JSON loading into the runtime — merging a submission is still a maintainer hand-writing the `AddModel(...)` C# call, keeping this pure tooling/process rather than adding a new capability-loading path to a hardware-control-adjacent subsystem (see the doc's "why not just load JSON at runtime" note). Validator smoke-tested against both a valid submission and multiple deliberately-broken ones (bad ProductId pattern, out-of-range year, invalid enum, unknown field, semantic contradiction) — all correctly rejected with clear messages; CI glob/exit-code logic dry-run locally.
- [x] First-run capability disclosure view — built as a persistent "Model Capabilities" section on the existing Diagnostics tab (`Views/DiagnosticsView.xaml`) rather than a new first-run-only wizard page: shows the detected model's name/Product ID, a "User-verified" vs. "Inferred, not yet verified" badge, any model-specific `Notes` (quirks/limitations), and a supported/not-supported grid for the 12 capabilities most likely to generate "why doesn't X work" reports (fan curves, independent fan curves, real RPM readback, MUX switch, GPU power boost, Advanced Optimus, 4-zone RGB, per-key RGB, light bar, undervolt, power limits, overboost). All bound directly to the already-public `MainViewModel.DetectedCapabilities`/`ModelCapabilityDatabase` data — no new service, no new hardware probing. A persistent view was chosen over a first-run-only page because it stays useful after the user dismisses onboarding and works for users reinstalling/updating who never see first-run again. Build clean, full suite (941/941) green, live-launched with no binding/log errors.
- [ ] Decide on and budget for code signing (cert type, CI signing pipeline) — cost/process decision, not pure engineering

### Phase D — Hardware-Gated (Needs Field Evidence)

- [ ] Dedicated Balanced fan mode (per-model capability gating required)
- [ ] AMD GPU (ADL2) OC startup persistence
- [ ] AMD CPU undervolt (Curve Optimizer) startup persistence
- [ ] Self-validating PL1/PL2 readback loop to replace manual per-model field-report gating
- [x] Audit all model-name string-matching safety gates for the `IsSensitiveModel()`-style fragile-match bug class — **found and fixed a real, live recurrence.** `MainViewModel.ShouldRunStartupHardwareRestore()` (`ViewModels/MainViewModel.cs:2364`) reimplemented the exact same "is this a sensitive OMEN 16 / Victus model requiring the extra startup-restore opt-in" check inline, using the old broken literal `model.Contains("OMEN 16")` substring match — the precise bug already found and fixed in `StartupRestorePolicy.IsSensitiveModel()` back in 3.8.1 (real WMI strings like `"OMEN Gaming Laptop 16-ap0xxx"` don't contain the literal substring `"OMEN 16"`, so the safety override could silently fail to engage). The duplicate sat two lines below a call to `StartupRestorePolicy.IsEnabled(...)` in the same method — the already-fixed, already-tested policy class was right there and just wasn't used. `SystemControlViewModel` has its own independent copy of the same method (`SystemControlViewModel.cs:2964`) that was already calling `StartupRestorePolicy.IsSensitiveModel()` correctly, which is what made the MainViewModel copy's staleness obvious by comparison. **Fix:** replaced the inline re-check with a delegating call to `StartupRestorePolicy.IsSensitiveModel(model)`, matching `SystemControlViewModel`'s already-correct version. No new test added — the underlying logic is already covered by `StartupRestorePolicyTests.cs`'s theory cases (including the exact `"OMEN Gaming Laptop 16-..."` shape that was broken), and this is now a one-line delegation rather than independent logic to re-verify. Full test suite + build clean.
  - **Rest of the sweep, checked and found not to be the same bug class:** every other `model.Contains("OMEN"...)` site in the codebase (`SystemControlViewModel`'s three EC-GPU-boost-detection sites, `GpuSwitchService`, `WmiBiosMonitor.ShouldPreferWorkerCpuTemp`) matches broadly on the bare `"OMEN"` substring (present in every real HP OMEN WMI string seen in the field logs reviewed this session) to *enable* a feature-detection path, not to *block* a safety-sensitive one — over-matching there is the safe direction, unlike the `IsSensitiveModel` pattern where under-matching silently disables a safety block. `ShouldPreferWorkerCpuTemp` in particular already uses the correct defensive pattern: multiple board-specific substrings covering different naming conventions, not a single fragile literal.
  - **Noted but out of scope for this pass:** `MainViewModel` and `SystemControlViewModel` maintain two independent copies of `ShouldRunStartupHardwareRestore` — the duplication itself (not just today's bug) is exactly the kind of thing the `MainViewModel` decomposition work above should eventually consolidate, but that's a larger, separate change.
  - **Follow-up this cycle: checked the rest of the duplicated method names between these two ViewModels and found a bigger version of the same problem.** `ApplyUndervoltAsync`/`ResetUndervoltAsync` were also duplicated, and `MainViewModel`'s copy (plus its whole surrounding command/property wiring — `ApplyUndervoltCommand`, `RequestedCoreOffset`, `UndervoltStatus`, etc.) was meaningfully behind `SystemControlViewModel`'s real implementation: no per-core offsets, no config persistence, no error handling. Traced it to `Views/SystemControlView.xaml` — the only thing that ever bound to `MainViewModel`'s copy — and confirmed via exhaustive search (XAML instantiation, DataTemplate mapping, code-behind construction) that this view was never placed anywhere in the app's visual tree, superseded by `TuningView.xaml` and never cleaned up. **Removed** the orphaned view and ~80 lines of dead code in `MainViewModel.cs` (now 5,250 lines, down from 5,333) — kept the shared `UndervoltService` instance/lifecycle intact, only removed the dead UI wiring around it. Full detail, including a process note about how the file deletion was handled, in `CHANGELOG_v4.0.0.md`.
  - **Broadened the audit to every ViewModel pair, not just these two** (cross-referenced private method names across all 18 ViewModel files). Found a second `LoadCurve`/`SaveCustomPreset` duplication between `MainViewModel` and `FanControlViewModel` — **investigated, and deliberately left alone this session**, unlike the undervolt case. Traced the same way: `FanControlView.xaml`'s custom-curve editing controls (`SelectedPreset`, `CustomFanCurve`, `CustomPresetName`, `SaveCustomPresetCommand`) bind against `FanControlViewModel` (confirmed live — reached via `AdvancedView.xaml`'s `<views:FanControlView DataContext="{Binding FanControl}" />`), not `MainViewModel`'s same-named members, which looked like the same "orphaned duplicate" pattern at first. **But it isn't as clean-cut**: `MainViewModel.FanPresets` (the underlying preset *list*, as opposed to the curve-editing sub-feature) turned out to be genuinely live and load-bearing — used extensively for hotkey fan-mode cycling and tray Quick-Profile resolution (`ResolveNextHotkeyFanMode`-style lookups, ~15 call sites). Only `CustomFanCurve`/`CustomPresetName`/`SaveCustomPresetCommand`/the `LoadCurve`/`SaveCustomPreset()` methods look dead; `FanPresets`/`SelectedPreset` do not, and `SelectedPreset`'s setter is what wires the two together (`LoadCurve` is called from inside it). Untangling live fan-mode-cycling logic from dead curve-editing logic in the same property needs careful, single-purpose tracing — not something to rush in the same pass as the undervolt cleanup, especially right after a process mistake in that cleanup (see `CHANGELOG_v4.0.0.md`). Left as a scoped, documented follow-up rather than acted on.
  - **Rest of the cross-ViewModel duplicate names checked and ruled out as low-stakes:** `SetStatusAction`/`SetStatusDone`/`SetStatusFailed` (shared across `BloatwareManagerViewModel`/`MemoryOptimizerViewModel`/`SystemOptimizerViewModel` — a common status-message helper pattern, not safety logic), `OpenConfigFolder` (`MainViewModel`/`SettingsViewModel` — both just open a folder in Explorer, one has a try/catch the other doesn't, functionally trivial either way), `DismissFanPerformanceInfoBanner`, `CreateProfile`, `OnActiveProfileChanged`, `OnPerformanceModeApplied`, `OnMonitoringSampleUpdated` — all either coincidental same-named event handlers for different scopes or genuinely low-consequence UI helpers. None show the "one copy fixed/complete, one copy stale" shape that made the `IsSensitiveModel` and undervolt cases worth fixing.
- [ ] Board `8D41` keyboard RGB via Darfon `0d62:54bf` HID controller — take up [GitHub #151](https://github.com/theantipopau/omencore/issues/151)'s offer to capture USB HID feature reports before writing the backend
- [ ] Board OMEN MAX 16z-ak000 (AMD, `UserVerified: No`) — fan duty%/RPM mapping doesn't match reporter's expectation (10% ≈ 1000 RPM against a claimed ~6000 RPM max; 0% floors ~300 RPM instead of off). Root cause undetermined: this board's RPM readback returns null, so all displayed RPM is a software estimate, not sensor truth — needs either real tachometer confirmation or a reporter-submitted Calibration Wizard profile (now more likely accurate after the 5000ms delay fix above) before any formula change. See the SprinkSponk field-report section above for the full trace.

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
