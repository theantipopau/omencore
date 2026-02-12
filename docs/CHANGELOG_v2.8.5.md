# OmenCore v2.8.5 — Community Bug Fix Update

**Release Date:** 2026-02-10
**Focus:** 9 community-reported bug fixes + 4 code quality improvements

---

## Bug Fixes

### Bloatware Removal Not Working
- **Root Cause:** `Get-AppxPackage` expects the `-Name` parameter (e.g., `Microsoft.BingWeather`), but was being passed the full `PackageFullName` (e.g., `Microsoft.BingWeather_4.53.52220.0_x64__8wekyb3d8bbwe`) — which never matches anything
- **Fix:** Extract the short Name portion from PackageFullName via `Split('_')[0]` before passing to `Get-AppxPackage` removal commands
- **File:** `BloatwareManagerService.cs`

### Fan Diagnostic Fails at 30% Speed
- **Root Cause:** Fixed 15% RPM tolerance was too tight at low fan speeds — at 30% target, expected ~1650 RPM but actual was 2000–2200 RPM due to the fan's minimum RPM floor and discrete speed steps
- **Fix:** Adaptive tolerance scaling (wider at low percentages, 25% base tolerance) with a minimum 500 RPM absolute tolerance floor
- **File:** `FanVerificationService.cs`

### Fans Stuck at Max After Fan Test
- **Root Cause:** When user was in auto/BIOS fan mode (no preset active), `_preTestPreset` was null, so the diagnostic cleanup had nothing to restore — fans stayed at whatever test speed they were set to
- **Fix:** Both single-test and guided-test cleanup now call `FanService.RestoreAutoControl()` when no preset was saved, returning fans to BIOS-managed mode
- **Files:** `FanDiagnosticsViewModel.cs`, `FanService.cs`

### Fan Test Error Text Truncated
- **Root Cause:** Error message TextBlock in fan diagnostics view had no text wrapping configured
- **Fix:** Added `TextWrapping="Wrap"` and `MaxWidth="500"` to the error message display
- **File:** `FanDiagnosticsView.xaml`

### Fn+F2/F3 Still Toggles OmenCore Window
- **Root Cause:** Keyboard hook scan code fallback matched `0x0046` (Scroll Lock / Fn+brightness scan code) without verifying it was actually an OMEN key press
- **Fix:** Non-extended scan codes (`0x0046`, `0x009D`) now require a known OMEN virtual key code (`VK_LAUNCH_APP2`, `VK_LAUNCH_APP1`, `VK_OEM_OMEN`, etc.) to prevent false positives from Fn+brightness keys
- **File:** `OmenKeyService.cs`

### OMEN 4-Zone Keyboard Not Detected (xd0xxx)
- **Root Cause:** Two issues — (1) `KeyboardDiagnosticsViewModel` was initialized before `_keyboardLightingService` was created, so it always received null; (2) `GetConfigByModelName()` couldn't fuzzy-match "OMEN by HP Gaming Laptop 16-xd0xxx" against "OMEN 16-xd0xxx (2024) AMD" since neither string fully contains the other
- **Fix:** (1) Moved keyboard diagnostics init to after keyboard lighting service creation. (2) Added series-pattern regex matching that extracts the model identifier (e.g., "16-xd0xxx") from both strings for comparison
- **Files:** `MainViewModel.cs`, `KeyboardModelDatabase.cs`

### OMEN Gaming Hub Not Detected as Conflict
- **Root Cause:** Conflict detection only looked for `OGHAgent`, `HPOmenCommandCenter`, and `OMEN Gaming Hub` process names — missed the separate `OmenLightingService` and `OmenLighting` processes visible in Task Manager
- **Fix:** Added `OmenLightingService`, `OmenLighting`, and `HP.OMEN.GameHub` to the OMEN Hub process name list
- **File:** `ConflictDetectionService.cs`

### Startup Doesn't Work on Shutdown + Power On (Only Restart)
- **Root Cause:** Windows Fast Startup (enabled by default) hibernates the kernel on shutdown. The `/sc onlogon` task trigger doesn't fire because no logon event occurs — the previous session is simply restored from hibernation
- **Fix:** Replaced single `onlogon` trigger with an XML task definition using `LogonTrigger` (5s delay) + `StartWhenAvailable=true` to recover missed triggers after Fast Startup resume
- **File:** `SettingsViewModel.cs`

