# OmenCore v5.0.3

**Release Date:** not yet released
**Type:** Fix release: per-key keyboard on the OMEN MAX 16z-ak000.

## Fixed

- **OMEN MAX 16z-ak000 (`8DD5`): the per-key editor said the keyboard does not support per-key control** (Discord).
  This board is the configure-to-order AMD build of the OMEN MAX 16-ak0xxx (`8D87`), whose per-key keyboard OmenCore
  already drives. The "z" in its model name ("16z-ak000") kept it from matching that entry, so it fell to the default
  four-zone keyboard, whose commands on this chassis reach the light bar, not the keys. `8DD5` now has its own keyboard
  entry and tries the same per-key backend first; if that keyboard is not there, the zone controls still work as
  before. *Implemented, pending confirmation on 8DD5.*
- **Bloatware: adding a second app to the remove list replaced the first** (#177). "Remove Selected" now adds to the
  list that is already open, so several apps can be queued and removed together.

## Added

- **Victus 15-fb0xxx AMD (`8A3C`)** (#271, submitted by beamchik): an exact entry. It used to borrow the `8A3E` entry
  by name, which says 1 fan where this firmware reports 2. Guided Fan Verification passed 6/6 twice; fan curves on,
  independent curves off, not marked field-confirmed.

## Confirmed

- **Linux Max on the `8EEC` driver** (#238, from 5.0.2): `omencore-cli fan --speed 100` reaches full speed, confirmed
  by the reporter.

## Investigated, not fixed yet

- **Victus `8C2F`: manual fan control after exit** (#246). mecaniko-tallerOS traced the exit path: the shutdown reset
  writes fan level 20 to both fans as a "transition hint" before handing back to Auto, and that write may be what
  leaves the fans in manual control. The existing experimental setting "Do not pin a fan level when returning to
  Auto" skips that write, so an A/B with it on and off can settle it before the exit path changes.
- **Victus 16-s0 `8BD4`: fans do not follow temperature** (#226). In Custom, the reading falls from about 2400 to 1200
  RPM while OmenCore re-sends the level every 15 to 20 seconds, so the firmware accepts the level and does not hold
  it. Same family of V1 behaviour as #227 and #143; needs a focused test.
- **AMD Cezanne (`8A3C`): the STAPM slider has no effect** (#272). The SMU accepts the STAPM write and the limit stays
  at 35 W; only the PPT limits move. Not yet traced.
