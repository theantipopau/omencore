# OmenCore v5.0.0

**Release Date:** not yet released
**Type:** Major release — trust, distribution and UI. Not a feature pile.
**Status:** In development on `main`. This file is the running record for the cycle; the plan behind it is
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
  **108.3 MB**, against budgets of 92 / 78 / 118 MB. *Implemented; the publish-and-measure path ran locally and passed,
  the CI job has not yet run on a runner.*
- **Startup is measured phase by phase, from a real launch** (roadmap WP-31). The app recorded a single number for
  startup (`StartupTimeToInteractiveMs`) and nothing about where the time went, so every "startup feels slow" change
  was guesswork. `StartupTimeline` now records named phases against the OS process start time, each carrying its own
  cost rather than only the running total, logs one line per phase, and writes a `[Startup Timeline]` block into
  `runtime-performance.txt`. `qa/measure-startup-memory.ps1` launches the built app, samples working set and private
  bytes until it is idle, then reports the phase table, steady-state and peak footprint, idle CPU and the render mode
  the run used; it re-launches itself elevated because the app's manifest is `requireAdministrator`.
  *Implemented; measured on one machine, in software rendering because RTSS was running.*
- **Startup and idle memory are enforced, not just measured** (roadmap WP-31 follow-up). `qa/memory-budget.json`
  publishes the ceilings — 128 MB private idle, 176 MB peak, 320 MB working set, 3000 ms to completed startup work —
  and `qa/check-memory-budget.ps1` launches the Release build twice, reads the startup timeline and footprint from
  those runs, and fails when any of them is over. A `startup-memory-budget` CI job runs it on every push. A run that
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
  4000 ms — the runner's slowest observed launch plus headroom — and it still fails on the 2 s cold-WMI regression
  this work removed. First runner execution: 109.5 MB private, 2285 ms median app startup, all six CI jobs green.

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
- **Update trust.** The updater still takes its hash from release-note text and there is no Authenticode check before
  the elevated launch. 5.0.0 scope (roadmap WP-30).
- **Cross-process fan arbitration.** `FanWriteLease` is in-process only. The global mutex removes the multi-session
  case, but a second independent tool writing the EC is still outside what OmenCore can see.

## Issue housekeeping

Twelve issues closed as completed against shipped code (details in each thread): `#115`, `#172`, `#195`, `#199`,
`#205`, `#207`, `#211`, `#212`, `#213`, `#214`, `#220`, `#241`. All were verified against `4.5.1` or the `8BBE` /
`8C2D` / `8BA9` entries shipped earlier; the three that still carry a reporter question (`#212`, `#213`, `#241`) were
closed with an explicit invitation to post a diagnostics export on the same thread if the behaviour recurs.

## Breaking changes planned for the release

These are not on `main` yet; they are listed here so the version number is not a surprise when they land. Each gets a
migration or a documented default before the tag.

- **Config schema version.** A 4.x `config.json` is migrated, not reinterpreted. Unknown keys are preserved.
- **No more always-elevated UI.** Hardware writes move behind a service or scoped helper; the client runs as a normal
  user. Existing autostart entries are rewritten during install.
- **Signed binaries only.** Unsigned builds will be refused by the updater rather than offered.
- **Version single-sourced** from `Directory.Build.props`; `VERSION.txt` and the installer read it.
