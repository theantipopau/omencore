# OmenCore v5.0.0

**Release Date:** not yet released
**Type:** Major release — trust, distribution and UI. Not a feature pile.
**Status:** In development on the `v5.0.0-wp31-startup-budget` branch (pushed, not merged to `main`). This file is the running record for the cycle; the plan behind it is
`docs/ROADMAP_v5.0.0.md`. Everything below is what is actually in the tree today, marked the way this project
always marks it: *implemented, pending confirmation* unless a reporter verified it on real hardware.

## Added

- **`--restore-fans`, a headless hand-back to the BIOS.** The installer force-stops the GUI, which skips `OnExit` and
  the EC reset in `FanService.Dispose`, so the firmware Max flag outlived an uninstall and left the fans pinned.
  `OmenCore.exe --restore-fans` runs before the single-instance check and before dependency injection (no window, no
  HardwareWorker, no tray icon), clears the flag through `HpWmiBios.ReleaseMaxAndHandBackToBios()` and exits 0 or 1.
  Wired into `[UninstallRun]` and into `PrepareToInstall`, because an upgrade kills the app the same way an uninstall
  does. *Implemented; not yet run on hardware from a real installer.*
- **`ProcessWait`** — a small bounded-wait helper (`TryWait`, `Complete`, `Kill`) used by every child-process call
  the app waits on, so a timeout and a killed process tree are one call instead of a pattern each caller improvises.
  Fire-and-forget launches (opening Explorer, a browser, the store page) deliberately do not use it.
- **`ProcessWaitTests`** — one test that a 60 s child is abandoned at a 500 ms bound, one that a fast child still
  returns its output. The first one fails on the old pattern by design.
- **A reason behind every gated feature** (roadmap WP-44). Controls used to disappear or grey out with the explanation
  living only in a log line, which is why so many model reports arrive as "X is missing". `CapabilityReasonResolver`
  now derives, from state the app already detects, a coded reason per feature — fan control, fan curves, independent
  curves, GPU Power Boost, MUX, zone/per-key lighting, undervolt, performance modes — and each carries a user-readable
  sentence plus its source: `model database`, `firmware`, `runtime detection`, `safety policy` or `runtime API
  readiness`. A database "no" is therefore never reported as a detection failure. The Advanced page shows the fan-curve
  and GPU Power Boost reasons inline; the remaining features expose the same sentence as view-model properties for the
  pages that own those controls, and diagnostics write `capability-reasons.txt` from the same strings. *Implemented;
  wording verified against unit tests, not yet read on other boards' hardware.*
- **Performance modes remember the whole machine, not just wattages** (roadmap WP-43). A `PerformanceMode` used to be
  a name, two or three wattages and a power-plan GUID, so "Quiet" and "Performance" differed in power limits alone
  while fans, GPU boost, refresh rate, lighting and battery care stayed wherever the user last left them. Modes now
  carry an optional fan preset or custom curve, GPU Power Boost level, refresh rate, lighting profile and charge-limit
  state; null means "this mode does not touch that setting", which is also what every 4.x config already deserialises
  to, so the format change is additive and needs no migration. `PerformanceModeService` applies them after the power
  limits through a new `IPerformanceModeEffectApplier` contract and records what was applied, skipped and failed in
  the apply trace, so a mode switch cannot claim a change it did not make. A game profile that names a saved mode
  inherits the whole bundle, because it applies through the same `PerformanceModeService.Apply`; a name that matches no
  saved mode still falls back to a stock power-limit profile, as it does today. The Advanced page shows what the selected mode carries
  and has a **Remember current state** button that captures the machine as it is. *Implemented; the fan, refresh-rate,
  lighting and battery-care writes have not been exercised on hardware yet.*
