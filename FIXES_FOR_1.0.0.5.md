# Fixes Needed for OmenCore 1.0.0.5

## Critical Fixes

### 1. Monitoring Tab Scrollbar Missing ✅ **FIXED**
**Status**: Completed 2025-12-10

**Issue**: DashboardView.xaml missing ScrollViewer wrapper, causing content to overflow without scrollbar when window is small or charts are visible.

**Fix Applied**:
- Wrapped entire Grid in ScrollViewer with `VerticalScrollBarVisibility="Auto"`
- Added right padding (12px) to prevent scrollbar overlap
- Updated indentation for nested content

**Files Modified**:
- `src/OmenCoreApp/Views/DashboardView.xaml`

**Testing**:
- Resize window to small height
- Enable charts (disable "Reduce CPU Usage")
- Verify vertical scrollbar appears
- Confirm all content accessible via scroll

---

## High Priority Fixes

### 2. Initial Launch Error (User-Reported)
**Status**: Needs investigation

**Issue**: User reported error on initial launch after installing 1.0.0.4.

**Possible Causes**:
1. Missing .NET 8 Desktop Runtime
2. WinRing0 driver prompt causing focus issues
3. Config file creation race condition
4. First-time permissions issues

**Investigation Needed**:
- Ask user for exact error message
- Check Event Viewer logs
- Review App.xaml.cs exception handlers
- Test on fresh Windows VM without .NET 8 pre-installed

**Proposed Fixes**:
- Add better error dialog with actionable message
- Ensure LoggingService creates directory before writing
- Add .NET runtime check on startup with download link
- Improve driver detection prompt UX

---

### 3. Driver Installation Guide Path Not Found
**Status**: Needs fix

**Issue**: When installed via installer, the path calculation in `PromptDriverInstallation()` looks for:
```csharp
@"..\..\..\..\docs\WINRING0_SETUP.md"
```
This won't exist in installed location.

**Fix Approach**:
```csharp
// In App.xaml.cs, PromptDriverInstallation()
var setupDocPath = System.IO.Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory, 
    "docs", 
    "WINRING0_SETUP.md");

if (!System.IO.File.Exists(setupDocPath))
{
    // Fallback to online documentation
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = "https://github.com/theantipopau/omencore/blob/main/docs/WINRING0_SETUP.md",
        UseShellExecute = true
    });
}
```

**Additional**: Consider bundling key docs in installer.

---

## Medium Priority Fixes

### 4. LibreHardwareMonitor Not Bundled
**Status**: Intentional, but needs documentation improvement

**Current State**: Installer comments out LibreHardwareMonitor bundling due to extraction path issue.

**Options**:
1. Fix extraction logic in build-installer.ps1 to properly bundle LHM
2. Create separate "OmenCore + Driver" installer variant
3. Improve in-app messaging about driver requirement

**Recommendation**: Fix build script and bundle in 1.0.0.5:

```powershell
# In build-installer.ps1, after download:
Expand-Archive -Path "$PSScriptRoot\installer\LibreHardwareMonitor.zip" `
               -DestinationPath "$PSScriptRoot\installer\LibreHardwareMonitor" `
               -Force

# Verify extraction
if (!(Test-Path "$PSScriptRoot\installer\LibreHardwareMonitor")) {
    Write-Host "❌ LibreHardwareMonitor extraction failed" -ForegroundColor Red
    exit 1
}
```

Then uncomment bundling lines in OmenCoreInstaller.iss.

---

### 5. Log Directory Not Created Before First Write
**Status**: Potential race condition

**Issue**: If LoggingService.Initialize() fails to create directory, subsequent log calls may throw.

**Fix**:
```csharp
// In LoggingService.cs
public void Initialize()
{
    try
    {
        var logDir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(logDir))
        {
            Directory.CreateDirectory(logDir); // Creates all parent directories
        }
        
        _logWriter = new StreamWriter(_logPath, append: true) 
        { 
            AutoFlush = true 
        };
        
        Info("=== Logging initialized ===");
    }
    catch (Exception ex)
    {
        // Fallback: Log to console if file logging fails
        Console.WriteLine($"Failed to initialize logging: {ex.Message}");
        _logWriter = null; // Disable file logging
    }
}

private void Log(string level, string message)
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    var logLine = $"[{timestamp}] [{level}] {message}";
    
    // Always output to debug/console
    Debug.WriteLine(logLine);
    
    // Only write to file if available
    _logWriter?.WriteLine(logLine);
}
```

---

### 6. Exception Handling Improvements
**Status**: Enhancement

