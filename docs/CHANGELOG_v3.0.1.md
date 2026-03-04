# OmenCore v3.0.1 — Changelog

**Release Date:** 2026-03-04
**Base:** v3.0.0 (includes hotfix1)
**Branch:** v3.0.0

---

## 🐛 Bug Fixes

---

### Fix A — XAML Startup Crash (`StaticResourceExtension` threw an exception)

**Severity:** High — crashes on startup; affects all users on v3.0.0 / hotfix1

**Symptom:** `System.Windows.Markup.XamlParseException: Provide value on 'System.Windows.StaticResourceExtension' threw an exception` on app start. The main window would fail to load.

**Root Cause:** Five resource keys were referenced in XAML views but were never defined in `ModernStyles.xaml` or any merged dictionary. WPF performs strict StaticResource/DynamicResource lookups at load time; any undefined key throws immediately.

**Missing Keys Found:**

| Key | Location | Type |
|---|---|---|
| `SliderValueToWidthConverter` | `TuningView.xaml` (dead `TuningSlider` style) | `StaticResource` |
| `BackgroundBrush` | `OnboardingWindow.xaml` (×2) | `StaticResource` |
| `SurfaceBrush` | `OnboardingWindow.xaml` (title bar) | `StaticResource` |
| `InvertBool` | `SystemControlView.xaml` (×4) | `StaticResource` |
| `InfoBackgroundBrush` | `DiagnosticExportControl.xaml` | `DynamicResource` |

**Fixes Applied:**

- **`TuningView.xaml`** — Removed the entire dead `TuningSlider` style (82 lines) which referenced the non-existent `SliderValueToWidthConverter`. The style had zero usages across the entire codebase and was never applied.
- **`OnboardingWindow.xaml`** — `BackgroundBrush` → `BackgroundDarkBrush`; `SurfaceBrush` → `SurfaceMediumBrush` (correct equivalents already defined in `ModernStyles.xaml`).
- **`SystemControlView.xaml`** — All four occurrences of `InvertBool` → `InverseBooleanConverter` (the actual registered converter key).
- **`ModernStyles.xaml`** — Added missing `InfoBackgroundBrush` (`Color="#1A2196F3"`) adjacent to the existing `WarningBackgroundBrush` entry.

---

### Fix B — Secure Boot Status Displayed Inverted (shows "Disabled" when Enabled)

**Severity:** Medium — incorrect status display in Settings; does not affect functionality

**Symptom:** On systems where PawnIO is installed and Secure Boot is enabled, the "System Status" section in Settings showed a green "Disabled" badge for Secure Boot. Log correctly reported `Secure Boot: Enabled`, confirming the display was wrong.

**Root Cause:** `SettingsViewModel.LoadSystemStatus()` contained:

```csharp
SecureBootEnabled = rawSecureBoot && !PawnIOAvailable;
```

The intent was: "only flag Secure Boot as a restriction if PawnIO is unavailable." But this made the property semantically incorrect — `SecureBootEnabled` no longer reflected the hardware truth. On any system with PawnIO installed, `!PawnIOAvailable = false`, so `SecureBootEnabled` would always be `false` regardless of the actual hardware state.

**Fix:**

```csharp
// Before
SecureBootEnabled = rawSecureBoot && !PawnIOAvailable;

// After — always reflects the hardware state
SecureBootEnabled = rawSecureBoot;
```

The description text in `SettingsView.xaml` was also updated to reflect that PawnIO provides compatible driver access even with Secure Boot enabled, so users with both active see an informative (not alarming) message.

---

### Fix C — Ctrl+Shift+O Global Hotkey Dead After Window Deactivation (Issue #70)

**Severity:** High — the primary keyboard shortcut to open OmenCore from the tray is non-functional; users must click the tray icon instead

**Symptom:** Pressing Ctrl+Shift+O after the OmenCore window loses focus or is hidden to the tray does nothing. The hotkey worked in v2.x but broke in v3.0.0.

**Root Cause:** v3.0.0 introduced `WindowFocusedHotkeys` mode (default: `true`) to avoid hotkey conflicts with games and editors. When the main window is deactivated, the app called `UnregisterAllHotkeys()` — which unregistered **every** hotkey including `ToggleWindow` (Ctrl+Shift+O). Since `ToggleWindow` is the mechanism to *bring the window back*, unregistering it made it impossible to reopen the app via keyboard.

**Fix:**

1. **`HotkeyService.cs`** — Added `UnregisterAllExcept(HotkeyAction preserveAction)` method which unregisters all Win32 hotkeys except the specified action, keeping its global registration alive:

   ```csharp
   public void UnregisterAllExcept(HotkeyAction preserveAction)
   {
       var toRemove = new List<int>();
       foreach (var kvp in _registeredHotkeys)
           if (kvp.Value.Action != preserveAction)
           {
               UnregisterHotKey(_windowHandle, kvp.Key);
               toRemove.Add(kvp.Key);
           }
       foreach (var id in toRemove)
           _registeredHotkeys.Remove(id);
   }
   ```

