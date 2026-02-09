# OmenCore v2.8.0 + v2.8.1 — AMD GPU OC, Display Overdrive, OSD Upgrades, Fan Safety & 12 Community Bug Fixes 🚀🐛

Hey r/HPOmen!

Big update dropping today — v2.8.0 adds a bunch of new features, and v2.8.1 immediately follows with 12 bug fixes reported by the community during testing. Grab whichever is latest (v2.8.1 includes everything from v2.8.0).

**Download:** https://github.com/theantipopau/omencore/releases/tag/v2.8.1

---

## ✨ New Features (v2.8.0)

### AMD GPU Overclocking
- Full RDNA/RDNA2/RDNA3 support via ADL2/Overdrive8 API
- Core clock, memory clock, and power limit offsets with hardware-reported range clamping
- Reset to defaults
- New sliders in the Tuning view

### Display Overdrive Toggle
- Panel overdrive control via HP WMI BIOS (CMD 0x35/0x36)
- Auto-detects compatible OMEN displays
- Toggle in Advanced view

### OSD: New Metrics
- **Battery %** — Color-coded charge level with AC/battery icon (⚡/🔋)
- **CPU Clock Speed** — Average across all cores, auto-formats GHz/MHz
- **GPU Clock Speed** — Real-time from monitoring sample
- All toggleable in Settings → OSD

### Game Library Tab
- New lazy-loaded Game Library tab in main window
- Integrates with existing GameLibraryService

### Logitech HID++ 2.0 Full Effects
- Breathing, spectrum, flash, and wave effects with speed control
- HID++ 1.0 fallback on older devices
- All effects gracefully fall back to static color on failure

### Corsair HID Effects
- Breathing, spectrum cycle, and wave effects via direct HID
- New brightness slider scaling all RGB values 0-100%

### Fan Curve Save/Load UX
- Delete custom presets with confirmation
- Import/export fan curves as JSON files
- One-click re-apply saved presets
- Auto-apply on save

### OMEN Desktop Support
- WMI fan control enabled for OMEN 25L, 30L, 35L, 40L, 45L desktops
- Fan curves, RPM readback, and performance modes all supported

### Tab UI Overhaul
- Scrollable tab headers with animated accent underline
- Compact padding and hover effects

### Conflict Detection
- ConflictDetectionService now wired and active at startup
- Background monitoring every 60 seconds for conflicting software (MSI Afterburner, OMEN Gaming Hub, etc.)

### Linux: ACPI Platform Profile + hwmon PWM Fan Control
- ACPI `platform_profile` interface for performance mode on OMEN Max models
- hwmon-based PWM fan speed control via `hp-wmi` driver as a safe alternative to direct EC

---

## 🔧 Safety & Reliability (v2.8.0)

- **Undervolt Safety Clamping** — Intel MSR: [-250, 0] mV; AMD CO: [-30, +30]. Prevents accidental extreme values
- **Thermal Protection Debounce** — 5s activation / 15s release debounce. Threshold raised to 90°C with 10°C hysteresis. No more fan yo-yo during transient spikes
- **MaxFanLevel Auto-Detection** — Models using 0-100% range vs 0-55 krpm now auto-detected at startup
- **GPU OC Store-on-Failure** — UI no longer shows offsets that weren't actually applied to hardware
- **HardwareWorker Survival** — Worker process now survives parent exit and continues collecting sensor data. New OmenCore instances reconnect seamlessly — no more 3-5s temp dropouts on restart
- **Bloatware Uninstaller** — 3-tier removal (current user → AllUsers → provisioned). 8 new HP targets. HP Support Assistant preserved
- **Linux EC Safety** — Blocked EC writes on OMEN Max 2025 (16t-ah, 17t-ah) where legacy registers contain serial data

---

## 🐛 Community Bug Fixes (v2.8.1)

All 12 of these were reported by community members during v2.8.0 testing — thanks to everyone who helped!

### Fan Control
| Bug | Fix |
|-----|-----|
| **Fn+F2/F3 opens OmenCore** | WMI handler now fail-closed — brightness keys no longer trigger OMEN key |
| **Auto mode fans stuck ~1000rpm** | `RestoreAutoControl()` no longer resets on Victus models with MaxFanLevel=100 |
| **Quiet profile = max fans** | ThermalPolicy-aware mapping — V0/Legacy models get correct `LegacyCool` code |
| **Phantom 4200-4400rpm** | `GetFanRpmDirect` V2-gated — no more garbage data on V0/V1 systems |
| **Fan % wrong** | Uses auto-detected `_maxFanLevel` instead of hardcoded `/55` |

### OSD (On-Screen Display)
| Bug | Fix |
|-----|-----|
| **Horizontal layout broken** | Layout orientation now applied from settings at render time |
| **Upload/download stuck at 0** | Network timer starts for any enabled metric, not just latency |
| **FPS shows GPU load %** | Shows "N/A" when RTSS unavailable instead of GPU activity fallback |

### Linux
| Bug | Fix |
|-----|-----|
| **Diagnose output truncated** | Box widened 61→90 chars with word-wrapping |
| **Fan speeds wrong/stuck** | Unbuffered sysfs reads + hwmon RPM-to-percent fallback |
| **Keyboard reports 4-zone for per-key models** | DMI detection for per-key RGB models (16-wf0, Transcend, Max) |
| **GUI missing from Linux ZIP** | Avalonia GUI now built and bundled |

### System
| Bug | Fix |
|-----|-----|
| **EC timeout / crash (dead battery)** | Auto-disables battery polling after 3× 0% on AC; 10s query cooldown; EC-safe AC detection |
| **Auto-update "16-bit application" error** | SHA256 extraction fixed; PE header validation; corrupt downloads now caught before execution |

---

## 🧹 Code Cleanup (v2.8.0)

- Removed 4 dead-code files (~1,525 lines): SettingsRestorationService, WinRing0MsrAccess, HpCmslService, ConfigBackupService

---

## 📦 Downloads

| File | Description |
|------|-------------|
| `OmenCoreSetup-2.8.1.exe` | Windows installer (recommended) |
| `OmenCore-2.8.1-win-x64.zip` | Windows portable |
| `OmenCore-2.8.1-linux-x64.zip` | Linux portable (CLI + GUI) |

SHA256 hashes available in the release notes for verification.

---

## ℹ️ Notes

- **Migration**: No breaking changes from v2.7.x — existing settings and profiles carry over
- **Windows Defender**: May flag the executable as a false positive. PawnIO driver is signed and Secure Boot compatible. See [Antivirus FAQ](https://github.com/theantipopau/omencore/blob/main/docs/ANTIVIRUS_FAQ.md)
- **Linux GUI**: Run `./omencore-gui` for the Avalonia GUI, or `./omencore-cli` for the terminal interface
- **OMEN Max 2025**: Direct EC control is blocked for safety — use ACPI platform_profile or hwmon PWM instead

Full v2.8.0 changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.8.0.md
Full v2.8.1 changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.8.1.md
GitHub: https://github.com/theantipopau/omencore
