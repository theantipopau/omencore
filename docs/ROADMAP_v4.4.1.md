# OmenCore v4.4.1 Roadmap

**Status:** In progress. Opened 2026-09-25, one day after v4.4.0 shipped (and was republished the
same day for the in-app updater fix — see `docs/CHANGELOG_v4.4.0.md`).
**Base version:** v4.4.0
**Predecessor doc:** `docs/ROADMAP_v4.4.0.md` — carried the 4.3.1 → 4.4.0 cycle. That document is now
historical record.

---

## Why This Cycle Exists

The day after 4.4.0 shipped, real field data started coming back against it: three new GitHub
issues with diagnostics exports (`#211`, `#212`, `#213`), a new Guided Fan Verification result on
an existing report (`#207`), and a mode-switch confirmation on another (`#195`). Also reviewed the
project's active community forks for anything worth pulling into `main`, and worked through a
Discord report about fan presets not surviving a reboot.

---

## Done

### GitHub #211: Board `8BBE` (Victus 16-r0xxx, Intel) Given an Exact Entry

Was resolving via generic Family fallback (`IsKnownModel: no` in its own diagnostics trace) since
first being noted in `#172` and traced further in `#198`. This export finally provided real data:
WMI fan control and V1 thermal policy confirmed live (`FanCommandHistoryCount: 80`, thermal
protection engaging/releasing correctly). Fan curves, GPU Power Boost, RGB and undervolt weren't
exercised, so those stay conservative. `RequiredCpuVendor = Intel` mirrors the guard `8C2F` already
carries in the other direction — the two entries now share the `16-r0` WMI name pattern from
opposite vendor guards, so the existing vendor-mismatch tests were updated to check both directions
resolve to the correct entry instead of the old "must return null" assertion (which was pinning the
*absence* of an `8BBE` entry, not a property worth keeping once it has one). 3 tests.

### GitHub #212: Zone-Colour RGB Architecture Gap — Targeted Fix Implemented, Pending Confirmation

An exceptionally thorough report: board `8BD4`'s own firmware topology probe reports
`OneZoneWithNumpad`, not four-zone, and every RGB backend tested (WMI, explicit Wmi, forced EC)
failed colour verification the same way (accepted write, black or mismatched readback). Traced to
source: `WmiBiosBackend.ZoneCount` and `EcDirectBackend.ZoneCount` are both hardcoded to `4` -
neither reads the model database's `HasFourZoneRgb` flag or the live topology probe result before
building the colour payload, so a 4-zone, 12-byte `ColorTable` is sent regardless of what the
keyboard controller actually is.

**Explicitly did not "fix" this by flipping `HasFourZoneRgb` to `false` for `8BD4`.** That flag has
never gated zone *count* anywhere in the write path - it only gates whether
`CapabilityDetectionService` offers zone lighting *at all*. Flipping it trips the "no zone lighting"
branch and falls back to `LightingCapability.SingleColor`, which per `IKeyboardBackend` means
`SetBacklight` on/off only, no colour whatsoever - strictly worse than the wrong-zone-count write
this board already has, on a board OMEN Gaming Hub colours correctly. Reverted that change before it
landed; documented the real finding in the model database's own Notes instead, and asked the
reporter for one cheap diagnostic (does a uniform same-colour-in-all-four-slots write verify where
four different colours didn't) that would narrow down the real single-zone byte layout without
guessing at it.

**Follow-up evidence (2026-09-25):** the reporter ran that diagnostic - identical colour in all four
zone slots - and it failed exactly the same way. That rules out "only slot 0 is read" and leaves the
more precise hypothesis: `HpWmiBios.SetColorTable`'s own 128-byte payload hardcoded byte 0 (its own
declared zone count) to `4` for every board, unconditionally, and the firmware may be validating that
byte against its own real topology and silently discarding the whole write on a mismatch - which
matches the exact failure signature (WMI-transport-level success, firmware-level silent rejection)
even with a correctly-formed uniform-colour write.

**Implemented, pending confirmation:** `HpWmiBios.SetColorTable` takes a `zoneCount` parameter
instead of hardcoding `4` into byte 0; a new `HpWmiBios.MapLightingTypeToZoneCount` maps the live
topology probe onto it (`OneZoneWithNumpad`/`OneZoneWithoutNumpad` → 1, everything else → the
historical default of 4); `WmiBiosBackend.ZoneCount` now reads that mapping live instead of
returning a hardcoded `4`, and `SetZoneColorsAsync` passes it through to `SetColorTable`. Everything
else about the write - the 12-byte, 4-slot colour payload itself - is deliberately **unchanged**,
because the real single-zone byte layout is still not known; this tests only the one piece there is
hard evidence for. `EcDirectBackend` was deliberately left untouched: its hardcoded EC registers
(0xB1-0xBC) are a different, older-generation mechanism (OMEN 15/16/17 2020-2022) with no declared-
zone-count byte at all, and `8BD4` (a 2023 board) has no `EcColorRegisters` override, so its EC-path
failure is a separate, expected limitation, not this bug. 10 new tests (8 for the zone-count mapping,
covering every topology value; the existing `HasFourZoneRgb` pin stays in place). **Not yet confirmed
on real hardware** - if colour still fails after this ships, the byte-0 hypothesis is wrong and the
real single-zone layout remains unknown; asked the reporter to test once a build is available.

