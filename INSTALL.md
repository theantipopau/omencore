# OmenCore Installation Guide

Complete installation instructions for OmenCore v3.4.0 on Windows and Linux.

---

## 📋 Table of Contents

- [Windows Installation](#-windows-installation)
  - [Option 1: Installer (Recommended)](#option-1-installer-recommended)
  - [Option 2: Portable ZIP](#option-2-portable-zip)
  - [Post-Installation](#post-installation-windows)
- [Linux Installation](#-linux-installation)
  - [Quick Start (GUI)](#quick-start-gui)
  - [Quick Start (CLI)](#quick-start-cli)
  - [Kernel Requirements](#kernel-requirements)
  - [CLI Usage](#cli-usage)
  - [Running as a Daemon](#running-as-a-daemon)
  - [Troubleshooting Linux](#troubleshooting-linux)
- [Uninstallation](#-uninstallation)
- [Additional Resources](#-additional-resources)

---

## 🪟 Windows Installation

### Option 1: Installer (Recommended)

1. **Download** `OmenCoreSetup-3.4.0.exe` from [Releases](https://github.com/theantipopau/omencore/releases/tag/v3.4.0)

2. **Verify** the SHA256 hash published in the release notes before running (optional but recommended)

3. **Run** the installer as Administrator

4. **Select options during install:**
   - ✅ Install PawnIO driver (RECOMMENDED — enables MSR access, Secure Boot compatible)
   - ✅ Create Start Menu shortcut
   - ☐ Create Desktop shortcut (optional)
   - ☐ Start with Windows (optional)

5. **Launch** OmenCore from the Start Menu

> **SmartScreen warning:** If Windows SmartScreen shows an "Unknown publisher" prompt, click **More info → Run anyway**. OmenCore is open-source and does not yet have an EV code-signing certificate. See [ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) if blocked by antivirus.

### Option 2: Portable ZIP

1. **Download** `OmenCore-3.4.0-win-x64.zip` from [Releases](https://github.com/theantipopau/omencore/releases/tag/v3.4.0)

2. **Verify SHA256** of the ZIP (hash published in GitHub Release notes)

3. **Extract** to any location (e.g. `C:\OmenCore`)

4. **Run** `OmenCore.exe` as Administrator (right-click → Run as administrator)

> For portable installs, PawnIO can be installed separately from [pawnio.eu](https://pawnio.eu) if advanced EC/MSR access is needed.

### Post-Installation (Windows)

- **Config location:** `%APPDATA%\OmenCore\config.json`
- **Logs location:** `%LOCALAPPDATA%\OmenCore\OmenCore_<timestamp>.log`
- OmenCore auto-detects your laptop model and selects the best fan control method (WMI BIOS by default — no drivers needed for basic operation)
- Use **Settings → OGH Cleanup** to safely remove OMEN Gaming Hub if desired

> **Startup hardware restore (v3.3.1+):** `EnableStartupHardwareRestore` is **disabled by default**. This feature applies saved hardware settings on every startup, but on OMEN 16 and Victus models it has been observed to cause CMOS state loss. Do **not** enable this option on those models unless you understand the risk and have a recovery plan.

---

## 🐧 Linux Installation

### Quick Start (GUI)

```bash
# 1. Download the Linux release
wget https://github.com/theantipopau/omencore/releases/download/v3.4.0/OmenCore-3.4.0-linux-x64.zip

# 2. Extract
mkdir -p OmenCore-linux-x64
unzip OmenCore-3.4.0-linux-x64.zip -d OmenCore-linux-x64
cd OmenCore-linux-x64

# 3. Make executables
chmod +x omencore-cli omencore-gui

# 4. Launch GUI (prefer normal user session)
./omencore-gui
```

> Prefer launching the GUI without `sudo`. OmenCore escalates privileged actions when needed.

> If you must launch as root (e.g. in a minimal session), preserve your display and session variables:
> ```bash
> sudo --preserve-env=DISPLAY,XAUTHORITY,XDG_RUNTIME_DIR,DBUS_SESSION_BUS_ADDRESS ./omencore-gui
> ```

> **Software rendering (v3.3.1+):** OmenCore automatically retries in software mode when GPU/GLX initialization fails. The choice is persisted in `render-startup-state.json` so subsequent launches skip the GPU attempt. You can also force it manually: `OMENCORE_GUI_RENDER_MODE=software ./omencore-gui`

> CachyOS / Arch / Fedora users: same steps. The build is self-contained and all .NET dependencies are bundled.

### Quick Start (CLI)

```bash
# 1. Download
wget https://github.com/theantipopau/omencore/releases/download/v3.4.0/OmenCore-3.4.0-linux-x64.zip

# 2. Extract
mkdir -p OmenCore-linux-x64
unzip OmenCore-3.4.0-linux-x64.zip -d OmenCore-linux-x64
cd OmenCore-linux-x64

# 3. Install to system path (optional)
sudo cp omencore-cli /usr/local/bin/
sudo chmod +x /usr/local/bin/omencore-cli

# 4. Test
sudo omencore-cli status
```

### Kernel Requirements

| OMEN Model | Recommended Kernel | Access Method | Notes |
|------------|-------------------|---------------|-------|
| **2023+ (13th Gen+)** | **6.18+** | `hp-wmi` | ✅ Best support — native sysfs fan control |
| 2023+ (13th Gen+) | 6.5–6.17 | `hp-wmi` | Basic support, some features limited |
| 2020–2022 | Any | `ec_sys` | `sudo modprobe ec_sys write_support=1` |
| Pre-2020 | Any | `ec_sys` | Limited; EC registers vary by model |

**Check your kernel version:**
```bash
uname -r
```

**Why kernel 6.18+?**
Linux 6.18 includes enhanced HP-WMI driver patches for OMEN laptops: native fan curve control via sysfs, improved thermal profile switching, and better RPM readback. Most gaming distros (CachyOS, Arch, Nobara) already ship 6.18+. Ubuntu LTS users can use [mainline](https://github.com/bkw777/mainline) to upgrade.

**For older models (pre-2023):**
```bash
# Load EC module with write support
sudo modprobe ec_sys write_support=1

# Make persistent across reboots
echo "ec_sys" | sudo tee /etc/modules-load.d/ec_sys.conf
echo "options ec_sys write_support=1" | sudo tee /etc/modprobe.d/ec_sys.conf
```

**For very new models (2025+):**
Brand-new OMEN models may not yet be in the hp-wmi driver. Check recognition with `dmesg | grep -i wmi`. Advanced users can patch the hp-wmi driver from [patchwork.kernel.org](https://patchwork.kernel.org/project/platform-driver-x86/list/).

### CLI Usage

```bash
# System status
sudo omencore-cli status
sudo omencore-cli status --json       # JSON output for scripting

# Fan control
sudo omencore-cli fan --profile auto
sudo omencore-cli fan --profile max
sudo omencore-cli fan --speed 80      # Set 80% speed manually

# Keyboard RGB
sudo omencore-cli keyboard --color FF0000 --brightness 100

# Battery management
sudo omencore-cli battery status
sudo omencore-cli battery threshold 80   # Limit charge to 80%

# Continuous monitoring
sudo omencore-cli monitor --interval 1000

# Diagnostics bundle (for bug reports)
sudo omencore-cli --report > omencore-report.txt
```

### Running as a Daemon

```bash
# Install systemd service
sudo omencore-cli daemon --install

# Enable and start
sudo systemctl enable omencore
sudo systemctl start omencore

# Check status and logs
sudo systemctl status omencore
journalctl -u omencore -f
```

**Configuration:**
- Daemon config: `/etc/omencore/config.toml`
- User config: `~/.config/omencore/config.toml`

Generate default config:
```bash
omencore-cli daemon --generate-config > ~/.config/omencore/config.toml
```

### Troubleshooting Linux

#### "Permission denied" errors

OmenCore requires root for hardware access:
```bash
./omencore-gui
sudo omencore-cli status
```

#### "ec_sys module not found"

Some distros (Fedora 43+, some Arch builds) don't ship `ec_sys`:

```bash
# On 2023+ models, use hp-wmi instead:
sudo modprobe hp-wmi

# Check if ACPI EC debug path is available:
ls /sys/kernel/debug/ec/ec0/io
```

#### Fan control has no effect

```bash
# Check whether your model is recognized by hp-wmi
dmesg | grep -i wmi
dmesg | grep -i omen

# If not recognized, generate a report for the issue tracker:
sudo omencore-cli --report > omencore-report.txt
```

#### GUI won't start (Avalonia)

```bash
# Check for missing system libraries
ldd ./omencore-gui | grep "not found"

# Try with an explicit display
DISPLAY=:0 ./omencore-gui

# If elevated launch is unavoidable, preserve DBus/session variables
sudo --preserve-env=DISPLAY,XAUTHORITY,XDG_RUNTIME_DIR,DBUS_SESSION_BUS_ADDRESS,OMENCORE_GUI_RENDER_MODE ./omencore-gui
```

Notes:
- OmenCore now retries once in software mode automatically when Linux renderer initialization fails.
- After repeated renderer startup failures, OmenCore can persist software as last-known-good render mode.

#### "Could not load file or assembly 'System.Runtime, Version=8.0.0.0'"

```bash
# Re-download fixed Linux package into a clean folder
rm -rf OmenCore-linux-x64
mkdir -p OmenCore-linux-x64
wget https://github.com/theantipopau/omencore/releases/download/v3.4.0/OmenCore-3.4.0-linux-x64.zip
unzip OmenCore-3.4.0-linux-x64.zip -d OmenCore-linux-x64
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

# Verify binary and retry
file ./omencore-cli
sudo ./omencore-cli status
```

#### "Method not found: Boolean System.OperatingSystem.IsWindows()"

```bash
# Fixed in v3.3.1+ Linux GUI package
./omencore-gui
```

---

## 🗑️ Uninstallation

### Windows — Installer

1. Exit OmenCore (right-click tray icon → Exit)
2. Uninstall via **Settings → Apps → OmenCore → Uninstall**
3. *Optional:* Remove PawnIO driver — Device Manager → System devices → PawnIO → Uninstall
4. *Optional:* Delete remaining user data:
   - `%APPDATA%\OmenCore\`
   - `%LOCALAPPDATA%\OmenCore\`

### Windows — Portable

Delete the folder where you extracted OmenCore. No registry entries or system files are created by the portable build.

### Linux

```bash
# Stop and disable daemon (if installed)
sudo systemctl stop omencore
sudo systemctl disable omencore

# Remove binaries
sudo rm -f /usr/local/bin/omencore-cli
sudo rm -f /usr/local/bin/omencore-gui

# Remove config and data
sudo rm -rf /etc/omencore
rm -rf ~/.config/omencore

# Remove extracted archive (if not installed system-wide)
rm -rf ~/OmenCore-linux-x64
```

---

## 📚 Additional Resources

- [README.md](../README.md) — Project overview, features, and requirements
- [docs/LINUX_INSTALL_GUIDE.md](docs/LINUX_INSTALL_GUIDE.md) — Extended Linux guide with distro-specific notes
- [docs/ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) — Antivirus false positive handling
- [docs/DEFENDER_FALSE_POSITIVE.md](docs/DEFENDER_FALSE_POSITIVE.md) — Windows Defender exclusion steps
- [docs/WINRING0_SETUP.md](docs/WINRING0_SETUP.md) — WinRing0 driver setup and troubleshooting
- [Discord Server](https://discord.gg/9WhJdabGk8) — Community support
- [GitHub Issues](https://github.com/theantipopau/omencore/issues) — Bug reports and feature requests
