# OmenCore v4.2.0 – In Development

**Release Date:** TBD
**Release Status:** In development. Base version v4.1.7 (tagged and released 2026-08-16).
**Type:** Minor release. Three pillars: sensor-truth/fan-control accuracy, perceived performance and motion, and a typography move to Roboto Condensed. Also absorbs field-report bug fixes from v4.1.7 users as they arrive.
**Base Version:** v4.1.7
**Tracking doc:** `docs/ROADMAP_v4.2.0.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

---

## Fixed: CPU Temperature Could Report a Chassis Sensor Instead of the CPU

Reported on r/HPOmen: "it can show the CPU at 36 degrees when it's over 80." On boards exposing several ACPI thermal zones, OmenCore latched onto whichever zone WMI happened to enumerate first and kept it for the whole session — often a skin/ambient sensor sitting in the mid-30s. Fan curves consume this value, so it was a fan-control bug, not just a display one.

Selection is now a pure, unit-tested function: prefer a previously-confirmed zone, else one whose name hints at CPU, else — only when genuinely ambiguous — the hottest zone, re-evaluated every poll rather than latched. Single-zone systems and name-matched boards are unaffected. Multi-zone selection reasoning is now logged so the next report is diagnosable from a log export. 14 new tests.

## Added: CPU Temperature Source Is Now Visible

OmenCore already tracked which source (WMI BIOS / ACPI / LibreHardwareMonitor fallback) it trusts and why — but nothing read it, not even the diagnostics export. The Dashboard CPU chip now shows it as a tooltip, with a warning glyph when the fallback source is active. The diagnostics export's `[CPU Temperature Authority]` section now reports the actual per-tick sensor authority instead of just the overall backend. 10 new tests.

## Added: Temperature Source Comparison Diagnostic

New card on the Diagnostics tab reads WMI BIOS, ACPI, and LibreHardwareMonitor side by side on demand, showing which is trusted, why, and a warning when they disagree by more than 18°C (the same threshold the live outlier guard uses). Read-only and side-effect-free — it can't perturb the background monitor loop. 24 new tests.

## Added: OSD Fallback-Source Marker

The OSD gets a small always-visible `~` marker next to the CPU temperature when the fallback source is active. Deliberately not a tooltip: the OSD is click-through at all times, so a hover tooltip would be permanently unreachable during gameplay — the one situation it exists for.

---

## Fixed: Linux GUI Fan Warning Pointed at a Dead-End Workaround

On boards with only a coarse `pwm_enable` toggle and no thermal-profile path (e.g. board `8E41`, [#99](https://github.com/theantipopau/omencore/issues/99)), the Linux GUI advised using performance profiles — which do nothing there. Meanwhile Max Fan *does* work on those boards and was never mentioned. The banner now uses the capability classifier's real per-case reason (already computed, previously unread) and notes the Max Fan override only where a write path genuinely exists. Messaging only, no control change. Build-verified.

## Added: Linux Support for the `omen-rgb-keyboard` DKMS Driver

Keyboard RGB now also probes `/sys/devices/platform/omen-rgb-keyboard/rgb_zones/` and the `omen::kbd_backlight` LED class, alongside the existing hp-wmi paths. That out-of-tree driver requires blacklisting `hp_wmi`, so on machines running it none of the previously-known paths exist and OmenCore found no keyboard interface at all. Its tested-hardware list includes boards already in our field reports (`16-wf0xxx`/`8BCA`, `16-wd0xxx`/`8BA9`). Purely additive path detection — no change for existing users. 10 new tests.

---

## Removed: Two RGB Services That Were Never Instantiated

`TemperatureRgbService` and `ScreenColorSamplingService` were fully-built polling services with zero references anywhere in production code (both duplicated functionality that is wired up elsewhere). Deleted, along with the one test that constructed one of them purely as a timer-registration example. Also documented why `LibreHardwareMonitorImpl.EnsureCacheFresh()`'s blocking IPC call is currently safe, so a future per-tick caller doesn't silently reintroduce a stall.

## Added: Startup and Tab-Switch Timing, Measured Every Run

Three prior UI-responsiveness passes fixed real problems without moving "feels laggy" reports — because the actual costs had never been measured. New `StartupAndNavigationPerformanceTracker` records startup time-to-interactive (from OS process start through first real paint) and per-tab switch cost (including lazy view construction and the render pass that follows). Both appear automatically in `runtime-performance.txt`. 7 new tests.

Not yet done: multi-hour idle CPU and working-set growth — needs a longer-running capture mode.

## Added: Reduce-Motion Preference

New "Reduce motion" setting, combined with the Windows-wide "Show animations" preference, gating all future animation work. Defaults to *not* reducing motion if the OS read fails. Conveniently, the system preference is the same value the built-in "Best Performance" optimization already writes. 4 new tests.

**No animation code shipped yet** — this is just the gate. Transitions need live UI verification this environment can't provide.

## Added: Background-Thread Timer Coordinator

`BackgroundPollingCoordinator` — the off-UI-thread counterpart to `UiPollingCoordinator`, sharing the same already-tested scheduler. 1000ms base cadence (background work has no render-responsiveness pressure), plus a reentrancy guard a `DispatcherTimer` doesn't need. 3 new tests.

Not done: migrating `ProcessMonitoringService` onto it — it changes its poll interval at runtime, which the scheduler doesn't yet support for a live subscription. That's a design decision, not a typing task.

## Fixed: Games Tab Virtualization Was Silently Doing Nothing

The games list declared `IsVirtualizing`/`Recycling`/`ScrollUnit=Pixel` but overrode its panel to a plain `WrapPanel`, which doesn't virtualize — so every detected game kept a full visual tree alive regardless of scroll position. Removed the override so the real virtualizing panel applies, and reflowed each game from a stacked card into a single-line row (also more scannable per screen). Bindings and styles unchanged. Build-verified only — worth an eyeball on the next real run.

## Added: Fan Curve Share Codes

Copy the current curve to the clipboard as a one-line code, or paste one in to import — for sharing in Discord/GitHub where a file attachment is awkward. File-based sharing already existed via Import/Export Presets. Malformed codes are rejected outright. 15 new tests.

---

## Typography (Roboto Condensed): Step 1 Done, Step 2 Blocked

**Step 1 — consolidation, shipped.** Routed 105 XAML font declarations and 13 hardcoded C# font-construction sites (tray icon, context menus, toasts — none of which can use a XAML resource) onto two shared resources. Without this the eventual switch would have been partial. Pure refactor, zero visual change. 3 new tests.

**Step 2 — attempted, reverted.** Embedded the official variable font, then verified it with a test rather than trusting a clean build. Result was genuinely non-deterministic: run in isolation, WPF resolved only 3 of 9 weights and no renderable glyphs; run in the full suite, all 9 worked. Two attempts to pin down the trigger failed. Reverted to the safe font rather than ship something observed failing half the time.

Next step would be a *static* (non-variable) font release, sidestepping the issue — that needs a fresh download and explicit approval.

License note: Roboto Condensed is **SIL OFL 1.1** (an earlier note here said Apache 2.0, which was checked against the legacy Roboto repo, not the current distribution).

---

## Field Reports Triaged

**[#177](https://github.com/theantipopau/omencore/issues/177)** (board `8BAB`) — five complaints, traced individually:
- *Fixed:* the "Temperature warnings" toggle didn't suppress thermal-protection toasts — that method checked only the master notifications flag, unlike its sibling.
- *Fixed:* Games tab empty after restart. The library was never meant to persist, but nothing triggered a re-scan either; now scans once when the tab is first opened. 1 new test.
- *Diagnosed, not fixed:* custom fan curve not restored on restart. Power Automation reapplies a per-power-source preset on every AC/Battery transition (including startup), overriding the last-selected curve. Working as designed with a surprising default; changing it touches fan behavior and needs field validation.
- *Investigated:* GPU temperature spikes on battery. Ruled out the suspected NVAPI path. Found a real asymmetry — GPU temperature has no outlier guard where CPU has two — but no confirmed mechanism, so no speculative fix. Asked for a repro using the new comparison diagnostic.
- *Likely not a bug:* AC-power temperature spikes into the 90s on a 13900HX with Max Fan responding, then settling — normal boost behavior.

**[#141](https://github.com/theantipopau/omencore/issues/141)** — the Fn+F2 false-trigger was already fixed by the keyboard-hook rewrite that postdates the report; added a regression test reproducing the exact reported input. The other two complaints need a hardware capture that was never provided.

**[#163](https://github.com/theantipopau/omencore/issues/163)** — already fixed; the AMD/Intel board disambiguation has a dedicated test using this reporter's exact WMI string.

**[#137](https://github.com/theantipopau/omencore/issues/137)** (board `8BCD`, Linux) — already handled; the capability classifier downgrades this board because its firmware aborts every WMI call (a genuine ACPI bug userspace can't fix). Verifying it surfaced a real gap: **`OmenCore.Linux` had no test project at all.** Added one — 25 tests now covering the classification matrix and sysfs path tables.

**Discord (board `8DCD`)** — independent confirmation that the 4.1.7 Max-Fan-Latch fix resolved fans being stuck high/low. Also flagged [#146](https://github.com/theantipopau/omencore/issues/146) as a likely instance of the same bug — notable because it involved a thermal shutdown in a closed backpack.

---

## Community Project Comparison

Reviewed three community projects after Discord suggestions. Full analysis in the roadmap; summary:

**[OmenXHub](https://github.com/MasonDye/OmenXHub)** (MIT, derived from OmenSuperHub) — shipped the fan-curve share codes above from this. Confirmed OmenCore's temperature smoothing and per-tick EC write pattern are already equivalent or better. Its board handling is thinner than ours and outsourced to HP's own DLLs — it embeds and redistributes 14+ HP proprietary binaries, which is the opposite of this project's OGH-independence goal.

**[OmenCtl](https://github.com/yunusemreyl/OmenCtl)** (GPLv3) — the reported "Max TGP unlock" is, per their own docs, a firmware side-effect of the ordinary performance-mode switch that OmenCore already performs, not a separate command. Useful anyway: it suggests the long-open "GPU Power Boost doesn't reach OGH's wattage" item may be looking in the wrong place.

**[omen-rgb-keyboard](https://github.com/OmenLinux/omen-rgb-keyboard)** (GPLv3) — a Linux DKMS driver; shipped sysfs support for it above.

**Declined:** the actual driver-level TGP unlock (in OmenXHub) force-installs a pinned 2023 NVIDIA `nvpcf` driver and force-uninstalls all other versions. Redistributing NVIDIA binaries is almost certainly outside their terms, and force-removing a user's display-power driver can leave their graphics stack in a state they didn't ask for. A fan-control tool shouldn't swap GPU drivers underneath its users.

No code was copied from any of these — OmenCore is MIT and two of the three are GPLv3, so facts and observed behavior only.

---

**Full suite:** 1361 Windows tests, 25 Linux tests.

*(Further entries added as work lands.)*
