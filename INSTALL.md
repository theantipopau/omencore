# OmenCore Installation Guide

Complete installation, upgrade, portable, Linux, and uninstall instructions for OmenCore 3.9.0.

## Choose Your Package

| Package | Platform | Use This When |
|---|---|---|
| `OmenCoreSetup-3.9.0.exe` | Windows | You want the normal install flow, Start Menu entry, and optional PawnIO install. |
| `OmenCore-3.9.0-win-x64.zip` | Windows | You want a portable copy, test build, or no installer changes. |
| `OmenCore-3.9.0-linux-x64.zip` | Linux | You want the CLI and Avalonia GUI bundle. |

Always download from the [latest GitHub Release](https://github.com/theantipopau/omencore/releases/latest), then compare the SHA256 hash against the release notes before running binaries.

## Windows Installer

### Requirements

- Windows 10 build 19041+ or Windows 11.
- Administrator rights for hardware-control actions.
- No separate .NET install required for release artifacts.
- PawnIO recommended for EC/MSR features and Secure Boot-compatible low-level access.

### Install

1. Download `OmenCoreSetup-3.9.0.exe`.
2. Verify the SHA256 hash from the GitHub Release notes.
3. Right-click the installer and choose **Run as administrator**.
4. Keep **Install PawnIO Driver** selected unless you only need WMI-only features and monitoring.
5. Choose Start Menu/Desktop/startup options.
6. Launch OmenCore from the Start Menu.

If Windows SmartScreen shows an unknown-publisher prompt, choose **More info** and then **Run anyway** only if the SHA256 matches the release notes. See [docs/ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md) if antivirus blocks hardware-driver components.

### First Launch

1. Run OmenCore as Administrator.
2. Open Diagnostics or About and confirm your model identity summary.
3. Check the Fan Control page for the capability badge:
   - `Curves` means custom curves/manual fan levels are available.
   - `Profile-only` means OmenCore will use OEM firmware fan profiles and block unsupported curve writes.
4. Use **Restore OEM Auto** if fans sound stuck after experimenting with Max/custom modes.

### Important Startup Note

Startup hardware restore is disabled by default. Leave it disabled unless you have validated the behavior on your exact model and understand the recovery steps. Some OMEN/Victus firmware has reacted poorly to automatic hardware writes immediately after boot.

## Windows Portable ZIP

### Install

1. Download `OmenCore-3.9.0-win-x64.zip`.
2. Verify the SHA256 hash from the GitHub Release notes.
3. Extract to a normal writable folder, for example `C:\Tools\OmenCore`.
4. Right-click `OmenCore.exe` and choose **Run as administrator**.

Avoid extracting into `Program Files` unless you want to manage folder permissions manually.

### PawnIO With Portable Builds

Portable packages do not run the installer flow. If PawnIO is not already installed, install it through the normal OmenCore installer or the bundled driver package used by the release. Reboot after installing PawnIO if diagnostics say the driver is installed but not runtime-ready.

### Portable Data Locations

- Config: `%APPDATA%\OmenCore\config.json`
- Logs: `%LOCALAPPDATA%\OmenCore\OmenCore_<timestamp>.log`
- Diagnostics exports: selected by the user at export time.

Deleting the extracted portable folder does not remove config, logs, or PawnIO.

## Windows Upgrade

1. Exit OmenCore from the tray menu.
2. Install `OmenCoreSetup-3.9.0.exe` over the previous version.
3. Keep PawnIO selected if you use EC/MSR features.
4. Launch as Administrator.
5. Open Diagnostics and confirm:
   - App version is `3.9.0`.
   - Model identity uses exact ProductId where available.
   - Fan/RGB/performance capabilities match your hardware.

If upgrading from a much older version, export or back up `%APPDATA%\OmenCore\config.json` first.

## Linux Package

### Requirements

- x64 Linux.
- Root privileges for hardware writes.
- A desktop session for the Avalonia GUI.
- One of the following hardware paths, depending on model:
  - `hp-wmi` platform profile.
  - `hp-wmi`/hwmon PWM and fan input files.
  - `ec_sys` with write support for older models.

### Quick Start: CLI

```bash
VERSION=3.9.0
wget "https://github.com/theantipopau/omencore/releases/download/v${VERSION}/OmenCore-${VERSION}-linux-x64.zip"
mkdir -p OmenCore-linux-x64
unzip "OmenCore-${VERSION}-linux-x64.zip" -d OmenCore-linux-x64
cd OmenCore-linux-x64
chmod +x omencore-cli omencore-gui

sudo ./omencore-cli status
sudo ./omencore-cli diagnose --report > omencore-report.txt
```

### Quick Start: GUI

```bash
cd OmenCore-linux-x64
./omencore-gui
```

Prefer the normal user session for GUI launch. If your environment requires root GUI launch, preserve the desktop/session variables:

```bash
sudo --preserve-env=DISPLAY,XAUTHORITY,XDG_RUNTIME_DIR,DBUS_SESSION_BUS_ADDRESS ./omencore-gui
```

If the renderer fails, force software rendering:

```bash
OMENCORE_GUI_RENDER_MODE=software ./omencore-gui
```

### Optional CLI Install To PATH

```bash
sudo cp omencore-cli /usr/local/bin/
sudo chmod +x /usr/local/bin/omencore-cli
sudo omencore-cli status
```

Install `omencore-gui` similarly only if you want a system-wide GUI binary.

## Linux Hardware Paths

### 2023+ OMEN Models

Most 2023+ models should try `hp-wmi` first:

```bash
sudo modprobe hp-wmi
ls /sys/devices/platform/hp-wmi 2>/dev/null
find /sys/devices/platform -iname '*profile*' -o -iname 'pwm*' -o -iname 'fan*_input'
```

Some systems expose only profile switching. Others expose PWM/fan input via hwmon.

### Older Models With ec_sys

```bash
sudo modprobe ec_sys write_support=1
ls /sys/kernel/debug/ec/ec0/io
```

To load at boot:

```bash
echo "ec_sys" | sudo tee /etc/modules-load.d/ec_sys.conf
echo "options ec_sys write_support=1" | sudo tee /etc/modprobe.d/ec_sys.conf
```

### hp-omen-gaming-wmi-dkms Compatible Paths

Some Arch/CachyOS users test `hp-omen-gaming-wmi-dkms`. OmenCore does not install or manage DKMS modules. If the module exposes standard `hp-wmi`/hwmon files such as `pwm1_enable`, `pwm1`, and `fan1_input`, OmenCore can label and use it as an `hp-omen-gaming-wmi-dkms compatible` backend.

Install, remove, rebuild, and Secure Boot-sign DKMS packages through your distro tools.

## Linux CLI Examples

```bash
# Status and diagnostics
sudo ./omencore-cli status
sudo ./omencore-cli status --json
sudo ./omencore-cli diagnose --report > omencore-report.txt

# Fan control
sudo ./omencore-cli fan --profile auto
sudo ./omencore-cli fan --profile max
sudo ./omencore-cli fan --speed 80

# Keyboard lighting
sudo ./omencore-cli keyboard --color FF0000 --brightness 100

# Battery
sudo ./omencore-cli battery status
sudo ./omencore-cli battery threshold 80

# Monitoring
sudo ./omencore-cli monitor --interval 1000
```

Unsupported commands should report a capability reason instead of silently pretending to work.

## Linux Daemon

```bash
sudo ./omencore-cli daemon --install
sudo systemctl enable omencore
sudo systemctl start omencore
sudo systemctl status omencore
journalctl -u omencore -f
```

Configuration locations:

- System daemon config: `/etc/omencore/config.toml`
- User config: `~/.config/omencore/config.toml`

Generate a starter config:

```bash
mkdir -p ~/.config/omencore
./omencore-cli daemon --generate-config > ~/.config/omencore/config.toml
```

## Diagnostics And Bug Reports

### Windows

1. Reproduce the issue.
2. Open Diagnostics.
3. Export diagnostics.
4. Include the model identity summary and the newest `%LOCALAPPDATA%\OmenCore\OmenCore_<timestamp>.log`.

For fan reports, include what you clicked before the issue, whether **Restore OEM Auto** helped, and whether RPM/level readback changed.

If Diagnostics shows `Unknown <Family> Model` or "Resolution source: Family fallback", your model is not yet in the capability database â€” see [README.md: Requesting Support For An Unrecognized Model](README.md#requesting-support-for-an-unrecognized-model) for exactly what to capture before filing the issue.

### Linux

From a source checkout:

```bash
./qa/collect-linux-triage.sh
```

From a release package:

```bash
sudo ./omencore-cli diagnose --report > omencore-report.txt
uname -a > kernel.txt
dmesg | grep -i -E "hp|omen|wmi|ec" > dmesg-omen.txt
```

Attach those files to the issue.

## Troubleshooting

### Windows: Fan Control Has No Effect

- Confirm the app is running as Administrator.
- Check Fan Control capability badge.
- Export diagnostics and inspect fan command history.
- If on a profile-only model, use OEM profiles instead of custom curves.
- Use **Restore OEM Auto** before retesting.

### Windows: Fans Stay High After Load

1. Press **Restore OEM Auto**.
2. Wait 20-30 seconds.
3. Export diagnostics.
4. Include temperature, RPM, active fan mode, active performance profile, and the model identity summary.

WMI V1 boards can hold stale manual floors. v3.7.1 enables floor-clear overrides only where field evidence supports it.

### Windows: PawnIO Installed But Not Ready

- Reboot Windows after installing PawnIO.
- Run OmenCore as Administrator.
- Check Diagnostics for PawnIO EC/MSR status.
- Secure Boot is supported with PawnIO; older WinRing0 warnings usually indicate leftovers or another utility.

### Windows: GPU Power Boost Does Not Reach Expected Wattage

- Confirm GPU Power Boost is available in System Control.
- Apply Minimum, Medium, and Maximum and check diagnostics/readback.
- Compare against a consistent workload.
- Some firmware accepts WMI boost states but still clamps effective wattage.
- Extended boost is hardware-specific and should not be assumed on RTX 4050/4060-class systems.

### Windows: RGB Turns Off Or Does Not Restore

- Close OMEN Light Studio and OMEN Gaming Hub lighting services if they are taking ownership.
- Use the Lighting page restore/apply action.
- Export diagnostics and include `rgb-control-path.txt`.
- OMEN Max per-key hardware detection does not mean the dedicated per-key editor is enabled yet.

### Windows: Battery Care (Charge Limit) Fails To Enable

- Confirm the laptop is on AC power; some HP firmware rejects the battery-care WMI command on battery.
- Toggle the equivalent setting in OMEN Gaming Hub to confirm the feature works at the firmware level, then export diagnostics including `wmi-command-history.txt` and your BIOS version before filing an issue â€” this is a firmware/WMI command question, not something OmenCore can guess a fix for without that evidence.
- This is independent of model identity: the toggle is always shown and always attempted regardless of whether your model has an exact capability profile.

### Windows: Performance Profile Reverts To Balanced After Relaunch

- Fixed in 3.8.1 for the tray menu, the `Ctrl+Shift+E` hotkey cycle, and the General page's quick-profile buttons (GitHub #145) â€” these previously applied the mode to hardware but never saved it as the startup preference.
- If you still see this after upgrading, export diagnostics and include whether you changed modes from the System Control/Tuning page specifically (a different, already-persisting path) versus the tray/hotkey/General entry points.

### Windows: OSD Does Not Appear In Fullscreen Game

- Try borderless fullscreen.
- Confirm OSD is enabled and hotkey is registered.
- Try RTSS for FPS/overlay interoperability.
- Some exclusive fullscreen or anti-cheat paths can block normal WPF topmost overlays.

### Linux: Permission Denied

Use `sudo` for CLI hardware commands:

```bash
sudo ./omencore-cli status
```

Launch the GUI as a normal user unless you specifically need root GUI access.

### Linux: GUI Does Not Start

```bash
ldd ./omencore-gui | grep "not found"
DISPLAY=:0 ./omencore-gui
OMENCORE_GUI_RENDER_MODE=software ./omencore-gui
```

If elevated launch is unavoidable:

```bash
sudo --preserve-env=DISPLAY,XAUTHORITY,XDG_RUNTIME_DIR,DBUS_SESSION_BUS_ADDRESS,OMENCORE_GUI_RENDER_MODE ./omencore-gui
```

### Linux: ec_sys Missing

Some kernels do not ship `ec_sys`. On newer OMEN systems, check `hp-wmi` first:

```bash
sudo modprobe hp-wmi
find /sys/devices/platform -iname '*profile*' -o -iname 'pwm*' -o -iname 'fan*_input'
```

If no control files exist, collect diagnostics and include kernel/distro/model details.

## Uninstall

### Windows Installer

1. Exit OmenCore from the tray.
2. Open **Settings -> Apps -> Installed apps**.
3. Uninstall OmenCore.
4. Optional: remove PawnIO from Device Manager or its uninstaller if you no longer need it.
5. Optional: delete user data:
   - `%APPDATA%\OmenCore\`
   - `%LOCALAPPDATA%\OmenCore\`

### Windows Portable

1. Exit OmenCore.
2. Delete the extracted folder.
3. Optional: delete `%APPDATA%\OmenCore\` and `%LOCALAPPDATA%\OmenCore\`.
4. Optional: uninstall PawnIO separately if installed.

### Linux

```bash
sudo systemctl stop omencore 2>/dev/null || true
sudo systemctl disable omencore 2>/dev/null || true
sudo rm -f /usr/local/bin/omencore-cli
sudo rm -f /usr/local/bin/omencore-gui
sudo rm -rf /etc/omencore
rm -rf ~/.config/omencore
rm -rf ~/OmenCore-linux-x64
```

If you enabled `ec_sys` at boot:

```bash
sudo rm -f /etc/modules-load.d/ec_sys.conf
sudo rm -f /etc/modprobe.d/ec_sys.conf
```

## Release Operator Notes

For maintainers preparing 3.9.0:

```powershell
dotnet restore OmenCore.sln
dotnet build OmenCore.sln -c Release --no-restore
dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj -c Release --no-build
git diff --check
pwsh ./build-installer.ps1
pwsh ./build-linux-package.ps1
```

After building:

```powershell
Get-FileHash artifacts\OmenCoreSetup-3.9.0.exe -Algorithm SHA256
Get-FileHash artifacts\OmenCore-3.9.0-win-x64.zip -Algorithm SHA256
Get-FileHash artifacts\OmenCore-3.9.0-linux-x64.zip -Algorithm SHA256
```

`artifacts\SHA256SUMS-3.9.0.txt` can be uploaded alongside the artifacts. Publish those hashes in the GitHub Release notes. Do not publish an in-app update without SHA256 hashes.

## Additional Resources

- [README.md](README.md)
- [docs/CHANGELOG_v3.9.0.md](docs/CHANGELOG_v3.9.0.md)
- [docs/CHANGELOG_v3.8.1.md](docs/CHANGELOG_v3.8.1.md)
- [docs/FINAL_RELEASE_CHECKLIST.md](docs/FINAL_RELEASE_CHECKLIST.md)
- [docs/LINUX_INSTALL_GUIDE.md](docs/LINUX_INSTALL_GUIDE.md)
- [docs/ANTIVIRUS_FAQ.md](docs/ANTIVIRUS_FAQ.md)
- [docs/DEFENDER_FALSE_POSITIVE.md](docs/DEFENDER_FALSE_POSITIVE.md)
- [drivers/PawnIO/README.md](drivers/PawnIO/README.md)
- [Discord](https://discord.gg/9WhJdabGk8)
- [GitHub Issues](https://github.com/theantipopau/omencore/issues)
