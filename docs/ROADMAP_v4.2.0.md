# OmenCore v4.2.0 Roadmap — Trust the Numbers, Then Make It Feel Fast

**Status:** Planning. Opened 2026-08-16, immediately after v4.1.7 was built and staged.
**Base version:** v4.1.7
**Predecessor doc:** `docs/ROADMAP_v4.0.0.md` — carried the 4.0.0 → 4.1.7 cycle. Items still open there are rolled forward into this document (see "Rolled Over From v4.0.0" below) and that document should be treated as historical record from here on.

---

## Why This Cycle Exists

v4.1.x was overwhelmingly a *correctness* cycle: safety gates, silent-failure fixes, honesty passes, and sixteen reviewed community PRs. It made the app tell the truth more often. It did not make the app **feel** good to use, and it did not fix the single thing users judge a fan-control app on: **is the temperature it shows me real?**

Two pieces of public feedback (r/HPOmen, 2026-08-16, on the v4.1.7 announcement thread) frame this cycle better than any internal audit did. Both are quoted in full because both are, on investigation, **substantially correct** — and one of them is correct in a way the codebase already knew about and had gated rather than solved.

> **/u/Fennel-Extra** — "I tried to use it several times on my Omen Transcend 14. But I always got bored just for two reasons. 1. The app is feels extremely slow to move. Feels even laggier than the OGH. 2. The fan controls are just completely crazy and don't really work as intended. For instance, I played any game and the program never adjusted the fan speed so it led to overheating and fps loss. Are they fixed now?"

> **/u/CharmingMeasurement1** — "For me, this app is clunky. It displays made-up temperatures—it can show the CPU at 36 degrees when it's over 80. The fan controls are illogical. I recommend Omenmon-reborn, where you can manually control the fans with 1-degree precision."

Neither of these is a vague vibe complaint. Each maps to a specific, locatable defect or design gap. That mapping is the backbone of this roadmap.

### What the feedback actually maps to

**"Shows the CPU at 36 degrees when it's over 80" — traced to a real, specific bug.**

`WmiBiosMonitor.GetAcpiCpuTemperature()` (`src/OmenCoreApp/Hardware/WmiBiosMonitor.cs:2525`) enumerates `MSAcpi_ThermalZoneTemperature` and **latches the first thermal zone it encounters** into `_cpuThermalZoneInstance`, permanently for the process lifetime:

```csharp
if (_cpuThermalZoneInstance == null)
{
    // First valid zone — use it as default
    bestTemp = tempC;
    _cpuThermalZoneInstance = instanceName;
}
```

A later zone can only displace that latch if its instance name contains `CPU`, `CPUZ`, or `TZ00`. Real HP ACPI zone names frequently match none of those (`\_TZ.TZ01`, `THRM`, `ACPI\ThermalZone\THM0_0`), and on many laptops the first-enumerated zone is a **skin/chassis/ambient** sensor that genuinely sits in the mid-30s while the CPU package is at 80°C+. WMI enumeration order is not contractually stable, so which zone wins can vary per boot.

There *is* an outlier guard — `MaxAcpiDeltaFromWmiC = 18.0` (line 117) rejects an ACPI reading more than 18°C from the current cached value — **but it is conditioned on `_cachedCpuTemp > 0`** (line 945). When the HP WMI BIOS CPU temperature is unavailable or zero, that guard does not apply, and the latched zone is accepted unconditionally as "CPU temperature". A board whose WMI BIOS path is weak or absent therefore displays a chassis sensor as its CPU temp, indefinitely, with no cross-check and no user-visible indication of which sensor is being trusted.

That is a precise, code-level explanation of "36 degrees when it's over 80" — and because fan curves consume this same value, **a wrong temperature is also a fan-control bug, not just a display bug.** This is the highest-priority item in this roadmap.

**"Never adjusted the fan speed" on a Transcend 14 — not a bug; the app genuinely cannot, and hides that badly.**

