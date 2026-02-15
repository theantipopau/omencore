# OmenCore v1.4.0 Roadmap

**Target Release:** Q1 2026  
**Status:** Planning  
**Last Updated:** December 15, 2025

---

## Overview

v1.4.0 focuses on **advanced features**, **peripheral ecosystem expansion**, and **polishing the user experience** based on beta feedback and community requests.

### Beta2 Lessons Learned

From v1.3.0-beta2 development, the following issues informed our priorities:

| Issue | Lesson | v1.4 Impact |
|-------|--------|-------------|
| Version parsing failed for `-beta2` | Need robust semantic versioning | Add SemVer library |
| OSD didn't work minimized | HwndSource isolation pattern works | Apply pattern elsewhere |
| OMEN key read wrong config field | Config field unification needed | Audit all config access |
| Fan presets didn't apply | Service/ViewModel sync issues | Better MVVM patterns |
| GPU boost reset on boot | Retry logic needed for boot-time | Centralized startup sequencer |

---

## ï¿½ Community Bug Reports (v1.3.0-beta2 Feedback)

Critical bugs reported by community members that need addressing in v1.4:

### BUG-1: System Restore Point Creation Fails
**Reported by:** Multiple users  
**Error:** `System.Management.ManagementException: No encontrado` (Spanish: "Not found")  
**File:** `Services/SystemRestoreService.cs:line 98`  
**Root Cause:** WMI property access failing - likely System Restore disabled or insufficient permissions  
**Fix:**
```csharp
// Check if System Restore is enabled before attempting
// Add proper error handling for non-English Windows
// Provide user-friendly error message
```
**Priority:** Medium

---

### BUG-2: CPU Undervolting Fails (MSR Write Error)
**Reported by:** Multiple users (Intel 13th gen, AMD Ryzen)  
**Error:** `Failed to write MSR 0x152` - InvalidOperationException  
**File:** `Hardware/WinRing0MsrAccess.cs:line 119`  
**Root Cause:** 
- Intel: Plundervolt patches block MSR writes on many systems
- AMD: Ryzen CPUs don't support voltage offset via MSR 0x150
**Fix:**
- Detect if MSR writes are blocked and show informative message
- Add AMD Ryzen detection and disable undervolt UI for unsupported CPUs
- Add "Undervolt unavailable (blocked by BIOS)" status message
**Priority:** High

---

### BUG-3: TCC Offset Resets to 100Â°C on Reboot
**Reported by:** Multiple users (Omen Max 16, Omen 17-ck2xxx)  
**Symptom:** CPU Temperature Limit returns to 100Â°C after PC restart  
**File:** `App.xaml.cs`, `Services/FanService.cs`  
**Root Cause:** TCC offset not persisted to config or not re-applied on startup  
**Fix:**
- Save TCC offset to config when changed
- Re-apply saved TCC offset in StartupSequencer
- Verify successful application after boot
**Priority:** High

---

### BUG-4: GPU Power Boost Resets to Minimum on Reboot
**Reported by:** Multiple users  
**Symptom:** GPU TGP/Dynamic Boost returns to Minimum after restart  
**File:** `ViewModels/MainViewModel.cs` (line ~1750 reapply logic)  
**Root Cause:** Reapply happens too early before WMI BIOS is ready  
**Fix:**
- Move GPU boost restoration to StartupSequencer with retry logic
- Add verification that setting was actually applied
- Log success/failure of boot-time restoration
**Priority:** High

---

### BUG-5: Auto-Start Not Working
**Reported by:** Multiple users  
**Symptom:** App doesn't start automatically with Windows  
**File:** `ViewModels/SettingsViewModel.cs` (registry code)  
**Root Cause:** Registry write failing or task scheduler entry not created  
**Fix:**
- Check if registry entry exists after write
- Add fallback to Task Scheduler method
- Show error if both methods fail
- Verify startup entry in Settings view
**Priority:** Medium

---

### BUG-6: SSD Sensor Shows 0Â°C and 0% Usage
**Reported by:** User with SAMSUNG MZVLB512HBJQ-000H1  
**Symptom:** SSD temperature and usage always show 0  
**File:** `Services/HardwareMonitorService.cs`  
**Root Cause:** LibreHardwareMonitor not detecting Samsung NVMe SMART data  
**Fix:**
- Check if SSD sensors are available before displaying
- Hide SSD widget if no valid data
- Add fallback to WMI Win32_DiskDrive for basic info
**Priority:** Low

