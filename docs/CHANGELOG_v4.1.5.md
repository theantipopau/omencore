# OmenCore v4.1.5 – GPU Power Boost, Fan-Control Safety, and UX Clarity Fixes

**Release Date:** TBD
**Release Status:** Code-complete and test-verified in this environment (993/993 tests, 0 build warnings); artifacts not yet built or tagged.
**Type:** Patch release — three targeted fixes found while triaging field reports for the next cycle, no new feature surface
**Base Version:** v4.1.0
**Tracking doc:** `docs/ROADMAP_v4.0.0.md` — see "Newly Reported (Post-4.1.0 Release): Field Reports Triaged 2026-08-01" for the full traces this release acts on.

---

## Fixed: GPU Power Boost Never Worked on Victus 16-d1176TX (board `8A25`)

Same reporter (Discord, ACe_Centrick) filed this on three consecutive versions — v3.8.0, v4.0.0, and v4.1.0 — each time getting the same `GPU Power Boost: skipped — HP Victus does not support WMI TGP/PPAB control` log line, despite reporting that older OmenCore versions and OMEN Gaming Hub both successfully boost their RTX 3060 from 85W base TGP to 100W on the same hardware. One independent corroboration (OsamaBiden) on the same board family.

4.1.0 already fixed the *architecture* gap this depended on (the blanket Victus vendor-family deny now yields to an explicit per-model `SupportsGpuPowerBoost = true` opt-in), but no Victus board had that flag set, so behavior didn't change. This release adds the flag: a dedicated `ProductId = "8A25"` model-database entry (this exact board previously had no entry of its own — both field logs show `Found model by model-name pattern '16-d1' (no entry for ProductId '8A25')`, meaning it was silently inheriting the unconfirmed `8A26` sibling entry) with `SupportsGpuPowerBoost = true`. The `8A26` entry itself is untouched and still `false`.

This is a judgment call made after three consistent reports with no counter-evidence, rather than after receiving a session log with explicit before/after wattage figures — flagged transparently rather than claimed as field-log-proven. 2 new tests confirm the new entry resolves by exact ProductId ahead of the shared `16-d1` name pattern, and that it's independent of `8A26`.

## Fixed: Locked Fan-Curve Tooltip Read as "Not Verified" Instead of "Not Supported"

GitHub #156 (and its earlier #149) both center on a reporter trying to "force-verify" their OMEN Transcend 14 (board `8C58`) profile so custom fan curves would unlock. Traced the code: curve availability (`FanService.FanCurvesAvailable`) is gated purely by the `SupportsFanCurves` capability flag, completely independent of `UserVerified` — this board's `SupportsFanCurves = false` because Transcend 14 exposes a WMI profile-only fan interface, not a real curve API, same as its `8E41` sibling. The "unverified" status the reporter kept asking about has nothing to do with why curves are locked.

The Diagnostics tab already carries correct explanatory copy for this distinction; the locked Custom/Direct fan-control tiles elsewhere in the app didn't. `FanControlViewModel.CustomCurveTooltip`/`DirectFanControlTooltip` now explicitly state the limitation is a hardware/firmware capability, not related to verification status, instead of the ambiguous "unavailable for this model." Pure UI string change — no control-availability logic changed.

## Fixed: `ApplyMaxCooling()` Silently Reported Success Even When the Hardware Write Failed

Found during a broader audit of fan-control/RGB code for the same "logs success without confirming a real write happened" bug class already fixed once this cycle (the Logitech HID++ fallback). `IFanController.ApplyMaxCooling()` returned `void` — its three real backends (WMI, EC, OGH proxy) all had a real success/failure result available (`SetMaxFanSpeed`, `SetMaxFan`) but discarded it before it ever reached the interface boundary. `FanService.ApplyMaxCooling()` then unconditionally set its internal fan-mode field to `"Max"` and logged `"Max cooling mode active"` regardless of whether the write actually succeeded.

This is safety-relevant: `MainViewModel.OnQuietSafetyOverrideActivated` — the handler that fires when temperature crosses into critical territory while in Quiet mode — calls `ApplyMaxCooling()` and then reads `FanService.GetCurrentFanMode()` back as its confirmation that the emergency response took effect. Because that field was set unconditionally, the confirmation was circular: a transient WMI busy error during a genuine thermal emergency would leave fans unchanged while the log, the UI, and the "confirmation" all agreed nothing was wrong.

**Fix:** `IFanController.ApplyMaxCooling()` now returns `bool`, propagated honestly from all four backend implementations (WMI, EC — which already computed but discarded this result, OGH proxy, and the no-backend fallback, which now correctly returns `false`). `FanService.ApplyMaxCooling()` only marks Max mode active and logs success when a write actually succeeded (either the controller call itself, or — for non-WMI backends only — the existing defensive `SetFanSpeed(100)` fallback); on failure it logs a warning and records the failure in fan-command history instead. `MainViewModel`'s safety-override handler now surfaces a distinct warning toast ("temp critical, but Max cooling failed to apply") instead of unconditionally claiming success. 19 test-fake `IFanController` implementations across the test suite were updated for the new interface signature (all continue returning `true`, preserving existing test behavior); 1 new regression test (`ApplyMaxCooling_ControllerReportsFailure_DoesNotClaimMaxModeActive`) pins the fixed behavior on the WMI backend path specifically, since non-WMI backends already had a working fallback that happened to mask this bug.

**Scope note:** `ApplyAutoMode()`/`ApplyQuietMode()` on the same interface have the identical `void`-return shape and were flagged during the same audit, but were left unchanged this release — narrower scope, matching what was actually requested.

---

## Not Actioned This Release (Found During the Same Audit, Deliberately Deferred)

A broader pass across fan control, RGB providers (Razer, Corsair, Logitech), and UI/accessibility surfaced several more candidates of the same general shape — silently-swallowed write failures in `RazerService`'s effect setters (near-identical to the bug already fixed for Logitech this cycle), an unmatched effect-string case in `RazerRgbProvider`, per-device failure swallowing in `CorsairRgbProvider`, a `MainWindow.xaml` banner that conflates "unsupported" with "unverified" the same way the fan-curve tooltip used to, and an unfinished accessibility-labeling pass in `SettingsView.xaml` (~25 OSD toggle buttons with no `AutomationProperties.Name`). None of these were fixed this release — logged for a future pass.