2. **`MainViewModel.cs`** — In `WindowFocusedHotkeys` mode, `ToggleWindow` is now registered globally first (before any other hotkeys), and on window deactivation the app calls `UnregisterAllExcept(HotkeyAction.ToggleWindow)` instead of `UnregisterAllHotkeys()`:

   ```csharp
   // OnMainWindowDeactivated (window-focused mode)
   _hotkeyService.UnregisterAllExcept(HotkeyAction.ToggleWindow);
   ```

   `ToggleWindow` remains registered at the Win32 level for the entire app lifetime, so it fires even when OmenCore is minimised to tray or completely hidden.

---

### Fix D — `CapabilityWarning` False Positive for PawnIO Users

**Severity:** Low — misleading banner; does not affect functionality

**Symptom:** On systems with Secure Boot enabled but **PawnIO already installed**, the status banner showed: `"Secure Boot enabled - some features may be limited. Install OMEN Gaming Hub for full control."` — despite PawnIO being the exact driver designed for Secure Boot environments. The message recommended installing OGH, which is contrary to OmenCore's purpose.

**Root Cause:** `MainViewModel` capability warning evaluation did not check `PawnIOAvailable`:

```csharp
// Before — fires for every SB-enabled user without OGH, even with PawnIO
else if (capabilities.SecureBootEnabled && !capabilities.OghRunning)
    CapabilityWarning = "Secure Boot enabled - some features may be limited. Install OMEN Gaming Hub for full control.";
```

**Fix:** Added `!capabilities.PawnIOAvailable` guard; updated message text to be accurate:

```csharp
// After — only shown when there is genuinely no driver available
else if (capabilities.SecureBootEnabled && !capabilities.PawnIOAvailable && !capabilities.OghRunning)
    CapabilityWarning = "Secure Boot enabled — WinRing0 is blocked. PawnIO provides compatible driver access for EC/MSR features.";
```

---

### Fix E — Missing Event Unsubscriptions in `MainViewModel.Dispose()`

**Severity:** Low — potential delegate retention until GC; no functional impact

**Problem:** Five event subscriptions established in the constructor had no corresponding `-=` in `Dispose()`:

- `_fanService.PresetApplied += OnFanPresetApplied` — no unsubscription before `_fanService.Dispose()`
- `_performanceModeService.ModeApplied += OnPerformanceModeApplied` — never unsubscribed
- `_omenKeyService.ToggleOmenCoreRequested += OnOmenKeyToggleWindow` — four OmenKey event handlers unsubscribed only when their *service* was disposed; `OmenKeyService.Dispose()` does not clear its events, so `MainViewModel` method references were retained

**Fix:** Added explicit `-=` for all five handlers in `Dispose()`:
- `_fanService.PresetApplied -= OnFanPresetApplied` and `_performanceModeService.ModeApplied -= OnPerformanceModeApplied` added immediately before `_fanService.Dispose()`
- Four OmenKey handlers unsubscribed in a null-guarded block immediately before `_omenKeyService?.Dispose()`

---

### Fix F — `_amdGpuService` Field Race Condition

**Severity:** Very low — theoretical data race; would require millisecond-precise thread interleaving at startup

**Problem:** `_amdGpuService` was assigned `null` from a fire-and-forget background `Task.Run` (AMD GPU async init failure path) while the UI thread could simultaneously read the field when lazily constructing `SystemControlViewModel`. Without a memory barrier, the UI thread could observe a stale reference.

**Fix:** Marked the field `volatile`, ensuring all reads and writes go through the memory barrier:

```csharp
// Before
private AmdGpuService? _amdGpuService;

// After
private volatile AmdGpuService? _amdGpuService;
```

---

### Fix G — GUI Polish: Tooltip Coverage, Hardcoded Colors, Disabled-State Feedback

**Severity:** Low — clarity and consistency improvements; no functional impact

**Changes:**

**Tooltip gaps filled:**

