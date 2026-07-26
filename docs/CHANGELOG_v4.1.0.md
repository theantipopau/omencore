# OmenCore v4.1.0 – Field-Report Fixes, Diagnostics Overhaul, and Real-Log Bug Hunting

**Release Date:** TBD
**Release Status:** Code-complete, test-verified (990/990 tests, 0 build warnings across all projects), merged to `main`, and artifacts built and hashed (see SHA256 hashes below), plus runtime verification of the freeze-heuristic fix against a machine that reproduced the false positive (see Runtime Verification). **No physical-hardware confirmation yet** from the original reporters; the fan reassert-loop fix in particular needs their confirmation that the audible behavior matches.
**Type:** Minor release — targeted fixes for post-4.0.0 field reports, the architecture/accuracy issues found while tracing them, and a second pass driven by reading real users' diagnostics exports and application logs directly
**Base Version:** v4.0.0
**Tracking doc:** `docs/ROADMAP_v4.0.0.md` — see "Newly Reported (Post-4.0.0 Release): Field Reports Triaged 2026-07-25" onward for the full traces this release acts on.

## SHA256 Hashes

```text
2FB3F301F896EAFE6E8034493C4CE83F7AB95ED7ED207814B6DCB799A97501F8  OmenCoreSetup-4.1.0.exe
39B24556EDE3001006E3AD48F8EB4978F92CEDF31B1C9C765AB8C8508590403A  OmenCore-4.1.0-win-x64.zip
0BB93B570C627764632A0CC7E2759DE88621F58808DBA31111B54BEF4AF1F933  OmenCore-4.1.0-linux-x64.zip
```

Also published as `artifacts/SHA256SUMS-4.1.0.txt`. Verify with `Get-FileHash <file> -Algorithm SHA256` (Windows) or `sha256sum <file>` (Linux) before installing.

---

## Purpose

4.0.0 shipped, and five GitHub issues plus two Discord threads arrived against it. Tracing them turned up four real, provable defects — three of which had been misleading users into believing hardware telemetry was broken, and one of which had OmenCore fighting its own firmware for minutes at a time — plus two accuracy problems in diagnostics that were actively hindering triage of those same reports.

A second pass then swept older bug-report docs for anything that never made it into the 4.0.0 consolidation, which turned up a systemic bug in Power Automation and a chance to add real diagnostic instrumentation for a still-unresolved thermal-safety report. A third pass went further still: reviewing real `omencore-diagnostics-*` exports and raw application logs from actual users (not synthetic tests) directly surfaced four more real, previously-undiscovered bugs — two diagnostics collectors that had never worked in any export ever produced, a Logitech RGB write path that silently did nothing while claiming success, and a thermal-authority selector that flip-flopped nearly 200 times in one real session.

Every change here is either pure UI/display, a provable logic bug, or metadata/diagnostics. Nothing widens hardware-control surface; the fan-control change moves strictly in the direction of *fewer* EC writes, and the thermal-authority fix moves strictly in the direction of *fewer* source switches.

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

## Fixed: Board `8D41` Light Bar Zones Silently Failed on Linux Through the Wrong sysfs Path

