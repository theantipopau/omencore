# OmenCore v5.0.0 Roadmap

**Status:** Planning (opened 2026-11-11, the day after 4.5.1 shipped).
**Previous cycle:** `docs/ROADMAP_v4.5.0.md` / `docs/CHANGELOG_v4.5.1.md`.
**Running notes for this cycle:** `docs/CHANGELOG_v5.0.0.md`.
**Why a major version:** 5.0.0 changes things a user can see and can depend on — the trust model around the
elevated process, the configuration file format, and the window itself. It is not a feature pile; it is the release
that makes the existing feature set credible to someone who has never heard of the project.

Working rules are unchanged: issues, exports, forks and PRs are field evidence; separate *confirmed* (a reporter saw
it on real hardware) from *implemented, pending confirmation*; fix root causes with a test; keep the suite green;
nothing is released, tagged or closed without the maintainer. Attribution stays Matthew Hurley only.

## 1. Where 4.5.1 leaves us

4.5.1 (2026-11-10) closed the trust gap on the hardware side and left three open fronts.

| Front | State | What 5.0.0 has to do |
|---|---|---|
| Hardware behaviour | Six fixes shipped in 4.5.1, of which five still read *implemented, pending confirmation* (`8F5C` fan modes, `8E10` backlight, `8BD4` Quick Colors, `8BCD` layout, `8EEC`/`8A4D` notes) | Convert or correct them from real reports before stacking new writes on top |
| Coverage | 90 open issues, of which about 25 are model requests for boards that already have a conservative entry | Finish the verification loop instead of adding boards one at a time |
| Trust | Unsigned binaries, a hash scraped from release notes, `requireAdministrator` on every launch, no provenance | This is the release where that gets fixed |

Measured today on this branch: build 0 warnings / 0 errors, 2005 app tests and 78 Linux tests green in ~7 minutes,
~177.5k lines, 93 boards named and 19 field-verified.

The report that matters most for 5.0.0 is #232: 200 MB of private memory on an 8 GB machine, and a UI the reporter
describes as hostile. #133 says the same about responsiveness, #48 lists nine concrete UI grievances, and #234 is a
Linux HiDPI failure. Four separate reports against the front end is a pattern, not noise.

## 2. Already on the 5.0.0 branch

Landed 2026-11-11 on `v5.0.0-wp31-startup-budget` (pushed to origin, not merged to `main`), in the same commit that
opened this cycle. All are engineering fixes; none is released yet.

- **Fans are handed back on uninstall and upgrade.** `taskkill /F` skipped `OnExit`, so the firmware Max flag outlived
  the uninstall and left machines pinned. A headless `OmenCore.exe --restore-fans` now clears it through
  `HpWmiBios.ReleaseMaxAndHandBackToBios()`, wired into `[UninstallRun]` and `PrepareToInstall`.
  *Unverified on hardware until someone runs it on a real install.*
- **Child-process timeouts can fire.** Ten call sites read a child's output to completion before waiting, so the
  deadline could never apply. `ProcessWait` bounds them, kills the tree, and `ProcessWaitTests` pins both sides.
