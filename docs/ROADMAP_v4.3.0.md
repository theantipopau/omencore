# OmenCore v4.3.0 Roadmap

**Status:** In progress. Opened 2026-08-30, the day v4.2.0 went live on GitHub.
**Base version:** v4.2.0
**Predecessor doc:** `docs/ROADMAP_v4.2.0.md` — carried the 4.1.7 → 4.2.0 cycle. That document is now historical record.

---

## Why This Cycle Exists

v4.2.0 shipped 2026-08-30. Within hours, five new GitHub issues came in (#178–#182) — a mix of
new-model support requests, a Linux keyboard-lighting field report, and two deeper
capability/behavior bugs. This cycle started as a patch release built directly from that batch,
following the same "read the actual report, verify against actual code, fix what's real"
discipline as every field-report pass in the 4.2.0 cycle.

One item turned out to be bigger than its originating report: triaging #182's "family fallback"
complaint led to a systemic capability-honesty bug affecting every unrecognized board, not just
that one — see below.

While scoping what else could reasonably land alongside those fixes, the audit surfaced
`OmenCore.Core` — extracting the service/hardware layer out of the WPF app — as the gating item
for a Windows CLI, a local HTTP/named-pipe API, and any future headless operation. Owner approved
starting it 2026-08-31 ("yep do it"), and it grew into real work the same session: the extraction
itself, then the first slice of the Windows CLI it unblocked. Given the scope that had grown
past "patch release," the owner decided to fold everything into one v4.3.0 release rather than
shipping a separate v4.2.1 patch first — this document (and `docs/CHANGELOG_v4.3.0.md`) now
carries the whole cycle; the two were merged from what were briefly separate v4.2.1/v4.3.0 docs.

---

## Done

### Model Capability Fallbacks Defaulted to Optimistic, Not Conservative

**Report:** [#182](https://github.com/theantipopau/omencore/issues/182) — HP OMEN 17-cb0xxx (i9-9880H, RTX 2080, BIOS AMI F.53), board `8603`. OmenCore resolved this as "Unknown OMEN17 Model" via family fallback, and the Model Capabilities screen showed Custom fan curves, Independent CPU/GPU fan curves, GPU Power Boost, 4-zone keyboard RGB, CPU undervolting, and Power limit adjustment **all as "Supported."** The reporter independently ran OmenMon (a completely separate, unrelated tool) and got `BIOS call failed: Command not available` at `OmenMon.Hardware.Bios.BiosCtl.GetGpuPower()` — direct, independent confirmation that GPU Power Boost does not actually work on this hardware, contradicting what OmenCore's own capability screen claimed.

**Root cause, traced in `ModelCapabilityDatabase.cs`:**

1. `GetCapabilitiesByFamily(family)` (line ~1970) — used when a board's ProductId isn't in the database but its WMI model name resolves to a known family — picked `_knownModels.Values.FirstOrDefault(m => m.Family == family)` as a "template," then cloned essentially all of that template's feature flags: `SupportsFanControlEc`, `SupportsFanCurves`, `SupportsIndependentFanCurves`, `HasMuxSwitch`, `SupportsGpuPowerBoost`, `HasFourZoneRgb`, `HasPerKeyRgb`, `SupportsUndervolt`. Whichever board happened to be enumerated first in the dictionary for that family decided what every *other*, completely unrelated, unverified board in that family claimed to support. For `OmenModelFamily.OMEN17`, that template happened to be a fully-featured board — so 8603 inherited capabilities it doesn't have.

2. `DefaultCapabilities` (line ~291) — the ultimate fallback when even the family can't be resolved — had the same problem baked in directly: `SupportsFanControlEc = true`, `SupportsFanCurves = true`, `SupportsIndependentFanCurves = true`, `SupportsGpuPowerBoost = true`, `HasFourZoneRgb = true`, all set explicitly in the object initializer. `SupportsUndervolt`, `SupportsTccOffset`, and `SupportsPowerLimits` weren't even explicitly set here, so they silently inherited the `ModelCapabilities` class's own property-level default of `true` for each.

3. Checking the class-level property defaults themselves (`ModelCapabilities`, line ~11) confirmed the root shape of the problem: `SupportsFanControlEc`, `SupportsFanCurves`, `SupportsIndependentFanCurves`, `SupportsGpuPowerBoost`, `HasFourZoneRgb`, `SupportsUndervolt`, `SupportsTccOffset`, and `SupportsPowerLimits` **all default to `true` at the class level** — meaning any code path that constructs a `ModelCapabilities` without explicitly setting one of these silently claims it's supported. This is the same failure shape `SupportsEcPowerLimits` was already fixed for once before (GitHub #159 — "every board that didn't explicitly opt out got this unconfirmed, higher-risk write path attempted by default"), documented in that property's own doc comment, but never generalized to the other flags with identical risk profiles.

**Considered and rejected:** flipping the class-level property defaults themselves to `false`. This would be the most thorough fix, but it has a much larger, harder-to-verify blast radius — auditing every one of the ~150 named board entries in this ~2000-line file to confirm none of them silently relies on inheriting `= true` without setting it explicitly is a large, risky undertaking on its own, and any entry that does would silently downgrade to "not supported" the moment the class default flipped. Deferred; flagged below as a possible future pass with its own dedicated audit.

**What shipped instead:** only the two *fallback* paths — `DefaultCapabilities` and `GetCapabilitiesByFamily` — now explicitly set conservative values for every write-capable or hardware-specific flag, while still inheriting the two things genuinely safe to assume for any HP OMEN/Victus laptop: `SupportsFanControlWmi` and `SupportsPerformanceModes` (plus `FanZoneCount`, a physical-layout convention rather than a write-gated feature claim). This exactly matches the existing, deliberate precedent already documented at the OMEN Slim 16 (`8D40`) entry: *"Reporter confirms core WMI fan/profile control already works via family fallback, so that much is shared"* — immediately followed by a warning not to assume MUX switch, GPU TGP range, undervolt, or keyboard RGB just because two boards share a family. This fix makes the two fallback *methods* actually follow the discipline every named board entry in the file already follows by hand.

**Tests:** 2 new tests in `ModelCapabilityDatabaseFallbackTests.cs` — `DefaultCapabilities_DoesNotClaimWriteCapableFeaturesAsSupported` and `GetCapabilitiesByFamily_DoesNotInheritWriteCapableFeaturesFromTemplateBoard` (the latter iterates every `OmenModelFamily` value). No existing test asserted the old optimistic behavior, so nothing needed updating — the only prior assertions were "must return non-null" and "WMI fan control must be true," both still true. Full suite: 1374/1374 (up from 1372).

**Not a field-validation item.** This tightens false-positive "supported" claims to correctly conservative ones — it can only prevent an unverified write path from being offered, never enable a new one. Same evidence-gate classification as every other capability-honesty fix this project has shipped.

---

### Quiet Safety Monitor Cascaded Into a Performance-Mode Switch When Linked

**Report:** [#181](https://github.com/theantipopau/omencore/issues/181) — OMEN Max 16 ah0xxx (`8D41`), Intel Core Ultra 9 275HX + RTX 5090. Among several observations in a long, well-researched report: *"OmenCore says the performance and fan modes are linked, and it has happened several times that some transient CPU spikes made OmenCore enable max fan mode, which made the laptop's fans deafening and inherently enabled Performance completely disregarding the fact it was on Quiet earlier... I've never had this behaviour happen using OGH."*

**Traced to a confirmed, reproducible-from-code interaction bug**, not a misreading:

1. `AppConfig.QuietSafety` (default `Enabled = true`, `SafetyOnTempC = 90.0`) arms a monitor whenever the user is on the Quiet profile. When temperature crosses 90°C, `MainViewModel.OnQuietSafetyOverrideActivated` fires, logging *"Temperature critical — switching to Max cooling (Quiet power mode retained)"* — the feature's explicit design intent is to force fans to Max **while leaving the power/performance profile alone**.
2. That handler calls `_fanService.ApplyMaxCooling()`. `FanService.ApplyMaxCooling()` (line ~2853), on a successful write, sets `_currentFanMode = "Max"` and calls `PublishPresetApplied("Max")` — raising the `FanService.PresetApplied` event, described in its own doc comment as firing for *"fan preset changes from FanService (e.g., power automation)"* — a deliberately broad net that doesn't distinguish a user click from an internal safety override.
3. `MainViewModel.OnFanPresetApplied` (the `PresetApplied` subscriber) unconditionally checks `IsFanPerformanceLinked`, and if true, calls `FanPerformanceLinkMapper.MapFanModeToPerformanceMode("Max")` — which returns `"Performance"` — and applies it, switching the user off Quiet entirely.

A modern high-TDP mobile CPU like the Ultra 9 275HX can legitimately hit 90°C+ on a normal transient boost spike (this is expected turbo behavior, not necessarily thermal danger), so this isn't a rare edge case for this hardware class — it's a plausible everyday trigger for anyone using Quiet + linking together, which explains why the reporter saw it "several times."

**Fix:** the Quiet Safety Monitor's cooling activation now applies Max cooling through a path that intentionally does not raise the link-sync cascade, while every other `PresetApplied` consumer (tray icon text, sidebar status, dashboard, quick popup) is untouched and still correctly reflects "Max" fan state. The performance profile now stays exactly where the user left it, matching the feature's own stated intent and matching OGH's observed behavior on the same hardware.

**Considered and rejected:** raising `SafetyOnTempC`'s default, or disabling linking automatically while Quiet Safety is armed. Both are behavior/threshold changes on real hardware and would need field validation before shipping — this fix instead makes an already-safety-gated internal mechanism stop leaking into a feature (linking) it was never supposed to interact with, which is a pure logic-correctness fix, not a new tuning decision.

**Implementation:** `FanService.PresetApplied` changed from `EventHandler<string>` to `EventHandler<FanPresetAppliedEventArgs>` — a new small immutable type carrying `PresetName` and `SuppressLinkedProfileSync`. Only one subscriber existed (`MainViewModel.OnFanPresetApplied`), so this was a safe, contained signature change rather than a risky wide-reaching one. `ApplyMaxCooling(bool forceApply = false, bool suppressLinkedProfileSync = false)` threads the new flag to both of its `PublishPresetApplied` call sites. `OnQuietSafetyOverrideActivated` now calls `_fanService.ApplyMaxCooling(suppressLinkedProfileSync: true)`; the two other `ApplyMaxCooling()` call sites in `MainViewModel` (the `Ctrl+Shift+M` hotkey and the OMEN key toggle) were deliberately left as normal user-initiated calls — a user explicitly asking for Max Fan while linked should still cascade to Performance, only the safety monitor's implicit override should not.

**Tests:** 2 new tests in `FanPresetVerificationTests.cs` (`ApplyMaxCooling_SuppressLinkedProfileSync_FlowsThroughToEventArgs`, `ApplyMaxCooling_DefaultCall_DoesNotSuppressLinkedProfileSync`) confirm the flag reaches subscribers correctly in both directions. 2 existing tests in the same file needed updating for the new event-argument type (they read `e.PresetName` instead of a raw string now), plus one reflection-based test (`ApplyMaxCooling_ForcedApply_AlwaysWrites`) needed its `Invoke(fanService, new object[] { true })` call updated to pass both parameters explicitly, since `MethodInfo.Invoke` doesn't apply C# default-parameter values the way a normal call does. No test exists for `MainViewModel.OnFanPresetApplied`'s link-sync gate itself — it's a private method on a very large, heavily-DI'd ViewModel, and the guard is a simple, visually-verifiable one-line boolean check, so a full integration-test harness wasn't judged worth it for this fix, consistent with similar judgment calls made elsewhere in this codebase.

---

### Corsair `NotSupported` Stubs Could Show "Success" for a Write That Never Reached the Device

Turned out bigger than "an afternoon" once traced end to end: `ApplyDpiStagesAsync` wasn't just mislabeled, it was returning plain `Task` with no way to signal failure at all, so a confirmed-failed write on the RGB.NET backend (`"DPI configuration not supported via RGB.NET"`) still let the ViewModel update the device model, save config defaults, and update the saved profile as if it had succeeded — right after a real confirmation dialog told the user hardware was about to change.

Fixed by changing the interface to `Task<bool>` (matching `ApplyLightingAsync`'s existing "true only if the write actually reached the device" contract) end to end: `ICorsairSdkProvider` → `CorsairSdkStub`/`CorsairICueSdk`/`CorsairHidDirect` → `CorsairDeviceService` → both ViewModel call sites (`LightingViewModel.ApplyCorsairDpiAsync`, `MainViewModel.SaveCorsairDpi`). A failed apply now shows an explicit "DPI Settings Not Applied" dialog and leaves the device model untouched.

`BatteryPercent = 100` (2 spots) fixed to `0`, which — via `CorsairDeviceStatus.ToString()`'s existing `if (BatteryPercent > 0)` convention — now correctly shows nothing instead of a fabricated full charge. `FirmwareVersion = "Unknown"` was already honest as written; left alone.

Macro upload has the identical "not supported, no-op" shape on every backend (including the real HID-direct one, which explicitly logs "not yet implemented") but is never called from any ViewModel — confirmed via grep, no UI action reaches `ApplyMacroAsync` at all — so it's dead code, not a live honesty bug; needs an actual "build it or remove the UI for it" decision, not a quick fix, and wasn't touched here.

3 new/strengthened tests, 2 test fakes updated for the new signature. The signature change also shifted two pre-existing bare-`catch{}` blocks in `CorsairHidDirect.cs` past the code-hygiene gate's line-pinned baseline; updated those two baseline entries with the same shift-tracking comment convention already used throughout that file rather than suppressing the check. Full suite: 1377/1377.

---

### RGB Page's "Control Ownership" Card Could Show "Confirmed" With No Real Keyboard Backend

Raised by the owner during v4.2.0 and explicitly deferred twice (`ROADMAP_v4.2.0.md:376`, `:597`) — only two spot-fixes had landed there (scene swatches, the stray purple badge).

**Research first.** Looked at how OpenRGB (GPL-2.0, the leading open-source multi-brand RGB tool — verified against its actual `.ui` Qt source, not just docs) structures multi-brand device control: a `QTabWidget` with one tab per *detected* device, not static per-brand sections — the adoptable principle is "only show controls for hardware that's actually present," not the code itself (different language/framework, incompatible license anyway). Also checked `omen-light-studio` on GitHub, which looked like a promising OMEN-specific reference — turned out to be an empty org with one `.github` profile repo, zero code, zero stars; discarded.

**First pass at applying that principle found the concern was already solved.** Read `LightingView.xaml` expecting to find Corsair/Logitech/Razer sections always rendering full-empty even for users who own none of that hardware (the naive read of the XAML, with no `Visibility` binding on those three section `Border`s, looked exactly like that bug). Tracing further into `LightingViewModel.cs` and `FeaturePreferences.cs` disproved it: `CorsairIntegrationEnabled`/`LogitechIntegrationEnabled`/`RazerIntegrationEnabled` already default to `false` ("user must enable if they have \[brand\] devices" is in the doc comment), and each card's body already collapses to just a header + status pill + enable-toggle via `IsRazerCardContentVisible => RazerCardEnabled` and its Corsair/Logitech equivalents. A fresh install already shows collapsed brand headers, not full empty sections — this was good, deliberate design already in place, not a bug. Recorded here so nobody re-diagnoses the same non-issue later.

**Live-machine look (owner drove the actual app, not a code read) found a real bug instead.** A screenshot of the running Lighting page on a desktop PC with no HP hardware at all (AMD Ryzen 7 9800X3D + RX 9070 XT, already correctly flagged by the page's own "Unsupported System Detected" banner) showed the "Control ownership" card reading `HP Keyboard (None)` right next to a green `Confirmed` badge, and the "OMEN Keyboard" status chip at the top of the page highlighted as active alongside it. Getting to that screenshot needed its own detour: launching the debug build via `dotnet OmenCore.dll` (to dodge `app.manifest`'s `requireAdministrator`, since an elevated window blocks input from the automation tooling via Windows UIPI) crashed outright — Windows Event Log showed an access violation (`0xc0000005`) in `coreclr.dll` seconds after startup, almost certainly a native P/Invoke call (EC/WMI/PawnIO/ADL2) not expecting to run unprivileged. Relaunching the real, elevated `OmenCore.exe` (owner approved the UAC prompt) was stable; the owner then drove the app directly and sent screenshots rather than the tooling controlling it.

**Root cause, traced in `KeyboardLightingService.cs`:** `IsAvailable` (line 70) was `_useV2Backend || _wmiBiosAvailable || _wmiAvailable || _ecAvailable || (_oghProxy != null && _oghProxy.IsAvailable)` — including `_ecAvailable` with no further condition. `BackendType` (the property that actually decides what gets used and is the thing `RgbOwnershipSummary`/`HpKeyboardActiveBackend` display) only ever returns an EC-backed result — in either the explicit-preference branch (line 113) or the Auto-mode fallthrough (line 129) — when `_ecAvailable && IsExperimentalEcEnabled` both hold; otherwise it falls through to `"None"`. `_ecAvailable` itself (set at line 226) means only "`_ecAccess.IsAvailable` returned true" — a generic "can this process talk to *an* embedded controller via PawnIO" check, true on nearly any modern PC for ordinary power-management purposes, with no relationship to whether that EC is an HP OMEN keyboard controller. This dev machine has PawnIO installed (confirmed in the same session's log: `PawnIO installed, but its bundled MSR module only supports Intel CPUs`), so `_ecAvailable` was `true`, `IsExperimentalEcEnabled` was `false` (the user never opted into the experimental/riskier EC keyboard-write path), and `IsAvailable` and `BackendType` landed on opposite verdicts for the identical state.

**Fix:** gated the EC term in `IsAvailable` on `IsExperimentalEcEnabled`, matching `BackendType`'s own logic exactly: `_useV2Backend || _wmiBiosAvailable || _wmiAvailable || (_ecAvailable && IsExperimentalEcEnabled) || (_oghProxy != null && _oghProxy.IsAvailable)`. One-line change; `_useV2Backend` was independently confirmed always consistent with `_v2Service != null` (both set together, inside the same constructor `try` block, single assignment site each) so it needed no change.

**Tests:** new file `KeyboardLightingServiceAvailabilityTests.cs` (3 tests) — `_ecAvailable=true` with no experimental opt-in now correctly yields `IsAvailable=false`/`BackendType="None"`; `_ecAvailable=true` with the opt-in yields `IsAvailable=true`/`BackendType="EC"`; and a no-backend-at-all baseline. Built via `RuntimeHelpers.GetUninitializedObject` + reflection to set the relevant private fields directly, since the real constructor needs live WMI/EC hardware-access objects — no existing test file covered this class directly before now. Full suite: 1380/1380.

**Not a field-validation item.** Pure logic-consistency fix between two properties of the same class describing the same state — doesn't touch any EC/WMI write path, only which value gets displayed and treated as "available" for UI purposes.

**Remaining scope, not yet done:** the rest of the "needs love" pass (visual consistency across brand-header treatments — hardcoded colored-square letter badges like "C"/"R" standing in for real logos — and whatever else surfaces from continuing to look at the live page) is still open. Continuing as time allows rather than trying to fully scope a scroll-length page from one screenshot.

---

### Two New Model Database Entries

- **`8E5E`** — HP Victus 15-fa2303TX (C2JQ3PA), [#178](https://github.com/theantipopau/omencore/issues/178). Reporter's own fan-verification diagnostic: `Backend: WMI BIOS | RPM source: Estimated`, 3/6 tests passed (60/100, "Fair") — WMI fan-level control responds, but RPM comes back as the commanded level echoed, not a real tachometer reading, and that estimate diverged from expectations under sustained load (CPU@60%, CPU@100%, GPU@100% all failed with "evidence: None"). Reflected as `SupportsRpmReadback = false` rather than claiming a number this board hasn't actually demonstrated. Single-zone, static-color-only keyboard backlight per the reporter, matching the established `15-fa`-series pattern (`FanZoneCount = 1`, `HasFourZoneRgb = false`).
- **`8603`** — HP OMEN 17-cb0xxx (2019, i9-9880H + RTX 2080), [#182](https://github.com/theantipopau/omencore/issues/182). Pre-dates the 2021-2023 `OmenModelFamily.OMEN17` range, so classified `Legacy` instead. Gives this board a fixed, named entry instead of depending on the family-fallback path (now itself fixed, but still generic) — GPU Power Boost specifically confirmed non-functional via the reporter's independent OmenMon probe, everything else conservative pending further field data.

---

### Extract `OmenCore.Core`

**Scope-mapping first, before moving anything.** `OmenCoreApp.csproj` was `<UseWPF>true</UseWPF>`
with `Models/` (40 files), `Hardware/` (38), and `Services/` (114, across 9 subdirectories) all
compiled straight into the WPF assembly. Rather than trust the earlier claim that "only
`NotificationService.cs`" had a WPF coupling, grepped `Services/`, `Hardware/`, `Models/`, and the
top-level `Corsair/`/`Logitech/`/`Razer/` DTO folders directly for `System.Windows` (both `using`
lines and fully-qualified inline references, since several call sites used the latter with no
`using` at all). Found 14 files with a real coupling, not 1 — the original audit had only counted
literal `using System.Windows` lines and missed every fully-qualified `System.Windows.Application.Current`
/`System.Windows.Forms.SystemInformation`/`System.Windows.MessageBox.Show` call.

**Every one of the 14 turned out to be one of three narrow, mechanical patterns** (not 14 unrelated
problems): a UI-thread-marshal check (`Application.Current?.Dispatcher`), an AC/battery status
read (`System.Windows.Forms.SystemInformation.PowerStatus`), or a hard window/interop dependency
(`HwndSource`, a WPF `MessageBox.Show`, the WPF `Key` enum as a public property type). The first
two are UI-framework-agnostic *concepts* wearing a WPF-specific implementation; the third genuinely
can't move without either a window handle or the type they're built around.

**New Core-side abstractions, both in `OmenCore.Core/Utils/`:**
- `UiThreadMarshaller` — three settable static delegates (`InvokeAsync`, `BeginInvoke`,
  `IsOnUiThread`) plus `ShouldSuppressActivation` (see `AppHost` below). Default behavior (no host
  wired) runs everything inline and reports no UI thread — correct for tests and a future headless
  host. `OmenCoreApp.App`'s constructor wires the real WPF `Dispatcher`-backed behavior in via a
  new `WireUiThreadMarshaller()`, called right after `ForceInvariantNumberFormatting()` and before
  anything that could construct a Core service. Five call sites moved onto it
  (`CorsairDeviceService`, `LogitechDeviceService`, `PowerAutomationService`,
  `HardwareMonitoringService`, `ThermalSensorProvider`, `FanService` — six, plus
  `NotificationService`'s two `DispatcherHelper.RunOnUiThread` calls, which is the same pattern
  under a different name). In two cases (`FanService`'s telemetry sync, `PowerAutomationService`'s
  `PowerStateChanged` raise) the original code's "no dispatcher → skip entirely" fallback became
  "no dispatcher → run inline" — a deliberate, reasoned improvement (headless callers now actually
  get the update instead of it silently vanishing), not an oversight; recorded here so it reads as
  intentional if it's ever questioned.
- `PowerStatusHelper` — a direct P/Invoke wrapper around `kernel32!GetSystemPowerStatus`, the same
  Win32 call `System.Windows.Forms.SystemInformation.PowerStatus` itself wraps. Deliberately does
  **not** swallow a failed query (throws `Win32Exception`, matching what the WinForms property
  would surface) — every original call site (`PowerAutomationService`, `AutomationService`,
  `WmiBiosMonitor`) already had its own try/catch with a WMI or WinRT fallback path, and catching
  the error inside the helper would have silently skipped that fallback instead of triggering it.
  Also replaced a confusing `BatteryLifePercent * 100` / "255*100=25500 means unknown" pattern in
  `AutomationService.EvaluateBatteryTrigger` with a plain `int?` (`null` = unknown) — same
  observable behavior, clearer types, done in the same edit rather than as a separate cleanup pass.

**The bigger, unplanned find: `App.Logging`/`App.Configuration`/bare `App.Current`.** These reach
nearly every service in the codebase via a bare `App.Logging` call, relying on C# resolving an
unqualified name through *enclosing* namespaces — `OmenCore.Services`/`OmenCore.Hardware` are
nested under `OmenCore`, so `App` (defined in `namespace OmenCore` in `OmenCoreApp/App.xaml.cs`)
resolves without a `using` directive. That trick depends on being in the same assembly; it doesn't
survive a project split at all, circular-reference or not. Grep for the literal `System.Windows`
string never had a chance of catching this, and it wasn't caught until the actual compiler said
"`App` does not exist in the current context" 16 times across 8 files. Fixed by adding
`OmenCore.AppHost` (a new file directly in Core, `namespace OmenCore`) holding the real
`LoggingService`/`ConfigurationService` singleton instances, then making `OmenCoreApp.App.Logging`/
`.Configuration` thin forwarding properties to it — so every WPF-side call site that already says
`App.Logging` keeps compiling unchanged, and the 6 Core-side files that used it were mechanically
repointed to `AppHost.Logging`/`AppHost.Configuration`. `App.ShouldSuppressWindowActivation`
(RDP/lock session-state, read by `OmenKeyService` to decide whether to suppress a hotkey action)
got the same treatment via `UiThreadMarshaller.ShouldSuppressActivation`, wired alongside the
dispatcher delegates.

**`InternalsVisibleTo` had the wrong assembly name on the first pass.** 15 files across the moved
folders use `internal` types. Added `[assembly: InternalsVisibleTo("OmenCoreApp")]` to Core by
habit — and it silently didn't work, because `OmenCoreApp.csproj` sets
`<AssemblyName>OmenCore</AssemblyName>`; the actual compiled assembly is named `OmenCore`, not
`OmenCoreApp` (that's just the project folder). Caught by the compiler (`CS0122: inaccessible due
to its protection level`) on the first full-solution build, not by inspection — fixed to
`InternalsVisibleTo("OmenCore")`, with a comment recording why the obvious name is wrong so nobody
"fixes" it back.

**Two files reclassified mid-move from "stays behind" to "moves," because grouping by folder
location was misleading.** `Utils/BackgroundPollingCoordinator.cs` and `Utils/PollingScheduler.cs`
live in the WPF-converters-and-commands `Utils/` folder by convention, but both are explicitly
documented in their own doc comments as having "no WPF/Dispatcher dependency" — pure
`System.Threading.Timer`-based scheduling infrastructure that `ProcessMonitoringService.cs`
(a Core file) actually depends on. Same story for `Utils/AppVersionProvider.cs`, needed by
`ProfileExportService.cs` — moved, and while touching it, switched its
`Assembly.GetExecutingAssembly()` to `Assembly.GetEntryAssembly() ?? GetExecutingAssembly()`,
since "app version" should mean the running `.exe`'s version regardless of which assembly the
reading code happens to live in now (the two were identical today only because Core's `.csproj`
happens to carry the same version stamp as the app; that coincidence isn't guaranteed to hold).

**One value-type decoupling: `Corsair/MacroAction.cs`'s `Key` property.** Used the WPF
`System.Windows.Input.Key` enum, and was reachable from six Core files (`ICorsairSdkProvider`,
`CorsairHidDirect`, `CorsairDeviceService`, `AppConfig`, `ConfigurationService`,
`DefaultConfiguration`) via `MacroProfile`/`MacroAction` — far too central to just exclude the way
the six window/UI-bound files were excluded. Checked live usage before touching the type, not
after: `LightingViewModel.cs`'s macro profiles are hardcoded placeholder names
("Default"/"Gaming"/"Productivity") with permanently-empty `Actions` lists, and
`MacroService.PushEvent(Key, bool, int)` — the only code that would ever populate a real `Key` —
has zero callers anywhere in the codebase (confirmed via grep, not assumed from an earlier
"probably dead" note about macro upload generally). Retyped `MacroAction.Key` to a raw
`int` (a future real implementation can decide its own representation) and moved the whole
`Corsair`/`Logitech`/`Razer` DTO folders to Core along with it.

**`OmenCore.HardwareWorker.csproj`'s file-linking broke and needed a path fix.** It shares
`GpuPowerStateProbe.cs`/`GpuRestartGate.cs` with the main app via `<Compile Include="..\OmenCoreApp\Hardware\...">`
rather than a project reference (a third sharing pattern, older than this extraction). Both files
moved to `Hardware/` under Core, so the `Include` paths needed updating to
`..\OmenCore.Core\Hardware\...` — caught immediately by the full-solution build (`CS2001: source
file could not be found`), not something that needed hunting for.

**Verification.** Full solution build clean (0 warnings, 0 errors) across all 7 projects. Full test
suite: 1380/1380, identical to the pre-move count — this was a pure structural move plus
behavior-preserving abstraction, not a functional change, and the unchanged test count is the
actual evidence of that, not just an assertion.

**What's still in `OmenCoreApp` on purpose, not by omission:** `ToastNotificationService.cs` (WPF
toast UI itself), `OsdService.cs` (owns a real WPF overlay `Window`), `HotkeyService.cs` +
`RuntimeHotkeyCoordinator.cs` (global hotkey registration needs a real `HwndSource`/window handle
to receive `WM_HOTKEY`), `CurveRecoveryService.cs` (pops a `MessageBox.Show` directly from inside a
service — a real design smell, flagged for a future pass, not fixed here since changing it means
an event/callback contract change at its call site too), `DiagnosticExportService.cs` +
`ModelReportService.cs` + `ModelIdentityResolutionSummary.cs` (the diagnostics-export layer takes
a live `HotkeyService?` dependency to report hotkey state in the exported bundle, so it stays
paired with it), and `MacroService.cs` itself (kept its WPF `Key`-typed `PushEvent` signature since
nothing calls it either way — no reason to touch a second file for a change already isolated to
the type it constructs).

---

### Windows CLI — first slice: `status` / `fan` / `performance`

The actual point of the extraction, started the same session right after Core landed. New
`src/OmenCore.Cli` console project (`AssemblyName: omencore-cli`, matching Linux's binary name),
referencing `OmenCore.Core` directly — no duplicated hardware logic. `System.CommandLine
2.0.0-beta4.22272.1`, the same package/version Linux's CLI uses, for a consistent option-parsing
feel across platforms (`-p`/`--profile`, `-j`/`--json`, etc.).

Linux's `Commands/` folder was read as a **structural** template (per-verb `Command.Create()`
factories, `--json` for scripting, a boxed human-readable default) — not a source of Windows
hardware logic, since Linux talks to sysfs paths directly and shares none of that with
`OmenCore.Core`.

**`CliContext.cs`** is the actual new work: a bootstrap that constructs `HardwareBringup` →
`FanService`/`PerformanceModeService` the *same way* `MainViewModel`'s constructor does
(`src/OmenCoreApp/ViewModels/MainViewModel.cs`, ~line 2490 onward, traced line-by-line rather than
guessed at) — so a CLI command exercises the identical, already-shipped `ApplyPreset`/`Apply` code
paths a GUI click would, not a second implementation. One deliberate behavioral difference,
called out in its own doc comment: **the CLI never calls `FanService.Dispose()`**.
`Dispose()` resets the EC back to BIOS auto-control, which is correct when the GUI app quits but
wrong for a CLI — `fan --profile quiet` is supposed to leave the fan on quiet after the process
exits, matching how the Linux CLI's writes persist past the invocation that made them. Also
disables `NotificationService` (`IsEnabled = false`) so a script looping fan-profile changes
doesn't get spammed with toasts — a GUI affordance the CLI shouldn't inherit by default.

**Scope of this first slice:** `status` (read-only: model/board ID, EC/fan-controller
availability and backend, live fan RPM/duty via `IFanController.ReadFanSpeeds()`, current
performance mode; `--json` for scripting), `fan --profile <name>` / `--status` (applies a preset
by name from `config.FanPresets`, matching what the GUI's preset buttons already do), `performance
--mode <name>` / `--status` (same shape against `config.PerformanceModes`). Deliberately **not**
included: curve presets won't keep re-evaluating temperature after the process exits (that needs
`FanService.Start()`'s background monitor loop running continuously — i.e. a persistent process,
which is exactly what Linux's `daemon` command is for and this doesn't have yet), keyboard
lighting, `monitor` (continuous telemetry stream), `config` (get/set arbitrary config keys), and
`diagnose` (would want to reuse `DiagnosticExportService`, which stayed in `OmenCoreApp` — see
above).

**Verified:** full solution build clean (0 warnings, 0 errors), and `--help` for the root command
and all three subcommands rendered correctly via the framework-dependent host
(`dotnet omencore-cli.dll --help`, which never reaches `CliContext.Create()` — System.CommandLine
handles `--help` before invoking a handler, so this checks the option/argument wiring without
touching any hardware code). **Not verified: an actual elevated run against real hardware.**
Running the self-contained `.exe` directly triggers the `requireAdministrator` manifest's UAC
prompt (same as `OmenCoreApp.exe`), and bypassing that via the non-elevated `dotnet
omencore-cli.dll <command>` path is the same trick that produced a genuine native access
violation in `coreclr.dll` earlier this cycle when tried against the WPF app (documented in the
RGB-page-pass entry above) — not worth risking again just to "test" something the owner can
verify directly and safely by running the real elevated binary. This is new-caller-of-
already-shipped-methods, not new hardware-write logic, so it doesn't need the evidence-gate's
field-validation bar the way a fan/EC *behavior* change would — but it does need an actual
elevated run before anyone should treat it as done, and that hasn't happened yet.

**One packaging note, not a functional problem:** the self-contained publish output pulls in the
full Windows Desktop shared runtime (`PresentationCore`, `PresentationFramework`,
`System.Windows.Forms.*`) even though neither `OmenCore.Cli.csproj` nor `OmenCore.Core.csproj` set
`UseWPF`/`UseWindowsForms` — an artifact of the `net8.0-windows10.0.19041.0` TFM (needed for the
WinRT `Windows.Devices.Power.Battery` fallback in `PowerAutomationService`) plus
`SelfContained=true` bundling whatever the Windows Desktop runtime pack makes available, not of
the CLI actually referencing WPF types anywhere (0 build errors with no `UseWPF` confirms that).
Bigger download than a "lean CLI" ideally would be; not investigated further this pass since it
doesn't affect correctness.

---

## Decided, Not Fixed

### `FanControlView`'s "Custom: Unavailable" State

From an r/omencore question this session. `FanControlViewModel.cs:508` renders the bare word
`Unavailable` with no indication of *why*, and the two causes are very different for the user: no
fan backend at all (`FanWritesAvailable == false`) versus this board's `SupportsFanCurves` flag
being conservatively false. The Model Capabilities screen already carries the right explanatory
language; this card doesn't link to it or distinguish the cases. Owner chose "leave as information
for now"; reconsider only if it recurs.

---

## Investigated, Not Yet Actioned

### GitHub #179 — Linux Per-Key RGB for OMEN MAX 16-ak0xxx (board `8D87`)

Exceptionally detailed field report from a Linux user (Omarchy/Arch, kernel 7.1.9): the internal keyboard (Darfon `0D62:54BF`, "HP Gaming Keyboard II") exposes 5 HID interfaces, all currently claimed by the generic `hid-generic` driver. OmenCore's Linux keyboard backend looks for `/sys/class/leds/hp::kbd_backlight`, which doesn't exist on this board — there is no HP WMI/sysfs LED interface exposed for this keyboard's RGB controller at all. The reporter independently tested `arfelious/omen-rgb-linux`'s userspace HID implementation and confirmed both full-keyboard static color and true per-key RGB work correctly through `/dev/hidraw4` (USB HID interface 3 of that VID:PID), including setting individual keys (`Esc` red, `WASD` green) independently.

This confirms a real hardware mapping (`VID:PID = 0D62:54BF`, RGB HID interface = 3) but implementing support means adding a new Linux HID-direct backend to `OmenCore.Linux`/`OmenCore.Avalonia` — a real feature addition, not a one-line fix, and one this environment can't validate without the actual hardware. The reporter has offered to test a development build. Scoping this properly (new backend architecture, sysfs-vs-HID backend selection, per-key protocol from the referenced project) is deferred to a future pass rather than guessed at blind.

### GitHub #180 — "Doesn't start with Windows, config not saving"

One sentence, no board ID, no diagnostics export, no repro steps. A quick scan of `ConfigurationService.cs` didn't surface an obvious candidate bug without more to go on, and guessing at config-save/startup-task code blind risks a wasted or wrong fix. Needs a diagnostics export or explicit repro steps (does it happen every launch? any error in the log? which Windows startup mechanism — Task Scheduler entry per `Settings > Start with Windows`, or something else?) before this is actionable.

### GitHub #181 — GPU Power Boost Wattage Ceiling (the non-linking part)

Separate from the Quiet Safety/linking bug fixed above: the reporter's transcribed wattage table shows OmenCore's GPU Power Boost levels landing at different absolute wattages than expected, and specifically that the ceiling seems to track whatever OGH last configured rather than an OmenCore-controlled absolute value.

Traced to `HpWmiBios.BuildGpuPowerPayload` (`GpuPowerLevel` enum): every level (`Minimum`/`Medium`/`Maximum`/`Extended3`/`Extended4`) is documented in its own XML comments as a **relative boost step** ("Custom TGP enabled (+15W on most models)", "+15-25W depending on model") sent via the same `customTgp`/`ppab` bit pattern HP's own BIOS handler expects — not an absolute-wattage command. OmenCore and OGH both ultimately hand the same relative step to the same firmware handler; the actual resulting wattage ceiling is therefore firmware/EC-state-determined, not something either app fully controls independently. This is an architectural constraint already correctly documented in code, not a silent bug — though it's plausible the *UI* doesn't communicate this uncertainty clearly enough to the user, worth a look in a future pass.

Not actioned: would need the reporter to test with OGH fully closed (not just not running — fully uninstalled or its background services stopped) between profile switches to isolate whether the ceiling genuinely persists across OGH's absence, before any code change is justified. Real-hardware, RTX 5090-specific behavior this environment cannot reproduce.

### PR #176 — Re-reviewed 2026-08-30, Recommend Against Merging As-Is

Fresh trace against the 2026-08-19 8-agent synthesis, prompted by a new commit landing 2026-08-29 (`6c560253`, "Subscribe to the process trace events, not a one-second diff of the process table").

- **Process-monitoring fix: genuinely fixed.** Now subscribes to `Win32_ProcessStartTrace`/`Win32_ProcessStopTrace` (kernel-pushed, extrinsic trace classes) instead of `__InstanceCreationEvent`/`__InstanceDeletionEvent ... WITHIN 1`, which had no real notification source and was being serviced by WMI silently re-polling the entire process table twice a second inside `WmiPrvSE.exe`. New test (`ProcessMonitoringEventQueryTests.cs`) pins the query strings. One caveat: the branch is now stale against `main`, which independently consolidated its own polling timer on 2026-08-21 (commit `5bd77eb`) — will need a rebase, not just a review, before merging.
- **Keyboard "effect-freeze" bug: still broken, relocated a second time.** `DojoPerKeyBackend.cs`'s `_mapR/_mapG/_mapB` fields are set but never cleared to null. The specific gap from the 08-19 review (`ApplyRecord`/`TakeHostControl` not resetting `_mcuShowsHostMap`) is now closed — but `SetBacklightEnabledAsync` still uses `_mapR != null` ("was a map ever painted") as a stand-in for "is the map what's currently displayed," so toggling backlight off/on after applying a device effect resurrects the stale map and freezes the effect on the next brightness change. Same root defect, one hop further from where it was found last time.
- **iGPU Curve Optimizer gating bug: unchanged.** `AmdUndervoltProvider.cs`'s early-exit check still runs before the more complete family-based capability table, so several real AMD APU families (HawkPoint, VanGogh, Rembrandt, RenoirLucienne, CezanneBarcelo) still get the wrong "no confirmed iGPU CO" reason instead of reaching the real check. The Strix Halo/Strix Point exclusion itself remains correctly fixed.
- **Changelog-target mismatch: still present.** New entries in this PR still target `docs/CHANGELOG_v4.1.7.md`, several versions behind the current repo state.
- **New, unrelated finding:** `AmdUndervoltProvider.ProbeAsync` now sets a specific warning reason for a dropped iGPU CO request, and it does flow correctly through `TuningStatusFormatter` to a real "Verified: warning (...)" message — this part of the original honesty fix works end-to-end even though the `IgpuOffsetRequestedButNotApplied` flag it also sets is never read anywhere (cosmetically dead code, not a bug).

Two separate bugs surviving two independent fix attempts each is a pattern worth taking seriously — not simply "needs one more small patch." Recommend asking the contributor to address the relocated keyboard bug and the still-untouched iGPU CO gating bug specifically, rather than merging as-is or attempting a from-scratch fix ourselves given the size of this PR (74 files). Final call on wait/fix-ourselves/merge-as-is remains the owner's; this is fresh evidence for that decision, not a decision itself.

---

## Possible Future Pass: Class-Level Capability Defaults

Flagged above and worth recording explicitly: `ModelCapabilities`'s property-level defaults (`SupportsFanControlEc`, `SupportsFanCurves`, `SupportsIndependentFanCurves`, `SupportsGpuPowerBoost`, `HasFourZoneRgb`, `SupportsUndervolt`, `SupportsTccOffset`, `SupportsPowerLimits` — all `= true` at the class level) are the root shape of the bug fixed above for the two fallback paths, but the ~150 named board entries in `ModelCapabilityDatabase.cs` were not audited for whether any of them silently relies on inheriting one of these `= true` without setting it explicitly. A future pass could grep every `AddModel(...)` block for entries missing an explicit value on each of these eight properties, and either confirm each omission is intentional (the board genuinely supports it and just never had to say so) or add the explicit `= true`/`= false` the same discipline already used everywhere else in the file expects. Larger and riskier than the rest of this cycle's items — not attempted here.

---

## Not Yet Started

### Windows CLI — remaining commands

`keyboard`, `monitor`, `config`, and `daemon` (continuous curve/hold as a persistent process,
matching Linux's shape) are not built yet — see the scope note in the CLI's "Done" entry above for
why each was left out of the first slice.

### Local HTTP / named-pipe control API

`ROADMAP_v2.5.0.md`'s nice-to-have, for Stream Deck / scripting / home-automation integration.
Unblocked by the Core extraction, not started.

### Package-reference cleanup on `OmenCoreApp.csproj` (minor, deliberately deferred)

`OmenCoreApp.csproj` still lists `CUE.NET`, `HidSharp`, `LibreHardwareMonitorLib`, `NAudio`,
`NvAPIWrapper.Net`, `RGB.NET.Core`, `RGB.NET.Devices.Corsair`, `System.Management`, and
`System.ServiceProcess.ServiceController` even though grep confirms none of `ViewModels/`,
`Views/`, `Controls/`, or the remaining `Utils/` reference those namespaces directly anymore — they
now reach the app only transitively through the `OmenCore.Core` project reference. Redundant, not
broken (doesn't affect correctness, only a marginally larger restore/reference set). Left alone
here rather than risking a last-minute trim after a green full-suite run; a follow-up pass can
verify each package is genuinely droppable and remove them one at a time.

### Extend `PowerAutomationService` rather than building a scheduler — but settle profile ownership first

`ROADMAP_v2.5.0.md` §7 asks for time-of-day / lid-close / charger-connect profile triggers. This is **not** greenfield: `Services/PowerAutomationService.cs` already implements exactly this shape for one trigger type, with a clean settings model (`AcFanPreset`, `AcPerformanceMode`, `AcGpuMode`, and Battery equivalents) and existing `PowerStateChanged` / `SystemSuspending` / `SystemResuming` events. Adding trigger sources to a working service is far cheaper than a new subsystem.

**The trap, and it's a real one:** this is the same service diagnosed in v4.2.0's #177 triage as the cause of "custom fan curve not restored on restart" — it reapplies a per-power-source preset on *every* AC/Battery transition including startup, silently overriding whatever the user last selected. That was recorded as "working as designed with a surprising default." Adding more trigger types multiplies that surprise across more moments. **Whoever picks this up should settle the "who owns the currently-active profile" question first** — user selection vs. automation, and how a user override survives the next trigger — otherwise this ships a bigger version of an existing complaint. That ownership question is arguably the more valuable piece of work of the two.

### Localization / i18n

Requested in four separate cycles (`v1.4.md` §17, `v2.5.0.md`, `v2.6.0.md` §10, `v4.0.0.md`) — the single most persistently-asked-for item in the project's history, with zero infrastructure today (no `.resx`, no culture handling, confirmed 0 matches). Two things make it more expensive now than when first raised, and both should be priced in: v4.2.0's nav-rail redesign and accessibility passes *added* user-facing strings, and the Roboto Condensed switch (Pillar 3, still blocked on a genuine font-rendering non-determinism) has glyph-coverage implications for non-Latin scripts that would need resolving in the same breath. Big, but the demand signal is unambiguous.

### `MainViewModel` feature-scoped extraction (6,247 lines)

Deferred out of v4.2.0's Phase C when the owner chose the animation experiment instead. It keeps getting deferred, and it's increasingly the thing that makes everything else expensive — it's where the #181 link-cascade bug lived, where the fan/performance sync tangles, and it would need touching by the profile-ownership work above. Worth doing on its own terms rather than waiting for a cycle where it's blocking something urgent.

### Linux `omencore-gui` tray icon + GUI-side config persistence

Planned since `ROADMAP_v2.0.md`, still open. Verified absent — `TrayIcon` appears only in compiled Avalonia framework DLLs, never in project source; the tray only exists today as an external shell script. This is a real gap against the README's own "feature parity with Windows" claim.

### Smaller, genuinely-absent, no-gate items

All verified missing, all from `v1.4.md`/`v2.6.0.md` unless noted: network dashboard (per-app bandwidth, ping, QoS — `NetworkOptimizer.cs` today only does TCP/Nagle registry tweaks); storage health/SMART/TBW dashboard; config cloud sync (`v1.4.md` §11); display calibration & night mode (§13); external-monitor DDC/CI brightness (§18, and `ScreenSamplingService` is still single-monitor only per `v2.1.md` §4); webcam/mic privacy toggles (§19); battery calibration wizard (`v1.2`/`v1.3`); Discord Rich Presence / OBS integration (`v2.6.0.md` §7); plugin system (`v1.4.md` §16); SteelSeries RGB (`v1.5.md` §11); dashboard personalization and a per-model first-run tuning wizard (`v3.2.5.md` §8).

### Still hardware-gated — group these into one testing round

These can't be built or verified from this environment, but several would be cleared by a *single* cooperative tester rather than needing separate campaigns. Worth batching next time a tester volunteers: per-core overclocking (`v1.5.md`, deferred for warranty/BIOS-lock risk and recommended to stay deferred absent strong demand); a dedicated Balanced fan mode decoupled from Auto (`v4.0.0.md`); independent CPU/GPU fan curves (UI-visible but `FanControlViewModel.cs` hardcodes `IndependentCurvesFeatureAvailable => false`, `v3.3.0.md` #19); AMD GPU OC / Curve Optimizer startup persistence; a self-validating PL1/PL2 readback loop; board `8D41`'s Darfon `0x0D62:0x54BF` per-key HID backend (deliberately unimplemented — nobody has confirmed the device speaks `CMD_BYTE 0x0F`, and writing untested bytes to real hardware every launch was judged unacceptable); real Razer per-key/standalone Chroma (`RazerService.cs` still requires Synapse 3 running; `v4.0.0.md` calls it "the weakest of the three"); Logitech DPI via direct HID; and the GPU Dynamic Boost ceiling question (`v1.5.md` §3) — which is now the *same underlying question* as #181's GPU Power Boost report above, and should be investigated as one item, not two.

### Settled — do not revive without new information

Recorded so nobody re-litigates them: **NVIDIA V/F-curve editor** — planned across three roadmaps (`v1.3`, `v2.0`, `v2.1`), never built, and `TuningView.xaml` now actively directs users to MSI Afterburner instead; treat as a deliberate punt, not an oversight. **HP ENVY / non-gaming HP support** — ruled out of scope in `v4.0.0.md` (GitHub #154): `hp-wmi` loads but exposes no fan-control interface on that firmware. **Logs in `Program Files`** — won't-fix by design (`v1.4.md` BUG-12); `%APPDATA%` avoids requiring elevation on every write. **The Max Fan re-assert-loop's narrower mitigation** — rejected because backing off after repeated identical readings is indistinguishable from a genuinely-reverting fan, risking under-cooling a different board.

---

## Standing Rules (unchanged, carried from v4.2.0)

- **Evidence gate.** Fan/EC/thermal/OC/UV *behavior* changes need field validation before shipping. Architecture, performance, display-honesty, and pure-UI items do not. The fixes in this document tighten false-positive claims toward conservative, or are pure structural/architectural work with a full green test suite as verification — none of it added a new hardware-write path, so none needed field validation to ship.
- **One item at a time, verified before moving on.** Build clean, full suite green, live-smoke-test the real UI path where feasible.
- **Update this document as you go.** Check items off only once verified, with a one-line note on what changed and which files.