**Current**: Many catch blocks just log and continue.

**Improvement**: Add user-facing notifications for critical failures:

```csharp
// In MainViewModel.cs (example pattern)
try
{
    await _fanService.ApplyFanCurveAsync(curve);
    ShowNotification("✓ Fan curve applied", NotificationType.Success);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("driver"))
{
    ShowNotification("⚠️ Driver required for fan control", NotificationType.Warning);
    Logging.Error($"Fan control failed: {ex.Message}");
}
catch (Exception ex)
{
    ShowNotification("❌ Failed to apply fan curve", NotificationType.Error);
    Logging.Error($"Unexpected error: {ex}");
}
```

---

## Low Priority / Technical Debt

### 7. CUE.NET Compatibility Warning
**Status**: Functional but outdated

**Issue**: Build shows warning about .NET Framework compatibility.

**Solution**: Migrate to newer Corsair iCUE SDK when available, or suppress warning if functionality confirmed stable.

### 8. Windows Defender False Positive Documentation
**Status**: Document workaround

**Action**: Add section to README.md and TROUBLESHOOTING.md:
- Why WinRing0 is flagged
- How to add exclusion
- Alternative: Use Microsoft's own hardware drivers

### 9. Code Coverage Improvements
**Status**: Enhancement

**Current**: Basic unit tests for critical services.

**Goal for 1.1.0**: 
- 60% coverage target
- Add integration tests for fan/RGB services
- Mock hardware interactions

---

## UI/UX Improvements

### 10. Dark Mode Enhancements
**Status**: Future enhancement

- System theme detection
- Light/dark toggle in Settings
- High contrast mode support

### 11. Localization Support
**Status**: Future feature (1.2.0+)

- Internationalization framework
- String externalization
- Community translations

### 12. Advanced Settings Panel
**Status**: Enhancement

Hidden settings that power users might need:
- EC register address customization
- Polling intervals
- Debug mode toggle
- Hardware detection overrides

---

## Performance Optimizations

### 13. Chart Rendering Optimization
**Status**: Monitor performance

**Current**: Charts redraw on every data point.

**Optimization**: Implement data point pooling and incremental updates for better performance on lower-end systems.

### 14. Peripheral Service Throttling
**Status**: Enhancement

**Current**: Polls RGB devices on fixed interval.

**Improvement**: Adaptive polling based on activity (fast when user is changing settings, slow when idle).

---

## Release Checklist for 1.0.0.5

### Pre-Development
- [ ] Review all user-reported issues from 1.0.0.4
- [ ] Prioritize fixes (Critical > High > Medium)
- [ ] Update VERSION.txt to 1.0.0.5

### Development
- [x] Fix #1: Monitoring scrollbar
- [ ] Fix #2: Investigate initial launch error
- [ ] Fix #3: Driver guide path resolution
- [ ] Fix #4: LibreHardwareMonitor bundling
- [ ] Fix #5: Log directory creation
- [ ] Optional: Fix #6-12 based on time/priority

### Testing
- [ ] Run full regression test suite (see TESTING_GUIDE.md)
- [ ] Test on clean Windows 10 VM
- [ ] Test on clean Windows 11 VM
- [ ] Test upgrade from 1.0.0.4
- [ ] Verify all critical features work
- [ ] Memory leak testing (run for 1 hour)

### Documentation
- [ ] Update CHANGELOG.md with 1.0.0.5 changes
- [ ] Update README.md if needed
- [ ] Create RELEASE_NOTES_1.0.0.5.md
- [ ] Update GITHUB_RELEASE_TEMPLATE.md

### Build & Release
- [ ] Build artifacts with build-installer.ps1
- [ ] Calculate SHA256 hashes
- [ ] Test installer and ZIP locally
- [ ] Create Git tag v1.0.0.5
- [ ] Push to GitHub
- [ ] Create GitHub release with notes
- [ ] Upload artifacts
- [ ] Announce in discussions/community

---

## Future Roadmap (1.1.0+)

### Hardware Support
- Expand laptop model compatibility
- Add AMD CPU support
- Support for more RGB ecosystems (Razer, SteelSeries)

### Features
- Per-game profiles (auto-switching)
- Scheduled performance modes
- Cloud config sync
- Web UI for remote control
- System tray mini-dashboard

### Architecture
- Plugin system for hardware providers
- REST API for third-party integrations
- Event-driven architecture refactor
- Separate UI and service processes

---

**Last Updated**: 2025-12-10
**Target Release Date**: TBD (after user confirms initial error details)
