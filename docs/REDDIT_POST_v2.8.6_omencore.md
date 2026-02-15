# OmenCore v2.8.6 — 9 Bug Fixes, Self-Sustaining Monitoring, Memory Optimizer & EC Safety Hardening

Hey r/OmenCore!

v2.8.6 is out — this is a big community-driven bug fix patch with 9 fixes and 8 enhancements. Massive thanks to OsamaBiden, Saixknox, and SimplyCarrying from Discord for the reports that made this possible.

**Download:** https://github.com/theantipopau/omencore/releases/tag/v2.8.6

---

## 🐛 Bug Fixes

### Sensor & Power Fixes
- **CPU Temp 0°C** (OMEN MAX 16t) — Arrow Lake / Core Ultra CPUs now use "CPU DTS" fallback sensor when primary returns 0°C
- **CPU Power 0W** — Implemented Intel RAPL MSR power reading via PawnIO. CPU wattage now shows real-time package power instead of stuck at 0W
- **GPU Power 0W** — Added fallback TDP table for laptop GPUs (RTX 3060–4090) when NVAPI power limit queries fail. GPU wattage now displays correctly
- **GPU Temp frozen at idle** — Fixed false positive freeze detection. Idle GPUs (load <10%) now get a 2-minute threshold instead of 30 seconds. NVML auto-recovers after 60s instead of permanent disable

### Fan & UI Fixes
- **Fn+F2/F3 steals hotkeys** — Bare function key OSD hotkeys now auto-enforce Ctrl+Shift modifier
- **RPM glitch / false MaxFanLevel=100** — Removed unreliable fan-level auto-detection that broke when OMEN Hub was running
- **Quick profile UI desync** — OMEN tab now properly updates when switching Performance/Balanced/Quiet profiles
- **Game library buttons** — Launch/Create/Edit buttons now enable after selecting a game

### Afterburner Coexistence
- **Afterburner shared memory broken** — Fixed MAHM v2 data offset bug. Was reading at string field offset 260 instead of data float at offset 1048. Afterburner GPU metrics now flow correctly into OmenCore when both apps are running

---

## ✨ Enhancements

### 🏗️ Self-Sustaining Monitoring (Major)
OmenCore no longer depends on LibreHardwareMonitor, WinRing0, or NVML for core monitoring:
- **CPU/GPU Temp**: HP WMI BIOS (same as OmenMon)
- **Fan RPM**: HP WMI BIOS — real hardware values
- **GPU Load/Clocks/VRAM/Power**: NVIDIA NVAPI — direct driver API
- **CPU Load**: Windows PerformanceCounter
- **CPU Power**: Intel RAPL MSR via PawnIO
- **RAM/Battery/SSD**: WMI queries

No more frozen temps, worker process crashes, or antivirus false positives from WinRing0.

### 🧹 Memory Optimizer Tab (New)
- Real-time RAM monitoring with color-coded usage bar
- **Smart Clean**: Trim working sets + purge standby + combine pages
- **Deep Clean**: Full standby purge + file cache flush + modified page flush
- 5 individual operations with risk indicators
- Auto-clean with configurable threshold (50-95%)
- Uses Windows native `NtSetSystemInformation` — no third-party dependencies

### Other Enhancements
- **MSI Afterburner coexistence** — When Afterburner is running, OmenCore reads GPU data from shared memory instead of polling NVAPI (zero driver contention)
- **Monitoring source indicator** — Dashboard shows which data path is active
- **OMEN Desktop support** — Desktops (25L–45L) get experimental support instead of being hard-blocked
- **RPM debounce** — 3s filter prevents phantom RPM spikes during profile transitions
- **V1/V2-aware fan restore** — Correct BIOS control restoration for both fan system types
- **Model database** — OMEN 16 xd0xxx (2024) and MAX models added

---

## 🛡️ EC Safety Hardening

Critical fixes to prevent the EC (Embedded Controller) from being overwhelmed:
- **EC write reduction**: 15-33 ops/second → ~0.5 ops/second during thermal protection
- **Fan smoothing disabled for EC backend** — single write instead of rapid ramp
- **EC RPM read throttling** — 10s minimum interval when LHM has no fan sensors
- **Keyboard EC write throttling** — 200ms minimum between RGB writes

This fixes the false "Critical Battery Trigger Met" system crashes (Windows Event 524) caused by EC timeouts.

---

## 📦 Downloads

| File | Description |
|------|-------------|
| `OmenCoreSetup-2.8.6.exe` | Windows installer (recommended) |
| `OmenCore-2.8.6-win-x64.zip` | Windows portable |
| `OmenCore-2.8.6-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

### SHA256 Checksums
```
931704AE3B733046CA81C9586A9E41645BCDCEB1C0B1D0F0EF3DE14DBC600EC0  OmenCoreSetup-2.8.6.exe
2FEE152809400A913D3811A913CC0F13409966B99245ABF9E4A6B81CC900B3A5  OmenCore-2.8.6-win-x64.zip
2ED425B6840BE8142BDCFA63ADD8927B9A02B835712B99414B9417688726BC6D  OmenCore-2.8.6-linux-x64.zip
```

---

## ℹ️ Notes

- **Migration**: No breaking changes from v2.8.5 — existing settings carry over
- **Windows Defender**: May flag as false positive. See [Antivirus FAQ](https://github.com/theantipopau/omencore/blob/main/docs/ANTIVIRUS_FAQ.md)
- **Linux GUI**: Run `./omencore-gui` for Avalonia GUI or `./omencore-cli` for terminal
- **.NET embedded**: All builds are self-contained — no .NET runtime install needed

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.8.6.md
GitHub: https://github.com/theantipopau/omencore
Discord: https://discord.gg/9WhJdabGk8
