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

## Fixed: Linux GUI Fan-Control Warning Pointed Users at a Workaround That Doesn't Work on Hwmon-Only Boards

Investigated while re-checking the roadmap's premise that OmenCore silently does nothing on profile-only boards (Reddit, `Fennel-Extra`, OMEN Transcend 14: "the program never adjusted the fan speed... led to overheating"). That premise turned out to be stale on Windows — the "Profile-only" badge and explanation there have been live since the 4.0.0 cycle — but tracing the equivalent Linux GUI (`omencore-gui`) path surfaced a real, separate bug.

`FanControlViewModel`'s capability warning banner showed one hardcoded message for every "profile-only" board: *"Use System Control performance profiles for cooling behavior."* For boards with a genuine ACPI `thermal_profile`/`platform_profile` path, that's accurate. For boards reaching profile-only status purely via a coarse `hp-wmi` hwmon `pwm_enable` toggle (auto/full, no per-fan duty write) — confirmed via [GitHub #99](https://github.com/theantipopau/omencore/issues/99)'s attached diagnostics for board `8E41` (OMEN Transcend 14-fb1xxx), which shows `Thermal Profile Control: ✗ Missing` — that advice is a dead end: `SetPerformanceModeAsync` has no thermal path to resolve and falls through to `powerprofilesctl` or throws outright, with nothing fan-relevant to do either way.

Meanwhile the one thing that **does** work on that exact board class was never mentioned: `SetCpuFanSpeedAsync`/`SetGpuFanSpeedAsync` already fall back to the coarse hwmon `pwm_enable` full-speed/auto toggle (the GitHub #174 fix from v4.1.7) before any profile-based path, and that call isn't gated behind curve-editing support — so Max Fan / Emergency Stop is a real, working override the warning banner should have said so.

**Fix:** `LinuxCapabilityClassifier` already computes a correctly-differentiated `Reason` string per situation, and it was already plumbed all the way to `SystemCapabilities.FanControlCapabilityReason` — just never read. `FanControlViewModel.InitializeCapabilitiesAsync()` now uses that real reason instead of the generic per-class string, and appends a truthful note that Max Fan still works as a coarse override — only for the `profile-only` class, where a real write path is confirmed to exist; never claimed for `telemetry-only`/`unsupported-control`, where none does. Pure messaging fix, no control-behavior change. Build-verified only: no automated test project exists for this target and there's no Linux/OMEN hardware in the development environment, consistent with how this Linux GUI's other fixes have been verified this cycle.

**Not claimed to resolve the original Reddit report** — that reporter didn't specify Windows or Linux, and without their diagnostics this can't be conclusively tied to board `8E41` or GitHub #99. Recorded as a real, separate bug found while investigating, not as a fix for the report that prompted the investigation.

---

## Added: CPU Temperature Source Is Now Visible, Instead of Computed and Discarded

Continuing the sensor-truth work from the ACPI thermal-zone fix above: `WmiBiosMonitor` has tracked `CpuTemperatureAuthoritySource`/`CpuTemperatureAuthorityReason` internally for a while — which of WMI BIOS, ACPI Thermal Zone, or the LibreHardwareMonitor fallback is currently trusted, and why. Checking the roadmap's own premise before building anything found it was already out of date: these weren't "shown only in deep diagnostics" as previously written, they were read *nowhere at all* — not the UI, not even the diagnostics export.

**Fix:** added `CpuTemperatureSource`/`CpuTemperatureSourceReason` to `MonitoringSample`, populated from the existing internal fields in `WmiBiosMonitor.BuildSampleFromCache()`. The Dashboard's CPU temperature chip now shows this as a tooltip (`DashboardViewModel.CpuTemperatureSourceTooltip`), and a small warning glyph appears when the trusted source is the `LHM Fallback` path — meaning the primary WMI/ACPI reading was recently rejected as implausible for the observed load/power and a secondary sensor is being trusted instead (`IsCpuTemperatureSourceFallback`). Also fixed the diagnostics export's `[CPU Temperature Authority]` section, which — despite the name — only ever printed the overall monitoring backend (`MonitoringSource`/`Health`/`LastSampleAgeSeconds`), never the actual per-tick sensor authority; it now reports both, alongside a new `CPU Temp Source` line in `hardware-info.txt`.

Pure additive UI/diagnostics surfacing of already-correct backend data — no control or detection behavior changed. 8 new tests (`DashboardViewModelCpuTemperatureSourceTests.cs`), plus 2 new assertions in the existing `MonitoringSampleCopyConstructorTests.cs`. Full suite: 1310/1310.

**Not done in this pass:** a guided-diagnostics side-by-side comparison of every available temperature source. Still open on the roadmap.

## Added: OSD Now Marks a Fallback CPU Temperature Source Too — as a Glyph, Not a Tooltip

Follow-up to the Dashboard change above. The original plan called for an "OSD tooltip" mirroring the Dashboard's — checked that against the actual window before building and found it wouldn't have worked at all: `OsdOverlayWindow` applies `WS_EX_TRANSPARENT` unconditionally (one call site, no toggle anywhere), so it's genuinely click-through at all times. A hover tooltip on a window that never receives mouse input would be permanently unreachable during the one situation the OSD exists for — actually being on screen during a game.

**Fix:** added `IsCpuTempSourceFallback` to `OsdOverlayWindow` (same "is the trusted source `LHM Fallback`" check used on the Dashboard), driving a small always-visible `~` marker next to the CPU temperature reading instead of a tooltip — glanceable at a glance, no interaction required, minimal added visual weight. No dedicated test added: this window has no existing test coverage for any of its other `UpdateStats()`-assigned properties either (including `CpuTemp` itself) — a real WPF `Window` with Win32 interop in its constructor, consistent with why this class sits outside this project's automated test coverage entirely. Build-verified only, matching the established tier for this file.

---

*(Further entries added as work lands.)*
