# OmenCore v3.0.1 — Optimization Opportunities

**Bloatware Manager & Memory Optimizer Enhancements**

---

## 🗑️ Bloatware Manager Optimizations

### Current State
✅ **Strengths:**
- Comprehensive multi-source scanning (AppX, Win32, Startup, Scheduled Tasks)
- Risk-based categorization with descriptions
- One-click bulk removal (low-risk items)
- Restore capability for AppX packages
- Category & risk filtering
- Beautiful UI with publisher truncation tooltips

⚠️ **Gaps:** Some features could improve user safety and convenience

---

### Enhancement 1: Bulk Restore Capability
**Priority:** MEDIUM | **Effort:** 1 day | **Impact:** MEDIUM

**Current:** Only restore single selected item  
**Issue:** Users who removed multiple items must restore one-by-one (tedious)

**Proposed Implementation:**
```csharp
// In BloatwareManagerViewModel
public async Task RestoreAllRemovedAsync()
{
    var removedApps = AllApps.Where(a => a.IsRemoved && a.CanRestore).ToList();
    if (!removedApps.Any()) return;
    
    var result = MessageBox.Show(
        $"Restore {removedApps.Count} removed items?\n\nThis will reinstall the packages.",
        "Restore All",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);
    
    if (result != MessageBoxResult.Yes) return;
    
    try
    {
        IsProcessing = true;
        BulkRestoreProgress = 0;
        BulkRestoreTotal = removedApps.Count;
        
        int count = 0;
        foreach (var app in removedApps)
        {
            count++;
            BulkRestoreProgress = count;
            StatusMessage = $"Restoring {count}/{removedApps.Count}: {app.Name}...";
            await _service.RestoreAppAsync(app);
        }
        
        StatusMessage = $"Restored {count} items";
        UpdateCounts();
    }
    finally
    {
        IsProcessing = false;
        BulkRestoreProgress = 0;
    }
}
```

**UI Addition:** "🔄 Restore All Removed" button (visible only when removed items exist)

**Benefits:**
- Undo bulk removal in one operation
- Similar UX to bulk remove

---

### Enhancement 2: Backup Management UI
**Priority:** LOW | **Effort:** 2 days | **Impact:** LOW

**Current:** Backups stored silently; no visibility or cleanup option  
**Issue:** Users don't know backup exists or how much disk space used

**Proposed Implementation:**
```csharp
// Add to BloatwareManagerViewModel
public class BackupInfo
{
    public long SizeBytes { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedDate { get; set; }
    public string FormattedSize => (SizeBytes / 1024.0 / 1024.0).ToString("F1") + " MB";
}

public BackupInfo? BackupStats { get; private set; }

private void UpdateBackupStats()
{
    var backups = _service.GetBackupStats();
    BackupStats = backups;
    OnPropertyChanged(nameof(BackupStats));
}

public async Task ClearAllBackupsAsync()
{
    var result = MessageBox.Show(
        $"Clear all backups ({BackupStats?.FormattedSize})?\n\n" +
        "You won't be able to restore removed items.",
        "Clear Backups",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);
    
    if (result == MessageBoxResult.Yes)
    {
        _service.ClearAllBackups();
        UpdateBackupStats();
    }
}
```

**UI Addition:** Show in status area:
```
🗄️ Backups: 45.3 MB (8 items) [Clear Backups button]
```

**Benefits:**
- Transparency about disk usage
- User control over backup storage
- Privacy awareness (can delete unused backups)

---

### Enhancement 3: Pre-Scan Safety Recommendations
**Priority:** MEDIUM | **Effort:** 3 hours | **Impact:** MEDIUM

**Current:** Risk rating shown only after scan  
**Issue:** Users unfamiliar with each app might hesitate to remove

**Proposed Implementation:**
```csharp
// In BloatwareManagerService
public class RemovalRecommendation
{
    public string PackageName { get; set; }
    public string Reason { get; set; }  // "Free disk space", "High conflict rate"
    public int ConfidencePercent { get; set; } // 0-100
}

public List<RemovalRecommendation> GetRemovalRecommendations()
{
    var recommendations = new List<RemovalRecommendation>();
    
    // Known unnecessary bloatware
    var knownUnnecessary = new[] { "Cortana", "Xbox", "Skype" };
    
    foreach (var app in _detectedApps)
    {
        if (knownUnnecessary.Any(x => app.Name.Contains(x, StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add(new RemovalRecommendation
            {
                PackageName = app.Name,
                Reason = "Widely considered unnecessary; rarely used",
                ConfidencePercent = 95
            });
        }
        
        if (app.RemovalRisk == RemovalRisk.Low)
        {
            recommendations.Add(new RemovalRecommendation
            {
                PackageName = app.Name,
                Reason = "Low risk; safe to remove",
                ConfidencePercent = 90
            });
        }
    }
    
    return recommendations.OrderByDescending(r => r.ConfidencePercent).ToList();
}
```