- **The portable download has a published ceiling, and CI enforces it** (roadmap WP-46). The app already published
  self-contained single-file with the runtime compressed in, but nothing stopped the artifact growing release by
  release and the size quoted on the website was a number somebody had written down. `qa/check-size-budget.ps1`
  publishes the portable build the way `build-installer.ps1` ships it, zips it with the hardware worker, and fails when
  the app exe, the worker or the zip exceeds `qa/size-budget.json`; a new CI job runs it on every push. Measured on
  2026-11-11 (win-x64, self-contained, single-file, compression on): app **82 MB**, worker **69.3 MB**, portable zip
  **108.3 MB**, against budgets of 92 / 78 / 118 MB. *Confirmed on a GitHub runner: 82.1 / 69.3 / 108.3 MB, within
  budget.*
- **Startup is measured phase by phase, from a real launch** (roadmap WP-31). The app recorded a single number for
  startup (`StartupTimeToInteractiveMs`) and nothing about where the time went, so every "startup feels slow" change
  was guesswork. `StartupTimeline` now records named phases against the OS process start time, each carrying its own
  cost rather than only the running total, logs one line per phase, and writes a `[Startup Timeline]` block into
  `runtime-performance.txt`. `qa/measure-startup-memory.ps1` launches the built app, samples working set and private
  bytes until it is idle, then reports the phase table, steady-state and peak footprint, idle CPU and the render mode
  the run used; it re-launches itself elevated because the app's manifest is `requireAdministrator`.
  *Implemented; measured on one machine, in software rendering because RTSS was running.*
- **Startup and idle memory are enforced, not just measured** (roadmap WP-31 follow-up). `qa/memory-budget.json`
  publishes the ceilings — 140 MB private idle (raised from 128 MB, see below), 176 MB peak, 320 MB working set, 3000 ms to completed startup work —
  and `qa/check-memory-budget.ps1` launches the Release build three times, reads the startup timeline and footprint
  from those runs, and fails when the judged values are over. A `startup-memory-budget` CI job runs it on every push. A run that
  never completes startup fails rather than passing with the check skipped, and the measurement isolates its config
  through `OMENCORE_CONFIG_DIR`, so a machine with no settings shows no first-run dialogs and the real user config is
  never read or written. That seeded config is written in the app's own camelCase schema — the first CI execution used
  PascalCase keys, which `ConfigurationService` ignores, so the app still believed it was a first run, blocked on the
  modal driver prompt (reachable only on a machine without PawnIO, which no development machine here is), and the gate
  failed at 17.5 MB. The harness now logs the phase startup stopped in, with the app's own log tail, instead of only
  reporting the missing phase. Time to first paint and idle CPU are printed but not enforced: across the same period the
  app's own startup held a 1.7-2.2 s band while first paint ranged 2.4-12.3 s, all of the spread inside `Show()`
  (RTSS software rendering, window-manager and hotkey registration stalls), which no startup fix can address — a gate
  on it would flake and then be disabled. Startup is judged on the **median of three** launches and memory on the
  **worst** attempt: three launches of one build on one GitHub runner spanned 2285-3650 ms with no code change, so
  judging the fastest let the slowest sit 22% over budget while judging the slowest would fail a busy runner that
  changed nothing; memory is deterministic here, so its worst observation is simply the honest number. The ceiling is
  3000 ms — the worse of the two runner medians observed (1657 and 2301 ms) plus roughly 30%, and deliberately not the
  slowest single launch (3930 ms), which is the noise the median exists to ignore: budgeting for that would let a real
  2 s regression, whose median lands around 3.6-4.3 s, pass. One runner execution measured 114.7 MB worst private and
  1657 ms median app startup; another measured 109.4 MB and 2103 ms. The idle-private ceiling then moved from 128 to
  140 MB after the gate failed on a CLI-only commit: the previous run on another host measured 109.3 / 106.9 / 104.1 MB
  and this one 133.2 / 131.4 / 127.8 MB, both in hardware rendering with identical startup phases, so the 24 MB was the
  machine. 140 MB is the worst observed attempt plus about 5%, still far below the 200 MB that #232 reported, and the
  product goal of 120 MB on a real 8 GB machine (measured at 103-116 MB locally) is unchanged - the CI ceiling exists
  to catch regressions, not to double as the target.
