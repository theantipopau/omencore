# OmenCore v4.3.0

**Release Date:** TBD — rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-08-30, immediately after v4.2.0 shipped.
**Type:** Feature release. Started as a v4.2.1 patch cycle (field-report fixes from GitHub issues
opened after v4.2.0, #178–#182) that grew into the `OmenCore.Core` extraction and the first slice
of a Windows CLI — folded together into one v4.3.0 release rather than shipping a separate patch
first.
**Base Version:** v4.2.0
**Tracking doc:** `docs/ROADMAP_v4.3.0.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

---

## Fixed

### Linux: GPU Telemetry Never Queried NVML, So a Real NVIDIA GPU Read as 0°C/Unavailable

**Report:** [#186](https://github.com/theantipopau/omencore/issues/186) — OMEN Max 16-ah0xxx (board `8D41`, RTX 5080). `omencore-cli status` reported `GPU Temperature: 0°C` / `GPU Telemetry: unavailable` and the GUI showed `0°`, `0% usage`, `Power: 0 W`, with the adapter shown by raw PCI ID (`NVIDIA GPU (0x2c59)`) — while `nvidia-smi` read the exact same GPU correctly (41°C, 24W) in the same second, unprivileged, no root needed.

Traced to a real gap, not a driver/permissions problem as the reporter already suspected: OmenCore's Linux GPU telemetry only ever checked `hwmon` (`/sys/class/hwmon/*/name == "nvidia"`, which the proprietary NVIDIA driver typically doesn't register — unlike `amdgpu`/`nouveau`) and the OMEN EC's GPU thermal register. NVML — the library `nvidia-smi` itself is built on, and the correct way to read an NVIDIA GPU on Linux regardless of hwmon exposure — was never referenced anywhere in the codebase.

Added `NvmlInterop` (`OmenCore.Linux/Hardware/NvmlInterop.cs`), a P/Invoke wrapper around `libnvidia-ml.so.1` with a custom `NativeLibrary` resolver (systems with only the runtime driver package installed, no `-dev` symlink, often only have the versioned `.so.1`, which .NET's default Linux library probing doesn't try). Wired in as the first-priority source in `LinuxTelemetryResolver.GetGpuTemperature` — ahead of hwmon and EC — since it's authoritative for NVIDIA GPUs. Also fixed **`MonitorCommand`**, which turned out to have its own third, independent `hwmon.GetGpuTemperature() ?? ec.GetGpuTemperature()` chain bypassing the shared resolver entirely (same underlying bug, a second time); it now routes through `LinuxTelemetryResolver` like `StatusCommand`/`DiagnoseCommand` already did. `status`/`monitor` now also show GPU name, power draw, and utilization from NVML. Per the reporter's own suggestion, `diagnose`'s Notes now surface *why* GPU telemetry is unavailable (NVML load/init failure reason, not just "unavailable") when the whole fallback chain is exhausted.

3 new tests (`NvmlInteropTests.cs`) confirm NVML absence fails closed (null, not a thrown exception) with a diagnosable reason — the one thing verifiable without real NVIDIA hardware. Linux suite: 28/28 (up from 25/25).

**GUI (`OmenCore.Avalonia`) parity landed in the same cycle, as a follow-up commit.** `LinuxHardwareService.cs` turned out to be a *third*, fully independent GPU-telemetry implementation — its own hwmon-only temperature read, and its own `0x2c59`-style raw-PCI-ID name fallback (`FormatGpuName`), completely separate from the CLI's `LinuxTelemetryResolver`. `GetStatusAsync` now tries `NvmlInterop.TryGetPrimaryGpu()` first for temperature (falling through to the existing hwmon read unchanged when NVML isn't available), and populates `GpuUsage`/`PowerConsumption` from it — both fields already existed on the `HardwareStatus` DTO and were already bound in `DashboardViewModel`, but were dead on real Linux hardware (only ever populated in the Windows-side mock data path), which is exactly the `0% usage`/`Power: 0 W` the report showed. `ReadGpuNameAsync` now prefers NVML's actual product name over the PCI-ID fallback. `HasStatusChanged` (the gate for the `StatusChanged` event the dashboard's live updates depend on) now also compares `GpuUsage`/`PowerConsumption` — previously moot since both were always 0, now a real staleness gap now that they carry live data. No test project exists for `OmenCore.Avalonia` at all (none did before this either) — verified via a clean build only.

**Not a field-validation item** in the evidence-gate sense — this is a read-only telemetry addition, no fan/EC/thermal/OC/UV write path touched. It is, however, genuinely unverifiable from this environment (no Linux machine with an NVIDIA GPU to run it against) — the P/Invoke bindings match NVML's documented, ABI-stable public API, and the CLI build targets (`win-x64`, cross-compiled `linux-x64`) compile clean, but an actual run against real hardware is the real verification still needed.

### Model Capability Fallbacks Were Optimistic Instead of Conservative

Traced from [#182](https://github.com/theantipopau/omencore/issues/182) (board `8603`, a 2019 OMEN 17-cb0xxx): the Model Capabilities screen showed GPU Power Boost, custom fan curves, independent fan curves, and 4-zone RGB all as "Supported" on a board OmenCore had never seen before — and the reporter's own independent OmenMon probe confirmed the underlying BIOS `GetGpuPower()` call fails outright on that hardware.

Root cause: the two fallback paths used when a board has no exact ProductId match — `DefaultCapabilities` (nothing matched at all) and `GetCapabilitiesByFamily` (family matched, nothing more specific) — defaulted to claiming most advanced, write-capable features as supported. `GetCapabilitiesByFamily` specifically cloned *whichever board happened to be first in the dictionary* for that family as a template, so an unrelated, unverified board silently inherited that board's entire capability set. Same class of bug `SupportsEcPowerLimits` was already fixed for once (GitHub #159) — an unconfirmed write path attempted by default — just never generalized to the other optimistic flags.

Both fallbacks now only assume the one thing genuinely safe across any HP OMEN/Victus laptop: WMI BIOS fan-mode switching and OEM performance profiles. Everything with its own write path (fan curves, EC fan control, GPU Power Boost, undervolt, TCC offset, direct power limits, 4-zone/per-key RGB, MUX switch) defaults to false until a real board entry confirms it — matching how every named entry in the database already behaves. 2 new regression tests assert the conservative defaults directly, including one that checks every `OmenModelFamily` value.

### Quiet Safety Monitor Could Silently Switch Performance Mode When Fan/Performance Linking Was On

Reported on [#181](https://github.com/theantipopau/omencore/issues/181) (OMEN Max 16 ah0xxx, RTX 5090): "some transient CPU spikes made OmenCore enable max fan mode, which made the laptop's fans deafening and inherently enabled Performance completely disregarding the fact it was on Quiet earlier."

Traced to a real interaction bug. The Quiet Safety Monitor (on by default, triggers at 90°C) calls `FanService.ApplyMaxCooling()` specifically to keep the user on their chosen power profile while forcing fans to Max — its own log message says "Quiet power mode retained." But `ApplyMaxCooling()` unconditionally raises the same `PresetApplied` event a normal user-initiated Max Fan click would, and `MainViewModel.OnFanPresetApplied` treats every `PresetApplied` event as fan-mode-changed-by-something, cascading into a performance-mode switch via `FanPerformanceLinkMapper` whenever Fan/Performance linking is enabled — completely undoing the "power mode retained" guarantee for anyone using that combination.

Fixed by giving `FanService.PresetApplied` a real payload (`FanPresetAppliedEventArgs`, replacing the old plain `string`) carrying a `SuppressLinkedProfileSync` flag, and threading a `suppressLinkedProfileSync` parameter through `ApplyMaxCooling(...)` down to that event. The Quiet Safety Monitor is now the one caller that passes `suppressLinkedProfileSync: true`; every user-initiated Max Fan trigger (button, hotkey, OMEN key) still passes the default `false` and keeps cascading into the linked performance-mode switch exactly as before. `MainViewModel.OnFanPresetApplied` checks the flag before running the link-sync block — every other consumer (tray icon, sidebar, dashboard) is unaffected and still correctly shows Max fan state regardless. 2 new tests confirming the flag reaches `PresetApplied` correctly in both the suppressed and default cases, plus 3 existing tests updated for the new event-argument type (2 call-site updates, 1 reflection-based test that needed both parameters passed explicitly since `MethodInfo.Invoke` doesn't apply C# default-parameter values). Full suite: 1376/1376.

### Corsair DPI Editor Could Show "Success" for a Write That Never Reached the Mouse

Found while working the roadmap's "decide and be honest" item on Corsair's RGB.NET-backed provider (`CorsairICueSdk`). Two related honesty gaps:

`DiscoverDevicesAsync`/`GetDeviceStatusAsync` hardcoded `BatteryPercent = 100` for every Corsair device with the comment "RGB.NET doesn't expose battery info" — a fabricated full-charge reading, not a placeholder. `CorsairDeviceStatus`'s own display logic already only shows a battery line `if (BatteryPercent > 0)`, so the fix is just using that existing convention honestly: `BatteryPercent = 0` now correctly shows nothing instead of a fake 100%.

The bigger issue: `ApplyDpiStagesAsync` presents a real confirmation dialog — "This will change the hardware DPI settings on the selected device" — then, on this backend, logs "DPI configuration not supported via RGB.NET" and returns without writing anything. Nothing in the call chain checked for that: the ViewModel updated the device model, saved config defaults, and updated the saved DPI profile as if the write had succeeded, with no failure ever surfaced to the user. `ApplyDpiStagesAsync` now returns `Task<bool>` end to end (interface → `CorsairSdkStub`/`CorsairICueSdk`/`CorsairHidDirect` → `CorsairDeviceService` → both ViewModel call sites), matching the same "return true only if the write actually reached the device" contract `ApplyLightingAsync` already used. A failed apply now shows an explicit "DPI Settings Not Applied" dialog instead of silently updating state. 3 new/strengthened tests (`CorsairRgbProviderTests`, `CorsairHidDpiIntegrationTests`) plus 2 test fakes updated for the new signature.

Corsair macro upload has the identical "not supported, no-op" shape on every backend including the real HID-direct one, but is never called from any ViewModel — no UI action reaches it, so it's dead code rather than a live honesty bug. Left alone; noted in the roadmap as needing an actual implementation decision, not a quick fix.

One incidental fix along the way: the `ApplyDpiStagesAsync` signature change shifted two pre-existing, already-tracked bare `catch {}` blocks in `CorsairHidDirect.cs` down by two lines, which the code-hygiene gate's line-pinned baseline correctly flagged as "new" violations. Updated the baseline's line numbers with the same shift-tracking convention already used elsewhere in that file (`ReleaseGateCodeHygieneTests.cs`) rather than suppressing the check. Full suite: 1377/1377.

### Linux: Switching Max → Auto Under Load Could Leave Both Fans Dead and Cause a Thermal Shutdown

**High-severity safety fix.** [#183](https://github.com/theantipopau/omencore/issues/183) (OMEN MAX 16-ak0xxx, board `8D87`, Ryzen AI 9 HX 375 + RTX 5080): switching the fan profile from `max` to `auto` via `omencore-cli fan --profile auto` while under a full gaming load left both fans at 0 RPM indefinitely — they never resumed as temperatures kept climbing — and the laptop thermally shut down shortly after.

Traced to `LinuxEcController.SetFanProfileViaAcpiHwmon`: on this board, `pwm_enable=2` ("firmware auto") is a policy flag telling the firmware to take over, not a guarantee it actually does — degraded ACPI on this board (kernel logs showed `WMAA`/`WHCM`/`WQB*` method aborts) let the write report success while the firmware's own fan-curve handler never resumed driving `pwm1` upward. Nothing else was watching: `omencore-cli` is a one-shot command, not a monitored daemon.

Fixed by polling fan RPM for a few seconds after every Auto-mode write on this code path; if both fans are still at 0 while CPU or GPU temperature is above a conservative 85°C safety bar, automatically falls back to Max mode (the exact write path the reporter confirmed reaches ~6000 RPM reliably on this board) instead of reporting Auto as applied. `RestoreAutoMode()` — also called directly by the fan-curve daemon (`Daemon/FanCurveEngine.cs`) — was refactored to route through the same fixed code path instead of duplicating the unprotected writes, so the daemon gets the same protection. No new/unverified write path was introduced — the fallback only ever reuses a mode already proven to work on this exact board.

### Windows: OSD Toggle-Hotkey Cleanup Could Throw a Null-Reference During Shutdown

Found while auditing a diagnostics bundle for GitHub #184: `[WARN] OSD: Hotkey cleanup encountered an error: Value cannot be null. (Parameter 'window')` appeared during shutdown, right before the app finished exiting.

`OsdService.UnregisterToggleHotkey()` re-derived the window handle via `new WindowInteropHelper(Application.Current.MainWindow)` — `Application.Current.MainWindow` can already be null by the time this cleanup runs, and `WindowInteropHelper`'s constructor throws exactly this exception when passed null. The fix uses `_hotkeySource.Handle` instead — `HwndSource.Handle` is guaranteed to be the same hwnd the hotkey was originally registered against (`RegisterHotkeyWithHandle`), so this is strictly more correct as well as null-safe. Caught and logged rather than crashing either way, so this was silent/cosmetic in practice, not a functional bug — fixed anyway since the correct fix was small and unambiguous once traced.

### In-Process Telemetry Fallback Could Crash the Whole App on Hybrid AMD+NVIDIA Hardware

**High-severity stability fix**, found via a recurring test-suite crash (3 of 4 full runs this session), not a field report. Full test runs kept aborting with an unrecoverable `System.AccessViolationException` in `LibreHardwareMonitor.Hardware.Gpu.AmdGpu.Update()` → `AtiAdlxx.ADL2_Adapter_DedicatedVRAMUsage_Get` — a native, corrupted-state exception no C# `catch` block can intercept, so it kills the whole process outright.

`OmenCore.HardwareWorker` (the out-of-process telemetry host) already quarantines exactly this: on startup, if both an AMD and an NVIDIA GPU are detected, it disables AMD ADL telemetry entirely rather than risk this exact crash. But `LibreHardwareMonitorImpl` — the in-process fallback `ThermalSensorProvider`/`FanService`/`HardwareMonitoringService` use when the out-of-process worker isn't available — had no equivalent protection on its own local `Computer` object. This isn't hypothetical: the #184 diagnostics bundle (a real hybrid AMD+NVIDIA laptop) shows `WmiBiosMonitor` switching to this exact "LHM Fallback" CPU-temperature authority repeatedly during normal use.

Added `QuarantineHybridAmdGpuTelemetryIfNeeded()` to `LibreHardwareMonitorImpl`, mirroring the worker's own detection (`hasAmdGpu && hasNvidiaGpu` → never call `.Update()` on the AMD GPU hardware object at all), applied at both call sites that dispatch GPU updates (`UpdateHardwareReadings`, `GetFanSpeeds`). CPU/fan/memory/storage telemetry and the NVIDIA GPU (a separate, already-hardened NVML path) are unaffected — only the specific AMD ADL call this instability traces to is skipped.

Full suite: 1380/1380, confirmed clean across repeated runs after the fix (pre-fix: crashed 3 of 4 full runs at this exact spot).

### Power Automation Silently Overwrote Manual Fan/Performance Selections on Every Startup

Re-diagnosed from v4.2.0's #177 triage ("custom fan curve not restored on restart"), traced to its exact cause this time: with Power Automation enabled, `MainViewModel` force-applied the configured AC/Battery preset on *every* startup, unconditionally — not just when the power source had actually changed. A user who manually picked a different preset mid-session, then closed and reopened the app on the same power source, had that manual choice silently discarded and replaced every time, by design rather than by bug.

Fixed by teaching `PowerAutomationService` to persist the AC/Battery state it last confirmed (`PowerAutomationSettings.LastKnownAcState`) and compare it against the freshly-detected state at the start of each session. The startup profile apply now only fires when the power source is known or suspected to have actually changed since the app last had control (including while it was closed) — otherwise it's a no-op, and the user's last manual selection (already restored earlier in the same startup sequence) stands. Settles the "who owns the active profile" question the roadmap had flagged as a precondition for adding more automation trigger types. New regression test simulates two sequential app sessions against the same config to pin the "unchanged power source → skip" behavior down; the two pre-existing tests (which always start from a fresh config with no prior baseline) needed no changes, since "no baseline yet" correctly still applies once. Full suite: 1381/1381.

### GPU Power Boost Card Always Showed "+15W" Even When Extended Was Selected, With No Ceiling Caveat

Follow-up to [#181](https://github.com/theantipopau/omencore/issues/181)'s non-linking half (see "Investigated, Not Yet Actioned" below): the "EXTRA POWER" badge on the GPU Power Boost card was a hardcoded `"+15W"` string regardless of which level was actually selected, so choosing Extended (documented elsewhere in the same file as "+25W or more") still showed the Maximum level's number. Now bound to a per-level `GpuPowerBoostWattageText` property (Minimum/base → `+0W`, Medium → `Custom`, Maximum → `+15W`, Extended → `+25W`), matching the level-aware wording a different summary property in the same ViewModel already used elsewhere. Also added a caveat callout on the card itself (matching the existing "Hardware Limitation" warning style used on the GPU Switching card) explaining that these are relative boost requests to shared firmware, not an absolute wattage guarantee, and suggesting a full OMEN Gaming Hub close if the observed wattage doesn't match. Pure UI/display-honesty change — no hardware-write path touched, no field validation needed. Full suite unaffected (no logic under test changed, only bound display text).

### RGB Page's "Control Ownership" Card Could Show "Confirmed" With No Real Keyboard Backend

Found by actually driving the app and looking at the Lighting page (a live-machine look, not just a code read) — on a desktop PC with no HP hardware at all, the "Control ownership" card showed "HP Keyboard (None)" as its summary text, right next to a green "Confirmed" ownership badge, and the "OMEN Keyboard" status chip at the top of the page was highlighted as if active.

Traced to `KeyboardLightingService.IsAvailable` including `_ecAvailable` unconditionally, while `BackendType` (which correctly produced "None" in this exact case) only ever reports an EC backend when the user has explicitly opted into the experimental EC keyboard-write path (`IsExperimentalEcEnabled`). `_ecAvailable` only means "we can talk to *an* embedded controller via PawnIO" — true on almost any modern PC for basic power management, regardless of whether it's an HP OMEN keyboard-controlling EC. `IsAvailable` now applies the same experimental-opt-in gate `BackendType` already used, so the two agree. 3 new tests (`KeyboardLightingServiceAvailabilityTests.cs`, using the uninitialized-object + reflection pattern already established elsewhere in this test project, since the class's constructor needs real hardware access objects). Full suite: 1380/1380.

### Keyboard RGB "Did Not Verify" Status Didn't Surface Its Own Fix Suggestion

Traced from a Discord report ("keyboard rgb aint changing") plus its attached diagnostics: on this board (HP OMEN 16-wd0xxx, `8BA9`), the WMI ColorTable keyboard-lighting write is accepted but its color readback never verifies, and `KeyboardLightingService`'s telemetry shows 0% WMI success across the whole session. The code already had the right troubleshooting suggestion — "Try enabling 'Experimental EC Keyboard' in Settings if RGB doesn't change" — but it was only ever written to the log file, never to the same status text the Lighting page already shows the user (`KeyboardRestoreStatusText`, which said only "...did not verify..." with no next step). `LightingViewModel.ApplyKeyboardColorsAsync` now appends the hint to that visible status text whenever WMI has zero verified successes, instead of leaving it log-only. Pure UI/display fix — no lighting write path changed.

### RGB Page's "Control Ownership" Card Could Show "Confirmed" With No Real Keyboard Backend

Found by actually driving the app and looking at the Lighting page (a live-machine look, not just a code read) — on a desktop PC with no HP hardware at all, the "Control ownership" card showed "HP Keyboard (None)" as its summary text, right next to a green "Confirmed" ownership badge, and the "OMEN Keyboard" status chip at the top of the page was highlighted as if active.

Traced to `KeyboardLightingService.IsAvailable` including `_ecAvailable` unconditionally, while `BackendType` (which correctly produced "None" in this exact case) only ever reports an EC backend when the user has explicitly opted into the experimental EC keyboard-write path (`IsExperimentalEcEnabled`). `_ecAvailable` only means "we can talk to *an* embedded controller via PawnIO" — true on almost any modern PC for basic power management, regardless of whether it's an HP OMEN keyboard-controlling EC. `IsAvailable` now applies the same experimental-opt-in gate `BackendType` already used, so the two agree. 3 new tests (`KeyboardLightingServiceAvailabilityTests.cs`, using the uninitialized-object + reflection pattern already established elsewhere in this test project, since the class's constructor needs real hardware access objects). Full suite: 1380/1380.

---

## Added

### Temperature, Idle, Process, and WiFi Network Triggers for Automation Rules

Discovered mid-cycle while scoping "extend `PowerAutomationService` with time-of-day/lid-close/charger-connect triggers" (roadmap "Not Yet Started"): that framing was stale. A separate, more general rule engine (`AutomationService` + a real "Automation Rules" editor in Settings) already ships time-window and AC-power triggers today — the roadmap entry didn't know it existed. `AutomationService`'s backend has actually supported **seven** trigger types since v2.3.0 (Time, Battery, ACPower, Temperature, Process, Idle, WiFiSSID), but the UI/validator only ever exposed three (`AutomationRuleSchemaValidator.SupportedTriggerTypes`) — the other four were implemented and functional but deliberately held back as "not shipped yet," with no comment on record explaining which ones were actually safe to promote.

Reviewed all four gated types before touching anything: **Temperature** (CPU/GPU threshold, e.g. "above 85°C") is complete and correct — same `ThermalSensorProvider` reading already used elsewhere, no gaps found. **Idle** (system idle minutes via `GetLastInputInfo`) is also correct — same TickCount64-wraparound-safe pattern already used elsewhere in the codebase, self-contained, no external dependency issues. **WiFiSSID** has a real, confirmed bug — its fallback path (when the primary WMI SSID query fails) doesn't actually match the configured SSID at all, just checks whether *any* wireless interface is up; promoting it as-is would ship a rule that silently doesn't do what its name says. **Process** has a different real, confirmed bug: its trigger checks `ProcessMonitoringService.ActiveProcesses`, which only ever contains processes explicitly registered via `TrackProcess()` — and that's only ever called from `GameProfileService` for configured Game Profiles. A Process-trigger rule for any executable that isn't *also* a configured Game Profile would silently never fire.

Promoted **Temperature and Idle** first: added both to `SupportedTriggerTypes`, validation cases (Temperature: threshold 1-110°C, condition Above/Below, sensor optional; Idle: threshold 1-999 minutes), and matching UI fields in the Settings → Automation Rules editor, mirroring the existing Battery trigger's fields exactly. Not a field-validation item — both only expose already-shipped, already-tested read+action paths (`ThermalSensorProvider.ReadTemperatures`/`GetLastInputInfo` → `FanService.ApplyPreset`/`PerformanceModeService.SetPerformanceMode`) to new UI triggers, no new hardware I/O.

**Process and WiFiSSID promoted as a same-cycle follow-up**, once each one's real bug was actually fixed rather than promoted as-is:

- **WiFiSSID's** primary lookup (the `"root\WlanApi"` WMI namespace, largely obsolete since Vista/7) and its own fallback (which didn't check the SSID at all — just whether *any* wireless interface was up, meaning a rule for "Home-WiFi" could fire on any network) are both replaced by a new `OmenCore.Utils.WlanSsidHelper`, a P/Invoke wrapper around `wlanapi.dll`'s Native Wifi API (`WlanQueryInterface` with `wlan_intf_opcode_current_connection`) — the same API the Windows network flyout itself is built on, and reliably present since it's backed by the WLAN AutoConfig service every Windows 10/11 install ships. Fails closed (`false`, never throws) when no interface is connected or the service isn't available.
- **Process's** trigger reads `ProcessMonitoringService.ActiveProcesses`, which only ever contains executables registered via `TrackProcess()` — previously called only by `GameProfileService`, for configured Game Profiles. `AutomationService.EvaluateRules` now also calls `TrackProcess()` for every enabled Process-trigger rule's executable name each evaluation tick (via a new pure, unit-tested `GetProcessTriggerExecutableNames` helper) — `TrackProcess()` is an idempotent set-add and this never un-tracks, so it can't undo tracking that `GameProfileService` still relies on. A Process-trigger rule for any executable — not just one that also happens to be a configured Game Profile — now actually evaluates.

All 7 backend trigger types (Time, Battery, ACPower, Temperature, Process, Idle, WiFiSSID) are shipped as of this pass. New/updated tests: 6 in `AutomationRuleSchemaValidatorTests.cs` (Process/WiFiSSID acceptance + required-field rejection) plus a regression guard for `GetProcessTriggerExecutableNames` (dedup, enabled-only, correct trigger type). Not a field-validation item — the trigger evaluation itself reuses already-shipped action paths; the fixes are read-path correctness (a real SSID query instead of a broken one, and registering the right processes to track), not a new hardware write.

### Lid-Close Automation Trigger

Completes the `ROADMAP_v2.5.0.md` §7 ask (time-of-day / lid-close / charger-connect) — the first two already shipped via `AutomationService` (see above); this adds the third, genuinely-missing one: a new `TriggerType.LidState`.

Lid state has no poll-on-demand Win32 API the way AC/battery does — Windows only pushes lid transitions, via `WM_POWERBROADCAST` to a real window's message queue. New `OmenCore.Utils.LidSwitchMonitor` runs a tiny, invisible message-only window on its own dedicated background thread (no WPF dependency, staying consistent with the Core/App split from earlier this cycle), registers for `GUID_LIDSWITCH_STATE_CHANGE`, and caches the latest lid state for `AutomationService`'s regular poll to read — fails closed (rule never fires) until a real notification arrives or on a desktop with no lid at all. New "Lid state" field in Settings → Automation Rules (Closed/Open). 7 new tests: 4 for the pure broadcast-interpretation logic, 3 for the validator. Full suite: 1407/1407.

Not verified against real hardware (no physical lid to test in this environment) — but unlike a fan/EC write, the failure mode here is strictly "the rule doesn't fire," never a hardware-unsafe state, so this ships as implemented-pending-confirmation rather than held back.

### Three New Model Database Entries From Field Reports

- **[#178](https://github.com/theantipopau/omencore/issues/178)** — HP Victus 15-fa2303TX (C2JQ3PA), board `8E5E`. Added using the reporter's own fan-verification diagnostic (WMI fan-level control responds, but RPM readback is level-estimated rather than a real tachometer — reflected as `SupportsRpmReadback = false` rather than claiming a number this board hasn't demonstrated). Single-zone, static-color-only keyboard backlight per the reporter.
- **[#182](https://github.com/theantipopau/omencore/issues/182)** — HP OMEN 17-cb0xxx (i9-9880H + RTX 2080), board `8603`. Gives this board a fixed database entry instead of depending on the now-fixed-but-still-generic family fallback.
- **Discord (GHOST), 2026-09-02** — HP OMEN 16-wd0xxx (i7-13620H + RTX 4060), board `8BA9`. Was resolving only as "Unknown OMEN16 Model" via family fallback; this entry gives it a named identity with the same conservative flags the live capability probe already granted it every session (not a capability change).

All three entries are conservative and unverified pending further field confirmation, consistent with every other database addition this cycle.

### `OmenCore.Core` — a standalone class library for the hardware/service layer

`OmenCoreApp.csproj` was `<UseWPF>true</UseWPF>` with the entire service and hardware layer
compiled directly into the WPF application assembly. That blocked three separate wishlist items
(a Windows CLI, a local HTTP/named-pipe control API, any future headless/service-mode operation)
because none of them could reference the logic without either dragging WPF into a console app or
duplicating it.

Moved `Models/`, `Hardware/`, and nearly all of `Services/` (194 files total) into a new
`src/OmenCore.Core/OmenCore.Core.csproj` — no `UseWPF`, targets the same
`net8.0-windows10.0.19041.0` as the app (needed for a WinRT battery-status fallback, unrelated to
WPF). `OmenCoreApp` now references it as a `ProjectReference`; everything that isn't genuinely
WPF/window-specific kept working with zero call-site changes, since C# namespaces don't care which
assembly a file physically lives in.

Nine files stayed behind because they're real WPF/window couplings, not just leftover imports:
`ToastNotificationService.cs` (WPF toast UI), `OsdService.cs` (WPF overlay window),
`HotkeyService.cs` + `RuntimeHotkeyCoordinator.cs` (need a real `HwndSource`/Dispatcher to
register and dispatch global hotkeys), `CurveRecoveryService.cs` (pops a `MessageBox` directly —
a service owning UI, flagged as a smell but not fixed here), `MacroService.cs` (its
`MacroAction.Key` property used the WPF `Key` enum — confirmed, not assumed, to have zero live
callers before deciding this was safe to just retype as a raw `int` instead of chasing it further),
and `DiagnosticExportService.cs` + `ModelReportService.cs` + `ModelIdentityResolutionSummary.cs`
(the diagnostics-export/reporting layer takes a live `HotkeyService` dependency to report its
state, so it stays with it).

A handful of files turned out to have a hidden WPF/WinForms coupling that a first pass wouldn't
catch (`Application.Current?.Dispatcher`, `System.Windows.Forms.SystemInformation.PowerStatus`,
a bare `App.Logging`/`App.Current` reference relying on C# nested-namespace lookup) — see
`docs/ROADMAP_v4.3.0.md` for the two new abstractions (`UiThreadMarshaller`, `PowerStatusHelper`)
and the `AppHost` singleton relocation that resolved them, all in Core, all wired back to the real
WPF behavior from `OmenCoreApp.App`'s constructor.

Full suite: 1380/1380, unchanged from before the move — this was a structural extraction, not a
behavior change, and the tests back that up.

### Windows CLI — `status` / `fan` / `performance` / `keyboard` / `monitor` / `config` / `daemon`

New `omencore-cli` console app (`src/OmenCore.Cli`), built directly on `OmenCore.Core` — no
duplicated hardware logic. `status [--json]` reports model/board ID, EC and fan-controller
availability, live fan RPM/duty, and current performance mode. `fan --profile <name>` / `--status`
and `performance --mode <name>` / `--status` apply presets from the same config the GUI's preset
buttons use, through the same `FanService.ApplyPreset`/`PerformanceModeService.Apply` calls the
GUI makes — a new caller of already-shipped code, not new hardware-write behavior. `keyboard
--color <hex>` / `--status` applies a static color across the whole keyboard via the same
`KeyboardLightingService.ApplyEffect` the GUI's lighting controls use. `monitor [--interval ms]`
redraws live CPU/GPU temperature and fan RPM/duty in place until Ctrl+C, matching the Linux CLI's
`monitor` command in shape. `config --show` / `--get <key>` / `--set key=value` reads/writes a
curated subset of settings (polling interval, log level, diagnostics/telemetry opt-in,
fan/performance linking, Quiet Safety Monitor threshold) — not all of `AppConfig`, which has 60+
top-level properties; scoping the full thing wasn't attempted. `daemon --profile <name>` runs a
fan preset in the foreground with `FanService.Start()`'s continuous monitor loop actually running
(curve/hold support), until Ctrl+C; `daemon --status` checks whether the GUI app is already
running before you start it (both would fight for fan ownership). Foreground-only — no
Windows Service or Scheduled Task self-installation, unlike Linux's systemd-managed `daemon`;
that's a real, separate deployment decision, not something to default into.

Command parsing verified end-to-end (root and every subcommand's `--help` renders correctly).
`config --show`/`--get` and `daemon --status` were additionally run for real (neither touches
hardware, so both were safe to actually run) — `config --show`/`--get` against the real
`%APPDATA%\OmenCore\config.json`, and `daemon --status` correctly detected the GUI app wasn't
running and listed the real configured presets. `config --set` was not run for real, to avoid
mutating that live file from an unsupervised test; its logic mirrors the same tested pattern the
other commands' setters already use. **`status`/`fan`/`performance`/`keyboard`/`monitor`/
`daemon --profile` not yet verified against real hardware** — that needs an actual elevated run,
which is next. See `docs/ROADMAP_v4.3.0.md` for the full bootstrap trace and what's deliberately
out of scope.

### Package-Reference Cleanup on `OmenCoreApp.csproj`

Removed nine packages (`CUE.NET`, `HidSharp`, `LibreHardwareMonitorLib`, `NAudio`,
`NvAPIWrapper.Net`, `RGB.NET.Core`, `RGB.NET.Devices.Corsair`, `System.Management`,
`System.ServiceProcess.ServiceController`) now reached only transitively via the `OmenCore.Core`
project reference. Full solution build and test suite (1380/1380) confirmed clean.

---

## Investigated, Not Yet Actioned

- **[#179](https://github.com/theantipopau/omencore/issues/179)** — Linux per-key RGB for OMEN MAX 16-ak0xxx (board `8D87`) via direct HID (`0D62:54BF`, interface 3). Excellent, detailed field data — a real feature addition (new Linux HID backend), not a quick fix. Scoped for a future pass.
- **[#180](https://github.com/theantipopau/omencore/issues/180)** — "Doesn't start with Windows, config not saving." One sentence, no diagnostics, no repro steps. Needs a diagnostics export or repro steps before it's actionable.
- **[#181](https://github.com/theantipopau/omencore/issues/181)** GPU Power Boost wattage — architectural, not a code bug: OmenCore and OGH both send relative *boost steps* to the firmware, not absolute wattages (already documented in code as "+15-25W depending on model"), so the actual ceiling is firmware-determined and can be influenced by whatever OGH last configured. The UI-clarity half of this is now fixed (see "Fixed" above); still needs the reporter to test with OGH fully closed to isolate the wattage-ceiling question itself further.
- **PR [#176](https://github.com/theantipopau/omencore/pull/176)** — re-reviewed 2026-08-30. The process-monitoring fix from 2026-08-29 is real and correct, but two bugs from the 2026-08-19 review (keyboard "effect-freeze," iGPU Curve Optimizer gating) are **still broken**, with the keyboard bug relocated a second time. Branch is also now stale against `main`. Recommend against merging as-is; decision still pending owner call.
- The local HTTP/named-pipe control API — unblocked by the Core extraction, not started.
- Class-level capability defaults audit (the ~150 named board entries in `ModelCapabilityDatabase.cs` haven't been checked for silent reliance on the class-level `= true` defaults) — see `docs/ROADMAP_v4.3.0.md`.
- **Discord (GHOST), 2026-09-02** — "OMEN key also not working" on board `8BA9`. The attached `LastOmenKeyCandidate` (`vk=0xFF, scan=0x002B, rejected, reason=strict-mode-oem-omen-scan-mismatch, ageMs=369800`) is **not evidence of a bug** — this exact `(VK, scan)` pair was already diagnosed as a real brightness-key/OMEN-key collision on a different OMEN 16 board (GitHub #141) and is deliberately rejected by design, pinned by an existing regression test (`OmenKeyServiceTests.VkOemOmen_WithBrightnessDownScanCode_IsRejectedInStrictMode`). The 6-minute-old timestamp means this was very likely captured from an earlier brightness-key press, not a fresh physical OMEN-key test. Needs a clean re-test (press the physical OMEN key once, export diagnostics immediately after) before this is actionable — see `docs/ROADMAP_v4.3.0.md`.
- **Discord (PRIMUS_626), 2026-09-02** — "can't do anything is Tuning" on HP Victus 16-e0xxx (board `88ED`, matched via the existing #128 `88EC` name-pattern entry). Not a bug: `SupportsUndervolt=false` on this board is an intentional, already-documented conservative default pending field verification (same discipline as every other Victus/OMEN entry this cycle) — GPU OC via NVAPI should still be usable (`Supports OC: True` in every session log in the bundle). See `docs/ROADMAP_v4.3.0.md`.
- **[#123](https://github.com/theantipopau/omencore/issues/123)** — GPU TGP capped at 80W on Linux for the OMEN Max 16-ah0xxx / RTX 5080 (board `8D41`; independently confirmed on a second BIOS/distro in [a follow-up comment](https://github.com/theantipopau/omencore/issues/123#issuecomment-5516731635)). Traced and confirmed **not actionable in OmenCore's own code** — see `docs/ROADMAP_v4.3.0.md` for the full trace.

---

*(Further entries added as work lands.)*
