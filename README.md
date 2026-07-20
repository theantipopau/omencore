<div align="center">

<img src="docs/screenshots/githublogo.png" alt="OmenCore" width="520" />

# OmenCore

### Lightweight local control for HP OMEN and Victus gaming laptops

[![Version](https://img.shields.io/badge/version-4.0.0-red.svg?style=for-the-badge)](docs/CHANGELOG_v4.0.0.md)
[![License](https://img.shields.io/badge/license-MIT-green.svg?style=for-the-badge)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg?style=for-the-badge)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Discord](https://img.shields.io/badge/Discord-Join-5865F2.svg?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/9WhJdabGk8)

</div>

---

OmenCore is an independent control center for HP OMEN and Victus systems. It focuses on the local workflows people actually use in OMEN Gaming Hub: fan control, performance profiles, telemetry, keyboard lighting, OSD, power tools, diagnostics, and safe cleanup of HP background software.

It runs without ads, account prompts, cloud telemetry, or OMEN Gaming Hub. Hardware access is handled through local WMI BIOS, EC, PawnIO, Linux sysfs, and platform backends when the device exposes them.

![OmenCore main window](docs/screenshots/main-window.png)

## At A Glance

| Area | What OmenCore Provides |
|---|---|
| Fan and thermal control | WMI BIOS fan profiles, Max/Auto handoff, custom curves where the model safely supports them |
| Performance profiles | Quiet, Balanced, Performance, custom profile routing, power-policy diagnostics |
| GPU controls | MUX switching and GPU Power Boost on supported OMEN firmware |
| RGB | OMEN keyboard zone lighting plus external RGB provider integration where supported |
| Monitoring | CPU/GPU temperature, load, fan telemetry, health state, history, and core-control diagnostics |
| OSD and tray | Click-through overlay, hotkey toasts, quick popup, live tray status |
| Cleanup | OMEN Gaming Hub and HP bloatware detection/removal helpers |
| Linux | CLI and Avalonia GUI for supported hp-wmi/ec_sys/sysfs paths |

## Why People Use It

| OmenCore Principle | Result |
|---|---|
| Local first | No sign-in, no ads, no outbound telemetry ([details](docs/TELEMETRY.md)) |
| Safety gated | Unsupported EC/fan/RGB paths stay hidden or diagnostic-only |
| Field driven | Model quirks are tracked by ProductId, BIOS behavior, and logs |
| Fast startup | Hardware polling and heavy providers are deferred where possible |
| Honest capability UI | Requested, confirmed, degraded, and unsupported states are separated |

## Current Release

**Version:** 4.0.0<br>
**Status:** Code-complete and test-verified in this environment (953/953 tests, 0 build warnings); artifacts not yet built or tagged<br>
**Release notes:** [docs/CHANGELOG_v4.0.0.md](docs/CHANGELOG_v4.0.0.md)<br>
**Roadmap:** [docs/ROADMAP_v4.0.0.md](docs/ROADMAP_v4.0.0.md)

v4.0.0 is a sustainability-focused major release, not a features-first one: it pays down the architecture debt that had accumulated across the 3.x series rather than adding new hardware-control surface. No fan/thermal/EC control *behavior* changed in this release (the two exceptions are both bug fixes restoring already-documented behavior, not new logic — see the changelog header for detail). Highlights: a shared DI composition root replacing 19 of ~40 manually-wired fields in the `MainViewModel` god-object, a consolidated polling scheduler replacing part of a 27-timer sprawl, a community-contributable model-database submission pipeline, a persistent "Model Capabilities" diagnostics panel, an accessibility labeling pass across five views (~140 controls), and game-profile automation improvements (window-title disambiguation, WMI event-based process detection, multi-game restore handling).

### v4.0.0 Highlights

- **Architecture:** introduced a real DI composition root — 19 of ~40 manually-wired `MainViewModel` fields migrated onto it, and the hardware bring-up sequence (NVAPI/PawnIO/WMI BIOS/capability detection/EC/fan-controller construction) extracted into its own `HardwareBringup` class as a prerequisite for the rest.
- **Architecture:** built a shared `PollingScheduler`/`UiPollingCoordinator` and migrated the tray icon, quick popup, and OSD timers onto it — a first cut at consolidating a 27-timer sprawl (corrected from an earlier "21 timers" estimate) across three different timer APIs.
- **Removed** ~80 lines of dead, orphaned CPU-undervolt UI wiring in `MainViewModel` — a second, meaningfully-behind copy of `SystemControlViewModel`'s real implementation, bound only to a view that was never in the app's visual tree.
- **Fixed a safety-gate bug:** the OMEN 16/Victus sensitive-model startup-restore check had a live, un-updated copy of the exact fragile-string-match bug already fixed elsewhere in 3.8.1 — could silently skip the extra safety opt-in it exists to enforce.
- **Fixed:** the "disable thermal protection" toggle's documented promise ("fans will NEVER be automatically overridden") didn't actually hold for custom fan curves — a separate always-on safety clamp ignored the toggle. Also made the emergency-override temperature itself configurable (was a hardcoded 95°C).
- Community-contributable model-database pipeline: JSON schema, a dependency-free validator, a PR template, and a CI job — so new hardware support no longer has to funnel through one person hand-writing every entry.
- New persistent "Model Capabilities" panel on the Diagnostics tab — shows what your detected model does and doesn't support, and whether that profile is field-verified or inferred, before you go looking for a setting that isn't there.
- Game profiles: window-title disambiguation for same-exe-different-game cases, WMI event-based process detection replacing pure polling, and multi-game restore handling (switches to a still-running game's profile instead of unconditionally restoring defaults).
- Accessibility: `AutomationProperties` labeling added across Dashboard, Advanced, FanControl, Lighting, and (partially) Settings views — roughly 140 previously-silent controls now announce correctly to screen readers.
- Tray menu: "Max Fan" promoted to one click deep (was three), plus a new in-app notification history and a persistent "Report a Problem" entry reachable regardless of Lite/Advanced mode.
- Corrected a stale risk record: `BiosUpdateService` was documented as "the firmware-write path" — it never writes firmware at all, only checks for updates and hands off to HP's own tools. Added 19 tests for the parts that actually matter (version comparison, URL construction).
- New interactive GitHub Pages site (replaces the old custom omencore.info site) with live release info, a feature/comparison showcase, and donation info.

Full detail on every item, including what was investigated and deliberately *not* changed, in [docs/CHANGELOG_v4.0.0.md](docs/CHANGELOG_v4.0.0.md).

### v3.9.0 Highlights (previous minor release)

- **Silent failure:** the OMEN key action setting (Settings → OMEN Key) was completely non-functional for all four UI options — the saved string never matched any backing enum value, so any non-default choice was silently discarded on every relaunch.
- **Silent failure:** newly created or duplicated game profiles were lost if the app crashed before another action triggered a save — `CreateProfile()`/`DuplicateProfile()` never persisted.
- **Silent failure:** `FanController.ResetEcToDefaults()` and bridge-temperature reads swallowed exceptions with no diagnostic trail; both now log the failure (no control-behavior change).
- **Silent failure:** crash reports never included a stack trace, making community bug reports nearly impossible to diagnose from the log alone; both global exception handlers now log full `[CrashTrace]` stacks.
- Tray icon: white temperature-digit text on the yellow/green badge background is now black above a computed luminance threshold — fixes an eye-straining contrast issue reported on Discord.
- Quick Access popup: new "Enable quick access popup" toggle so users who keep hitting Display Off by accident can disable the whole popup instead of losing the OMEN key entirely.
- GPU Power Boost now actually follows the General tab's Performance/Balanced/Quiet profile cards, the tray quick-profile menu, and the hotkey cycle — previously the boost level was frozen at whatever was last set manually, regardless of profile.
- Fixed the Custom tab rendering with the default white WPF theme instead of the app's dark theme (a local `TabItem.Style` was overriding the shared dark template).
- OSD performance-mode row no longer shows a stale "Balanced" default before the first confirmed runtime state arrives; falls back to the last explicitly-applied mode instead of a hardcoded default.
- OSD no longer silently drops behind borderless/windowed-fullscreen games — it now re-asserts topmost every second instead of only once. True DXGI exclusive fullscreen still can't show any overlay window (a Windows compositor limitation).
- Fixed an idle-time integer overflow in Automation Service rules that could misbehave after ~24.9 days of uptime, and a battery-percentage automation bug that could fire "above N%" rules constantly on desktops/sensor-failure systems.
- Fixed the auto-updater's HardwareWorker shutdown sequence: one process failing to close no longer skipped closing the rest, and the installer now waits for confirmed exit instead of assuming it.
- Direct model entries added for HP Victus 15-fa1xxx (`8C3F`, fan-control delay fix) and OMEN 16 (2024) wf1xxx Intel (`8C77`, V1/V2 profile-mismatch crash fix), a family-fallback profile for HP Victus 15 2025 AMD (`fb3xxx`), plus an OMEN Transcend 14 (`8C58`) capability alignment.

### v3.8.2 Highlights (previous patch release)

- **Critical:** fixed an Application Hang within 10-20s of launch (`8BCD`) caused by a named-pipe desync between the app and its out-of-process hardware worker — concurrent requests could race and consume each other's stale replies, with no reconnect-on-failure.
- **Critical (safety):** fixed fans stuck at max independent of temperature, lid-close failing to suspend, and a resulting BIOS thermal shutdown ([#146](https://github.com/theantipopau/omencore/issues/146)) — the Max-mode keepalive timer is now stopped unconditionally on suspend, not just as a side effect of a successful restore.
- Power Automation's AC/Battery fan and performance profiles now actually apply at app startup — previously they only took effect on the next power-source change.
- Diagnostics export wiring fix: `wmi-command-history.txt`, `hardware-info.txt`, and `ec-state.txt` had been empty placeholders in every diagnostics zip ever exported, regardless of hardware state; bug reports going forward will contain real data.
- Fixed the Optimizer's "Disable Last Access Timestamps" toggle always reporting itself as failed even when it had applied correctly (a registry-encoding mismatch, not an elevation issue).
- Fixed a rare, timing-dependent background crash in the fan monitor loop during shutdown.
- New conservative identities added for OMEN Slim 16-an0xxx (`8D40`), OMEN 17-ck1xxx (`8A18`), and OMEN Transcend 14-fb1xxx (`8E41`).

### v3.8.1 Highlights (previous patch release)

- `8A18` OMEN 17-ck1xxx: conservative exact capability profile, with V1 fan-level fallback explicitly labeled as an estimate rather than measured RPM, and fan verification evidence kept honest about command-success vs. physical confirmation.
- Quick Access popup shortcut is now configurable (Display Off, Lock Windows, or Disabled) to prevent accidental display-off clicks.
- Saved Custom fan-curve selection now migrates correctly when `LastFanPresetName` is missing or stale, without bypassing the startup fan-write safety gate.
- GPU OC Tuning page shows a dedicated startup-reapply status chip explaining whether a confirmed profile is enabled or blocked, and why.
- The OMEN 16/Victus sensitive-model startup-restore safety override now matches real-world HP model-name variants instead of only the literal "OMEN 16" substring.
- OMEN-key diagnostics now record the last accepted/rejected key candidate (source, VK/scan codes, and reason) for field debugging of #141-class reports.
- `HpWmiBios` heartbeat, the fan countdown-extension reassert loop, and the Razer Chroma heartbeat are now visible in background-timer diagnostics.
- `8D40` OMEN Slim 16-an0xxx: exact conservative identity added (GitHub #145), replacing low-confidence family fallback.
- Fixed Performance Profile silently reverting to Balanced after relaunch when changed via the tray menu, the `Ctrl+Shift+E` hotkey cycle, or the General page's quick-profile buttons (GitHub #145) — these paths now persist the choice the same way the System Control page always did.

Older release notes ([v3.8.0](docs/CHANGELOG_v3.8.0.md) and earlier) are kept in `docs/` rather than summarized here.

## Current Development Focus

**v4.0.0 is a sustainability-focused cycle**, not a features-first one — see the Vision section of [docs/ROADMAP_v4.0.0.md](docs/ROADMAP_v4.0.0.md) for the full rationale. Instead of another field-driven patch cycle, this release pays down architecture debt (the `MainViewModel` god-object, timer sprawl), invests in process (a community-contributable model database instead of every SKU going through one person), and closes an accessibility gap. All work is code-complete and test-verified (953/953) in this environment — artifacts have not yet been built. Unlike prior releases, nothing in 4.0.0 is gated on physical-hardware confirmation: no fan/thermal/EC control behavior changed (the two exceptions are bug fixes restoring already-documented/already-tested behavior, not new logic).

**What's intentionally *not* in this release:** the larger architectural items — privilege separation, a real RGB provider architecture, i18n, Linux tray/config persistence — are scoped in the roadmap but not started; they're multi-release efforts, not something to rush into one cycle. Every hardware-specific item still awaiting field evidence from 3.9.0 (fan RPM/thermal reports, RGB routing, OC/UV persistence) carries forward unchanged, since 4.0.0 didn't touch that code.

The active work is tracked in:

- [docs/CHANGELOG_v4.0.0.md](docs/CHANGELOG_v4.0.0.md) - the current release notes, written incrementally as work landed.
- [docs/ROADMAP_v4.0.0.md](docs/ROADMAP_v4.0.0.md) - the full scope, phase ordering, and execution checklist this cycle worked through.
- [docs/CHANGELOG_v3.9.0.md](docs/CHANGELOG_v3.9.0.md) - the prior release's notes and validation status.

Prior-release work is kept for historical reference:

- [docs/CHANGELOG_v3.8.2.md](docs/CHANGELOG_v3.8.2.md), [docs/CHANGELOG_v3.8.1.md](docs/CHANGELOG_v3.8.1.md), [docs/CHANGELOG_v3.8.0.md](docs/CHANGELOG_v3.8.0.md) - field fixes, UI polish, diagnostics, and validation status for each release.
- [docs/3.8.1-BUG-REPORTS.md](docs/3.8.1-BUG-REPORTS.md), [docs/3.8.0-BUG-REPORTS.md](docs/3.8.0-BUG-REPORTS.md) - tracked model reports and issue follow-up.

The consolidated `core-control-readiness.txt` diagnostic report (fan backend/readback state, RGB surface/backend state, tuning startup/readback state, monitoring health, next validation actions) introduced in 3.8.0 remains in place, joined in 4.0.0 by the persistent "Model Capabilities" panel on the Diagnostics tab.

## Downloads

Release artifacts are published on the [GitHub Releases](https://github.com/theantipopau/omencore/releases/latest) page.

| Artifact | Platform | Recommended For |
|---|---|---|
| `OmenCoreSetup-4.0.0.exe` | Windows | Most users. Installs app and can install PawnIO. |
| `OmenCore-4.0.0-win-x64.zip` | Windows | Portable use, testing, or no installer preference. |
| `OmenCore-4.0.0-linux-x64.zip` | Linux | CLI plus Avalonia GUI, self-contained runtime. |

Final GitHub release notes must include SHA256 hashes for every artifact. The in-app updater requires release hashes before it will install an update.

## Quick Start

### Windows

1. Download `OmenCoreSetup-4.0.0.exe` from [Releases](https://github.com/theantipopau/omencore/releases/latest).
2. Verify the SHA256 hash from the release notes.
3. Run the installer as Administrator.
4. Keep PawnIO selected unless you only want monitoring and WMI-only features.
5. Launch OmenCore from the Start Menu.

Portable users can download `OmenCore-4.0.0-win-x64.zip`, extract it to a normal folder, and run `OmenCore.exe` as Administrator.

See [INSTALL.md](INSTALL.md) for the full Windows guide.

### Linux

```bash
VERSION=4.0.0
wget "https://github.com/theantipopau/omencore/releases/download/v${VERSION}/OmenCore-${VERSION}-linux-x64.zip"
mkdir -p OmenCore-linux-x64
unzip "OmenCore-${VERSION}-linux-x64.zip" -d OmenCore-linux-x64
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

sudo ./omencore-cli status
./omencore-gui
```

Prefer launching the GUI as your normal desktop user. Use `sudo` for CLI operations that need hardware access.

For bug reports, collect a triage bundle:

```bash
./qa/collect-linux-triage.sh
```

See [INSTALL.md](INSTALL.md) and [docs/LINUX_INSTALL_GUIDE.md](docs/LINUX_INSTALL_GUIDE.md) for Linux details.

## Feature Matrix

### Thermal And Fan Control

- WMI BIOS fan profile control on supported OMEN/Victus laptops.
- Max, Auto, Quiet, Gaming, Extreme, and custom presets where capability allows.
- Custom fan curves with temperature breakpoints on models with validated curve support.
- Profile-only fan gating for models where the firmware supports OEM profile modes but not safe manual curve writes.
- Restore OEM Auto action to release OmenCore fan ownership and return to firmware auto mode.
- Fan command history, launch diagnostics, and core-control readiness exports for field validation.

### Performance And Power

- Quiet, Balanced, Performance, and custom profile routing.
- WMI thermal-policy fallback when direct EC/MSR power limits are unavailable.
- CPU/GPU power apply traces in diagnostics.
- Intel undervolt and TCC controls where the model, BIOS, and runtime allow them.
- GPU Power Boost on supported OMEN firmware.
- GPU OC and power-limit profile storage where backend support is available or power-limit-only routing is exposed.
- MUX switching where the BIOS exposes Hybrid, Discrete, or Integrated modes.

### RGB And Lighting

- OMEN 4-zone keyboard lighting with profile, zone, brightness, and backlight operations.
- Model-aware fallback and serialized keyboard lighting writes.
- RGB diagnostics showing backend ownership, active path, and conflict status.
- OMEN Max per-key-capable hardware detection plus first-pass HID per-key routing for known/inferred HP keyboard controller PIDs.
- External RGB provider surfaces for Corsair, Logitech, Razer, OpenRGB, and system providers where available.
- Built-in RGB scenes include static, breathing, spectrum, wave, ambient, audio-reactive, Heat Wave, Calm Pulse, and Lights Off paths where providers support them.
- Readiness diagnostics report the active HP keyboard surface, observed physical surface, and whether a result is verified, accepted/unverified, unavailable, or blocked by a conflict.

Note: OMEN Max dedicated HID per-key routing is intentionally conservative until field logs confirm the correct USB PID list and physical keyboard segment mapping.

### Monitoring, OSD, And Diagnostics

- CPU/GPU temperature, load, fan level/RPM, battery, memory, storage, and GPU telemetry.
- Out-of-process hardware worker for crash isolation.
- Telemetry health states: valid, inactive, unavailable, stale, degraded, and invalid.
- Click-through OSD with RTSS FPS integration where available.
- RTSS FPS display falls back to average FPS when instant FPS is unavailable and keeps RTSS unavailable/waiting states explicit.
- Tray quick popup and status badges.
- Diagnostic exports with model identity, RGB path, resource footprint, fan history, launch readiness, core-control readiness, tuning safety, and runtime state.

### System Tools

- Guided OMEN Gaming Hub cleanup.
- Bloatware scanner and removable HP app inventory.
- Memory optimizer and gaming-mode helpers.
- Per-game profile automation with exact executable-path matching, duplicate apply suppression, optional default restore on exit, and feature-gated process monitoring.
- Auto-update with SHA256 verification.

## Hardware Support

OmenCore is built for HP OMEN and HP Victus laptops. Desktop OMEN systems are treated conservatively.

| Hardware Class | Support Level | Notes |
|---|---|---|
| OMEN 15/16/17 laptops | Primary | WMI BIOS, fan/profile, telemetry, RGB, power features by model |
| Victus laptops | Supported with gates | Fan/profile/monitoring/backlight; GPU TGP and undervolt often unavailable |
| OMEN Max 16/17 | Active validation | Power/profile identity paths; HID per-key RGB backend needs PID/segment field confirmation |
| OMEN Transcend | Active validation | Profile-based fan and lighting paths vary by ProductId |
| OMEN desktops | Limited | Monitoring/profile/cleanup; fan writes are safety-gated |
| HP Spectre and other HP | Limited | Monitoring and selected WMI paths only |
| Non-HP systems | Unsupported | Monitoring-only behavior may work, control features are not targeted |

Model support is keyed by ProductId where possible. Diagnostic exports include a model identity summary so unsupported or inferred profiles can be fixed without guesswork.

## Requirements

### Windows

- Windows 10 build 19041+ or Windows 11.
- Administrator rights for WMI BIOS, EC, MSR, fan, and power operations.
- Self-contained .NET 8 runtime in release builds.
- PawnIO recommended for advanced EC/MSR features and Secure Boot-compatible low-level access.

### Linux

- x64 Linux with `hp-wmi`, `ec_sys`, or compatible hwmon/sysfs interfaces.
- Root privileges for hardware writes.
- A normal desktop session for the Avalonia GUI.
- Kernel support varies heavily by model and distro.

## Backend Priority

Windows fan control normally follows this order:

1. WMI BIOS - preferred for modern OMEN laptops.
2. PawnIO-backed EC/MSR paths - advanced access where safe and validated.
3. OGH proxy - last-resort fallback when local firmware paths require it.

Linux control normally follows available sysfs/hwmon capability:

1. `hp-wmi` / platform profile.
2. `hp-wmi` hwmon PWM and fan input paths.
3. `ec_sys` for older models.
4. Diagnostic-only mode when no safe write path exists.

## Known Limits

4.0.0 changed no fan/thermal/EC control behavior, so every hardware-confirmation item below carries forward unchanged from 3.9.0 — nothing here was introduced or resolved by the 4.0.0 cycle itself. 4.0.0's own work (DI composition root, timer consolidation, accessibility labeling, model-database tooling) is architecture/UI/process only and doesn't need physical-hardware confirmation; the one open item specific to 4.0.0 is that its accessibility labeling hasn't been verified with an actual screen reader (NVDA/Narrator/JAWS) in this environment — build-clean and test-green confirm nothing broke, not that the announced names read well.

- `8C77` OMEN 16 (2024) wf1xxx Intel: the V1/V2 profile-mismatch fix is code-complete and test-verified but **not yet confirmed on the reporter's physical hardware**.
- `8BCD` OMEN 16 xd0010AX: four fan-behavior reports (Balanced-switch oscillation, Quiet RPM floor, Quiet thermal ceiling, ramp-down stepping) are evidence-gated — no fan/thermal code was changed without physical-hardware evidence, per project safety policy.
- The hang fix (`8BCD`), the fan-stuck-at-max/failed-standby fix (`88D2`), and the Power Automation/Optimizer fixes (`8D41`) from v3.8.2 remain code-complete and test-verified in this environment but **not yet confirmed on the reporters' physical hardware** — see Release Conditions in [docs/CHANGELOG_v3.8.2.md](docs/CHANGELOG_v3.8.2.md).
- Some 3.8.0 and 3.8.1 fixes still require physical hardware validation, especially fan ramp-down, RGB surface routing, and GPU wattage parity.
- OMEN Max per-key RGB now has a first-pass HID backend in active development, but it is not fully verified until field logs confirm the USB PID list and physical segment behavior.
- `8DCD` Victus 15 fan-speed collapse under sustained load (GitHub #143) is under investigation; treat it as thermal-safety critical until disproven.
- `8D26` OMEN 16-ap0xxx dedicated key and Fn+P event routing (GitHub #141) needs shipped-artifact and physical-hardware confirmation.
- `8E41` OMEN Transcend 14 idle-load thermal-emergency reports are under investigation; current evidence leans toward real (brief) thermal excursions rather than a sensor glitch, but the zero-debounce safety response itself is deliberately unchanged either way.
- OGH Eco mode parity is tracked but not implemented.
- Direct PL1/PL2 controls remain firmware/MSR gated on many systems.
- OSD now fights back against borderless/windowed-fullscreen games stealing topmost (fixed in 3.9.0). True DXGI exclusive-fullscreen still cannot show any overlay window — that's a Windows compositor limitation, not something a WPF window can override without D3D/DXGI hook injection.
- `8574` legacy OMEN 15 support is partial until fresh diagnostics confirm effective fan command readback.

## Active Validation Targets

Carried forward unchanged from 3.9.0 — 4.0.0 didn't touch fan/thermal/EC/RGB control code, so none of these needed re-validation this cycle.

- `8C77` OMEN 16 (2024) wf1xxx Intel: confirm the direct model entry and V1 fan-control path resolve the `FileNotFoundException` crash on the Custom Fan Curve tab.
- `8BCD` OMEN 16-xd0010AX: per-poll EC register dump during Balanced-switch fan oscillation; RPM vs. EC register snapshot for the Quiet RPM floor; WMI ThermalPolicy + per-zone temp log at the Quiet thermal ceiling; 100ms-resolution RPM log during ramp-down.
- `8BCD` OMEN 16-xd0010ax: confirm the named-pipe hang fix actually stops the Application Hang on the original reporter's hardware.
- `88D2` OMEN 15-en1xxx: confirm the Max-mode keepalive fix lets lid-close suspend cleanly with no BIOS thermal shutdown.
- `8D41` OMEN MAX 16 ah0500na: confirm Power Automation now applies at boot; RGB light-bar-vs-keyboard routing and battery-preset-name substitution still need a session log.
- `8D40` OMEN Slim 16-an0xxx: exact identity validation, plus Battery Care (Charge Limit) WMI evidence — now collectible via the fixed diagnostics export.
- `8DCD` Victus 15: bounded, abortable load test confirming Performance mode no longer drops below 2000 RPM above 80C.
- `8D26` OMEN 16-ap0xxx: Fn+F2 never-intercept behavior and dedicated OMEN-key/Fn+P event path on physical hardware.
- `8E9A` HyperX OMEN MAX 16t-ah100: exact conservative identity pending full diagnostic evidence.
- `8A18` OMEN 17-ck1xxx: bounded load test with independent physical RPM/temperature source.
- `8E41` OMEN Transcend 14-fb1xxx: diagnostics-zip-level raw per-poll temperature evidence for the idle thermal-emergency reports.
- `8D87` OMEN Max: WMI-only Max fan hold, Restore OEM Auto, and HID per-key RGB PID confirmation.
- `8BD4` Victus 16: conservative WMI V1 Auto/Max handoff and WMI ColorTable RGB confirmation.
- `8C30` Victus 15-fb1xxx: Quiet/Balanced/Performance WMI policy routing and wattage/RPM readback validation.
- `878C` OMEN 15-ek0xxx: Quick Profile fan wake/ramp validation after exact WMI fallback routing.
- `8600` OMEN 15-dh0xxx: PawnIO install/reboot telemetry recovery plus Quiet/Balanced/Performance/Auto/Max fan-mode validation.
- `88EE` Victus 16-e0194nw: exact ProductId identity confirmation plus fan/RGB/readback evidence before enabling capabilities beyond conservative routing.
- `8BCD` Linux: degraded WMI/ACPI reporting with effective fan/RGB/battery readback before claiming full control.
- GPU OC startup reapply: confirmed-profile reboot test on NVIDIA, plus AMD manual-only wording or equivalent persistence.
- Background memory/responsiveness: scenario-matrix measurement against the 3.8.1 budgets before claiming any reduction.
- Startup restore: keep hardware restore opt-in until fan, RGB, performance, undervolt, and GPU OC readback passes on the target model.

## Development

### Build

```powershell
git clone https://github.com/theantipopau/omencore.git
cd omencore
dotnet restore OmenCore.sln
dotnet build OmenCore.sln --configuration Release
```

### Run Tests

```powershell
dotnet test OmenCore.sln
```

### Build Windows Artifacts

```powershell
pwsh ./build-installer.ps1
```

Expected outputs:

- `artifacts/OmenCoreSetup-4.0.0.exe`
- `artifacts/OmenCore-4.0.0-win-x64.zip`
- `artifacts/SHA256SUMS-4.0.0.txt`

### Build Linux Artifact

```powershell
pwsh ./build-linux-package.ps1
```

Expected outputs:

- `artifacts/OmenCore-4.0.0-linux-x64.zip`
- `artifacts/OmenCore-4.0.0-linux-x64.zip.sha256`
- `artifacts/version.json`
- `artifacts/linux-version-verification-4.0.0-linux-x64.json`

## Release Checklist

Before publishing a stable GitHub release:

1. Confirm `VERSION.txt`, project versions, installer version, README, and INSTALL all match.
2. Run `dotnet restore`, Release build, test suite, and `git diff --check`.
3. Build Windows installer/portable and Linux zip.
4. Generate SHA256 hashes for all artifacts.
5. Add hashes, known limits, and hardware validation status to GitHub Release notes.
6. Upload artifacts.
7. Tag the release only after the final notes and artifacts match.

## Troubleshooting

| Symptom | First Thing To Check |
|---|---|
| Fan control has no effect | Model capability summary and fan command history in diagnostics |
| Fans stay elevated | Use Restore OEM Auto, then export diagnostics with `core-control-readiness.txt` |
| GPU Power Boost changes but wattage does not | Firmware/backend support and FurMark/telemetry readback |
| PawnIO unavailable | Keep PawnIO selected in the installer, reboot, and run as Administrator |
| PawnIO setup asks for `-install` or `-uninstall` | Use v3.8.0+ installer builds; standalone fallback is `PawnIO_setup.exe -install` from an elevated terminal |
| Undervolt hidden | Model or BIOS may block MSR undervolt; check tuning readiness and startup recovery state |
| RGB turns off or does not restore | Check active keyboard backend, target surface, accepted/unverified status, and conflicting HP lighting tools |
| Battery Care (Charge Limit) fails | Confirm AC power is connected; compare against OMEN Gaming Hub; export `wmi-command-history.txt` and BIOS version |
| Performance profile reverts to Balanced after relaunch | Fixed in 3.8.1 for tray/hotkey/General quick-profile changes (GitHub #145); if still seen, note which entry point you used |
| OSD not visible in a game | Fixed in 3.9.0 for borderless/windowed-fullscreen (overlay now re-asserts topmost every second); true DXGI exclusive fullscreen still cannot show any overlay window — switch the game to borderless/windowed-fullscreen mode |
| Linux permission denied | Run CLI command with `sudo` |

Windows logs are stored under `%LOCALAPPDATA%\OmenCore\`. Linux diagnostics can be collected with `sudo ./omencore-cli diagnose --report`.

## Documentation

- [INSTALL.md](INSTALL.md) - installation, upgrade, portable use, Linux setup, uninstall.
- [docs/CHANGELOG_v4.0.0.md](docs/CHANGELOG_v4.0.0.md) - current release notes.
- [docs/ROADMAP_v4.0.0.md](docs/ROADMAP_v4.0.0.md) - current roadmap, scope, and execution checklist.
- [docs/CHANGELOG_v3.9.0.md](docs/CHANGELOG_v3.9.0.md) - previous release notes.
- [docs/3.8.1-BUG-REPORTS.md](docs/3.8.1-BUG-REPORTS.md) - active field report tracking (covers GitHub #141-#146 and Discord reports through v3.8.2).
- [docs/CHANGELOG_v3.8.2.md](docs/CHANGELOG_v3.8.2.md) - earlier release notes.
- [docs/CHANGELOG_v3.8.1.md](docs/CHANGELOG_v3.8.1.md) - earlier release notes.
- [docs/CHANGELOG_v3.8.0.md](docs/CHANGELOG_v3.8.0.md) - earlier release notes.
- [docs/CHANGELOG_v3.7.1.md](docs/CHANGELOG_v3.7.1.md) - earlier release notes.
- [docs/3.8.0-CORE-CONTROLS-NEXT-STEPS.md](docs/3.8.0-CORE-CONTROLS-NEXT-STEPS.md) - core control validation and practical next steps.
- [docs/3.8.0-BUG-REPORTS.md](docs/3.8.0-BUG-REPORTS.md) - prior 3.8.0 field report tracking.
- [docs/FINAL_RELEASE_CHECKLIST.md](docs/FINAL_RELEASE_CHECKLIST.md) - a historical release-gate checklist from the v3.7.1 cycle, kept for reference; not maintained per release.
- [docs/3.7.1-BUG-REPORTS.md](docs/3.7.1-BUG-REPORTS.md) - field report tracking.
- [docs/LINUX_INSTALL_GUIDE.md](docs/LINUX_INSTALL_GUIDE.md) - Linux details.
- [docs/ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) - antivirus and driver guidance.
- [docs/DEFENDER_FALSE_POSITIVE.md](docs/DEFENDER_FALSE_POSITIVE.md) - Defender guidance.
- [drivers/PawnIO/README.md](drivers/PawnIO/README.md) - PawnIO backend details.

## Version History

| Version | Summary |
|---|---|
| 4.0.0 | Major release: sustainability/architecture cycle, not features-first — DI composition root started (19/~40 `MainViewModel` fields migrated), shared polling coordinator, community model-database contribution pipeline, persistent "Model Capabilities" diagnostics panel, accessibility labeling pass (~140 controls across 5 views), game-profile window-title disambiguation + WMI event-based detection + multi-game restore, dead-code removal, safety-gate string-match audit fix. No fan/thermal/EC control behavior changed. |
| 3.9.0 | Minor release: non-functional OMEN key action fix, game-profile-loss-on-crash fix, silent EC-failure logging, crash stack traces, GPU Power Boost/profile linkage, Custom tab theme fix, OSD stale-default fix, AutomationService idle/battery bugs, HardwareWorker update-kill-loop fix, `8C3F`/`8C77` model additions |
| 3.8.2 | Patch release: critical Application Hang fix (#BUG-3820-001), fans-stuck-at-max/failed-standby fix (#146), Power Automation boot-apply fix, diagnostics-export wiring fix (#145 evidence gap), Optimizer verification fix, fan-monitor-loop shutdown-race fix |
| 3.8.1 | Patch release: GitHub #141-#145 follow-up, fan-telemetry truthfulness, saved Custom curve fix, GPU OC startup-reapply clarity, OMEN-key field diagnostics, performance-profile relaunch persistence fix |
| 3.8.0 | Release candidate: field fixes, fan/RGB/tuning readiness diagnostics, UI responsiveness, model-specific validation |
| 3.7.1 | Quick Access profiles, WMI V1 fan recovery, profile-only fan gating, AMD ADL containment, launch diagnostics |
| 3.7.0 | Runtime recovery, fan/profile authority, OMEN Max identity, Linux diagnose improvements |
| 3.6.3 | Desktop fan-write safety, conservative WMI fan handoff, OSD startup hardening |
| 3.6.2 | Runtime source-of-truth hardening, RGB fallback reliability, Linux diagnostics |
| 3.6.1 | Fan/performance sync, tray/OSD consistency, WMI fan CPU reduction |
| 3.6.0 | Lightweight runtime, hardware-worker reliability, fan/RGB/hotkey hardening |
| 3.5.0 | Diagnostics clarity, safer tuning flow, conflict and recovery guardrails |

Older release notes live in [docs/](docs/).

## Contributing

Useful contributions include fresh diagnostic exports, model ProductId verification, EC/WMI behavior reports, Linux sysfs snapshots, translations, and focused bug fixes. Please include logs and the model identity summary when filing hardware-control issues.

### Requesting Support For An Unrecognized Model

If Diagnostics reports `Unknown <Family> Model` or "Resolution source: Family fallback / Low confidence" (as opposed to an exact ProductId match), your laptop works through conservative generic defaults rather than a model-specific profile. To get it added:

1. Open **Diagnostics** (or **About**) and copy the **Model Identity Summary** in full, including `Capability ProductId`, `Baseboard ProductId`, `WMI model`, `System SKU` / HP support product number, and the keyboard identity lines.
2. Note your CPU, GPU, and BIOS version, and whether fan control, Battery Care, RGB, and performance-mode persistence work or fail individually — a feature that already works via family fallback should stay marked as working so the new profile does not become more restrictive than what you have today.
3. Open a [GitHub issue](https://github.com/theantipopau/omencore/issues) with that summary, your symptoms, and (if relevant) what OMEN Gaming Hub shows for the same feature.
4. Exact identity entries always start conservative: WMI fan/profile control only where evidence already shows it working, with direct EC writes, MUX switching, undervolt, and RGB left unclaimed until a tester confirms the surface exists. Capabilities are widened in a follow-up once that evidence arrives — see [docs/3.8.1-BUG-REPORTS.md](docs/3.8.1-BUG-REPORTS.md) for examples of this pattern (`8D40`, `8A18`, `8E9A`).

## Safety And Disclaimer

OmenCore is provided as-is. Fan control, EC writes, undervolting, GPU power changes, and MUX switching can affect stability and hardware behavior. Use restore points, read capability warnings, and avoid enabling unverified hardware restore paths unless you understand the recovery steps.

OmenCore is not made by or endorsed by HP.

## Links

- GitHub: https://github.com/theantipopau/omencore
- Releases: https://github.com/theantipopau/omencore/releases/latest
- Issues: https://github.com/theantipopau/omencore/issues
- Discord: https://discord.gg/9WhJdabGk8
- Donate: https://www.paypal.com/donate/?business=XH8CKYF8T7EBU

## License

MIT License. See [LICENSE](LICENSE).

Third-party components include LibreHardwareMonitor, Hardcodet.NotifyIcon.Wpf, PawnIO, and vendor RGB SDK files where bundled. See the relevant source folders and driver documentation for details.