- **The window follows Windows' high-contrast palette, and the navigation rail has names** (roadmap WP-45).
  `HighContrastTheme` reads `SystemParameters.HighContrast` before the first window is created and again whenever the
  setting changes mid-session, then repaints the twelve chrome brushes (window and surface backgrounds, the four text
  tones, borders, accent and focus ring) from `SystemColors`, so a user on High Contrast Black, White or a custom
  scheme gets their own foreground/background pair instead of ours. The mechanism matters: a second merged resource
  dictionary would not work, because every style reaches these brushes through `StaticResource`, which binds at load
  time to the brush object; the theme mutates those objects in place, which repaints every consumer with no XAML
  re-parse. That requires unfrozen brushes, so the twelve carry `PresentationOptions:Freeze="False"` and
  `HighContrastThemeTests` fails if a repainted key disappears from the style sheet or loses the attribute. Literal
  hex fills inside control templates keep their colours — recolouring those belongs to WP-33's chrome pass, and a
  half-converted template looks worse than one that stays put. The eleven navigation tabs also gained
  `AutomationProperties.Name`: their headers are a `Path` plus a `TextBlock`, which leaves a screen reader with no
  label for the item. Motion already respected the OS setting (`MotionPreference`, tested); the remaining I.4 gap is
  a font-scale setting, which needs the per-view font sizes turned into shared resources first — that is WP-33's
  work, not a runtime tree walk.
- **The shell speaks Russian and Turkish** (roadmap WP-32, first slice). There was no resource infrastructure at
  all: 1,108 literal `Text=` values across the views and no `.resx` anywhere (counted in the XAML, not estimated). `Localization/Strings.resx` now holds the shell catalog with
  `Strings.ru.resx` and `Strings.tr.resx` beside it, compiled to satellite assemblies; `Strings.cs` is a hand-written
  accessor because the SDK only generates a designer class under Visual Studio, and CI builds from the CLI, so the
  generated-type guarantee is replaced by `StringsResourceTests` (key parity across all three catalogs, no empty value,
  a property for every key, and one test that actually loads "Настройки" and "Oyunlar" through `ResourceManager` rather
  than only reading the files). The window title, the three window-control tooltips and all eleven navigation tabs read
  from the catalog through `x:Static`, so those tabs are now translated *and* named for a screen reader from one entry
  instead of two English literals. Language is chosen in Settings (`auto`, English, Русский, Türkçe) and applied at
  startup before the first window loads, because `x:Static` resolves as each view is parsed; the row says it takes
  effect next launch rather than pretending to switch live. Only the UI culture moves — number formatting stays
  invariant, so a Russian window still reads "95.0%" instead of "95,0%". *Implemented for the shell and the Settings
  window/tray sections (33 more entries); roughly 1,060 literal values in the other pages are the rest of WP-32.*
- **The Settings page's window and tray sections are translated, labels and help text together** (WP-32, second
  slice). The eleven rows between "Window Behavior" and the Corsair telemetry toggle — start with Windows, start
  minimized, Lite mode, headless, software rendering, reduce motion, language, minimize to tray, stay on top, the two
  Quick Access rows, the tray temperature row and the two Corsair rows — now read from the same three catalogs as the
  shell, descriptions included, because a translated heading over English explanation text is worse than leaving the
  row alone. Each toggle's `AutomationProperties.Name` uses the row's own catalog entry instead of a second English
  literal, so a Russian user's screen reader says "Меньше анимации" rather than "Reduce motion". The page is now
  mixed: these sections are translated and the rest of Settings is not, which is what a partially migrated page looks
  like and is why the remaining rows are listed as work rather than described as done.
