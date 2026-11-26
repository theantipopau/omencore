# OmenCore v5.0.3

**Release Date:** 2026-11-26
**Type:** Fix release: thermal protection on the OMEN 15-en1xxx, per-key keyboard on the OMEN MAX 16z-ak000, three new
board entries.

## Fixed

- **OMEN 15-en1xxx (`88D2`): thermal protection read a stuck CPU temperature before a thermal shutdown** (#274). This
  board's WMI temperature call returns the same value on every sensor index. Each time the real-sensor read failed,
  CPU temperature authority fell back to that value, and in the reported session thermal protection read about 44 °C
  for the last three minutes before Windows shut down at the ACPI `_HOT` limit. The 15-en1xxx now always takes CPU
  temperature from the real sensor, and on such boards a failed read keeps the last recent real reading instead of
  the WMI value. *Implemented, pending confirmation on 88D2.* Whether the Quiet preset (firmware Cool policy) held the
  fans low under load is not yet measured.
- **OMEN MAX 16z-ak000 (`8DD5`): the per-key editor said the keyboard does not support per-key control** (Discord).
  This board is the configure-to-order AMD build of the OMEN MAX 16-ak0xxx (`8D87`), whose per-key keyboard OmenCore
  already drives. The "z" in its model name ("16z-ak000") kept it from matching that entry, so it fell to the default
  four-zone keyboard, whose commands on this chassis reach the light bar, not the keys. `8DD5` now has its own keyboard
  entry and tries the same per-key backend first; if that keyboard is not there, the zone controls still work as
  before. *Implemented, pending confirmation on 8DD5.*
- **Bloatware: adding a second app to the remove list replaced the first** (#177). "Remove Selected" now adds to the
  list that is already open, so several apps can be queued and removed together.

## Added

- **OMEN 16-k0xxx Intel (`8A4D`)** (#263, contributed by Kenyi001): an exact entry for this 2022 board. Two fans,
  max fan level 55, four-zone keyboard. Guided Fan Verification passed 6/6 on 5.0.2 (BIOS F.29); MUX switch, GPU Power
  Boost and independent curves stay off until there is evidence for them. Not marked field-confirmed.
- **Victus 15-fb0xxx AMD (`8A3C`)** (#271, submitted by beamchik): an exact entry. It used to borrow the `8A3E` entry
  by name, which says 1 fan where this firmware reports 2. Guided Fan Verification passed 6/6 twice; fan curves on,
  independent curves off, not marked field-confirmed.
- **Victus 16-e0xxx AMD (`88EB`)** (#273, tested by tmnkomrad-eng): an exact entry. It used to borrow the `88EC`
  entry by name and passed Guided Fan Verification 6/6 on that profile, so the new entry copies its settings. Not
  marked field-confirmed.

## Website

- **The 5.0.2 site update never went live.** The Pages workflow uploaded the site and then, in the same job, could not
  find the upload, so the site stayed on 5.0.1. Upload and deploy now run as separate jobs, and the site shows 5.0.2.

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
- **AMD Cezanne (`8A3C`): sustained power held at 35 W** (#272). The reporter traced it: OmenCore's STAPM write lands,
  and the APU skin-temperature target (`STT LIMIT APU`, 45 °C) pulls the budget down. An STT control is a feature
  request for the next minor version.
- **Victus 15-fb3xxx (`8DD0`): Guided Fan Verification fails CPU @ 60%** (#269). At 60% the CPU fan's level readback
  stayed flat short of the target (the previous step's 16, then 30, then 23 of 33) for the whole 12-second check, so a
  longer wait alone may not pass it. Fan control itself works on both fans.
- **A general fail-safe for a stuck CPU temperature** (#274 follow-up). Handing fans to the BIOS, or to the watchdog's
  90%, whenever the CPU temperature looks frozen is not safe with the current freeze detector: it fires on most
  boards in field logs (461 times in one `8A4D` session), and it did not fire in the last minutes before the `88D2`
  shutdown. The `88D2` fix above stays model-scoped; a general fail-safe needs a more reliable signal first.
