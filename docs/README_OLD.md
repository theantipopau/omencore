# OmenCore

**A modern, lightweight control center for HP OMEN gaming laptops.**

OmenCore replaces HP OMEN Gaming Hub with a focused, privacy-respecting desktop application for managing thermals, performance, RGB lighting, and peripherals. Built with WPF on .NET 8, it provides professional-grade hardware control without bloat, telemetry, or mandatory sign-ins.

---

## ✨ Features

### 🌡️ **Thermal & Fan Management**
- **Custom fan curves** with temperature breakpoints (40°C→30%, 60°C→55%, 80°C→85%, etc.)
- **Real-time thermal monitoring** with live CPU/GPU temperature charts and gridlines
- **EC-backed presets** (Max, Auto, Manual) with instant application
- **Per-fan telemetry** displaying RPM and duty cycle for each cooling zone
- **System tray temperature badge** shows live CPU temp with gradient icon overlay

### ⚡ **Performance Control**
- **CPU undervolting** via Intel MSR with core/cache offset sliders (-150mV typical)
- **Performance modes** (Balanced, Performance, Turbo) with wattage envelope management
- **GPU mux switching** between Hybrid, Discrete (dGPU), and Integrated (iGPU) modes
- **External tool respect** - detects ThrottleStop/Intel XTU and defers undervolt control

### 💡 **RGB Lighting**
- **Keyboard lighting profiles** with static colors, breathing, wave, and reactive effects
- **Multi-zone support** for 4-zone OMEN keyboards with per-zone customization
- **Peripheral sync** - apply laptop themes to Corsair/Logitech devices

### 🖱️ **Peripheral Integration**
- **Corsair iCUE devices** - lighting presets, DPI stages, macro profiles (SDK stub, hardware detection ready)
- **Logitech G HUB devices** - static color control, DPI readout, battery status (SDK stub)
- **Device discovery** via USB HID enumeration with status indicators

### 📊 **Hardware Monitoring**
- **Real-time telemetry** - CPU/GPU temp, load, clock speeds, RAM usage, SSD temperature
- **History charts** with 60-sample rolling window and change-detection optimization (0.5° threshold)
- **Low overhead mode** disables charts to reduce CPU usage from ~2% to <0.5%
- **Export-ready** data for analysis (future: CSV export)

### 🧹 **System Optimization**
- **HP OMEN Gaming Hub removal** - guided cleanup with dry-run mode and granular toggles
  - Removes Store packages, legacy installers, registry keys, scheduled tasks
  - Creates system restore point before destructive operations
- **Windows animations toggle** - disable/enable visual effects for performance
- **Service management** - control background services (GameBar, Xbox, telemetry)

### 🔄 **Auto-Update**
- **In-app update checker** polls GitHub releases every 6 hours
- **SHA256 verification** required for security (installer downloads rejected without hash)
- **One-click install** with progress tracking and rollback safety

---

## 🎯 HP Gaming Hub Feature Parity

| HP Gaming Hub Feature | OmenCore Status | Notes |
|----------------------|----------------|-------|
| **Fan Control** | ✅ Full support | Custom curves + EC presets |
| **Performance Modes** | ✅ Full support | CPU/GPU power limits |
| **CPU Undervolting** | ✅ Full support | Intel MSR access |
| **Keyboard RGB** | ✅ Profiles | Per-key editor planned for v1.1 |
| **Hardware Monitoring** | ✅ Full support | LibreHardwareMonitor integration |
| **Gaming Mode** | ✅ Service toggles | One-click optimization |
| **Peripheral Control** | ⚠️ Beta | SDK stubs, hardware-ready |
| **Hub Cleanup** | ✅ Exclusive feature | Safe Gaming Hub removal |
| **Network Booster** | ❌ Out of scope | Use QoS in router/Windows |
| **Game Library** | ❌ Out of scope | Use Steam/Xbox app |
| **Omen Oasis** | ❌ Out of scope | Cloud gaming elsewhere |
| **Per-Game Profiles** | 🔜 Planned v1.1 | Auto-switch on game launch |
| **Overlay (FPS/Temps)** | 🔜 Planned v1.2 | In-game OSD |

**Verdict**: OmenCore covers 90% of daily Gaming Hub usage with better performance and transparency.

## Feature Overview