- **A configuration file can be checked before the app reads it** (roadmap I.10). `omencore-cli config --validate
  [--path file]` reports every value outside the ranges OmenCore enforces, and — the part that answers the usual
  complaint — whether the loader would silently change it. `ConfigurationService.ValidateAndRepair` resets a
  monitoring interval outside 500-10000 ms to 1000, a negative smoothing duration to 1000, an unknown memory profile to
  Balanced and an unshipped language to the system one, which is right for starting up and useless to someone asking
  why their value did not stick, so each finding carries `RepairedOnLoad` and the CLI prints it as such. The rules cite
  ranges the tree already documents or enforces: battery 60-100 %, thermal protection 75-95 °C, emergency 90-99 °C and
  at least 2 °C above protection, Quiet Safety releasing below its trigger, OSD opacity 0-1 with toast 800-6000 ms at
  80-140 %, memory interval 1-120 min and threshold 50-95 %, `HH:mm` schedule times, and duplicate performance-mode
  names. Exit status is 1 for errors, 2 for an unreadable or missing file, 0 otherwise. It uses `Environment.Exit`
  rather than `Environment.ExitCode` because `Program` returns `InvokeAsync`'s 0, which overrode the property: the first
  version of this command printed two errors and then reported success. *Verified on two fixture files and by
  `ConfigValidatorTests`; the validator runs on the file as written, before repairs.*
- **The interface zooms, and the mode picker stops eating a screen** (roadmap WP-33, first slice; this is the
  font-scale setting WP-45 deferred). Every view sets explicit `FontSize` values, so an inherited size or a style
  setter reaches none of them — the only mechanism that moves all of them at once is a layout transform on the page
  host, which is what a user asking for "bigger" means anyway. `UiScalePercent` stores one of seven steps (80-150,
  snapped to the nearest on load so a hand-edited value cannot make the window unreadable) and is applied as a
  `LayoutTransform` on the content presenter, inside a `ScrollViewer` added for exactly this: a layout transform
  changes measured size, so the page scrolls when the zoom outruns the window instead of clipping. That scroller is
  enabled **only when the scale is not 100 %**: a scrolling `ScrollViewer` measures its child with infinite height,
  which turns every `*` row on a page into `Auto` — the chart that used to fill the window would size to its content
  and the page would grow to fit. At 100 % the host keeps the constrained measurement pages were written against,
  and `UiScale.PageScrollsAsOne` is the one place that decision is made (pinned by a test). `Ctrl` with `+`, `-`
  and `0` change the scale from anywhere in the window and save it; the Settings row changes the same value. 100 %
  removes the transform rather than applying an identity scale, so the ordinary path stays untouched. Separately,
  the four profile cards on the General page lose 12 px of icon, 6 px of padding and 6 px of gap before each
  section — the #48.6/#48.7 complaint about the mode picker consuming more vertical space than its content. The
  edit is contained: `ProfileCard` is declared inside `GeneralView.xaml` and the shared `SurfaceCard`/`CardBorder`
  styles in `ModernStyles.xaml` are unchanged, so no other page's cards moved. *Zoom and the scroll gate are covered
  by tests and a clean build; neither the density change nor the gate has been looked at on screen.*
- **Tab switching is measured, and it was already not rebuilding anything** (roadmap WP-33). The premise behind the
  navigation complaint is that every switch rebuilds the page; in this tree it does not — `EnsureTabContentCreated`
  keeps `_initializedTabs` and assigns `tab.Content` once, so the second visit to a tab reuses the same element tree.
  Rather than add a second caching layer on top of a correct one, each switch now logs `[Nav] <tab>: created|reused,
  N ms to layout`, which turns "switching feels slow" into a number on the user's own machine. What such a switch
  actually costs is a layout pass over an existing page (plus the 160 ms fade, which `Reduce motion` already
  suppresses), and that is what the remaining page work in WP-33 has to reduce. `omencore-cli nav` now collects those
  numbers without clicking: it launches the app with `--nav-cycle=N`, the window walks every visible page through the
  normal selection path (so view creation, layout and the `[Nav]` line all run) and writes the per-page samples to
  `%LOCALAPPDATA%\OmenCore\nav-report.json`, which the command prints as a table or as `--json`. It reads back a
  rounded copy of the same in-memory tracker the diagnostics export uses. Because the switch cost belongs to the
  window, the measurement has to live in the app; the CLI starts and reads it rather than trying to host a UI it
  cannot. A failed run says why — an already-running instance hands the launch off instead of cycling, and that
  case reports "no timings were written" rather than an empty table. *Argument parsing, the report round trip and
  the scroll gate are covered by tests; the cycle itself has not been run end-to-end on this machine, which needs
  an elevated launch of the app.*

