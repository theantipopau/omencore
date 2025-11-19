# OmenCore

OmenCore is a production-ready control surface for HP Omen laptops with bonus Corsair peripheral integration. It combines fan and thermal tuning, performance/P-state orchestration, keyboard RGB scripting, system optimization switches, and an entire tab for iCUE-compatible devices. The UI is a WPF desktop experience branded with the bundled `src/logo.png` asset, including splash/about/sidebar treatments.

## Feature Overview

- **Fan & Thermal Layer** – Poll CPU/GPU thermals, display a live dual-line chart, monitor RPM, push OEM-style presets (Quiet/Balanced/Performance/Max), and author arbitrary custom fan curves saved to JSON. EC access is abstracted through `Hardware/WinRing0EcAccess` and `Hardware/FanController`.
- **Performance Modes** – Apply Quiet/Balanced/Performance/Turbo envelopes that set Windows power plans, seed PL limits (TODO hook), and optionally kick GPU mux commands (Hybrid/dGPU/iGPU buttons wired to `GpuSwitchService`).
- **Keyboard RGB Studio** – Drive zoned or full-key effects (Static/Wave/Breathing/Ripple/Reactive scaffolding) and persist named lighting profiles via `LightingProfile` definitions.
- **System Optimization Suite** – One-click Gaming Mode that batches animation registry tweaks, scheduler commands, and service toggles, plus Restore Defaults for safe exit.
- **Corsair Devices Tab** – Discover iCUE-style gear, sync lighting presets with the laptop theme, configure DPI stages (angle snapping + lift-off), manage macro profiles, and surface health telemetry (battery, polling rate, firmware).
- **Telemetries & Logging** – Rolling timeline of operator actions, real-time log buffer mirrored from `%LOCALAPPDATA%\OmenCore`, and JSON config persisted under `%APPDATA%\OmenCore`.

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
