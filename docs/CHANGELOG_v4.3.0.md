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

### RGB Page's "Control Ownership" Card Could Show "Confirmed" With No Real Keyboard Backend

Found by actually driving the app and looking at the Lighting page (a live-machine look, not just a code read) — on a desktop PC with no HP hardware at all, the "Control ownership" card showed "HP Keyboard (None)" as its summary text, right next to a green "Confirmed" ownership badge, and the "OMEN Keyboard" status chip at the top of the page was highlighted as if active.

Traced to `KeyboardLightingService.IsAvailable` including `_ecAvailable` unconditionally, while `BackendType` (which correctly produced "None" in this exact case) only ever reports an EC backend when the user has explicitly opted into the experimental EC keyboard-write path (`IsExperimentalEcEnabled`). `_ecAvailable` only means "we can talk to *an* embedded controller via PawnIO" — true on almost any modern PC for basic power management, regardless of whether it's an HP OMEN keyboard-controlling EC. `IsAvailable` now applies the same experimental-opt-in gate `BackendType` already used, so the two agree. 3 new tests (`KeyboardLightingServiceAvailabilityTests.cs`, using the uninitialized-object + reflection pattern already established elsewhere in this test project, since the class's constructor needs real hardware access objects). Full suite: 1380/1380.

---

## Added

### Two New Model Database Entries From Field Reports

- **[#178](https://github.com/theantipopau/omencore/issues/178)** — HP Victus 15-fa2303TX (C2JQ3PA), board `8E5E`. Added using the reporter's own fan-verification diagnostic (WMI fan-level control responds, but RPM readback is level-estimated rather than a real tachometer — reflected as `SupportsRpmReadback = false` rather than claiming a number this board hasn't demonstrated). Single-zone, static-color-only keyboard backlight per the reporter.
- **[#182](https://github.com/theantipopau/omencore/issues/182)** — HP OMEN 17-cb0xxx (i9-9880H + RTX 2080), board `8603`. Gives this board a fixed database entry instead of depending on the now-fixed-but-still-generic family fallback.

Both entries are conservative and unverified pending further field confirmation, consistent with every other database addition this cycle.

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

### Windows CLI — first slice (`status` / `fan` / `performance`)

New `omencore-cli` console app (`src/OmenCore.Cli`), built directly on `OmenCore.Core` — no
duplicated hardware logic. `status [--json]` reports model/board ID, EC and fan-controller
availability, live fan RPM/duty, and current performance mode. `fan --profile <name>` / `--status`
and `performance --mode <name>` / `--status` apply presets from the same config the GUI's preset
buttons use, through the same `FanService.ApplyPreset`/`PerformanceModeService.Apply` calls the
GUI makes — a new caller of already-shipped code, not new hardware-write behavior.

Command parsing verified end-to-end (root and all three subcommands' `--help` render correctly).
**Not yet verified against real hardware** — that needs an actual elevated run, which is next.
See `docs/ROADMAP_v4.3.0.md` for the full bootstrap trace and what's deliberately out of scope
(curve presets, keyboard, `monitor`, `config`, `daemon`).

---

## Investigated, Not Yet Actioned

- **[#179](https://github.com/theantipopau/omencore/issues/179)** — Linux per-key RGB for OMEN MAX 16-ak0xxx (board `8D87`) via direct HID (`0D62:54BF`, interface 3). Excellent, detailed field data — a real feature addition (new Linux HID backend), not a quick fix. Scoped for a future pass.
- **[#180](https://github.com/theantipopau/omencore/issues/180)** — "Doesn't start with Windows, config not saving." One sentence, no diagnostics, no repro steps. Needs a diagnostics export or repro steps before it's actionable.
- **[#181](https://github.com/theantipopau/omencore/issues/181)** GPU Power Boost wattage — architectural, not a code bug: OmenCore and OGH both send relative *boost steps* to the firmware, not absolute wattages (already documented in code as "+15-25W depending on model"), so the actual ceiling is firmware-determined and can be influenced by whatever OGH last configured. Needs the reporter to test with OGH fully closed to isolate further.
- **PR [#176](https://github.com/theantipopau/omencore/pull/176)** — re-reviewed 2026-08-30. The process-monitoring fix from 2026-08-29 is real and correct, but two bugs from the 2026-08-19 review (keyboard "effect-freeze," iGPU Curve Optimizer gating) are **still broken**, with the keyboard bug relocated a second time. Branch is also now stale against `main`. Recommend against merging as-is; decision still pending owner call.
- **Package-reference cleanup on `OmenCoreApp.csproj`** — several packages (CUE.NET, HidSharp, LibreHardwareMonitorLib, NAudio, NvAPIWrapper.Net, RGB.NET.*, System.Management, System.ServiceProcess.ServiceController) are now only needed transitively via the Core project reference. Redundant, not broken; deferred rather than risking a last-minute trim.
- Remaining Windows CLI commands (`keyboard`, `monitor`, `config`, `daemon`) and the local HTTP/named-pipe control API — both unblocked by the Core extraction, neither started.
- Class-level capability defaults audit (the ~150 named board entries in `ModelCapabilityDatabase.cs` haven't been checked for silent reliance on the class-level `= true` defaults) — see `docs/ROADMAP_v4.3.0.md`.

---

*(Further entries added as work lands.)*
