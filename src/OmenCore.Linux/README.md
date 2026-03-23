# OmenCore Linux CLI

Command-line utility for controlling HP OMEN laptops on Linux.

## Features

- 🌀 **Fan Control**: Set profiles (auto, silent, balanced, gaming, max) or manual speed
- 🔥 **Performance Mode**: Switch between default, balanced, performance, and cool modes
- 💡 **Keyboard RGB**: Control 4-zone RGB keyboard lighting
- 📊 **Monitoring**: Real-time temperature and fan speed monitoring
- 📈 **Status**: Quick system overview

## Requirements

### Kernel Modules

```bash
# EC access (required for fan/performance control)
sudo modprobe ec_sys write_support=1

# HP WMI (required for keyboard lighting)
sudo modprobe hp-wmi
```

### Verification

```bash
# Check EC access
ls -la /sys/kernel/debug/ec/ec0/io

# Check HP WMI
ls /sys/devices/platform/hp-wmi/
ls /sys/class/leds/hp::kbd_backlight/
```

## Installation

### From Release

```bash
# Download latest release
wget https://github.com/Jeyloh/omencore/releases/latest/download/omencore-cli-linux-x64.tar.gz

# Extract
tar -xzf omencore-cli-linux-x64.tar.gz

# Install
sudo cp omencore-cli /usr/local/bin/
sudo chmod +x /usr/local/bin/omencore-cli
```

### From Source

```bash
# Clone repository
git clone https://github.com/Jeyloh/omencore.git
cd omencore

# Build
cd src/OmenCore.Linux
dotnet publish -c Release -r linux-x64 --self-contained

# Install
sudo cp bin/Release/net8.0/linux-x64/publish/omencore-cli /usr/local/bin/
```

## Usage

### Fan Control

```bash
# Show current fan status
sudo omencore-cli fan

# Set fan profile
sudo omencore-cli fan --profile auto
sudo omencore-cli fan --profile silent
sudo omencore-cli fan --profile balanced
sudo omencore-cli fan --profile gaming
sudo omencore-cli fan --profile max

# Set manual fan speed (%)
sudo omencore-cli fan --speed 80

# Set individual fan speeds (RPM)
sudo omencore-cli fan --fan1 3500 --fan2 4000

# Enable/disable fan boost
sudo omencore-cli fan --boost
sudo omencore-cli fan --boost false
```

### Performance Mode

```bash
# Show current mode
sudo omencore-cli perf

# Set performance mode
sudo omencore-cli perf --mode default
sudo omencore-cli perf --mode balanced
sudo omencore-cli perf --mode performance
sudo omencore-cli perf --mode cool
```

### Keyboard Lighting

```bash
# Show keyboard status
omencore-cli keyboard

# Set all zones to red
omencore-cli keyboard --color FF0000

# Set specific zone (0-3)
omencore-cli keyboard --zone 0 --color 00FF00

# Set brightness
omencore-cli keyboard --brightness 80

# Turn off
omencore-cli keyboard --off
```

### System Status

```bash
# Show full system status
sudo omencore-cli status
```

### Real-time Monitor

```bash
# Start real-time monitoring (Ctrl+C to exit)
sudo omencore-cli monitor

# Custom refresh interval (500ms)
sudo omencore-cli monitor --interval 500
```

## Systemd Service (Daemon Mode)

Create `/etc/systemd/system/omencore.service`:

```ini
[Unit]
Description=OmenCore Fan Control Daemon
After=multi-user.target

[Service]
Type=simple
ExecStart=/usr/local/bin/omencore-cli fan --profile auto
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable omencore
sudo systemctl start omencore
```

## EC Register Map

Based on [omen-fan](https://github.com/alou-S/omen-fan) documentation:

| Register | Description | Values |
|----------|-------------|--------|
| 0x34 | Fan 1 Speed (100 RPM units) | 0-55 |
| 0x35 | Fan 2 Speed (100 RPM units) | 0-55 |
| 0x2E | Fan 1 Speed % | 0-100 |
| 0x2F | Fan 2 Speed % | 0-100 |
| 0xEC | Fan Boost | 0x00=OFF, 0x0C=ON |
| 0xF4 | Fan State | 0x00=BIOS, 0x02=Manual |
| 0x57 | CPU Temperature | °C |
| 0xB7 | GPU Temperature | °C |
| 0x95 | Performance Mode | 0x30=Default, 0x31=Perf, 0x50=Cool |
| 0xBA | Thermal Power | 0-5 |

## Troubleshooting

### Requesting board support (new model / board ID)

If your model is not fully supported yet (for example OMEN 16-ap0xxx, board 8E35),
open a GitHub issue and attach the diagnostics below.

Required:

```bash
# 1) OmenCore diagnostic bundle
sudo omencore-cli diagnose > omencore-diagnose.txt

# 2) DMI / board info
sudo dmidecode -t system -t baseboard > dmi-system-baseboard.txt

# 3) hp-wmi and thermal nodes
ls -R /sys/devices/platform/hp-wmi > hp-wmi-tree.txt
ls -R /sys/class/hwmon > hwmon-tree.txt

# 4) Loaded modules
lsmod | grep -E "hp_wmi|ec_sys|wmi" > module-state.txt
```

Optional but useful for deeper support:

```bash
# ACPI tables (optional)
sudo acpidump > acpidump.txt
```

Please include:
- Exact laptop model string (`sudo dmidecode -s system-product-name`)
- Board ID (from dmidecode/baseboard)
- Kernel version (`uname -a`)
- Distro + version
- Which commands/features fail and their exact error text

### "Cannot access EC"

```bash
# Verify module is loaded
lsmod | grep ec_sys

# Load with write support
sudo modprobe ec_sys write_support=1

# Make persistent (add to /etc/modules or modprobe.d)
echo "ec_sys write_support=1" | sudo tee /etc/modprobe.d/ec_sys.conf
```

### "Root privileges required"

Most operations require root access:

```bash
sudo omencore-cli fan --profile gaming
```

### Keyboard lighting not working

```bash
# Load HP WMI module
sudo modprobe hp-wmi

# Check available interfaces
ls /sys/class/leds/
ls /sys/devices/platform/hp-wmi/
```

## Contributing

See the main [OmenCore repository](https://github.com/Jeyloh/omencore) for contribution guidelines.

## License

MIT License - see [LICENSE](../../LICENSE) for details.

## Credits

- [hp-omen-linux-module](https://github.com/pelrun/hp-omen-linux-module) - Linux kernel module for HP Omen WMI
- [omen-fan](https://github.com/alou-S/omen-fan) - EC register documentation
