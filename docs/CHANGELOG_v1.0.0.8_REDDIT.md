# OmenCore v1.0.0.8 Changelog (Reddit Post)

---

## 🚀 OmenCore v1.0.0.8 Released - Game Profiles, Settings View & Major UI Overhaul

Hey OMEN community! Big update for OmenCore, the lightweight HP OMEN Gaming Hub replacement.

**Download**: [GitHub Releases](https://github.com/theantipopau/omencore/releases/tag/v1.0.0.8)  
**Website**: [omencore.info](https://omencore.info)

---

## ✨ What's New Since v1.0.0.6

### 🎮 Game Profile System (NEW!)
- **Auto-switching profiles** - Automatically apply fan curves, performance modes, undervolt settings, GPU mode, and lighting when games launch
- **Game detection** - Monitors running processes and switches profiles in real-time
- **Profile analytics** - Tracks launch count and total playtime per game
- **Import/Export** - Share your profiles with others or backup your settings
- **Restore on exit** - Returns to default settings when you close a game

### ⚙️ Settings View (NEW!)
- **Fan Dust Cleaning** - Built-in maintenance mode runs fans at max RPM to blow out dust
- **Hotkey Configuration** - Customize keyboard shortcuts for:
  - Toggle Fan Mode (default: Ctrl+Shift+F)
  - Toggle Performance Mode (default: Ctrl+Shift+P)
  - Toggle Boost/Quiet Mode
  - Show/Hide Window (default: Ctrl+Shift+O)
- **Notification Preferences** - Control when you see alerts for temperature warnings, fan changes, updates
- **Export/Import Config** - Backup and restore all your OmenCore settings

### 🎨 UI Improvements
- **Quick Fan Control Buttons** - One-click access to Silent, Balanced, Performance, Max modes
- **Temperature Warning Colors** - CPU/GPU temps now show green/yellow/red based on thresholds
- **RGB Color Picker** - New visual color selector with hex input for lighting profiles
- **Per-Zone RGB Control** - Independent color settings for each keyboard zone
- **Reset to Defaults** - Easy way to restore factory settings per section
- **Tooltips Everywhere** - Hover hints explaining what each control does
- **Battery Status Panel** - Shows charge level, health, and power source
- **CPU Wattage Display** - Real-time power consumption on the dashboard

### 🐛 Bug Fixes
- **Fixed multi-instance game crash** - Running multiple instances of the same game no longer crashes
- **Thread-safe process monitoring** - Switched to ConcurrentDictionary for reliability
- **Fixed ThermalChart binding** - Temperature history graph now displays correctly
- **Fixed system tray submenu styling** - No more white-on-white text in dark mode
- **Resource cleanup** - Proper disposal of monitoring services on exit

### 🖼️ Visual Polish
- **20+ vector icons** - Crisp, scalable icons replace emoji throughout the UI
- **Refined window controls** - Professional minimize/maximize/close buttons
- **Auto-hide update banner** - "Latest version" message fades after 3 seconds
- **Colored sidebar icons** - Better visual hierarchy in navigation
- **Loading states** - "Loading temperature history..." shown while data populates

---

## 📊 By the Numbers

| Metric | Value |
|--------|-------|
| New files | 8+ |
| Lines added | ~5,000+ |
| New features | 15+ |
| Bug fixes | 7 |

---

## 🔧 How to Update

1. Download `OmenCoreSetup-1.0.0.8.exe` from [Releases](https://github.com/theantipopau/omencore/releases/tag/v1.0.0.8)
2. Run installer (it upgrades in-place, keeping your settings)
3. Or download portable ZIP and extract over existing installation

---

## 💬 Feedback Welcome!

Let me know what features you'd like to see next! Current roadmap:
- [ ] In-game overlay (FPS/temps)
- [ ] Per-key RGB editor
- [ ] More peripheral support

**GitHub Issues**: [Report bugs or request features](https://github.com/theantipopau/omencore/issues)

---

*OmenCore is free, open-source, and respects your privacy. No telemetry, no sign-ins, no bloat.*
