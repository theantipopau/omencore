# OmenCore v4.2.0 – In Development

**Release Date:** TBD
**Release Status:** In development. Base version v4.1.7 (tagged and released 2026-08-16).
**Type:** Minor release — see `docs/ROADMAP_v4.2.0.md` for the full cycle plan. Three pillars: sensor-truth/fan-control accuracy, perceived performance and motion, and a typography move to Roboto Condensed. Also absorbs field-report bug fixes from v4.1.7 users as they arrive, same as prior cycles.
**Base Version:** v4.1.7
**Tracking doc:** `docs/ROADMAP_v4.2.0.md`

---

## Fixed: ACPI CPU Thermal Zone Selection Could Latch a Skin/Ambient Sensor Instead of the CPU

Reported on r/HPOmen (`CharmingMeasurement1`, 2026-08-16, on the v4.1.7 announcement thread): "It displays made-up temperatures — it can show the CPU at 36 degrees when it's over 80." Traced to `WmiBiosMonitor.GetAcpiCpuTemperature()`: on boards exposing multiple `MSAcpi_ThermalZoneTemperature` zones, the code picked whichever zone WMI enumerated *first* and latched that instance name permanently — only a later zone whose name contained `CPU`, `CPUZ`, or `TZ00` could displace it, and real HP zone names frequently match none of those. WMI enumeration order isn't a naming contract, so on affected boards a skin/chassis/ambient zone reading in the mid-30s could get latched at startup and reported as "CPU temperature" indefinitely — and since fan curves consume this same value, this was a fan-control input bug, not just a display bug.

**Fix:** extracted the selection logic into a pure, unit-tested `WmiBiosMonitor.SelectCpuThermalZone()`. New priority order: prefer whatever zone was previously confirmed by name if it's still present (stability); else prefer a zone whose name hints at CPU; else — only when genuinely ambiguous, no name match anywhere — pick the *hottest* of the unnamed zones, since the CPU is the one component here virtually guaranteed to run hottest under real load. That ambiguous pick is deliberately **never latched**: every subsequent poll re-evaluates from scratch rather than getting stuck on a single guess, so this can't reintroduce the same failure mode with a different trigger (e.g. a momentarily-warm non-CPU zone). Single-zone systems and boards where a zone already matches a name hint are unaffected — this only changes behavior for multi-zone, ambiguously-named systems, which is exactly where the bug lived. 14 new tests in `WmiBiosMonitorAcpiZoneSelectionTests.cs`, including a direct regression test reproducing the reported shape. No test coverage regression: the WMI query itself remains unmockable/untested as before (consistent with how this class's other WMI-backed reads are verified — build-clean + code review), but the actual selection algorithm — where the bug lived — is now fully covered.

Also added: whenever multiple zones are present and the selection changes, the reasoning is now logged (`ACPI CPU thermal zone: <name> (confirmed | ambiguous, using hottest reading) — all zones this poll: [...]`), so a future report on this class of bug is diagnosable from a log export instead of requiring a live repro.

**Not changed:** the existing `TryApplyCpuTemperatureFallback()` authority-mismatch safety net (probes a secondary LibreHardwareMonitor-backed reading when the cached temp looks implausibly low under real load) — investigated extending it to cover the `_cachedCpuTemp == 0` case this fix already closes, and found it would duplicate rather than complement this fix. That mechanism is untouched and remains the downstream safety net for cases this fix doesn't reach.

---

*(Further entries added as work lands.)*
