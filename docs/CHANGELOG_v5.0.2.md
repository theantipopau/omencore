# OmenCore v5.0.2

**Release Date:** 2026-11-23
**Type:** Fix release: fan hand-back after verification, GPU temperature spikes on battery, NVIDIA power unlock on newer drivers (Prophecy 1.2.9), Linux Max on newer `hp-wmi` drivers, Simplified Chinese, small UI fixes.

## Added

- **Custom fan curves on the Victus 15 `8E5F`** (#249). The reporter ran Guided Fan Verification on 5.0.1: 6/6, 80/100
  (fan-level evidence), the condition the 5.0.1 entry set for turning curves on.
- **Recorded field results:** `8DCD` Guided Fan Verification 6/6 on 5.0.1 (#143; CPU 100 % at the firmware ceiling,
  level 50 of 55). `8C2F` GPU fan tachometer tracked a GPU load up and back down on 5.0.1 (2,400 → 3,317 → 2,712 RPM)
  with the BIOS in Auto at all 49 checks (#246), so both tachometer channels from 5.0.1 are now hardware-checked.
  `8C2F` 15-inch chassis (15-fb2772nd, BIOS F.09) Guided Fan Verification 6/6 on 5.0.1 (#268).

- **Simplified Chinese interface** (#266, by pkq66882, #69): a `zh` catalog for every translated string, 简体中文 in
  the Settings language list, and Microsoft YaHei UI as the font fallback for Chinese text anywhere in the app (game
  and profile names, paths), so it no longer depends on whichever face Windows picks per glyph.

## Fixed

- **NVIDIA Power Unlock: Apply CURRENT stayed greyed out on every NVIDIA driver except 617.14** (Discord). CURRENT
  unlocks only on the reviewed driver or after the driver has been validated, and the validation step existed in the
  backend but no button reached it. Tuning now shows **Validate driver** when the installed driver needs it. The
  backend is updated to bobshmo's Prophecy Power Unlocker 1.2.9 (used with permission), which changes what that step
  sends: it now repeats the driver's own current power request instead of the public power ceiling, which can be
  higher (140 W over a 115 W request) and would have changed power in a step meant to change nothing. Also from
  1.2.9: Apply CURRENT no longer "rolls back" to a value guessed from the public ceiling, and reports whether the
  driver's own request reads back at the target; NVIDIA errors are reported without calling back into a failing
  driver. OmenCore's Victus preparation around CURRENT, its per-GPU target range and its settings folder are
  unchanged. *Implemented, pending confirmation on hardware.*

- **Fans stayed pinned at the last test level after Guided Fan Verification** (#143, Victus 15-fa2xxx `8DCD`, thermal
  policy V1). A verification run ends with the fans at a level written directly (55 here), and when the run started in
  Auto the plain Auto hand-back (`SetFanMode(Default)`, with the level hint skipped by the experimental "Do not pin a
  fan level" setting) did not clear it, so the fans stayed at about 5500 RPM. Restore OEM Auto always recovered them on
  that board. Both the guided run and the single-step diagnostic now end with that same full reset when they started in
  Auto, then re-apply the Auto preset so the interface shows what the user had. Runs that started in another preset
  re-apply that preset as before. *Implemented, pending confirmation on 8DCD.* The reporter's first pin, at idle with
  no verification run, is not explained by this and is still open.
- **Linux: Max still did not reach full speed on the `8EEC` driver** (#238). 5.0.1 stopped `pwm1_enable=0` from
  stopping the fans, but on this driver it settles near the BIOS-auto speed (1900 / 2200 RPM from idle), which the
  5.0.1 check (stopped or slower than before) accepted, while manual duty reaches 4300 / 4600 RPM. When the driver
  publishes `fan1_max`, full speed now has to reach 70 % of it; otherwise Max switches to manual duty 100 %. Boards
  without `fan1_max` keep the 5.0.1 rule. *Implemented, pending confirmation on 8EEC.*
- **The page scrollbar lost the app theme at zoom levels other than 100 %** (#246, found and fixed by
  mecaniko-tallerOS): the outer page scroller now uses the same style as the pages inside it.

- **GPU temperature spiking on battery, with the fans chasing it** (#177, OMEN 16 wf0, RTX 4060). Two sensors fed
  one reading: the HP BIOS GPU sensor on every poll and NVIDIA's die sensor, which on battery is read only every
  10 s to let the GPU sleep. When the two disagreed, the reading flipped between them. The die reading is now kept
  until its next refresh is due (plus 5 s), and the BIOS sensor is used only when NVIDIA has not answered. It should
  also cover the short 80 °C+ blips reported on AC, if those were a missed NVIDIA read. *Implemented, pending confirmation.*
- **"Temperature warnings" off still showed the critical temperature alert** (#177). The switch now covers that
  alert too. Thermal protection still raises the fans on a hot CPU or GPU whatever the switch says.
- **Games added by hand disappeared from the Games tab after a restart** (#177). Scanned games were already found
  again on every open; hand-added ones are now saved in the config and restored, skipping any that were deleted.
- **AMD Curve Optimizer showed "Degraded" for a rounded value** (#222, Ryzen 9 8940HX on `8E35`). Offsets go to the
  SMU in whole counts of 4 mV-equivalent, truncated toward zero, so −15 is applied as −12. The status now says it was
  applied at the nearest Curve Optimizer step instead of reporting a mismatch; a difference of a full step or more,
  and any difference on Intel, still shows as a mismatch.

## Changed

- **The "Model Not Yet Field-Confirmed" warning is now a calm, dismissible note.** It was an orange warning box on
  every page for every board without a confirmed entry, which is most boards, and users read it as "my laptop is not
  supported" (r/HPOmen thread on OMEN Gaming Hub alternatives, November 2026). It now says the laptop is supported with
  its family's standard profile, that Diagnostics shows which advanced options are off and why, and has a "Got it"
  button that hides it for that board. Another board, or this config restored on a different laptop, shows it again.

- **The OMEN key action list shows readable names** ("Cycle fan modes", "Max cooling on / off") instead of internal
  identifiers such as `CycleFanMode`, and the two duplicate alias entries are gone. A saved alias still selects its
  entry and behaves as before.

## Investigated, not fixed yet

- **Victus `8C2F`: manual fan ownership after the app exits** (#246). mecaniko-tallerOS found the EC in manual control
  after the official 5.0.1 build exited, on the board where the BIOS stays in Auto for the whole time the app runs. The
  exit path is what their BIOS Auto handoff patch (`0001`) addresses; it is under review for 5.1.
- **Board `878A` still shows "Model Not Yet Field-Confirmed"** (#218): needs a Guided Fan Verification run and an
  export on 5.0.x to promote.

## Pull requests

- **#263** (Kenyi001): an exact entry for the OMEN 16-k0xxx `8A4D`. Under review: the entry marks the board as
  user-verified, which changes how firmware fan counts are trusted, so it needs the export and Guided Fan Verification
  result behind it. The same board's Battery Care report (#243) is not covered by it. Kenyi001 has since run the
  verification (6/6); waiting on the export and two small changes.
- **#259, #250** (Dependabot): minor NuGet updates (FluentAssertions, Moq, test SDK, System.Management and others) and
  the Pages / build-provenance workflow actions. #258 and #262 were duplicates of #259 and are closed.

## Community feedback (r/HPOmen, November 2026)

A thread comparing OMEN Gaming Hub alternatives (OmenCore, OmenMon / OmenMon Reborn, Ohman). What users said about
OmenCore and what they liked elsewhere, with what this cycle and the next do about it:

| Feedback | Where it goes |
|---|---|
| "Sluggish and buggy" (most-upvoted comment; likely an older build) | 5.0.0 cut startup and idle memory and gates both in CI; the fan curve page still stutters (#222) and is on the 5.1 interface list. |
| "Cluttered with stuff you don't need" | 5.1: a simpler default view with the advanced pages one click away. |
| "Doesn't detect my laptop as official" | 5.0.2: the unconfirmed-model warning is now a calm, dismissible note (above). |
| Ohman: lightweight, many models, least-hassle setup | 5.1 first-run screen; model entries keep growing from exports. |
| Ohman stops OMEN background processes at boot, so OGH need not be uninstalled | 5.1: offer to pause the OMEN background services while OmenCore runs, reversibly, instead of only the cleanup tool. |
| The OMEN key cycling fan modes is convenient | Already available: Settings → OMEN key → "Cycle fan modes". 5.0.2: readable names in that list (above). 5.1: offer it on first run. |
| Separate CPU and GPU fan curves | Supported where the board allows independent curves; say so on the Fan page when it is off and why. |
| Desktop OMEN (45L) support | Monitoring only today; fan writes stay disabled until there is desktop evidence. Tracked for later. |
| OMEN 17 macro keys P1-P6 | Feature request; needs the key events from an owner's export. |