---

### BUG-7: Overlay Hotkey Doesn't Work
**Reported by:** Multiple users  
**Symptom:** Pressing OSD hotkey does nothing  
**File:** `Services/HotkeyService.cs`, `Services/OsdOverlayService.cs`  
**Root Cause:** Hotkey registration failing when app starts minimized  
**Fix:**
- Register hotkeys after window is fully loaded (already partially fixed in beta2)
- Add retry logic if initial registration fails
- Log hotkey registration status
**Priority:** Medium

---

### BUG-8: Unsupported Features Still Visible in UI
**Reported by:** AMD Ryzen users  
**Symptom:** Undervolt toggle visible but doesn't work on AMD; Corsair/Logitech visible with no devices  
**File:** `Views/AdvancedView.xaml`, `ViewModels/LightingViewModel.cs`  
**Root Cause:** UI doesn't hide unavailable features based on hardware detection  
**Fix:**
- Bind Visibility to capability detection results
- Add `IsUndervoltSupported`, `IsCorsairAvailable`, `IsLogitechAvailable` properties
- Hide sections when features unavailable
**Priority:** Medium

---

### BUG-9: System Tray Refresh Rate Display Shows Wrong Value
**Reported by:** User with 144Hz display  
**Symptom:** After changing to 144Hz, tray popup still shows "60Hz"  
**File:** `Views/TrayContextMenu.xaml`, `ViewModels/MainViewModel.cs`  
**Root Cause:** Refresh rate display not updated after change  
**Fix:**
- Refresh the tray menu items after applying display change
- Add PropertyChanged notification for CurrentRefreshRate
- Consider auto-toggle in Power Automation (60Hz on battery)
**Priority:** Low

---

### BUG-10: Fan Profile UI Confusing/Redundant
**Reported by:** Multiple users  
**Symptom:** Too many similar options (Quick Presets vs Choose Preset dropdown)  
**Analysis:**
- Quick Presets: Max, Gaming, Auto, Quiet
- Choose Preset: Quiet, Performance, Gaming (overlap/confusion)
- "Quiet" vs "Silent" naming inconsistency
**Fix:**
- Unify preset naming (use: Silent, Auto, Performance, Max)
- Remove redundant dropdown or merge with quick presets
- Add clear visual distinction between BIOS presets and custom curves
**Priority:** Medium

---

### BUG-11: OSD Position Missing "TopCenter" Option
**Reported by:** User request  
**Symptom:** Cannot position OSD at top-center of screen  
**File:** `ViewModels/SettingsViewModel.cs`, `Views/OsdOverlayWindow.xaml`  
**Fix:**
- Add TopCenter to OsdPosition enum
- Update positioning logic in OsdOverlayWindow
- Add button/option in Settings view
**Priority:** Low (Enhancement)

---