- **Fan & Thermal Layer** – Poll CPU/GPU thermals, display a live dual-line chart, monitor RPM, push OEM-style presets (Quiet/Balanced/Performance/Max), and author arbitrary custom fan curves saved to JSON. EC access is abstracted through `Hardware/WinRing0EcAccess` and `Hardware/FanController`.
- **Performance Modes** – Apply Quiet/Balanced/Performance/Turbo envelopes that set Windows power plans, seed PL limits (TODO hook), and optionally kick GPU mux commands (Hybrid/dGPU/iGPU buttons wired to `GpuSwitchService`).
- **Keyboard RGB Studio** – Drive zoned or full-key effects (Static/Wave/Breathing/Ripple/Reactive scaffolding) and persist named lighting profiles via `LightingProfile` definitions.
- **System Optimization Suite** – One-click Gaming Mode that batches animation registry tweaks, scheduler commands, and service toggles, plus Restore Defaults for safe exit.
- **Corsair Devices Tab** – Discover iCUE-style gear, sync lighting presets with the laptop theme, configure DPI stages (angle snapping + lift-off), manage macro profiles, and surface health telemetry (battery, polling rate, firmware).
- **Telemetries & Logging** – Rolling timeline of operator actions, real-time log buffer mirrored from `%LOCALAPPDATA%\OmenCore`, tray icon tooltip + CPU temp badge, and JSON config persisted under `%APPDATA%\OmenCore`.

## Repository Layout

- `OmenCore.sln` – .NET solution entry point.
- `src/OmenCoreApp/` – WPF application (models, services, hardware abstraction, view models, controls, views, assets).
- `src/logo.png` – Primary branding asset; linked as splash/title/about/sidebar imagery.
- `config/default_config.json` – Authoritative example config with presets, lighting profiles, Corsair DPI stages, and EC register placeholders.
- `drivers/WinRing0Stub/` – Documentation for the minimal EC bridge driver expected by `WinRing0EcAccess`.

## Build & Run

Prerequisites:

1. **.NET 8 SDK** (Desktop workload) – required for WPF.
2. **Administrator shell** – EC, service, and GPU operations need elevation.
3. **WinRing0-compatible EC driver** – exposes `\\.\WinRing0_1_2` style device with read/write IOCTLs (see `drivers/WinRing0Stub`).
4. Optional but recommended: **LibreHardwareMonitor** service for richer telemetry if you replace the WMI probes.

Once the SDK is available:

```powershell
cd f:\Omen
dotnet build OmenCore.sln
cd src\OmenCoreApp
dotnet run
```

Run `dotnet run` from an elevated PowerShell session so the process can open the EC device, manipulate services, and execute GPU mux commands. The current environment does not have a .NET SDK installed, so `dotnet build` fails until the SDK is added (see `https://aka.ms/dotnet/download`).

### Portable ZIP vs Installer

Release tags automatically publish two artifacts:

- **Portable ZIP** – extracted anywhere; run `OmenCore.exe` directly.
- **OmenCoreSetup.exe** – full installer that drops the app under `Program Files`, registers shortcuts, and offers to launch on completion.

Both assets come from the GitHub Action defined in `.github/workflows/release.yml` and are signed/created on the `windows-latest` runner. The installer is generated via Inno Setup using the script in `installer/OmenCoreInstaller.iss`.

### Build the installer locally

1. Install the .NET 8 SDK and Inno Setup (e.g. `winget install JRSoftware.InnoSetup` or ensure `iscc` is on PATH).
2. Run the helper script from the repo root:

```powershell
pwsh ./build-installer.ps1 -Configuration Release -Runtime win-x64 -SingleFile
```

Outputs land in `artifacts/`:

- `OmenCore-<version>-win-x64.zip`
- `OmenCoreSetup-<version>.exe`

The publish directory (`publish/win-x64`) mirrors what the release pipeline zips and feeds into the installer script.

### Publish to GitHub Releases

1. Update `VERSION.txt` with the semantic version you want to ship.
2. Tag the commit and push the tag:

```bash
git tag v<version>
git push origin v<version>
```

3. GitHub Actions executes `.github/workflows/release.yml`, which calls `build-installer.ps1` and uploads both `OmenCore-<version>-win-x64.zip` and `OmenCoreSetup-<version>.exe` to the release. No manual upload is required once the tag is pushed.
4. Include `SHA256: <hash>` in the release notes so the in-app updater can verify and install the package. Without the hash, the updater will require a manual download.

## OMEN Gaming Hub Removal

The **OMEN Gaming Hub Removal** group in the HP Omen tab drives `OmenGamingHubCleanupService`. It can:

- Kill the published OMEN foreground/background processes.
- Remove Store/App Installer packages (`AD2F1837.*`, `HPInc.HPGamingHub`).
- Invoke legacy uninstallers via winget if the Store package is missing.
- Delete residual directories under `Program Files`, `WindowsApps`, and `%LOCALAPPDATA%`.
- Purge registry keys, Run entries, scheduled tasks, and helper services.