- **The window shows which mode you are in** (roadmap 4.B, an idea from Ohman). The selected navigation tab and the
  sidebar's performance marker take the active profile's colour: orange for Performance, cyan for Balanced, green for
  Quiet, purple for Custom. Each profile card's border and its "Active" badge use the same colour instead of all four
  turning red. In Windows high contrast the system highlight colour is used instead. Error and warning text keeps its
  own colour, so a warning never turns green under Quiet.
- **Interface polish.**
  - The mode reads "Balanced" everywhere. The General page, the dashboard and the sidebar used to say "Default" in
    some places for the same mode; a mode you named yourself still shows your name.
  - The CPU, GPU and System cards on the General page size to their content instead of stretching to the window
    height and showing mostly empty card.
  - The sidebar group dividers are a thin fading rule instead of a heavy 3 px bar.
  - The Tuning page says why CPU undervolt is missing on this machine instead of leaving a gap, and its status box
    no longer prints "Safe rollback has not been run this session" twice.
  - The five icon-only header buttons have names for screen readers.
  *Covered by unit tests and checked in an offscreen render of the General page; not yet looked at in the running app.*

- **The updater checks downloads against the release's own checksum file** (roadmap WP-30). It used to read the
  SHA256 out of the release-note text, which is written for people and can be edited after the release. It now reads
  the `.sha256` file the release workflow attaches beside each download, computed from the exact file uploaded. If
  that file and the notes both give a hash and they disagree, the update is refused; if the checksum file cannot be
  read, the notes are used as before. Each release also gets a `SHA256SUMS` file (`sha256sum -c` format) and a GitHub
  build-provenance attestation, which anyone can check with `gh attestation verify <file> --repo theantipopau/omencore`.
  *Parser covered by tests; the workflow steps run for the first time on the next tag.*

## Fixed

- **Three startup costs the profile named.** Measured on the reference machine (Ryzen 7 9800X3D, RTSS running so WPF
  was drawing on the CPU): 7.85 s warm and 11.0 s cold to first paint, 138.2 MB private while idle. (1) CPU vendor
  detection ran `select * from Win32_Processor` on the startup thread — about 2.0 s on a cold WMI service — to decide
  which of two log lines to print. It and `RyzenControl.Init()` now read the kernel's
  `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` values through `CpuIdentityReader`, falling back to WMI only
  when that key is absent: the phase went from **2045 ms to 1 ms**. (2) The same path opened the PawnIO MSR driver on
  AMD boards to load a module this file already documented as Intel-only, so the vendor check now runs first and the
  open is skipped. (3) `GetSystemInfo()` fills seven WMI classes (~1.1 s) and `MainViewModel` always waited for it, so
  it is now warmed on a pool thread as soon as the service container exists and overlaps hardware bring-up
  (**1147 ms to 941 ms** in-phase; the rest is the queries themselves). After the changes, three runs of the new
  harness measured 2.4-4.0 s to first paint and 103.6-111 MB private idle; the spread comes from software rendering
  and initial tab construction, not from run-to-run drift in the fixed phases. Startup is still above the roadmap's
  1 s target, and the remaining costs are named rather than described as fixed: CLR/WPF before `OnStartup` (~0.4 s),
  the SystemInfo WMI population (~0.9 s) and first paint under software rendering.
  *Implemented; warm-start numbers from three runs of the new harness on one board, not from field reports.*
