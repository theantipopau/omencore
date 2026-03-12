# [Release] OmenCore v3.1.0 - Telemetry Integrity, Sleep Fan Fixes, and Tray/UI Polish

Release link: https://github.com/theantipopau/omencore/releases/tag/v3.1.0

OmenCore v3.1.0 is now ready as a single consolidated release (all pre-release fixes rolled into one patch train, no split hotfix releases).

## Highlights

- Fixed telemetry correctness issues across CPU/GPU/fan/power read paths.
- Added explicit sensor-state modeling (inactive, unavailable, stale, invalid) for clearer UI behavior.
- Removed fabricated fan RPM fallback values when readback is unavailable.
- Improved idle overhead by reducing hot-path logging and cutting unnecessary UI churn.

## Community-Reported Fixes Included

- GitHub #77: fan speed spiking/maxing during sleep or display-off flows.
  - Suspend/resume handling is now always active.
  - Fan writes are paused during suspend.
  - BIOS auto fan control is restored while sleeping.

- GitHub #78: wrong CPU temperature on OMEN MAX 16-ah0000 (Intel).
  - Added model-scoped CPU temp source override.
  - Affected models now prioritize worker-backed CPU sensor reads.
  - Diagnostics now expose whether this override is active.

## Tray and Diagnostics Polish

- Fixed tray menu visual artifacts (left white gutter and hover/icon bleed).
- Added explicit diagnostics status line for model-specific CPU temp override state.

## Downloads

- Windows installer: OmenCoreSetup-3.1.0.exe (101.09 MB)
- Windows portable zip: OmenCore-3.1.0-win-x64.zip (104.32 MB)
- Linux package zip: OmenCore-3.1.0-linux-x64.zip (43.55 MB)

## SHA256

D92548E4E3698A2B71D11A02ED64D918746C3C3CB06EC2035E8602D57C50AD8C  OmenCoreSetup-3.1.0.exe
1EA65E7BA857285A01A896FC2A7BF8418D1B8D9723DCB9EE4A350E6BA87A06F6  OmenCore-3.1.0-win-x64.zip
276686F92EB289B3196BDCD02CFC93E95F676D269515740060FB7B5A585D9D0F  OmenCore-3.1.0-linux-x64.zip

If you hit any regressions after updating, please open an issue with your model, CPU/GPU, and a diagnostics bundle from the Diagnostics tab.