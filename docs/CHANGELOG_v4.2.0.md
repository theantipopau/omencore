# OmenCore v4.2.0

**Release Date:** TBD — code-complete as of 2026-08-23. The most recent portable-build testing round (tab-switch animation, title-bar chrome, tray context menu) came back tester-confirmed with no further issues.
**Release Status:** Version bumped across the app/installer/build config. Two items remain before tagging: the [PR #176](https://github.com/theantipopau/omencore/pull/176) merge decision (pending owner call — see the roadmap) and the Phase D/E hardware-gated items (pending real-hardware field validation this environment can't provide). Everything else in this changelog is portable-build-verified.
**Type:** Minor release. Three pillars: sensor-truth/fan-control accuracy, perceived performance and motion, and a typography move to Roboto Condensed. Also absorbs field-report bug fixes from v4.1.7 users as they arrive.
**Base Version:** v4.1.7
**Tracking doc:** `docs/ROADMAP_v4.2.0.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

---

## Release Artifacts

Built locally via `build-installer.ps1` (Windows) and `build-linux-package.ps1` (Linux), Release configuration, self-contained single-file publishes. Verify a download by hashing it yourself and comparing against the value below — on Windows, `certutil -hashfile <file> SHA256`; on Linux/macOS, `sha256sum <file>`.

| Artifact | SHA256 |
|---|---|
| `OmenCoreSetup-4.2.0.exe` | `0DC9EF8558864181F5B27A7189251CD72CB45CA12123A912E4927552C7D6F14D` |
| `OmenCore-4.2.0-win-x64.zip` | `2D001F18411E94A16F3E26533FECA65BE55B8302A087DC6DF77F8520AE368F75` |
| `OmenCore-4.2.0-linux-x64.zip` | `5E99E64786F4F18A3EB9BB7889682C8AB5D64047C3D2CB83234FA59E17570C4B` |

Combined checksum file: `artifacts/SHA256SUMS-4.2.0.txt` (same convention as every prior release back to 3.4.1).

---

## Fixed

### CPU Temperature Could Report a Chassis Sensor Instead of the CPU

Reported on r/HPOmen: "it can show the CPU at 36 degrees when it's over 80." On boards exposing several ACPI thermal zones, OmenCore latched onto whichever zone WMI happened to enumerate first and kept it for the whole session — often a skin/ambient sensor sitting in the mid-30s. Fan curves consume this value, so it was a fan-control bug, not just a display one.

Selection is now a pure, unit-tested function: prefer a previously-confirmed zone, else one whose name hints at CPU, else — only when genuinely ambiguous — the hottest zone, re-evaluated every poll rather than latched. Single-zone systems and name-matched boards are unaffected. Multi-zone selection reasoning is now logged so the next report is diagnosable from a log export. 14 new tests.

### Linux GUI Fan Warning Pointed at a Dead-End Workaround

On boards with only a coarse `pwm_enable` toggle and no thermal-profile path (e.g. board `8E41`, [#99](https://github.com/theantipopau/omencore/issues/99)), the Linux GUI advised using performance profiles — which do nothing there. Meanwhile Max Fan *does* work on those boards and was never mentioned. The banner now uses the capability classifier's real per-case reason (already computed, previously unread) and notes the Max Fan override only where a write path genuinely exists. Messaging only, no control change. Build-verified.

### Games Tab Virtualization Was Silently Doing Nothing

The games list declared `IsVirtualizing`/`Recycling`/`ScrollUnit=Pixel` but overrode its panel to a plain `WrapPanel`, which doesn't virtualize — so every detected game kept a full visual tree alive regardless of scroll position. Removed the override so the real virtualizing panel applies, and reflowed each game from a stacked card into a single-line row (also more scannable per screen). Bindings and styles unchanged. Build-verified only — worth an eyeball on the next real run.

### AMD Undervolt Status Overclaimed "Readback" It Doesn't Have

A real-hardware test flagged the CPU Tuning tab showing "Verified: readback matches requested" for an AMD undervolt, alongside a "degraded" warning, with no way to independently confirm anything actually changed. Investigation confirmed the write itself is real (a genuine SMU mailbox exchange, with a code comment citing a measured clock uplift from prior testing) — but the status text was comparing the app's own memory of the last value it wrote against itself, since AMD's Curve Optimizer path has no hardware register to read back from (unlike Intel's MSR path, which does). It could never have shown a mismatch. Now shows "Verified: write acknowledged (no independent hardware readback on this path)" for AMD instead. Wording only — no write, clamp, or SMU command changed. 1 new test.

The "degraded" warning's exact trigger wasn't identified from code alone and needs a repeat report with the literal warning text to pin down.

### RGB Page Showed a False "OMEN Keyboard" on Non-HP Hardware

Reported on the portable test build, on a desktop PC with no HP hardware at all: the RGB page's "OMEN Keyboard" badge showed as present anyway. Root cause: keyboard-model detection's exception handler defaulted to a real HP OMEN config instead of "no config" on any detection failure — silently turning "couldn't determine the system" into "assume it's an OMEN." Fixed to match the same "not detected as HP" behavior already used everywhere else in that method. Pure logic fix, no keyboard write path touched.

### iCUE Not Detected Despite Running

Same report: iCUE was installed and running, but Corsair detection never found it. The check matched only a process named exactly `iCUE` — Corsair has changed that name across major iCUE releases. Now matches any process whose name contains "icue," case-insensitive.

### Sidebar Status Text Clipped Instead of Ellipsizing

The EC-backend status row had the same bug already fixed once this cycle for tab labels: a horizontal StackPanel gives its child unbounded width during layout, so `TextTrimming="CharacterEllipsis"` never actually engaged — text just got hard-clipped at the sidebar's edge instead of trimming with "…". Converted to a Grid, same fix pattern.

### General Tab's Profile Cards Were Completely Unusable by Keyboard

Found while doing an accessibility pass: the "Quick Profiles" and "Fan Mode" cards on the General tab — the app's default, first-seen screen — are plain `Border` elements with a mouse-click handler, not real buttons. No `Focusable`, no keyboard activation, no screen-reader exposure at all. A keyboard-only user could Tab past them entirely and had no way to switch performance or fan mode from this screen.

All 7 cards (4 profiles, 3 fan modes) are now real Tab stops with the app's own accent-colored focus ring (not the default dotted rectangle) and Enter/Space activation wired to the same view-model calls the mouse click already used. Visual appearance and mouse behavior unchanged.

### Window Minimize/Maximize/Close Buttons Were Rendering ASCII Text, Not Icons

Reported on Reddit: the title bar's maximize button showed literally as `[ ]`. Root cause: the button style already set the real Windows icon font (`Segoe MDL2 Assets`) as its `FontFamily`, but the button `Content` was plain ASCII (`"-"`, `"[ ]"`/`"[]"` toggling for maximize/restore, `"x"`) instead of that font's actual glyph codepoints — so it rendered exactly what it said, a literal bracket-space-bracket, not an icon. All three now use the standard Windows chrome glyphs (minimize/maximize/restore/close) the font was already selected for.

Broke an existing test in the process (`ReleaseGateCodeHygieneTests`), which turned out to be guarding a real past incident — a raw Unicode symbol pasted into source mojibaked in v1.0.0.4. The new glyphs avoid the same failure mode (written as ASCII-safe `&#xNNNN;`/`\u` escapes, never a raw pasted character), and the test was rewritten to check that directly rather than just updated to match the new text. Full detail in the roadmap.

### Title-Bar Buttons Still Looked Off After the Glyph Fix — Twice

First pass: the three caption buttons used `CornerRadius="4"` on their hover background and stopped 4px short of the window's right edge — a rounded, inset "chip" floating inside the title bar, not the flush, square-cornered strip every native Windows app uses. They also stopped 12px short of the title bar's own height (32 of 44px), leaving dead space above and below the actual click target. Fixed: flush to the corner (no margin), full title-bar height, square (`CornerRadius="0"`, relying on the outer window `Border`'s own rounded corner to clip them into shape at the very top edge). Cursor changed `Hand` → `Arrow` to match every OS caption button.

Second pass, on a real machine: the underlying glyph fix from earlier this cycle — real Segoe MDL2 Assets codepoints instead of literal `[ ]` — rendered as **tofu boxes** (☐☐☐, the standard "missing glyph" placeholder), not icons. Correct codepoints, but font-glyph availability isn't guaranteed the way plain ASCII text is, and this system apparently couldn't resolve them. Replaced the font dependency entirely: the three buttons now draw simple vector `Path` geometry (a line, a square outline, two overlapping squares, an X — `Icon.WindowMinimize/Maximize/Restore/Close` in `ModernStyles.xaml`) instead of any icon font, which is how native Windows chrome and most well-made custom-titlebar apps (VS Code, Windows Terminal) actually do this. No font, no codepoint, nothing that can fail to resolve. `Path.Stroke` binds to the button's own `Foreground`, so the existing hover/close-red color triggers keep working with no extra wiring.

---

## Added

### CPU Temperature Source Is Now Visible

OmenCore already tracked which source (WMI BIOS / ACPI / LibreHardwareMonitor fallback) it trusts and why — but nothing read it, not even the diagnostics export. The Dashboard CPU chip now shows it as a tooltip, with a warning glyph when the fallback source is active. The diagnostics export's `[CPU Temperature Authority]` section now reports the actual per-tick sensor authority instead of just the overall backend. 10 new tests.

### Temperature Source Comparison Diagnostic

New card on the Diagnostics tab reads WMI BIOS, ACPI, and LibreHardwareMonitor side by side on demand, showing which is trusted, why, and a warning when they disagree by more than 18°C (the same threshold the live outlier guard uses). Read-only and side-effect-free — it can't perturb the background monitor loop. 24 new tests.

### OSD Fallback-Source Marker

The OSD gets a small always-visible `~` marker next to the CPU temperature when the fallback source is active. Deliberately not a tooltip: the OSD is click-through at all times, so a hover tooltip would be permanently unreachable during gameplay — the one situation it exists for.

### Linux Support for the `omen-rgb-keyboard` DKMS Driver

Keyboard RGB now also probes `/sys/devices/platform/omen-rgb-keyboard/rgb_zones/` and the `omen::kbd_backlight` LED class, alongside the existing hp-wmi paths. That out-of-tree driver requires blacklisting `hp_wmi`, so on machines running it none of the previously-known paths exist and OmenCore found no keyboard interface at all. Its tested-hardware list includes boards already in our field reports (`16-wf0xxx`/`8BCA`, `16-wd0xxx`/`8BA9`). Purely additive path detection — no change for existing users. 10 new tests.

### Startup and Tab-Switch Timing, Measured Every Run

Three prior UI-responsiveness passes fixed real problems without moving "feels laggy" reports — because the actual costs had never been measured. New `StartupAndNavigationPerformanceTracker` records startup time-to-interactive (from OS process start through first real paint) and per-tab switch cost (including lazy view construction and the render pass that follows). Both appear automatically in `runtime-performance.txt`. 7 new tests.

Not yet done: multi-hour idle CPU and working-set growth — needs a longer-running capture mode.

### Reduce-Motion Preference

New "Reduce motion" setting, combined with the Windows-wide "Show animations" preference, gating all future animation work. Defaults to *not* reducing motion if the OS read fails. Conveniently, the system preference is the same value the built-in "Best Performance" optimization already writes. 4 new tests.

### Tab-Switch Fade Transition

First animation to actually use the gate above. Switching tabs now fades the new content in (160ms, ease-out, opacity only — no layout pass, GPU-composited) instead of popping in instantly. Skipped entirely when Reduce Motion is on (app setting or OS preference). Deliberately small and easily revertible: one `DoubleAnimation` on `Opacity`, triggered only from `TabControl.SelectionChanged` (a discrete, real tab change — not any property-changed notification, to avoid repeating the earlier dashboard-pulse-restarting-on-every-update bug class). No new automated tests — this is a live-feel judgment call, being verified via a portable build rather than assertions, same as the nav-rail redesign earlier this cycle.

### Background-Thread Timer Coordinator

`BackgroundPollingCoordinator` — the off-UI-thread counterpart to `UiPollingCoordinator`, sharing the same already-tested scheduler. 1000ms base cadence (background work has no render-responsiveness pressure), plus a reentrancy guard a `DispatcherTimer` doesn't need. 3 new tests.

**Follow-up: `ProcessMonitoringService` now consolidated onto it too.** It used to own a private `System.Timers.Timer` specifically because its poll rate changes at runtime (2s while a game is running, 10s idle, 20s when WMI eventing is doing the real work) and the shared scheduler had no way to change a live subscription's interval. Added that capability (`IPollingSubscription.UpdateInterval`) instead of working around its absence — one fewer independent OS timer running for the life of the app, same poll cadences and thread-pool-callback semantics as before. 5 new tests.

### Fan Curve Share Codes

Copy the current curve to the clipboard as a one-line code, or paste one in to import — for sharing in Discord/GitHub where a file attachment is awkward. File-based sharing already existed via Import/Export Presets. Malformed codes are rejected outright. 15 new tests.

---

## Improved

### Tuning Tab Buttons Now Say What They Actually Do

The Tuning tab — CPU undervolt, power limits, thermal offset, GPU overclock — had 16 buttons with **no accessibility labels at all** and only 4 tooltips, on the page where a mis-click has the most real consequence. Worst case: four separate buttons labelled exactly "Reset to Defaults" (CPU limits, AMD limits, AMD GPU, NVIDIA GPU), indistinguishable to a screen reader and only disambiguated visually by which card you happen to be looking at.

Every button now has a distinct accessibility label naming its actual target and a plain-language tooltip, including the risk where relevant. Purely additive attributes — no layout, binding, or command wiring touched.

**Same pass extended to the Diagnostics tab** — 9 buttons went from 0 accessibility labels and 2 tooltips to 9/9 and 9/9, including risk-aware wording on "Drop the adapter power clamp" (screens go black for a few seconds) matching its own inline warning text.

### Icon Color Cleanup on the Top Bar and RGB Page

A design-taste pass flagged the top bar's Quick Actions row (fan preset, performance mode, lighting, gaming mode) using four different accent colors on five adjacent, equal-weight icon buttons — decorative color with no actual system behind it. All four now use the same neutral tone as the Restore button next to them; color is reserved for things that mean something (selection, status), not applied per-icon by default.

Same pass on the RGB page: Scene Quick Select buttons (OMEN Red, Cool Blue, Rainbow, Heat Wave, etc.) rendered identically grey regardless of what color they actually apply — `RgbScene.PrimaryColor` already carried that data, it was just never drawn. Each scene button now shows a small color swatch. The "Active: SceneName" badge also had a stray, uncatalogued purple (`#9C27B0`) that matched nothing else in the app; now uses the same accent color the rest of the app uses for "this is selected."

**RGB page still flagged as needing a broader pass** beyond these two spot-fixes — noted for next time, not blocking this release.

### System Tray Right-Click Menu Had Double Arrows Throughout

Every submenu item ("Advanced", "Fan Control", "GPU Power," etc.) carried a manually-typed "▶" in its header text — but the custom dark context-menu template already draws its own submenu-arrow glyph on the right edge for any item with children, so every one of these showed two arrows: the typed character mid-text and the real arrow past it. It was also inconsistent with the one item that didn't have this problem — "Monitor: Healthy" already used a plain colon. Removed the redundant "▶" everywhere (11 spots) and standardized the live-value headers ("Fan Mode: Auto", "Performance: Balanced", "GPU Power: Medium", "Display: 165Hz", "Keyboard Backlight: High") on the colon convention the menu already used in one place. Also added a separator before "Exit OmenCore" so it's not sitting directly under "Check for Updates" with no visual gap — the one genuinely hard-to-undo item in the menu. Text/layout only, no command wiring touched.

### Model Capabilities Screen Clarified What It Does and Doesn't Cover

A field report read "Custom fan curves: Not supported" / "GPU power boost: Not supported" on an unverified board and concluded the General tab's Auto/Performance/Max fan buttons must be gated by the same flag — they aren't; those buttons only check whether a fan-control backend was created at all, independent of the advanced-feature flags shown on this screen. Added one clarifying sentence to the existing disclaimer text so the next reader doesn't draw the same reasonable-looking but incorrect conclusion.

---

## Changed

### Navigation Moved to a Vertical Rail, With Grouped Sections

The main window's ~10 sections were in a horizontal strip inside a scroll viewer — so below a certain window width, tabs scrolled out of sight with nothing indicating they were there. Navigation is now a vertical rail down the left of the content area: every section visible at once, with room for readable labels (icon-only was rejected — "Optimizer" vs "Memory" vs "Bloatware" aren't guessable from an icon).

Related sections are now visually grouped — control, insight, system cleanup, personalization, app — with separators between them.

Implemented as a retemplate, not a restructure: same tabs, same order, same indices, same lazy content loading, **no C# changed at all**. Built by hand rather than adopting a Fluent control library, which would have fought the existing dark theme. The Settings tab's own inner tabs stay horizontal as before. 5 new tests parse the resource dictionary and verify both styles resolve correctly, since a clean build alone doesn't prove a template is valid.

**Follow-up after real-hardware feedback:** a portable test build showed the rail sitting next to the existing sidebar as two visibly separate boxes. Merged them into one continuous panel — logo, live status, Quick Actions, then the tabs, no seam — and moved the sidebar's specs/utility buttons (CPU/RAM/GPU, About, Import/Export, Profiles, Check for Updates) into a new bar under the title bar. Tab labels are also larger and more spaced (14px, more padding, no more hardcoded 164px width — it scales with the sidebar column now). Build-verified (0 warnings/errors) and full suite green; still wants an eyeball on the next real run.

**Second follow-up, same tester, restored (non-maximized) window:** Quick Actions' fixed height at the top of the sidebar was squeezing the tab list on a shorter window. Moved Quick Actions (and Restore Defaults) out of the sidebar entirely, into compact icon buttons in the top bar — the sidebar now goes straight from the logo/status card to the tabs, so the full tab list gets priority for whatever vertical space exists. Tab labels also now ellipsis (`…`) instead of hard-clipping if the sidebar column is ever narrower than a label needs. Full suite green.

**Third follow-up:** group separators are now a small orange-to-red gradient bar instead of a flat grey hairline, so the sidebar carries some of the app's own brand color. And the tab list now shrinks itself — text, icons, and spacing together, as one scale — to fit a short window instead of just scrolling at full size; it stops shrinking at a floor (roughly 72% of normal size) and falls back to scrolling past that point, so it never gets shrunk down to unreadable. Full suite green.

---

## Removed

### Two RGB Services That Were Never Instantiated

`TemperatureRgbService` and `ScreenColorSamplingService` were fully-built polling services with zero references anywhere in production code (both duplicated functionality that is wired up elsewhere). Deleted, along with the one test that constructed one of them purely as a timer-registration example. Also documented why `LibreHardwareMonitorImpl.EnsureCacheFresh()`'s blocking IPC call is currently safe, so a future per-tick caller doesn't silently reintroduce a stall.

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

**Reddit (kn_kry)** — Victus, no board ID or diagnostics: the concrete, checkable claim (maximize button rendering as literal `[ ]`) was correct and is fixed, above. The rest (fan control, temperature display, RGB not working at all, perceived slowness) is real signal but not independently actionable without a board ID — full triage in the roadmap, including why several of those symptoms could be genuine hardware limitations on some Victus models rather than bugs.

**[#140](https://github.com/theantipopau/omencore/issues/140) follow-up** (Victus 16-e0194nw, board `88EE`) — reporter concluded fans weren't ramping because the Model Capabilities screen showed several advanced features as "Not supported" on their unverified board. Traced the actual gating: those flags (custom fan curves, independent zones, GPU power boost) don't reach the General tab's Auto/Performance/Max fan buttons, which only check whether a control backend exists — `88EE` has `SupportsFanControlWmi = true`, so that path should work. Fixed the screen's wording (above) to say so directly; couldn't independently confirm whether Max mode itself works without their diagnostics log, which wasn't shared. Full trace in the roadmap.

---

## Community Project Comparison

Reviewed three community projects after Discord suggestions. Full analysis in the roadmap; summary:

**[OmenXHub](https://github.com/MasonDye/OmenXHub)** (MIT, derived from OmenSuperHub) — shipped the fan-curve share codes above from this. Confirmed OmenCore's temperature smoothing and per-tick EC write pattern are already equivalent or better. Its board handling is thinner than ours and outsourced to HP's own DLLs — it embeds and redistributes 14+ HP proprietary binaries, which is the opposite of this project's OGH-independence goal.

**[OmenCtl](https://github.com/yunusemreyl/OmenCtl)** (GPLv3) — the reported "Max TGP unlock" is, per their own docs, a firmware side-effect of the ordinary performance-mode switch that OmenCore already performs, not a separate command. Useful anyway: it suggests the long-open "GPU Power Boost doesn't reach OGH's wattage" item may be looking in the wrong place.

**[omen-rgb-keyboard](https://github.com/OmenLinux/omen-rgb-keyboard)** (GPLv3) — a Linux DKMS driver; shipped sysfs support for it above.

**Declined:** the actual driver-level TGP unlock (in OmenXHub) force-installs a pinned 2023 NVIDIA `nvpcf` driver and force-uninstalls all other versions. Redistributing NVIDIA binaries is almost certainly outside their terms, and force-removing a user's display-power driver can leave their graphics stack in a state they didn't ask for. A fan-control tool shouldn't swap GPU drivers underneath its users.

No code was copied from any of these — OmenCore is MIT and two of the three are GPLv3, so facts and observed behavior only.

---

**Full suite:** 1372 Windows tests, 25 Linux tests.

*(Further entries added as work lands.)*
