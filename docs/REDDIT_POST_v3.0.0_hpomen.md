# OmenCore 3.0.0 — The Complete OMEN Gaming Hub Replacement (Architecture Overhaul)

Hey [r/OMEN](https://reddit.com/r/OMEN) and [r/Victus](https://reddit.com/r/Victus)!

I'm sharing a major update to **OmenCore** — the free, open-source replacement for HP OMEN Gaming Hub. v3.0.0 is a complete architecture overhaul with seven critical bug fixes, guided fan diagnostics, memory optimization, keyboard lighting enhancements, and dozens of UI improvements.

**If you've ever had:**
- GPU telemetry freeze and not recover without restarting the app
- Fan control not working on specific models
- All temperature sensors reading 0°C
- Startup taking 30+ seconds to initialize
- Confusing app behavior that "seems to work fine" but data goes missing

...this release fixes all of that.

---

## What is OmenCore?

**OmenCore is a completely free, open-source replacement for HP OMEN Gaming Hub.** Here's why people use it:

✅ Works without OMEN Gaming Hub installed — uses native WMI BIOS commands
✅ No ads, no telemetry, no Microsoft account sign-in requirements
✅ ~70 MB portable executable (no installation bloat)
✅ Runs on Windows AND Linux (CLI + GUI on both)
✅ Open source (MIT license) — you own your data

**Key differences from OMEN Gaming Hub:**
| Feature | OmenCore | OMEN Hub |
|---------|----------|----------|
| Fan curves | ✅ Independent CPU/GPU | ✅ Single curve |
| Linux support | ✅ Full CLI + GUI | ❌ Windows only |
| RGB lighting | ✅ 4-zone + animations | ✅ 4-zone |
| Memory optimizer | ✅ Smart + Deep clean | ❌ N/A |
| Keyboard diagnostics | ✅ Guided tests | ❌ N/A |
| Open source | ✅ View all code | ❌ Proprietary |
| No HP account | ✅ Completely offline | ❌ Requires login |

---

## Core Features

### 🎮 Fan Control
- Custom fan curves with drag-and-drop editor
- Independent CPU/GPU curves
- Preset profiles: Silent, Balanced, Performance, Max
- Real-time RPM monitoring and verification
- **NEW:** Ghost curve overlay (preview presets before applying)
- **NEW:** Guided fan diagnostics (test at 30/60/100%, verify hardware)

### ⚡ Performance Modes
- Switch Quiet / Balanced / Performance modes
- GPU power level control (Min/Med/Max)
- Undervolt support via PawnIO (Intel/AMD)
- TCC offset for thermal throttling control
- **NEW:** Profile scheduler (time-of-day automation)

### 🌈 RGB Keyboard Lighting
- Full 4-zone color customization
- **NEW:** Native brightness control (direct WMI commands)
- **NEW:** LED animation effects (Breathing, ColorCycle, Wave)
- **NEW:** Per-zone intensity control
- Syncs custom colors across restarts

### 📋 System Optimization
- Bloatware manager (identify + remove junk)
- Memory optimizer (Smart Clean + Deep Clean + auto-clean)
- Windows service profiler (disable unnecessary services safely)
- Real-time RAM/storage monitoring

### 📊 Monitoring & Diagnostics
- Real-time CPU/GPU temps, usage, power consumption
- Temperature history charts with 1m/5m/15m/30m time ranges
- On-screen display overlay (OSD)
- **NEW:** Guided fan diagnostics with PASS/FAIL results
- **NEW:** One-click model report export for support threads

### 🔋 Battery Management
- Custom charge threshold (60-100%)
- Real-time battery health monitoring
- Battery-aware fan profiles
- Power mode automation

### 💰 Other Features
- Works entirely offline (no cloud, no telemetry)
- Headless mode for HTPC/server operation
- Settings search (instantly find any setting)
- Onboarding wizard for first-time users
- Works on **any OMEN/Victus laptop** (auto-detects features)

---

## What Changed in v3.0.0?

### 🛠️ Architecture Overhaul
**Problem:** OmenCore relied on complex driver detection (LibreHardwareMonitor, WinRing0, NVML) causing false negatives and Defender complaints.

**Solution:** Complete rebuild around **native WMI BIOS + NVAPI**. No external drivers needed for monitoring. Falls back gracefully: if one sensor source fails, others continue independently.

**Result:** Faster startup (~16s from ~39s), no more Defender false positives, rock-solid telemetry recovery.

### 🔧 7 Critical Regressions Fixed

1. **GPU telemetry locked up permanently** — Would only recover on app restart. Now auto-recovers in 60 seconds.
2. **OMEN 16-wf1xxx (8BAB) fan control broken** — Added proper model database entry. Fans now respond.
3. **Fan auto mode shows 0 RPM after profile switch** — Ghost RPM debounce lingering. Now clears immediately.
4. **Linux performance mode silent failure** — hp-wmi boards couldn't apply perf mode. Now uses proper priority routing.
5. **All sensors read 0°C on some systems** — WMI BIOS check blocking other sources. Now sources work independently.
6. **Monitor loop hangs permanently** — 5 consecutive errors = dead telemetry forever. Now restarts gracefully.
7. **Secure Boot confusion** — Green "PawnIO OK" + yellow "Secure Boot enabled" contradicting eachother. Now correctly combined.

**Plus 3 stability improvements:** NVAPI auto-recovery, better startup, dashboard real metrics from hardware.

### ✨ New Features
- **Guided Fan Diagnostics** — Test fans at 30%, 60%, 100% with live PASS/FAIL results
- **Memory Optimizer Tab** — Real-time monitoring + Smart/Deep clean + periodic auto-clean
- **Keyboard Brightness + Animations** — Native WMI control + Breathing/ColorCycle/Wave effects
- **V2 Keyboard Engine** — PawnIO-native backend for verified models (8A14, 8A15, 8BAD auto-enabled)
- **Headless Mode** — `--headless` flag for HTPC/server (fans + control without GUI)
- **Profile Scheduler** — Time-of-day rules (e.g., Performance at 9am, Silent at 5pm)
- **Diagnostics Export** — One-click ZIP with logs, telemetry, model info for Github issues
- **Temperature Chart Time Ranges** — 1m / 5m / 15m / 30m selector above charts

### 🎨 UI Polish
- **Settings search bar** — Instantly find any setting across all tabs
- **System Optimizer redesign** — Emoji icons replaced with proper Path icons; hardcoded colors → theme brushes
- **Bloatware Manager improvements** — Risk level filter (All/Low/Med/High), bulk remove progress bar, fixed status badges, BETA removed
- **Keyboard zone schematic** — Visual diagram with proportional key counts (instead of four equal boxes)
- **Onboarding wizard** — 3-step welcome for first-time users
- **Zero-temperature "—°C" display** — No more false 0°C when sensors are unavailable
- **Richer tray tooltip** — Separate CPU/GPU fan RPM + battery status line

### 🐧 Linux Improvements
- Systemd service bundle fix (`DOTNET_BUNDLE_EXTRACT_BASE_DIR` now auto-configured)
- Thermal throttle watchdog (opt-in; re-applies perf mode after cooldown)
- Per-fan RPM safety guardrails

---

## Downloads & Install

**Windows Installer (Recommended):**
https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCoreSetup-3.0.0.exe

**Windows Portable (No Installation):**
https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCore-3.0.0-win-x64.zip

**Linux Portable:**
https://github.com/theantipopau/omencore/releases/download/v3.0.0/OmenCore-3.0.0-linux-x64.zip

**Checksums:**
```
8D655695  OmenCoreSetup-3.0.0.exe
DC8C9568  OmenCore-3.0.0-win-x64.zip
60533522  OmenCore-3.0.0-linux-x64.zip
```

**Full checksums:** https://github.com/theantipopau/omencore/releases/tag/v3.0.0

---

## Links

🌐 **Website:** [omencore.info](https://omencore.info)  
⭐ **GitHub:** [github.com/theantipopau/omencore](https://github.com/theantipopau/omencore)  
💬 **Discord:** [discord.gg/9WhJdabGk8](https://discord.gg/9WhJdabGk8)  
📖 **Full Changelog:** [CHANGELOG_v3.0.0.md](https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.0.md)

---

## FAQ

**Q: Does this work with my specific model?**
A: OmenCore works on every OMEN and Victus laptop. It auto-detects your model and shows which features are available. See [omencore.info](https://omencore.info) for the full supported models list.

**Q: Will Windows Defender flag this as malware?**
A: No. OmenCore is 100% open source (view all code) and signed. Older Defender versions may have false positives due to previous versions using WinRing0—this is completely removed in v3.0.0. See [Antivirus FAQ](https://github.com/theantipopau/omencore/blob/main/docs/ANTIVIRUS_FAQ.md).

**Q: Can I run this alongside OMEN Gaming Hub?**
A: Yes, they don't interfere. But once you try OmenCore's fan curves, diagnostics, and headless mode, you'll probably uninstall OGH anyway.

**Q: Is my data safe?**
A: Completely. OmenCore runs 100% offline. No telemetry, no cloud sync, no Microsoft account. All config is stored locally in JSON.

**Q: Can I use this on Linux?**
A: Yes! Full CLI and GUI (Avalonia) on Linux. CLI same as Windows. Perfect for headless servers.

**Q: This looks abandoned. Is it still maintained?**
A: No! Active development with regular updates. Check the [Discord](https://discord.gg/9WhJdabGk8) for latest news and crash reports, and [GitHub](https://github.com/theantipopau/omencore) for code.

---

**Made with ❤️ for the HP OMEN community. Thanks to everyone on Discord and GitHub for the detailed bug reports!**

---

*OmenCore is not affiliated with HP Inc. OMEN and Victus are trademarks of HP Inc. This project is provided as-is under the MIT License.*