**UI Addition:** Post-scan card:
```
💡 Quick Recommendations

✓ Xbox App                  95% safe to remove  (unused on gaming PC)
✓ Cortana                   95% safe to remove  (useless on gaming laptop)  
✓ Mobile Device Manager     85% safe to remove  (rarely needed)

[Remove All Recommended]
```

**Benefits:**
- Guides new users
- Reduces decision paralysis
- Higher confidence in choices

---

### Enhancement 4: Scheduled Scan Background Task
**Priority:** LOW | **Effort:** 4 hours | **Impact:** LOW

**Current:** User must manually scan each time  
**Issue:** Bloatware can be installed; users might not check regularly

**Proposed Implementation:**
```csharp
public class BackgroundScanSettings
{
    public bool Enabled { get; set; }
    public int IntervalDays { get; set; } = 7;
    public TimeSpan ScheduledTime { get; set; } = new TimeSpan(3, 0, 0); // 3 AM
}

public void SetBackgroundScan(bool enabled, int intervalDays, TimeSpan? time = null)
{
    _backgroundScanEnabled = enabled;
    _backgroundScanInterval = Math.Clamp(intervalDays, 1, 30);
    _backgroundScanTime = time ?? new TimeSpan(3, 0, 0);
    
    if (enabled)
    {
        _backgroundScanTimer?.Dispose();
        ScheduleNextScan();
        _logger.Info($"Background scan scheduled every {intervalDays} day(s) at {_backgroundScanTime}");
    }
    else
    {
        _backgroundScanTimer?.Dispose();
    }
}

private void ScheduleNextScan()
{
    var now = DateTime.Now;
    var scanTime = now.Date.Add(_backgroundScanTime);
    
    if (scanTime <= now)
        scanTime = scanTime.AddDays(_backgroundScanInterval);
    
    var delay = scanTime - now;
    _backgroundScanTimer = new Timer(_ => TriggerBackgroundScan(), null, delay, TimeSpan.FromDays(_backgroundScanInterval));
}

private async void TriggerBackgroundScan()
{
    _logger.Info("Running scheduled background bloatware scan...");
    var apps = await ScanForBloatwareAsync();
    
    var newBloatware = apps.Where(a => !a.IsRemoved && a.RemovalRisk == RemovalRisk.Low).ToList();
    if (newBloatware.Any())
    {
        _logger.Warn($"Found {newBloatware.Count} low-risk removable items");
        // Notify via tray balloon or log
    }
}
```

**UI Addition:** Settings section in Advanced tab:
```
⏰ Background Bloatware Scan

[Toggle] Scan automatically
Every [7] days at [03:00] AM
```

**Benefits:**
- Proactive bloatware detection
- Users stay informed without manual checks

---

## 💾 Memory Optimizer Enhancements

### Current State
✅ **Strengths:**
- 8 safe memory cleaning operations
- Auto-clean (threshold-based)
- Interval-clean (periodic)
- Live memory statistics
- Non-blocking async operations

⚠️ **Gaps:** User experience could be refined

---

### Enhancement 1: Memory Cleaning Profiles
**Priority:** HIGH | **Effort:** 4 hours | **Impact:** HIGH

**Current:** User selects individual options (overwhelms non-technical users)  
**Issue:** Checkboxes are confusing; users don't know optimal combination

**Proposed Implementation:**
```csharp
public enum MemoryCleanProfile
{
    /// <summary>Safe working set cleanup only (minimal impact)</summary>
    Conservative = MemoryCleanFlags.WorkingSet,
    
    /// <summary>Working set + file cache + standby (recommended)</summary>
    Balanced = MemoryCleanFlags.WorkingSet | MemoryCleanFlags.SystemFileCache | MemoryCleanFlags.StandbyList,
    
    /// <summary>All safe operations (full cleanup)</summary>
    Aggressive = MemoryCleanFlags.AllSafe
}

// In MemoryOptimizerViewModel
public string SelectedProfile
{
    get => _selectedProfile;
    set
    {
        _selectedProfile = value;
        OnPropertyChanged();
        // Update flags based on profile
    }
}

private async Task CleanWithProfileAsync(string profileName)
{
    var flags = profileName switch
    {
        "Conservative" => MemoryCleanProfile.Conservative,
        "Balanced" => MemoryCleanProfile.Balanced,
        "Aggressive" => MemoryCleanProfile.Aggressive,
        _ => MemoryCleanProfile.Balanced
    };
    
    var result = await _memoryService.CleanMemoryAsync((MemoryCleanFlags)flags);
    // ...
}
```