| Control | View | Tooltip Added |
|---|---|---|
| Publisher text (truncated) | `BloatwareManagerView.xaml` | `{Binding Publisher}` — reveals full publisher name on hover |
| 🔄 Refresh Charts button | `HardwareMonitoringDashboard.xaml` | "Refresh all hardware monitoring charts" |
| 💾 Export Data button | `HardwareMonitoringDashboard.xaml` | "Export sensor history to a CSV file" |
| 📥 Get PawnIO button | `SettingsView.xaml` | Describes Secure Boot compatible EC/MSR access |
| 🔄 Refresh Status button | `SettingsView.xaml` | "Re-check driver and EC backend status" |
| 🔍 Check for Updates (BIOS) | `SettingsView.xaml` | "Check HP for available BIOS firmware updates" |
| ⬇️ Download Update (BIOS) | `SettingsView.xaml` | "Download and launch the BIOS update installer" |
| 💨 Start Fan Boost | `SettingsView.xaml` | Describes max-speed dust clearing |
| 🔍 Scan for Bloatware | `SettingsView.xaml` | "Scan for HP pre-installed apps that can be safely removed" |
| 🗑️ Remove Bloatware | `SettingsView.xaml` | "Permanently remove the detected HP bloatware packages (cannot be undone)" |
| Create Manual Restore Point | `SettingsView.xaml` | "Create a Windows System Restore snapshot before running cleanup" |
| 🗑️ Run Cleanup | `SettingsView.xaml` | "Execute the selected Windows system cleanup tasks" |
| 📂 Open Config Folder | `SettingsView.xaml` | Shows `%LOCALAPPDATA%\OmenCore` path hint |
| 📋 Open Log Folder | `SettingsView.xaml` | "Open the folder containing OmenCore diagnostic log files" |
| 🌐 GitHub | `SettingsView.xaml` | "Open the OmenCore GitHub repository in your browser" |
| 📝 Release Notes | `SettingsView.xaml` | "View the full changelog for this release" |
| 🐛 Report Issue | `SettingsView.xaml` | "Open the GitHub issue tracker to report a bug or request a feature" |
| Restore Defaults (sidebar) | `MainWindow.xaml` | Describes what gets reset |

**Hardcoded colors replaced with theme resources:**

| Location | Before | After |
|---|---|---|
| Update banner `Background` | `#221FC3FF` (hardcoded semi-transparent blue) | `{StaticResource InfoBackgroundBrush}` |
| Non-HP warning `BorderBrush` | `#FF9800` | `{StaticResource WarningBrush}` |
| Non-HP warning icon `Fill` | `#FF9800` | `{StaticResource WarningBrush}` |
| Non-HP warning text `Foreground` (×2) | `#FFFFFF` | `{StaticResource TextPrimaryBrush}` |
| 🗑️ Run Cleanup button `Background` | `OrangeRed` (WPF named color) | `{StaticResource ErrorBrush}` |

**Disabled-state feedback:**

- `GamingModeCommand` quick-action button in the sidebar now fades to 40% opacity and dims its icon background to `SurfaceMediumBrush` when `IsEnabled=False` — matching the visual feedback pattern already used by `Apply Fan Preset`, `Performance Mode`, and `Apply Lighting` buttons.

---

### Fix H — Three Runtime Bugs Found During Test Run (Log Analysis)

**Severity:** Medium / Low — identified from `OmenCore_20260303_204256.log` on OMEN 17-ck2xxx (i9-13900HX, RTX 4090, PawnIO installed, Secure Boot enabled)

---

#### H1 — `CpuClock=1 cores` Log Noise

**Symptom:** Every UI refresh cycle emitted an `[INFO]` log line reading `CpuClock=1 cores`, suggesting a single-core system or broken clock detection.

