# OmenCore v2.9.0 — Stability & Telemetry Recovery Patch

Hey r/OmenCore!

v2.9.0 is here — a major stability update with headless mode, configurable worker timeout, and 9 community-reported bug fixes. Thanks to everyone on Discord for the detailed reports!

**Download:** https://github.com/theantipopau/omencore/releases/tag/v2.9.0

---

## ✨ New Features

### Headless Mode
- Run OmenCore without a GUI using `--headless` flag
- Perfect for servers, remote management, or background operation
- All monitoring and control functions work normally

### Hardware Worker Orphan Timeout
- Configurable timeout (1-60 minutes) for worker process persistence
- UI controls in Settings > Monitoring
- Prevents orphaned processes when main app crashes

### Self-Sustaining Monitoring
- Complete independence from LibreHardwareMonitor, WinRing0, and NVML
- Uses WMI BIOS + NVAPI natively for all sensors
- No more frozen temperatures or antivirus false positives

### Memory Optimizer Tab
- Real-time RAM usage monitoring
- Smart Clean: Trim processes + purge standby memory
- Deep Clean: Full system memory optimization
- Auto-clean with configurable thresholds

### Enhanced Keyboard Lighting
- V2 lighting engine with 4-zone RGB control
- Breathing, color cycle, and wave effects
- Improved backend detection and fallback

---

## 🐛 Bug Fixes (9 Community Reports)

### App Stability
- **App freeze after tray actions** — Implemented last-write-wins queue system for fan mode/profile changes
- **Fn+F2/F3 brightness keys steal shortcuts** — Added stronger exclusion logic for brightness keys
- **Temperature freeze recovery** — Idle-aware thresholds prevent false positives

### Power & Sensors
- **CPU/GPU power 0W dropouts** — Fallback TDP tables and Intel RAPL MSR reading
- **GPU temp frozen at idle** — NVML auto-recovery and extended idle thresholds
- **Afterburner coexistence broken** — Fixed MAHM shared memory offset bug

### UI & UX
- **Quick profile UI desync** — OMEN tab properly updates on profile switches
- **Game library buttons disabled** — Fixed state after game selection
- **EC safety hardening** — Reduced writes from 15-33/sec to ~0.5/sec to prevent battery shutdowns

---

## 📦 Downloads

| File | Description |
|------|-------------|
| `OmenCore-2.9.0-win-x64.zip` | Windows portable (recommended) |
| `OmenCore-2.9.0-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

### SHA256 Checksums
```
37265CEF301576D6492E153BE413B1B686DF9162A01A07F8D53F15F0EB0E1B48  OmenCore-2.9.0-win-x64.zip
EB59465DEC2F28EE2E11D686D0FDCECCA6BF89A9FF7D3125B6EE6E5E531588C7  OmenCore-2.9.0-linux-x64.zip
```

---

## ℹ️ Notes

- **Migration**: No breaking changes — all settings carry over
- **Windows Defender**: May flag as false positive. See [Antivirus FAQ](https://github.com/theantipopau/omencore/blob/main/docs/ANTIVIRUS_FAQ.md)
- **Linux**: `./omencore-cli` for terminal, `./omencore-gui` for Avalonia GUI
- **Self-contained**: No .NET runtime installation required

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.9.0.md
GitHub: https://github.com/theantipopau/omencore
Discord: https://discord.gg/9WhJdabGk8