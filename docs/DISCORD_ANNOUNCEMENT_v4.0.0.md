# v4.0.0 Announcement Drafts

## Discord Post (≤2000 characters)

1762 characters — fits in a single Discord message, hashes included.

```text
# v4.0.0 - Sustainability, Architecture, and Accessibility

Big one. Not a features release - this cycle paid down architecture debt from the 3.x series instead of adding new hardware-control surface. No fan/thermal/EC control behavior changed.

## Architecture
- DI composition root started - 19 of ~40 fields pulled out of the MainViewModel god-object
- Shared polling coordinator replacing part of a 27-timer sprawl (tray, popup, OSD)
- Removed ~80 lines of dead duplicate undervolt UI

## Process
- Community model-database pipeline (schema + validator + PR template) - new hardware support no longer funnels through one person
- New Model Capabilities panel on Diagnostics - shows what your model does/does not support

## Game Profiles
- Window-title disambiguation for same-exe-different-game setups
- WMI event-based process detection replacing plain polling
- Switches to a still-running game's profile instead of restoring defaults when one game closes

## Accessibility
- Screen-reader labeling across Dashboard, Advanced, FanControl, Lighting, and Settings - ~140 previously-silent controls

## Bug Fixes
- Startup restore safety gate could silently fail on some OMEN 16/Victus model strings
- Thermal emergency override did not actually hold for custom fan curves; temperature now configurable
- BiosUpdateService corrected - never wrote firmware at all, added real test coverage

## Also
- New interactive site at omencore.info, replacing the old custom site

Download: https://github.com/theantipopau/omencore/releases/tag/v4.0.0

EEDAD07B  OmenCoreSetup-4.0.0.exe
9E209D36  OmenCore-4.0.0-win-x64.zip
A0F4B6DA  OmenCore-4.0.0-linux-x64.zip

Full changelog + full hashes: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v4.0.0.md
```

## Reddit Post

Title:

```text
OmenCore v4.0.0 released — architecture cleanup, community model database, accessibility pass (free, open-source HP OMEN/Victus control center)
```

Body (Reddit markdown):

```markdown
**OmenCore** is a free, open-source, local-first replacement for OMEN Gaming Hub — fan curves, performance modes, multi-brand RGB, undervolting, and monitoring, with no telemetry by default. v4.0.0 just shipped, and it's not a features release — it's a sustainability cycle that pays down architecture debt instead of adding new hardware-control surface.

**No fan/thermal/EC control behavior changed in this release.**

### Architecture

* Started a real DI composition root — 19 of ~40 manually-wired fields pulled out of the `MainViewModel` god-object
* New shared polling coordinator replacing part of a 27-timer sprawl (tray, popup, OSD)
* Removed ~80 lines of dead duplicate undervolt UI that was never actually reachable

### Process

* **Community model-database pipeline** — schema + validator + PR template, so new hardware support doesn't have to funnel through one person hand-writing every entry
* New **Model Capabilities** panel on the Diagnostics tab — shows what your exact model does/doesn't support before you go looking for a setting that isn't there

### Game Profiles

* Window-title disambiguation for same-exe-different-game setups (multiple games under one launcher/runtime)
* WMI event-based process detection replacing plain polling
* Switches to a still-running game's profile instead of blindly restoring defaults when one game closes

### Accessibility

* Screen-reader labeling added across Dashboard, Advanced, Fan Control, Lighting, and (partially) Settings — roughly 140 previously-silent controls

### Bug fixes

* **Startup restore safety gate** could silently fail to engage on some OMEN 16/Victus model strings (a stale duplicate of an already-fixed check)
* **Thermal emergency override** — the "disable thermal protection" toggle didn't actually hold for custom fan curves like it promised; the override temperature is now configurable too
* Corrected a stale risk record: `BiosUpdateService` was documented as "the firmware-write path" — it never writes firmware at all, only checks for updates

---

**Site:** https://omencore.info (new, replaces the old custom site)

**Download:** https://github.com/theantipopau/omencore/releases/tag/v4.0.0

**Full changelog:** https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v4.0.0.md

^(Not affiliated with or endorsed by HP.)
```

---

**Status:** hashes above are real, verified against the built artifacts (`artifacts/SHA256SUMS-4.0.0.txt`, also in `docs/CHANGELOG_v4.0.0.md`). Both are safe to post once you've uploaded `OmenCoreSetup-4.0.0.exe`, `OmenCore-4.0.0-win-x64.zip`, and `OmenCore-4.0.0-linux-x64.zip` to the `v4.0.0` GitHub Release — the download links above point at that release tag and won't resolve to real files until then.
