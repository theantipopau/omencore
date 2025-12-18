# OmenCore v2.0 Development Tracker

**Branch:** `v2.0-dev`  
**Started:** December 18, 2025  
**Target:** Q2 2026

---

## 📋 Development Checklist

### Phase 1: Foundation & Quick Wins (Alpha 1 - Jan 2026)

**Priority: UI/UX fixes that don't break functionality**

#### 1.1 System Tray Overhaul ⬜
- [ ] Create custom context menu control (no white margins)
- [ ] Dark theme context menu with proper styling
- [ ] Icons visible at all times (not just on hover)
- [ ] Smooth hover animations
- [ ] Temperature display improvements
- [ ] Compact mode option

#### 1.2 Dashboard Polish ⬜
- [ ] Card-based layout with consistent shadows
- [ ] Improve LoadChart rendering smoothness
- [ ] Status badges with clear iconography
- [ ] Quick action button hover states
- [ ] Better spacing and alignment

#### 1.3 Settings View Improvements ⬜
- [ ] Toggle switches instead of checkboxes
- [ ] Grouped sections with headers
- [ ] Inline help text for complex options
- [ ] Better organization

#### 1.4 Typography & Visual Consistency ⬜
- [ ] Consistent font weights throughout
- [ ] Monospace font for numeric values
- [ ] Better contrast ratios
- [ ] Consistent color usage for status indicators

---

### Phase 2: System Optimizer (Alpha 2 - Feb 2026)

**Priority: High-impact feature users are asking for**

#### 2.1 Core Infrastructure ⬜
- [ ] Create `Services/SystemOptimizer/` folder structure
- [ ] `SystemOptimizerService.cs` - main orchestration
- [ ] `RegistryBackup.cs` - backup/restore registry keys
- [ ] `OptimizationVerifier.cs` - check current state

#### 2.2 Power Optimizations ⬜
- [ ] `PowerOptimizer.cs`
- [ ] Ultimate Performance power plan activation
- [ ] Hardware GPU scheduling toggle
- [ ] Game Mode enable/disable
- [ ] Win32PrioritySeparation optimization

#### 2.3 Service Optimizations ⬜
- [ ] `ServiceOptimizer.cs`
- [ ] Telemetry service management
- [ ] SysMain/Superfetch control (SSD detection)
- [ ] Windows Search indexing toggle
- [ ] Background task optimization

#### 2.4 Network Optimizations ⬜
- [ ] `NetworkOptimizer.cs`
- [ ] TCP optimizations (TcpNoDelay, TcpAckFrequency)
- [ ] Delivery Optimization (P2P) toggle
- [ ] Nagle algorithm control

#### 2.5 Input & Graphics ⬜
- [ ] `InputOptimizer.cs`
- [ ] Disable mouse acceleration (pointer precision)
- [ ] Game DVR/Game Bar control
- [ ] Input queue size optimization

#### 2.6 Visual Effects ⬜
- [ ] `VisualEffectsOptimizer.cs`
- [ ] Animation toggle
- [ ] Transparency effects control
- [ ] Balanced vs Minimal presets

#### 2.7 Storage Optimizations ⬜
- [ ] `StorageOptimizer.cs`
- [ ] SSD vs HDD detection
- [ ] TRIM verification for SSDs
- [ ] 8.3 filename creation toggle

#### 2.8 UI Implementation ⬜
- [ ] `SystemOptimizerViewModel.cs`
- [ ] `SystemOptimizerView.xaml`
- [ ] Add to sidebar navigation
- [ ] Quick action buttons (Gaming/Balanced/Revert)
- [ ] Individual toggle switches
- [ ] Status indicators per optimization

---

### Phase 3: RGB Overhaul (Beta 1 - Mar 2026)

#### 3.1 Asset Preparation ⬜
- [ ] Create `Assets/Corsair/` folder with device images
- [ ] Create `Assets/Razer/` folder with device images
- [ ] Create `Assets/Logitech/` folder with device images
- [ ] Brand logo assets (SVG preferred)

#### 3.2 Enhanced Corsair SDK ⬜
- [ ] Upgrade to iCUE SDK 4.0
- [ ] Full device enumeration with images
- [ ] Battery status for wireless devices
- [ ] DPI control for mice
- [ ] Hardware lighting mode support

#### 3.3 Full Razer Chroma SDK ⬜
- [ ] Chroma SDK 3.x integration
- [ ] Per-key RGB for keyboards
- [ ] Effect library (Wave, Spectrum, Breathing, Reactive)
- [ ] Custom effect creator

#### 3.4 Enhanced Logitech SDK ⬜
- [ ] G HUB SDK integration
- [ ] Direct HID fallback (no G HUB required)
- [ ] LightSpeed wireless status
- [ ] PowerPlay charging status

