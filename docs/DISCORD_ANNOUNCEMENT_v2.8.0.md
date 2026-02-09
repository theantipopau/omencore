# 🚀 OmenCore v2.8.0 Released!

**Download:** https://github.com/theantipopau/omencore/releases/tag/v2.8.0

## ✨ What's New

🎮 **AMD GPU Overclocking** — Full RDNA/RDNA2/RDNA3 OC via ADL2 (core clock, mem clock, power limit)

🖥️ **OMEN Desktop Support** — Fan control, performance modes, and RPM readback for 25L–45L desktops

🌀 **Fan Curve Import/Export** — Save, load, delete, and share fan curves as JSON files

💡 **Corsair + Logitech Effects** — Breathing, spectrum, wave via direct HID — no iCUE/G HUB needed

📊 **OSD: Battery %, CPU/GPU Clock** — Three new toggleable OSD metrics

🎯 **Display Overdrive** — Panel overdrive toggle for compatible OMEN displays

## 🐛 Key Fixes

- **Thermal debounce** — 5s/15s debounce stops fan yo-yo from brief temp spikes
- **MaxFanLevel auto-detect** — Fixed "100%" only being 55% on percentage-based models
- **HardwareWorker survives restarts** — No more 3-5s temp gaps on app restart
- **Bloatware uninstaller** — 3-tier removal now handles OEM-provisioned packages
- **Undervolt safety** — Intel MSR clamped [-250, 0] mV; AMD CO clamped [-30, +30]
- **OSD FPS** — Real FPS via RTSS instead of GPU load percentage
- **Fan curves preserved** on AC/battery switch
- **Linux OMEN Max** — Blocked unsafe EC writes, added ACPI/hwmon alternatives
- **12 converter crash fixes**, 6 real diagnostic detections, tab UI overhaul

## 📦 Downloads

| File | SHA256 |
|------|--------|
| `OmenCoreSetup-2.8.0.exe` | `ADD02976...B2213173` |
| `OmenCore-2.8.0-win-x64.zip` | `7DC97B96...E70FAAC5` |
| `OmenCore-2.8.0-linux-x64.zip` | `D45942DE...8C6A45E9` |

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.8.0.md

---
*Report issues on GitHub or in #bug-reports*
