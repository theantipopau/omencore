# v4.1.7 Reddit Announcement — r/OmenCore

Title:

```text
[Release] OmenCore v4.1.7 — EC power-limit safety default, Max-fan latch fix, Linux fan-control fix, 16 community PRs
```

Body (Reddit markdown):

```markdown
OmenCore v4.1.7 is out. It's the largest patch to date — it grew from an originally-scoped v4.1.6 into this after a second and third wave of field reports and community contributions kept landing before anything got tagged, so v4.1.6 itself was never released.

**Release:** https://github.com/theantipopau/omencore/releases/tag/v4.1.7

---

## Safety

* **Unconfirmed EC power-limit register writes (CPU PL1/PL2, GPU TGP) were being attempted by default** on any board that didn't explicitly opt out. The code's own header comment already documented these addresses as unconfirmed placeholders with an explicit hardware-damage warning — a new `SupportsEcPowerLimits` capability flag now defaults this off everywhere until a model's real addresses are field-confirmed. No board has ever had them confirmed, so this is a pure safety tightening, not a regression for anyone.
* GPU Power Boost's EC fallback claimed **"Extended" applied** when the underlying register genuinely can't represent anything beyond "Maximum" — caught firing on real hardware in a reporter's own session logs. It now refuses instead of falsely claiming success.

## Fan

* Switching Performance Mode away from an active Max Fan hold cleared OmenCore's internal tracking without releasing the real BIOS `SetFanMax` latch — fans stayed at maximum speed until the app was fully closed. Reported on board `8DCD` (HP Victus 15 fa2082wm); now fixed at the source (missing `SetFanMax(false)` call).
* The guided fan diagnostic displayed RPM readings **one full test-step stale** (60% consistently reading lower than 30%) — root-caused directly from a community member's own session logs to a display path reading a lagging cache instead of an already-fresh value a few lines away. Pure display/scoring fix, no fan-control behavior touched.

## Linux

* **`omencore-gui` never actually controlled fans on boards exposing only a coarse `pwm_enable` toggle** (no writable duty file) — every fan request from the GUI silently did nothing. Reported alongside a real overheat/shutdown incident on board `8BCA` (OMEN 16-xf0xxx). The CLI already had the correct fallback; the GUI didn't, because the two Linux targets carry entirely separate hardware implementations. Ported the fix into the GUI.
* Keyboard RGB now tries three more sysfs backends (`hp-wmi/rgb_zones/zone00`–`zone03`, `hp-wmi/keyboardleds`, `hp_omen::kbd_backlight/zone_colors`) in both Linux targets, plus a fix for keyboard *brightness* on boards using the `hp_omen::` LED class name that both targets previously missed entirely.
* Adopted community PR [#150](https://github.com/theantipopau/omencore/pull/150) (`murilopontes`) — documented EC-offset `0xEC` fan boost for board `84DB` (OMEN 15-dc0xxx), where stock `hp-wmi` fails with EINVAL.

## Community contributions (tempestnano — 16 PRs/branches, all individually file-by-file reviewed and merged)

* Board `8D87` (OMEN Max 16) support: real fan ranges/tachometers, adapter-aware behavior
* AMD Curve Optimizer/SMU transport fix
* Per-key RGB for board `8D87` that also fixed **reactive lighting being completely inert for every user on every board**
* **Per-LED keyboard lighting extended to every supported board**
* An **experimental adapter power override** that lifts GPU/CPU power clamps caused by an under-rated charger — off by default, single-board evidence so far, merged on an explicit informed decision after the review surfaced a real prior crash the fix itself resolves
* A fix for the app's own polling keeping a sleeping dGPU awake
* A real BSOD bug in the background hardware-monitoring process — its watchdog could run forever on PID reuse, polling the GPU on battery until Windows killed the machine

## Also in this release

* Numbers throughout the UI rendered with a comma instead of a period on comma-decimal Windows locales — fixed once at app startup instead of touching 400+ call sites
* A real Spotify install flagged as bloatware, and board `8BCA` (shared by an Intel and an AMD SKU) resolving to the wrong CPU vendor's profile — both fixed
* A brand-new AMD Family 26 CPU ("AMD Ryzen AI 7 350") was misidentified as ~2020 Renoir/Lucienne silicon — fixed
* **Added:** RAM Smart Clean in Quick Access, an opt-in "clean memory on launch" per game profile, and a shared/default profile for a whole list of games
* A WMI model-name-pattern fallback could hand an Intel board an AMD-only capability profile just because HP reused the same marketing name across board revisions — fixed
* A full code review of GPU Power Boost, GPU OC/UV, and CPU OC/UV (`docs/TUNING-SUBSYSTEMS-REVIEW.md`) found and fixed several places where the app reported something as applied when it wasn't — reporting/consolidation only, no tuning behavior changed on the wire
* Three separate UI-responsiveness passes for the standing "laggy UI" complaint

---

## Known limits worth reading before you update

* **Max Fan Mode can trigger a repeating background re-assert loop** on boards whose real Max-hold fan level sits well below the level OmenCore expects (confirmed on `8A18`, `8A25`, `8E10`, `8D41`) — it re-sends the Max command roughly every 20 seconds for as long as Max Fan Mode stays engaged. One board's full multi-day log also showed near-constant in-game stutter; the connection is **plausible but not proven**. Workaround: use a custom fan curve or the Performance/Gaming preset instead of literal Max Fan Mode while gaming.
* The Linux fan-control and RGB fixes above are build-verified and code-reviewed only — there's no Linux/OMEN hardware or automated test project for either Linux target in the dev environment. If you're on an affected board, a confirmation report helps a lot.

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v4.1.7.md

## Downloads

Windows:
* `OmenCoreSetup-4.1.7.exe` (recommended)
* `OmenCore-4.1.7-win-x64.zip` (portable)

Linux:
* `OmenCore-4.1.7-linux-x64.zip`

## SHA256

```
756ED7477D8F9800766FEC2DB1EA7B88EAA625BB933EC6ED62EB1A7E32CC4B67  OmenCoreSetup-4.1.7.exe
A6895154E87CDA8891FFE323C567FC3C868CA48A4874C7DB80784C29AC229EFE  OmenCore-4.1.7-win-x64.zip
C2B6E47EE02CDC123EB846CC5E4ED0302C075F1DFF085C2F5B1A1B63B147BFE5  OmenCore-4.1.7-linux-x64.zip
```

If you hit a regression, please open an issue with your laptop model, BIOS version, and OmenCore logs so we can reproduce: https://github.com/theantipopau/omencore/issues

Thanks to everyone who reported issues, tested builds, and shared logs on Discord, Reddit, and GitHub this cycle — especially `tempestnano` and `murilopontes` for the direct contributions.

^(Not affiliated with or endorsed by HP.)
```

---

**Status:** safe to post once `OmenCoreSetup-4.1.7.exe`, `OmenCore-4.1.7-win-x64.zip`, and `OmenCore-4.1.7-linux-x64.zip` are uploaded to the `v4.1.7` GitHub Release — the release/download links above won't resolve until the tag is pushed and the release is published. Hashes are real, verified against the built artifacts in `artifacts/`.
