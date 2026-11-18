# OmenCore v5.0.2

**Release Date:** not yet released
**Type:** Fix release: fan hand-back after verification, Linux Max on newer `hp-wmi` drivers, small UI fixes.

## Added

- **Custom fan curves on the Victus 15 `8E5F`** (#249). The reporter ran Guided Fan Verification on 5.0.1: 6/6, 80/100
  (fan-level evidence), the condition the 5.0.1 entry set for turning curves on.
- **Recorded field results:** `8DCD` Guided Fan Verification 6/6 on 5.0.1 (#143; CPU 100 % at the firmware ceiling,
  level 50 of 55). `8C2F` GPU fan tachometer tracked a GPU load up and back down on 5.0.1 (2,400 → 3,317 → 2,712 RPM)
  with the BIOS in Auto at all 49 checks (#246), so both tachometer channels from 5.0.1 are now hardware-checked.

## Fixed

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

- **AMD Curve Optimizer showed "Degraded" for a rounded value** (#222, Ryzen 9 8940HX on `8E35`). Offsets go to the
  SMU in whole counts of 4 mV-equivalent, truncated toward zero, so −15 is applied as −12. The status now says it was
  applied at the nearest Curve Optimizer step instead of reporting a mismatch; a difference of a full step or more,
  and any difference on Intel, still shows as a mismatch.

## Investigated, not fixed yet

- **Victus `8C2F`: manual fan ownership after the app exits** (#246). mecaniko-tallerOS found the EC in manual control
  after the official 5.0.1 build exited, on the board where the BIOS stays in Auto for the whole time the app runs. The
  exit path is what their BIOS Auto handoff patch (`0001`) addresses; it is under review for 5.1.
- **Board `878A` still shows "Model Not Yet Field-Confirmed"** (#218): needs a Guided Fan Verification run and an
  export on 5.0.x to promote.

## Pull requests

- **#263** (Kenyi001): an exact entry for the OMEN 16-k0xxx `8A4D`. Under review: the entry marks the board as
  user-verified, which changes how firmware fan counts are trusted, so it needs the export and Guided Fan Verification
  result behind it. The same board's Battery Care report (#243) is not covered by it.
