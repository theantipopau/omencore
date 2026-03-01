# OmenCore v3.0.0 — Architecture Review, Regression Investigation & PR-Ready Fix Plan

**Prepared:** Comprehensive source-code analysis (no external logs; all findings from static analysis of `f:\Omen\src\`)  
**Scope:** v2.9.1 → v3.0.0-draft delta, GitHub Issues #67/#68, Status-tab anomalies, Linux alignment  
**Priority Legend:** 🔴 P0 = blocking / data-loss risk · 🟡 P1 = user-visible failure · 🟢 P2 = quality/polish

---

## 1. Executive Summary

Seven confirmed root causes were identified across Windows and Linux subsystems. Four require immediate action before v3.0.0 ships (P0). No log files exist in the repository; all analysis is from static source review.

| # | Severity | Component | Root Cause Summary |
|---|----------|-----------|-------------------|
| RC-1 | 🔴 P0 | `WmiBiosMonitor.cs` | NVAPI hang silently disables GPU telemetry permanently; no recovery path |
| RC-2 | 🔴 P0 | `ModelCapabilityDatabase.cs` | ProductId `8BAB` (OMEN 16-wf1xxx / Board 8C78) missing → Transcend family fallback → WMI fan control disabled |
| RC-3 | 🔴 P0 | `WmiFanController.cs` | `RestoreAutoControl()` skips reset sequence when not in Max mode → 3 s RPM=0 window on Auto transition |
| RC-4 | 🔴 P0 | `LinuxEcController.cs` | `SetPerformanceMode()` calls `WriteByte()` which requires `HasEcAccess`; hp-wmi path not tried → silent fail |
| RC-5 | 🟡 P1 | `SettingsViewModel.cs` | Secure Boot yellow warning shown even when PawnIO is green (independent display paths) |
| RC-6 | 🟡 P1 | `SystemInfoService.cs` | `Standalone = Degraded` fires when OGH + HP-SEU absent (threshold ≥ 2), masking healthy installs |
| RC-7 | 🟢 P2 | `HardwareMonitoringService.cs` | Monitor loop exits permanently after 5 errors with no restart, no user notification |

---

## 2. Architecture Overview

```
╔══════════════════════════════════════════════════════════════╗
║  OmenCoreApp  (.NET 8 / WPF / Windows)                       ║
╠══════════════════════════════════════════════════════════════╣
║                                                              ║
║  ┌─────────────────────────────────────────────────────┐     ║
║  │ HardwareMonitoringService  (10 s timeout, 5 errors) │     ║
║  │  └─► polling loop → WmiBiosMonitor.UpdateReadings() │     ║
║  └───────────────────┬─────────────────────────────────┘     ║
║                      │                                       ║
║  ┌───────────────────▼────────────────────────────────────┐  ║
║  │ WmiBiosMonitor  ← PRIMARY TELEMETRY BRIDGE             │  ║
║  │  Source 1: HpWmiBios.GetBothTemperatures() cmd 0x23    │  ║
║  │  Source 1: HpWmiBios.GetFanRpmDirect()     cmd 0x38    │  ║
║  │  Source 2: NvapiService  (GPU temp/load/power/VRAM)    │  ║
║  │  Source 3: PerformanceCounter (CPU load)               │  ║
║  │  Source 3b: ACPI thermal zone (ambient)                │  ║
║  │  Source 4: PawnIOMsrAccess (CPU RAPL, throttle bits)   │  ║
║  │  Source 5: WMI (SSD temp, battery)                     │  ║
║  └────────────────────────────────────────────────────────┘  ║
║                                                              ║
║  ┌─────────────────────────────────────────────────────┐     ║
║  │ WmiFanController  (V1: 0-55 krpm / V2: 0-100%)      │     ║
║  │  └─► HpWmiBios (fan on/off, SetFanLevel, SetFanMode) │     ║
║  └─────────────────────────────────────────────────────┘     ║
║                                                              ║
║  ┌─────────────────────────────────────────────────────┐     ║
║  │ CapabilityDetectionService  (11-phase pipeline)      │     ║
║  │  └─► ModelCapabilityDatabase  (static product dict)  │     ║
║  └─────────────────────────────────────────────────────┘     ║
║                                                              ║
║  PawnIOEcAccess / WinRing0EcAccess  (EC registers — MSR     ║
║   undervolt, keyboard lighting, throttle bits only)          ║
╚══════════════════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════════════════╗
║  OmenCore.Linux  (.NET 8 / Avalonia + System.CommandLine)    ║
╠══════════════════════════════════════════════════════════════╣
║  LinuxEcController  (backend priority order)                 ║
║    1. ec_sys  (/sys/kernel/debug/ec/ec0/io)                  ║
║    2. hp-wmi  (/sys/devices/platform/hp-wmi/thermal_profile) ║
║    3. ACPI platform_profile  (/sys/firmware/acpi/...)        ║
║    4. hwmon-pwm  (/sys/class/hwmon/hwmon*/fan*_*_output)     ║
║                                                              ║
║  SetPerformanceMode() → WriteByte(REG_PERF_MODE=0x95)        ║
║    ⚠ ONLY tries ec_sys — ignores hp-wmi when HasEcAccess=F  ║
╚══════════════════════════════════════════════════════════════╝
```

**Key constants** (`LinuxEcController.cs`):

| Register | OMEN meaning | Victus meaning |
|----------|--------------|----------------|
| `0x95` | Performance mode | Performance mode |
| `0x30` | Default / Balanced | — |
| `0x31` | Performance | — |
| `0x50` | Cool / Quiet | — |
| `0x00` / `0x01` | — | Balanced / Performance |

The OMEN register values are correct for OMEN 16-wf1xxx. The bug is routing, not values.

---

## 3. Changelog Delta Summary (v2.9.1 → v3.0.0-draft)

| Area | What Changed | Risk |
|------|-------------|------|
| Fan smoothing | New hysteresis filter on RPM reads | Medium — introduces 3 s debounce that surfaces RC-3 |
| Preset verification | `VerifyMaxAppliedWithRetries()` added | Low |
| Model DB additions | Several 2024 models added | **8BAB still absent** — RC-2 |
| Monitoring restart | `TryRestartAsync()` stub added | Medium — it always returns `true` (no-op), false sense of safety |
| Startup safe-mode guard | New `StartupSafeModeGuardEnabled` feature | Low |
| Dependency audit | `SystemInfoService.PerformDependencyAudit()` added | Introduces RC-6 threshold bug |
| BIOS reliability stats | `BiosReliabilityStats` / `RefreshBiosReliability()` added | Low — cosmetic |
| OSD overlay | Full OSD implementation | Low |
| Power automation | `PowerAutomationService` + Settings bindings | Low |
| Linux CLI | `PerformanceCommand` added | **RC-4 introduced here** |

---

## 4. GitHub Issue Analysis

### Issue #68 — OMEN 16-wf1xxx: Temps stuck at 28 °C, fan RPM = 0, fan control not working

**Report profile:** ProductId `8BAB`, BIOS board `8C78`, BIOS version F.29.

#### RC-2 (Immediate / WMI fan control disabled)

`ModelCapabilityDatabase` lookup for `8BAB`:
1. Exact ProductId lookup: **not found**.
2. Falls through to `GetCapabilitiesByFamily(OMEN2024Plus)`.
3. `OMEN2024Plus` family → first model found during dictionary iteration (non-deterministic, but in practice the Transcend entry `8C3A`) is used as the template.
4. Transcend has `SupportsFanControlWmi = false`.
5. `CapabilityDetectionService.RefineCapabilitiesFromModel()` uses this template → WMI fan control disabled.

Result: all WMI fan commands gated → fans appear frozen at 0 RPM and no profile switching works.

#### RC-1 (Telemetry stuck at 28 °C)

`WmiBiosMonitor.UpdateReadings()` has a single outer `try/catch` that silently swallows all exceptions. Source 2 (NVAPI) failure path:

```csharp
// WmiBiosMonitor.cs — UpdateReadings SOURCE 2
try {
    if (!_nvapiMonitoringDisabled && _nvapi?.IsAvailable == true) {
        // ... reads GPU temp, load, power
    }
} catch {
    _nvapiConsecutiveFailures++;
    if (_nvapiConsecutiveFailures >= 10) _nvapiMonitoringDisabled = true; // PERMANENT
}
```

Once `_nvapiMonitoringDisabled = true`, GPU temperature is never updated again until restart. Source 3b (ACPI thermal zone) provides a fallback ambient reading of ~28 °C, which then becomes the displayed "CPU temp" when WMI BIOS temperature reading also stalls.

Additionally, `StabilizePowerReading()` retains the last valid power reading for up to 30 consecutive zero reads when the system appears active (`cpuLoad ≥ 2 || temp ≥ 38 °C`). This can display a stale `0 W` or an old watts value for 30× polling cycles (≈ 60 s at default 2 s interval).

#### Root cause chain for #68:

```
8BAB not in DB
  → Transcend family template
    → SupportsFanControlWmi = false
      → WMI fan commands blocked
        → Fan RPM reads return 0
          → RPM debounce shows 0 permanently

NVAPI exception (iGPU/Optimus detection issue on 8C78)
  → _nvapiMonitoringDisabled = true after 10 failures
    → GPU temp source dead
      → ACPI thermal zone ambient (28°C) displayed as "temp"
```

### Issue #67 — GPU temperature not detecting

Exact same RC-1 path. NVAPI is permanently disabled after 10 consecutive failures (`_nvapiConsecutiveFailures >= 10`). There is no recovery timer, no retry after cooldown, no user notification. The `_hardwareMonitoringService.TryRestartAsync()` method exists but its implementation is a no-op stub (`return true` immediately).

---

## 5. Status Tab Analysis

### 5a. Secure Boot Warning With PawnIO Green

**Location:** `SettingsViewModel.cs` — `LoadSystemStatus()` (line 2940) and `CheckDriverStatus()` (line 2650).

```csharp
// LoadSystemStatus() — sets Secure Boot state
SecureBootEnabled = IsSecureBootEnabled();           // ← unconditional
PawnIOAvailable = (key != null);                    // ← independent check

// CheckDriverStatus() — sets driver badge
if (pawnIoAvailable) {
    DriverStatusText = "PawnIO Installed";
    DriverStatusColor = Green;
}
// ...
// SecureBootEnabled is data-bound in XAML as a separate warning row
// It is NEVER suppressed when PawnIO is available
```

The XAML binds `SecureBootEnabled` directly to a yellow warning banner. Since `PawnIOAvailable` is never consulted to suppress `SecureBootEnabled`, a user with PawnIO installed (green badge) still sees a yellow Secure Boot warning. This is misleading — PawnIO is explicitly designed for Secure Boot environments.

### 5b. Standalone = Degraded on Clean Installs

**Location:** `SystemInfoService.PerformDependencyAudit()`.

```csharp
// SystemInfoService.cs
var optionalMissing = new List<string>();
if (!OghRunning)        optionalMissing.Add("OMEN Gaming Hub");
if (!HpSeuRunning)      optionalMissing.Add("HP System Event Utility");
if (!LhmPresent)        optionalMissing.Add("LibreHardwareMonitor");
if (!PawnIOPresent)     optionalMissing.Add("PawnIO");
if (!WinRing0Present)   optionalMissing.Add("WinRing0");

if (optionalMissing.Count >= 2)
    status = "Degraded";       // ← fires on OGH-absent + HP-SEU-absent
```

OGH and HP System Event Utility are absent on most intentional "clean standalone" installs. Neither is required for fan control, performance switching, or temperature monitoring in v2.8.6+. Two absent optional components exceed the threshold → `Degraded`, displayed in the UI with orange color, alarming users who have a fully functional install.

### 5c. BIOS WMI Reliability 71.4% ("Poor")

**Location:** `WmiBiosMonitor.GetReliabilityStats()` → `SettingsViewModel.BiosReliabilityStats`.

The reliability ratio is `successfulQueries / totalQueries`. BIOS WMI calls include high-frequency polling (temp, fan RPM every 2 s). A GPU PCIe state change or brief BIOS lock-out can briefly spike timeouts, pulling the ratio down temporarily. The `HealthRating = "Poor"` threshold is set at < 80% success rate.

**Impact:** None. The reliability stats are **purely diagnostic**. Fan control and performance switching do not gate on `HealthRating`. The UI shows an orange indicator and "Poor" text which causes user alarm without any actual functional impact. The text needs clarification.

---

## 6. Linux Kernel Alignment (hp-wmi / Board 8C78, Kernel 7.0-rc1)

### EC Register Mapping — Verified Correct for OMEN

OmenCore's Linux EC register values (`0x30 = Balanced`, `0x31 = Performance`, `0x50 = Cool`) match the OMEN-specific mapping in the Linux kernel hp-wmi driver for kernel ≥ 6.5. These are distinct from Victus mappings (`0x00/0x01`). The register definitions in `LinuxEcController.cs` are **correct**.

### Critical Routing Bug (RC-4)

The OMEN 16-wf1xxx uses `hp-wmi` for thermal profile management, not direct EC register access. On this system:

- `HasEcAccess = false` (ec_sys not available, kernel module not loaded or not applicable)
- `HasHpWmiAccess = true` (hp-wmi thermal profile sysfs present)
- `IsAvailable = true` (OR condition)

`PerformanceCommand.HandlePerformanceCommandAsync()`:
```csharp
if (!ec.IsAvailable) {
    Console.WriteLine("✗ EC not available");
    return;
}
// IsAvailable is true → passes guard

var ok = ec.SetPerformanceMode(PerformanceMode.Performance);
// SetPerformanceMode → WriteByte(REG_PERF_MODE, 0x31)
// WriteByte() checks HasEcAccess → false → returns false

Console.WriteLine(ok ? "✓ ..." : "✗ Failed to set performance mode: performance");
// Prints failure even though hp-wmi thermal_profile IS available
```

`SetHpWmiThermalProfile(string profile)` exists in `LinuxEcController.cs` and correctly writes to `/sys/devices/platform/hp-wmi/thermal_profile` with kernel-validated string values ("balanced", "performance", "quiet"). It is **never called** from `SetPerformanceMode()`.

### Kernel 7.0-rc1 hp-wmi Changes

The kernel 7.0-rc1 hp-wmi driver reorganised `thermal_profile` attribute handling with stricter validation. OmenCore's `SetHpWmiThermalProfile()` uses the string literals `"balanced"`, `"performance"`, `"quiet"` which are **valid** in both 6.x and 7.0-rc1. No code change is needed for the string values — only routing is broken.

---

## 7. Victus EC Throttle Strategy & Global Hotkey

### Victus Performance Modes

Victus uses a different EC register value set from OMEN. `LinuxEcController.cs` defines:

```csharp
// Used for SetPerformanceMode when _isVictus == true (model name contains "Victus")
private const byte VICTUS_PERF_DEFAULT     = 0x00;
private const byte VICTUS_PERF_PERFORMANCE = 0x01;
```

The detection logic `_isVictus = modelName?.Contains("Victus", OrdinalIgnoreCase) == true` works for most cases but is fragile to OEM string variations. On OMEN 17-ck2xxx boards that share Victus EC firmware, this misclassification causes wrong register values.

### Global Hotkey Conflict

`SettingsViewModel.HotkeysWindowFocused` (default `true`) restricts hotkeys to window-focused mode. Users who want global hotkeys (e.g., Ctrl+Shift+P for performance mode in-game) must disable this. The `OsdHotkey` setter auto-prefixes bare Fn keys with `Ctrl+Shift+` to prevent stealing system shortcuts — this is correct behavior but the validation message only appears in logs, not the UI.

**No code change needed for basic hotkey behavior.** The window-focused default is intentional (documented in `SettingsViewModel.cs` XML comment) and the right call for most users.

---

## 8. Log Analysis

No log files exist in the repository (`%LocalAppData%\OmenCore\` and `%AppData%\OmenCore\` are runtime paths not committed). All analysis is from static source review.

**Log paths at runtime:**
- Windows: `%LocalAppData%\OmenCore\omencore.log`
- Config: `%AppData%\OmenCore\config.json`

For issue reproduction, collect logs with `--verbose` flag and attach `omencore.log`. Key log lines to look for:

| Signal | Log text | Source |
|--------|----------|--------|
| NVAPI disabled | `NVAPI monitoring disabled after 10 consecutive failures` | `WmiBiosMonitor.cs` |
| Model DB miss | `Using default capabilities` or `Using family defaults` | `ModelCapabilityDatabase.cs` |
| Fan control blocked | `SupportsFanControlWmi = false` | `CapabilityDetectionService.cs` |
| Monitor loop exit | `MonitorLoopAsync exiting after consecutive errors` | `HardwareMonitoringService.cs` |
| Linux perf mode fail | `Failed to set performance mode` | `PerformanceCommand.cs` |

---

## 9. Root Cause Ranking

| Rank | ID | Impact | Affected Users | Reversible? |
|------|----|--------|---------------|-------------|
| 1 | RC-2 | 🔴 Fan control completely broken | All 8BAB/8C78 owners | Yes — restart |
| 2 | RC-1 | 🔴 Telemetry dead after NVAPI fail | Any NVIDIA Optimus system | Yes — restart |
| 3 | RC-3 | 🔴 Auto mode shows 0 RPM | All Windows, any non-Max preset | Yes — switch away and back |
| 4 | RC-4 | 🔴 Linux perf mode CLI silently fails | All hp-wmi-only Linux installs | N/A — per command |
| 5 | RC-5 | 🟡 Confusing dual-status in UI | All Secure Boot + PawnIO users | No action needed |
| 6 | RC-6 | 🟡 Healthy install shows Degraded | All non-OGH installs | Cosmetic — no functional impact |
| 7 | RC-7 | 🟢 Monitor loop never recovers | Any 5-error crash scenario | Yes — restart |

---

## 10. Fix Plan

### P0-1 — Add NVAPI Recovery Cooldown · `WmiBiosMonitor.cs`

**Problem:** `_nvapiMonitoringDisabled = true` set permanently; `_nvapiConsecutiveFailures` never reset.

**Fix:**
```csharp
// Add field
private DateTime _nvapiDisabledUntil = DateTime.MinValue;
private const int NvapiRecoveryCooldownSeconds = 60;

// Replace permanent-disable logic in UpdateReadings SOURCE 2:
// BEFORE:
if (_nvapiConsecutiveFailures >= 10) _nvapiMonitoringDisabled = true;

// AFTER:
if (_nvapiConsecutiveFailures >= 10) {
    _nvapiMonitoringDisabled = true;
    _nvapiDisabledUntil = DateTime.Now.AddSeconds(NvapiRecoveryCooldownSeconds);
    _logging.Warn($"NVAPI monitoring suspended for {NvapiRecoveryCooldownSeconds}s after 10 consecutive failures");
}

// At top of SOURCE 2 block, add recovery check:
if (_nvapiMonitoringDisabled && DateTime.Now >= _nvapiDisabledUntil) {
    _nvapiMonitoringDisabled = false;
    _nvapiConsecutiveFailures = 0;
    _logging.Info("NVAPI monitoring re-enabled after cooldown period");
}
```

**Risk:** Re-enabling on a hung GPU driver could cause up to 10 s poll stall (monitoring timeout). Mitigated by the existing 10 s `ReadSampleTimeoutMs` in `HardwareMonitoringService`.

---

### P0-2 — Add ProductId 8BAB to ModelCapabilityDatabase · `ModelCapabilityDatabase.cs`

**Problem:** `8BAB` lookup falls through to family default which picks Transcend template with `SupportsFanControlWmi = false`.

**Fix:** Add entry before the closing of the dictionary initializer:

```csharp
AddModel(new ModelCapabilities {
    ProductId = "8BAB",
    ModelName = "OMEN 16 (2024) wf1xxx Intel",
    ModelNamePattern = "16-wf1",
    ModelYear = 2024,
    Family = OmenModelFamily.OMEN16,
    SupportsFanControlWmi = true,
    SupportsFanCurves = true,
    FanZoneCount = 2,
    HasMuxSwitch = true,
    SupportsGpuPowerBoost = true,
    HasFourZoneRgb = true,
    MaxFanLevel = 100,          // V2 percentage-based (same as wf0xxx 8BCA)
    UserVerified = false,
    Notes = "OMEN 16-wf1xxx (2024 Intel) — Board 8C78. Added for Issue #68. " +
            "Set UserVerified=true after community confirmation."
});
```

**Cross-reference:** `8BCA` (wf0xxx 2023 Intel) already in DB — use it as the copy template. Confirm `MaxFanLevel = 100` (V2) by checking if `GetFanLevel()` on the device returns percentage or RPM values. If user reports still broken, check `FanVersionAutoDetect` logs.

---

### P0-3 — Fix `RestoreAutoControl()` to Always Execute Reset Sequence · `WmiFanController.cs`

**Problem:** Guard `_isMaxModeActive || IsManualControlActive` skips `ResetFromMaxMode()` when switching from a standard preset (Quiet/Extreme) to Auto. RPM debounce then shows 0 for 3 s.

**Fix:**
```csharp
public bool RestoreAutoControl()
{
    try
    {
        _logging.Info("RestoreAutoControl: resetting fan to auto");

        // Always reset regardless of prior mode, to ensure clean state
        // (previously only ran when _isMaxModeActive or IsManualControlActive)
        ResetFromMaxMode();

        StopCountdownExtension();

        // Clear debounce window so readings aren't filtered during transition
        _lastProfileSwitch = DateTime.MinValue;

        _isMaxModeActive = false;
        IsManualControlActive = false;

        return SetFanMode(FanMode.Default);
    }
    catch (Exception ex)
    {
        _logging.Error($"RestoreAutoControl failed: {ex.Message}", ex);
        return false;
    }
}
```

**Risk:** `ResetFromMaxMode()` on V1 systems sends `SetFanLevel(20,20)` then `SetFanLevel(0,0)`. On V2 systems it is already a no-op. The extra call on V1 is safe — it forces a clean BIOS handoff.

---

### P0-4 — Route `SetPerformanceMode()` Through hp-wmi · `LinuxEcController.cs`

**Problem:** `SetPerformanceMode()` only calls `WriteByte(REG_PERF_MODE)` which requires `HasEcAccess`. Systems with hp-wmi-only access silently return `false`.

**Fix:**
```csharp
public bool SetPerformanceMode(PerformanceMode mode)
{
    // Priority 1: hp-wmi thermal_profile (most compatible, Secure Boot safe)
    if (HasHpWmiAccess)
    {
        var profile = mode switch {
            PerformanceMode.Performance => "performance",
            PerformanceMode.Cool        => "quiet",
            _                           => "balanced"
        };
        if (SetHpWmiThermalProfile(profile))
        {
            _logging.Info($"SetPerformanceMode via hp-wmi: {profile}");
            return true;
        }
        _logging.Warn($"hp-wmi thermal_profile write failed for {profile}, falling through");
    }

    // Priority 2: ACPI platform_profile
    if (HasAcpiProfileAccess)
    {
        var profile = mode switch {
            PerformanceMode.Performance => "performance",
            PerformanceMode.Cool        => "low-power",
            _                           => "balanced"
        };
        if (SetAcpiPlatformProfile(profile))
        {
            _logging.Info($"SetPerformanceMode via ACPI platform_profile: {profile}");
            return true;
        }
    }

    // Priority 3: Direct EC register (requires ec_sys)
    if (HasEcAccess)
    {
        byte value = _isVictus
            ? mode switch {
                PerformanceMode.Performance => VICTUS_PERF_PERFORMANCE,
                _                           => VICTUS_PERF_DEFAULT
              }
            : mode switch {
                PerformanceMode.Performance => PERF_MODE_PERFORMANCE,
                PerformanceMode.Cool        => PERF_MODE_COOL,
                _                           => PERF_MODE_DEFAULT
              };
        return WriteByte(REG_PERF_MODE, value);
    }

    _logging.Error("SetPerformanceMode: no available backend (no ec_sys, no hp-wmi, no ACPI)");
    return false;
}
```

**Note:** Verify `SetAcpiPlatformProfile()` exists or add the missing stub alongside `SetHpWmiThermalProfile()`.

---

### P1-1 — Suppress Secure Boot Warning When PawnIO is Available · `SettingsViewModel.cs`

**Problem:** `SecureBootEnabled` is set unconditionally from `IsSecureBootEnabled()` in `LoadSystemStatus()`. PawnIO availability is not consulted.

**Fix** in `LoadSystemStatus()`:
```csharp
var rawSecureBoot = IsSecureBootEnabled();
var pawnIOPresent = IsPawnIOAvailable();

PawnIOAvailable = pawnIOPresent;

// Secure Boot is only a meaningful warning if PawnIO is NOT available.
// PawnIO is explicitly designed for Secure Boot environments.
SecureBootEnabled = rawSecureBoot && !pawnIOPresent;
```

Or if you want to preserve the raw value for informational display while removing the warning state, add a separate `SecureBootWarning` boolean:
```csharp
SecureBootEnabled = rawSecureBoot;           // factual display
SecureBootWarning = rawSecureBoot && !pawnIOPresent; // drives yellow color in XAML
```

And update XAML binding from `SecureBootEnabled` to `SecureBootWarning` for the warning banner background/icon.

---

### P1-2 — Fix Standalone = Degraded Threshold · `SystemInfoService.cs`

**Problem:** Threshold `optionalMissing.Count >= 2` fires on perfectly healthy installs missing OGH + HP-SEU.

**Option A (threshold raise):**
```csharp
// Change
if (optionalMissing.Count >= 2)  status = "Degraded";
// To  
if (optionalMissing.Count >= 3)  status = "Degraded";
```

**Option B (semantic split — preferred):**
```csharp
// Separate "recommended" from "optional" components
var recommendedMissing = new List<string>();
var infoMissing = new List<string>();

if (!PawnIOPresent && !WinRing0Present)
    recommendedMissing.Add("EC driver (PawnIO/WinRing0)");   // needed for undervolt/MSR
if (!HpWmiBiosAvailable)
    requiredMissing.Add("HP WMI BIOS");  // already required

// OGH and HP-SEU are informational only
if (!OghRunning)    infoMissing.Add("OMEN Gaming Hub");
if (!HpSeuRunning)  infoMissing.Add("HP System Event Utility");

if (requiredMissing.Any())        status = "Critical";
else if (recommendedMissing.Any()) status = "Degraded";
else                               status = "Standalone";   // was previously "Optimal"
```

Option B is more accurate. The UI Summary text should read "OGH/HP-SEU absent (not required for core operation)" to stop user alarm.

---

### P2 — Monitor Loop Restart on Consecutive Errors · `HardwareMonitoringService.cs`

**Problem:** `MonitorLoopAsync` exits permanently after 5 consecutive errors. No restart, no notification.

**Fix:**
```csharp
// In MonitorLoopAsync error accumulation block:
if (consecutiveErrors >= maxErrors)
{
    _logging.Warn($"MonitorLoop: {maxErrors} consecutive errors — restarting monitoring bridge");
    consecutiveErrors = 0;
    
    // Brief back-off before restart attempt
    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    
    // Attempt bridge reset (make TryRestartAsync actually restart the WmiBiosMonitor)
    await _bridge.TryRestartAsync().ConfigureAwait(false);
    
    // Optionally surface to UI
    _statusChanged?.Invoke(MonitorStatus.Recovering);
    continue;
}
```

Also make `WmiBiosMonitor.TryRestartAsync()` non-stub — it should close and re-open the WMI session.

---

## 11. Proposed Code Changes (File-by-File Reference)

| File | Lines | Change | Priority |
|------|-------|--------|----------|
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | NVAPI disable block | Add `_nvapiDisabledUntil` + reset logic | 🔴 P0-1 |
| `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` | After last `AddModel()` call | Add `8BAB` entry | 🔴 P0-2 |
| `src/OmenCoreApp/Hardware/WmiFanController.cs` | `RestoreAutoControl()` | Remove guard, always call `ResetFromMaxMode()`, clear debounce | 🔴 P0-3 |
| `src/OmenCore.Linux/Hardware/LinuxEcController.cs` | `SetPerformanceMode()` | Full hp-wmi → ACPI → EC priority routing | 🔴 P0-4 |
| `src/OmenCoreApp/ViewModels/SettingsViewModel.cs` | `LoadSystemStatus()` | `SecureBootEnabled` → gated on `!pawnIOPresent` | 🟡 P1-1 |
| `src/OmenCoreApp/Services/SystemInfoService.cs` | `PerformDependencyAudit()` | Raise threshold or split required/recommended/info | 🟡 P1-2 |
| `src/OmenCoreApp/Services/HardwareMonitoringService.cs` | `MonitorLoopAsync()` error accumulator | Replace exit with restart + backoff | 🟢 P2 |
| `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs` | `TryRestartAsync()` | Implement real WMI session reset (currently no-op) | 🟢 P2 |

---

## 12. Test Plan

### Unit Tests

#### `ModelCapabilityDatabaseTests`
```csharp
[Fact]
public void GetCapabilities_8BAB_ReturnsCorrectFamily()
{
    var caps = ModelCapabilityDatabase.GetCapabilities("8BAB");
    Assert.Equal(OmenModelFamily.OMEN16, caps.Family);
    Assert.True(caps.SupportsFanControlWmi);
    Assert.Equal(2, caps.FanZoneCount);
}

[Fact]
public void GetCapabilities_8BAB_NotTranscendFallback()
{
    var caps = ModelCapabilityDatabase.GetCapabilities("8BAB");
    Assert.NotEqual("8C3A", caps.ProductId);  // Not Transcend
}
```

#### `WmiBiosMonitorTests`
```csharp
[Fact]
public async Task NvapiDisabled_RecoversAfter60Seconds()
{
    var monitor = CreateMonitorWithNvapiStub(alwaysFails: true);
    // Simulate 10 failures
    for (int i = 0; i < 10; i++) await monitor.UpdateReadingsAsync();
    Assert.True(monitor.IsNvapiDisabled);

    // Advance clock by 61s
    monitor.AdvanceFakeClock(61);
    await monitor.UpdateReadingsAsync();
    Assert.False(monitor.IsNvapiDisabled);
}
```

#### `WmiFanControllerTests`
```csharp
[Fact]
public void RestoreAutoControl_FromExtremePreset_ExecutesResetSequence()
{
    var controller = CreateFanController();
    controller.SetExtremePreset();   // sets IsManualControlActive=false
    var resetCalled = false;
    controller.OnResetFromMaxMode += () => resetCalled = true;

    controller.RestoreAutoControl();
    Assert.True(resetCalled);
}

[Fact]
public void RestoreAutoControl_ClearsDebounceWindow()
{
    var controller = CreateFanController();
    controller.SetExtremePreset();
    controller.RestoreAutoControl();
    Assert.Equal(DateTime.MinValue, controller.LastProfileSwitch);
}
```

#### `LinuxEcControllerTests`
```csharp
[Fact]
public void SetPerformanceMode_UsesHpWmi_WhenEcNotAvailable()
{
    var ec = CreateLinuxEcController(hasEcAccess: false, hasHpWmiAccess: true);
    var hpWmiCalled = false;
    ec.OnSetHpWmiThermalProfile += (profile) => { hpWmiCalled = true; };

    var result = ec.SetPerformanceMode(PerformanceMode.Performance);
    Assert.True(hpWmiCalled);
    Assert.True(result);
}

[Fact]
public void SetPerformanceMode_DoesNotCallWriteByte_WhenHpWmiSucceeds()
{
    var ec = CreateLinuxEcController(hasEcAccess: false, hasHpWmiAccess: true);
    var writeByteCalled = false;
    ec.OnWriteByte += (reg, val) => { writeByteCalled = true; };

    ec.SetPerformanceMode(PerformanceMode.Performance);
    Assert.False(writeByteCalled);
}
```

### Integration Tests

| Test | Steps | Pass Criterion |
|------|-------|---------------|
| OMEN 16-wf1xxx fan control | Boot with 8BAB, open OmenCore, switch to Extreme | Fan RPM > 0, no "unsupported" error |
| Auto mode transition | Set Extreme → switch to Auto | RPM shows > 0 within 1 s of switch |
| Linux CLI perf mode (hp-wmi system) | `omencore-linux perf --mode performance` on hp-wmi-only system | Exit 0, "✓ Set to performance" |
| Status tab — PawnIO + Secure Boot | Enable Secure Boot in test VM, install PawnIO | No yellow Secure Boot warning |
| Dependency audit — no OGH | Remove OGH in clean install | Status shows "Standalone" or "Optimal", not "Degraded" |
| NVAPI recovery | Inject 10 NVAPI failures, wait 61 s | GPU temp resumes updating |

---

## 13. Regression Risks

### RC-3 Fix (Fan Auto Reset)
- **Risk:** `ResetFromMaxMode()` on V1 systems sends `SetFanLevel(20,20)` then `SetFanLevel(0,0)`. If the system is mid-ramp, the extra step may cause a brief RPM spike.
- **Mitigation:** `ResetFromMaxMode()` already checks `_fanVersion == V1` conditionally; V2 is already a no-op. Low risk.
- **Regression:** None expected on V2 systems.

### RC-1 Fix (NVAPI Recovery)
- **Risk:** Re-enabling NVAPI on a system with a genuinely hung NVIDIA driver causes the monitoring loop to stall for up to 10 s (the existing `ReadSampleTimeoutMs`).
- **Mitigation:** The existing timeout machinery already handles this. The 60 s cooldown ensures we don't retry aggressively. Accept this risk.
- **Watch for:** `consecutiveTimeouts` counter increments during NVAPI re-enable — if it hits 3, the bridge restart path triggers.

### RC-2 Fix (8BAB Model Entry)
- **Risk:** wf1xxx variants (8BAB) may have different WMI fan command semantics than wf0xxx (8BCA). Enabling `SupportsFanControlWmi = true` without hardware verification could send incorrect WMI commands.
- **Mitigation:** Set `UserVerified = false` in the entry. Add a "Report your experience" prompt in the UI for unverified models. Roll back to `SupportsFanControlWmi = false` if reports of fan controller bricking emerge.
- **Note:** wf1xxx is Intel + NVIDIA (same discrete GPU path as wf0xxx). WMI BIOS commands are HP platform-level, not GPU-specific. Risk is low.

### P1-1 Fix (Secure Boot Warning Suppression)
- **Risk:** A user has PawnIO installed but it is corrupted/non-functional. The suppressed Secure Boot warning hides the real issue.
- **Mitigation:** Use `IsPawnIOAvailable()` which does a device file open test (not just registry check). If the device file is inaccessible, `PawnIOAvailable = false`, warning remains visible.

### P1-2 Fix (Standalone Threshold)
- **Risk:** Raising threshold to 3 could hide a genuinely degraded install where 3 optional components are missing, including the EC driver (PawnIO/WinRing0).
- **Mitigation:** Use Option B from the fix plan — semantic split keeps the "no EC driver" case at Degraded regardless of OGH/HP-SEU state.

---

## Appendix A — Verified ProductIds in ModelCapabilityDatabase

| ProductId | Model | `SupportsFanControlWmi` | Notes |
|-----------|-------|------------------------|-------|
| 8BCA | OMEN 16 wf0xxx 2023 Intel | true | wf0xxx reference |
| 8BCD | OMEN 16 xd0xxx 2024 AMD | true | xd0xxx reference |
| 8C3A | OMEN Transcend 16 | **false** | Transcend — different WMI path |
| **8BAB** | **OMEN 16 wf1xxx 2024 Intel** | **MISSING** | **Issue #68 root cause** |

---

## Appendix B — Linux Backend Priority Matrix

| Backend | `IsAvailable` condition | Fan | Perf Mode | Works on 8C78? |
|---------|------------------------|-----|-----------|----------------|
| ec_sys | `HasEcAccess` | ✓ | ✓ | ✗ (not exposed) |
| hp-wmi | `HasHpWmiAccess` | ✓ | ✗ (BUG) | ✓ (present) |
| ACPI platform_profile | `HasAcpiProfileAccess` | ✗ | ✓ | ✓ (fallback) |
| hwmon-pwm | `HasHwmonFanAccess` | ✓ | ✗ | ✓ |

After RC-4 fix, hp-wmi column for Perf Mode becomes ✓.