- **One controller per machine.** The single-instance mutex is `Global\`, with a per-session fallback, so an RDP or
  fast-user-switch session can no longer drive the same EC registers.
- **Three advisories retired.** `System.Drawing.Common` 4.7.2, `System.Text.Json` 8.0.5, `Tmds.DBus.Protocol` 0.21.3.
  `dotnet list package --vulnerable --include-transitive` is clean.
- **The model-database CI validator points at a real file again.** It had been checking a path that no longer exists,
  so the duplicate-ProductId warning silently never ran.

## 3. Peer review: what the other OMEN/Victus projects do better

Eight projects are active on the same hardware. Each one is better than OmenCore at something specific; the
column on the right is the 5.0.0 job, not a wish list.

| Project | Platform | Where it beats us | Concretely |
|---|---|---|---|
| [Ohman](https://github.com/P4R1H/ohman) | Windows | Product focus and restraint | One 1.6 MB exe, three modes that each remember their own fans/power/GPU, the window takes the mode's colour, tray covers mode/fan/backlight/refresh/graphics, OMEN key does something useful, and controls the firmware does not offer are *hidden* rather than shown broken. It also refuses to write fan levels between 1 and 1800 rpm because no fan holds them. |
| [OmenMon](https://github.com/OmenMon/OmenMon) | Windows | Footprint and CLI parity | ~14 MB against OGH's ~1.4 GB, no network at all, EC *and* WMI, full CLI that mirrors the GUI, plain-text config a user can edit, OMEN key remapping. |
| [victus-control](https://github.com/Batuhan4/victus-control) | Linux | Privilege model and "Better Auto" | A privileged backend service plus sudoers drop-ins (the GUI never runs as root), re-samples CPU/GPU every ~2 s and reapplies, GTK4 client, systemd user unit for the OMEN key, animated RGB. |
| [omen-fan-control](https://github.com/arfelious/omen-fan-control) | Linux | Installation of kernel support | Ships the backported `hp-wmi` patch and installs it, so fan nodes exist on stock distros; a watchdog re-applies the profile; a dust-expulsion mode; a tested-board matrix with kernel versions. |
| [hp-omen-dkms](https://github.com/saikiranworks/hp-omen-dkms) | Linux | Board-specific EC fixes | Four-zone RGB over sysfs, an AUTO-handoff fix for Slim boards, and an explicit exit pulse for boards that do not clear user-defined mode on a single `max_set(0)`. |
| [Victus Hub](https://github.com/evident0/victus-hub) | Linux | Integration with the rest of the system | Maps OmenCore's three profiles onto `tuned-adm` / `power-profiles-daemon`, MUX switching, `ryzenadj` PPT/Tctl limits, and a process list with right-click kill inside the same window. |
| [OpenOMEN](https://github.com/openomen/openomen) | Linux | Distribution and onboarding | Driver + daemon + CLI, packaged for Arch/Nix/deb, a wiki, a Discord, and a supported-device table users can read before downloading. |
| [HP_Fan_Control](https://github.com/klashain/HP_Fan_Control) | Windows | Guard rails and i18n | A user-set maximum RPM ceiling so a curve cannot redline the fans, plus English and Turkish UI. |

### What that means in one paragraph

OmenCore is the widest in capability and the weakest in *presentation and trust*. It has more hardware paths than any
of them (WMI BIOS + EC + PawnIO, Windows + Linux, NVIDIA unlock, MUX, undervolt, per-board capability database) and
the largest test suite. It does not have a signature, a package manager presence, an installer that does not ask for
admin forever, a UI that a reporter describes without contempt, or a verification loop that converts its 90 open
issues into closed ones. 5.0.0 is those four things plus the five cheap adaptations in section I, and nothing else
is allowed to push them out.

## 4. Candidate work, by theme

Ordered within each theme by value, and by how little new hardware risk it carries. Items marked (data) need a field
export first and are not started without one.

### A. Trust and distribution — the actual 5.0.0 headline

- **Code signing.** The installer and both exes. SmartScreen currently says "unknown publisher" (#192), which is the
  first thing a new user sees. Cost decision for the maintainer; everything else here is blocked behind the same
  trust question.
- **Updater provenance.** Verify the downloaded installer's Authenticode signature and SHA256 before the elevated
  launch, not after, and publish an `SHA256SUMS` file from the release workflow instead of the note text the updater
  scrapes today. Add GitHub build provenance (`actions/attest-build-provenance`) to `release.yml`.
- **Stop running the whole app as administrator.** Move hardware writes into a service or a scoped helper the way
  victus-control does, and make the UI a normal-privilege client. This is the largest engineering item in the cycle
  and the reason the version is 5.0.0. It is also what lets the app start at login without a UAC prompt.
- **Packaging.** winget and Scoop manifests (both absent today), and on Linux a real package instead of a zip: deb,
  rpm, AUR, Flatpak. Reuse the layout from `omen-fan-control` and `OpenOMEN`.
- **Single version source.** Version is currently duplicated across `VERSION.txt`, the `.iss` and six csproj files;
  one `Directory.Build.props` value feeds all of them.
- **Config schema version**, so a 4.x config is migrated rather than silently reinterpreted.

### B. UI/UX — the second headline, and the one users feel daily

Evidence: #232 (200 MB private, corner radius in fullscreen), #133 (sluggish toggles), #48 (nine suggestions),
#234 (Linux HiDPI).

- **Kill the startup and navigation cost.** Profile the shell before changing it: the audit found 967 hardcoded XAML
  strings and a page-per-tab navigation model that rebuilds views on every switch. Target: < 1 s to interactive,
  < 120 MB private idle, no visible stutter while scrolling (#48.5, #133).
  *Profiled 2026-11-11 (WP-31).* `StartupTimeline` plus `qa/measure-startup-memory.ps1` measured 11.0 s cold and
  7.85 s warm to first paint, 138.2 MB private idle at 0.85% CPU on the reference machine — in software rendering,
  because RTSS was running. Three costs were removed: CPU identity now comes from the registry instead of a cold
  `Win32_Processor` query (2045 ms → 1 ms), the Intel-only MSR probe no longer opens a driver on AMD, and
  `GetSystemInfo()` is warmed while hardware bring-up runs (1147 ms → 941 ms in-phase). After: 2.4-4.0 s to first
  paint and 103.6-111 MB private across three runs. What remains is CLR/WPF before `OnStartup` (~0.4 s), the
  SystemInfo WMI population (~0.9 s) and first paint under software rendering, so the < 1 s target still belongs to
  the navigation work in WP-33/WP-34. *WP-33 started 2026-11-11:* the display scale (80-150 %, Ctrl +/-/0, applied as
  a layout transform inside a scroller) ships the font-scale item WP-45 deferred, the profile cards lost the padding
  and icon size that made the mode picker taller than its content, and each switch logs created/reused plus ms to
  layout. Views were already cached per tab, so the remaining navigation work is the cost of laying out a page, not
  of building it. `omencore-cli nav` drives that measurement without a person clicking: it launches the app with
  `--nav-cycle=N` and prints the per-page timings the window writes to `%LOCALAPPDATA%\OmenCore\nav-report.json`.
  The host's scroller is gated to non-100 % scales, so the unzoomed layout still measures with the window's real
  height instead of turning star rows into auto rows.
- **High-DPI on both platforms.** Per-monitor V2 on Windows, and an application scale setting that does not depend on
  XWayland's `GDK_SCALE` on Linux (#234), including a sensible default on 2560×1600 panels.
- **Mode-aware chrome.** Adopt Ohman's idea: the window's accent follows the active performance mode, so the current
  state is legible at a glance without reading a label. *Done 2026-11-12:* a separate `ModeAccentBrush` (never
  `AccentBrush`, which also means error/warning) drives the selected tab and the sidebar marker; the profile cards'
  borders and badges use the same colours; high contrast uses the system highlight. Same pass: one display name for
  the middle mode ("Balanced", not "Default"), stat cards no longer stretch, subtler group dividers, the undervolt
  reason on the Tuning page, screen-reader names on the header icon buttons.
- **One diagnostics home.** Fan / keyboard / hardware diagnostics move into a single diagnostics page (#48.1), away
  from the tabs people use every day.
- **Logs and activity as resizable, remembered panels** (#48.4), with the collapse behaviour the reporter asked for.
- **Density pass** on Fan & Thermal (#48.6, #48.7): the mode picker and fan block consume far more vertical space than
  their content, which makes the page scroll for no reason.
- **Zoom** (Ctrl +/-/0) and layout-independent scaling (#48.8).
- **Internationalisation.** Extract strings, ship a resource-only translation path, and start with the two languages
  already asked for: Russian (#242) and Turkish (peer precedent). This also retires the 1,108 literal `Text=` values
  counted in the views.
  *Started 2026-11-11 (WP-32).* The plumbing exists and the shell uses it: three catalogs
  (`Localization/Strings{,.ru,.tr}.resx`), a hand-written typed accessor (CLI builds generate no designer class, so a
  parity test stands in for it), a config-driven language setting applied before the first window loads, and a
  Settings row. The window title, window-control tooltips and all eleven navigation tabs are translated. What remains
  is the bulk: about 1,060 literal values in the other views, which can now be moved page by page without touching
  the mechanism. The Settings page's window and tray sections followed on the same day (33 entries, help text and
  screen-reader names included), which shows the per-page migration shape.
- **Hotkey management** (#46): every registered hotkey visible and remappable, including the Scroll Lock / Pause
  binding the reporter could not find — and the OMEN key, which peers make a first-class action.
- **First run.** One screen: what board this is, what it supports, what it does not, and a single button to run Guided
  Fan Verification. Most model-request issues end with "post your export"; the export should be one click from here.
- **Fullscreen/windowed polish** (#232): no rounded corners at maximised, no custom chrome fighting the OS.

### C. Fans and thermals

- **Better Auto** (#246 contribution, #189, #143): when firmware Auto under-cools in Performance, offer an opt-in
  curve that tracks temperature with hysteresis, re-sampled like victus-control's ~2 s loop. Off by default, per board.
- **Settle the level-20 hand-back hint** (#227, #226, #217, #143). The conservative policy sends a level-20 hint and,
  on some boards, never the follow-up that hands control back. This is the single most reported fan symptom in the
  open list and the experimental setting from 4.5.0 needs an owner A/B to close it.
- **Fan stall guard** (data). Duty > 0 with 0 RPM for N seconds forces Max and warns. Only on boards where RPM is
  really read.
- **Percentages as well as RPM** (#224): the reporter asked for curve editing in %, which is also what makes a curve
  portable between boards.
- **Max auto-return** (already implemented, pending confirmation): keep it, and add a "return to Auto" tray action.
- **Safe maximum duty**, as in HP_Fan_Control: a user-set ceiling so a bad curve cannot redline the machine.
- **Dust-expulsion / fan cleaning** parity with `omen-fan-control`, and a test-mode that restores state on crash.

### D. Power, GPU and battery

- **Battery Care on 0x2B** (#243): the firmware refuses legacy `0x24` and implements the standard charge-mode command.
  A PR is in progress; land it behind a per-board capability flag.
- **TGP ceiling inheritance** (#181, #123): GPU power boost on `8D41` is clamped to whatever OGH last wrote. Read the
  firmware ceiling and show it, rather than silently inheriting another vendor's value.
- **A validation step for NVIDIA power unlock** (#236): "Apply CURRENT" needs a real pre-flight (driver allowlist,
  NVAPI writable policy check, expected-vs-actual readback) instead of a single reviewed driver match.
- **Throttle reason, read-only** (from the 4.5.0 roadmap, not done): expose which limit fired, via the existing PawnIO
  MSR path on Intel first.
- **Platform-profile integration on Linux**, mapping the three modes to `power-profiles-daemon` / `tuned-adm` so the
  rest of the desktop agrees with us (Victus Hub does this today).

### E. RGB

- **Per-key** for `8D87` (#179) and `8D41` (#151, #87), from the owners' own captures. Read-only probes first; no
  writes until a board answers what we asked.
- **Linux per-key via hidraw** (port of the Windows Dojo backend) — only with a named tester.
- **The probe must never leave a keyboard dark** (4.5.1 fixed the restore; 5.0.0 adds a "restore lighting" tray action
  and a visible warning before the probe runs).
- **Animation/effects parity with G-Helper and victus-control**, starting with the three effects those projects show:
  breathing, rainbow cycle, and audio-reactive, all off by default on boards where the write path is unproven.
- **Colour layout is per board, never global.** 4.5.0 proved this the hard way (#130, #212, #244). Any new layout ships
  with a board allowlist and a test that fails if a board without evidence receives it.

### F. Linux parity

- **HiDPI** (#234) and the black-window reports (#114 Wayland/KDE, #118 Ubuntu/AMD): set an explicit GDK backend and
  scale, and stop depending on XWayland defaults.
- **Systemd service + package**, replacing the zip-plus-script flow; ship the keyboard suspend hook inside the package.
- **Kernel support story**: either detect and document the required `hp-wmi` version per board, or offer the DKMS
  helper that `omen-fan-control` and `hp-omen-dkms` already ship. Report the running driver's capability in `diagnose`.
- **Tablet/Hyprland-safe window sizing**, i.e. respect `GDK_SCALE`, `Xft.dpi` and the compositor's scale factor.

### G. Model database and the verification loop

- **Close the loop on the ~25 open model requests** for boards that already have an entry: one reply template that
  says what the entry supports, what it does not, and the exact one-click export needed to promote it. Most are not
  bugs; they are unanswered.
- **Promotion criteria written down** — what makes `UserVerified = true`, per feature (fan levels, curves, GPU boost,
  undervolt, RGB topology) rather than per board. The 4.5.1 notes show the flags are already feature-scoped in
  practice; make that the schema.
- **Guided Fan Verification v2**: percentages, a real RPM check on boards that report it, and a machine-readable result
  the database generator can consume, so an export turns into a database entry in one step.

### H. Engineering hygiene

- **Docs consolidation.** 121 changelog/roadmap files and ~299 files under `docs/`. Keep `docs/` for per-version
  records, move the evergreen material (model DB guide, diagnostics format, fan theory) into `docs/guides/` and link
  it from the README.
- **Dependabot** for the three ecosystems (NuGet, GitHub Actions, pip), with the release-gate tests as the safety net.
- **CI matrix**: the Linux job runs one image; add the two distro families the reporters actually use (Arch-based and
  Ubuntu LTS).
- **Performance budget in CI**: a test that fails if idle private memory or startup time regress past a threshold.
  *Partly done 2026-11-11* (with WP-31): `qa/memory-budget.json` holds the ceilings and
  `qa/check-memory-budget.ps1` launches the Release build, measures a real startup timeline and idle footprint, and
  fails past them; a `startup-memory-budget` CI job runs it on every push. Startup is judged on the app's own
  completion (`worker_bootstrap`), not on first paint: in the same measurement period the app's startup held a
  1.7-2.2 s band while first paint ranged 2.4-12.3 s, with the whole spread inside `Show()` under software rendering,
  and a gate on that would flake rather than protect anything. First paint and idle CPU are printed, not enforced.
  Startup is judged on the median of three launches (one runner launch in three spanned 1.4 s with no code change)
  and memory on the worst attempt, against a 3000 ms ceiling taken from the worse of two observed runner medians
  (1657 and 2301 ms) rather than from the slowest single launch, which would have let a real 2 s regression through.
  Idle private memory is capped at 140 MB for the same reason: two hosts measured 104-109 MB and 128-133 MB on code
  the change could not move, so a 128 MB ceiling failed on the machine rather than on the app. The 120 MB product goal
  for an 8 GB machine is measured locally and is not this ceiling.
- **Release notes generated from the changelog**, not written twice.
- **Tests open real sensor drivers.** About 66 test sites construct `LibreHardwareMonitorImpl()`, which initialises
  PawnIO and LibreHardwareMonitor on the machine running the tests. On a desktop with an AMD Radeon the native ADL call
  `ADL2_Adapter_DedicatedVRAMUsage_Get` crashed the test host on 2026-11-12 (an `AccessViolationException` cannot be
  caught), aborting the run partway. CI runners have no such GPU, so CI never sees it. Give those tests a stub sensor
  source instead of the real bridge; this is the same rule as "never touch real hardware from tests". *Done
  2026-11-12:* `ThermalSensorProvider.FromReader` takes a delegate and opens no driver, and every one of those test
  sites now uses `TestThermals.NoHardware()` (zero readings, what the CI runners report); suite 2108/2108.

### I. Adaptations from the peer review (2026-11-11)

The tree was checked before this section was written. Game detection and per-game profiles, the OSD/FPS overlay,
hotkey remap including the OMEN key, the tray and quick popup, the refresh-rate toggle, MUX detection, battery care on
`0x24`, audio-reactive RGB across HP/Corsair/Razer/Logitech, curve share codes, profile export, scheduled rules,
NVIDIA power and the Linux daemon all already exist and are not repeated here. What follows is what is actually
missing, ranked by return per unit of work. None of it needs new hardware evidence.

- **I.1 Per-mode memory (Ohman).** `PerformanceMode` is `Name`, three wattages, `LinkedPowerPlanGuid` and a
  description — nothing else. So "Quiet" and "Performance" differ only in power limits, while every peer mode bundles
  fans, GPU mode, lighting and refresh rate and remembers them per mode. Add fan mode, curve reference, GPU
  boost/undervolt profile, lighting scene, refresh rate and charge-limit state; `GameProfile` then carries them too.
  This is an `AppConfig` change inside the schema being versioned for 5.0.0, and `PerformanceModeService` already
  tracks "applied effects", so the apply path is half-built. Highest-value item in this section.
- **I.2 A reason on every disabled control.** The database already knows why a board has curves, RGB or MUX switched
  off; today that is free-text `Notes`. Promote it to a short structured reason ("firmware refuses level writes, rc
  46", "not measured on this board") and show it on the control and in the export. Most open model requests are that
  note never reaching the screen.
- **I.3 Controller watchdog.** `CurveRecoveryService` covers only the in-process case. A scheduled task that checks the
  applied state against the config and re-applies — or hands back through `--restore-fans` — survives a kill, an OOM
  or a Windows update restart. Peer precedent: `omen-fan-control`.
- **I.4 Accessibility and font scale.** Mostly landed 2026-11-11 (WP-45): 335 `AutomationProperties.Name` attributes
  across the views (counted in the XAML) plus one per navigation tab (their headers are a `Path` and a `TextBlock`, which gave a screen reader no
  label), a reduced-motion path (`MotionPreference`, tested, gated on the OS setting with a per-user override), and a
  high-contrast palette (`HighContrastTheme`) that repaints the twelve chrome brushes from `SystemColors` at startup
  and on change. The brushes are mutated in place rather than swapped by dictionary, because `StaticResource` binds at
  load time — which is why those twelve carry `PresentationOptions:Freeze="False"`, enforced by a test. Still open:
  a font-scale setting. Every view sets explicit `FontSize` values, so neither an inherited size nor a style setter
  reaches them; doing it properly means the size/string extraction in WP-32/WP-33, and the zoom there (#48.8) is the
  right place. #234 is the separate HiDPI bug. XAML only, no hardware risk, so it fills any phase waiting on field
  data.
- **I.5 Size and packaging budget.** OmenMon's whole pitch is 14 MB against OGH's ~1.4 GB. Publish a portable
  single-file build and hold a size budget in CI beside the memory budget. A credibility artefact as much as a
  technical one, and the easiest claim of the cycle to check from the website.

Smaller items, folded into existing packages rather than opening new ones: a user-set maximum duty ceiling (I.6, into
WP-37), tray dust-expulsion with state restore (I.7, WP-37), an "on battery → Quiet + 60 Hz + charge limit" preset in
`PowerAutomationService` (I.8, WP-36), full-profile share codes extending `FanCurveShareCode` (I.9, WP-48),
`config --validate` against the schema (I.10, WP-35 — done 2026-11-11 as
`omencore-cli config --validate`, reporting loader repairs as well as out-of-range values), SBOM plus `dotnet nuget audit` in CI (I.11, WP-42), and a
website supported-model table generated from the database rather than written by hand (I.12, WP-42).

## 5. Definition of done for 5.0.0

5.0.0 ships when all of these are true. Anything else is 5.1.

1. Installer and binaries are signed; SmartScreen shows the publisher (#192 closed by evidence).
2. The updater verifies signature and hash before elevation, and the release publishes `SHA256SUMS` plus provenance.
3. The UI no longer runs elevated for hardware writes, or — if the service split slips — the split is scoped, written
   down, and scheduled as 5.0.x with an explicit explanation in the notes.
4. App startup (`worker_bootstrap`) under 2 s on the median of three launches, enforced in CI, and idle private memory
   under 120 MB on an 8 GB machine, measured locally (the CI ceiling stays a regression gate). The earlier "< 1 s to
   interactive" target is not reachable with WPF plus the SystemInfo WMI population, which the WP-31 profile measured;
   first paint is reported, not gated, for the reasons in section 4.H.
5. A portable single-file build exists and both the size and memory budgets are enforced in CI (I.5).
6. No page scrolls for empty space at 100 % scaling on a 1080p panel; HiDPI works on Windows and Linux (#234 closed).
7. Every control that cannot do what it says is disabled *with a reason the user can read*, and the same reason is in
   the diagnostics export (I.2).
8. Keyboard navigation, screen-reader names, high contrast and a font-scale setting work end to end (I.4).
9. Shell strings are externalised and Russian and Turkish are shipped (#242 closed).
10. Modes remember their fans, GPU state, lighting and refresh rate, and a game profile can carry all of it (I.1).
11. A killed or crashed controller recovers the configured state without the user noticing, or hands the fans back and
    says so (I.3).
12. Every fan report in the open list has either a fix with a test or a written reason it cannot be fixed (#227, #213,
    #218 answered on the issue).
13. Battery Care on 0x2B landed (#243), and the TGP ceiling is reported on `8D41` (#181).
14. Full suite green, 0 build warnings, and a migration path from a 4.x config.
15. `README`, website, model-database docs and `CLAUDE.md` all describe 5.0.0 and nothing older.

## 6. Work packages

Phases are ordered so that nothing waits on hardware, and so the irreversible items (signing, service split) happen
while there is still time to test them.

| Phase | Package | Scope | Needs |
|---|---|---|---|
| 1 | WP-30 | Signing decision + Authenticode check before elevation; `SHA256SUMS`, provenance and the updater reading the checksum file done 2026-11-13 | Maintainer (certificate) |
| 1 | WP-31 | ~~Memory/startup profiling pass, then the fixes it names~~ — done 2026-11-11, see §4.B | Nothing |
| 1 | WP-32 | i18n plumbing done; shell + Settings (window/tray) translated; ~1,060 literals remain | Nothing |
| 1 | WP-43 | Per-mode memory: `PerformanceMode` fields, migration, apply path (I.1) | Nothing |
| 1 | WP-44 | Structured capability reasons in the DB, the UI and the export (I.2) | Nothing |
| 2 | WP-33 | zoom + display scale done; profile-card density tightened; switches instrumented (views were already cached); page rework remains | Nothing |
| 2 | WP-34 | HiDPI Windows + Linux (#234, #114, #118) | Nothing |
| 2 | WP-35 | Privilege split design + service skeleton; `config --validate` done (I.10) | Architecture decision |
| 2 | WP-36 | Battery Care 0x2B (#243), TGP ceiling readout (#181), battery preset (I.8) | PR in flight |
| 2 | WP-45 | Accessibility: names, high contrast, reduced motion done; font scale moves to WP-33's zoom (I.4) | Nothing |
| 2 | WP-46 | Portable single-file build + size budget in CI (I.5) | Nothing |
| 3 | WP-37 | Better Auto (#246, #189), curve in %, maximum duty ceiling (I.6), dust expulsion (I.7) | Owner A/B |
| 3 | WP-38 | Hand-back hint resolution (#227, #217, #143) | Owner A/B |
| 3 | WP-39 | Per-key RGB 8D87 / 8D41 (#179, #151) | Owner captures |
| 3 | WP-47 | Controller watchdog: verify applied state, re-apply or hand back (I.3) | Nothing |
| 4 | WP-40 | Linux packaging + service + DKMS story | Nothing |
| 4 | WP-48 | Full-profile share codes on top of `FanCurveShareCode` (I.9) | Nothing |
| 4 | WP-41 | Model-request reply sweep + DB promotion criteria | Nothing |
| 4 | WP-42 | Docs consolidation, Dependabot, CI matrix, perf+size budget, SBOM (I.11), generated model table (I.12) | Nothing |

## 7. Open-issue index (90 open on 2026-11-11, 78 after the housekeeping sweep)

Twelve issues were closed as completed against shipped code on 2026-11-11 (`#115`, `#172`, `#195`, `#199`, `#205`,
`#207`, `#211`, `#212`, `#213`, `#214`, `#220`, `#241`); the buckets below are what remains.

