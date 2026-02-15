# OmenCore Linux Installation Guide

Complete guide for installing and running OmenCore on Linux distributions. OmenCore provides **CLI** and **GUI (Avalonia)** interfaces for controlling HP OMEN & Victus laptops.

---

## Table of Contents

- [System Requirements](#system-requirements)
- [Quick Install](#quick-install)
- [Distribution-Specific Setup](#distribution-specific-setup)
  - [Ubuntu / Pop!_OS / Debian](#ubuntu--popos--debian)
  - [Fedora](#fedora)
  - [Arch Linux / CachyOS / Manjaro](#arch-linux--cachyos--manjaro)
  - [openSUSE](#opensuse)
- [Kernel Module Setup](#kernel-module-setup)
- [GUI Installation](#gui-installation)
- [CLI Usage](#cli-usage)
- [Daemon / Background Service](#daemon--background-service)
- [Configuration](#configuration)
- [Fan Control Methods](#fan-control-methods)
- [Supported Models & Features](#supported-models--features)
- [Troubleshooting](#troubleshooting)
- [Uninstallation](#uninstallation)

---

## System Requirements

| Requirement | Details |
|-------------|---------|
| **OS** | Any x86_64 Linux distribution (glibc-based) |
| **Kernel** | 5.15+ (pre-2023 models), 6.5+ (2023+ models), 6.18+ recommended |
| **Runtime** | Self-contained — no .NET runtime needed |
| **Privileges** | Root/sudo required for hardware access |
| **GPU** | NVIDIA: `nvidia-smi` for GPU monitoring (optional) |
| **Display** | X11 or Wayland for GUI (CLI works headless) |

---

## Quick Install

### CLI Only (Recommended for servers/headless)

```bash
# Download latest release
wget https://github.com/theantipopau/omencore/releases/latest/download/OmenCore-2.8.6-linux-x64.zip

# Extract
unzip OmenCore-2.8.6-linux-x64.zip
cd OmenCore-linux-x64

# Make executable
chmod +x omencore-cli

# Verify it works
sudo ./omencore-cli status

# (Optional) Install system-wide
sudo cp omencore-cli /usr/local/bin/
```

### GUI + CLI

```bash
# Download latest release
wget https://github.com/theantipopau/omencore/releases/latest/download/OmenCore-2.8.6-linux-x64.zip

# Extract
unzip OmenCore-2.8.6-linux-x64.zip
cd OmenCore-linux-x64

# Make both executables
chmod +x omencore-cli omencore-gui

# Run GUI (requires display)
sudo ./omencore-gui

# Or run CLI
sudo ./omencore-cli status
```

---

## Distribution-Specific Setup

### Ubuntu / Pop!_OS / Debian

```bash
# Install dependencies
sudo apt update
sudo apt install -y unzip wget libice6 libsm6 libx11-6 libxext6 libxrandr2 libxi6

# Download and extract OmenCore
wget https://github.com/theantipopau/omencore/releases/latest/download/OmenCore-2.8.6-linux-x64.zip
unzip OmenCore-2.8.6-linux-x64.zip
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

# Load kernel modules
sudo modprobe hp-wmi
sudo modprobe ec_sys write_support=1  # May fail on newer kernels — that's OK

# Test
sudo ./omencore-cli status
```

**Ubuntu-specific notes:**
- **AppArmor** may block EC access. Check `dmesg | grep apparmor` for denials.
- **Secure Boot** may prevent loading unsigned kernel modules. Either disable Secure Boot or sign the module.

### Fedora

```bash
# Install dependencies
sudo dnf install -y unzip wget libICE libSM libX11 libXext libXrandr libXi

# Download and extract
wget https://github.com/theantipopau/omencore/releases/latest/download/OmenCore-2.8.6-linux-x64.zip
unzip OmenCore-2.8.6-linux-x64.zip
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

# Load HP-WMI module
sudo modprobe hp-wmi

# Test
sudo ./omencore-cli status
```

**Fedora-specific notes:**
- **Fedora 43+** removed `ec_sys` from the default kernel. Use `hp-wmi` for 2023+ models.
- **SELinux** may require policy adjustments. Run `sudo setsebool -P domain_can_mmap_files 1` if you encounter permission issues.

### Arch Linux / CachyOS / Manjaro

```bash
# Install dependencies (most already present)
sudo pacman -S --needed unzip wget libice libsm libx11 libxext libxrandr libxi

# Download and extract
wget https://github.com/theantipopau/omencore/releases/latest/download/OmenCore-2.8.6-linux-x64.zip
unzip OmenCore-2.8.6-linux-x64.zip
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

# Load kernel modules
sudo modprobe hp-wmi
sudo modprobe ec_sys write_support=1

# Test
sudo ./omencore-cli status
```

**Arch-specific notes:**
- After kernel updates, modules may need to be reloaded: `sudo modprobe -r hp-wmi && sudo modprobe hp-wmi`
- CachyOS users with custom kernels may have additional hp-wmi patches pre-applied.

### openSUSE

```bash
# Install dependencies
sudo zypper install unzip wget libICE6 libSM6 libX11-6 libXext6 libXrandr2 libXi6

# Download and extract
wget https://github.com/theantipopau/omencore/releases/latest/download/OmenCore-2.8.6-linux-x64.zip
unzip OmenCore-2.8.6-linux-x64.zip
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

# Load kernel modules
sudo modprobe hp-wmi

# Test
sudo ./omencore-cli status
```

---

## Kernel Module Setup

OmenCore uses different kernel modules depending on your OMEN model generation.

### Determine Your Model

```bash
# Check model name
cat /sys/class/dmi/id/product_name

# Check BIOS version
cat /sys/class/dmi/id/bios_version

# Check kernel version
uname -r
```

### Module Requirements by Model

| OMEN Generation | Module | Setup |
|-----------------|--------|-------|
| **2025+ OMEN Max** | `hp-wmi` (hwmon PWM) | `sudo modprobe hp-wmi` |
| **2023+ (13th Gen+)** | `hp-wmi` | `sudo modprobe hp-wmi` |
| **2020–2022** | `ec_sys` | `sudo modprobe ec_sys write_support=1` |
| **Pre-2020** | `ec_sys` | `sudo modprobe ec_sys write_support=1` |

### Make Modules Persistent (Survive Reboot)

```bash
# Create module load file
echo -e "hp-wmi\nec_sys" | sudo tee /etc/modules-load.d/omencore.conf

# Create module options file
echo "options ec_sys write_support=1" | sudo tee /etc/modprobe.d/omencore.conf

# Reload module configuration
sudo systemctl restart systemd-modules-load.service
```

### Verify Module Status

```bash
# Check loaded modules
lsmod | grep -E "hp_wmi|ec_sys"

# Check HP-WMI sysfs (2023+ models)
ls /sys/devices/platform/hp-wmi/ 2>/dev/null

# Check EC access (pre-2023 models)
ls /sys/kernel/debug/ec/ec0/io 2>/dev/null

# Check ACPI platform profile (2025+ models)
cat /sys/firmware/acpi/platform_profile 2>/dev/null
cat /sys/firmware/acpi/platform_profile_choices 2>/dev/null
```

---

## GUI Installation

The Avalonia-based GUI provides a graphical dashboard similar to the Windows version.

### Requirements

- X11 or Wayland display server
- GUI libraries: `libice`, `libsm`, `libx11`, `libxext`, `libxrandr`, `libxi`

### Running the GUI

```bash
# Standard launch
sudo ./omencore-gui

# If using Wayland
DISPLAY=:0 sudo ./omencore-gui

# Or with explicit Wayland
WAYLAND_DISPLAY=wayland-0 sudo ./omencore-gui

# Check for missing libraries
ldd ./omencore-gui | grep "not found"
```

### Desktop Entry (Optional)

Create a `.desktop` file for application menu integration:

```bash
sudo tee /usr/share/applications/omencore.desktop << 'EOF'
[Desktop Entry]
Name=OmenCore
Comment=HP OMEN & Victus Laptop Control Center
Exec=pkexec /usr/local/bin/omencore-gui
Icon=utilities-system-monitor
Terminal=false
Type=Application
Categories=System;HardwareSettings;
Keywords=fan;temperature;omen;hp;gaming;
EOF
```

### System Tray (Optional)

OmenCore includes a Python-based system tray script:

```bash
# Install Python dependency
pip install pystray pillow

# Run tray icon (requires omencore-cli in PATH)
python3 scripts/omencore-tray.sh &
```

---

## CLI Usage

### System Status

```bash
# Full system overview
sudo omencore-cli status

# JSON output (for scripting)
sudo omencore-cli status --json

# Pipe to jq for specific fields
sudo omencore-cli status --json | jq '.cpu_temp'
```

### Fan Control

```bash
# View current fan status
sudo omencore-cli fan --status

# Apply fan profiles
sudo omencore-cli fan --profile auto       # BIOS-managed (default)
sudo omencore-cli fan --profile silent     # Low noise
sudo omencore-cli fan --profile balanced   # Balanced
sudo omencore-cli fan --profile gaming     # Gaming workloads
sudo omencore-cli fan --profile max        # Maximum cooling

# Set specific fan speed (percentage)
sudo omencore-cli fan --speed 70

# Set individual fan RPMs
sudo omencore-cli fan --fan1 3000 --fan2 2800

# Custom fan curve
sudo omencore-cli fan --curve "40:20,50:30,60:50,70:70,80:90,90:100"
# Format: temp1:speed1,temp2:speed2,...

# Enable/disable fan boost
sudo omencore-cli fan --boost

# Battery-aware auto mode
sudo omencore-cli fan --battery-aware
```

### Performance Modes

```bash
# Switch performance mode
sudo omencore-cli perf --mode default      # Balanced
sudo omencore-cli perf --mode balanced     # Moderate boost
sudo omencore-cli perf --mode performance  # Maximum boost
sudo omencore-cli perf --mode cool         # Throttled for quiet

# Set TCC offset (Intel thermal throttle)
sudo omencore-cli perf --tcc 5             # Throttle 5°C below TjMax

# Set thermal power limit level (0-5)
sudo omencore-cli perf --power-limit 3
```

### Keyboard RGB

```bash
# Set all zones to one color
sudo omencore-cli keyboard --color FF0000  # Red

# Set specific zone (0-3)
sudo omencore-cli keyboard --zone 0 --color 00FF00  # Zone 1 green

# Set brightness (0-100)
sudo omencore-cli keyboard --brightness 80

# Turn off keyboard backlight
sudo omencore-cli keyboard --off
```

### Battery Management

```bash
# Check battery status
sudo omencore-cli battery status

# Set charge threshold (limits max charge)
sudo omencore-cli battery threshold 80     # Don't charge above 80%
sudo omencore-cli battery threshold 0      # Disable threshold (charge to 100%)

# Set battery profile
sudo omencore-cli battery profile quiet         # Low power
sudo omencore-cli battery profile balanced      # Normal
sudo omencore-cli battery profile performance   # Max power on battery
```

### Real-Time Monitoring

```bash
# Live dashboard with color-coded progress bars
sudo omencore-cli monitor

# Custom poll interval (milliseconds)
sudo omencore-cli monitor --interval 2000
```

### Diagnostics

```bash
# Full environment diagnostics
sudo omencore-cli diagnose

# JSON diagnostics output
sudo omencore-cli diagnose --json

# Generate a debug report for GitHub issues
sudo omencore-cli diagnose --report > omencore-report.txt

# Export diagnostics to file
sudo omencore-cli diagnose --export /tmp/omencore-diag.json
```

### Configuration

```bash
# Show current configuration
omencore-cli config --show

# Set configuration values
omencore-cli config --set fan.profile=gaming
omencore-cli config --set keyboard.color=00FF00
omencore-cli config --set general.poll_interval_ms=3000
```

---

## Daemon / Background Service

Run OmenCore as a background service to automatically manage fans, apply profiles on boot, and maintain custom fan curves.

### Install as systemd Service

```bash
# Install the service
sudo ./omencore-cli daemon --install

# Enable on boot
sudo systemctl enable omencore

# Start now
sudo systemctl start omencore

# Check status
sudo systemctl status omencore
```

### Manage the Service

```bash
# Stop the service
sudo systemctl stop omencore

# Restart (reloads config)
sudo systemctl restart omencore

# View live logs
journalctl -u omencore -f

# View last 50 log lines
journalctl -u omencore -n 50
```

### Run in Foreground (Debugging)

```bash
# Run daemon in foreground (useful for debugging)
sudo ./omencore-cli daemon --run

# With custom config
sudo ./omencore-cli daemon --run --config /path/to/config.toml
```

### Generate Service Files

```bash
# Print systemd unit file (for manual installation)
./omencore-cli daemon --generate-service

# Generate default config file
./omencore-cli daemon --generate-config > ~/.config/omencore/config.toml
```

### Uninstall Service

```bash
sudo ./omencore-cli daemon --uninstall
```

### Daemon Features

- **Auto fan curve** — Continuously adjusts fan speed based on temperature with configurable curve
- **Hysteresis** — Prevents fan speed oscillation (default 3°C deadzone)
- **Battery-aware** — Reduces fan speed by 20% on battery power (below 85°C)
- **Config reload** — Watches config file for changes (restart service apply)
- **Low-overhead mode** — On battery: longer poll intervals (5s vs 2s), reduced logging, cached sensor paths
- **Graceful shutdown** — Restores BIOS fan control on exit
- **Auto-restart** — systemd restarts service on failure (5s delay)

---

## Configuration

### Config File Locations

| Location | Priority | Use Case |
|----------|----------|----------|
| `/etc/omencore/config.toml` | System-wide | Daemon/service settings |
| `~/.config/omencore/config.toml` | Per-user | User preferences |
| `~/.config/omencore/config.json` | Per-user | Quick key-value settings |

### Configuration Reference

```toml
[general]
poll_interval_ms = 2000          # Sensor polling interval
log_level = "info"               # debug, info, warn, error

[general.low_overhead]           # Battery-aware throttling
enable_on_battery = true
poll_interval_ms = 5000          # Slower polling on battery
disable_sensor_scanning = true
reduce_logging = true

[fan]
profile = "auto"                 # auto, silent, balanced, gaming, max, custom
boost = false                    # Fan boost toggle
smooth_transition = true         # Gradual fan speed changes

[fan.curve]                      # Used when profile = "custom"
enabled = false
hysteresis = 3                   # °C deadzone to prevent oscillation
points = [
    { temp = 40, speed = 20 },
    { temp = 50, speed = 30 },
    { temp = 60, speed = 50 },
    { temp = 70, speed = 70 },
    { temp = 80, speed = 90 },
    { temp = 90, speed = 100 }
]

[performance]
mode = "balanced"                # default, balanced, performance, cool

[keyboard]
enabled = true
color = "FF0000"
brightness = 100

[startup]
apply_on_boot = true             # Apply saved settings when daemon starts
restore_on_exit = true           # Restore BIOS defaults when daemon exits
```

---

## Fan Control Methods

OmenCore automatically selects the best fan control method available for your hardware.

### Method Priority (Highest to Lowest)

1. **hp-wmi sysfs** (2023+ models)
   - Path: `/sys/devices/platform/hp-wmi/`
   - Uses `thermal_profile`, `fan1_output`/`fan2_output`, `fan_always_on`
   - Most reliable for modern models

2. **hp-wmi hwmon PWM** (2025+ OMEN Max)
   - Path: `/sys/devices/platform/hp-wmi/hwmon/hwmonN/`
   - Direct PWM control: `pwm1_enable` (0=full, 1=manual, 2=auto)
   - RPM readback from `fan1_input`/`fan2_input`

3. **ACPI Platform Profile** (kernel 5.18+)
   - Path: `/sys/firmware/acpi/platform_profile`
   - Values: `low-power`, `balanced`, `performance`
   - Profile-based rather than direct fan control

4. **ec_sys Direct EC** (pre-2023 models)
   - Path: `/sys/kernel/debug/ec/ec0/io`
   - Direct reads/writes to EC registers
   - Based on [omen-fan](https://github.com/alou-S/omen-fan) register documentation

### Safety Protections

- **2025+ OMEN Max models** (`16t-ah0xxx`, `17t-ah0xxx`) — EC writes are **blocked** to prevent EC panic
- Fan speed 0% is blocked in manual mode to prevent overheating
- BIOS auto control is always restorable via `omencore-cli fan --profile auto`
- Daemon restores BIOS control on service stop/crash

---

## Supported Models & Features

| Feature | hp-wmi (2023+) | hwmon PWM (2025+) | ec_sys (pre-2023) |
|---------|:--------------:|:-----------------:|:-----------------:|
| Fan profile switching | Yes | Yes | Yes |
| Manual fan speed % | Varies | Yes | Yes |
| Custom fan curves | Varies | Yes | Yes |
| RPM readback | If exposed | Yes | Yes |
| Keyboard RGB (4-zone) | Yes | Yes | Yes |
| Battery threshold | Yes | Yes | N/A |
| Performance modes | Yes | Yes | EC-based |
| Temperature monitoring | Yes | Yes | Yes |

### Known Working Models

| Model | Kernel | Method | Notes |
|-------|--------|--------|-------|
| OMEN 15 2020 | 5.15+ | ec_sys | Full EC access |
| OMEN 16 2022 | 5.19+ | ec_sys | Full EC access |
| OMEN 16 2023 (wf0xxx) | 6.5+ | hp-wmi | Partial fan control |
| OMEN 17 2021 | 5.15+ | ec_sys | Full EC access |
| OMEN 16 2024 (xd0xxx) | 6.5+ | hp-wmi | WMI fan control |
| OMEN Max 2025 | 6.18+ | hwmon PWM | EC writes blocked |

### Run Diagnostics to Check Your Model

```bash
sudo ./omencore-cli diagnose
```

This will show:
- Detected model name and product ID
- Available kernel modules
- Accessible sysfs paths
- Recommended fan control method
- Feature compatibility matrix

---

## Troubleshooting

### "Permission denied" Errors

OmenCore requires root access for hardware control:
```bash
sudo ./omencore-cli status
sudo ./omencore-gui
```

### "ec_sys module not found"

Some distributions (Fedora 43+, some Arch builds) don't include `ec_sys`:

1. **Use hp-wmi instead** (2023+ models):
   ```bash
   sudo modprobe hp-wmi
   ```

2. **Check if debugfs is mounted** (required for ec_sys):
   ```bash
   mount | grep debugfs
   # If not mounted:
   sudo mount -t debugfs debugfs /sys/kernel/debug
   ```

### Fan Control Has No Effect

1. **Check which method was detected**:
   ```bash
   sudo ./omencore-cli diagnose
   ```

2. **For very new models** (2025+), the hp-wmi driver may not yet support your laptop:
   ```bash
   dmesg | grep -i wmi
   dmesg | grep -i omen
   ```

3. **Generate a debug report** for the developer:
   ```bash
   sudo ./omencore-cli diagnose --report > omencore-report.txt
   ```

### GUI Won't Start

```bash
# Check for missing X11 libraries
ldd ./omencore-gui | grep "not found"

# Try with explicit display
DISPLAY=:0 sudo ./omencore-gui

# For Wayland, try XWayland
XDG_SESSION_TYPE=x11 sudo ./omencore-gui
```

### Temperatures Show 0°C

1. **Ensure kernel modules are loaded**:
   ```bash
   lsmod | grep -E "hp_wmi|coretemp|k10temp"
   ```

2. **Load temperature modules**:
   ```bash
   sudo modprobe coretemp   # Intel CPUs
   sudo modprobe k10temp    # AMD CPUs
   ```

### High CPU Usage From Daemon

Increase the poll interval:
```bash
sudo omencore-cli config --set general.poll_interval_ms=5000
sudo systemctl restart omencore
```

### Kernel Module Not Loading After Update

On rolling-release distros (Arch, CachyOS), kernel updates may require module reload:
```bash
sudo modprobe -r hp-wmi
sudo modprobe hp-wmi
```

---

## Uninstallation

### Remove OmenCore Files

```bash
# Stop daemon if running
sudo systemctl stop omencore 2>/dev/null
sudo systemctl disable omencore 2>/dev/null

# Uninstall daemon
sudo ./omencore-cli daemon --uninstall 2>/dev/null

# Remove installed binary
sudo rm -f /usr/local/bin/omencore-cli
sudo rm -f /usr/local/bin/omencore-gui

# Remove config and logs
sudo rm -rf /etc/omencore
rm -rf ~/.config/omencore

# Remove desktop entry
sudo rm -f /usr/share/applications/omencore.desktop

# Remove extracted files
rm -rf ~/OmenCore-linux-x64
```

### Remove Kernel Module Config (Optional)

Only remove if you no longer need hp-wmi or ec_sys for other purposes:
```bash
sudo rm -f /etc/modules-load.d/omencore.conf
sudo rm -f /etc/modprobe.d/omencore.conf
```

---

## Additional Resources

- [Main Documentation](../README.md) — Full feature overview
- [LINUX_TESTING.md](LINUX_TESTING.md) — Detailed testing guide for distro-specific validation
- [LINUX_QA_TESTING.md](LINUX_QA_TESTING.md) — QA testing checklist
- [Discord Server](https://discord.gg/9WhJdabGk8) — Community support
- [GitHub Issues](https://github.com/theantipopau/omencore/issues) — Bug reports and feature requests

---

## Contributing Linux Support

If your OMEN model isn't supported:

1. **Run diagnostics**: `sudo ./omencore-cli diagnose --report > report.txt`
2. **Share the report** on Discord or GitHub Issues
3. **Include**: Model name, product ID (`cat /sys/class/dmi/id/product_name`), kernel version, distro

This helps us add your model to the database and verify which control methods work.
