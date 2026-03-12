## OmenCore v3.1.0 is live

Download: https://github.com/theantipopau/omencore/releases/tag/v3.1.0

This is a single consolidated 3.1.0 release that rolls all pre-release fixes into one patch line.

### Key fixes

- Telemetry integrity updates for CPU/GPU/fan/power data quality.
- Explicit sensor-state handling (inactive/unavailable/stale/invalid).
- Removed synthetic fan RPM fallback values.
- Reduced idle overhead from monitor/UI hot paths.

### Community issues addressed

- GitHub #77 (sleep fan spikes): suspend handling is always active now, fan writes pause during suspend, BIOS auto control restored while sleeping.
- GitHub #78 (OMEN MAX 16-ah0000 wrong CPU temp): model-specific CPU source override now prioritizes worker-backed readings.

### UI/UX polish

- Tray menu artifact fix (left white gutter and hover/icon bleed removed).
- Diagnostics now includes a line showing whether model-specific CPU temp override is active.

### Artifacts

- OmenCoreSetup-3.1.0.exe (101.09 MB)
- OmenCore-3.1.0-win-x64.zip (104.32 MB)
- OmenCore-3.1.0-linux-x64.zip (43.55 MB)

### SHA256

- D92548E4E3698A2B71D11A02ED64D918746C3C3CB06EC2035E8602D57C50AD8C  OmenCoreSetup-3.1.0.exe
- 1EA65E7BA857285A01A896FC2A7BF8418D1B8D9723DCB9EE4A350E6BA87A06F6  OmenCore-3.1.0-win-x64.zip
- 276686F92EB289B3196BDCD02CFC93E95F676D269515740060FB7B5A585D9D0F  OmenCore-3.1.0-linux-x64.zip

If anything looks off after updating, please post your model + diagnostics bundle in support.