**Reported in:** [GitHub #151](https://github.com/theantipopau/omencore/issues/151) (Nefreyu, board `8D41`, Darfon `0d62:54bf` keyboard) — reporter proved zones 0-3 (the chassis light bar) are writable via raw sysfs on this board, but `omencore-cli` never lit them.

**Root cause:** `LinuxKeyboardController.cs` only ever wrote to `/sys/devices/platform/hp-wmi/zoneN_color`. This board's community driver instead exposes zone control through a separate `hp-rgb-lighting` platform device, with plain `zoneN` filenames (no `_color` suffix) — a different sysfs device entirely, not a naming variant of the same one.

**Fix:** `SetZoneColor` now tries the existing `hp-wmi/zoneN_color` path first (unchanged for every board that already worked), and falls back to `hp-rgb-lighting/zoneN` when that path doesn't exist. `HasZoneControl`/`IsAvailable` detection extended the same way. This covers only the light-bar zones the reporter already confirmed are writable — it does not touch keyboard zones 4-7, which reach a separate Darfon USB HID controller and remain correctly gated behind the field HID-capture work below.

**Verified:** build clean, 0 warnings. No test project exists for `OmenCore.Linux` (no Linux hardware or CI runner available in this environment), so this is code-review-verified only, not a live sysfs write — noted here rather than overclaimed.

---

## Not Changed This Release (Deliberately)

- **[GitHub #154](https://github.com/theantipopau/omencore/issues/154) — HP ENVY 14-eb0xxx:** out of scope; an ENVY is not an OMEN or Victus board and its firmware exposes no thermal-profile/fan-target interface. Worth noting the reporter's diagnostics were among the most thorough received this cycle, should ENVY support ever be considered.
- **[GitHub #151](https://github.com/theantipopau/omencore/issues/151) — board `8D41` keyboard zones 4-7 (Darfon HID controller):** the light-bar half of this report is fixed above; the keyboard-zone half still needs the reporter's offered USB HID feature-report capture before a backend can be written.
- **Discord (SprinkSponk, board `8D87`) — CPU package power caps at 71W vs. 105W via OGH:** traced this cycle (see below) — root cause found, fix identified, deliberately not applied pending field confirmation.
- **#153's underlying question of whether an external actor really resets fan state on board `8A18`:** the reassert *loop* is fixed, and the diagnostics collector that should have been able to answer this (`LastMaxModeExternalResetUtc`/`Details`) is now fixed too (see below) - but the question itself remains unanswered until a reporter's next export actually shows a real reset event or confirms none occurred.

---

## Traced, Not Fixed: Board `8D87` CPU Power Ceiling (71W vs. 105W via OGH)

**Reported by:** Discord (SprinkSponk, OMEN MAX 16z-ak000, board `8D87`, AMD) — CPU package power hard-caps at 71W where OGH reaches 105W on the same hardware.

**Root cause found.** Board `8D87` sets `SupportsFanControlEc = false` (2025 MAX-family EC register layout diverges from legacy — the same reason every 2025 MAX board disables direct EC writes) and has no `PerformanceCpuPl1Watts`/`PerformanceCpuPl2Watts` override, so `PerformanceModeService.Apply()`'s EC power-limit path is blocked for this board. The intended escape hatch for exactly this case, `AllowDecoupledWmiThermalPolicyFallback`, is set `true` on every other 2025 MAX-family board with EC writes disabled — `8D41` and `8D42`, both Intel — but **`8D87` and its AMD sibling entry `AK0003NR` are the only two 2025 MAX boards in the database that don't set it.** With neither path enabled, a performance-mode switch on this board currently does nothing to CPU/GPU power beyond the Windows power plan, so the firmware's own default cap simply never gets overridden — consistent with OGH (which likely drives the same WMI thermal-policy path itself) reaching 105W while OmenCore touches nothing.

**Not applied this release.** The fix is a one-line addition per entry (`AllowDecoupledWmiThermalPolicyFallback = true` on `8D87` and `AK0003NR`), but it is a genuine hardware-behavior change — it starts sending a WMI write on every mode switch for these boards where today nothing is sent — so per this project's evidence-gate rule it needs field confirmation, not just code-level confidence. Unlike the `8D41`/`8D42` siblings (`UserVerified = true`), both `8D87` and `AK0003NR` are `UserVerified = false`; the WMI fallback reaching higher wattage on the AMD SMU path is a strong hypothesis from symmetry with the Intel siblings, not a confirmed mechanism.

**Next step:** get a reporter to test a build with the flag flipped and confirm CPU power actually rises with no adverse thermal/fan/stability effects, before merging. Full detail in `docs/ROADMAP_v4.0.0.md`.

---

## Fixed: Power Automation Never Actually Applied CPU/GPU Wattage on AC/Battery Transitions

**Originally reported as:** a v3.8.1-era bug (`docs/3.8.1-BUG-REPORTS.md` BUG-3820-005 item 2) that never made it into the 4.0.0 roadmap consolidation — "Battery profile set to 'Silent' applies as 'Custom' or 'Balanced' instead," on OMEN MAX 16 `8D41`. Found by re-sweeping older bug-report docs for anything that fell through the cracks.

**Root cause — much bigger than the original report scoped.** `PowerAutomationService.ApplyPowerProfile()` (the opt-in AC/Battery profile switcher) applied performance mode by building `new PerformanceMode { Name = perfMode }` and calling `Apply()` directly — a bare object with `CpuPowerLimitWatts = 0` and `GpuPowerLimitWatts = 0`. `PerformanceModeService.Apply()`'s own "both limits non-positive" guard then skips the EC power-limit step entirely unless the model defines a per-mode wattage override — **only 2 of 59 models in the database do.** So for the other 57, on both AC and Battery transitions, this feature's performance-mode step silently changed nothing but the fan preset. The 8D41 "Silent" report was the visible edge of this: `"Silent"` (the literal config default) matches no board's real mode-name list either (only 3 boards use `"Quiet"` literally, none use `"Silent"`), so the UI's current-mode indicator also silently failed to update, leaving whatever was last shown — "Custom" or "Balanced," exactly as reported.

**Fix:** both the primary apply and the failure-path rollback now call `PerformanceModeService.SetPerformanceMode(string)` — an already-existing method that normalizes aliases and supplies real wattage, and the exact same entry point manual UI mode-selection already uses. This isn't new EC-write logic; it's removing an accidental bypass of an already-proven-safe path. `DirectEcPowerLimitWritesBlocked` still gates boards where EC writes are considered unsafe, unchanged. Power Automation remains strictly opt-in, so only users who already enabled it are affected — and the effect is that the feature now does what it always claimed to do.

**Still open:** the UI current-mode indicator may still lag after an automated transition on boards whose mode-name list doesn't include a literal alias target (e.g. `8D41`'s `Cool`, `AK0003NR`'s `L5P`) — the applied wattage is correct regardless, but display sync on those specific boards isn't fixed this pass.

**Verified:** 1 new test (`PowerAutomationServiceApplyCurrentProfileTests.ApplyCurrentProfile_AppliesRealWattage_NotBareZeroWattObject`) pins non-zero CPU/GPU wattage and a normalized mode name after a Power-Automation-triggered apply. Full suite green, 0 warnings.

---

## Added: Diagnostic Warning for Sustained High-Temp / Low-RPM Anomaly (GitHub #143)

**Originally reported as:** `docs/3.8.1-BUG-REPORTS.md` BUG-3810-002 — Victus 15 `8DCD`, Performance-mode fan reportedly collapsing from ~5000 RPM to below 2000 RPM while CPU stayed above 80°C for roughly five minutes, recovering only after a manual mode change. Flagged thermal-safety-critical; explicitly gated on a field reproduction with `wmi-command-history.txt`/`tuning-fan-focus.txt` evidence before any control-policy change — that gate is correct and unchanged.

**What shipped instead of a fix:** the report's own "Required Automated Coverage" line called for exactly this — "a sustained high-temperature/unexpectedly-low-RPM state emits an actionable warning and never reports the requested mode as verified solely from command success" — and that's buildable without field evidence, since it's pure diagnostics. `HardwareMonitoringService.CheckForUnexpectedLowRpmAtHighTemp` now watches every monitoring sample and warns once per sustained window when a `TelemetryDataState.Valid` RPM reading (CPU or GPU fan, checked independently) stays below 2000 RPM while that side's temperature stays at or above 80°C for 5+ consecutive polls. Both thresholds are the reporter's own numbers.

**This changes nothing about fan control.** It doesn't read the requested mode/preset, doesn't touch the EC or WMI, and doesn't influence any decision the fan curve engine makes — it only compares temperature to RPM already present on the sample and logs. Readings that aren't `TelemetryDataState.Valid` (Zero/Stale/Unavailable/Invalid) are skipped entirely rather than treated as evidence either way, so boards without real RPM telemetry won't generate false alarms.

Also checked `8DCD`'s own `ModelCapabilityDatabase.cs` entry while investigating: it already has `SupportsFanControlEc = false` and `AllowDecoupledWmiThermalPolicyFallback = true`, the correct conservative profile, and the report describes the collapse happening in Performance mode rather than Max mode, so this release's earlier Max-mode-health fix (see the #153 section above) doesn't apply here. No separate bug found in this board's database entry.

**Verified:** 7 new tests (`HardwareMonitoringUnexpectedLowRpmTests`) cover: the warning firing after the sustained threshold; staying silent below that threshold; staying silent when only temperature or only RPM is anomalous (not both); staying silent on non-`Valid` RPM state; the consecutive-count resetting on recovery; and firing only once per anomaly window rather than spamming the log. Full suite green, 0 warnings.

**Still open:** the actual root cause of #143. This only ensures the next reproduction's log carries unambiguous evidence instead of silence.

---

## Real Field Diagnostics Reviewed, Two More Broken Collectors Found and Fixed

While the RAM investigation above was framed around a fresh synthetic 3-minute test on this dev machine, the project owner pointed at five `omencore-diagnostics-*` export folders sitting in their Downloads directory — real bug-report attachments from users, collected for triage, spanning v3.8.0 through v4.0.0. (These were initially mischaracterized as "the project owner's own laptop" before checking the `Config source` paths, which show four distinct Windows usernames across the five exports — corrected once noticed.) One happens to be board `8C2F`, the exact board fixed earlier in this release for #155; three others turned out to be board `8A18`, directly relevant to the #153/#152 fixes above. Reviewing them directly surfaced a much better answer to PERF-3810-001, and two more collectors from the same "wiring never reached this file" bug class as the `wmi-command-history.txt` fix in 3.8.1.

### PERF-3810-001 confirmed with real data (superseding the earlier synthetic estimate)

`resource-footprint.txt` in each export already captures real `Process` working-set/private-bytes for both the main app and the separate `OmenCore.HardwareWorker` process — this collector was never broken. Across the five real sessions: main app working set ranged **355.6-705.0 MB**, plus **47.6-174.2 MB** for the hardware-worker process — combined **464-870 MB**. This confirms the >400 MB complaint as real and, in the worst observed session, more than double the reported threshold — considerably worse than the earlier synthetic tray-only 3-minute test (314-337 MB) suggested. No single subsystem cleanly explains the swing (the highest reading was actually a session with `LibreHardwareMonitor: not loaded`), so no code change was made here — this is confirmation, not a fix. Isolating the actual contributor needs a memory-profiler comparison across sessions, not more log-reading.

### Fixed: `system-info.txt` mislabeled the .NET managed heap as "RAM"

Cross-referencing `system-info.txt`'s `RAM: {n} MB` line against the same exports' `resource-footprint.txt` `[Managed Runtime] ManagedMemoryMB` line showed near-identical numbers in every export (e.g. 38.1 vs 37, 51.5 vs 51) — confirming `RAM:` was reporting `GC.GetTotalMemory(false)`, the .NET managed heap at that instant, not installed system memory. That's why it swung 16-51 MB across exports on the *same physical laptop* and read as alarming nonsense on first look. Fixed to query real installed physical memory via WMI (`Win32_ComputerSystem.TotalPhysicalMemory`, same technique `WmiBiosMonitor.GetTotalPhysicalMemoryGB()` already uses), now labeled `Installed RAM:`.

### Fixed: `hardware-info.txt` and `ec-state.txt` were placeholders in every single export, across all five OmenCore versions

Both files read the exact same byte-identical placeholder text ("Hardware monitoring not available" / "EC access not available") in 100% of the real exports reviewed — direct field confirmation, not speculation. Root cause: `CollectAndExportAsync`'s `ecAccess`/`hwMonitor` parameters had no constructor-level `?? _field` fallback, unlike `monitoringService`/`fanService`/`wmiController` (which got exactly this treatment for `wmi-command-history.txt` in 3.8.1) — neither production call site (Settings "Export Diagnostics", "Report Model") could ever populate them.

**Fix:** added an `IEcAccess? ecAccess` constructor parameter with the same fallback pattern, wired to `MainViewModel._ecAccess` at both construction sites. `hardware-info.txt`'s collector no longer requires a raw `LibreHardwareMonitorImpl` (unreachable from any production call site, and coupled to one specific `IHardwareMonitorBridge` implementation) — it now reads a new `HardwareMonitoringService.LastSample` property, the latest sample updated on every successful monitoring tick, independent of the UI-facing `Samples` history's throttling/Dispatcher requirements.

**Verified:** 4 new tests — `SystemInfoFile_ReportsInstalledPhysicalMemory_NotManagedHeap`, `HardwareInfoFile_ShowsPlaceholder_WhenNoMonitoringServiceAvailable`, `HardwareInfoFile_ReportsRealSample_WhenMonitoringServiceHasOne`, `EcStateFile_ShowsPlaceholder_WhenNoEcAccessAvailable` — plus the full existing `DiagnosticExportSnapshotTests` suite (33 tests total in that file) re-verified green. Full suite green, 0 warnings.

### Process note: full-suite runs were crashing intermittently, traced to this session's own new tests

Running the complete suite twice this cycle produced "Test host process crashed" partway through (a background exception from an undisposed `HardwareMonitoringService`'s monitor loop, thrown well after the originating test had already passed and moved on). Traced to two of this session's own new test files (`HardwareMonitoringUnexpectedLowRpmTests`, and the new tests added to `DiagnosticExportSnapshotTests` above) constructing `HardwareMonitoringService` — which starts a background polling loop on construction and implements `IDisposable` specifically to stop it — without disposing it. Fixed both to dispose properly. A clean full-suite re-run afterward passed 981/981 with no crash, confirming this was the cause. Not a pre-existing issue release-worthy of its own note, except as a reminder for future tests in this codebase: always dispose a constructed `HardwareMonitoringService`.

---

## Fixed: Fan-Controller Ownership Diagnostics Were Reading the Wrong Object (Directly Relevant to #153)

Continuing to review real diagnostics exports (this time from three additional real users, one of them on board `8A18` — the exact board #153/#152 concern) surfaced a fourth instance of the "diagnostic collector never reaches real data" bug class, and this one is the most consequential yet: it's the exact evidence source that could answer #153's still-open question of whether something external genuinely resets fan state on that board.

**Root cause:** `core-control-readiness.txt`, `monitoring-cadence-hold.txt`, `tuning-fan-focus.txt`, and `wmi-command-history.txt` all read `IsManualControlActive`, `CommandsIneffective`, `VerifyFailCount`, `LastMaxModeExternalResetUtc`, and `LastMaxModeExternalResetDetails` via reflection off `wmiController` — which is `HpWmiBios`, the raw WMI command layer. **`HpWmiBios` does not have any of these properties.** They live on the fan controller (`WmiFanController`/`WmiFanControllerWrapper`) instead — the exact class this release already touched for the #153 max-mode-health fix. `HpWmiBios` does correctly have `IsAvailable`/`Status`/`FanCount`/`GetCommandHistory()`, which is why those specific fields *did* work and why the bug went unnoticed — the file wasn't a placeholder, just partially wrong. Confirmed across every real export checked (multiple users, multiple boards, multiple versions): these five fields were always `<unavailable>`, regardless of hardware.

**Fix:** extended the `IFanController` interface with these five members as safe-default properties (`=> false`/`0`/`null`, matching the existing `IsHoldActive => false` pattern for backends without the concept), so every existing implementation keeps compiling with zero changes. Added the two missing forwards (`IsManualControlActive`, `VerifyFailCount`) to `WmiFanControllerWrapper` (it already forwarded the other three). Exposed the real controller via a new `FanService.Controller` property, and switched all four collectors to read from `fanService.Controller` instead of `wmiController` for these fields specifically — `IsAvailable`/`Status`/`FanCount`/command-history collection are untouched and still correctly read from `wmiController`. Also un-gated `tuning-fan-focus.txt`'s ownership fields from requiring a non-null `wmiController` at all, since the two objects aren't strictly coupled.

**This changes no fan-control behavior whatsoever** — every touched property is a read-only diagnostic getter already computed by the existing Max-mode-health tracking; nothing new is written to the EC/WMI, and no control decision is affected.

**Verified:** 3 new tests (`BuildCoreControlReadinessReport_FanOwnershipFields_ComeFromFanControllerNotWmiController`, `MonitoringCadenceHoldFile_FanOwnershipFields_ComeFromFanControllerNotWmiController`, `TuningFanFocusFile_FanOwnershipFields_ComeFromFanControllerNotWmiController`) pin that all four files now show the fan controller's real values (or its new safe defaults) instead of the old wiring-gap placeholder. Full suite (984/984) green, 0 warnings — one full-suite run hit the pre-existing "Test host process crashed" flakiness described above (this time from a different, unrelated undisposed `FanService` elsewhere in the wider test suite, not this session's own new tests, which all correctly dispose via `using`); a clean re-run confirmed 984/984 with no crash.

**Still open:** the underlying #153 question itself. This only means the *next* diagnostics export from an `8A18` reporter (or anyone else) will actually carry the evidence needed to answer it, instead of silence.

---

## Fixed: Logitech HID++ Fallback Was Unreachable, Silently Reporting False Success

Found by reading a raw application log (not just the summary files) inside one of the same real diagnostics exports — a ~73-minute session on board `8A18`. It logged **960 "HID write failed"/"HID effect write failed" warnings in a single session**, almost all clustered into a ~3-minute window while the user was testing RGB scenes/effects — roughly 5-6 failures per second, not slow background noise.

**Root cause:** `LogitechHidDirect.cs`'s `SendColorCommand`/`SendEffectCommand` always try an HID++ 2.0 write first, with the HID++ 1.0 fallback **nested inside the same `try` block**. When HID++ 2.0 throws — which it always did for this user's device (a `G715 Wireless`/`Lightspeed Receiver` pair that only speaks HID++ 1.0) — execution jumps straight to the outer `catch`, so the HID++ 1.0 fallback never runs. Worse, `SendColorCommand`'s outer catch swallowed the exception without rethrowing, so `ApplyStaticColorAsync` logged `"Applied lighting ... via direct HID"` as unconditional success. The log makes this visible in sequence: `HID write failed` → `Applied lighting #E6002E ... via direct HID` → `Applied color #E6002E @ 100% to G715 Wireless` — the last two lines both false. For this device, nothing was ever actually written to the keyboard/mouse, but the app reported success every single time.

**Fix:** restructured both methods so HID++ 1.0 is a genuinely independent second attempt (its own `try`, its own stream), reached only when HID++ 2.0 actually fails. Added a per-device `HidPlusPlus2Supported` tri-state flag so a confirmed-unsupported device skips the doomed HID++ 2.0 write (and its warning) on every later call instead of repeating both forever — cutting the spam to at most one warning per device per session. Success is now only reported when a write genuinely succeeded via either protocol; genuine failure now logs at `Error`, not silently. Also removed `SendEffectCommand`'s internal "HID++ 1.0 effect" attempt entirely rather than making the same dead branch reachable and guessing at undocumented behavior — animated effects have no HID++ 1.0 equivalent, and the caller (`ApplyBreathingEffectAsync` etc.) already has a correctly-labeled fallback to static color for exactly this case.

**No test coverage added** — `LogitechHidDirect` has no existing tests and depends on HidSharp's concrete `HidDevice`/`HidStream` types (real USB HID I/O, not mockable without a stream abstraction this class doesn't have), so this is code-review-verified only. Flagging that rather than overclaiming.

**Scope note:** confirmed in one real user's session, not cross-confirmed across multiple reporters like the diagnostics-collector fixes above — still real and reproducible straight from the log's own evidence, just a narrower evidence base than the others in this release.

---

## Fixed: CPU Thermal Authority Switching Had Asymmetric Debounce (~192 Flip-Flops in One Real Session)

Found by aggregating `[WmiBiosMonitor] CPU thermal authority switched:` lines across the same batch of real logs. One session alone logged **~192 authority transitions** in a single run, the large majority `LHM Fallback <-> ACPI Thermal Zone` flip-flops. This is the mechanism that decides which of WMI BIOS / ACPI Thermal Zone / LHM fallback is authoritative for the reported CPU temperature — the number fan curves consume via `Math.Max(cpuTemp, gpuTemp)` — not the freeze-detection heuristic already fixed this cycle.

**Root cause:** only the "returning to WMI BIOS" transition was debounced (3 consecutive confirming readings). Every other transition switched on a single reading: accepting ACPI Thermal Zone had no confirm-count gate at all, and entering LHM Fallback triggered as soon as the fallback reading differed from the current one by `>= 1.0°C` — a threshold ordinary cross-sensor noise crosses easily given HP WMI's whole-degree reporting. Two sensors sitting near that boundary during steady load would ping-pong the authority back and forth indefinitely, exactly matching the observed pattern.

**Fix:** generalized the WMI-only debounce into a symmetric `RequestCpuTemperatureAuthority` mechanism — any transition to a different source now needs 3 consecutive proposals of that same candidate before committing, matching the protection WMI-return already had. `ResetPendingCpuAuthorityIfMatches` clears an in-progress confirmation when an intervening tick proposes the opposite direction, so non-consecutive proposals can't accumulate into a false confirmation. Two transitions deliberately bypass this and still switch immediately: a model-specific override (an intentional configuration choice, not noise) and the hard-read-timeout reuse path (no alternative reading exists to wait for). Direction of risk is one-way — this can only make switches less frequent, never more.

**Verified:** 6 new tests (`WmiBiosMonitorTests`) cover no-switch-before-3-confirmations, switch-after-3, a non-matching interruption resetting the count, an already-active source updating its reason without debounce, and both directions of the explicit reset helper.

---

## One Existing Test Updated (Not Weakened)

`HotkeyAndMonitoringTests.CheckAndRecoverFrozenTemps_RateLimitsBridgeRestart_WhenTempsStayFrozen` started failing against the freeze-heuristic change, and it was right to: it fed a **completely static** sample (temperature *and* load both pinned) and expected a freeze to be detected. Under the new rule that is equilibrium, not a fault — which is precisely the false positive being removed.

The test's actual subject — that bridge restarts are rate-limited once a freeze *is* detected — remains valuable, so it was updated to model a genuinely stuck sensor: temperature pinned while load alternates 20%/45% (a 25-point swing). Same assertions, same rate-limiting coverage, valid premise. The reasoning is recorded in a comment at the test so the change isn't mistaken later for having been loosened to accommodate the implementation.

## Test Suite

**990/990 passing**, 0 build warnings across all projects (up from 953 at the 4.0.0 release — 37 new tests this cycle):
- `WmiFanControllerMaxModeHealthTests` (6, new file) — the `8A18` unwinnable-floor shape, preservation of the strict check on boards that reach the nominal floor, genuine-collapse detection via the backstop, cross-session peak isolation, telemetry-unavailable handling, RPM fallback.
- `WmiBiosMonitorFreezeHeuristicTests` (7, new file) — 100%-load and idle equilibrium (previously false-positived), wide-load-swing true positive, both threshold boundaries, absolute-ceiling backstop, unpopulated-sentinel safety.
- `DashboardTelemetrySourceTests` (3, new file) — dashboard ignores the raw event, projects pushed normalized samples, treats a null push as a no-op.
- `PowerAutomationServiceApplyCurrentProfileTests` (1 new test in an existing file) — a Power-Automation-triggered apply produces real, non-zero CPU/GPU wattage and a normalized mode name, not a bare zero-watt object.
- `HardwareMonitoringUnexpectedLowRpmTests` (7, new file) — the sustained high-temp/low-RPM warning firing after threshold, staying silent below it, staying silent when only one of the two conditions holds, staying silent on non-`Valid` RPM state, counter reset on recovery, single-fire-per-window.
- `DiagnosticExportSnapshotTests` (7 new tests in an existing file) — `system-info.txt` reports installed physical RAM not the GC heap; `hardware-info.txt`/`ec-state.txt` placeholder and real-data states; the fan-controller-ownership fields in `core-control-readiness.txt`/`monitoring-cadence-hold.txt`/`tuning-fan-focus.txt` come from the fan controller, not `wmiController`.
- `WmiBiosMonitorTests` (6 new tests in an existing file) — the CPU-thermal-authority debounce: no switch before 3 confirmations, switch after 3, a non-matching interruption resetting the count, an already-active source updating its reason without debounce, both directions of the explicit reset helper.

`LogitechHidDirect.cs`'s HID++ fallback fix has no test coverage (no existing test infrastructure for this class; depends on HidSharp's concrete, non-mockable `HidDevice`/`HidStream` types) — noted rather than overclaimed.

All tests use this codebase's established reflection pattern for private-method/private-field coverage, and every new test replays a shape taken from a real field log or diagnostics export rather than a hypothetical.

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