- **Timeouts could not fire, anywhere.** Every child-process call read the child's output to completion and only then
  waited, so `WaitForExit(ms)` was decoration: `ReadToEnd` returns when the child closes the pipe. Converted ten call
  sites — AppX and scheduled-task scans, memory compression read and write, the powercfg and fsutil checks, three
  verifier probes, and five Task Scheduler / HP package calls in Settings. Reads start first, the wait is bounded
  (3 to 60 s), a timeout kills the tree and logs which command overran, and `ExitCode` is only read after a confirmed
  exit. Two sites that redirect both pipes now drain stderr even when only stdout is parsed, which removes a second
  way to deadlock a child. A later audit of the same pattern found five more of them — four `fsutil behavior set`
  writes in `StorageOptimizer` that called `WaitForExit()` with no deadline, and the `Checkpoint-Computer` restore-point
  call, which redirected both pipes without ever reading them — and those now read first and wait with 15 s and 60 s
  bounds. The claim in this entry is that no waiting child-process call in the Windows code is unbounded; the audit is
  what makes that true rather than a description of the first sweep.
- **A second controller could run alongside the first.** The single-instance mutex was session-local, so an RDP or
  fast-user-switch session got its own copy and could write the same EC/WMI registers underneath the first process.
  It is now `Global\OmenCore_SingleInstance_Mutex`, with a per-session fallback when the global name cannot be created
  for another user's session. Another user cannot block you; you still cannot run two copies.
- **Three dependency advisories.** `System.Drawing.Common` 4.7.0 to 4.7.2 (CVE-2021-24112), `System.Text.Json` 8.0.0
  to 8.0.5 (CVE-2024-30105 and its follow-up DoS), `Tmds.DBus.Protocol` 0.20.0 to 0.21.3 (CVE-2026-39959, reached
  through Avalonia.FreeDesktop). `dotnet list package --vulnerable --include-transitive` now reports nothing for any
  project in the solution.
- **The model-database validator checked a file that does not exist.** `ci.yml` pointed `--existing-db` at
  `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`, which has been in `OmenCore.Core` for several versions, so
  the duplicate-ProductId warning never ran and every submission validated as a brand-new board. The workflow, the
  schema doc, the submissions README, the contributor guide and the script's own usage line now name the real path.
  Verified by submitting a known-duplicate board: the check fires at the new path and stays silent at the old one.

## Investigated, not fixed

- **The privilege model.** The app still runs elevated for the whole session, so the hardware-write path cannot be
  narrowed until it moves behind a service. Design work for 5.0.0, not a 5.0.0 change in this list.
- **Update trust, the part that needs a certificate.** There is still no Authenticode check before the elevated launch,
  because the binaries are not signed yet (roadmap WP-30, waiting on the signing decision).
- **Cross-process fan arbitration.** `FanWriteLease` is in-process only. The global mutex removes the multi-session
  case, but a second independent tool writing the EC is still outside what OmenCore can see.

## Issue housekeeping

Twelve issues closed as completed against shipped code (details in each thread): `#115`, `#172`, `#195`, `#199`,
`#205`, `#207`, `#211`, `#212`, `#213`, `#214`, `#220`, `#241`. Each was checked against `4.5.1` or the `8BBE` /
`8C2D` / `8BA9` entries shipped earlier; the three that still carry a reporter question (`#212`, `#213`, `#241`) were
closed with an explicit invitation to post a diagnostics export on the same thread if the behaviour recurs. `#212` is closed on shipped code, not on confirmation: the 8BD4 Quick Colors fix in 4.5.1 is still *implemented,
pending confirmation* and should be reopened if the owner reports it does not work.

## Breaking changes planned for the release

These are not on `main` yet; they are listed here so the version number is not a surprise when they land. Each gets a
migration or a documented default before the tag.

- **Config schema version.** A 4.x `config.json` is migrated, not reinterpreted. Unknown keys are preserved.
- **No more always-elevated UI.** Hardware writes move behind a service or scoped helper; the client runs as a normal
  user. Existing autostart entries are rewritten during install.
- **Signed binaries only.** Unsigned builds will be refused by the updater rather than offered.
- **Version single-sourced** from `Directory.Build.props`; `VERSION.txt` and the installer read it.