**UI Redesign:**
```
🧹 Memory Cleaner

Current Memory: 18.2 / 32.0 GB (57%)

[Profile Selection (Radio Buttons)]
○ Conservative  (Safe, light cleanup)
○ Balanced     ●  (Recommended, moderate cleanup)
○ Aggressive   (Full cleanup, may affect responsiveness)

[Clean Now] button

Last Cleaned: 2 hours ago (freed 2.8 GB)
Auto-clean: [Toggle] every 10 minutes when above 80%
```

**Benefits:**
- Simpler for regular users
- Advanced users can still access detailed options
- Clear guidance on recommended settings

---

### Enhancement 2: Estimated Freed Memory Preview
**Priority:** MEDIUM | **Effort:** 2 hours | **Impact:** MEDIUM

**Current:** Shows freed amount *after* cleanup  
**Issue:** Users don't know impact before clicking

**Proposed Implementation:**
```csharp
public class MemoryCleanPreview
{
    public long EstimatedFreeMB { get; set; }
    public int EnumeratedProcesses { get; set; }
    public long StandbyListMB { get; set; }
}

public MemoryCleanPreview PreviewMemoryCleaning(MemoryCleanFlags flags)
{
    var info = GetMemoryInfo();
    var preview = new MemoryCleanPreview();
    
    if (flags.HasFlag(MemoryCleanFlags.StandbyList))
    {
        // Estimate standby memory
        preview.StandbyListMB = Math.Max(0, info.TotalPhysicalMB - info.UsedPhysicalMB - 500);
    }
    
    if (flags.HasFlag(MemoryCleanFlags.WorkingSet))
    {
        // Rough estimate: 10-20% of used memory can be freed
        preview.EstimatedFreeMB = (long)(info.UsedPhysicalMB * 0.15);
    }
    
    preview.EnumeratedProcesses = Process.GetProcesses().Length;
    return preview;
}
```

**UI Addition:**
```
Click "Balanced" to clean and free approximately 2-4 GB
(actual amount depends on running processes)

[Clean Now]
```

**Benefits:**
- Sets expectations before cleanup
- Reduces user anxiety ("will it help?")
- Realistic impact display

---

### Enhancement 3: Process Memory Ranking
**Priority:** MEDIUM | **Effort:** 1 day | **Impact:** MEDIUM

**Current:** Shows system-wide memory stats; no per-process breakdown  
**Issue:** Users can't identify memory hogs; poor understanding of bottlenecks

**Proposed Implementation:**
```csharp
public class ProcessMemoryInfo
{
    public string ProcessName { get; set; }
    public long WorkingSetMB { get; set; }
    public long PrivateMemoryMB { get; set; }
    public double MemoryPercent { get; set; }
}

public List<ProcessMemoryInfo> GetTopMemoryHogs(int count = 10)
{
    var processes = Process.GetProcesses()
        .Where(p => {
            try { return p.WorkingSet64 > 0; }
            catch { return false; }
        })
        .Select(p => new ProcessMemoryInfo
        {
            ProcessName = p.ProcessName,
            WorkingSetMB = p.WorkingSet64 / 1024 / 1024,
            PrivateMemoryMB = p.PrivateMemorySize64 / 1024 / 1024,
            MemoryPercent = (p.WorkingSet64 * 100.0) / GetMemoryInfo().TotalPhysicalMB
        })
        .OrderByDescending(x => x.WorkingSetMB)
        .Take(count)
        .ToList();
    
    return processes;
}
```

**UI Addition:** New section in Memory Optimizer:
```
Top Memory Consumers

1. Chrome.exe          4.2 GB (13%) ⬆ Rising
2. Visual Studio       3.1 GB (10%)
3. Discord            1.8 GB (6%)
4. Explorer.exe        1.2 GB (4%)
5. Game.exe           8.9 GB (28%) 🎮 Gaming

💡 Desktop apps don't release memory automatically. 
   Consider closing unused apps before cleaning.
```

**Benefits:**
- Identifies actual problem areas
- Educates users on memory usage
- Shows if cleaning will actually help

---

### Enhancement 4: Game Mode Integration
**Priority:** MEDIUM | **Effort:** 3 hours | **Impact:** MEDIUM

**Current:** Auto-clean/interval-clean runs even during gaming  
**Issue:** Interrupts FPS by triggering memory cleanup mid-game

