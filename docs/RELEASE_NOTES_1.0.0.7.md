# OmenCore v1.0.0.7 Release Notes

**Release Date:** December 2025  
**Type:** Bug Fix Release

---

## Overview

This is a stability release focused on fixing critical bugs in the game profile process monitoring system introduced in v1.0.0.6.

---

## 🐛 Bug Fixes

### Critical: Multi-Instance Game Crash
**Issue:** Running multiple instances of the same game (e.g., two Minecraft windows) caused an application crash due to dictionary key collision.

**Fix:** Switched from using process name as the dictionary key to using the unique Process ID. This allows tracking multiple instances of the same game independently.

### Thread Safety Improvements
**Issue:** The process tracking dictionary was not thread-safe, leading to potential race conditions when games launched or closed during the WMI polling interval.

**Fix:** Replaced `Dictionary<string, ProcessInfo>` with `ConcurrentDictionary<int, ProcessInfo>` for lock-free concurrent access.

### Process.StartTime Exception
**Issue:** Accessing `Process.StartTime` could throw an exception if the process exited between the WMI query and property access.

**Fix:** Added try-catch wrapper around `StartTime` access to gracefully handle processes that exit during the monitoring scan.

### Resource Cleanup
**Issue:** `ProcessMonitoringService` and `GameProfileService` were not being disposed when the application closed, potentially leaving WMI queries running.

**Fix:** Added `Dispose()` calls in `MainViewModel.Dispose()` to properly clean up monitoring services.

### Silent Save Failures
**Issue:** Fire-and-forget profile saves could fail silently without any indication to the user.

**Fix:** Added try-catch with logging for profile save operations so errors are recorded in the system log.

---

## 📦 Files Changed

| File | Changes |
|------|---------|
| `ProcessMonitoringService.cs` | ConcurrentDictionary, process ID key, StartTime try-catch |
| `MainViewModel.cs` | Added Dispose calls for monitoring services |
| `GameProfileService.cs` | Error handling for fire-and-forget saves |
| `VERSION.txt` | Updated to 1.0.0.7 |
| `OmenCoreApp.csproj` | AssemblyVersion/FileVersion 1.0.0.7 |

---

## 🔄 Upgrade Notes

- **Direct upgrade** from v1.0.0.6 - no migration required
- Game profiles created in v1.0.0.6 are fully compatible
- No configuration changes needed

---

## 📊 Technical Details

### Before (v1.0.0.6):
```csharp
private readonly Dictionary<string, ProcessInfo> _activeProcesses = new();
// Key: "RocketLeague.exe" - crashes if two instances
```

### After (v1.0.0.7):
```csharp
public ConcurrentDictionary<int, ProcessInfo> ActiveProcesses { get; } = new();
// Key: 12345 (Process ID) - unique per instance
```

---

## ⬆️ Download

- **GitHub Release:** [v1.0.0.7](https://github.com/theantipopau/omencore/releases/tag/v1.0.0.7)
- **Website:** [omencore.info](https://omencore.info)

---

## 🙏 Feedback

Report issues or suggestions on [GitHub Issues](https://github.com/theantipopau/omencore/issues).
