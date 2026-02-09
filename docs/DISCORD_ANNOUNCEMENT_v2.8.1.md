# 🐛 OmenCore v2.8.1 — Community Bug Fix Update

**Download:** https://github.com/theantipopau/omencore/releases/tag/v2.8.1

---

## 🌀 FAN CONTROL FIXES

- **Auto Mode Stuck** — Fans no longer get stuck at ~1000rpm on Victus models when restoring auto control
- **Quiet = Max Fans** — Fixed on OMEN Transcend 14 and other V0/Legacy BIOS models — Quiet mode now correctly maps to the right BIOS command
- **Phantom RPM (4200-4400rpm)** — Fixed garbage RPM readings on V0/V1 systems — `GetFanRpmDirect` is now V2-only
- **Fan % Wrong** — Hardcoded `/55` replaced with auto-detected `_maxFanLevel` — correct percentage on all models

## ⌨️ KEY FIX

- **Fn+F2/F3 Opens OmenCore** — WMI event handler now fail-closed — brightness keys no longer trigger the OMEN key handler

## 🖥️ OSD FIXES

- **Horizontal Layout** — Actually works now — the layout setting is applied at render time
- **Network Upload/Download Stuck at 0** — Timer now starts when any net metric is enabled, not just latency
- **FPS Shows GPU%** — When RTSS is unavailable, FPS field now shows "N/A" instead of GPU activity percentage

## 🐧 LINUX FIXES

- **Diagnose Truncation** — Output box widened from 61→90 chars with word-wrapping for notes
- **Fan Speeds Wrong/Stuck** — Unbuffered sysfs reads prevent stale cached data; added hwmon RPM-to-percent fallback
- **Keyboard Zones** — Per-key RGB models (16-wf0, Transcend, Max, etc.) now detected correctly via DMI
- **GUI Missing** — Avalonia GUI (`omencore-gui`) now bundled in Linux ZIP

---

## 📦 DOWNLOADS

**Windows:** `OmenCoreSetup-2.8.1.exe` | `OmenCore-2.8.1-win-x64.zip`
**Linux:** `OmenCore-2.8.1-linux-x64.zip`

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.8.1.md

Thanks to everyone who reported bugs during v2.8.0 testing — 12 fixes in this patch! 🙏
