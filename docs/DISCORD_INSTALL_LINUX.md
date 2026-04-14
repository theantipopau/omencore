**OmenCore — Linux Install Guide**

📥 **Download**
https://github.com/theantipopau/omencore/releases/latest
File: `OmenCore-*-linux-x64.zip` — self-contained, no .NET install needed.

⚡ **Quick install (CLI)**
```bash
# Get the latest release URL
VERSION=$(curl -s https://api.github.com/repos/theantipopau/omencore/releases/latest | grep -oP '"tag_name": "\K[^"]*')
wget https://github.com/theantipopau/omencore/releases/download/${VERSION}/OmenCore-${VERSION}-linux-x64.zip
mkdir -p OmenCore && unzip OmenCore-*-linux-x64.zip -d OmenCore && cd OmenCore
chmod +x omencore-cli omencore-gui
sudo ./omencore-cli status
```

🖥️ **GUI**
```bash
./omencore-gui
```
> Prefer launching *without* sudo. If you must elevate: `sudo --preserve-env=DISPLAY,XAUTHORITY,XDG_RUNTIME_DIR,DBUS_SESSION_BUS_ADDRESS ./omencore-gui`
> Force software rendering if GLX fails: `OMENCORE_GUI_RENDER_MODE=software ./omencore-gui`
> **v3.3.0:** OmenCore now auto-retries in software mode on GPU initialisation failure and persists the choice in `render-startup-state.json` — subsequent launches skip the GPU attempt automatically.

🐧 **Kernel modules**
| Model generation | Module | Command |
|---|---|---|
| 2023+ (13th Gen+) | `hp-wmi` | `sudo modprobe hp-wmi` |
| Pre-2023 | `ec_sys` | `sudo modprobe ec_sys write_support=1` |
| 2025+ OMEN Max | `hp-wmi` hwmon | `sudo modprobe hp-wmi` |

Kernel **6.18+** recommended for 2023+ models (best sysfs fan control).

Make persistent: `echo "hp-wmi" | sudo tee /etc/modules-load.d/omencore.conf`

📋 **Common CLI commands**
```bash
sudo omencore-cli fan --profile auto   # BIOS auto
sudo omencore-cli fan --profile max    # max cooling
sudo omencore-cli fan --speed 70       # manual %
sudo omencore-cli battery threshold 80 # charge limit
```

� **Full Linux guide:** https://github.com/theantipopau/omencore/blob/main/docs/LINUX_INSTALL_GUIDE.md

🐛 **Issues with fan control or device detection?**
```bash
sudo ./omencore-cli diagnose --report > report.txt
```
Post `report.txt` in <#linux-support> or open an issue:
https://github.com/theantipopau/omencore/issues