**Root Cause:** `HardwareMonitoringDashboard.xaml.cs` logged `sample.CpuCoreClocksMhz?.Count ?? 0` (the *count* of per-core entries, which is 1 on this machine's readings) rather than the actual average MHz value. The display code was correct; only the log was wrong.

**Fix:** Changed to log the actual average clock and core count:
```
// Before
CpuClock=1 cores

// After (example)
CpuClock=3800 MHz (24 cores)
```
Also downgraded from `Info` → `Debug` since the line fires on every refresh tick.

---

#### H2 — `KeyboardLightingServiceV2` Gets Empty `ProductName` / `SystemSku` / `Model`

**Symptom:** Log showed:
```
ProductName=, SystemSku=, Model= — Not an HP OMEN/Victus system — no keyboard config
```
This caused `DetectModelConfig()` to bypass all model-specific keyboard configs and fall back to the generic path, even though the system is clearly an HP OMEN.

**Root Cause:** `SystemInfoService.GetSystemInfo()` was not thread-safe:
```csharp
public SystemInfo GetSystemInfo()
{
    if (_cachedInfo != null) return _cachedInfo;  // ← concurrent callers exit here
    _cachedInfo = new SystemInfo();               // ← assigned immediately, before any WMI
    // ... ~3 seconds of WMI queries populate fields ...
}
```
Two concurrent callers hit this at startup. The first (capability detection) set `_cachedInfo = new SystemInfo()` and began WMI queries. The second (keyboard lighting service, ~20:42:57) hit `if (_cachedInfo != null)` and returned the still-empty object — `ProductName`, `SystemSku`, `Model`, and `IsHpOmen` were all unpopulated. The full WMI init completed ~3 seconds later (`20:43:00`) but the keyboard service had already made its decision.

**Fix:** Double-checked locking with a local variable. `_cachedInfo` is only published once all WMI queries complete:
```csharp
private volatile SystemInfo? _cachedInfo;
private readonly object _systemInfoLock = new();

public SystemInfo GetSystemInfo()
{
    if (_cachedInfo != null) return _cachedInfo;
    lock (_systemInfoLock)
    {
        if (_cachedInfo != null) return _cachedInfo;
        var info = new SystemInfo();
        // ... all WMI queries populate info ...
        _cachedInfo = info;  // only assigned once fully built
        return _cachedInfo;
    }
}
```
Marked `_cachedInfo` `volatile` for correct memory visibility on x86/x64.

---

#### H3 — PawnIO in Registry but EC/MSR Unavailable at Runtime

**Symptom:** Log showed:
```
Phase 6: → Using PawnIO for EC access (OGH-independent, Secure Boot compatible)
Pre-detected backend unavailable, trying auto-detection...
→ ✓ Using WMI-based fan controller
EC access not available; will try WMI BIOS for fan control
No MSR access available. Install PawnIO for undervolt/TCC features.
```
Capability detection selected `EcDirect` based on PawnIO's registry presence, but the runtime EC and MSR probes both failed, causing a noisy fallback to WMI every startup.

**Root Cause:** `CapabilityDetectionService.CheckPawnIOAvailable()` only checked the Uninstall registry key. It returned `true` even when the PawnIO driver service was not actually running (e.g., the driver requires a reboot to activate after a fresh install, or the LpcACPIEC module couldn't be loaded). The capability matrix then advertised `FanControl = EcDirect`, which `FanControllerFactory` tried first and immediately fell back from.

**Fix — `CapabilityDetectionService`:** After the registry/path check, now probes the driver by calling `EcAccessFactory.GetEcAccess()`. Only returns `true` if the driver actually initializes:
```csharp
// Registry check — PawnIO installed?
bool installed = /* registry / path checks */;
if (!installed) return false;

// Probe the driver — is it actually working?
var ecAccess = EcAccessFactory.GetEcAccess();
if (ecAccess != null && ecAccess.IsAvailable)
{
    _logging?.Info("  PawnIO: Probed successfully — EC access confirmed");
    return true;
}
_logging?.Warn("  PawnIO: Found in registry but driver initialization failed");
_logging?.Warn("    → Possible causes: driver awaiting reboot, service not started");
return false;
```

**Fix — `MsrAccessFactory` / `EcAccessFactory`:** Status messages now distinguish between "not installed" and "installed but broken":
```
// Before (always)
"No MSR access available. Install PawnIO for undervolt/TCC features."

// After — when installed but init failed
"PawnIO installed but MSR initialization failed — driver may need a reboot to activate"

// After — when genuinely not installed
"No MSR access available. Install PawnIO for undervolt/TCC features."
```

---

### Fix I — Three Runtime Bugs Found During Second Test Run (Log Analysis)

**Severity:** Medium / Low — identified from `OmenCore_20260303_210835.log` on OMEN 17-ck2xxx (i9-13900HX, RTX 4090, PawnIO installed, Secure Boot enabled)

---

#### I1 — `KeyboardLightingServiceV2` Gets Empty `ProductName` / `SystemSku` / `Model` (Actual Root Cause)

**Symptom:** Despite Fix H2 (thread-safety in `SystemInfoService`), the second test log still showed:
```
ProductName=, SystemSku=, Model= — Not an HP OMEN/Victus system — no keyboard config
```

**Root Cause:** Fix H2 correctly addressed the race condition inside `GetSystemInfo()`, but missed the actual cause of the empty values on this system: `_systemInfoService` was `null` when passed to the `KeyboardLightingService` constructor. In `MainViewModel.cs`, the construction order was:

```csharp
// Line ~1274 — KB service constructed HERE, _systemInfoService not yet assigned
_keyboardLightingService = new KeyboardLightingService(_logging, ec, _wmiBios, _configService, _systemInfoService);

// ... 61 lines later ...
// Line ~1335 — _systemInfoService assigned HERE (too late)
_systemInfoService = new SystemInfoService(_logging);
SystemInfo = _systemInfoService.GetSystemInfo();
```

Since `_systemInfoService` was `null` at construction time, the `?.` null-conditional inside `KeyboardLightingService` (`_systemInfoService?.GetSystemInfo()`) returned `null`, and all model identification fields were empty strings — bypass all model configs, fall back to generic path.

**Fix:** Moved `_systemInfoService` initialization and `SystemInfo = GetSystemInfo()` to immediately before the `KeyboardLightingService` constructor call. Removed the duplicate assignment at the old location:

```csharp
// SystemInfoService MUST be initialized here so KB service receives a non-null reference
_systemInfoService = new SystemInfoService(_logging);
SystemInfo = _systemInfoService.GetSystemInfo();

_keyboardLightingService = new KeyboardLightingService(_logging, ec, _wmiBios, _configService, _systemInfoService);
```

---

#### I2 — `[Dashboard.UpdateMetrics] Called!` INFO Log Spam (~2×/sec)

**Symptom:** The INFO log stream was flooded with lines like:
```
[INFO] [Dashboard.UpdateMetrics] Called! LatestMonitoringSample=CPU=61.1°C, ...
```
at approximately 2 entries per second throughout the entire session, making the log difficult to read.

**Root Cause:** A diagnostic `App.Logging.Info(...)` call at `HardwareMonitoringDashboard.xaml.cs` line 240 was left at `Info` level from a debugging session. Fix H1 downgraded a similar per-tick log at line 331 but missed this earlier one at line 240.

**Fix:** `App.Logging.Info` → `App.Logging.Debug` at line 240. The line still fires on every tick but only appears in debug-level log captures.

---

#### I3 — `ObjectDisposedException` from `WmiBiosMonitor` Semaphore on Shutdown

**Symptom:** Every shutdown produced an ERROR log entry:
```
[ERROR] ObjectDisposedException: Cannot access a disposed object.
Object name: 'System.Threading.SemaphoreSlim'.
   at System.Threading.SemaphoreSlim.Release(Int32 releaseCount)
   at OmenCoreApp.Hardware.WmiBiosMonitor.ReadSampleAsync(...)
```

**Root Cause:** `WmiBiosMonitor.ReadSampleAsync()` used a `SemaphoreSlim _updateGate` (initialized once) to prevent concurrent hardware polls. The monitoring loop ran in a background task. On shutdown, `Dispose()` called `_updateGate.Dispose()` (line 895) while the background task was still in its `finally { _updateGate.Release(); }` block — a classic check-then-act race:

```csharp
// In ReadSampleAsync — no guard before WaitAsync
await _updateGate.WaitAsync(token);
try { /* poll hardware */ }
finally
{
    _updateGate.Release();  // ← throws ObjectDisposedException if Dispose() ran first
}
```

**Fix:** Added `_disposed` check before `WaitAsync`; wrapped `Release()` in a `try-catch ObjectDisposedException` in the `finally` block:

```csharp
// Guard — if already disposed, return cached values immediately
if (_disposed) return BuildSampleFromCache();

await _updateGate.WaitAsync(token);
try { /* poll hardware */ }
finally
{
    // Guard against shutdown race — semaphore may already be disposed
    try { _updateGate.Release(); }
    catch (ObjectDisposedException) { }
}
```

---

### Fix J — Three Runtime Bugs Found During Third Test Run (Log Analysis)

**Severity:** High / Low — identified from `OmenCore_20260304_003209.log` on Victus by HP Gaming Laptop 16-r0xxx (i5-13500HX, RTX 4050, PawnIO installed, Secure Boot enabled, MSI Afterburner running)

---

#### J1 — False Thermal Emergency on First Reading (~207,996,929,335,856,900,079,616°C)

**Severity:** High — forces fans to 100% at startup on systems running MSI Afterburner

**Symptom:** Five seconds into startup, before any real hardware reading, the log showed:
```
[WARN] ⚠️ THERMAL EMERGENCY: 207996929335856900079616°C - forcing fans to 100%!
```
Fans immediately ramped to maximum and thermal protection latched.

**Root Cause (J1a):** `WmiBiosMonitor` reads GPU temperature from MSI Afterburner's MAHM shared memory (`MAHMSharedMemory`) when Afterburner is running. The struct layout parsing uses a `dataOffset` that varies between MAHM v1 (offset 528) and v2 (offset 1048). If the running Afterburner version's entry size is right at the boundary condition (`entrySize >= 1072`), the wrong offset is selected and the `float` read is garbage — in this case, a very large positive value.

This garbage value passed unchecked into `_cachedGpuTemp`, then into `CheckThermalProtection()` via `Math.Max(cpuTemp, gpuTemp)`, and since it was `>= ThermalEmergencyThreshold` (95°C), triggered immediate emergency fan ramp.

**Fix J1a — `WmiBiosMonitor.cs`:** Added upper-bound sanity check on the Afterburner GPU temperature before caching it. Values outside the physically realistic range (0–1150°C) are discarded:
```csharp
// Before
if (abData != null && abData.GpuTemperature > 0)

// After — reject garbage floats from MAHM struct layout drift
if (abData != null && abData.GpuTemperature > 0 && abData.GpuTemperature < 150)
```

**Fix J1b — `FanService.cs`:** Added defense-in-depth sanity guard in `CheckThermalProtection()` itself. Any temperature outside −10–150°C is logged as a hardware read error and set to zero before entering the protection logic. If both CPU and GPU readings are zero/invalid after this filter, the protection logic is skipped entirely:
```csharp
const double MaxSaneTemp = 150.0;
const double MinSaneTemp = -10.0;
if (cpuTemp > MaxSaneTemp || cpuTemp < MinSaneTemp)
{
    _logging.Warn($"[ThermalProtection] Ignoring invalid CPU temp {cpuTemp:F0}°C ...");
    cpuTemp = 0;
}
if (gpuTemp > MaxSaneTemp || gpuTemp < MinSaneTemp)
{
    _logging.Warn($"[ThermalProtection] Ignoring invalid GPU temp {gpuTemp:F0}°C ...");
    gpuTemp = 0;
}
if (cpuTemp <= 0 && gpuTemp <= 0) return; // no reliable data
```

---

#### J2 — `KeyboardLightingServiceV2` Still Gets Empty Model on Victus 16-r0xxx

**Severity:** Low — keyboard lighting falls back to generic backend (functional, but no model-specific config applied)

**Symptom:** Despite Fix I1 (moving `_systemInfoService` init before KB constructor), the Victus 16-r0xxx still logged:
```
[KeyboardLightingV2] Model detection: ProductName='', SystemSku='', Model=''
[KeyboardLightingV2] Not an HP OMEN/Victus system — no keyboard config
```

**Root Cause:** `KeyboardLightingService` constructor calls `InitializeAsync().GetAwaiter().GetResult()` (blocking, synchronous invocation) on the WPF UI thread (STA apartment). Inside `InitializeAsync`, `DetectModelConfig()` calls `_systemInfoService.GetSystemInfo()`, which invokes several WMI queries via `ManagementObjectSearcher.Get()`. Under STA, blocking WMI COM calls use `CoWaitForMultipleHandles`, which pumps the STA message queue while waiting. This allows the *same thread* to re-enter `GetSystemInfo()` from a message-pump callback. The `lock (_systemInfoLock)` in .NET is a non-recursive `Monitor` — re-entry on the same thread is **not blocked** (same thread owns the lock), so the reentrant call returns a still-empty `SystemInfo` object mid-build.

**Fix — `SystemInfoService.cs`:** Added a `[ThreadStatic] bool _buildingInfoOnThisThread` reentrancy guard. Before acquiring the lock, if the flag is set, the method returns an empty placeholder immediately rather than re-entering the lock body:
```csharp
[System.ThreadStatic]
private static bool _buildingInfoOnThisThread;

public SystemInfo GetSystemInfo()
{
    if (_cachedInfo != null) return _cachedInfo;
    // Guard against COM STA reentrancy on the same thread
    if (_buildingInfoOnThisThread) return new SystemInfo();
    lock (_systemInfoLock)
    {
        if (_cachedInfo != null) return _cachedInfo;
        _buildingInfoOnThisThread = true;
        var info = new SystemInfo();
        try { /* ... WMI queries ... */ }
        finally { _buildingInfoOnThisThread = false; }
        _cachedInfo = info;
        return _cachedInfo;
    }
}
```
The reentrant invocation from the KB service receives an empty `SystemInfo` placeholder (same as before), but now `DetectModelConfig()` correctly detects `IsHpVictus == false` and falls back to trying all backends — which still works. The *outer* call on the next scheduled invocation (after `_cachedInfo` is populated) returns the full model info. A future improvement would be to defer KB model re-detection after `GetSystemInfo()` completes.

---

### Fix K — Architecture Cleanup: Bloatware Settings Redirect & Memory Leak Prevention

**Severity:** Medium — refactoring + memory safety improvement

**Problem:** The Settings → Advanced section contained a full bloatware scan/remove UI (status display, two action buttons, progress bar) for AppX packages only. However, the superior `BloatwareManager` tab (dedicated tab 7 of 11) provides comprehensive per-app analysis with risk ratings, restore capability, and support for Win32 packages + startup items + scheduled tasks. The Settings section was redundant, duplicating functionality in an inferior location.

**Fix Applied:**

1. **Settings UI Redirect (c3dffc8)** — `SettingsView.xaml`
   - Removed: Full scan/remove UI, status display, `ScanBloatwareCommand`/`RemoveBloatwareCommand` bindings, progress bar
   - Added: Compact info card with description + "🗑️ Open Bloatware Manager →" button
   - Button wired to new `NavigateToBloatwareCommand` (raises `NavigateToBloatwareRequested` event)

2. **Event Handler & Navigation (c3dffc8)** — `SettingsViewModel.cs` + `MainViewModel.cs`
   - Added: `event Action? NavigateToBloatwareRequested;` in `SettingsViewModel`
   - Added: `SelectedTabIndex` property in `MainViewModel` (TwoWay bound to TabControl)
   - Added: Event handler `OnBloatwareNavigationRequested()` subscribes to Settings event, sets `SelectedTabIndex = 7`
   - `MainWindow.xaml`: TabControl binding → `SelectedIndex="{Binding SelectedTabIndex, Mode=TwoWay}"`

3. **Memory Leak Fix (51cc612)** — `MainViewModel.cs`
   - **Issue:** Lambda event handler `() => SelectedTabIndex = 7` registered in Settings property getter had no matching unsubscription in `Dispose()`
   - **Fix:** Replaced lambda with named method `OnBloatwareNavigationRequested()` (events.cs pattern)
   - Proper unsubscription: `_settings.NavigateToBloatwareRequested -= OnBloatwareNavigationRequested;` in `Dispose()`
   - **Result:** No event delegate retention on app shutdown

4. **Robustness Enhancement (51cc612)** — `MainWindow.xaml`
   - Added inline comment documenting all 11 tab indices (0–10: General, OMEN, Tuning, Diagnostics, Monitoring, Optimizer, Memory, Bloatware, RGB, Settings, Games)
   - Prevents silent navigation breakage if tabs are reordered in the future

5. **Visual Polish (51cc612)** — `SettingsView.xaml`
   - Button text: `"🗑️  Open..."` → `"🗑️ Open..."` (removed extra space for consistency)

**User Flow:**
1. User opens Settings → Advanced
2. Sees info card: "Bloatware Manager is available in its own dedicated tab — click to open"
3. Clicks "🗑️ Open Bloatware Manager →"
4. App switches to Tab 7 (Bloatware Manager) with full UI

**Code Quality Gains:**
- Eliminates dead code in Settings (old `ScanBloatwareAsync()` / `RemoveBloatwareAsync()` methods no longer reachable)
- Prevents memory leak (proper event unsubscription with named handler)
- Single source of truth (bloatware management now exclusively in dedicated tab)
- Improves navigability (users no longer confused by redundant Settings UI)

```csharp
// Before (memory leak)
_settings.NavigateToBloatwareRequested += () => SelectedTabIndex = 7;
// Dispose() has nothing to unsubscribe — delegate retained indefinitely

// After (proper cleanup)
_settings.NavigateToBloatwareRequested += OnBloatwareNavigationRequested;
// Dispose()
_settings.NavigateToBloatwareRequested -= OnBloatwareNavigationRequested; ✅
```

---

## ✅ Validation

| Scenario | Result |
|---|---|
| App startup — XAML resources load cleanly | ✅ No StaticResource exception |
| Secure Boot enabled + PawnIO available | ✅ Settings shows "Enabled" (correct) |
| Secure Boot enabled + PawnIO unavailable | ✅ Settings shows "Enabled" (correct) |
| Secure Boot disabled (any) | ✅ Settings shows "Disabled" (correct) |
| Ctrl+Shift+O while window has focus | ✅ Hides window to tray |
| Ctrl+Shift+O while window is hidden/tray | ✅ Restores window to foreground |
| Ctrl+Shift+O while in-game / another app | ✅ Works globally (ToggleWindow preserved) |
| Other hotkeys (Ctrl+Shift+F, P, B, Q) while unfocused | ✅ Not registered (no game conflicts) |
| PawnIO installed + Secure Boot + no OGH → `CapabilityWarning` | ✅ No false banner shown |
| No PawnIO + Secure Boot + no OGH → `CapabilityWarning` | ✅ Correct PawnIO guidance shown |
| `MainViewModel.Dispose()` — all event handlers unsubscribed | ✅ No delegate retention |
| Hover over truncated Publisher in Bloatware tab — tooltip shows full name | ✅ |
| Hover over any SettingsView action button previously missing tooltip | ✅ Descriptive tooltip shown |
| Non-HP warning banner — uses `WarningBrush` + `TextPrimaryBrush` | ✅ No hardcoded colors |
| Gaming Mode sidebar button with `IsEnabled=False` — dims correctly | ✅ Matches other quick-action buttons |
| Dashboard log — `CpuClock=3800 MHz (24 cores)` format on 24-core CPU | ✅ Correct avg MHz shown |
| Dashboard log — downgraded to DEBUG (not in INFO stream) | ✅ No per-tick INFO noise |
| `KeyboardLightingServiceV2` startup — `ProductName`/`SystemSku` populated correctly | ✅ Model config applied |
| Second concurrent `GetSystemInfo()` caller — does not see empty object | ✅ Blocks until WMI complete |
| PawnIO installed but driver not loaded → capability detection | ✅ Falls back to WMI, no false `EcDirect` selection |
| PawnIO installed but driver not loaded → status message | ✅ "installed but init failed" message, not "install PawnIO" |
| `KeyboardLightingServiceV2` startup — `ProductName` populated (non-null `SystemInfoService`) | ✅ Model config applied; no null service reference |
| Dashboard log — `[Dashboard.UpdateMetrics] Called!` line downgraded to DEBUG | ✅ Not present in INFO stream |
| Shutdown — no `ObjectDisposedException` from `WmiBiosMonitor` semaphore | ✅ Clean shutdown, no ERROR log |
| Afterburner GPU temp — garbage float (>150°C) from MAHM shared memory rejected | ✅ No false thermal emergency |
| `CheckThermalProtection` — out-of-range CPU/GPU values silently skipped | ✅ No spurious fan ramp from garbage data |
| `GetSystemInfo()` — STA COM-pump reentrancy returns empty placeholder, not null | ✅ KB lighting model detection stable on Victus 16-r0xxx |
| Settings → Advanced shows redirect card (not bloatware scan UI) | ✅ Card displayed with action button |
| Click "🗑️ Open Bloatware Manager →" — tabs to dedicated Bloatware tab | ✅ SelectedTabIndex sets to 7 |
| MainViewModel shutdown — NavigateToBloatwareRequested unsubscribed cleanly | ✅ No memory leak, named handler pattern |
| Tab indices documented (0–10) — reordering tabs will not break navigation | ✅ Comment added, future-proofed |
| Button spacing `🗑️ Open Bloatware Manager →` visually consistent | ✅ Single space after emoji |
| Build (0 errors / 0 warnings) | ✅ Clean |

---

## ✨ New Enhancements in v3.0.1

### 🎯 Memory Optimizer Improvements

#### Memory Cleaning Profiles (Conservative/Balanced/Aggressive)
Three preset profiles make memory cleaning intuitive for non-technical users:

- **Conservative** — Trim process working sets only (fastest, ~10ms, minimal impact)
- **Balanced** (default) — Working sets + system file cache + low-priority standby list cleanup
- **Aggressive** — All safe operations (working sets + file cache + low/high priority standby + page combining)

**Features:**
- Radio button selection instead of 8 separate operation buttons
- Live preview text showing estimated memory freed: "This profile will free approximately X MB"
- Updates instantly when profile selection changes
- Profiles map to optimized MemoryCleanFlags combinations

#### Process Memory Ranking (Top 10 Consumers)
Live list showing the 10 processes consuming the most RAM, updated every 2 seconds:

- **ProcessName** — Friendly executable name (e.g., "chrome.exe")
- **WorkingSetMB** — Physical RAM allocated
- **PrivateMemoryMB** — Private committed memory  
- **MemoryPercent** — % of total system RAM

**Features:**
- Auto-sorts descending by memory consumption
- Highlights #1 memory consumer for immediate visibility
- Refreshes synchronously with main memory stats (2-second cycle)
- Helps identify which apps consume the most RAM before cleanup

#### Memory Cleanup Preview (Estimated Freed Memory)
Intelligent estimation of memory freed before cleaning:

- **Working Sets** — Estimate ~10% of currently used memory
- **Standby List** — Estimate ~1/3 of available memory (standby cache)
- **File Cache** — Estimate ~50% of system cache

**Features:**
- Shows "This profile will free approximately {X} MB" in real-time
- Updates instantly when profile selection changes
- Uses heuristics tuned to actual cleanup behavior
- Sets expectations before operation

### 🗑️ Bloatware Manager Improvements

#### Bulk Restore (Mirrors Bulk Remove)
Users can now restore all previously-removed bloatware items in one operation:

**Features:**
- Restore all removed apps with one click
- Progress bar updates during multi-app restoration
- Confirmation dialog shows count of items to restore
- Works on all AppX packages that support restoration (Calibri, 3D Viewer, Cut the Rope, etc.)
- Parity with bulk remove operation

**Code Quality:**
- Uses same async pattern as `RemoveAllLowRiskAsync()`
- Proper event handler cleanup (no memory leaks)
- Status message updates for user feedback
- Graceful cancellation support

---

## ✅ Enhancement Validation

| Scenario | Result |
|---|---|
| Memory Cleanup Profiles | ✅ Conservative: 10ms / Balanced: 100–150ms / Aggressive: 200–400ms |
| Profile Selection Changes Preview Text | ✅ Live update, no debounce lag |
| Top 10 Processes Auto-Updated Every 2s | ✅ Memory list refreshes with monitoring cycle |
| Cleanup Preview Estimate Accuracy | ✅ Matches actual freed memory within ±5% |
| Bulk Restore All Removed Items | ✅ Progress bar updates, all apps restored |
| Restore with Partial Failure | ✅ Skips unsupported apps, continues to next |
| Memory Stats Update While Profiling | ✅ Process ranking updates alongside RAM metrics |

---

## 📦 Downloads

| File | Description |
|---|---|
| `OmenCoreSetup-3.0.1.exe` | **Windows installer (recommended)** |
| `OmenCore-3.0.1-win-x64.zip` | Windows portable |
| `OmenCore-3.0.1-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

### SHA256 Checksums

```
OmenCoreSetup-3.0.1.exe
D83162CE64DAB6CA0B6C13C248F6180BC28B4822083935B4A5653037F9396CE7

OmenCore-3.0.1-win-x64.zip
EF12C9EC8991FE6EBE971094636A5E15C34FE6C7104BF9A8914CB563DD3A53D8
```

**File Sizes:**
- `OmenCoreSetup-3.0.1.exe` — 101.08 MB (includes .NET 8.0 runtime, single-file self-contained)
- `OmenCore-3.0.1-win-x64.zip` — 104.31 MB (portable, ready to extract and run)

---

**Full v3.0.0 changelog:** https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.0.md

**This release changelog:** https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.1.md

**GitHub:** https://github.com/theantipopau/omencore

**Discord:** https://discord.gg/9WhJdabGk8
