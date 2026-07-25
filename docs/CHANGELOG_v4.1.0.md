# OmenCore v4.1.0 – Field-Report Fixes: Telemetry Accuracy and Fan Reassert Loop

**Release Date:** TBD (in development)
**Release Status:** In development. Code-complete and test-verified for the items below — 969/969 tests passing, 0 build warnings across all projects, plus runtime verification of the freeze-heuristic fix against a machine that reproduced the false positive (see Runtime Verification). **No physical-hardware confirmation yet** from the original reporters; the fan reassert-loop fix in particular needs their confirmation that the audible behavior matches.
**Type:** Minor release — targeted fixes for post-4.0.0 field reports, plus the architecture/accuracy issues found while tracing them
**Base Version:** v4.0.0
**Tracking doc:** `docs/ROADMAP_v4.0.0.md` — see "Newly Reported (Post-4.0.0 Release): Field Reports Triaged 2026-07-25" for the full traces this release acts on.

---

## Purpose

4.0.0 shipped, and five GitHub issues plus two Discord threads arrived against it. Tracing them turned up four real, provable defects — three of which had been misleading users into believing hardware telemetry was broken, and one of which had OmenCore fighting its own firmware for minutes at a time. This release fixes those, plus two accuracy problems in diagnostics that were actively hindering triage.

Every change here is either pure UI/display, a provable logic bug, or metadata. Nothing widens hardware-control surface, and the one fan-control change moves strictly in the direction of *fewer* EC writes.

---

## Fixed: Sidebar and Dashboard Showed Different Temperatures at the Same Instant

