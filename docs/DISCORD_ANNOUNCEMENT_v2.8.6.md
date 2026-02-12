# v2.8.6 — 9 Bug Fixes + Self-Sustaining Monitoring

Thanks OsamaBiden, Saixknox, SimplyCarrying for the reports! 🙏

## Bug Fixes
- **CPU Temp 0°C** — Arrow Lake fallback sensor sweep
- **Fn+F2/F3 steals hotkeys** — Auto-enforces Ctrl+Shift on bare F-keys
- **RPM glitch** — Removed faulty MaxFanLevel auto-detection
- **Profile UI desync** — OMEN tab syncs on profile switch
- **Game library buttons** — Now enable after selecting a game
- **GPU temp frozen** — Idle-aware threshold + NVML 60s auto-recovery
- **CPU power 0W** — Intel RAPL MSR via PawnIO for real-time wattage
- **GPU power 0W** — Fallback TDP table for RTX 3060–4090 laptops
- **Afterburner coexistence** — Fixed MAHM v2 data offset (260→1048)

## Enhancements
- 🏗️ **Self-sustaining monitoring** — No LHM/WinRing0/NVML needed. WMI BIOS + NVAPI natively
- 🧹 **Memory Optimizer tab** — RAM monitoring + Smart/Deep clean
- **Afterburner coexistence** — Reads GPU data from shared memory (zero contention)
- **OMEN Desktop support** — Experimental instead of blocked
- **RPM debounce** — 3s filter for profile transitions
- **V1/V2 fan restore** — Correct BIOS restore for both systems

**Download:** <https://github.com/theantipopau/omencore/releases/tag/v2.8.6>

```
931704AE  OmenCoreSetup-2.8.6.exe
2FEE1528  OmenCore-2.8.6-win-x64.zip
2ED425B6  OmenCore-2.8.6-linux-x64.zip
```
Full hashes + changelog: <https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.8.6.md>
