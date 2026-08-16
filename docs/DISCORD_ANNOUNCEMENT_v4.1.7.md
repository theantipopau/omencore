# v4.1.7 Announcement Drafts

## Discord Post (≤2000 characters)

1998 characters — fits in a single Discord message, hashes shortened to 8 chars.

```text
# v4.1.7 - EC Power-Limit Safety, Max-Fan Latch Fix, Linux Fan-Control Fix, 16 Community PRs

Biggest patch yet - rolled forward from an unshipped v4.1.6 after two more waves of field reports landed.

## Safety
- Unconfirmed EC power-limit writes (CPU PL1/PL2, GPU TGP) were attempted by default on any board that didn't opt out. New `SupportsEcPowerLimits` flag now defaults this off everywhere until a model's addresses are field-confirmed.
- GPU Power Boost's EC fallback claimed "Extended" applied when the register can't represent anything past "Maximum" - now refuses instead of lying about it.

## Fan
- Switching Performance Mode away from an active Max hold left fans stuck at max until app restart - the BIOS latch is now released (board 8DCD).
- Guided fan diagnostic showed RPM one step stale (60% reading lower than 30%) - root-caused from a reporter's logs.

## Linux
- `omencore-gui` never actually controlled fans on boards with only a coarse pwm_enable toggle - reported alongside a real overheat/shutdown incident (board 8BCA). Fixed.
- 3 more keyboard RGB sysfs backends, plus a brightness fix for `hp_omen::` LED-class boards.

## Community (tempestnano, 16 PRs merged)
- Board 8D87 support, an AMD SMU transport fix, per-key -> per-LED RGB for every board (fixed reactive lighting being silently dead everywhere), an experimental adapter power override (off by default, disclosed), a real BSOD fix in the background monitor.

## Also
- Locale number formatting fixed; board 8BCA AMD/Intel misID + Spotify false-positive fixed; AMD Family 26 CPU misID fixed; RAM Smart Clean added to Quick Access; honesty pass on GPU Boost / GPU / CPU OC/UV reporting.

1288/1288 tests, 0 build warnings.

Download: https://github.com/theantipopau/omencore/releases/tag/v4.1.7

756ED747  OmenCoreSetup-4.1.7.exe
A6895154  OmenCore-4.1.7-win-x64.zip
C2B6E47E  OmenCore-4.1.7-linux-x64.zip

Full changelog + full hashes: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v4.1.7.md
```

---

**Status:** hashes above are real, verified against the built artifacts in `artifacts/` (full hashes below, also in `docs/CHANGELOG_v4.1.7.md`). Safe to post once `OmenCoreSetup-4.1.7.exe`, `OmenCore-4.1.7-win-x64.zip`, and `OmenCore-4.1.7-linux-x64.zip` are uploaded to the `v4.1.7` GitHub Release — the download link above won't resolve until the tag is pushed and the release is published.

Full SHA256:
```
756ED7477D8F9800766FEC2DB1EA7B88EAA625BB933EC6ED62EB1A7E32CC4B67  OmenCoreSetup-4.1.7.exe
A6895154E87CDA8891FFE323C567FC3C868CA48A4874C7DB80784C29AC229EFE  OmenCore-4.1.7-win-x64.zip
C2B6E47EE02CDC123EB846CC5E4ED0302C075F1DFF085C2F5B1A1B63B147BFE5  OmenCore-4.1.7-linux-x64.zip
```