**This is very likely NOT unique to `8BD4`.** Any board in the model database whose live topology
probe reports `OneZoneWithNumpad`/`OneZoneWithoutNumpad` is exposed to the identical bug, and now
gets the same byte-0 fix automatically the next time it's queried live - see "Open Investigations"
below for what's still unconfirmed.

### GitHub #207: Guided Fan Verification's 100% Test Had the Same "Max Ignored" Gap Already Fixed Elsewhere

A fresh Guided Fan Verification run on board `88F8` showed CPU@100%/GPU@100% both reading the exact
same RPM estimate as the 60% test just before them, with `evidence: None`. The 30/60% tests write a
discrete fan level and pass; the 100% test uses `SetFanMax(true)` instead (to reach the board's true
mechanical ceiling rather than a BIOS-capped level), and this board's firmware accepted that command
and never moved past wherever the fan already was - the same "firmware accepts Max, ignores it"
class of bug already fixed this cycle in `WmiFanController`'s maintenance loop (Ohman cross-check,
boards `8A26`/`8E5E`), reached through a completely different code path
(`FanVerificationService.ApplyAndVerifyFanSpeedAsync`'s one-shot apply, which has no equivalent
fallback). Fixed: after all verification retries fail with `SetFanMax`, one direct
`SetFanLevel(ExpectedLevel, ...)` write is tried before giving up. Also fixed in the same pass: the
existing "`SetFanMax` returned false" fallback path sent a hardcoded level of `55` regardless of the
board's real ceiling - now uses the same `ExpectedLevel` every other write in this method already
does. Needs real hardware to confirm; the retry logic can't be exercised without a live
`HpWmiBios`/`FanService`, same limitation as the `WmiFanController` fix it mirrors.

### Community Fork Reviewed: `ujjawalkaushik1110/omencore` — Two Real Gaps in `HardwareWatchdogService`

Not merged as-is (the branch also carried a duplicate, conflicting `8DD0` database entry claiming
`UserVerified = true` off one local diagnostic, and an unrelated ~1,300-line window-management CLI
feature) - reimplemented the watchdog fix directly against current `main` instead, with full credit
in the code and commit. Two real gaps found and fixed:

1. **The freeze failsafe released on the very next telemetry sample, regardless of what that sample
   actually read.** "The monitoring pipeline is alive again" was being treated as "safe to hand fans
   back to BIOS Auto" - the one thing the failsafe exists to prevent (BIOS Auto reclaiming a hot
   machine) was exactly what an inopportunely-timed release could do. Release now requires
   temperatures at or below 65°C - deliberately well under both `FanService`'s real ~90°C thermal-
   protection ramp and the 85°C informational-toast threshold - held for 15 continuous seconds.
2. **Once the failsafe activated, nothing ever touched the fans again.** `CheckWatchdog`'s own
   freeze-detection branch returned immediately on every subsequent tick while `_failsafeActive` was
   true, so the 90% fan speed was applied exactly once. Anything that reset fan state in the
   meantime (OGH, a firmware reassert, another controller) could silently undo it with nothing
   watching. The failsafe now reapplies itself every 15 seconds for as long as it stays active - the
   same "don't just fire once and hope" principle as the `WmiFanController`/`FanVerificationService`
   fixes above, applied to a third code path that had the same gap.

4 new tests cover the pure temperature-validity helper; the timer-driven reapply/release-hold
behavior needs a real `FanService` and real elapsed time, which this suite doesn't have.

### Discord: Fan Preset Not Surviving Reboot — Not a Bug, a Discoverability Gap

Two independent reports (Lune, board Victus 16-s0117nq; WilliamM404) described the same thing: a
custom fan preset reverts to Auto after a reboot, read as "fans suddenly go crazy." Traced the
actual code path (`StartupRestorePolicy`) rather than assuming a bug: `EnableStartupHardwareRestore`
alone is not enough on Victus or OMEN 16-class laptops - a second guardrail
(`AllowStartupRestoreOnOmen16OrVictus`) has to be explicitly enabled too, "for models that have
reported firmware sensitivity during boot." Both toggles already exist in Settings with real
explanatory text; the feature works as designed once both are found. No code change - this is a
support answer, not a fix, and changing the safety gate itself was never on the table without new
evidence about why it exists.

### Backlog Audit (2026-09-26): GPU Boost Display Gate, `88F8`, and Old Model Requests

Went back through the open backlog against current code, and analyzed three diagnostics exports that
had never been read (`#155`, `#184` on `8C2F`; `#207` on `88F8`).

- **GPU Power Boost display gate (fixed).** All three exports showed "Show GPU Power Boost: Yes" on a
  Victus while `SystemControlViewModel.DetectGpuPowerBoost` logged that it refuses every Victus
  without an explicit opt-in. Root cause: `HasGpuPowerControl` is set whenever WMI BIOS exists, so
  "runtime detection wins" meant "always shown". This is the concern behind PR `#210`'s
  `ShowGpuPowerBoost` change; that change applied the model flag to every family, which would also
  have hidden the tray entry on 10 known non-Victus OMEN boards whose backend still probes WMI and
  can enable it. Narrowed to exactly the backend's Victus rule instead. 4 tests.