### Update Checker Always Shows Update Available
- **Root Cause:** `UpdateCheckService.cs` had a hardcoded fallback version string `"2.7.1"` — 4 versions behind, causing it to always think an update is available
- **Fix:** Updated to `"2.8.5"` to match actual release version
- **File:** `UpdateCheckService.cs`

---

## Code Quality Improvements

- **Stale version strings fixed** — 4 additional files were still hardcoded at `"2.7.1"`: `TrayIconService.cs`, `ProfileExportService.cs` (x2), and `OmenCore.Linux/Program.cs`
- **Process resource leaks** — Added `using` statements to 4 `Process` objects in `SettingsViewModel.cs` that were not being disposed
- **ContinueWith fault handling** — `PerformanceModeService.cs` now checks `t.IsFaulted` before accessing `t.Result`, and uses `TaskContinuationOptions.NotOnCanceled`
- **Orphaned test file removed** — Deleted `SettingsRestorationServiceTests.cs` which referenced a service that was removed in a previous version

---

## Known Behaviors (Not Bugs)

### Performance Mode Increases CPU Temps by 10-15C
This is **expected behavior**. Performance mode unlocks higher CPU/GPU power limits (PL1/PL2), allowing the processor to boost higher and consume more power. Higher power = more heat. The fans will ramp up accordingly.

### Keyboard EC Commands Change Screen Brightness
On some models (including xd0xxx), certain EC registers overlap between keyboard backlight and display brightness circuits. This is a hardware-level register mapping issue specific to the laptop's EC firmware and cannot be fixed in software without a complete EC register map for each model variant.

---

## Files Changed (15 files)

| File | Change |
|------|--------|
| `BloatwareManagerService.cs` | Extract appxName from PackageFullName for `Get-AppxPackage` |
| `ConflictDetectionService.cs` | Added 3 OMEN Hub process name variants |
| `FanDiagnosticsView.xaml` | TextWrapping + MaxWidth on error TextBlock |
| `FanDiagnosticsViewModel.cs` | RestoreAutoControl fallback in test cleanup |
| `FanService.cs` | Added public `RestoreAutoControl()` wrapper method |
| `FanVerificationService.cs` | Adaptive tolerance + 500 RPM minimum |
| `KeyboardModelDatabase.cs` | Series-pattern regex fuzzy matching |
| `MainViewModel.cs` | Fixed keyboard diagnostics init order |
| `OmenKeyService.cs` | OMEN VK code required for non-extended scancodes |
| `PerformanceModeService.cs` | ContinueWith fault handling |
| `SettingsViewModel.cs` | XML task with LogonTrigger + StartWhenAvailable; Process `using` |
| `UpdateCheckService.cs` | Fixed stale hardcoded version (2.7.1 -> 2.8.5) |
| `TrayIconService.cs` | Fixed stale fallback version (2.7.1 -> 2.8.5) |
| `ProfileExportService.cs` | Fixed stale version strings (2.7.1 -> 2.8.5) |
| `OmenCore.Linux/Program.cs` | Fixed stale version const (2.7.1 -> 2.8.5) |

---

## Downloads

**Windows Installer:** `OmenCoreSetup-2.8.5.exe` (101.08 MB)
**Windows Portable:** `OmenCore-2.8.5-win-x64.zip` (104.23 MB)
**Linux:** `OmenCore-2.8.5-linux-x64.zip` (95.78 MB)

### SHA256 Checksums

```
E2765026C8E35ABE05D729973AE52074236C9EBDEE3886E2ACC1E59A40714C21  OmenCoreSetup-2.8.5.exe
319CDCFA839D67117CAFFBEA6AC3149009C75A4BDBD9D300F9536DE3A30E7A21  OmenCore-2.8.5-win-x64.zip
FEDB7D37DEE9772437123231AB057DE70F9D5D88778B5DAEAE92A395BC1D8E44  OmenCore-2.8.5-linux-x64.zip
```

---

## Upgrade Notes

- **From v2.8.1+**: Drop-in replacement. Run installer or replace files.
- **Startup task**: Users who already enabled "Start with Windows" should toggle it off and on again to apply the new startup task format.
- **Bloatware**: Users who previously tried removing bloatware should try again — it will now work correctly.
