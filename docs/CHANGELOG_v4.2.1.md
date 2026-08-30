# OmenCore v4.2.1

**Release Date:** TBD — rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-08-30, immediately after v4.2.0 shipped.
**Type:** Patch release. Field-report fixes from GitHub issues opened after v4.2.0 (#178–#182), plus a capability-database honesty fix found while triaging them.
**Base Version:** v4.2.0
**Tracking doc:** `docs/ROADMAP_v4.2.1.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

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

---

## Added

### Two New Model Database Entries From Field Reports

- **[#178](https://github.com/theantipopau/omencore/issues/178)** — HP Victus 15-fa2303TX (C2JQ3PA), board `8E5E`. Added using the reporter's own fan-verification diagnostic (WMI fan-level control responds, but RPM readback is level-estimated rather than a real tachometer — reflected as `SupportsRpmReadback = false` rather than claiming a number this board hasn't demonstrated). Single-zone, static-color-only keyboard backlight per the reporter.
- **[#182](https://github.com/theantipopau/omencore/issues/182)** — HP OMEN 17-cb0xxx (i9-9880H + RTX 2080), board `8603`. Gives this board a fixed database entry instead of depending on the now-fixed-but-still-generic family fallback.

Both entries are conservative and unverified pending further field confirmation, consistent with every other database addition this cycle.

---

## Investigated, Not Yet Actioned

- **[#179](https://github.com/theantipopau/omencore/issues/179)** — Linux per-key RGB for OMEN MAX 16-ak0xxx (board `8D87`) via direct HID (`0D62:54BF`, interface 3). Excellent, detailed field data — a real feature addition (new Linux HID backend), not a quick fix. Scoped for a future pass.
- **[#180](https://github.com/theantipopau/omencore/issues/180)** — "Doesn't start with Windows, config not saving." One sentence, no diagnostics, no repro steps. Needs a diagnostics export or repro steps before it's actionable.
- **[#181](https://github.com/theantipopau/omencore/issues/181)** GPU Power Boost wattage — architectural, not a code bug: OmenCore and OGH both send relative *boost steps* to the firmware, not absolute wattages (already documented in code as "+15-25W depending on model"), so the actual ceiling is firmware-determined and can be influenced by whatever OGH last configured. Needs the reporter to test with OGH fully closed to isolate further.
- **PR [#176](https://github.com/theantipopau/omencore/pull/176)** — re-reviewed 2026-08-30. The process-monitoring fix from 2026-08-29 is real and correct, but two bugs from the 2026-08-19 review (keyboard "effect-freeze," iGPU Curve Optimizer gating) are **still broken**, with the keyboard bug relocated a second time. Branch is also now stale against `main`. Recommend against merging as-is; decision still pending owner call.

---

*(Further entries added as work lands.)*
