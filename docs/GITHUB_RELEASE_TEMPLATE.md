# OmenCore v1.0.0.4 — Stability Upgrade + Visual Polish

Production-ready release with safety improvements, visual refinements, and architectural foundations for modularity.

## Download

- **Windows Installer**: [OmenCoreSetup-1.0.0.4.exe](https://github.com/theantipopau/omencore/releases/download/v1.0.0.4/OmenCoreSetup-1.0.0.4.exe)
  - **SHA256**: `3F954B6EB0A4ADC4AF43BE6118BD82EFE40BE4616975CEE662D80F335DBC65D0`
- **Portable ZIP**: [OmenCore-1.0.0.4-win-x64.zip](https://github.com/theantipopau/omencore/releases/download/v1.0.0.4/OmenCore-1.0.0.4-win-x64.zip)
  - **SHA256**: `595C7A1E49632353C8F0B82444BB6778FF731E48E2698727926805C8EA7393EE`

---

## ✨ What's New

### Live System Tray Temperature Badge
- 32px CPU temperature overlay on notification icon (updates every 2s)
- Gradient background with accent ring
- Tooltip shows full CPU/GPU telemetry

### Enhanced Hardware Safety
- **EC write allowlist** prevents dangerous register writes
  - Blocks battery charger, VRM, unknown addresses
  - Allows fan control, RGB, performance registers
  - Detailed exception messages for debugging

### Chart Visual Upgrades
- Gridlines with temperature labels on thermal charts
- Gradient backgrounds with rounded corners
- Refined color palette: #05060A backgrounds, #FF005C accents

### Architectural Improvements
- Sub-ViewModel pattern (FanControl, Dashboard, Lighting, SystemControl)
- Async peripheral services with factory pattern
- Change detection optimization (0.5° threshold)

---

## 🐛 Bug Fixes

- **Logging shutdown flush** - Thread join with 2s timeout ensures tail logs written
- **Cleanup toggle mapping** - "Remove legacy installers" now maps correctly
- **Auto-update safety** - Missing SHA256 hash skips download with warning instead of crashing
- **Garbled UI glyphs** - Replaced mojibake (â¬†, âš ) with ASCII text
- **TrayIconService disposal** - Properly unsubscribes timer to prevent memory leak
- **FanMode compatibility** - Defaults to Auto for old configs
- **Installer version** - Corrected to 1.0.0.4

---

## 🎨 Visual Improvements

- **Typography**: Segoe UI Variable Text with better rendering
- **Color palette**: Darker backgrounds, vibrant accents
- **SurfaceCard style**: 12px rounded corners, drop shadows
- **Modern tabs**: Pill-style with purple accent underline
- **Enhanced ComboBox**: Chevron icon, gradient selection
- **Refined DataGrid**: Alternating rows, horizontal gridlines

---

## 📦 Installation

### Fresh Install
1. Download `OmenCoreSetup-1.0.0.4.exe`
2. Run as Administrator
3. Select "Install WinRing0 driver" (recommended)
4. Launch from Start Menu

### Upgrade from 1.0.0.3
- **No uninstall required** - Installer upgrades in place
- **Config preserved** - Settings carry over automatically
- **No migrations** - 100% backward compatible

---

## 📋 Requirements

- **OS**: Windows 10 (build 19041+) or Windows 11
- **Runtime**: .NET 8 Desktop Runtime (x64)
- **Hardware**: HP OMEN 15/16/17 (2019-2024 models)
- **Driver**: WinRing0 v1.2 (bundled with installer)

---

## 🔒 Security Note

**Windows Defender False Positive**: WinRing0 driver flagged as `HackTool:Win64/WinRing0` - this is a known false positive for kernel hardware drivers. Add exclusion for `C:\Windows\System32\drivers\WinRing0x64.sys` and verify signature.

---

## 📚 Documentation

- [README.md](https://github.com/theantipopau/omencore/blob/main/README.md) - Full documentation
- [CHANGELOG.md](https://github.com/theantipopau/omencore/blob/main/CHANGELOG.md) - Version history
- [Detailed Release Notes](https://github.com/theantipopau/omencore/blob/main/docs/RELEASE_NOTES_1.0.0.4.md) - Technical details

---

## 🔮 Next Release (v1.1.0 Planned)

- Complete MainWindow tab refactor with UserControl integration
- Full Corsair iCUE SDK integration
- Full Logitech G HUB SDK integration
- Per-game profile switching
- Configurable EC address map
- Macro recording

---

## ⚠️ Disclaimer

THIS SOFTWARE IS PROVIDED "AS IS" WITHOUT WARRANTY. Modifying EC registers, undervolting, and mux switching can potentially damage hardware. Always test on non-production hardware first. The developers are not responsible for hardware damage, data loss, or warranty voids.

---

**Report Issues**: [GitHub Issue Tracker](https://github.com/theantipopau/omencore/issues)

**Made with ❤️ for the HP OMEN community**