### BUG-12: Logs Stored in AppData Instead of Program Files
**Reported by:** User request  
**Symptom:** Logs in `%APPDATA%\OmenCore\Logs` - user wants `C:\Program Files\OmenCore\Logs`  
**File:** `Services/LoggingService.cs`  
**Consideration:** Program Files requires admin elevation for writes  
**Fix:**
- Keep logs in AppData (standard Windows practice)
- Add "Open Log Folder" button in Settings
- Consider optional log location in config
**Priority:** Low (Won't Fix - by design)

---

### BUG-13: Antivirus False Positive (Trojan Detection)
**Reported by:** User  
**Symptom:** Antivirus flags OmenCore as trojan  
**Root Cause:** WinRing0 driver and low-level hardware access triggers heuristics  
**Fix:**
- Submit to Microsoft/major AV vendors for whitelisting
- Add documentation about false positives
- Consider code signing certificate (costs $200-500/year)
**Priority:** Medium (Reputation)

---

### Community Feature Requests (From Bug Reports)

| Request | Description | Priority |
|---------|-------------|----------|
| Keyboard animations | Wave, breathing, color cycle effects | Medium |
| Auto 60Hz on battery | Power Automation toggle for refresh rate | Low |
| Hide unsupported features | Don't show unavailable options | Medium |
| Keyboard control without OGH | Work without OMEN Gaming Hub installed | High (4-zone works) |

---

## ï¿½ðŸ”´ High Priority (Must Have)

### Priority Rationale
Based on beta2 feedback:
1. **Macro Editor** - #1 user request, mentioned in every release
2. **Per-Key RGB** - Large portion of OMEN laptops have this capability
3. **Startup Reliability** - Too many boot-time issues in beta
4. **Model Compatibility** - Transcend/2024+ models need better support

---

### 1. Full Macro Editor
**Status:** Planned (mentioned in beta2 changelog)  
**Effort:** High  
**Impact:** High  
**Files:** `Services/MacroService.cs`, `Views/MacroEditorWindow.xaml`, `Models/MacroDefinition.cs`

Replace basic record/playback with a full visual macro editor.

**Features:**
- Visual timeline editor with drag-and-drop actions
- Action types: Key press, key combo, mouse click, delay, text input
- Loop support (repeat N times, infinite)
- Conditional triggers (on app launch, on key press)
- Import/export macros as JSON
- Per-game macro profiles (tie to Game Profiles)

**Implementation Tickets:**
```
[MACRO-1] Create MacroDefinition model with serialization
  - MacroAction base class
  - KeyPressAction, MouseClickAction, DelayAction, TextInputAction
  - MacroCondition (trigger: key, button, app launch)
  - JSON serialization/deserialization

[MACRO-2] Create MacroEditorWindow XAML
  - Timeline ItemsControl with action blocks
  - Drag-and-drop reordering
  - Action property editors (key picker, delay slider)
  - Toolbar: Add Key, Add Delay, Add Mouse, Add Loop

[MACRO-3] Implement MacroPlaybackService
  - SendInput for key/mouse simulation
  - Async execution with cancellation
  - Loop handling with abort support
  - Inter-action delays

[MACRO-4] Hook integration for triggers
  - Extend OmenKeyService pattern for macro triggers
  - Mouse button hooks (Button 4/5)
  - App launch detection (process monitor)

[MACRO-5] Game profile integration
  - Add MacroProfiles to GameProfile model
  - Auto-load macros when game launches
  - Conflict detection (same trigger, different macros)
```

**UI Concept:**
```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ Macro Editor: "Auto Reload"                             â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚ â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â” â”‚
â”‚ â”‚ [Key: R] â†’ [Delay: 50ms] â†’ [Key: R Release]        â”‚ â”‚
â”‚ â”‚ [Delay: 2000ms] â†’ [Loop: 5x]                       â”‚ â”‚
â”‚ â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜ â”‚
â”‚                                                         â”‚
â”‚ [+ Key] [+ Delay] [+ Mouse] [+ Text] [+ Loop]          â”‚
â”‚                                                         â”‚
â”‚ Trigger: [ Button 4 (Mouse) â–¼]  Game: [All Games â–¼]    â”‚
â”‚                                         [Save] [Test]  â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

### 2. Per-Key RGB Keyboard Support
**Status:** Known limitation (type 0x03 keyboards)  
**Effort:** High  
**Impact:** High  
**Files:** `Hardware/PerKeyRgbController.cs`, `Views/KeyboardLayoutEditor.xaml`, `Services/KeyboardAnimationService.cs`

Full per-key RGB control for OMEN keyboards that support it.

**Features:**
- Individual key color assignment
- Visual keyboard layout editor
- Preset patterns: Wave, Ripple, Reactive, Static per-key
- Profile layers (base + reactive overlay)
- Export/import keyboard layouts

**Implementation Tickets:**
```
[RGB-1] Research per-key EC protocol
  - Analyze OmenMon EC dumps for type 0x03 keyboards
  - Document register addresses and data format
  - Test on real hardware (need community help)

[RGB-2] Create PerKeyRgbController
  - EC write methods for individual keys
  - Batch update for animations (minimize EC writes)
  - Key index mapping for different layouts

[RGB-3] Create KeyboardLayoutEditor XAML
  - SVG-based keyboard visual (ANSI/ISO layouts)
  - Click to select key(s), color picker
  - Multi-select with Ctrl/Shift
  - Copy/paste colors between keys

[RGB-4] Implement KeyboardAnimationService
  - Wave effect (horizontal, vertical, radial)
  - Reactive effect (ripple from pressed key)
  - Color cycle (per-key phase offset)
  - Animation frame timing (60fps target)

[RGB-5] Profile and preset system
  - Save/load per-key layouts
  - Built-in presets (Rainbow Wave, Fire, etc.)
  - Import/export as JSON
```

**Technical Requirements:**
- Reverse-engineer per-key EC protocol (likely 4-zone Ã— 32 keys)
- Animation table support for effects
- Potentially requires OmenMon-style direct EC writes
- **Community Testing:** Need volunteers with per-key OMEN keyboards

---

### 2b. 4-Zone Keyboard Interactive Controls (NEW - beta2 feedback)
**Status:** UI exists but non-functional  
**Effort:** Medium  
**Impact:** High  
**Files:** `Views/LightingView.xaml`, `ViewModels/LightingViewModel.cs`, `Services/KeyboardLightingService.cs`

The HP OMEN Keyboard section in RGB & Peripherals tab shows 4 zone boxes but they are completely static - cannot be clicked, no color picker, no apply button. Need to make this functional.

**Current State:**
- Zone boxes are display-only (Border elements with static colors)
- No click handlers or color pickers
- No binding to KeyboardLightingService
- Backend (WMI BIOS) is now wired up but UI doesn't use it

**Features Needed:**
- Clickable zone boxes that open color picker
- Per-zone color selection with preview
- "Apply" button to send colors to keyboard via WMI BIOS
- "Apply All" to set all zones to same color
- Save/load zone color presets
- Real-time preview of current keyboard colors

**Implementation Tickets:**
```
[KB4Z-1] Add zone color bindings to LightingViewModel
  - Zone1Color, Zone2Color, Zone3Color, Zone4Color properties
  - KeyboardLightingService injection
  - ApplyZoneColorsCommand, ApplyAllZonesCommand

[KB4Z-2] Make zone boxes interactive in LightingView.xaml
  - Add click handlers to zone Border elements
  - Open ColorPicker popup on click
  - Bind zone fill colors to ViewModel properties
  - Add "Apply to Keyboard" button

[KB4Z-3] Wire up KeyboardLightingService
  - Call SetZoneColor() for individual zones
  - Call SetAllZoneColors() for batch update
  - Read current colors on startup (GetColorTable)
  - Error handling with user feedback

[KB4Z-4] Add keyboard color presets
  - Built-in presets: Red, Gaming (red gradient), Cool (blue), etc.
  - Custom preset save/load
  - Quick-apply dropdown
```

**UI Mockup:**
```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ HP OMEN Keyboard                                            â”‚
â”‚ Your laptop's built-in 4-zone RGB keyboard                  â”‚
â”œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¤
â”‚ KEYBOARD ZONES (click to change color)                      â”‚
â”‚ â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â” â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â” â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â” â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”        â”‚
â”‚ â”‚  Zone 1  â”‚ â”‚  Zone 2  â”‚ â”‚  Zone 3  â”‚ â”‚  Zone 4  â”‚        â”‚
â”‚ â”‚   Left   â”‚ â”‚ Middle-L â”‚ â”‚ Middle-R â”‚ â”‚   Right  â”‚        â”‚
â”‚ â”‚    ðŸ”´    â”‚ â”‚    ðŸ”µ    â”‚ â”‚    ðŸŸ£    â”‚ â”‚    ðŸ”µ    â”‚        â”‚
â”‚ â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜ â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜ â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜ â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜        â”‚
â”‚                                                             â”‚
â”‚ Preset: [Gaming â–¼]  [Apply to Keyboard]  [All Same Color]  â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

### 3. Startup Reliability Improvements (NEW)
**Status:** New - based on beta2 issues  
**Effort:** Medium  
**Impact:** High  
**Files:** `Services/StartupSequencer.cs`, `App.xaml.cs`

Centralized startup sequencer to handle boot-time reliability issues.

**Problems Addressed:**
- GPU boost resets after boot (needs retries)
- OSD hotkey registration fails when minimized
- Fan preset not applied on first boot
- Services initialized in wrong order

**Implementation Tickets:**
```
[BOOT-1] Create StartupSequencer service
  - Ordered task queue with dependencies
  - Retry logic with exponential backoff
  - Timeout handling per task
  - Progress reporting for splash screen

[BOOT-2] Migrate startup logic from App.xaml.cs
  - Move GPU boost restoration to sequencer
  - Move fan preset restoration to sequencer
  - Move OSD initialization to sequencer
  - Add startup diagnostics logging

[BOOT-3] Add startup health check
  - Verify WMI BIOS connectivity
  - Verify EC driver availability
  - Verify hotkey registration
  - Show warning if any checks fail

[BOOT-4] Post-login delay handling
  - Detect Windows login completion
  - Wait for desktop ready before applying settings
  - Handle fast startup vs cold boot differences
```

---

### 4. OGH Cleanup Progress Dialog
**Status:** Known issue (no progress feedback)  
**Effort:** Medium  
**Impact:** Medium  
**Files:** `Views/OghCleanupProgressWindow.xaml`, `Services/OghCleanupService.cs`

Add detailed progress dialog during OMEN Gaming Hub cleanup.

**Implementation Tickets:**
```
[OGH-1] Create OghCleanupProgressWindow
  - Progress bar with percentage
  - Step list with checkmarks/X marks
  - Current action text
  - Cancel button
  - Estimated time remaining

[OGH-2] Refactor OGH cleanup to async steps
  - Each cleanup action as separate awaitable
  - Report progress after each step
  - Support cancellation between steps
  - Partial rollback on cancel

[OGH-3] Add cleanup summary report
  - List of removed components
  - Disk space recovered
  - Registry keys removed
  - Recommendation for restart
```

**Features:**
- Step-by-step progress display
- Current action description
- Success/failure icons per step
- Cancel button with partial rollback
- Summary report at end

---

### 5. FPS Overlay in OSD
**Status:** Feature request  
**Effort:** Medium  
**Impact:** Medium  
**Files:** `Services/FpsMonitorService.cs`, `Views/OsdOverlayWindow.xaml`

Add real-time FPS counter to the in-game OSD overlay.

**Implementation Tickets:**
```
[FPS-1] Research FPS monitoring approaches
  - RTSS shared memory (if available)
  - PresentMon ETW tracing
  - D3D11/D3D12 present hook
  - Choose approach based on complexity/reliability

[FPS-2] Implement FpsMonitorService
  - Selected approach implementation
  - Frame time tracking
  - FPS averaging (1s window)
  - Min/Max/1% low tracking

[FPS-3] Add FPS to OSD overlay
  - FPS display with color coding (green/yellow/red)
  - Frame time graph option
  - Toggle in OSD settings
```

**Implementation Options:**
1. **Hook-based** (like RTSS) - High accuracy, complex
2. **DXGI Present monitoring** - Moderate accuracy
3. **External tool integration** - Rely on RTSS/CapFrameX API

**Recommendation:** Start with RTSS shared memory reading (if RTSS installed), fallback to "Install RTSS for FPS" message.

**UI:**
```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ CPU: 72Â°C  GPU: 68Â°C â”‚
â”‚ CPU: 45%   GPU: 78%  â”‚
â”‚ FAN: 4200 / 3800 RPM â”‚
â”‚ FPS: 144 (avg: 138)  â”‚  â† New
â”‚ RAM: 12.4 GB         â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

## ðŸŸ¡ Medium Priority

### 6. Keyboard Effects Engine
**Status:** WIP (wave, color cycle not implemented)  
**Effort:** High  
**Impact:** Medium  
**Files:** `Services/KeyboardAnimationService.cs`, `Models/LightingEffect.cs`

Full animation engine for keyboard lighting effects.

**Effects to Implement:**
- Wave (horizontal/vertical/diagonal)
- Color cycle (smooth rainbow)
- Breathing (fade in/out)
- Reactive (key press ripple)
- Audio visualizer (mic/system audio)
- Custom scripted effects (Lua/JSON DSL)

**Implementation Tickets:**
```
[EFFECT-1] Create LightingEffect base framework
  - ILightingEffect interface
  - EffectParameters model (speed, colors, direction)
  - Animation frame loop (60fps target)
  
[EFFECT-2] Implement basic effects
  - WaveEffect (horizontal sweep)
  - BreathingEffect (fade in/out)
  - ColorCycleEffect (hue rotation)
  - StaticEffect (solid color)

[EFFECT-3] Add reactive effects
  - KeyPressRippleEffect (ripple from pressed key)
  - Requires keyboard hook for key location
  - Fade/expand animation

[EFFECT-4] Audio visualizer
  - WASAPI audio capture (loopback)
  - FFT frequency analysis
  - Map frequency bands to zones/keys
```

---

### 7. Advanced Game Profiles
**Status:** Basic implementation exists  
**Effort:** Medium  
**Impact:** High  
**Files:** `Models/GameProfile.cs`, `Services/GameProfileService.cs`, `Views/GameProfileEditorWindow.xaml`

Expand game profile system with more options.

**New Profile Options:**
- Auto-overclock GPU when game launches
- Auto-switch refresh rate per game
- Auto-enable/disable G-SYNC
- Network priority (QoS) per game
- Screenshot/recording hotkeys per game
- Auto-close background apps when game launches

**Implementation Tickets:**
```
[GAME-1] Extend GameProfile model
  - RefreshRateOverride (int? Hz)
  - GpuBoostLevel (int?)
  - CloseApps (List<string> process names)
  - NetworkPriority (bool)
  - CustomMacros (List<MacroRef>)

[GAME-2] Create GameProfileEditorWindow
  - Full profile editor with all options
  - Game executable picker
  - Icon extraction from exe
  - Test profile button

[GAME-3] Add profile auto-detection
  - Detect installed games (Steam, Epic, GOG)
  - Suggest default profiles based on genre
  - IGDB/RAWG API for game metadata (optional)
```

**Smart Profiles:**
- Auto-detect optimal settings based on game genre
- Community profile sharing (optional cloud sync)

---

### 8. Hardware Health Dashboard
**Status:** New feature  
**Effort:** Medium  
**Impact:** Medium  
**Files:** `Views/HardwareHealthView.xaml`, `Services/HealthMonitorService.cs`

Comprehensive hardware health monitoring page.

**Metrics:**
- Battery health (cycle count, wear level, design vs actual capacity)
- SSD health (SMART data, TBW, remaining life)
- Fan health (bearing wear estimation based on RPM variance)
- Thermal paste aging estimate (based on temp/load curves over time)

**Implementation Tickets:**
```
[HEALTH-1] Battery health monitoring
  - WMI Win32_Battery for cycle count
  - DesignCapacity vs FullChargeCapacity ratio
  - Wear level percentage display
  - Charge/discharge history graph

[HEALTH-2] SSD health monitoring
  - S.M.A.R.T. data via WMI/LibreHardwareMonitor
  - TBW (Total Bytes Written) tracking
  - Predicted remaining life
  - Warning alerts at 80%/90% wear

[HEALTH-3] Thermal trends analysis
  - Track max temps over time
  - Delta between idle and load temps
  - Suggest repaste if delta increases over months
```

**Alerts:**
- Battery degradation warnings
- SSD failure predictions
- Thermal throttling frequency tracking

---

### 9. Undervolting Profiles
**Status:** Basic undervolt exists  
**Effort:** Medium  
**Impact:** Medium  
**Files:** `Views/UndervoltProfilesView.xaml`, `Services/UndervoltService.cs`

Expand undervolting with named profiles and safety features.

**Features:**
- Named profiles (Gaming, Silent, Battery Saver)
- Per-game undervolt profiles
- Stability testing wizard
- Auto-rollback on BSOD
- Voltage/frequency curve editor (like ThrottleStop)

**Implementation Tickets:**
```
[UV-1] Named undervolt profiles
  - Profile model with name, offset values
  - Quick-switch in system tray
  - Per-profile enable/disable

[UV-2] Stability testing wizard
  - CPU stress test integration (use system utilities)
  - Progressive voltage reduction
  - Auto-detect stable offset
  - Guided wizard UI

[UV-3] BSOD recovery
  - Boot-time config check
  - If BSOD detected, revert to safe profile
  - Requires startup hook/registry flag
```

---

### 10. Splash Screen with Boot Diagnostics (NEW)
**Status:** New - addresses slow startup perception  
**Effort:** Low  
**Impact:** Medium  
**Files:** `Views/SplashWindow.xaml`, `App.xaml.cs`

Show a branded splash screen during startup with progress indicators.

**Features:**
- OmenCore logo with loading animation
- Startup step indicators (Initializing WMI, Loading config, etc.)
- Quick health check status
- Option to skip to tray

**Implementation Tickets:**
```
[SPLASH-1] Create SplashWindow
  - Borderless window with logo
  - Progress bar and status text
  - Fade-in/fade-out animations

[SPLASH-2] Integrate with StartupSequencer
  - Report progress to splash
  - Show current task name
  - Transition to main window on complete
```

---

### 11. Config Sync / Cloud Backup (NEW)
**Status:** New - frequent user request  
**Effort:** Medium  
**Impact:** Medium  
**Files:** `Services/ConfigSyncService.cs`, `Views/CloudBackupView.xaml`

Backup and sync configuration across devices.

**Options:**
1. **Local export/import** (already exists) - Enhance UX
2. **GitHub Gist sync** - Use GitHub API for storage
3. **OneDrive/Google Drive** - OAuth integration

**Implementation Tickets:**
```
[SYNC-1] Enhance local export/import
  - One-click backup button
  - Auto-backup on settings change
  - Backup history with restore points

[SYNC-2] GitHub Gist integration
  - OAuth login to GitHub
  - Create private gist with config JSON
  - Sync on app start (optional)
  - Conflict resolution UI
```

---

### 12. Notification Center (NEW)
**Status:** New - consolidate alerts  
**Effort:** Low  
**Impact:** Medium  
**Files:** `Views/NotificationCenterView.xaml`, `Services/NotificationService.cs`

Centralized notification/alert system.

**Features:**
- In-app notification bell with badge count
- Notification types: Info, Warning, Error, Update
- Notification history with timestamps
- Click to navigate to relevant setting
- Toast notifications (Windows Action Center integration)

**Implementation Tickets:**
```
[NOTIFY-1] Create NotificationService
  - AddNotification(type, title, message, action?)
  - Notification model with timestamp, read status
  - Persist to config for history

[NOTIFY-2] Create NotificationCenterView
  - Bell icon in header with unread count
  - Dropdown panel with notification list
  - Mark as read, dismiss, clear all

[NOTIFY-3] Integrate across app
  - Update available notification
  - Thermal warning notification
  - Battery health alert
  - Fan control errors
```

---

## ðŸŸ¢ Low Priority / Future

### 13. Display Calibration
**Effort:** Medium  
**Impact:** Low-Medium

Basic display calibration and color profile management.

- Color temperature quick-toggle (6500K, 5500K warm, 7500K cool)
- Night mode schedule
- HDR toggle and brightness control
- ICC profile switching per app

---

### 14. Network Dashboard
**Effort:** Medium  
**Impact:** Low

Network monitoring and optimization.

- Real-time bandwidth usage (per-app)
- Latency/ping monitor to game servers
- WiFi signal strength and channel optimization
- Killer/Intel WiFi driver tweaks (disable packet coalescing, etc.)

---

### 15. Power Plan Integration
**Effort:** Low  
**Impact:** Low

Deeper Windows power plan control.

- Custom power plans with OmenCore integration
- Core parking control
- CPU frequency limits per mode
- Quick-toggle AC/battery power plans

---

### 16. Plugin/Extension System
**Effort:** High  
**Impact:** Medium

Allow community plugins for extensibility.

- Plugin API for custom sensors
- Plugin API for custom actions/automations
- Scripting support (Lua or C# scripts)
- Community plugin repository

---

### 17. Localization
**Effort:** Medium  
**Impact:** Medium

Multi-language support.

**Priority Languages:**
1. English (done)
2. German
3. French
4. Spanish
5. Portuguese (Brazil)
6. Chinese (Simplified)
7. Japanese
8. Russian

---

### 18. External Monitor Support (NEW)
**Effort:** Medium  
**Impact:** Low-Medium

Control external monitors connected to the laptop.

- DDC/CI brightness control
- Refresh rate switching for external displays
- Per-monitor color profiles
- Multi-monitor layout presets

---

### 19. Webcam/Mic Quick Controls (NEW)
**Effort:** Low  
**Impact:** Low

Privacy controls for webcam and microphone.

- Hardware webcam disable/enable
- Mic mute hotkey (global)
- Visual indicator when cam/mic in use
- Auto-disable cam/mic when screen locks

---

## ðŸ› Technical Debt / Refactoring

### Code Quality
- [ ] Migrate more async void to proper async Task with error handling
- [ ] Add unit tests for critical services (FanService, ConfigurationService)
- [ ] Implement proper dependency injection throughout
- [ ] Add integration tests for WMI BIOS commands
- [ ] Document EC register maps for different OMEN models
- [ ] **NEW:** Unify config field access (FeaturePreferences vs AppConfig pattern)
- [ ] **NEW:** Add semantic versioning library (NuGet: Semver)
- [ ] **NEW:** Create service interfaces for better testability

### Performance
- [ ] Reduce cold startup time (lazy-load more views)
- [ ] Memory profiling and optimization
- [ ] Reduce installer size (trim unused localization resources)
- [ ] **NEW:** Profile WMI call frequency and batch where possible
- [ ] **NEW:** Investigate background service for monitoring (separate process)

### UX Polish
- [ ] Keyboard navigation support (full accessibility)
- [ ] High-DPI/scaling improvements
- [ ] Theme editor (custom accent colors)
- [ ] First-run wizard/tutorial
- [ ] **NEW:** Onboarding flow for new users
- [ ] **NEW:** Contextual help tooltips throughout UI

### Testing
- [ ] **NEW:** Create hardware abstraction layer for mocking
- [ ] **NEW:** Add CI/CD pipeline with automated builds
- [ ] **NEW:** Integration test suite for update service
- [ ] **NEW:** Test matrix for different OMEN models

---

## ðŸ“… Tentative Timeline

| Phase | Features | Target |
|-------|----------|--------|
| **v1.3.1** | Bug fixes from beta2 feedback | Dec 2025 |
| **Alpha 1** | Startup Sequencer, Splash Screen, Notification Center | Jan 2026 |
| **Alpha 2** | Macro Editor (basic), OGH Cleanup Dialog | Jan 2026 |
| **Alpha 3** | Per-Key RGB research, Keyboard Effects | Feb 2026 |
| **Beta 1** | FPS Overlay, Advanced Game Profiles, Config Sync | Feb 2026 |
| **Beta 2** | Hardware Health, Undervolt Profiles, Polish | Mar 2026 |
| **RC** | Bug fixes, performance, localization (partial) | Mar 2026 |
| **Release** | v1.4.0 stable | Apr 2026 |

### Quick Wins (Can ship in v1.3.x patches)
- Splash screen with startup progress
- Notification center (basic)
- Config auto-backup
- Enhanced local export/import

---

## ðŸ“Š Feature Priority Matrix

| Feature | Effort | Impact | Dependencies | Priority Score |
|---------|--------|--------|--------------|----------------|
| Macro Editor | High | High | None | ðŸ”´ P1 |
| Per-Key RGB | High | High | EC research | ðŸ”´ P1 |
| Startup Sequencer | Medium | High | None | ðŸ”´ P1 |
| OGH Cleanup Dialog | Medium | Medium | None | ðŸ”´ P1 |
| FPS Overlay | Medium | Medium | RTSS/research | ðŸŸ¡ P2 |
| Splash Screen | Low | Medium | Startup Sequencer | ðŸŸ¡ P2 |
| Notification Center | Low | Medium | None | ðŸŸ¡ P2 |
| Keyboard Effects | High | Medium | Per-Key RGB | ðŸŸ¡ P2 |
| Game Profiles | Medium | High | None | ðŸŸ¡ P2 |
| Hardware Health | Medium | Medium | LibreHW | ðŸŸ¡ P2 |
| Undervolt Profiles | Medium | Medium | Existing UV | ðŸŸ¡ P2 |
| Config Sync | Medium | Medium | None | ðŸŸ¢ P3 |
| Localization | Medium | Medium | String extraction | ðŸŸ¢ P3 |
| Plugin System | High | Medium | Architecture | ðŸŸ¢ P3 |

---

## ðŸ—³ï¸ Community Input

Features will be prioritized based on:
1. GitHub issue upvotes
2. Discord poll results
3. Reddit feedback
4. Technical feasibility

**Feedback Channels:**
- [GitHub Issues](https://github.com/Jeyloh/OmenCore/issues)
- [Discord Server](https://discord.gg/9WhJdabGk8)
- [Reddit r/HPOmen](https://reddit.com/r/HPOmen)

---

## ðŸ“š References

- **OmenMon** - EC access patterns, fan curve implementation
- **G-Helper** - UX patterns for gaming laptop utilities
- **ThrottleStop** - Undervolt UI/safety patterns
- **RTSS** - FPS overlay implementation reference
- **OpenRGB** - Per-key RGB protocol research
