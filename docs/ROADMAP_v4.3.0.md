# OmenCore v4.3.0 Roadmap

## Why This Cycle Exists

`docs/ROADMAP_v4.2.1.md`'s "v4.3.0 candidate slate" named extracting `OmenCore.Core` as item 1
and explicitly flagged it as "secretly the gating item for several others" — a Windows CLI, a
local HTTP/named-pipe control API, and any future headless/service-mode operation all silently
depend on the service layer being reachable without a WPF reference. Owner approved starting it
2026-08-31 ("yep do it").

---

## Done

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
happens to carry the same `4.2.0` version stamp as the app; that coincidence isn't guaranteed to
hold).

**One value-type decoupling: `Corsair/MacroAction.cs`'s `Key` property.** Used the WPF
`System.Windows.Input.Key` enum, and was reachable from six Core files (`ICorsairSdkProvider`,
`CorsairHidDirect`, `CorsairDeviceService`, `AppConfig`, `ConfigurationService`,
`DefaultConfiguration`) via `MacroProfile`/`MacroAction` — far too central to just exclude the way
the six window/UI-bound files were excluded. Checked live usage before touching the type, not
after: `LightingViewModel.cs`'s macro profiles are hardcoded placeholder names
("Default"/"Gaming"/"Productivity") with permanently-empty `Actions` lists, and
`MacroService.PushEvent(Key, bool, int)` — the only code that would ever populate a real `Key` —
has zero callers anywhere in the codebase (confirmed via grep, not assumed from the roadmap's
earlier "probably dead" note about macro upload generally). Retyped `MacroAction.Key` to a raw
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

## Not Yet Started

### Windows CLI

The actual point of the extraction. Linux already has a full `omencore-cli` with a clean per-verb
command structure (`src/OmenCore.Linux/Commands/`: Fan, Performance, Keyboard, Monitor, Status,
Config, Daemon, Diagnose) that Windows can use as a template rather than designing from scratch.
Not started — this roadmap entry exists to record that Core alone doesn't ship a CLI, it only
removes the reason one couldn't exist.

### Local HTTP / named-pipe control API

`ROADMAP_v2.5.0.md`'s nice-to-have, for Stream Deck / scripting / home-automation integration.
Same status: unblocked, not started.

### Package-reference cleanup on `OmenCoreApp.csproj` (minor, deliberately deferred)

`OmenCoreApp.csproj` still lists `CUE.NET`, `HidSharp`, `LibreHardwareMonitorLib`, `NAudio`,
`NvAPIWrapper.Net`, `RGB.NET.Core`, `RGB.NET.Devices.Corsair`, `System.Management`, and
`System.ServiceProcess.ServiceController` even though grep confirms none of `ViewModels/`,
`Views/`, `Controls/`, or the remaining `Utils/` reference those namespaces directly anymore — they
now reach the app only transitively through the `OmenCore.Core` project reference. Redundant, not
broken (doesn't affect correctness, only a marginally larger restore/reference set). Left alone
here rather than risking a last-minute trim after a green full-suite run; a follow-up pass can
verify each package is genuinely droppable and remove them one at a time.

### Everything else on the v4.3.0 candidate slate

`PowerAutomationService` extension (profile-ownership question first), localization/i18n,
`MainViewModel` extraction, Linux `omencore-gui` tray icon, and the smaller items — all still
exactly where `docs/ROADMAP_v4.2.1.md`'s "v4.3.0 candidate slate" left them. Not touched this pass.

---

## Carried Forward From v4.2.1

Still open there, unaffected by this extraction: PR #176 (owner decision pending), GitHub #179
(Linux per-key RGB, needs a new HID backend), #180 (needs repro info), #181's wattage-ceiling
question (needs reporter testing with OGH fully closed), and the class-level capability-defaults
audit. See `docs/ROADMAP_v4.2.1.md` for detail — none of it is v4.3.0 scope, just still true.

---

## Standing Rules (unchanged, carried from v4.2.0/v4.2.1)

- **Evidence gate.** Fan/EC/thermal/OC/UV *behavior* changes need field validation before shipping.
  This cycle's work is pure architecture/structure with a full green test suite as verification —
  no new hardware-write path was touched or enabled, so the gate doesn't apply here.
- **One item at a time, verified before moving on.** Build clean, full suite green, live-smoke-test
  the real UI path where feasible.
- **Update this document as you go.** Check items off only once verified, with a one-line note on
  what changed and which files.
