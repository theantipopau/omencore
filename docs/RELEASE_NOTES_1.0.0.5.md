# OmenCore v1.0.0.5 Release Notes

## Overview

Version 1.0.0.5 focuses on polish, user experience improvements, and reliability enhancements. This release addresses feedback from 1.0.0.4 users and improves the first-run experience.

---

## Highlights

### 🎯 Better First-Run Experience
The WinRing0 driver detection dialog now only appears once on the first launch, reducing annoyance for users who choose not to install the driver. The app remembers this choice via the new `FirstRunCompleted` flag in the config.

### 🎨 Visual Polish
- **Enhanced system tray tooltips** with emoji icons, multi-line formatting, and comprehensive system stats (CPU, GPU, RAM)
- **Improved button states** with better visual feedback for disabled buttons (grayed background, muted text, arrow cursor)
- **Smoother animations** on button hover with refined scale transitions

### 🔧 Reliability Improvements
- **Config validation** automatically repairs invalid or missing configuration properties
- **Smart path detection** for driver setup guide (checks install location, dev path, then online fallback)
- **Robust error handling** prevents crashes on malformed config files

### 📜 Scrollbar Fix
The Monitoring tab now properly scrolls when window is resized or content overflows, fixing a long-standing usability issue.

---

## Detailed Changes

### Added Features

#### FirstRunCompleted Flag
- Added `FirstRunCompleted` boolean to `AppConfig`
- Prevents driver prompt from appearing on every launch
- Saved immediately after user dismisses initial prompt
- Existing users will see the prompt once after upgrading

#### Enhanced Tray Tooltips
```
🎮 OmenCore v1.0.0.5
━━━━━━━━━━━━━━━━━━
🔥 CPU: 65°C @ 45%
🎯 GPU: 72°C @ 85%
💾 RAM: 12.3/32.0 GB (38%)
━━━━━━━━━━━━━━━━━━
Left-click to open dashboard
```

#### Config Validation
- `ValidateAndRepair()` method ensures all collections are initialized
- Validates monitoring interval (500-10000ms range)
- Validates EC device path (falls back to default)
- Logs warnings for invalid values
- Never crashes on malformed JSON - always falls back to defaults

#### Visual Improvements
- Button disabled state now uses `SurfaceDarkBrush` background
- Foreground changes to `TextMutedBrush` for better contrast
- Opacity reduced to 0.6 for subtle effect
- Cursor changes to arrow (no longer shows hand)

### Bug Fixes

#### Monitoring Tab Scrollbar
**Problem**: Content overflowed without scrollbar when window was resized or charts were visible.

**Solution**: Wrapped entire `DashboardView` content in `ScrollViewer` with `VerticalScrollBarVisibility="Auto"`.

**Impact**: Users can now access all monitoring content regardless of window size.

#### Driver Guide Path Resolution
**Problem**: Installed app looked for docs at dev path `../../../../docs/WINRING0_SETUP.md`, which doesn't exist.

**Solution**: Three-tier fallback system:
1. Check `<install>/docs/WINRING0_SETUP.md` (bundled docs)
2. Check `../../../../docs/WINRING0_SETUP.md` (dev environment)
3. Open online: `https://github.com/theantipopau/omencore/blob/main/docs/WINRING0_SETUP.md`

**Impact**: Setup guide always opens, whether installed, portable, or running from dev environment.

#### Config Loading Crash
**Problem**: Malformed JSON or missing properties could crash app on startup.

**Solution**: Wrapped `Load()` in try-catch with validation and default fallback.

**Impact**: App now gracefully handles corrupt config files and logs errors instead of crashing.

### Technical Changes

#### ConfigurationService Refactoring
- Added `Config` property for direct access (previously returned from `Load()`)
- Constructor now calls `Load()` and stores result in `Config`
- `Save()` requires explicit `AppConfig` parameter (no longer uses instance variable)
- Better separation of concerns

#### Logging Improvements
Version logging now shows:
```
OmenCore v1.0.0.5 starting up (Assembly: 1.0.0.5)
```

Previously only showed assembly version, which could differ from `VERSION.txt`.

#### Chart Performance
- Existing throttling (100ms, 10 FPS max) retained
- Visual caching with `BitmapCache` on polylines reduces redraw overhead
- DPI-aware stroke thickness for crisp lines on high-DPI displays

---

## Migration Notes

### From 1.0.0.4 → 1.0.0.5

#### Config Changes
- **New property**: `FirstRunCompleted` (boolean, defaults to `false`)
- **Backward compatible**: Old configs load fine, property added on first save
- **No user action required**: Config auto-upgrades on first run

#### Behavior Changes
- Driver prompt appears once for existing users after upgrading
- Users who dismissed it in 1.0.0.4 will see it one more time
- After dismissal, it won't appear again (saved to config)

#### Breaking Changes
- **None** - fully backward compatible

---

## Known Issues

### Build Warnings
- **CUE.NET compatibility warning**: Package targets .NET Framework but works fine with .NET 8
- **Inno Setup architecture warning**: Cosmetic only, "x64" deprecated in favor of "x64compatible"

### External Dependencies
- **LibreHardwareMonitor not bundled**: Users must install separately for driver
- **Windows Defender false positive**: WinRing0 driver flagged as HackTool (industry-wide issue)

### Future Improvements
See `FIXES_FOR_1.0.0.6.md` for roadmap.

---

## Testing Recommendations

### Smoke Test (2 minutes)
1. Fresh install on clean VM
2. Verify first-run prompt appears
3. Dismiss prompt, restart app
4. Confirm prompt doesn't appear again
5. Check tray tooltip shows stats
6. Resize window, verify scrollbar

### Regression Test (10 minutes)
1. Upgrade from 1.0.0.4
2. Verify settings preserved
3. Test all tabs load correctly
4. Confirm FirstRunCompleted added to config
5. Delete config, restart, verify defaults
6. Corrupt config JSON, restart, verify recovery

### Performance Test
- Monitor CPU usage: should remain <2% idle, 3-8% active
- Memory usage: <200 MB typical
- Tray tooltip updates every 2 seconds

---

## Build Information

- **Build Date**: 2025-12-10
- **Compiler**: .NET 8 SDK with WPF workload
- **Installer**: Inno Setup 6.6.1
- **Artifacts**:
  - `OmenCore-1.0.0.5-win-x64.zip` (portable)
  - `OmenCoreSetup-1.0.0.5.exe` (installer)

### SHA256 Hashes
```
ZIP: C2DE9F8A7270CB172926CA2249FA8978E88D586E02C1D789C3B9D41D0EC73C7E
EXE: D587950DBE6A38A5B8E14D89C632BB8ED0883D0D7211BE280F52258B5C61F69B
```

---

## Contributors

- Main development and testing
- Community feedback from 1.0.0.4 users

---

## Next Steps

1. **Tag release**: `git tag -a v1.0.0.5 -m "Release 1.0.0.5"`
2. **Push tag**: `git push origin v1.0.0.5`
3. **Create GitHub release** using `GITHUB_RELEASE_1.0.0.5.md`
4. **Upload artifacts** (ZIP and EXE)
5. **Announce** in Discussions

---

## Support

- **Issues**: https://github.com/theantipopau/omencore/issues
- **Discussions**: https://github.com/theantipopau/omencore/discussions
- **Documentation**: See `README.md` and `docs/` folder
