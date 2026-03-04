# v3.0.2 Hotfix - Pending Release

**Status:** Development (awaiting more bug reports)

This document tracks fixes being prepared for v3.0.2. Installer build will begin once user community confirms these address the reported issues and no additional bugs are discovered.

## Issues Fixed

### 1. ✅ PawnIO MSR Reboot Detection (CPU Power = 0W)

**Problem:**
- AMD Ryzen + PawnIO: CPU/GPU power reads as 0W persistently
- Root cause: PawnIO driver needs reboot after install to fully initialize MSR module
- Affects: Any user who just installed PawnIO

**Fix Applied:**
- Added `IsPawnIOInstalled()` static method to `PawnIOMsrAccess.cs`
- MainViewModel now detects if PawnIO is installed but MSR init failed
- Logs warning: `⚠️  CPU power reading will report 0W. Please restart your computer to fully activate PawnIO driver.`
- User can see this in the application log and take corrective action

**Code Changes:**
- `src/OmenCoreApp/Hardware/PawnIOMsrAccess.cs`: Added `IsPawnIOInstalled()` method
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`: Added post-init check for PawnIO installation vs. MSR functionality

---

### 2. ✅ CPU Temperature Freeze Detection (AMD WMI BIOS)

**Problem:**
- AMD OMEN 16 xd0xxx: CPU temp gets "stuck" at a single value (e.g. 66.1°C) for ~30 seconds
- WMI BIOS thermal sensor stops updating under certain load conditions
- Affects: AMD Ryzen laptops with WMI-based thermal monitoring

**Fix Applied:**
- Added freeze detection to `WmiBiosMonitor.cs` class
- Detects ≥30 consecutive identical temperature readings (≈30 seconds at 1Hz monitoring)
- Logs warning when freeze is detected: `🥶 CPU temperature appears frozen at 66.1°C for 30 readings`
- Logs recovery notice when temperature changes again
- App continues functioning with last-known-good temperature cache

**Code Changes:**
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`:
  - Added freeze detection fields: `_lastCpuTempReading`, `_consecutiveIdenticalCpuTempReads`, `_cpuTempFrozen`
  - Added logic to detect identical readings and log warnings
  - Tracks freeze duration and recovery

---

### 3. ✅ Fan RPM Instability at Max Speed (AFC Reversion)

**Problem:**
- AMD OMEN 16 xd0xxx: When fans set to max, RPM bounces (6200→5700→6000→5700 cycle)
- BIOS thermal controller fights with max fan command, reverting it frequently
- Affects: AMD OMEN models with aggressive AFC (Automatic Fan Control)

**Fix Applied:**
- Made **countdown extension timer MORE AGGRESSIVE** to prevent BIOS reversion
- Changed interval from 3000ms (3 seconds) to **800ms (0.8 seconds)**
- Changed initial delay from 1000ms to **250ms** to fire earlier
- This re-sends the `SetFanMax(true)` command 10x more frequently, overwhelming AFC revert attempts

**Code Changes:**
- `src/OmenCoreApp/Hardware/WmiFanController.cs`:
  - `CountdownExtensionIntervalMs`: 3000ms → **800ms**
  - `CountdownExtensionInitialDelayMs`: 1000ms → **250ms**
  - Updated comment to explain AMD OMEN 16 xd0xxx fast reversion issue

**Why This Fixes It:**
- Prior: BIOS had 3-second window to revert fans before next `SetFanMax(true)` re-send
- Now: Window is only 0.8 seconds, and initial check happens even faster (0.25s)
- Effectively "pins" max fan mode against BIOS reversion attempts

---

### 4. 📝 Brightness Keys (Fn+F2 / Fn+F3) - Known Limitation

**Problem:**
- AMD OMEN 16 xd0xxx: Fn+F2 and Fn+F3 (brightness keys) toggle OmenCore window
- Root cause: Windows brightness control system has OS-level priority
- OmenCore cannot intercept or override system brightness keys
- Workaround exists but is non-obvious

**Mitigation:**
- Documented as known limitation in logs and user guides
- Not a code bug — systemic Windows behavior
- Users can:
  1. Use keyboard brightness buttons on different function layer (if available)
  2. Disable OMEN key hotkey if conflicts persist
  3. Use Windows Settings → Display → Brightness instead

**No Code Fix Applied:**
- Issue is at OS level, not application level
- Code-based workaround would require low-level driver changes (out of scope)

---

## Testing Checklist (Before Release)

- [ ] AMD Ryzen + PawnIO: Verify warning message appears on fresh install (pre-reboot)
- [ ] AMD Ryzen: Verify warning disappears after system reboot
- [ ] AMD OMEN 16 xd0xxx: Set fans to Max and verify RPM stays stable (±100 RPM fluctuation OK)
- [ ] AMD OMEN 16 xd0xxx: Verify countdown extension logs at 0.8s intervals
- [ ] Intel OMEN: Verify no regression in fan control behavior
- [ ] All platforms: Verify CPU temp monitoring still works normally
- [ ] Build: 0 errors, 0 warnings

---

## Version Info

- **Base:** v3.0.1 (main branch, c2f98f7)
- **Branch:** v3.0.2 (development, await final bugs)
- **Build Date:** Pending (awaiting test feedback)

---

## Next Steps

1. **Gather validation from users** — Test these fixes on reported systems
2. **Confirm no regressions** — Ensure no new bugs introduced
3. **Wait for additional bug reports** — User community testing phase
4. **Final build & release** — When validated stable

**No installer builds will be created until validation is complete.**