| Bucket | Count | Handling |
|---|---|---|
| Model / board requests for boards that already have an entry | ~25 | WP-41: answer with what the entry does and the one-click export; close when the reporter's ask is met |
| Fan behaviour (stuck, hunting, hand-back, curves) | ~10 | WP-37, WP-38; several need an export taken at the moment of the fault |
| RGB / keyboard lighting | ~7 | WP-39; board allowlist only |
| UI/UX and performance | 4 (#232, #133, #48, #234) | WP-31 to WP-34 |
| Linux support (boards, drivers, packaging, scaling) | ~12 | WP-40 plus per-board notes |
| Feature requests (hotkeys, RAM clean, i18n, integrations) | ~8 | WP-32/33, or 5.1 |
| Code-review / contribution threads (#245, #246) | 2 | Maintainer review |

## 8. Risks and things this cycle will not do

- **No VBIOS flashing, no bundled `nvflash`, no voltage/clock tuning.** Unchanged from 4.5.0, for the same reason.
- **No unverified EC writes to close an issue faster.** The 4.5.0/4.5.1 record (#130, #212) is what that costs.
- **The service split is the schedule risk.** It touches every hardware path. If it slips, 5.0.0 still ships signed
  and packaged, and the split becomes 5.1 with the design note published.
- **No new sensor sources** without a board that reports them; the export stays the source of truth.
- **Not adopted from the peer review**, for the reasons given here rather than because they are hard: a PresentMon
  benchmark overlay (licence and a standing overhead budget the 4.4.5 work would have to protect, and the OSD already
  covers the common case), TPP calibration scanning (write loop with no live validation), VBIOS work, a built-in task
  manager (Windows plus the existing bloatware view), and per-app RGB rules (no board evidence, and it multiplies the
  configuration surface I.1 is trying to simplify).
