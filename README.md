# OmenCore

**A modern, lightweight, and fully independent control center for HP OMEN & Victus gaming laptops.**

OmenCore is a **complete replacement** for HP OMEN Gaming Hub - no OGH services required, no bloatware, no telemetry, no ads. Built with WPF on .NET 8, it provides professional-grade hardware control using native WMI BIOS commands that work directly with your laptop's firmware.

**🎯 Key Differentiators:**
- ✅ **100% OGH-Independent** - Works without OMEN Gaming Hub installed
- ✅ **No Bloatware** - Single 70MB self-contained executable
- ✅ **No Telemetry** - Your data stays on your machine
- ✅ **No Ads** - Clean, focused interface
- ✅ **No Sign-In Required** - Full offline operation

[![Version](https://img.shields.io/badge/version-1.3.0--beta2-blue.svg)](https://github.com/Jeyloh/OmenCore/releases/tag/v1.3.0-beta2)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Website](https://img.shields.io/badge/website-omencore.info-brightgreen.svg)](https://omencore.info)
[![Discord](https://img.shields.io/badge/Discord-Join%20Server-5865F2.svg?logo=discord&logoColor=white)](https://discord.gg/ahcUC2Un)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C.svg?logo=paypal&logoColor=white)](https://www.paypal.com/donate/?business=XH8CKYF8T7EBU&no_recurring=0&item_name=Thank+you+for+your+generous+donation%2C+this+will+allow+me+to+continue+developing+my+programs.&currency_code=AUD)

![OmenCore Screenshot](docs/screenshots/main-window.png)

---

## 🆕 What's New in v1.3.0-beta2

### 🐛 Critical Bug Fixes
- **Fan presets now work** - All presets (Auto, Quiet, Max) function correctly
- **GPU Power Boost persists** - TGP settings survive Windows restart
- **OSD overlay fixed** - Works when starting minimized to tray
- **OMEN key interception fixed** - ShowQuickPopup is now the default action
- **Start minimized reliable** - Consistent tray-only startup
- **Intel XTU false positive fixed** - Proper service detection

### 🎯 Complete OGH Independence
- **WMI BIOS First** - Direct firmware communication without OGH services
- **No OGH Required** - Uninstall OMEN Gaming Hub completely, OmenCore works standalone
- **Model Family Detection** - Automatically identifies OMEN 16/17, Victus, Transcend, Desktop
- **Command Verification** - Detects if fan commands actually work on your model

### 🔥 Fan Control That Actually Works
- **Continuous Monitoring** - OmenMon-style 15-second curve reapplication
- **Fan curves now work** - Settings persist instead of reverting after seconds
- **MAX mode fix** - Robust reset sequence to exit max fan mode
- **Hysteresis support** - Prevents annoying fan oscillation
- **More aggressive curves** - Based on OmenMon profiles to prevent throttling

### ⚡ Performance Improvements  
- **Reduced DPC Latency** - Adaptive polling (1-5s based on temp stability)
- **Lower CPU Usage** - 5x slower polling in low overhead mode
- **Fast Startup** - Async service initialization
- **Temperature smoothing** - EMA-based smooth display values

### ✨ New Features
- **Quick Popup** (middle-click tray) - Instant temp/fan control
- **Battery Care Mode** - Limit charge to 80% for battery longevity
- **In-Game OSD** - Click-through overlay showing temps/FPS
- **Tray Quick Profiles** - Fast mode switching from system tray
- **OMEN Key Interception** - Use OMEN key to show Quick Popup

See [CHANGELOG_v1.3.0-beta2.md](docs/CHANGELOG_v1.3.0-beta2.md) for full details.

---

## 🆕 What's New in v1.2

### 📈 Visual Fan Curve Editor
- **Interactive drag-and-drop editor** - Visual graph with temperature (X) and fan speed % (Y)
- Drag points to adjust, click to add, right-click to remove
- Live current temperature indicator with color-coded gradient
- Save custom curves as named presets

### 🔋 Power Automation (AC/Battery Switching)
- **Automatic profile switching** based on power source
- Configure separate presets for AC and battery
- Instant switching when plugging/unplugging

### 🌡️ Dynamic Tray Icon
- **Temperature display** with color-coded background
- 🟢 Green (<60°C) | 🟡 Yellow (60-75°C) | 🔴 Red (>75°C)
- See thermal state at a glance without opening app

### ⚠️ Throttling Detection
- **Real-time throttling indicator** in dashboard header
- Detects CPU/GPU thermal and power throttling
- Warning badge shows specific throttling reasons

### 🖥️ Display Control
- **Quick refresh rate toggle** from tray menu (165Hz ↔ 60Hz)
- **Turn Off Display** - screen off while system runs (for downloads, music)

### 📌 Quality of Life
- **Stay on Top** - keep window always visible
- **Single instance enforcement** - prevents multiple copies
- **Fan countdown extension** - auto re-applies settings every 90s to prevent BIOS reset
- **External undervolt detection** - warns about XTU/ThrottleStop conflicts

### 🛡️ Extended AMD Support
- **Hawk Point CPUs** - Ryzen 9 8940HX, 8940H, Ryzen 7 8845H, 8840H
- **AMD hybrid GPU detection** - Radeon 610M/680M/780M + NVIDIA systems
- **Generic H-series** - All mobile Ryzen H/HX processors

### 🐛 v1.2.1 Hotfixes
- Fixed fan stuck on Max speed after profile change
- Fixed preset name TextBox not accepting input
- Improved shutdown stability and reduced log spam

See [CHANGELOG_v1.2.0.md](docs/CHANGELOG_v1.2.0.md) and [CHANGELOG_v1.2.1.md](docs/CHANGELOG_v1.2.1.md) for full details.

---

## 🔧 Core Features

### 🌡️ **Thermal & Fan Management**
- **Custom fan curves** with temperature breakpoints (e.g., 40°C→30%, 60°C→55%, 80°C→85%)
- **WMI BIOS control** - No driver required! Works on AMD and Intel laptops
- **EC-backed presets** (Max, Auto, Manual) for instant fan control
- **Real-time monitoring** with live CPU/GPU temperature charts
- **Per-fan telemetry** displays RPM and duty cycle for each cooling zone
- **System tray badge** overlays live CPU temperature on the notification icon
- **CPU Temperature Limit** - Set max CPU temp via TCC offset (Intel only)

### ⚡ **Performance Control**
- **CPU undervolting** via Intel MSR with separate core/cache offset sliders (typical: -100mV to -150mV)
- **Performance modes** (Balanced, Performance, Turbo) manage CPU/GPU wattage envelopes
- **GPU Power Boost** - +15W Dynamic Boost control like Omen Gaming Hub
- **GPU mux switching** between Hybrid, Discrete (dGPU), and Integrated (iGPU) modes
- **External tool detection** - respects ThrottleStop/Intel XTU and defers control when detected

### 💡 **RGB Lighting**
- **Keyboard lighting profiles** with effects: Static, Breathing, Wave, Reactive (multi-zone support)
- **4-zone OMEN keyboards** with per-zone color and intensity control
- **Peripheral sync** - apply laptop themes to Corsair/Logitech devices
- **Profile preview** with live color swatches before applying

### 🖱️ **Peripheral Integration**
- **Corsair iCUE devices** - lighting presets, DPI stages, macro profiles
  - Hardware detection ready, SDK stub implementation (awaiting iCUE SDK integration)
- **Logitech G HUB devices** - static color control, DPI readout, battery status
  - Hardware detection ready, SDK stub implementation
- **Device discovery** via USB HID enumeration with connection status

### 📊 **Hardware Monitoring**
- **Real-time telemetry** - CPU/GPU temp, load, clock speeds, RAM, SSD temp
- **History charts** with 60-sample rolling window and smart change detection (0.5° threshold reduces UI updates)
- **Low overhead mode** disables charts to reduce CPU usage from ~2% to <0.5%
- **Detailed metrics** - per-core clocks, VRAM usage, disk activity

### 🧹 **System Optimization**
- **HP OMEN Gaming Hub removal** - guided cleanup with dry-run mode
  - Removes Store packages (`AD2F1837.*`, `HPInc.HPGamingHub`)
  - Cleans registry keys, scheduled tasks, startup entries
  - Deletes residual files from Program Files and AppData
  - Creates system restore point before destructive operations
- **Gaming Mode** - one-click optimization (disables animations, toggles services)
- **Service management** - control Windows Game Bar, Xbox services, telemetry

### 🔄 **Auto-Update**
- **In-app update checker** polls GitHub releases every 6 hours
- **SHA256 verification** required for security (updates rejected without hash)
- **One-click install** with download progress and integrity validation
- **Manual fallback** if automated install blocked (hash missing)

---

## 🎯 HP Gaming Hub Feature Parity

OmenCore is designed to **completely replace** OMEN Gaming Hub. You can safely uninstall OGH.

| HP Gaming Hub Feature | OmenCore Status | Notes |
|----------------------|----------------|-------|
| **Fan Control** | ✅ Full support | Custom curves + WMI BIOS presets (no OGH needed) |
| **Performance Modes** | ✅ Full support | CPU/GPU power limits via WMI |
| **CPU Undervolting** | ✅ Full support | Intel MSR access with safety |
| **GPU Power Boost** | ✅ Full support | +15W Dynamic Boost (PPAB) |
| **Keyboard RGB** | ✅ Profiles | Per-zone control with effects |
| **Hardware Monitoring** | ✅ Full support | LibreHardwareMonitor integration |
| **Gaming Mode** | ✅ Service toggles | One-click optimization |
| **Battery Care** | ✅ Full support | 80% charge limit |
| **Peripheral Control** | ⚠️ Beta (stub) | Hardware detection ready |
| **Hub Cleanup** | ✅ Exclusive | Safe Gaming Hub removal |
| **Per-Game Profiles** | ✅ Full support | Auto-switch on game detect |
| **In-Game Overlay** | ✅ Full support | Click-through OSD |
| **Network Booster** | ❌ Out of scope | Use router/Windows QoS |
| **Game Library** | ❌ Out of scope | Use Steam/Epic/Xbox app |
| **Omen Oasis** | ❌ Out of scope | Cloud gaming elsewhere |

**Verdict**: OmenCore covers **100% of essential Gaming Hub features** with better performance, no telemetry, no ads, and complete offline operation.

---

## 📋 Requirements

### System
- **OS**: Windows 10 (build 19041+) or Windows 11
- **Runtime**: Self-contained (.NET 8 embedded) - no separate installation needed
- **Privileges**: Administrator for WMI BIOS/EC/MSR operations
- **Disk**: 100 MB for app + 50 MB for logs/config
- **OGH**: ❌ **NOT REQUIRED** - OmenCore works without OMEN Gaming Hub

### Hardware
- **CPU**: Intel 6th-gen+ (Skylake or newer) for undervolting/TCC offset; AMD Ryzen supported for monitoring/fan control
- **Laptop**: HP OMEN 15/16/17 series and HP Victus (2019-2024 models)
  - ✅ Tested: OMEN 15-dh, 16-b, 16-k, 17-ck (2023), Victus 15/16
  - ✅ **OMEN Transcend 14/16**: Supported via WMI BIOS
  - ✅ **2023+ models**: Full WMI BIOS support, no OGH needed
- **Desktop**: HP OMEN 25L/30L/40L/45L (limited support)
  - ⚠️ Desktop PCs use different EC registers - fan control may not work
  - Monitoring, game profiles, and OGH cleanup still functional
  - Auto-detected via chassis type with warning message

### Fan Control Methods (Priority Order)
1. **WMI BIOS** (default) - No driver needed, works on all OMEN laptops
2. **EC Direct via PawnIO** - For advanced EC access (Secure Boot compatible)
3. **EC Direct via WinRing0** - Legacy driver (may need Secure Boot disabled)
4. **OGH Proxy** - Last resort fallback only if WMI fails (rare)

### Optional Drivers
- **PawnIO** (recommended for advanced features) - Secure Boot compatible EC access
- **WinRing0 v1.2** - Legacy kernel driver for EC/MSR access (may be blocked by Secure Boot)

**⚠️ Windows Defender False Positive**: WinRing0 is flagged as `HackTool:Win64/WinRing0` by antivirus. This is a **known false positive** for kernel hardware drivers. Add exclusion for `C:\Windows\System32\drivers\WinRing0x64.sys` and verify signature. See [WINRING0_SETUP.md](docs/WINRING0_SETUP.md).

**Compatibility Notes**:
- **Non-OMEN HP laptops**: Partial (monitoring yes, fan/RGB no due to different EC layout)
- **Other brands**: Not supported (EC registers are vendor-specific)
- **Virtual machines**: Monitoring-only mode (no hardware access)

---

## 🚀 Installation

### Option 1: Installer (Recommended)
1. Download `OmenCoreSetup-1.3.0-beta.exe` from [Releases](https://github.com/theantipopau/omencore/releases/latest)
2. Run installer as Administrator
3. (Optional) Select "Install PawnIO driver" for advanced EC features
4. Launch OmenCore from Start Menu or Desktop
5. (Optional) Use OGH Cleanup in Settings to remove OMEN Gaming Hub

### Option 2: Portable ZIP
1. Download `OmenCore-1.3.0-beta-win-x64.zip` from [Releases](https://github.com/theantipopau/omencore/releases/latest)
2. Extract to `C:\OmenCore` (or preferred location)
3. Right-click `OmenCore.exe` → Run as Administrator

### First Launch
- OmenCore auto-detects your model and selects the best fan control method
- WMI BIOS is used by default (no drivers needed for basic fan control)
- Config saved to `%APPDATA%\OmenCore\config.json`
- Logs written to `%LOCALAPPDATA%\OmenCore\OmenCore_<timestamp>.log`

### Uninstalling OMEN Gaming Hub
OmenCore includes a built-in **OGH Cleanup** tool (Settings tab):
1. Creates a System Restore point (safety)
2. Removes HP Gaming Hub Store packages
3. Cleans registry entries and scheduled tasks
4. Deletes residual files from Program Files/AppData

**After cleanup, OmenCore provides full fan control without OGH.**

---

## 🏗️ Architecture

**Technology Stack**:
- **.NET 8.0** (Windows 10.0.19041.0+) with nullable reference types
- **WPF** with hardware-accelerated rendering
- **LibreHardwareMonitor** for sensor polling (WinRing0 kernel driver)
- **EC Direct Access** for fan/LED control via Embedded Controller
- **Intel MSR** for CPU undervolting (Model-Specific Registers)

**Project Structure**:
```
OmenCore/
├── src/OmenCoreApp/
│   ├── ViewModels/          # MVVM pattern with sub-ViewModels
│   │   ├── MainViewModel.cs       (Main window, DI hub)
│   │   ├── FanControlViewModel    (Fan curves + presets)
│   │   ├── DashboardViewModel     (Telemetry aggregation)
│   │   └── SystemControlViewModel (Perf + undervolt + cleanup)
│   ├── Services/            # Business logic
│   │   ├── FanService             (EC writes, curve application)
│   │   ├── UndervoltService       (MSR writes, probe loop)
│   │   ├── HardwareMonitoringService (telemetry + change detect)
│   │   ├── AutoUpdateService      (GitHub API, SHA256 verify)
│   │   └── CorsairDeviceService   (iCUE SDK abstraction)
│   ├── Hardware/            # Low-level drivers
│   │   ├── WinRing0EcAccess       (EC I/O with safety allowlist)
│   │   ├── LibreHardwareMonitorImpl (sensor bridge)
│   │   └── IntelUndervoltProvider (MSR 0x150 writes)
│   ├── Views/               # UI layer
│   │   ├── MainWindow.xaml        (Tab host, 1000+ lines)
│   │   ├── FanControlView.xaml    (Fan UI)
│   │   └── DashboardView.xaml     (Telemetry cards)
│   ├── Controls/            # Custom WPF controls
│   │   ├── ThermalChart.xaml      (Temperature line chart)
│   │   └── LoadChart.xaml         (CPU/GPU load chart)
│   └── Utils/
│       ├── TrayIconService        (32px badge renderer)
│       └── LoggingService         (Async file writer)
├── installer/
│   └── OmenCoreInstaller.iss (Inno Setup script)
├── config/
│   └── default_config.json   (Preset definitions)
├── docs/
│   ├── CHANGELOG.md
│   ├── UPDATE_SUMMARY_2025-12-10.md
│   └── WINRING0_SETUP.md
└── VERSION.txt              (Semantic version)
```

**Design Principles**:
- **Safety First**: EC write allowlist blocks dangerous registers (battery, VRM, charger)
- **Async by Default**: All I/O uses `async/await` for UI responsiveness
- **Change Detection**: UI updates only when telemetry changes >0.5° or >0.5%
- **Graceful Degradation**: Services fail independently (no driver? disable fan control only)
- **Testability**: Unit tests for hardware access, services, and ViewModels

---

## 🛠️ Development

### Build Requirements
1. **Visual Studio 2022** (Community/Professional/Enterprise)
   - Workload: .NET Desktop Development
   - Optional: C++ Desktop Development (for driver projects)
2. **.NET 8 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
3. **Inno Setup** (for installer) - [Download](https://jrsoftware.org/isdl.php)

### Build from Source
```powershell
# Clone repository
git clone https://github.com/theantipopau/omencore.git
cd omencore

# Restore NuGet packages
dotnet restore OmenCore.sln

# Build Debug (for development)
dotnet build OmenCore.sln --configuration Debug

# Build Release (for distribution)
dotnet build OmenCore.sln --configuration Release

# Run from Visual Studio (F5) or command line
cd src\OmenCoreApp\bin\Release\net8.0-windows10.0.19041.0
.\OmenCore.exe
```

**⚠️ Must run as Administrator** for EC/MSR/driver access.

### Build Installer
```powershell
# Requires Inno Setup installed and 'iscc' on PATH
pwsh ./build-installer.ps1 -Configuration Release -Runtime win-x64 -SingleFile

# Outputs:
# - artifacts/OmenCore-1.0.0.7-win-x64.zip
# - artifacts/OmenCoreSetup-1.0.0.7.exe
```

### Run Tests
```powershell
# Run all unit tests
dotnet test OmenCore.sln

# Run tests with coverage
dotnet test OmenCore.sln --collect:"XPlat Code Coverage"

# Test specific project
dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj
```

---

## 📦 Release Process

1. **Update version** in `VERSION.txt`:
  ```
  1.0.0.7
  ```

2. **Create changelog entry** in `CHANGELOG.md`:
  ```markdown
  ## [1.0.0.7] - 2025-12-11
  ### Added
  - New feature description
  ### Fixed
  - Bug fix description
  ```

3. **Commit and tag**:
  ```bash
  git add VERSION.txt CHANGELOG.md
  git commit -m "Bump version to 1.0.0.7"
  git tag v1.0.0.7
  git push origin main
  git push origin v1.0.0.7
  ```

4. **GitHub Actions** automatically:
   - Builds Release configuration
   - Runs tests
   - Creates ZIP and installer
   - Publishes to GitHub Releases

5. **Manual release notes** (required for auto-updater):
   ```markdown
   ## What's New
   - Feature 1
   - Feature 2

   ## Bug Fixes
   - Fixed issue 1
   - Fixed issue 2

  SHA256: 54323D1F2F92086988A95EA7BD3D85CFDCC2F2F9348DA294443C7B6EB8AB6B23
   ```
   **⚠️ Include SHA256 hash** or in-app updater will require manual download.

---

## ⚙️ Configuration

### Config File Location
- **User config**: `%APPDATA%\OmenCore\config.json`
- **Default template**: `config/default_config.json` (in installation folder)
- **Open folder**: Click "Open Config Folder" in Settings tab

### Key Configuration Sections

**EC Device Path** (Hardware Access):
```json
{
  "ecDevicePath": "\\\\.\\WinRing0_1_2",
  "ecFanRegisterMap": {
    "fan1DutyCycle": 68,    // 0x44
    "fan2DutyCycle": 69,    // 0x45
    "fanMode": 70           // 0x46
  }
}
```

**Fan Presets**:
```json
{
  "fanPresets": [
    {
      "name": "Quiet",
      "curve": [
        { "temperatureC": 40, "fanPercent": 25 },
        { "temperatureC": 60, "fanPercent": 40 },
        { "temperatureC": 80, "fanPercent": 65 }
      ]
    }
  ]
}
```

**Performance Modes**:
```json
{
  "performanceModes": [
    {
      "name": "Balanced",
      "cpuPowerLimitWatts": 45,
      "gpuPowerLimitWatts": 80,
      "windowsPowerPlanGuid": "381b4222-f694-41f0-9685-ff5bb260df2e"
    }
  ]
}
```

**Lighting Profiles**:
```json
{
  "lightingProfiles": [
    {
      "name": "Red Wave",
      "effect": "Wave",
      "primaryColor": "#FF0000",
      "secondaryColor": "#8B0000",
      "zones": [
        { "id": "WASD", "color": "#FF0000", "brightness": 100 }
      ]
    }
  ]
}
```

### Reload Configuration
- Click "Reload Config" in Settings tab
- Or restart OmenCore
- Changes take effect immediately (no restart needed for most settings)

---

## 🔧 Advanced Usage

### EC Register Customization
If your HP OMEN model uses different EC registers:

1. Use [RWEverything](http://rweverything.com/) or similar tool to dump EC
2. Locate fan duty cycle registers (usually 0x44-0x46 range)
3. Update `ecFanRegisterMap` in `config.json`
4. **Test on sacrificial hardware first!**

**Safety allowlist** in `WinRing0EcAccess.cs` blocks writes to:
- Battery charger (0xFF range)
- VRM control (0x10-0x20 range)
- Unknown registers

### Custom Fan Curves
Create advanced curves in `config.json`:
```json
{
  "name": "Gaming",
  "curve": [
    { "temperatureC": 30, "fanPercent": 20 },
    { "temperatureC": 50, "fanPercent": 35 },
    { "temperatureC": 65, "fanPercent": 55 },
    { "temperatureC": 75, "fanPercent": 75 },
    { "temperatureC": 85, "fanPercent": 95 },
    { "temperatureC": 95, "fanPercent": 100 }
  ]
}
```

### Undervolt Tuning
**⚠️ Start conservative, test with stress tests (Prime95, OCCT)**

1. Start with -50mV core / -50mV cache
2. Run stress test for 30 minutes
3. If stable, decrease by -10mV
4. Repeat until unstable, then back off +10mV
5. Typical safe range: -80mV to -125mV (varies per CPU)

**Signs of instability**:
- Blue screen (CLOCK_WATCHDOG_TIMEOUT)
- Immediate reboot
- Application crashes
- Throttling to 800 MHz

---

## 🐛 Troubleshooting

### "WinRing0 driver not detected"
**Cause**: Kernel driver not installed or failed to load

**Solutions**:
1. Run installer with "Install WinRing0" task selected
2. OR install [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases) and run once
3. Check for driver conflicts (AI Suite, other hardware tools)
4. Disable Secure Boot if driver won't load
5. Verify driver: `Get-Service WinRing0_1_2_0` in PowerShell

### "Access Denied" errors
**Cause**: OmenCore not running as Administrator

**Solution**: Right-click `OmenCore.exe` → "Run as administrator"

### Fan control not working
**Possible causes**:
1. WinRing0 driver not loaded
2. Incorrect EC register addresses for your model
3. BIOS fan control override enabled
4. Conflicting software (HP Command Center, other fan tools)

**Debug steps**:
1. Check logs in `%LOCALAPPDATA%\OmenCore\`
2. Verify EC device opens: Look for "EC bridge ready" in logs
3. Try Max preset (simplest test)
4. Check BIOS settings for fan control options

### Undervolting doesn't work
**Possible causes**:
1. CPU doesn't support undervolting (10th-gen+ may have locked MSR)
2. BIOS setting "Undervolting Lock" enabled
3. ThrottleStop/Intel XTU conflicting
4. Wrong CPU architecture (AMD not supported)

**Solutions**:
1. Check BIOS for "Overclocking" or "Undervolting" settings
2. Exit other undervolting tools
3. Try Intel XTU to verify MSR accessibility
4. Some laptops have undervolting permanently locked

### Auto-update fails
**Cause**: Missing SHA256 hash in release notes

**Solution**: Download manually from [Releases page](https://github.com/theantipopau/omencore/releases)

### High CPU usage
**Cause**: Monitoring polling too aggressive

**Solutions**:
1. Enable "Low Overhead Mode" in Dashboard tab
2. Increase polling interval in `config.json`:
   ```json
   { "pollIntervalMs": 2000 }
   ```
3. Disable history charts if not needed

---

## 📚 Documentation

- **[CHANGELOG.md](CHANGELOG.md)** - Version history and release notes
- **[UPDATE_SUMMARY_2025-12-10.md](docs/UPDATE_SUMMARY_2025-12-10.md)** - Detailed v1.0.0.4 changes
- **[WINRING0_SETUP.md](docs/WINRING0_SETUP.md)** - Driver installation and antivirus exclusions
- **[SDK_INTEGRATION_GUIDE.md](docs/SDK_INTEGRATION_GUIDE.md)** - Corsair/Logitech SDK integration
- **[PERFORMANCE_GUIDE.md](docs/PERFORMANCE_GUIDE.md)** - Undervolting and performance tuning

---

## 🤝 Contributing

Contributions welcome! Please read our [Contributing Guidelines](CONTRIBUTING.md) first.

**Areas needing help**:
- [ ] Corsair iCUE SDK integration (replace stub)
- [ ] Logitech G HUB SDK integration (replace stub)
- [ ] Per-game profile switching
- [ ] In-game overlay (FPS/temps)
- [ ] EC register database for more OMEN models
- [ ] Localization (translations)

**Testing needed**:
- OMEN 15-en, 16-n, 17-ck models
- Desktop OMEN 25L/30L/40L/45L
- Windows 11 24H2

---

## 📄 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) file for details.

**Third-party components**:
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) - MPL 2.0
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) - Code Project Open License
- WinRing0 driver - OpenLibSys license

---

## ⚠️ Disclaimer

**THIS SOFTWARE IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND.**

- Modifying EC registers, undervolting, and mux switching can potentially damage hardware
- Always test on non-production hardware first
- Create system restore points before destructive operations
- The developers are not responsible for any hardware damage, data loss, or warranty voids
- HP does not endorse this project; use at your own risk

**Recommended precautions**:
1. Backup important data before first use
2. Monitor temperatures closely during initial testing
3. Start with conservative settings (lower undervolt, gentle fan curves)
4. Keep HP OMEN Gaming Hub installer for quick rollback if needed

---

## 🔗 Links

- **GitHub Repository**: https://github.com/theantipopau/omencore
- **Latest Release**: https://github.com/theantipopau/omencore/releases/latest
- **Issue Tracker**: https://github.com/theantipopau/omencore/issues
- **Discussions**: https://github.com/theantipopau/omencore/discussions
- **Discord Server**: https://discord.gg/ahcUC2Un
- **Subreddit**: https://reddit.com/r/omencore
- **Donate (PayPal)**: https://www.paypal.com/donate/?business=XH8CKYF8T7EBU&no_recurring=0&item_name=Thank+you+for+your+generous+donation%2C+this+will+allow+me+to+continue+developing+my+programs.&currency_code=AUD

---

## 💖 Support Development

If OmenCore has helped you get more out of your OMEN laptop, consider supporting development:

[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C.svg?logo=paypal&logoColor=white&style=for-the-badge)](https://www.paypal.com/donate/?business=XH8CKYF8T7EBU&no_recurring=0&item_name=Thank+you+for+your+generous+donation%2C+this+will+allow+me+to+continue+developing+my+programs.&currency_code=AUD)

Your support helps cover development time and testing hardware. Thank you! 🙏

---

## 🙏 Acknowledgments

- LibreHardwareMonitor team for sensor framework
- RWEverything for EC exploration tools
- ThrottleStop community for undervolting knowledge
- HP OMEN laptop owners who tested early builds
- Discord community for feedback and bug reports

---

**Made with ❤️ for the HP OMEN community**
