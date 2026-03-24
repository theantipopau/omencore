<div align="center">

<img src="docs/screenshots/githublogo.png" alt="OmenCore Logo" width="520" />

# OmenCore

## A Modern, Lightweight Control Center for HP OMEN & Victus Gaming Laptops

</div>

---

**OmenCore** is a **complete, independent replacement** for HP OMEN Gaming Hub. No dependencies. No OGH services. No bloatware, telemetry, or ads. Built on .NET 8, it delivers professional-grade hardware control using native WMI BIOS commands that work directly with your laptop's firmware.

### ✨ Why OmenCore?

| Feature | Status |
|---------|--------|
| **100% OGH-Independent** | ✅ Works without OMEN Gaming Hub installed |
| **Zero Bloatware** | ✅ Self-contained artifacts, no runtime installs |
| **No Telemetry** | ✅ Your data stays on your machine |
| **Ad-Free** | ✅ Clean, focused interface |
| **Offline Operation** | ✅ No sign-in required, fully local control |
| **Cross-Platform** | ✅ Windows WPF + Linux CLI & Avalonia GUI |

---

### 🎯 Quick Links

[![Version](https://img.shields.io/badge/version-3.2.1-red.svg?style=for-the-badge)](https://github.com/theantipopau/omencore/releases/tag/v3.2.1)
[![License](https://img.shields.io/badge/license-MIT-green.svg?style=for-the-badge)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg?style=for-the-badge)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Discord](https://img.shields.io/badge/Discord-Join%20Server-5865F2.svg?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/9WhJdabGk8)
[![Donate](https://img.shields.io/badge/Donate-PayPal-00457C.svg?style=for-the-badge&logo=paypal&logoColor=white)](https://www.paypal.com/donate/?business=XH8CKYF8T7EBU&no_recurring=0&item_name=Thank+you+for+your+generous+donation%2C+this+will+allow+me+to+continue+developing+my+programs.&currency_code=AUD)

---

### 📸 Interface Preview

![OmenCore Main Window](docs/screenshots/main-window.png)

## 🚀 **Quick Start**

### Windows

1. Download `OmenCoreSetup-3.2.1.exe` from [Releases](https://github.com/theantipopau/omencore/releases/tag/v3.2.1)
2. Run as Administrator
3. Launch OmenCore from the Start Menu

→ **[Full Installation Guide](INSTALL.md#-windows-installation)**

### Linux (CachyOS • Arch • Ubuntu • Fedora)

```bash
# Download & Extract
wget https://github.com/theantipopau/omencore/releases/download/v3.2.1/OmenCore-3.2.1-linux-x64.zip
mkdir -p OmenCore-linux-x64
unzip OmenCore-3.2.1-linux-x64.zip -d OmenCore-linux-x64
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

# CLI: Check status
sudo ./omencore-cli status

# GUI: Launch Avalonia
sudo ./omencore-gui
```

→ **[Complete Linux Guide](docs/LINUX_INSTALL_GUIDE.md)** | **[Quick Reference](INSTALL.md#-linux-installation)**

## 🔥 **What's New in v3.2.1**

v3.2.1 is a major hotfix rollup focused on reliability, telemetry accuracy, and UI polish.

### Reliability and telemetry

- Reduced false thermal alerts with stronger sensor sanity and persistence checks
- Improved sleep/resume monitoring recovery behavior and fan-state reapplication
- Hardened CPU temperature source fallback logic to prevent 32C/70C source oscillation on affected sessions
- Reduced fallback log noise for portable mode and CIM to legacy WMI transitions

### Fan and power behavior

- Improved fan safety around mode transitions and low-duty edge cases
- Better handling of transient AC/battery state glitches in automation paths
- Improved fan diagnostic scoring for Victus-class hardware characteristics

### UX polish

- Hotkey OSD visual upgrades with configurable duration, size, accent themes, and compact mode
- Premium surface styling pass across key dashboard and fan-control areas

→ **[Full Changelog](docs/CHANGELOG_v3.2.1.md)**

---

## 📦 **Downloads & Artifacts**

**Version:** v3.2.1 | **Build Date:** 2026-03-24 | **Status:** Released

| Download | Platform | Details |
|----------|----------|---------|
| **OmenCoreSetup-3.2.1.exe** | Windows | Installer (Recommended) — Includes .NET 8 runtime |
| **OmenCore-3.2.1-win-x64.zip** | Windows | Portable — Extract and run, no installation |
| **OmenCore-3.2.1-linux-x64.zip** | Linux | CLI + Avalonia GUI, self-contained runtime |

### SHA256

```text
3EB2BCC82A001FA408AF79031C74F8813F1E6F56429F323E5BDC4F97525FD907  OmenCoreSetup-3.2.1.exe
ED0A3A95B99B487D6905690EB12C79D8623CAE90743E2811EFF4A81DA632E695  OmenCore-3.2.1-win-x64.zip
344EA6C5BD4394B574939F2693B9842E6720475A6805357A058E68B4286FC1BA  OmenCore-3.2.1-linux-x64.zip
```

> Security: release hashes are also published on the [v3.2.1 release page](https://github.com/theantipopau/omencore/releases/tag/v3.2.1).

---

## 🔧 **Features**

### Thermal & Fan Management

- Custom fan curves with temperature breakpoints — CPU and GPU fans controlled independently
- WMI BIOS control — no driver required, works on AMD and Intel models
- EC-backed presets (Max, Auto, Manual) for instant fan switching
- Real-time monitoring with live CPU/GPU temperature history charts
- Per-fan telemetry — RPM and duty cycle for each cooling zone
- System tray badge — live CPU temperature on the notification icon
- CPU Temperature Limit — TCC offset control (Intel only)
- Fan preset save/load — name, export, import, and share `.omencore` profiles
- 0% duty remapping — curve interpolation can never stall fans below the configured minimum (v3.2.0)

### Performance Control

- CPU undervolting via Intel MSR with independent core/cache offset sliders (typical safe range: -80 to -125 mV)
- Performance modes (Balanced, Performance, Turbo) — CPU/GPU wattage envelope management
- GPU Power Boost — +15W Dynamic Boost (PPAB)
- GPU mux switching — Hybrid, Discrete (dGPU), and Integrated (iGPU)
- Per-game profiles — auto-switch on game process detection
- External tool detection — defers MSR control when ThrottleStop/Intel XTU is active

### RGB Lighting

- Keyboard lighting profiles — Static, Breathing, Wave, Reactive (multi-zone)
- 4-zone OMEN keyboards with per-zone color and intensity
- Per-key RGB on OMEN Max 16 (individual key addressing)
- Peripheral sync — apply themes to Corsair/Logitech/Razer devices
- Linux sysfs-based RGB capability detection (v3.2.0)

### Hardware Monitoring

- Real-time telemetry — CPU/GPU temp, load, clocks, RAM usage, SSD temp
- Telemetry state model: `Valid`, `Inactive`, `Unavailable`, `Stale`, `Degraded`, `Invalid`
- Dashboard banners for Stale and Degraded states with contextual messaging (v3.2.0)
- Rolling 60-sample history charts with 0.5° / 0.5% change threshold
- Low overhead mode — disables charts; reduces idle CPU from ~2% to <0.5%

### System Optimization

- HP OMEN Gaming Hub removal — guided cleanup with dry-run mode
- Gaming Mode — one-click service/animation toggle
- Battery care — adjustable charge limit (60–100%)
- OSD in-game overlay — click-through, configurable metrics
- Memory optimizer — smart/deep RAM clean using Windows native API
- Bloatware scanner — AppX detection, startup item manager, scheduled task cleaner

### Auto-Update

- Polls GitHub Releases every 6 hours
- SHA256 verification required (updates rejected without hash for security)
- One-click download with progress indicator and integrity validation
- Manual fallback if SHA256 is absent from release notes

---

## 🎯 HP Gaming Hub Feature Parity

OmenCore is designed to **completely replace** OMEN Gaming Hub.

| HP Gaming Hub Feature | OmenCore | Notes |
|----------------------|---------|-------|
| Fan Control | ✅ Full | Custom curves + WMI BIOS presets |
| Performance Modes | ✅ Full | CPU/GPU power envelope via WMI |
| CPU Undervolting | ✅ Full | Intel MSR with safety clamping |
| GPU Power Boost | ✅ Full | +15W Dynamic Boost (PPAB) |
| Keyboard RGB | ✅ Full | Per-zone + per-key on supported models |
| Hardware Monitoring | ✅ Full | LibreHardwareMonitor integration |
| Gaming Mode | ✅ Full | Service/animation optimization |
| Battery Care | ✅ Full | Adjustable 60–100% charge limit |
| Peripheral Control | ⚠️ Beta | Corsair/Logitech/Razer hardware detection ready |
| Hub Cleanup | ✅ Exclusive | Safe OGH removal tool |
| Per-Game Profiles | ✅ Full | Auto-switch on process detection |
| In-Game Overlay | ✅ Full | Click-through OSD |
| Network Booster | ❌ Out of scope | Use router/Windows QoS |
| Game Library | ❌ Out of scope | Use Steam/Epic/Xbox app |
| Omen Oasis | ❌ Out of scope | Cloud gaming out of scope |

**OmenCore covers 100% of essential Gaming Hub features** — with better performance, no telemetry, no ads, and full offline operation.

---

## 📋 Requirements

### System

- **OS:** Windows 10 (build 19041+) or Windows 11
- **Runtime:** Self-contained — .NET 8 embedded, no separate installation needed
- **Privileges:** Administrator for WMI BIOS/EC/MSR operations
- **Disk:** ~120 MB for app + ~50 MB logs/config
- **OGH:** NOT required — OmenCore works without OMEN Gaming Hub

### Hardware

- **CPU:** Intel 6th-gen+ for undervolting/TCC offset; AMD Ryzen supported for monitoring and fan control
- **Laptop:** HP OMEN 15/16/17 series and HP Victus (2019–2025 models)
  - ✅ Tested: OMEN 15-dh, 16-b, 16-k, 17-ck (2023/2024), Victus 15/16
  - ✅ OMEN Max 16 (2025): per-key RGB, RTX 50-series, full support
  - ✅ OMEN Transcend 14/16: WMI BIOS support
  - ✅ 2023+ models: full WMI BIOS support, no OGH needed
- **Desktop:** HP OMEN 25L/30L/40L/45L (limited support; monitoring, profiles, and OGH cleanup functional)

### Fan Control Driver Priority

1. **WMI BIOS** (default) — no driver, works on all OMEN laptops
2. **EC via PawnIO** — Secure Boot compatible
3. **EC via WinRing0** — legacy; may need Secure Boot disabled
4. **OGH Proxy** — last resort fallback

### Optional Drivers

- **PawnIO** — recommended for advanced EC/MSR access (Secure Boot compatible)
- **WinRing0 v1.2** — legacy kernel driver

> **Antivirus note:** WinRing0 is flagged as `HackTool:Win64/WinRing0` by Windows Defender — this is a known false positive for hardware drivers. See [ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) and [DEFENDER_FALSE_POSITIVE.md](docs/DEFENDER_FALSE_POSITIVE.md) for exclusion instructions.

**Compatibility:**
- HP Spectre: fan control and monitoring work; CPU/GPU power limits unavailable (different EC layout)
- HP Victus: fan control, monitoring, and keyboard backlight work; GPU TGP/PPAB and CPU undervolting unavailable (BIOS does not expose these on Victus)
- Non-OMEN HP laptops: monitoring only
- Other brands: not supported
- Virtual machines: monitoring-only mode

---

## 🏗️ Architecture

**Stack:** .NET 8.0 / WPF (Windows) / Avalonia (Linux) / LibreHardwareMonitor / EC Direct / Intel MSR

```
OmenCore/
├── src/OmenCoreApp/              # Windows WPF app (ViewModels, Views, Services, Controls)
├── src/OmenCore.HardwareWorker/  # Out-of-process hardware worker — crash isolation
├── src/OmenCore.Avalonia/        # Avalonia cross-platform UI (ViewModels, Services)
├── src/OmenCore.Desktop/         # Avalonia desktop shell + settings persistence
├── src/OmenCore.Linux/           # Linux hardware: hp-wmi, ec_sys, sysfs RGB probing
├── installer/                    # Inno Setup script
├── config/                       # default_config.json
├── docs/                         # Changelogs, audit reports, guides
└── VERSION.txt                   # Current: 3.2.1
```

**Principles:** Safety-first EC write allowlist · Async by default · Telemetry change-detection (0.5°/0.5%) · Graceful per-service degradation · Out-of-process crash isolation

---

## 🛠️ Development

### Requirements

- Visual Studio 2022 (Community+), workload: .NET Desktop Development
- .NET 8 SDK — [download](https://dotnet.microsoft.com/download/dotnet/8.0)
- Inno Setup (installer only) — [download](https://jrsoftware.org/isdl.php)

### Build

```powershell
git clone https://github.com/theantipopau/omencore.git
cd omencore
dotnet restore OmenCore.sln
dotnet build OmenCore.sln --configuration Release

# Run (Administrator required)
cd src\OmenCoreApp\bin\Release\net8.0-windows10.0.19041.0
.\OmenCore.exe
```

### Build Installer

```powershell
pwsh ./build-installer.ps1 -Configuration Release -Runtime win-x64 -SingleFile
# Outputs: artifacts/OmenCoreSetup-3.2.1.exe and artifacts/OmenCore-3.2.1-win-x64.zip
```

### Tests

```powershell
dotnet test OmenCore.sln
dotnet test OmenCore.sln --collect:"XPlat Code Coverage"
```

### Release Process

1. Update `VERSION.txt`
2. Add changelog under `docs/CHANGELOG_vX.Y.Z.md`
3. Tag and push: `git tag vX.Y.Z && git push origin main --tags`
4. Include SHA256 hash in GitHub Release notes — required for the in-app auto-updater

---

## 🐛 Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Fan control has no effect | WMI not supported on this model | Try PawnIO/ec_sys mode; check logs |
| Access denied errors | Not running as Administrator | Right-click → Run as administrator |
| WinRing0 not detected | Driver blocked by Secure Boot | Switch to PawnIO (Secure Boot compatible) |
| Undervolting not working | MSR locked in BIOS | Check BIOS overclocking settings; verify with Intel XTU |
| Auto-update fails | SHA256 missing from release notes | Download manually from the Releases page |
| High CPU at idle | Charts polling too aggressively | Enable Low Overhead Mode in Dashboard settings |
| Linux: permission denied | Hardware access needs root | Run with `sudo` |
| Linux: ec_sys not found | Module not in this kernel | Use `hp-wmi` on 2023+ models |

Detailed logs are in `%LOCALAPPDATA%\OmenCore\`. On Linux, use `sudo omencore-cli --report > report.txt` for a diagnostics bundle.

> **AMD undervolting:** Ryzen does not support Intel-style MSR undervolting. Use BIOS Curve Optimizer or Ryzen Master. OmenCore still provides full fan control, monitoring, RGB, and performance modes on AMD systems.

---

## 📖 Version History

| Version | Key Changes |
|---------|------------|
| **v3.2.1** | 23-fix hotfix rollup: telemetry hardening, OSD/premium UI polish, portable log hygiene, CPU temp oscillation guard |
| **v3.2.0** | Dashboard row fix, fan 0% safety, frozen temp watchdog, Avalonia preset save, Linux RGB detection |
| **v3.1.1** | CPU temp regression (17-ck1xxx), fan 0-RPM guard, worker crash on GPU driver install, PE header validation |
| **v3.1.0** | Telemetry state model, sleep/suspend fan hardening (#77), OMEN MAX 16 CPU temp override (#78) |
| **v3.0.2** | Hotfix: PE header validation, WinRing0 hash check |
| **v3.0.0** | Multi-project architecture, out-of-process HardwareWorker, full Avalonia Linux GUI |
| **v2.9.0** | Intel Core Ultra CPU temp fix, EC write reduction, memory optimizer, Afterburner coexistence |
| **v2.8.0** | AMD GPU OC (ADL2), OMEN desktop support, game library, Linux hwmon PWM control |

Older release notes: [docs/](docs/)

---

## 📚 Documentation

- [INSTALL.md](INSTALL.md) — Full installation guide for Windows and Linux
- [docs/LINUX_INSTALL_GUIDE.md](docs/LINUX_INSTALL_GUIDE.md) — Detailed Linux setup
- [docs/ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) — Antivirus false positive handling
- [docs/DEFENDER_FALSE_POSITIVE.md](docs/DEFENDER_FALSE_POSITIVE.md) — Windows Defender exclusion steps
- [docs/WINRING0_SETUP.md](docs/WINRING0_SETUP.md) — WinRing0 driver setup
- [docs/CHANGELOG_v3.2.1.md](docs/CHANGELOG_v3.2.1.md) — Current release notes

---

## 🤝 Contributing

Contributions welcome! Priority areas:

- [ ] Corsair iCUE / Logitech G HUB SDK implementations (replace stubs)
- [ ] EC register maps for models not yet in the allowlist
- [ ] Testing on OMEN Max 16/17 (2025) with RTX 50-series
- [ ] Testing on OMEN 15-en, 16-n series
- [ ] Localization / translations

---

## ⚠️ Disclaimer

This software is provided "as is" without warranty. Modifying EC registers, undervolting, and mux switching can potentially damage hardware. Always create system restore points before making changes. The developers are not responsible for hardware damage, data loss, or warranty voids. HP does not endorse this project; use at your own risk.

---

## 🔗 Links

- **GitHub:** https://github.com/theantipopau/omencore
- **Releases:** https://github.com/theantipopau/omencore/releases/latest
- **Issues:** https://github.com/theantipopau/omencore/issues
- **Discord:** https://discord.gg/9WhJdabGk8
- **Donate:** https://www.paypal.com/donate/?business=XH8CKYF8T7EBU

---

## 📄 License

MIT License — see [LICENSE](LICENSE) for details.

**Third-party components:**
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — MPL 2.0
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — CPOL
- WinRing0 driver — OpenLibSys license

---

*Made with care for the HP OMEN community.*