- **`88F8` entry (added).** From the `#207` export - see changelog. The family fallback had also been
  overriding the firmware's Fan Count 2 down to 1; the exact entry corrects that for this board.
  Other unknown Victus boards still get the family default of 1 - worth revisiting separately.
- **`#115`/`#172`** are the same misidentification the `8BBE` entry fixes.
- **`8C2F` 15" chassis (`#155`, `#184`)**: neither export exercised fans beyond a mode switch, so no
  promotion. `#184` shows keyboard topology `Normal` (backlight only) on the 15" while the entry
  claims `HasFourZoneRgb = true` from the 16" report - one ProductId, two keyboards. Not changed:
  flipping it would cost the 16" colour control (see `#212` on why that flag can't be per-chassis).
  The WMI topology probe already reports the truth at runtime.
- **`8C58` (`#149`/`#156`)**: fan curves are off on purpose, no export to justify enabling them.

---

## Open Investigations

### Zone-colour RGB: byte-0 fix shipped for `WmiBiosBackend`, EC path and the real payload layout still open

`WmiBiosBackend.ZoneCount` is now live and board-aware (done above); `EcDirectBackend.ZoneCount`
still hardcodes `4` unconditionally and was deliberately left alone this pass - see #212 above for
why. Even with the `WmiBiosBackend` fix, the real single-zone `ColorTable` byte *layout* (as opposed
to just the declared count in byte 0) is still unconfirmed - if HP's firmware wants something other
than "12 bytes, 4 RGB triples" for a 1-zone board, this fix alone won't be enough. Needs: (1)
hardware confirmation the byte-0 fix actually resolves `#212`, or evidence it doesn't; (2) if it
doesn't, the real single-zone WMI byte layout from a board owner; (3) a decision on `EcDirectBackend`
once the WMI path is settled - it has no declared-zone-count byte at all, so any fix there would be a
different shape entirely; (4) a survey of which model-database entries with `HasFourZoneRgb = true`
have never had their zone count independently confirmed, since this bug would have been silently
misattributed as "keyboard may not support this method" on every one of them rather than traced to
its real cause.

### GitHub #213 — fans stuck at max regardless of preset, board `8DD0`

No diagnostics export attached, only a screenshot. `8DD0` already has a real, evidence-backed entry
(fan curves and RPM readback both confirmed by a prior contributor's PR), so this reads as a genuine
behavior bug rather than a missing-model report - but nothing can be traced from a screenshot alone.
Asked for an export captured while the symptom is happening. **Update 2026-09-26:** reporter says
it cleared after a restart; now asking about occasional high temps at low load — needs an export
taken while it's happening (likely background load or BIOS fan policy, nothing to trace yet).

### Carried forward from v4.4.0, unchanged

- Board `8E35` Performance mode (`#195`) — WMI policy fallback confirmed to fire correctly during a
  live game session. **Update 2026-09-26:** a second session with LibreHardwareMonitor as an
  independent reader, across five alternating segments, showed Balanced package power plateauing at
  ~66-72 W max while Performance peaked at 78-80 W (~+3.5 W average, higher temps) — versus a 0.0 W
  difference before 4.4.0 enabled the fallback. Field-supported, not fully confirmed: the actual
  firmware limit values are still unread (RyzenAdj fails with `Unable to get os_access Obj`), so the
  entry stays `UserVerified = false`.
- The `#199` sidebar/dashboard performance-mode label mismatch — **fixed 2026-09-26.** Root cause:
  the reporter's clue was "after reboot, startup restore left disabled". `SystemControlViewModel`
  and `MainViewModel.HydrateCollections` both seeded the saved `LastPerformanceModeName` as the
  *current* mode, even when the startup restore was skipped; the dashboard/General labels come from
  runtime-confirmed state and correctly said "Default". Those two labels now say "Default" until a
  mode is actually applied (any path: picker, tray, hotkey, automation). Picker selection unchanged.
- `#189` automatic fan curves — still the most-cited gap, no code yet.
- The `8D87` GPU power unlock — still gated off (`EcWritePathValidated = false`), needs an 8D87
  owner to validate the EC write path and the rewritten pin-loop model before any of it can turn on.

---

## Standing Rules (unchanged, carried from v4.4.0)

- **Evidence gate.** Fan/EC/thermal/OC/UV *behavior* changes need field validation before shipping.
  Architecture, performance, display-honesty, and pure-UI items do not.
- **A fix that isn't confirmed yet is not the same claim as a fix that is.** Say which one it is,
  every time, including in this document.
- **Reviewing a fork or a PR means reading what it actually changes, not just what it says it
  does.** Two of `ujjawalkaushik1110`'s four branches carried a duplicate, unverified board entry
  bundled in with a genuinely good fix - taking the good part meant not taking the branch.
- **Update this document as you go.**