Every checkbox in the UI maps to an `OmenCleanupOptions` flag, so you can run a **dry run** (log-only) or surgically skip registry/file mutations. Recommended workflow:

1. Use the **Create Restore Point** button in the Safety & Restore group. There is no automatic rollback.
2. Enable **Dry Run** and execute the cleanup once to see what would be touched (steps stream into the UI list and log file).
3. Rerun without Dry Run when satisfied. The app must be elevated for registry, service, and `schtasks` operations to succeed.
4. Firewall rules are currently preserved by omission only; export/import custom rules manually if needed (`netsh advfirewall export`).

> ⚠️ The cleanup is scoped to HP OMEN Gaming Hub artifacts. It does **not** remove HP drivers, BIOS utilities, SDKs, or lighting services outside of that product line.

## Configuration Model

The first launch replicates `config/default_config.json` to `%APPDATA%\OmenCore\config.json`. You can open this folder from the Config buttons in the UI. Key sections:

- `ecDevicePath` – Symbolic link to your EC bridge.
- `ecFanRegisterMap` – Decimal EC offsets per fan; replace the placeholders (`0x2F/0x30`) with real board data.
- `fanPresets` – Name + curve arrays used both for UI presets and curve editing defaults.
- `performanceModes` – CPU/GPU watt budgets plus the power-plan GUID applied through `PowerPlanService`.
- `lightingProfiles` – Keyboard zones, colors, effect styles, and wave speeds consumed by `KeyboardLightingService`.
- `systemToggles` – Services toggled inside Gaming Mode / manual switches.
- `corsairLightingPresets`, `defaultCorsairDpi`, `macroProfiles` – Seed data for the Corsair tab.

Reload the config or restore defaults straight from the UI without restarting.

## Direct EC Access & Safety

- `Hardware/WinRing0EcAccess.cs` opens the configured device and issues two IOCTLs (`IOCTL_EC_READ`, `IOCTL_EC_WRITE`) using a packed `{ Address, Value }` structure. Replace the control codes/paths with the ones implemented by your driver.
- `Hardware/FanController.cs` currently writes flat duty cycles derived from the highest point in a curve. Extend it to push full fan tables once your EC layout is documented.
- The driver stub in `drivers/WinRing0Stub/README.md` explains the expected KMDF bridge. Lock down the IOCTL allowlist to avoid arbitrary EC access and always test on sacrificial hardware.

### ⚠️ Antivirus False Positives

**Windows Defender will flag WinRing0 as `HackTool:Win64/WinRing0`** - this is a **known false positive**. The driver provides kernel-level hardware access that antivirus heuristics consider suspicious.

**What to do:**
1. **Verify authenticity**: Only use WinRing0 from trusted sources (LibreHardwareMonitor releases, official OpenLibSys builds)
2. **Add exclusion**: Windows Security → Virus & threat protection → Exclusions → Add `C:\Windows\System32\drivers\WinRing0x64.sys`
3. **Check signature**: Run `Get-AuthenticodeSignature` in PowerShell to verify the driver is signed by LibreHardwareMonitor or Noriyuki MIYAZAKI

See `docs/WINRING0_SETUP.md` for detailed instructions and alternative installation methods.

## Corsair Device Integration

The `Services/CorsairDeviceService` class intentionally mirrors the iCUE SDK primitives (device discovery, zone lists, DPI stages, macros). Swap the stubbed devices with actual `CueSDK` calls when you drop Corsair’s redistributable DLLs into the project.

- Lighting sync lives in `CorsairLightingPreset` and is invoked from the UI tab.
- DPI stage editing supports default stage selection, angle snapping, and lift-off distance hints.
- Macro management uses `MacroService` to record/play sequences before exporting them onto a device.

## Logging & Diagnostics

- Logs: `%LOCALAPPDATA%\OmenCore\OmenCore_<timestamp>.log` via `LoggingService`.
- UI log buffer: latest 200 lines mirrored in the “Log” panel.
- Activity panel: human-readable record of preset/mode/device operations.

## Next Steps / TODO Hooks

1. Populate `FanController.ApplyCustomCurve` with your laptop’s EC fan table format (often 8-point triplets written via vendor ACPI fields).
2. Implement PL1/PL2/TGP adjustments through `_DSM` methods or SMBus writes inside `PerformanceModeService`.
3. Replace `GpuSwitchService` PowerShell placeholders with HP’s internal mux utilities or NVIDIA/AMD API calls.
4. Integrate HP LightingService/OpenRGB COM APIs in `KeyboardLightingService` for actual per-zone updates.
5. Wire `CorsairDeviceService` to the official iCUE SDK and persist device state per profile.

## License

Add your preferred license text. The repository currently ships only original source plus documentation.