#### 3.5 Unified RGB Engine ⬜
- [ ] "Sync All" functionality
- [ ] Cross-brand effect presets
- [ ] Audio-reactive mode
- [ ] Screen color sampling

#### 3.6 Lighting View Redesign ⬜
- [ ] Device cards with images
- [ ] Connection status indicators
- [ ] Battery level display
- [ ] Per-device controls

---

### Phase 4: Linux Support (Beta 2 - Mar 2026)

#### 4.1 Linux CLI ⬜
- [ ] Create `OmenCore.Linux` project
- [ ] EC register access via `/sys/kernel/debug/ec/ec0/io`
- [ ] Fan control commands
- [ ] Performance mode commands
- [ ] Keyboard lighting commands
- [ ] Status/monitor commands

#### 4.2 Linux Daemon ⬜
- [ ] Daemon mode implementation
- [ ] systemd service file
- [ ] TOML configuration
- [ ] Automatic fan curves

#### 4.3 Distro Testing ⬜
- [ ] Ubuntu 24.04
- [ ] Fedora 40
- [ ] Arch Linux
- [ ] Pop!_OS

---

### Phase 5: Advanced Features (RC - Apr 2026)

#### 5.1 OSD Overlay ⬜
- [ ] RTSS integration research
- [ ] Transparent overlay window fallback
- [ ] Mode change notifications
- [ ] Customizable metrics display

#### 5.2 Game Profiles ⬜
- [ ] Process detection for games
- [ ] Per-game settings storage
- [ ] Auto-apply on game launch
- [ ] Steam/GOG/Epic library integration

#### 5.3 GPU Overclocking ⬜
- [ ] NVAPI SDK integration
- [ ] Core clock offset slider
- [ ] Memory clock offset slider
- [ ] Power limit adjustment
- [ ] V/F curve editor (advanced)

#### 5.4 CPU Overclocking ⬜
- [ ] PL1/PL2 adjustment UI
- [ ] Turbo duration control
- [ ] Comprehensive warnings

---

### Phase 6: Polish & Release (May 2026)

#### 6.1 Linux GUI ⬜
- [ ] Avalonia UI setup
- [ ] Port main views
- [ ] Cross-platform testing

#### 6.2 Bloatware Manager ⬜
- [ ] App enumeration
- [ ] Safe removal
- [ ] Restoration capability

#### 6.3 Final Testing ⬜
- [ ] Full regression testing
- [ ] Performance benchmarking
- [ ] Documentation updates

---

## 📝 Changelog

### v2.0.0-alpha1 (In Progress)

#### Added
- ❄️ Extreme fan preset (100% at 75°C for high-power systems)
- 🎛️ Extreme button in Fan Control GUI

#### Changed
- 📄 Roadmap renamed from v1.6 to v2.0

#### Fixed
- (pending)

---

### Upcoming Changes

*This section will be updated as features are implemented*

---

## 🎯 Implementation Strategy

### Why Start with System Tray + System Optimizer?

1. **System Tray Fix (Phase 1.1)**
   - Currently has visible bugs (white margins, icon issues)
   - Quick visual win that improves user perception
   - Low risk - doesn't affect core functionality
   - ~1-2 days of work

2. **System Optimizer (Phase 2)**
   - High user demand (you already have the batch script)
   - Complements existing features (Fan + Thermal + GPU Power + **System Tweaks**)
   - Self-contained - can be developed in parallel
   - Uses familiar C#/.NET APIs (Registry, WMI, ServiceController)
   - Provides immediate value to gaming users
   - ~2-3 weeks of work

3. **RGB Overhaul (Phase 3) - Later**
   - Requires external SDK dependencies
   - Need device images/logos (licensing considerations)
   - More complex with multiple vendor integrations
   - Best done after core stability

4. **Linux (Phase 4) - Later**
   - Requires new project structure
   - Different testing environment
   - Can be parallelized once Windows is stable

---

## 📊 Progress Summary

| Phase | Status | Progress |
|-------|--------|----------|
| 1. Foundation & Quick Wins | 🟡 Starting | 0/20 |
| 2. System Optimizer | ⚪ Planned | 0/35 |
| 3. RGB Overhaul | ⚪ Planned | 0/24 |
| 4. Linux Support | ⚪ Planned | 0/12 |
| 5. Advanced Features | ⚪ Planned | 0/15 |
| 6. Polish & Release | ⚪ Planned | 0/8 |

**Overall: 0/114 tasks (0%)**

---

## 🔗 Quick Links

- [ROADMAP_v2.0.md](ROADMAP_v2.0.md) - Full feature specifications
- [v1.5-stable branch](https://github.com/theantipopau/omencore/tree/v1.5-stable) - Bug fixes for current release
- [v2.0-dev branch](https://github.com/theantipopau/omencore/tree/v2.0-dev) - Active development

---

*Last Updated: December 18, 2025*