**Reported in:** [GitHub #152](https://github.com/theantipopau/omencore/issues/152) (xenon205, OMEN 17-ck1xxx, board `8A18`), with screenshots showing the sidebar at 65°C/54°C while the main cards and tray tooltip read 53°C/45°C simultaneously.

**Root cause — the sidebar was displaying raw, unfiltered sensor data.** `MainViewModel.NormalizeMonitoringSample` does considerably more than its name suggests: it sanitizes load percentages, range-clamps temperatures to 0-125°C, holds the last good reading when a per-field `TelemetryDataState` isn't acceptable, and runs `StabilizeTemperatureSample` spike rejection. `GeneralViewModel` (the General tab's main cards) received that fully-normalized sample. `DashboardViewModel` (which feeds the sidebar's live-temp chips) subscribed to `HardwareMonitoringService.SampleUpdated` **directly**, so it bypassed every one of those steps and rendered raw sensor output — including the exact spikes stabilization existed to reject.

So the two surfaces genuinely disagreed, and the sidebar was the *less* trustworthy of the two — not merely differently throttled.

**Fix:** `DashboardViewModel` no longer subscribes to the raw telemetry event at all. `MainViewModel` now pushes the single normalized sample to it via a new `UpdateFromNormalizedSample`, exactly as it already did for `GeneralViewModel`. The surfaces agree by construction instead of by keeping two independent filter implementations in sync — the third instance of the "duplicated logic quietly drifts" pattern found in this codebase across the 4.0.0 cycle.

**Verified:** 3 new tests (`DashboardTelemetrySourceTests`) pin that the dashboard ignores the raw event, projects pushed normalized samples, and treats a null push as a no-op rather than a reset to zero.

---

## Fixed: "Temperature Appears Frozen" Was Mostly a False Positive

**Reported in:** [#152](https://github.com/theantipopau/omencore/issues/152) / [#153](https://github.com/theantipopau/omencore/issues/153), and very likely the origin of the broader "temps not reading correctly" perception across several reports.

**Root cause — the detector ignored sensor quantization.** It counted consecutive identical temperature readings (>20, or >40 when idle) and warned. But HP WMI BIOS reports temperature in **whole degrees Celsius**, so a machine at thermal equilibrium legitimately reports the same integer many times in a row. That is a *working* sensor.

The field evidence is unambiguous. One diagnostics bundle contained **48 freeze warnings in a single session**, including:

```
🥶 GPU temperature appears frozen at 48,0°C for 21 readings (load=100%)
```

A GPU pinned at full load holding a steady 48°C is textbook thermal equilibrium with adequate cooling. Users reading a flood of alarming warnings reasonably concluded temperature reporting was broken, and filed it as a bug.

**Fix:** an identical-temperature run is now only reported as frozen when a correlated signal says the temperature *should* have moved — specifically, when load swung ≥15 percentage points across the same window while the temperature did not shift by even one quantization step. Flat temperature under flat load is treated as equilibrium and stays silent. An absolute read-count ceiling is retained as a backstop so a sensor genuinely wedged through a long stretch of constant load is still surfaced. The warning now also reports the observed load swing, so future reports carry the discriminating evidence rather than just a count.

Applied to **all four** detection sites — this logic was duplicated across two classes:
- `WmiBiosMonitor`: WMI CPU, WMI GPU, and ACPI CPU paths (the ACPI path gets its own load-range tracking, since it has an independent counter that the WMI path must not be able to reset mid-window).
- `HardwareMonitoringService`: CPU and GPU paths. This detector matters more than the diagnostic one, because a false positive here also flips `_usingWmiFallback` and can trigger a monitoring-bridge restart — so the false positives were causing real churn, not just log noise.

**Verified:** 7 new tests (`WmiBiosMonitorFreezeHeuristicTests`) replay the exact field shapes, including the 100%-load and idle equilibrium cases that previously false-positived, the wide-load-swing case that must still trip, threshold boundaries, and the unpopulated-sentinel case.

**Not claimed as fixed:** whether board `8A18` *also* has a genuine sensor stall underneath the noise. This removes the false positives; any real stall will now be reported with a load-swing figure proving it.

---

## Fixed: Fans Re-Asserted to Max in a Loop After a Thermal Emergency

**Reported in:** [GitHub #153](https://github.com/theantipopau/omencore/issues/153) — "rpms are also stuck at max after overheat alert." Reporter also noted a third-party tool (OmenMon-Reborn) handles the same scenario on the same hardware without this behavior.

**Root cause — the drop detection was mathematically unwinnable on this board.** `WmiFanController.IsMaxModeTelemetryHealthy` derived its healthy-floor as `MaxFanLevel * 0.90`. Board `8A18`'s nominal `MaxFanLevel` is 55, giving a floor of 50 — but the hardware holds a **steady 46-48** while Max mode is genuinely active. The log shows this on every single maintenance cycle:

```
⚠️ External fan reset suspected - Max mode re-applied after sustained drop (levels=46/48 floor=50, rpm=n/a)
```

The check could therefore never pass on this board, so every cycle concluded "sustained drop" and re-asserted `SetFanMax` — roughly every 20 seconds for ~2.5 minutes, against firmware that was already holding max. The readback was rock-steady throughout, never degrading; a genuine external reset would trend toward idle, not sit 4% under an arbitrary threshold.

This also matches a weakness the code already documented elsewhere: `SetMaxFanSpeed`'s own comment notes `MaxFanLevel` is an unreliable proxy for the real hardware ceiling in the *opposite* direction too (OMEN 16-xd0xxx holds level 63 against a nominal 55).

**Fix:** the strict nominal check remains the default and primary path. Only once a board has demonstrated, across several consecutive reads, that it *never* reaches the nominal floor does the check fall back to that board's own observed peak (tight 0.94 tolerance), plus an absolute 50%-of-nominal backstop so a genuine collapse toward idle is still caught. Boards that do reach the nominal floor keep today's behavior bit-for-bit — so the "stuck-at-mid" protection the deliberately-high 0.90 threshold was chosen for is preserved everywhere it is actually testable. The learned peak is discarded on every Max-mode entry and exit so it can never leak between holds.

**Risk direction is deliberately one-way:** this can only make OmenCore write to the EC *less* often, never more.

**Verified:** 6 new tests (`WmiFanControllerMaxModeHealthTests`) cover the `8A18` shape, preservation of the strict check on boards that reach the nominal floor, genuine-collapse detection through the backstop, cross-session peak isolation, telemetry-unavailable handling, and the RPM-fallback path.

**Still needs field confirmation** that the repeated re-assertion actually stops on the reporter's hardware. The logic bug is provable from the log alone; only they can confirm the audible/physical behavior matches.

---

## Fixed: Per-Model GPU Power Boost Capability Flag Was Dead Code on Every Victus Board

**Reported by:** Discord (`🐬🐬 🅰🌜𝓔`, HP Victus 16-d1176TX, board `8A25`, RTX 3060), corroborated by OsamaBiden — GPU Power Boost stays locked at base TGP (85W) and never reaches the 100W dynamic-boost ceiling OMEN Gaming Hub reaches on the same hardware.

**Root cause:** `SystemControlViewModel.DetectGpuPowerBoost()` short-circuited on `sysInfo.IsHpVictus == true` before ever consulting the model database. That blanket deny was added deliberately in v3.2.0 to fix a real bug (GitHub #89 — WMI probing on some Victus boards returned false-positive values that incorrectly enabled the UI and produced API errors on apply), and it is **not** new to 4.0.0. But applying it unconditionally made `ModelCapabilities.SupportsGpuPowerBoost` unreachable for every Victus board: even a field-verified entry declaring support could never take effect.

It also put this gate at odds with `DeviceCapabilities.ShowGpuPowerBoost`, which *does* consult the database — which is why the reporter's own diagnostics truthfully said `Show GPU Power Boost: Yes` for a board where the feature was hard-disabled in code.

**Fix:** the blanket deny remains the default for Victus, but an explicit per-model `SupportsGpuPowerBoost = true` opt-in now wins. **No Victus entry sets that flag today, so behavior is unchanged on every currently shipping board** and the #89 protection still applies. What changes is that the flag becomes meaningful again, and the two gates stop contradicting each other.

**Deliberately not changed — needs field evidence:** whether ProductId `8A25` deserves its own database entry, and whether the flag should be `true` for it. Next step is a full diagnostics export plus a second independent confirmation before flipping a GPU/TGP capability flag.

---

## Fixed: Startup Log Falsely Claimed "Found model by ProductId"

Found while tracing the report above, and it affects **every** triage that starts from a startup log.

`CapabilityDetectionService` reported the resolution source as `ProductId` for anything that wasn't an ambiguous-ID disambiguation — even when `GetPreferredCapabilities` had actually fallen through to a `ModelNamePattern` match on a *different* board's entry. Board `8A25` has no entry of its own and resolves to the `8A26` entry via its `"16-d1"` pattern, yet the log read `✓ Found model by ProductId: HP Victus 16 (2023/2024) d1xxx` — obscuring that the running board was never in the database at all.

**Fix:** the resolution source is now reported accurately. A pattern-based fallback logs `model-name pattern '16-d1' (no entry for ProductId '8A25')`, so future reports make the distinction visible instead of hiding it.

---

## Fixed: Board `8C2F` Described a 15" Laptop as a 16" Model

**Reported in:** [GitHub #155](https://github.com/theantipopau/omencore/issues/155) (VagapovDanil, Victus 15-fb2082wm).

HP reused board ID `8C2F` across both a 15" and a 16" Victus Ryzen chassis. The database entry — added from the 16-inch report in GitHub #110 — was named `HP Victus 16 (2024+) Ryzen r0xxx`, so a 15-fb2xxx machine matched it by exact ProductId and was then described as a 16" model throughout diagnostics.

**Fix (metadata only):** renamed to `HP Victus 15/16 (2024+) Ryzen (shared board)`, with notes recording both reports and stating explicitly that the capability flags were inferred from the 16" report and are **not** confirmed on the 15" chassis. No capability flags were changed, so there is no hardware risk.

**Still open:** whether fan/RGB/thermal behavior on the 15" chassis actually matches the 16" assumptions.

---

## Not Changed This Release (Deliberately)

- **[GitHub #154](https://github.com/theantipopau/omencore/issues/154) — HP ENVY 14-eb0xxx:** out of scope; an ENVY is not an OMEN or Victus board and its firmware exposes no thermal-profile/fan-target interface. Worth noting the reporter's diagnostics were among the most thorough received this cycle, should ENVY support ever be considered.
- **[GitHub #151](https://github.com/theantipopau/omencore/issues/151) — board `8D41` Darfon `0d62:54bf` keyboard RGB:** already fully tracked; still gated on the reporter's offered HID capture. The reporter has since confirmed HP's own OMEN Light Studio doesn't support this controller either, which is useful corroboration but doesn't unblock the work.
- **Discord (SprinkSponk, board `8D87`) — CPU package power caps at 71W vs. 105W via OGH:** logged, not yet traced. `PowerLimitController` clamps only to a generous 10-150W range, so the cap is coming from elsewhere (likely the AMD SMU path or firmware). Board `8D87` now has multiple open unconfirmed capability questions and would benefit from one consolidated field-evidence pass.
- **#153's underlying question of whether an external actor really resets fan state on board `8A18`:** the reassert *loop* is fixed, but whether anything genuinely drops the fan level remains unanswered without an EC register trace.

---

## One Existing Test Updated (Not Weakened)

`HotkeyAndMonitoringTests.CheckAndRecoverFrozenTemps_RateLimitsBridgeRestart_WhenTempsStayFrozen` started failing against the freeze-heuristic change, and it was right to: it fed a **completely static** sample (temperature *and* load both pinned) and expected a freeze to be detected. Under the new rule that is equilibrium, not a fault — which is precisely the false positive being removed.

The test's actual subject — that bridge restarts are rate-limited once a freeze *is* detected — remains valuable, so it was updated to model a genuinely stuck sensor: temperature pinned while load alternates 20%/45% (a 25-point swing). Same assertions, same rate-limiting coverage, valid premise. The reasoning is recorded in a comment at the test so the change isn't mistaken later for having been loosened to accommodate the implementation.

## Test Suite

**969/969 passing**, 0 build warnings across all projects (up from 953 at the 4.0.0 release). 16 new tests added across three files:
- `WmiFanControllerMaxModeHealthTests` (6) — the `8A18` unwinnable-floor shape, preservation of the strict check on boards that reach the nominal floor, genuine-collapse detection via the backstop, cross-session peak isolation, telemetry-unavailable handling, RPM fallback.
- `WmiBiosMonitorFreezeHeuristicTests` (7) — 100%-load and idle equilibrium (previously false-positived), wide-load-swing true positive, both threshold boundaries, absolute-ceiling backstop, unpopulated-sentinel safety.
- `DashboardTelemetrySourceTests` (3) — dashboard ignores the raw event, projects pushed normalized samples, treats a null push as a no-op.

All use this codebase's established reflection pattern for private-method coverage, and every one replays a shape taken from a real field log rather than a hypothetical.

## Runtime Verification

Beyond the test suite, the freeze-heuristic change was verified against real runtime behavior on the development machine, which reproduced the false positive on the pre-fix build:

| | Pre-fix build (`OmenCore_20260719_161729.log`) | Post-fix build (`OmenCore_20260725_153059.log`) |
|---|---|---|
| Runtime | ~8 min | 5 min 42 s (18 telemetry cycles) |
| First "appears frozen" warning | **3 min 14 s** | none |
| Bridge restarts triggered | **1** (at 4 min 35 s) | **0** |
| Errors | 0 | 0 |

The post-fix run deliberately ran past the 3m14s mark where the old build first warned, so the comparison clears that window rather than simply being too short to reach it.

**Still outstanding:** none of this substitutes for confirmation on the *reporters'* hardware — particularly for the fan reassert loop, where only they can confirm the audible behavior matches.
