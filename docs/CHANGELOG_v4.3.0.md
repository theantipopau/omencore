# OmenCore v4.3.0

**Release Date:** TBD — rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-08-31, alongside the v4.2.1 patch cycle.
**Type:** Feature release. First item: extracting a standalone `OmenCore.Core` library out of the
WPF app, per `docs/ROADMAP_v4.2.1.md`'s "v4.3.0 candidate slate" item 1.
**Base Version:** v4.2.1 (in progress)
**Tracking doc:** `docs/ROADMAP_v4.3.0.md` — full investigation detail lives there; this file stays short.

---

## Added

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

---

## Investigated, Not Yet Actioned

Nothing yet — see `docs/ROADMAP_v4.3.0.md` for what building on top of this (Windows CLI, the
HTTP/named-pipe API) still requires.

---

*(Further entries added as work lands.)*