**Proposed Implementation:**
```csharp
public void EnableGameModeAwareness(bool enabled)
{
    _gameModeAware = enabled;
    
    if (enabled)
    {
        // Register for game mode notifications
        SubscribeToGameModeChanges();
        _logger.Info("Game mode awareness enabled");
    }
}

private void OnGameModeChanged(bool isGamingNow)
{
    if (isGamingNow)
    {
        _autoCleanSuspended = true;
        _intervalCleanSuspended = true;
        _logger.Info("Auto-clean suspended (game mode active)");
    }
    else
    {
        _autoCleanSuspended = false;
        _intervalCleanSuspended = false;
        _logger.Info("Auto-clean resumed (game mode ended)");
    }
}

private async void TryRunScheduledClean(string statusMessage)
{
    if (_autoCleanSuspended || _intervalCleanSuspended)
    {
        _logger.Info($"Skipping scheduled clean (game mode active)");
        return;  // Don't clean during gaming
    }
    
    // ... normal cleanup logic
}
```

**UI Addition:** Settings checkbox:
```
☑ Pause auto-clean during gaming (Game Mode aware)
```

**Benefits:**
- No FPS drops during gameplay
- Smart deferral until gaming ends
- Better gaming experience

---

### Enhancement 5: Memory Cleaning Strategy Profiles
**Priority:** LOW | **Effort:** 4 hours | **Impact:** LOW

**Current:** Fixed 30-second check interval for auto-clean  
**Issue:** Too frequent for some, too infrequent for others

**Proposed Implementation:**
```csharp
public enum AutoCleanStrategy
{
    /// <summary>Check every 1 minute; clean when exceeds threshold</summary>
    Lightweight,
    
    /// <summary>Check every 30 seconds; default strategy</summary>
    Balanced,
    
    /// <summary>Check every 10 seconds; aggressive threshold policing</summary>
    Performance,
    
    /// <summary>Disabled; manual cleaning only</summary>
    Manual
}

public void SetAutoCleanStrategy(AutoCleanStrategy strategy)
{
    var checkInterval = strategy switch
    {
        AutoCleanStrategy.Lightweight => TimeSpan.FromSeconds(60),
        AutoCleanStrategy.Balanced => TimeSpan.FromSeconds(30),
        AutoCleanStrategy.Performance => TimeSpan.FromSeconds(10),
        _ => TimeSpan.Zero
    };
    
    if (checkInterval == TimeSpan.Zero)
    {
        _autoCleanEnabled = false;
    }
    else
    {
        SetAutoClean(true, _autoCleanThresholdPercent);
        // Adjust timer interval
    }
}
```

**UI Addition:**
```
Auto-Clean Strategy

[Dropdown]
├ Lightweight (minimal overhead, checks every 1 min)
├ Balanced (default, checks every 30 sec)
├ Performance (aggressive, checks every 10 sec)
└ Manual (disabled, click to clean)
```

---

## 📊 Implementation Priority Ranking

| Feature | Component | Priority | Effort | Impact | Days |
|---------|-----------|----------|--------|--------|------|
| Memory Cleaning Profiles | Optimizer | 🔴 HIGH | 🟢 Low | 🔴 HIGH | 0.5 |
| Bulk Restore | Bloatware | 🟡 MEDIUM | 🟢 Low | 🟡 MEDIUM | 0.5 |
| Process Memory Ranking | Optimizer | 🟡 MEDIUM | 🟡 Medium | 🟡 MEDIUM | 1 |
| Memory Preview | Optimizer | 🟡 MEDIUM | 🟢 Low | 🟡 MEDIUM | 0.5 |
| Game Mode Integration | Optimizer | 🟡 MEDIUM | 🟡 Medium | 🟡 MEDIUM | 0.75 |
| Backup Management UI | Bloatware | 🟢 LOW | 🟡 Medium | 🟢 LOW | 0.5 |
| Pre-Scan Recommendations | Bloatware | 🟡 MEDIUM | 🟢 Low | 🟡 MEDIUM | 0.5 |
| Scheduled Scans | Bloatware | 🟢 LOW | 🟡 Medium | 🟢 LOW | 1 |
| Clean Strategies | Optimizer | 🟢 LOW | 🟡 Medium | 🟢 LOW | 1 |

---

## 🚀 Quick Wins (Next Update)

These can be implemented in **v3.0.2** (3-4 hour sprint):

1. **Memory Cleaning Profiles** — Simplify UI dramatically
2. **Bulk Restore** — Complete bloatware management symmetry
3. **Memory Process Ranking** — Show users what's eating RAM
4. **Memory Preview** — Set expectations before cleanup

**Total Effort:** ~2.5 days  
**Expected Impact:** HIGH user satisfaction improvement

---

## 📝 Summary

**Bloatware Manager** is feature-complete but could benefit from:
- Bulk restore (parity with bulk remove)
- Backup visibility (transparency)
- Initial recommendations (guidance)

**Memory Optimizer** is powerful but could benefit from:
- Profile simplification (UX clarity)
- Process rankings (user education)
- Game mode awareness (gaming experience)
- Cleanup preview (expectation setting)

Both tools are solid; enhancements are polish, not necessity.

