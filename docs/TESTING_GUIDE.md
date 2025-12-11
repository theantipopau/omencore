# OmenCore Testing Guide

## Local Testing Workflow

### Testing the Installer Build

1. **Build Release Artifacts**
   ```powershell
   .\build-installer.ps1 -Configuration Release -Runtime win-x64 -SingleFile
   ```

2. **Uninstall Previous Version** (if upgrading)
   - Go to Settings > Apps > Installed Apps
   - Search for "OmenCore"
   - Click Uninstall (or run the installer - it will upgrade in place)

3. **Install Fresh Build**
   ```powershell
   Start-Process -FilePath ".\artifacts\OmenCoreSetup-1.0.0.4.exe" -Verb RunAs
   ```

4. **First Launch Checks**
   - Launch OmenCore from Start Menu
   - Check for driver detection popup
   - Verify tray icon appears
   - Check all tabs load without errors

### Testing the Portable ZIP

1. **Extract and Run**
   ```powershell
   Expand-Archive -Path ".\artifacts\OmenCore-1.0.0.4-win-x64.zip" -DestinationPath ".\test-portable"
   cd .\test-portable
   .\OmenCore.exe
   ```

2. **Verify Portable Behavior**
   - Config should save in same directory
   - No installer registry entries
   - Can run from any location

---

## Common Issues and Solutions

### Issue: "Application failed to initialize properly"

**Cause**: Missing .NET 8 Desktop Runtime

**Solution**:
```powershell
winget install Microsoft.DotNet.DesktopRuntime.8
```

### Issue: Driver Detection Popup on Every Launch

**Cause**: WinRing0 driver not installed

**Solution**:
- Install LibreHardwareMonitor: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases
- Run LibreHardwareMonitor once as Administrator to install driver
- Restart OmenCore

**Note**: Windows Defender may flag WinRing0 - this is a false positive. Add exception if needed.

### Issue: Scrollbar Missing on Monitoring Tab

**Status**: Fixed in next build (1.0.0.5)

**Workaround**: Resize window or toggle "Reduce CPU Usage" mode

### Issue: Fan Control Not Working

**Possible Causes**:
1. WinRing0 driver not installed → See driver solution above
2. Unsupported laptop model → Check compatibility list in README
3. HP OMEN Command Center still running → Close it first

**Debug Steps**:
```powershell
# Check if WinRing0 driver is loaded
Get-WmiObject Win32_SystemDriver | Where-Object {$_.Name -like "*WinRing*"}

# Check OmenCore logs (after first run creates them)
Get-Content "$env:LOCALAPPDATA\OmenCore\logs\*.log" -Tail 50
```

### Issue: "Access Denied" Errors

**Cause**: OmenCore needs Administrator privileges for hardware control

**Solution**:
- Right-click OmenCore shortcut → Run as Administrator
- OR: Right-click OmenCore.exe → Properties → Compatibility → "Run as administrator"

---

## Feature Testing Checklist

### Dashboard / Monitoring
- [ ] CPU/GPU temperatures display correctly
- [ ] Charts update in real-time
- [ ] "Reduce CPU Usage" toggle works
- [ ] Hardware summary cards show correct info
- [ ] **Scrollbar appears when content overflows** ✅ Fixed in 1.0.0.5

### Fan Control
- [ ] Fan mode switches (Auto/Manual/Off)
- [ ] Custom curves can be saved/loaded
- [ ] Fan speed updates in real-time
- [ ] Temperature-based triggering works
- [ ] Presets save and load correctly

### System Control
- [ ] Performance mode switches (Silent/Balanced/Performance)
- [ ] Power limits apply correctly
- [ ] GPU switch modes work (if supported)
- [ ] CPU undervolt applies and persists (if enabled)
- [ ] Settings save across restarts

### RGB & Peripherals
- [ ] Corsair devices detected (if present)
- [ ] Logitech devices detected (if present)
- [ ] Lighting effects apply correctly
- [ ] Macro profiles work
- [ ] DPI stages configure correctly

