# OmenCore v2.0 Roadmap

**Target Release:** Q2 2026  
**Status:** Planning  
**Last Updated:** December 21, 2025

---

## Recent Progress (Dec 19–21, 2025)

A short list of key developments since the last update:

- ✅ **GPU Voltage/Current Graph** implemented and integrated into the Dashboard (real-time V/C monitoring complete).
- ✅ **Per-core undervolt UI** implemented (individual core sliders and config persistence). Hardware application integration and verification are in progress.
- ✅ **Keyboard lighting improvements**: added an "Apply on startup" toggle and fixed automatic keyboard color resets on startup.
- ✅ **TCC offset handling**: reapply saved TCC offsets on startup with exponential backoff retry to handle BIOS/WMI timing issues.
- ✅ **CPU temperature monitoring**: improved sensor selection and smoothing; auto-reinit if temp sensors report zero repeatedly.
- ✅ **Build & tests**: repaired a large code corruption in `SystemControlViewModel.cs`, normalized per-core types, and restored a clean build; all unit tests pass locally.

These items are reflected in the v2.0 changelog and the development tracker (see `V2_DEVELOPMENT.md`).

---

## Overview

Version 2.0 is a **major release** focusing on:
- 🎨 **Complete UI/UX Overhaul** (Priority #1)
- 🌈 **Professional Peripheral RGB Support** (Corsair, Logitech, Razer)
- 🐧 **Linux Support**
- 🚀 **Windows System Optimizer Integration**
- 📺 **On-Screen Display (OSD)** improvements
- ⚡ **CPU/GPU Overclocking** (beyond undervolting)
- 🎮 **Advanced Game Integration**

### Research Sources

- [hp-omen-linux-module](https://github.com/pelrun/hp-omen-linux-module) - Linux kernel module for HP Omen WMI (220 stars)
- [omen-fan](https://github.com/alou-S/omen-fan) - Python-based Linux fan control utility (99 stars)
- [OmenHubLight](https://github.com/determ1ne/OmenHubLight) - Archived C# Omen utility (architecture reference)

---

## 🎨 Priority #1: Complete UI/UX Overhaul

### 1. Visual Design Refresh

**Target:** v2.0.0-alpha  
**Effort:** High  
**Impact:** Very High

Complete redesign of the application interface for improved clarity, consistency, and polish.

#### Goals

- **Modern, cohesive dark theme** across all views
- **Improved information hierarchy** - important data stands out
- **Consistent spacing and alignment** throughout
- **Better use of color** for status indicators and branding
- **Smooth animations** for state changes
- **Responsive layouts** for different window sizes

#### System Tray Overhaul

**Current Issues:**
- White left margin on context menu items
- Icons/emojis only appear on hover
- Inconsistent styling with main app

**Fixes:**
- Custom-drawn context menu with full dark theme
- Properly styled icons visible at all times (no white background)
- Smooth hover transitions matching app aesthetic
- Temperature display refinements
- Compact mode option

**Mockup:**
```
┌─────────────────────────────────┐
│ 🌡️ CPU: 65°C  GPU: 72°C        │
├─────────────────────────────────┤
│ 🔥 Performance Mode         ▶  │
│ 🌀 Fan Profile              ▶  │
│ 💡 Keyboard Lighting        ▶  │
├─────────────────────────────────┤
│ ⚙️ Settings                     │
│ 📊 Open Dashboard               │
├─────────────────────────────────┤
│ ❌ Exit                         │
└─────────────────────────────────┘
```

#### Dashboard View Polish

- **Card-based layout** with consistent shadows and borders
- **Live graphs** with smoother rendering
- **Status badges** with clear iconography
- **Quick action buttons** with hover states

#### Settings View Redesign

- **Grouped sections** with collapsible headers
- **Toggle switches** instead of checkboxes
- **Inline help text** for complex options
- **Search/filter** for finding settings

---

### 2. Typography & Iconography

**Custom icon set** for consistent visual language:
- Fan speed indicators (animated)
- Temperature gauges
- Performance mode badges
- RGB zone indicators
- Device connection status

**Typography improvements:**
- Consistent font weights
- Better contrast ratios (WCAG AA)
- Monospace for values (temps, speeds, voltages)

---

### 3. Accessibility Improvements

- Keyboard navigation throughout
- Screen reader compatibility
- High contrast mode option
- Reduced motion option for animations

---

## 🌈 Priority #2: Professional Peripheral RGB Support

### 4. Complete Brand Integration

**Target:** v2.0.0-beta  
**Effort:** High  
**Impact:** Very High

Professional-grade RGB control with proper branding, device images, and full SDK integration.

#### Visual Assets

Each peripheral brand section will include:
- **Official logos** (with permission/licensing)
- **Device images** showing actual hardware
- **Connection status indicators** (USB, wireless, Bluetooth)
- **Battery level** for wireless devices
- **Firmware version** display

#### UI Mockup - Lighting View

```
┌─────────────────────────────────────────────────────────────────────────┐
│  🌈 Lighting Control                                    [Sync All 🔗]   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─── HP OMEN Keyboard ──────────────────────────────────────────────┐ │
│  │  [Image: 4-zone keyboard]                                         │ │
│  │  Zone 1: 🔴  Zone 2: 🟠  Zone 3: 🟡  Zone 4: 🔴    [Apply]       │ │
│  │  Effect: [Static ▼]  Brightness: [████████░░] 80%                 │ │
│  └───────────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  ┌─── CORSAIR ───────────────────────────────────────────────────────┐ │
│  │  [Corsair Logo]                           Status: ✅ iCUE Connected │
│  │                                                                    │ │
│  │  🖱️ Dark Core RGB PRO                    🔋 85% │ 📡 Wireless     │ │
│  │     [Device Image]                                                 │ │
│  │     Color: [🔴 #FF0000]  Effect: [Breathing ▼]  [Apply]           │ │
│  │                                                                    │ │
│  │  🎧 HS70 PRO Wireless                    🔋 60% │ 📡 Wireless     │ │
│  │     [Device Image]                                                 │ │
│  │     Color: [🔵 #0066FF]  Effect: [Static ▼]     [Apply]           │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  ┌─── RAZER ─────────────────────────────────────────────────────────┐ │
│  │  [Razer Logo]                          Status: ✅ Chroma Connected │ │
│  │                                                                    │ │
│  │  ⌨️ BlackWidow V4 Pro                              🔌 USB         │ │
│  │     [Device Image]                                                 │ │
│  │     Mode: [Chroma Effects ▼]                                       │ │
│  │     [Wave] [Spectrum] [Breathing] [Reactive] [Custom]             │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  ┌─── LOGITECH ──────────────────────────────────────────────────────┐ │
│  │  [Logitech G Logo]                      Status: ⚠️ G HUB Required  │
│  │                                                                    │ │
│  │  🖱️ G Pro X Superlight 2                🔋 45% │ ⚡ LightSpeed    │ │
│  │     [Device Image]                                                 │ │
│  │     DPI: [25600 ▼]  Color: [🟢 #00FF00]        [Apply]            │ │
│  │                                                                    │ │
│  │  ⌨️ G915 X TKL                          🔋 70% │ ⚡ LightSpeed    │ │
│  │     [Device Image]                                                 │ │
│  │     Effect: [LIGHTSYNC ▼]  Brightness: 100%    [Apply]            │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

### 5. Corsair Integration (Enhanced)

**SDK:** iCUE SDK 4.0 + Direct HID fallback

**Features:**
- Full device enumeration with images
- Per-LED control for supported devices
- Hardware lighting mode (persists without software)
- DPI stages for mice
- Battery monitoring for wireless devices
- Macro sync (import from iCUE)

**Device Image Assets:**
```
Assets/Corsair/
  dark-core-rgb-pro.png
  scimitar-rgb-elite.png
  hs70-pro-wireless.png
  k70-rgb-mk2.png
  mm700-rgb.png
  ...
```

---

### 6. Razer Integration (Full Chroma SDK)

**SDK:** Razer Chroma SDK 3.x

**Features:**
- Full Chroma effect library
- Per-key RGB for keyboards
- Mouse zone control
- Headset lighting
- Mousepad/dock lighting
- Chroma Connect apps support
- Custom effect creator

**Device Image Assets:**
```
Assets/Razer/
  blackwidow-v4-pro.png
  deathadder-v3-pro.png
  kraken-v3-pro.png
  firefly-v2.png
  ...
```

---

### 7. Logitech Integration (Enhanced)

**SDK:** G HUB SDK + Direct HID (no G HUB mode)

**Features:**
- LIGHTSYNC effect support
- Per-key RGB for G-series keyboards
- Mouse HERO sensor DPI control
- PowerPlay charging status
- LightSpeed wireless status
- G Cloud device support

**Device Image Assets:**
```
Assets/Logitech/
  g-pro-x-superlight-2.png
  g502-x-plus.png
  g915-x-tkl.png
  g733.png
  g-cloud.png
  ...
```

---

### 8. Unified RGB Engine

**Cross-brand synchronization:**
- Single color picker → all devices
- Preset effects that work across brands
- Audio-reactive mode (all devices pulse to music)
- Game integration (ammo, health, cooldowns)
- Screen color sampling (ambient lighting)

---

## 🔴 Critical Priority: Linux Support

### 1. Linux CLI Tool (Phase 1)

**Target:** v2.0.0-alpha  
**Effort:** High  
**Impact:** Very High

Create a command-line utility for Linux that provides core functionality without a GUI.

#### Architecture

```
┌────────────────────────────────────────────────────────────┐
│                    omencore-cli                            │
├────────────────────────────────────────────────────────────┤
│  Commands:                                                 │
│    omencore fan --profile auto|silent|gaming|max           │
│    omencore fan --speed 50%                                │
│    omencore fan --curve "40:20,50:30,60:50,80:80,90:100"  │
│    omencore perf --mode balanced|performance               │
│    omencore keyboard --color FF0000                        │
│    omencore keyboard --zone 0 --color 00FF00               │
│    omencore status                                         │
│    omencore monitor                                        │
├────────────────────────────────────────────────────────────┤
│  Daemon mode:                                              │
│    omencore-daemon --config /etc/omencore/config.toml     │
│    systemctl enable omencore                               │
└────────────────────────────────────────────────────────────┘
```

#### Implementation Strategy

**Recommended: Hybrid Approach**
- Phase 1: Python CLI for basic fan control (quick win, inspired by omen-fan)
- Phase 2: Full .NET CLI with all features  
- Phase 3: Avalonia UI cross-platform GUI

#### EC Register Map (from omen-fan)

Based on [omen-fan/docs/probes.md](https://github.com/alou-S/omen-fan/blob/main/docs/probes.md):

```
Fan Control:
  0x34*   Fan 1 Speed Set     units of 100RPM  
  0x35*   Fan 2 Speed Set     units of 100RPM
  0x2E    Fan 1 Speed %       Range 0 - 100
  0x2F    Fan 2 Speed %       Range 0 - 100
  0xEC?   Fan Boost           00 (OFF), 0x0C (ON)
  0xF4*   Fan State           00 (Enable), 02 (Disable)

Temperature:
  0x57    CPU Temp            int °C
  0xB7    GPU Temp            int °C

BIOS Control:
  0x62*   BIOS Control        00 (Enabled), 06 (Disabled)
  0x63*   Timer               Counts down from 120 (0x78) to 0
                              Resets fan control when reaches 0

Power:
  0x95*   Performance Mode    0x30=Default, 0x31=Performance, 0x50=Cool
  0xBA**  Thermal Power       00-05 (power limit multiplier)
```

#### Linux Kernel Requirements

```bash
# Load EC module with write support
sudo modprobe ec_sys write_support=1

# Verify EC access
ls -la /sys/kernel/debug/ec/ec0/io

# HP WMI module (keyboard lighting, hotkeys)
modprobe hp-wmi
```

#### Files to Create

```
src/
  OmenCore.Linux/
    OmenCore.Linux.csproj          # .NET 8 cross-platform project
    Program.cs                      # CLI entry point
    Commands/
      FanCommand.cs
      PerformanceCommand.cs
      KeyboardCommand.cs
    Hardware/
      LinuxEcController.cs          # /sys/kernel/debug/ec/ec0/io access
      LinuxHwMonController.cs       # /sys/class/hwmon/* sensors
      LinuxKeyboardController.cs    # /sys/devices/platform/hp-wmi/*
```

---

### 2. Linux GUI (Phase 2)

**Target:** v2.0.0-beta  
**Effort:** Very High  

**Recommended: Avalonia UI**
- Reuse XAML knowledge from WPF
- Single codebase for Windows/Linux
- MVVM architecture compatible

---

### 3. Linux Systemd Integration

```ini
# /etc/systemd/system/omencore.service
[Unit]
Description=OmenCore Fan Control Daemon
After=multi-user.target

[Service]
Type=simple
ExecStart=/usr/bin/omencore-daemon
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

---

## 🟡 Important: On-Screen Display (OSD)

### 4. In-Game OSD Overlay

**Target:** v2.0.0  
**Effort:** High  
**Impact:** High

Display real-time performance metrics as an overlay during games.

```
┌──────────────────────────────┐
│ CPU: 65°C  4.8GHz  45W      │
│ GPU: 72°C  1890MHz 125W     │
│ RAM: 28.4/32 GB             │
│ FAN: 3500 / 4200 RPM        │
│ FPS: 142                    │
└──────────────────────────────┘
```

**Implementation Options:**
- **Windows:** RTSS (RivaTuner) integration
- **Linux:** MangoHud integration
- **Fallback:** Transparent overlay window (borderless games)

---

### 5. Mode Change OSD

Show brief notification when performance mode or fan profile changes.

```
┌─────────────────────────────────┐
│   🔥 Performance Mode          │
│      Turbo Engaged             │
└─────────────────────────────────┘
        (fades after 2 seconds)
```

---

## ⚡ Advanced: CPU/GPU Overclocking

### 6. CPU Overclocking (Intel)

**Target:** v2.0.0  
**Risk:** ⚠️ High - Can cause instability

**Features:**
- PL1/PL2 (Power Limits) adjustment
- Turbo duration (Tau) control
- Already have undervolt, add overvolt option (+0 to +100mV)

**Implementation:**
```csharp
// MSR_PKG_POWER_LIMIT (0x610)
public void SetPL1(int watts, int timeWindow);
public void SetPL2(int watts);
```

---

### 7. GPU Overclocking (NVIDIA)

**Target:** v2.0.0  
**Effort:** Medium  

**Features:**
- Core clock offset: -500MHz to +300MHz
- Memory clock offset: -500MHz to +1500MHz  
- Power limit slider (if not vBIOS locked)
- V/F curve editor (advanced)

**Implementation:**
- NVAPI SDK integration
- Similar to MSI Afterburner functionality

---

### 8. GPU Overclocking (AMD)

**Target:** v2.0.0  
**Effort:** Medium  

**Features:**
- Core/Memory frequency adjustment
- STAPM/Fast/Slow power limits
- RyzenAdj integration for mobile APUs

---

### 9. Overclocking Profiles

Save/load overclocking configurations per-game or per-use-case.

```json
{
  "name": "Gaming Profile",
  "cpu": { "pl1_watts": 55, "undervolt_mv": -100 },
  "gpu": { "core_offset_mhz": 150, "memory_offset_mhz": 500 }
}
```

---

## 🎮 Peripheral RGB Support (Legacy - See Priority #2 Above)

> **Note:** The sections below are superseded by the Priority #2 RGB overhaul above.
> Keeping for technical reference only.

### 12. Full Razer Chroma SDK Integration

**Target:** v2.0.0  
**Effort:** Medium  
**Impact:** High

Complete integration with Razer Chroma SDK for full device control (beyond v1.5's preliminary support).

**Features:**
- Full device enumeration via Chroma SDK
- Per-key RGB control for keyboards
- Mouse lighting zones
- Headset lighting
- Mousepad RGB control
- Chroma effects (Wave, Spectrum, Breathing, Reactive)
- Profile sync with OmenCore presets

**Implementation:**
```csharp
// Native Razer SDK integration
[DllImport("RzChromaSDK64.dll")]
public static extern RzResult Init();

// Device enumeration
public async Task<List<RazerDevice>> EnumerateDevicesAsync();
public async Task ApplyEffect(Guid deviceId, ChromaEffect effect);
```

---

### 13. Enhanced Corsair iCUE SDK Integration

**Target:** v2.0.0  
**Effort:** Medium  
**Impact:** High

Upgrade from current HID-direct approach to full iCUE SDK when available.

**Current Limitations (v1.5):**
- Direct HID only - basic lighting
- PID database requires manual updates
- No DPI control
- No macro support

**v2.0 Goals:**
- Integrate official Corsair iCUE SDK
- Full lighting effect support
- DPI profile control for mice
- Macro recording/playback
- Battery status for wireless devices
- Device firmware updates

**Hybrid Approach:**
- Use iCUE SDK when iCUE is running
- Fallback to direct HID when iCUE not installed
- Best of both worlds

---

### 14. Greater Logitech G HUB Integration

**Target:** v2.0.0  
**Effort:** Medium  
**Impact:** Medium

Expand Logitech support beyond current basic implementation.

**Current Limitations (v1.5):**
- Requires G HUB running
- Limited to basic SDK features
- No direct HID fallback

**v2.0 Goals:**
- Direct HID support (like Corsair) - no G HUB required
- Per-key RGB for keyboards
- Mouse DPI control
- LightSpeed wireless device support
- PowerPlay charging status
- G HUB SDK as optional enhancement layer

**Device Support Priority:**
- G Pro X Superlight
- G502 X series
- G915/G915 X keyboards
- G733/G PRO X headsets

---

### 15. Unified RGB Control

**Target:** v2.0.0  
**Effort:** High  
**Impact:** Very High

Single interface to control all RGB devices regardless of brand.

**Features:**
- "Sync All" button - applies same color/effect to all devices
- Brand-agnostic presets (Gaming, Productivity, Night Mode)
- Per-zone color mapping across devices
- Audio-reactive lighting (microphone input)
- Game integration (health bars, ammo, etc.)

**UI Mockup:**
```
┌─────────────────────────────────────────────────────────┐
│  RGB Sync                                               │
│  ┌─────────────────────────────────────────────────┐   │
│  │ [🔗 Sync All]  [Preset: Gaming ▼]  [Color: 🔴]  │   │
│  ├─────────────────────────────────────────────────┤   │
│  │ HP OMEN Keyboard    ✓ Synced                    │   │
│  │ Corsair M65         ✓ Synced                    │   │
│  │ Razer BlackWidow    ✓ Synced                    │   │
│  │ Logitech G502       ✓ Synced                    │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## 🎮 Game Integration

### 10. Per-Game Profiles

Automatically apply settings when specific games launch.

**Detection Methods:**
- Process name monitoring
- Steam/GOG/Epic library integration
- Manual executable selection

---

### 11. Game Library View

```
┌─────────────────────────────────────────────────────────┐
│  Game Library                                           │
│  ┌─────────────────────────────────────────────────┐   │
│  │ 🎮 Cyberpunk 2077        [Turbo Gaming]         │   │
│  │    [▶ Launch] [⚙️ Settings]                      │   │
│  ├─────────────────────────────────────────────────┤   │
│  │ 🎮 Elden Ring            [Balanced]             │   │
│  │    [▶ Launch] [⚙️ Settings]                      │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## 🚀 Windows System Optimizer

### 16. Integrated Gaming Optimizer

**Target:** v2.0.0  
**Effort:** High  
**Impact:** Very High  
**Source:** [windows11nontouchgamingoptimizer](https://github.com/theantipopau/windows11nontouchgamingoptimizer)

Port the Windows 11 Gaming Optimizer batch script into a native C# implementation within OmenCore.

#### Rationale

- Same target audience (gamers optimizing Windows for performance)
- Complements existing fan/thermal/GPU controls
- Native C# is cleaner, safer, and more maintainable than batch scripts
- Leverages OmenCore's existing UI, config, and logging systems
- Can integrate with Game Profiles for automatic per-game optimization

#### Features to Integrate

**Core Optimizations:**
- ✅ Ultimate/High Performance power plan switching (already have via Performance Modes)
- 🆕 Debloat Windows 11 - Remove consumer apps, OEM bloatware
- 🆕 Disable touch/pen features (for non-touch gaming rigs)
- 🆕 Gaming performance registry tweaks (Game Mode, GPU scheduling, input lag)
- 🆕 Service optimization (telemetry, indexing, background tasks)
- 🆕 Network latency tweaks (TCP/IP, delivery optimization)
- 🆕 Visual effects optimization (animations, transparency)
- 🆕 Startup program management

**Advanced:**
- 🆕 GPU latency registry tweaks (20+ vendor-agnostic tweaks)
- 🆕 Audio latency reduction
- 🆕 Mouse/keyboard input optimization (disable pointer precision, queue sizes)
- 🆕 CPU core parking control
- 🆕 Page file optimization based on RAM
- 🆕 SSD vs HDD detection with hardware-specific tweaks

**Safety:**
- 🆕 System Restore point creation before changes
- 🆕 Full registry backup of modified keys
- 🆕 Comprehensive undo/revert functionality
- 🆕 Verification tool to check optimization status

#### New View: System Optimizer

```
┌─────────────────────────────────────────────────────────────────┐
│  System Optimizer                            [Apply] [Undo All] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Quick Actions:                                                 │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐             │
│  │  Gaming     │  │  Balanced   │  │   Revert    │             │
│  │  Maximum    │  │  Recommended│  │   Defaults  │             │
│  └─────────────┘  └─────────────┘  └─────────────┘             │
│                                                                 │
│  Status: 12/15 optimizations active           [View Report]     │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│  Power & Performance                                            │
│  ├─ [✓] Ultimate Performance power plan                         │
│  ├─ [✓] Hardware GPU scheduling                                 │
│  ├─ [✓] Game Mode enabled                                       │
│  └─ [✓] Foreground app priority (Win32PrioritySeparation)      │
│                                                                 │
│  Services & Background                                          │
│  ├─ [✓] Disable telemetry services                              │
│  ├─ [ ] Disable Windows Search indexing                         │
│  └─ [✓] Disable Superfetch (SSD detected)                       │
│                                                                 │
│  Network                                                         │
│  ├─ [✓] TCP optimizations (TcpNoDelay, TcpAckFrequency)         │
│  ├─ [✓] Disable Delivery Optimization (P2P)                     │
│  └─ [ ] Aggressive mode (may affect VPN)        ⚠️              │
│                                                                 │
│  Input & Graphics                                               │
│  ├─ [✓] Disable mouse acceleration                              │
│  ├─ [✓] Increased input queue sizes                             │
│  └─ [✓] Disable Game DVR recording                              │
│                                                                 │
│  Visual Effects                                                  │
│  ├─ [ ] Balanced (smooth + performance)                         │
│  └─ [✓] Minimal (maximum FPS)                                   │
│                                                                 │
│  Storage                                                         │
│  ├─ [✓] SSD: TRIM enabled, defrag disabled                      │
│  └─ [✓] Disable 8.3 filename creation                           │
│                                                                 │
│  Bloatware Management                               [Manage...] │
│  └─ 24 consumer apps available to remove                        │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

#### Implementation Architecture

```
src/OmenCoreApp/
  Services/
    SystemOptimizer/
      SystemOptimizerService.cs       # Main orchestration
      PowerOptimizer.cs               # Power plans, performance modes
      ServiceOptimizer.cs             # Windows services management
      NetworkOptimizer.cs             # TCP/IP, Delivery Optimization
      VisualEffectsOptimizer.cs       # Animations, transparency
      InputOptimizer.cs               # Mouse, keyboard settings
      StorageOptimizer.cs             # SSD/HDD detection, TRIM
      BloatwareManager.cs             # App removal/restoration
      RegistryBackup.cs               # Backup/restore registry keys
      OptimizationVerifier.cs         # Check current state
  
  ViewModels/
    SystemOptimizerViewModel.cs       # UI bindings
  
  Views/
    SystemOptimizerView.xaml          # New sidebar tab
  
  Models/
    OptimizationProfile.cs            # Preset configurations
    OptimizationState.cs              # Current system state
```

#### Game Profile Integration

Automatically apply/revert optimizations when games launch:

```json
{
  "gameName": "Cyberpunk 2077",
  "executable": "Cyberpunk2077.exe",
  "fanPreset": "Gaming",
  "performanceMode": "Performance",
  "gpuPowerBoost": "Maximum",
  "systemOptimizations": {
    "profile": "Gaming Maximum",
    "applyOnLaunch": true,
    "revertOnExit": true
  }
}
```

#### Safety Considerations

- **Laptop Detection**: Warn about battery impact, offer to skip power-intensive tweaks
- **Restore Points**: Auto-create before applying optimizations (if System Protection enabled)
- **Registry Backup**: Store all modified keys in `%APPDATA%\OmenCore\registry_backup\`
- **Incremental Apply**: Each toggle can be applied/reverted independently
- **Verification**: Show pass/fail status for each optimization
- **Logging**: Full audit trail of all changes

#### Migration from Batch Script

| Batch Feature | C# Implementation |
|---------------|-------------------|
| `reg add` commands | `Microsoft.Win32.Registry` API |
| `sc config` (services) | `System.ServiceProcess.ServiceController` |
| `powercfg` | WMI `Win32_PowerPlan` + P/Invoke |
| `wmic` queries | WMI/CIM via `System.Management` |
| `PowerShell -Command` | Direct .NET equivalents |
| Restore point | `System.Management` WMI `SystemRestore` class |

---

## 🔧 Architecture Improvements (from v1.5 audit)

### Consolidated Hardware Polling

Currently FanService and HardwareMonitoringService run separate polling loops.

**Goals:**
- Single `ThermalDataProvider` shared service
- FanService subscribes to temperature events
- Reduce CPU overhead

---

### HPCMSL Integration for BIOS Updates

Replace HP API scraping with official HP Client Management Script Library.

**Benefits:**
- Authoritative BIOS version information
- Proper SoftPaq metadata

---

### Per-Fan Curve Support

Allow independent fan curves for CPU and GPU fans.

**Implementation:**
- "Link fans" toggle (default: linked)
- Separate CPU/GPU curve editors when unlinked

---

## Bug Fixes

### Auto-Update File Locking Issue

**Problem:** Auto-update download fails with `IOException` when computing SHA256 hash.

**Fix:**
- Ensure download stream disposed before hash computation
- Use `FileShare.Read` when opening for hash

---

## Implementation Timeline

| Phase | Version | Target | Features |
|-------|---------|--------|----------|
| Alpha 1 | 2.0.0-alpha1 | Jan 2026 | **UI/UX Overhaul** - System tray fix, dark theme consistency |
| Alpha 2 | 2.0.0-alpha2 | Feb 2026 | **RGB Overhaul** - Device images, brand logos, connection status |
| Alpha 3 | 2.0.0-alpha3 | Feb 2026 | Linux CLI, Basic EC access |
| Beta 1 | 2.0.0-beta1 | Mar 2026 | **Full Corsair/Razer/Logitech SDK** integration |
| Beta 2 | 2.0.0-beta2 | Mar 2026 | Linux daemon, OSD overlay, **System Optimizer UI** |
| RC | 2.0.0-rc | Apr 2026 | GPU/CPU OC, Game profiles, Bloatware manager |
| Release | 2.0.0 | May 2026 | Full Linux GUI, All features, Polish |
| Release | 1.6.0 | May 2026 | Full Linux GUI, All features |

---

## Technical Debt / Prerequisites

1. **Refactor Hardware Abstraction**
   - Create `IFanController`, `IPerformanceController` interfaces
   - Enable dependency injection for platform-specific code

2. **Configuration Migration**
   - Move from JSON to TOML (cross-platform standard)

3. **Logging Improvements**
   - Structured logging (Serilog)
   - Platform-appropriate log locations

---

## Risk Assessment

| Feature | Risk | Mitigation |
|---------|------|------------|
| Linux EC Access | Medium | Extensive testing, kernel version checks |
| CPU Overclocking | High | Comprehensive warnings, conservative defaults |
| GPU Overclocking | Medium | Use vendor APIs, respect vBIOS limits |
| OSD Overlay | Low | RTSS/MangoHud integration |

---

## Linux Testing Matrix

| Distro | Desktop | HP-WMI | Status |
|--------|---------|--------|--------|
| Ubuntu 24.04 | GNOME | TBD | |
| Fedora 40 | GNOME | TBD | |
| Arch | KDE | TBD | |
| Pop!_OS 24.04 | COSMIC | TBD | |

---

## References

- [hp-omen-linux-module](https://github.com/pelrun/hp-omen-linux-module) - Linux kernel WMI module
- [omen-fan](https://github.com/alou-S/omen-fan) - Python Linux fan control
- [OmenHubLight](https://github.com/determ1ne/OmenHubLight) - Archived C# implementation
- [Avalonia UI](https://avaloniaui.net/) - Cross-platform .NET UI framework
- [MangoHud](https://github.com/flightlessmango/MangoHud) - Linux gaming overlay
- [NVAPI](https://developer.nvidia.com/nvapi) - NVIDIA GPU control API
- [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) - AMD mobile power management

---

## Community Feedback Requests

We're looking for testers with:
- HP Omen laptops running Linux (any distro)
- AMD GPU Omen models
- Victus series laptops
- Omen laptops with per-key RGB keyboards

---

*Last Updated: December 18, 2025*
