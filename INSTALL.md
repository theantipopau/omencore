# OmenCore Installation Guide

Complete installation instructions for Windows and Linux.

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
  - [Running as a Daemon](#running-as-a-daemon)
  - [Troubleshooting Linux](#troubleshooting-linux)
- [Uninstallation](#-uninstallation)

---

## 🪟 Windows Installation

### Option 1: Installer (Recommended)

1. **Download** `OmenCoreSetup-2.7.1.exe` from [Releases](https://github.com/theantipopau/omencore/releases/latest)

2. **Run** the installer as Administrator

3. **Select options:**
   - ✅ Install PawnIO driver (RECOMMENDED - enables MSR access, Secure Boot compatible)
   - ✅ Create Start Menu shortcut
   - ☐ Create Desktop shortcut (optional)
   - ☐ Start with Windows (optional)

4. **Launch** OmenCore from Start Menu or Desktop

### Option 2: Portable ZIP

1. **Download** `OmenCore-2.7.1-win-x64.zip` from [Releases](https://github.com/theantipopau/omencore/releases/latest)

2. **Extract** to `C:\OmenCore` (or your preferred location)

3. **Run** `OmenCore.exe` as Administrator (right-click → Run as administrator)

> 💡 **Tip:** For portable installations, you can install PawnIO separately from [pawnio.eu](https://pawnio.eu)

### Post-Installation (Windows)

- **Config location:** `%APPDATA%\OmenCore\config.json`
- **Logs location:** `%LOCALAPPDATA%\OmenCore\OmenCore_<timestamp>.log`
- OmenCore auto-detects your laptop model and selects the best fan control method
- Use **Settings → OGH Cleanup** to remove OMEN Gaming Hub if desired

---

## 🐧 Linux Installation

### Quick Start (GUI)

For the graphical interface (Avalonia-based):

```bash
# 1. Download the Linux release
wget https://github.com/theantipopau/omencore/releases/download/v2.7.1/OmenCore-linux-x64.zip

# 2. Extract
unzip OmenCore-linux-x64.zip
cd OmenCore-linux-x64

# 3. Make executable
chmod +x OmenCore

# 4. Run with sudo (required for hardware access)
sudo ./OmenCore
```

> **CachyOS / Arch / Fedora users:** The same steps apply. The Linux build is self-contained and includes all dependencies.

### Quick Start (CLI)

For command-line only (no GUI):

```bash
# 1. Download CLI release
wget https://github.com/theantipopau/omencore/releases/download/v2.7.1/omencore-cli-linux-x64.tar.gz

# 2. Extract
tar -xzf omencore-cli-linux-x64.tar.gz

# 3. Install to system path
sudo cp omencore-cli /usr/local/bin/
sudo chmod +x /usr/local/bin/omencore-cli

# 4. Test
sudo omencore-cli status
```

### CLI Usage Examples

```bash
# View system status
sudo omencore-cli status
sudo omencore-cli status --json    # JSON output for scripting

# Fan control
sudo omencore-cli fan --profile auto
sudo omencore-cli fan --profile max
sudo omencore-cli fan --speed 80   # Set 80% speed

# Keyboard RGB
sudo omencore-cli keyboard --color FF0000 --brightness 100

# Battery management
sudo omencore-cli battery status
sudo omencore-cli battery threshold 80   # Limit charge to 80%

# Continuous monitoring
sudo omencore-cli monitor --interval 1000
```

### Kernel Requirements

| OMEN Model | Recommended Kernel | Access Method | Notes |
|------------|-------------------|---------------|-------|
| **2023+ (13th Gen+)** | **6.18+** | `hp-wmi` | ✅ Best support via HP-WMI driver |
| 2023+ (13th Gen+) | 6.5-6.17 | `hp-wmi` | Basic support, some features limited |
| 2020-2022 | Any | `ec_sys` | `sudo modprobe ec_sys write_support=1` |
| Pre-2020 | Any | `ec_sys` | Limited support, EC registers vary |

#### For Newer Models (2023+)

If you have kernel 6.18+, no special setup is needed - just run OmenCore with sudo.

Check your kernel version:
```bash
uname -r
```

#### For Older Models (Pre-2023)

Enable EC write access:
```bash
# Load module with write support
sudo modprobe ec_sys write_support=1

# Make persistent (survives reboot)
echo "ec_sys" | sudo tee /etc/modules-load.d/ec_sys.conf
echo "options ec_sys write_support=1" | sudo tee /etc/modprobe.d/ec_sys.conf
```

### Running as a Daemon

To run OmenCore as a background service:

```bash
# Install systemd service
sudo omencore-cli daemon --install

# Enable on boot
sudo systemctl enable omencore

# Start now
sudo systemctl start omencore

# Check status
sudo systemctl status omencore

# View logs
journalctl -u omencore -f
```

### Configuration (Linux)

- **Daemon config:** `/etc/omencore/config.toml`
- **User config:** `~/.config/omencore/config.toml`

Generate default config:
```bash
omencore-cli daemon --generate-config > ~/.config/omencore/config.toml
```

### Troubleshooting Linux

#### "Permission denied" errors

OmenCore requires root access for hardware control:
```bash
sudo ./OmenCore        # GUI
sudo omencore-cli      # CLI
```

#### "ec_sys module not found"

Some distros (Fedora 43+, some Arch builds) don't include `ec_sys`:

1. **Use hp-wmi instead (2023+ models):**
   ```bash
   sudo modprobe hp-wmi
   ```

2. **Check if acpi_ec works:**
   ```bash
   ls /sys/kernel/debug/ec/ec0/io
   ```

3. **Fedora users:** The `ec_sys` module was removed from the default kernel. Use `hp-wmi` for 2023+ models.

#### Fan control has no effect

For very new models (2025+), the hp-wmi driver may not yet support your laptop:
```bash
# Check if your model is recognized
dmesg | grep -i wmi
dmesg | grep -i omen
```

If not recognized, you may need to wait for kernel patches or use the `--report` flag to generate debug info:
```bash
sudo omencore-cli --report > omencore-report.txt
```

#### GUI won't start

For Avalonia GUI issues:
```bash
# Check for missing dependencies
ldd ./OmenCore | grep "not found"

# Try with explicit display
DISPLAY=:0 sudo ./OmenCore
```

---

## 🗑️ Uninstallation

### Windows - Installer

1. **Exit OmenCore** (right-click tray icon → Exit)
2. **Uninstall:** Settings → Apps → Search "OmenCore" → Uninstall
3. **Optional - Remove PawnIO:**
   - Device Manager → System devices → PawnIO → Uninstall device
4. **Optional - Delete remaining files:**
   - `C:\Program Files\OmenCore\`
   - `%APPDATA%\OmenCore\`
   - `%LOCALAPPDATA%\OmenCore\`

### Windows - Portable

Simply delete the folder where you extracted OmenCore.

### Linux

```bash
# Stop daemon if running
sudo systemctl stop omencore
sudo systemctl disable omencore

# Remove files
sudo rm /usr/local/bin/omencore-cli
sudo rm -rf /etc/omencore
rm -rf ~/.config/omencore

# Remove GUI if extracted
rm -rf ~/OmenCore-linux-x64
```

---

## 📚 Additional Resources

- [LINUX_TESTING.md](docs/LINUX_TESTING.md) - Detailed Linux testing guide
- [QUICK_START.md](docs/QUICK_START.md) - Quick start for new users
- [ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) - Antivirus false positive information
- [Discord Server](https://discord.gg/AMwVGGyn) - Community support