### Services
- [ ] Windows services toggle on/off
- [ ] HP OMEN cleanup detects installed components
- [ ] Cleanup operations complete successfully
- [ ] Auto-update checks for new versions (when available)

### Tray Icon
- [ ] Icon shows in system tray
- [ ] CPU temperature badge updates
- [ ] Left-click opens main window
- [ ] Right-click shows context menu
- [ ] Exit option closes app cleanly

---

## Regression Testing (After Code Changes)

### Quick Smoke Test (5 minutes)
1. Launch app
2. Check each tab loads
3. Change one setting per tab
4. Restart app and verify settings persisted

### Full Regression Test (20 minutes)
1. Run full feature checklist above
2. Test error scenarios:
   - Disconnect peripheral during use
   - Change settings rapidly
   - Minimize/maximize window repeatedly
3. Check memory usage over 10 minutes
4. Verify logs contain no unexpected errors

### Performance Test
```powershell
# Monitor CPU usage while app is running
Get-Process OmenCore | Select-Object CPU, WorkingSet64, Threads
```

**Expected**:
- Idle: <2% CPU
- Active monitoring: 3-8% CPU
- Memory: <200 MB

---

## Debug Mode Testing

### Enable Verbose Logging

Edit `config/default_config.json` (or `%LOCALAPPDATA%\OmenCore\config.json`):
```json
{
  "LogLevel": "Debug",
  "MonitoringInterval": 1000
}
```

### View Real-Time Logs

```powershell
# Tail logs in real-time
Get-Content "$env:LOCALAPPDATA\OmenCore\logs\omencore.log" -Wait -Tail 20
```

### Common Log Patterns

**Healthy startup**:
```
[INFO] OmenCore v1.0.0.4 starting up
[INFO] ✓ WinRing0 driver detected - full hardware control available
[INFO] Configuration loaded from ...
[INFO] Monitoring service started
```

**Driver missing**:
```
[WARN] ⚠️ WinRing0 driver not detected - fan control and undervolt will be disabled
[INFO] 💡 To enable fan control: Install LibreHardwareMonitor or see docs/WINRING0_SETUP.md
```

**Peripheral detection**:
```
[INFO] Corsair SDK initialized: 3 devices detected
[INFO] Logitech service started: 2 devices detected
```

---

## Known Issues (1.0.0.4)

### Critical
- None

### High Priority
1. **Monitoring tab scrollbar missing** → Fixed in 1.0.0.5

### Medium Priority
1. Driver installation guide path may not exist when installed via installer
2. LibreHardwareMonitor not bundled in installer (users must install separately)

### Low Priority
1. CUE.NET compatibility warning during build (functional, but older API)
2. Windows Defender false positive on WinRing0 driver

---

## Pre-Release Validation

Before publishing a new release:

1. ✅ Build both ZIP and EXE with no errors
2. ✅ SHA256 hashes calculated and documented
3. ✅ Test fresh install on clean Windows VM
4. ✅ Test upgrade from previous version
5. ✅ Verify all features in checklist
6. ✅ Check CHANGELOG.md is updated
7. ✅ Ensure VERSION.txt matches installer version
8. ✅ Tag matches version in all files

---

## Bug Reporting Template

When reporting issues during testing:

```markdown
**OmenCore Version**: 1.0.0.4
**OS**: Windows 11 23H2
**Laptop Model**: HP OMEN 16-k0000
**WinRing0 Driver**: Installed / Not Installed

**Steps to Reproduce**:
1. 
2. 
3. 

**Expected Behavior**:


**Actual Behavior**:


**Logs** (from %LOCALAPPDATA%\OmenCore\logs):
```
[paste relevant log lines]
```

**Screenshot** (if applicable):
[attach image]
```

---

## Contact for Testing Help

- GitHub Issues: https://github.com/theantipopau/omencore/issues
- Discussions: https://github.com/theantipopau/omencore/discussions