Both Transcend 14 database entries (`8C58` and `8E41`, `ModelCapabilityDatabase.cs:1426` and `:1448`) carry `SupportsFanCurves = false` and `SupportsIndependentFanCurves = false`. The Transcend 14 exposes a WMI *profile-only* fan interface with no real curve API. So on that reporter's hardware OmenCore is behaving exactly as designed: it will not run a curve, because it has no curve to run.

`docs/ROADMAP_v4.0.0.md` already recorded this once (GitHub #156/#149, "UX Confusion, Not the Bug It Looks Like") and concluded the Diagnostics tab explains it adequately. **That conclusion was wrong, and this feedback is the evidence.** A user who installs a fan-control app, plays a game, watches their laptop overheat, and has to go find a Diagnostics tab to learn the app was never going to touch their fans has been failed by the product, regardless of how correct the gating logic is. "The capability flag is accurate" and "the user was misled" are both true here.

**"Feels extremely slow to move. Feels even laggier than the OGH." / "clunky"**

Three UI-responsiveness passes shipped across 4.1.x, each fixing real measured problems (chart teardown per tick, a pulse animation restarting continuously, a window-wide blur on every resize, per-second OSD brush allocation). Users still describe the app as laggier than the thing it replaces. That means the passes fixed real bugs but did not address whatever dominates *perceived* responsiveness — most likely startup cost, view-switch cost, and the absence of any motion design at all, rather than steady-state frame time. This cycle treats perceived performance as its own workstream with its own measurements, not as a series of opportunistic fixes.

**"Omenmon-reborn, where you can manually control the fans with 1-degree precision"**

Worth naming plainly: a competing tool is being recommended specifically for *fan control granularity*. Not for features, not for polish — for the core thing this app exists to do. That is a direct competitive signal and it belongs in the record.

---

## Cycle Goals

Three headline pillars, in priority order. Pillar 1 outranks the others because a beautiful, fast UI displaying a wrong temperature is worse than useless — it is confidently wrong.

1. **Sensor truth, and the fan control that depends on it.** Make the displayed CPU/GPU temperature provably the right sensor, make the app say which sensor it is using, and make fan response follow from a trustworthy number.
2. **Perceived performance and motion.** Startup time, view-switch cost, transition smoothness, and a real reduction in idle CPU/RAM over long background sessions.
3. **Typography and visual identity.** Move to Roboto Condensed as the app font.

---

## Pillar 1 — Sensor Truth and Fan Control

**This pillar is partly evidence-gated.** Per the project's standing rule (`ROADMAP_v4.0.0.md`, "How To Tackle This", item 2), anything that changes fan/EC/thermal *behavior* on real hardware needs field validation before shipping. Diagnostic, display, and honesty work is exempt. Each item below is tagged accordingly.

### 1.1 Fix ACPI thermal-zone selection — **partly exempt, partly gated**

- **[Exempt — pure correctness] DONE.** Replaced the "first zone wins, latched forever" logic in `GetAcpiCpuTemperature()` with `WmiBiosMonitor.SelectCpuThermalZone()`, extracted as a pure, unit-tested function: prefer a zone still matching the previously-confirmed name, else a zone whose name hints at CPU (`CPU`/`CPUZ`/`TZ00`), else — only when genuinely ambiguous — the *hottest* of the unnamed zones, since the CPU is the one component virtually guaranteed to run hottest under real load. Critically, an ambiguous pick is **never latched**: every poll re-evaluates from scratch until a confident name match appears, so a single unlucky "which zone is warmest right now" guess can't get stuck the way the old first-index latch did. 14 new tests in `WmiBiosMonitorAcpiZoneSelectionTests.cs`, including a direct regression test reproducing the field-reported shape (cool zone enumerated first, hot zone second, no name hints). Full suite: 1302/1302. No hardware behavior changed for single-zone systems (still just uses the only zone, same as before) or for boards where a zone already matches a name hint — this only changes what happens on multi-zone, ambiguously-named systems, which is exactly where the bug lived.
- **[Exempt] Partially done.** Multi-zone selection reasoning is now logged (`[WmiBiosMonitor] ACPI CPU thermal zone: <name> (confirmed | ambiguous, using hottest reading) — all zones this poll: [...]`) whenever there's more than one zone and the selection changes, so a future field report is diagnosable from a log export instead of requiring a live repro. **Not yet done:** surfacing this in the structured diagnostics-export bundle itself (still log-only) — folded into 1.2 below, since both need the same "show every source side by side" UI work.
- **[Exempt] Deliberately not duplicated.** Investigated extending the 18°C outlier guard to apply when `_cachedCpuTemp == 0` — found `TryApplyCpuTemperatureFallback()` already does materially the same job downstream, and more thoroughly: it detects an implausibly-low cached temp under real load/power (`ImplausiblyLowCpuTempThresholdC`/`CpuLoadThresholdForAuthorityMismatchPercent` etc.), probes a secondary LibreHardwareMonitor-backed worker reading, and requires 2 consecutive confirmed-mismatch readings before switching authority. Adding a second, independent plausibility check inside `GetAcpiCpuTemperature()` itself would duplicate that pipeline stage rather than complement it. The zone-selection fix above reduces how often that downstream mechanism even needs to fire; it doesn't need to be redundant with it.
- **[Gated]** Any change to which source ultimately *wins* authority for a given board, since fan curves consume that value. Needs a before/after log from an affected machine.

### 1.2 Surface the sensor, don't just use it — **exempt**

The app already tracks `CpuTemperatureAuthoritySource` and `CpuTemperatureAuthorityReason` internally (`WmiBiosMonitor.cs:322-324`) and shows them only in deep diagnostics. Users cannot currently answer "where is this number coming from?" without exporting a bundle.

- **Dashboard: DONE.** Checked first and found the premise needed correcting again — `CpuTemperatureAuthoritySource`/`Reason` weren't "shown only in deep diagnostics" as originally written here, they were computed and read *nowhere at all*, not even by the diagnostics export. Added both to `MonitoringSample` (`CpuTemperatureSource`/`CpuTemperatureSourceReason`, populated in `WmiBiosMonitor.BuildSampleFromCache()`), then a `DashboardViewModel.CpuTemperatureSourceTooltip` combining source + reason into a plain-language tooltip on the CPU temperature chip, plus `IsCpuTemperatureSourceFallback` driving a small warning glyph when the trusted source is `LHM Fallback` — meaning the primary WMI/ACPI reading was recently rejected as implausible. Also fixed the diagnostics export's existing `[CPU Temperature Authority]` section, which — despite its name — only ever printed `MonitoringSource`/`Health`/`LastSampleAgeSeconds` (the overall monitoring backend) and never the actual per-tick sensor authority; it now does. 8 new tests (`DashboardViewModelCpuTemperatureSourceTests.cs`) plus 2 assertions added to the existing `MonitoringSampleCopyConstructorTests.cs`. Full suite: 1310/1310.
- **OSD: DONE, but not as a tooltip.** Checked before building and found the plan itself was wrong for this surface: `OsdOverlayWindow` applies `WS_EX_TRANSPARENT` unconditionally (one call site, no toggle) — it's genuinely click-through at all times, so a hover tooltip would never be reachable during actual gameplay, the OSD's entire reason to exist. Added `IsCpuTempSourceFallback` (same "trusted source is LHM Fallback" check as the Dashboard) driving a small always-visible `~` marker next to the CPU temp reading instead of a tooltip — glanceable, no hover required, minimal visual cost in line with Pillar 2's decluttering goal. No dedicated test: `OsdOverlayWindow.xaml.cs` is a real WPF `Window` with Win32 P/Invoke in its constructor and has no existing test coverage for any of its other `UpdateStats()`-assigned properties either (`CpuTemp` included) — this one-line addition matches that same established, build-verified-only tier rather than introducing a new one.
- **Guided-diagnostics side-by-side comparison: DONE.** A genuinely new feature rather than another "wire up existing data" fix — reading WMI BIOS, ACPI Thermal Zone, and LibreHardwareMonitor simultaneously on demand required real design, since the live monitor loop only ever tracks one *winning* source at a time. Added `WmiBiosMonitor.GetCpuTemperatureSourceComparisonAsync()`, deliberately read-only and side-effect-free: the ACPI read reuses the same `SelectCpuThermalZone` pure function from the earlier fix but is called with `latchedInstance: null`, so it can never race the background monitor loop's own `_cpuThermalZoneInstance`. New `ICpuTemperatureSourceComparer` interface (same shape as `IFanVerificationService`) keeps the new `TemperatureSourceDiagnosticsViewModel` unit-testable without depending on the concrete WMI-coupled class. New "Temperature Source Comparison" card on the Diagnostics tab: one button, one read-only result showing every source side by side plus which one is currently trusted and why, with a visible warning state when they disagree by more than the same 18°C threshold the live outlier guard already uses. Caught and fixed an invented, nonexistent `BoolToRunningLabelConverter` XAML resource before it shipped — verified every resource key used against the actual style dictionaries rather than assuming, after getting one wrong on the first pass. 24 new tests (`CpuTemperatureSourceComparisonTests.cs`, `TemperatureSourceDiagnosticsViewModelTests.cs`). Full suite: 1326/1326.

### 1.3 Stop silently doing nothing on profile-only boards — **exempt**

**Correction, Windows side: this premise was stale.** Checked `git blame` before building anything — the Windows Fan Control tab's "Profile-only" badge, header hint ("Select an OEM fan profile"), and `FanOwnershipDetail` explanation ("Profile-only firmware control; custom curves and fixed fan levels are unavailable") all landed in `b538316` (2026-07-08), part of the 4.0.0 cycle — over a month before the Reddit report this item was written from. It's on the Fan Control tab's own header, not buried in Diagnostics. The predecessor roadmap's GitHub #156/#149 conclusion ("the Diagnostics tab already carries correct explanatory copy") was itself outdated by the time it was written; this is now doubly confirmed correct. Nothing to build here — recording the correction rather than shipping a redundant "fix."

**Real gap found instead, on the Linux GUI (`omencore-gui`) — FIXED.** `FanControlViewModel.cs` had the mirror-image problem from a different cause: when `CanEditFanCurve=false`, the curve-editor cards collapse via `IsVisible="{Binding CanEditFanCurve}"` with no explanation — except a warning banner *does* exist (`ShowCapabilityWarning`/`CapabilityWarningMessage`, bound in `FanControlView.axaml:17-20`), so the "zero explanation" framing was also wrong on first read. The actual bug: the banner's "profile-only" message was a single hardcoded string for every profile-only sub-case — *"Use System Control performance profiles for cooling behavior"* — while `LinuxCapabilityClassifier`'s real, already-computed `Reason` (plumbed all the way to `SystemCapabilities.FanControlCapabilityReason` and then never read) correctly distinguishes two very different situations. Traced `SetPerformanceModeAsync`: on a board with no `thermal_profile`/`platform_profile` path (exactly [GitHub #99](https://github.com/theantipopau/omencore/issues/99)'s board `8E41`, confirmed via that issue's own attached diagnostics — `Thermal Profile Control: ✗ Missing`), the suggested "performance profiles" workaround falls through to `powerprofilesctl` or throws outright — it has nothing fan-relevant to do. Meanwhile `SetCpuFanSpeedAsync`/`SetGpuFanSpeedAsync` **do** work on that exact board class: they already fall back to `TryWriteHwmonPwmEnable` (the 4.1.7 GitHub #174 fix) before any profile-based path, and that call isn't gated behind `CanEditFanCurve` — so Max Fan / Emergency Stop is real, working guidance the banner never gave. **Fix:** `InitializeCapabilitiesAsync()` now prefers the classifier's real `Reason` text over the generic per-class string, and appends a truthful note that Max Fan still works as a coarse override when the class is `profile-only` — never claims it for `telemetry-only`/`unsupported-control`, where no write path exists at all. Build-verified only (no test project for this target, no Linux/OMEN hardware in the dev environment — same tier as prior Linux GUI fixes this cycle).

**Not the same bug as the Fennel-Extra Reddit report** — that reporter didn't specify Windows or Linux, and without their diagnostics this can't be conclusively linked to #99 or to board `8E41` specifically. Recorded as a real, separate, now-fixed issue found along the way, not claimed as resolving the original report.

This is a UX honesty fix of exactly the kind this project already values; it changes no hardware behavior whatsoever.

### 1.4 Close out the Max-level-floor bug — **gated, highest-value carryover**

Confirmed on four boards (`8A18`, `8A25`, `8E10`, `8D41`) and traced repeatedly across 4.1.x without a fix, because every candidate fix was a threshold guess. Board `8E10`'s multi-day log showed the re-assert loop firing every ~20s continuously while Max Fan Mode is engaged, plus a correlated (unproven) stutter complaint.

The real fix is a **design decision, not a threshold tweak**: should `ExpectedLevel` and the observed-peak reference be board-relative rather than nominal-max-relative, across *both* `FanVerificationService.IsLevelReadbackMatch()` and `WmiFanController.IsMaxModeTelemetryHealthy()`? Scope this properly in 4.2.0 rather than deferring again. Needs a sustained-Max-hold log with independent RPM evidence from at least one affected board.

### 1.5 Fan-control granularity — **gated, scoping only**

Directly prompted by the OmenMon-reborn comparison. Investigate what per-degree manual control would require on boards that genuinely support duty-cycle writes, and whether the current curve-point model is the limiting factor or the hardware interface is. Scoping and design in this cycle; implementation only with field evidence.

---

## Pillar 2 — Perceived Performance, Motion, and Footprint

### 2.1 Measure before optimizing — **exempt**

Three prior passes fixed real problems without moving the needle on user perception, which means the wrong thing was being measured. Establish real baselines first:

- Cold-start and warm-start time to interactive
- Per-view switch cost (the most likely "feels laggy" culprit, and never measured)
- Steady-state idle CPU while minimized to tray, over a multi-hour session
- Working-set growth over the same period (field reports confirm **355–705 MB** main app plus 48–174 MB worker — real, not exaggerated, and the specific contributor was never isolated)

Ship the harness, not just the numbers, so regressions are catchable later.

### 2.2 Motion and transitions — **exempt**

The app currently has essentially no motion design. Add a coherent, *cheap* transition system: view-change transitions, state-change easing on gauges and readouts, and skeleton/placeholder states so first paint never shows an empty or jumping layout. Constraints, learned from the animation bugs already fixed this cycle: every animation must be GPU-composited where possible, must never restart on unrelated property changes, must be disabled wholesale under a "reduce motion" setting, and must respect the OS reduced-motion preference.

### 2.3 Idle CPU/RAM conservation — **gated (both known findings)**

Two architectural findings were traced in the 4.1.6 cycle and deliberately left untouched, both because they sit in the temperature pipeline that drives fan control:

1. **`FanService`'s independent polling defeats tray-mode backoff.** `HardwareMonitoringService` correctly backs off 2s → 10s in tray mode, but `FanService.MonitorLoop` reads temperature on its own 1–5s schedule against a shared 500ms read cache that ignores tray mode. Real WMI/EC hardware reads therefore continue on FanService's cadence while minimized — **tray mode currently saves nothing on this path.**
2. **`OmenCore.HardwareWorker` never learns the main app is minimized or idle.** It launches unconditionally at startup rather than lazily on first need, and only backs off after 15–30s of nobody calling it. There is no signal path from the app's tray state into the worker at all; fixing it needs a cross-process cadence command.

Both are real wins for the "conserve CPU/RAM" goal and both change how fast the app notices a temperature change while unattended — a thermal-responsiveness question with physical stakes. Design first (what cache bound is safe, how it interacts with hysteresis and thermal protection), then validate, then ship.

### 2.4 Dead-code and allocation cleanup — **exempt**

- Delete `TemperatureRgbService` and `ScreenColorSamplingService` — fully-built services with independent polling loops, **never instantiated anywhere in production code**. Zero runtime cost today, but each would silently reintroduce a redundant poller if wired up, since `LightingViewModel` already implements the same features off the shared `HardwareMonitoringService.SampleUpdated` stream. One caveat the predecessor roadmap missed: `TemperatureRgbService` *is* constructed by `OmenCoreApp.Tests/Services/BackgroundTimerRegistryTests.cs:124`, so deletion means updating that test to use a different timer-registering service — a small task, but not a clean `git rm`.
- Continue the timer consolidation onto `UiPollingCoordinator`. The named UI cluster is done; a **background-thread-flavored coordinator** is still needed before `ProcessMonitoringService` and its peers can join (folding them into the UI-thread coordinator would be a regression, not a consolidation).
- Add a guardrail comment or assertion on `LibreHardwareMonitorImpl.EnsureCacheFresh()`, which sync-blocks a threadpool thread on IPC and is currently safe only because nothing calls it per-tick.

---

## Pillar 3 — Typography: Roboto Condensed

Move the app to [Roboto Condensed](https://fonts.google.com/specimen/Roboto+Condensed).

- **Embed the font; do not assume it is installed.** Roboto Condensed is not a Windows system font. It must ship as an embedded resource (`src/OmenCoreApp/Fonts/`, referenced via a WPF pack URI) with a correct fallback chain, or non-installed machines will silently fall back to a default and look broken.
- **Confirm and record the license** before shipping, and add attribution to `LICENSE`/third-party notices as required.
- **The migration is not a one-line change.** There are **102 `FontFamily` declarations** across the WPF XAML. A central `AppFontFamily` resource exists (`Styles/ModernStyles.xaml:544`) but is widely bypassed by hardcoded values (`Segoe UI`, `Segoe UI Variable Display`, `Consolas`). Consolidate onto the shared resources first, then switch the resource — that is the only version of this change that is reviewable and revertable.
- **Keep the monospace family separate and intact.** `MonospaceFontFamily` (Cascadia Mono/Consolas) is used deliberately for log output, diagnostic dumps, and hex values, where column alignment matters. Roboto Condensed must not replace it. Likewise leave `Segoe MDL2 Assets` alone — that is an icon font, not text.
- Condensed faces are narrower; **re-check dense layouts** (fan curve editor, diagnostics tables, settings rows) for reflow, and re-verify accessibility labeling still reads correctly at the new metrics.
- Consider exposing a font choice in Settings, given some users will prefer the system font.

---

## Rolled Over From v4.0.0

Open items carried forward, grouped as they were in the predecessor document. Nothing here was completed in the 4.1.x cycle.

### Architecture

- **`MainViewModel` decomposition** — *partially done.* 19 of ~40 fields are DI-seeded and `HardwareBringup` is extracted and registered. The remaining ~22 fields are entangled with the four hardware bring-up locals and need an injectable "hardware context" abstraction that does not exist yet. **Extracting actual business logic and bound properties into feature-scoped ViewModels has not started** — that is the part needing real UI regression coverage, and it overlaps directly with Pillar 2's view-switch cost work.
- **`CapabilityDetectionService`'s 5s undervolt probe blocks the UI thread during startup** (called synchronously from `MainViewModel`'s constructor during `App.OnStartup`). Empirically completes in ~1–1.2s in every field log reviewed, so it has never been reported as a hang — but it is a real cold-start cost and directly relevant to Pillar 2.1.
- **RGB provider architecture** — unify `RgbManager` vs. per-provider direct writes; prerequisite for any new vendor work.

### Security and distribution

- **Code signing** — cost/process decision, still unmade. Blocks the item below.
- **Authenticode signature check before elevated installer execution** in `AutoUpdateService.InstallUpdateAsync` — blocked on the above.
- **Privilege separation** — prototype the limited-privilege `HardwareWorker` service per `docs/PRIVILEGE_SEPARATION_SPIKE.md`.

### Platform

- **i18n foundation** — resource-based strings + language selector. Note this now interacts with Pillar 3: do the font consolidation first so a future non-Latin script fallback has one place to change.
- **Linux: `omencore-gui` tray icon + GUI-side config persistence.**
- **Linux: AUR packaging** — drafted in `packaging/aur/`, untested on real Arch hardware; blocked on a real icon asset, real release checksums, and a maintainer decision.
- **`LinuxCapabilityClassifier` "full-control" overclaim** — noted during the 4.1.7 Linux work, not actioned.
- **DKMS backport module (`omen-fan-control`)** — for boards where the *kernel driver* lacks real sysfs write paths (the actual root cause on `8BCA`), pointing users at this module is a better remedy than anything userspace can do. Worth surfacing in Linux diagnostics output or install docs.

### Hardware-gated (need field evidence)

- Dedicated Balanced fan mode (per-model capability gating required)
- AMD GPU (ADL2) OC startup persistence
- AMD CPU undervolt (Curve Optimizer) startup persistence
- Self-validating PL1/PL2 readback loop, to replace manual per-model field-report gating
- Board `8D41` keyboard RGB via the Darfon `0x0D62:0x54BF` HID controller — [GitHub #151](https://github.com/theantipopau/omencore/issues/151)'s offered HID capture is still the unblocker. **Note: the predecessor roadmap's framing of this item is now stale** — it claimed `HidPerKeyBackend.cs` hardcodes `HP_VID = 0x03F0` and "would never detect this controller." The Darfon VID was added to `ScannedVendorIds` during the 8D87 work, so the device *is* scanned and logged today. The real remaining blocker is narrower and better documented in the source itself: `0x0D62:0x54BF` is deliberately absent from `KnownPerKeyPids` because while the device has the right packet shape (65-byte output reports on MI_00/MI_03), **nobody has confirmed it speaks the `CMD_BYTE 0x0F` command set** — and adding it would start sending those bytes on every launch to find out. The file's own comment states the bar correctly: add it once someone has confirmed the protocol on hardware they are willing to power-cycle.
- Board OMEN MAX 16z-ak000 fan duty%/RPM mapping mismatch — this board's RPM readback returns null, so all displayed RPM is a software estimate. Overlaps with Pillar 1.
- **EC register `0x59` lead for boards `8E35`/`8A43`** — independent corroboration from `OmenCtl`'s hardware documentation that these boards' ACPI-WMI thermal-profile calls fail silently and need a direct EC fallback to `0x59` (not the standard `0x95`). Windows-side EC-register territory; needs the same field-validation treatment as any EC write.
- **New (2026-08-16): board `8E9A`, "HyperX OMEN MAX 16t-ah100"** — [GitHub #142](https://github.com/theantipopau/omencore/issues/142). "HyperX" is HP's 2026 co-brand on the existing OMEN line (HyperX OMEN MAX 16/16/15, launched CES/Imagine 2026 with Intel Core Ultra 200HX / next-gen Ryzen AI, up to RTX 5090), not a separate hardware platform — the WMI model string still contains `OMEN`, so identity resolution already falls through correctly to Family fallback (`FAMILY_OMEN16`, Low confidence) rather than misbehaving. The reporter's diagnostics export has no behavioral confirmation attached (no fan test, no RGB attempt, no undervolt attempt) — not enough to safely author a dedicated `ModelCapabilityDatabase` entry yet, per this project's own promotion rule. **Checked OmenMon-Reborn (`seakyy/OmenMon-Reborn`, GPLv3) and other community projects (`omen-fan-control`, `OmenSuperHub`, `OmenCtl`, live `hp-wmi` kernel patch threads) for reusable board data — nothing found.** This hardware is too new (first report: June 2026) for community reverse-engineering to have caught up anywhere. Worth re-checking those sources periodically as this SKU sees wider adoption. OmenMon-Reborn's own architecture is worth studying later regardless: a dynamic XML model database plus a "safe heuristic auto-detector" that configures itself read-only for genuinely unknown boards is a different, possibly complementary approach to this project's current binary verified-entry-vs-family-fallback model — not proposing to adopt it, just flagging it as a reference point for a future Family-fallback improvement discussion.

### Diagnostics and smaller carryovers

- Fresh session log needed for the **stale-OSD-fan-mode** report before any change
- **`TryApplyEcGpuBoost()`'s model gate** is still a broad `model.Contains("OMEN")` substring match with no capability-database flag
- **`HighDutyManualModeReapplyIntervalMs = 5000`** sends a genuine `SetFanLevel` WMI write every 5 seconds during ≥70% duty by design — real background I/O during exactly the gaming sessions where stutter is reported. Overlaps Pillar 2.3.

---

## Also Outstanding: PR #176

[PR #176](https://github.com/theantipopau/omencore/pull/176) (tempestnano, board `8D87` keyboard lighting) was reviewed at the close of the 4.1.7 cycle and **not merged**. The review confirmed one real functional regression plus three lower-severity findings:

1. **`DojoPerKeyBackend.SetBacklightEnabledAsync` (line 327)** sets `_mcuShowsHostMap = true` whenever the backlight is re-enabled and a stale `_mapR` exists, without checking whether a device *effect* was actually running. Toggling the backlight off/on after installing an effect freezes that animation into a static frame on the next brightness change. **Merge-blocking.**
2. The UI's brightness-scaling formula does not match the backend's for ~22% of value combinations, contradicting an invariant that both the code comments and a shipped test assert (the test only exercises channels that happen not to diverge).
3. `RemoveFnSlot` double-writes `config.json` synchronously on the UI thread for a single click.
4. `FnCyclePlan.MaxSlots` is a hardcoded `12` duplicating rather than deriving from the effect enum count.

Resolve finding 1 (with the contributor, ideally) before merging.

---

## Suggested Order of Work

| Phase | Work | Gate |
|---|---|---|
| **A** | Performance baseline harness (2.1); ACPI zone-selection fix + multi-zone diagnostics (1.1 exempt parts); sensor-source surfacing (1.2); profile-only-board honesty (1.3) | None — ship on code review + tests |
| **B** | Font consolidation onto shared resources, then the Roboto Condensed switch (Pillar 3); dead-service deletion + allocation cleanup (2.4) | None |
| **C** | Motion/transition system (2.2); view-switch cost work, which pulls in the `MainViewModel` feature-scoped extraction | None, but needs real UI regression coverage |
| **D** | Max-level-floor redesign (1.4); tray-mode polling/worker cadence (2.3); fan granularity scoping (1.5) | **Field validation required** |
| **E** | Carried-over hardware-gated items; code-signing decision and its dependents | **Field validation / owner decision** |

Phase A is deliberately first and deliberately unglamorous: **the temperature has to be right before anything built on top of it is worth improving.**

---

## Standing Rules (unchanged)

- **Evidence gate.** Fan/EC/thermal/OC/UV *behavior* changes need field validation before shipping. Architecture, performance, display-honesty, and pure-UI items do not.
- **One item at a time, verified before moving on.** Build clean, full suite green (1288/1288 as of v4.1.7 — expect this to grow), and live-smoke-test the real UI path where feasible.
- **Update this document as you go.** Check items off only once verified, with a one-line note on what changed and which files, so the next person does not have to re-derive it from git history.
