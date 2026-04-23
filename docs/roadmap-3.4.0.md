# OmenCore v3.4.0 Final Release Roadmap

This roadmap is based on a full audit of the active shipping codepaths in the Windows WPF app, the Windows hardware worker, the Linux CLI, the Avalonia Linux GUI, and the current packaging/update pipeline — cross-referenced against the shipped changelogs for v3.2.5, v3.3.0, and v3.3.1. The goal for v3.4.0 is to refine and stabilize what already exists, not to expand scope.

Each roadmap item now includes a **Plan** section describing exactly what to change and where. A dedicated **Test Proposals** section at the end describes 60–75 new tests that can be added without hardware.

**Roadmap scope:** 3 Critical items, 32 High Priority items (including 3 user-reported bugs + 14 from community/Discord/Reddit reports), 23 Nice-to-Have items, ~60–75 proposed tests across 5 tiers, and a full GitHub issue triage of all 72 open issues.

## Current State Summary

The v3.2.5→v3.3.0→v3.3.1 cycle was the most productive stability and polish sequence in the project's history. v3.3.0 alone shipped ~80 items including critical fixes for monitoring halt, fan curve freezing, Restore Defaults deadlock, and the non-English startup crash that v3.3.1 then root-caused and eliminated. The Windows core stack — monitoring, fan control, resume recovery, worker isolation, update hashing, locale-safe exception handling — is now substantially hardened.

What remains for v3.4.0 is narrower than a typical audit would suggest:

- The release gate infrastructure is well-designed (advisory baseline + blocking new violations + zero-tolerance `ex.Message.Contains`), and its baseline has now been refreshed and returned to green.
- The Linux/Avalonia surface has real gaps in fan-control semantics and automated coverage, but the Windows product is close to final quality.
- Several placeholder or experimental surfaces remain visible in the release-facing UI. v3.3.0 addressed some of these (GPU fan curve status card, RGB provider transparency, Lite Mode), but a few still need cleanup.
- Documentation and packaging still have some drift from shipped code (primarily README and archived/dead project cleanup), though installer and build-script alignment has improved.
- Two deferred v3.3.1 implementation steps (STEP-09: monitoring dispatch simplification, STEP-13: keyboard lighting async init) remain pending hardware validation.

## Validation Snapshot

- Editor/compiler error check: **zero errors** across all inspected projects (12 analyzer warnings in test files were resolved on 2026-04-17).
- Automated tests: **193 passed, 0 failed**.
- Release gate blocking test is now green after baseline refresh in `ReleaseGateCodeHygieneTests`.
- Test trajectory: the suite grew from ~124 tests (pre-3.3.1) to 193 with subsequent reliability and regression additions.

## Critical (must fix before release)

### 1. Refresh the release-gate baseline and get back to green ✅ FIXED (baseline refreshed, suite green)

Area: Code Quality, Release Readiness

Evidence:

- `ReleaseGateCodeHygieneTests.NoBareCatchBraces_NewViolations_Blocking` was previously failing due to shifted baseline line numbers.
- The baseline entries were refreshed and the gate is now passing again.
- Current validation state is green (`193 passed, 0 failed`) with no new blocking bare-catch violations.

#### Implemented

- Updated known baseline entries in `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs` to match post-edit line shifts.
- Re-validated with full suite runs to confirm no new blocking violations.

Why this is critical:

- A final release cannot ship with the release-gate suite failing.
- The gate design is sound — it just needs its baseline updated to match current line numbers.

#### Plan

**Step 1 — Identify shifted violations (~15 min)**

Run the test suite. The `NoBareCatchBraces_NewViolations_Blocking` test will output lines like `NEW: FanControllerFactory.cs:153` for violations not in the baseline. Compare these against the existing `KnownBareCatchViolations` entries for the same file to identify which baseline line numbers shifted.

**Step 2 — Update the baseline in `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs` (~15 min)**

The `KnownBareCatchViolations` HashSet (lines 47–128) contains 83 `"filename:line"` entries. For each shifted violation:
- Remove the old `"FanControllerFactory.cs:151"` (or similar stale line number)
- Add the new `"FanControllerFactory.cs:153"` (or whatever the test output reports)
- Add a comment: `// shifted from :151 in post-3.3.1 edits`

Do the same for `WmiBiosMonitor.cs` and any other files whose line numbers drifted.

**Step 3 — Verify green (~5 min)**

Re-run the test suite. All 171+ tests should pass. The advisory test should report the same 83 known violations (same count, updated line numbers). The blocking test should report zero new violations.

**Step 4 — Extend gate to Linux/Avalonia sources (new work, ~30 min)**

Currently `GetCSharpSourceFiles()` only scans `src/OmenCoreApp/`. Extend it to also scan `src/OmenCore.Avalonia/` and `src/OmenCore.Linux/`. This requires:
- Modify `GetMainSourceRoot()` to return the `src/` directory (parent of `OmenCoreApp/`)
- Or add a parallel `GetLinuxAvaloniaSourceFiles()` helper that scans those two trees
- Capture a new baseline of bare catches in those trees (there are many in `LinuxHardwareService.cs` and `MainWindowViewModel.cs`)
- Add a separate advisory test `NoBareCatchBraces_LinuxAvalonia_Advisory` and blocking test `NoBareCatchBraces_LinuxAvalonia_Blocking` with their own baseline

Files to modify:
- `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs`

Recommendation:

- Update the `KnownBareCatchViolations` baseline in `ReleaseGateCodeHygieneTests.cs` to reflect current line numbers for the same known violations, with a comment noting the shift.
- Continue the gradual bare-catch cleanup trajectory started in v3.3.1 — target the highest-risk remaining violations in fan control and monitoring paths.
- Extend the hygiene gate to also scan `src/OmenCore.Avalonia` and `src/OmenCore.Linux` where bare catches and silent-failure patterns remain untracked.

### 2. Decide the Linux/Avalonia fan-control contract and align the UI to it ✅ PARTIALLY FIXED (Option B shipped: one-shot + capability-gated UI)

Area: Feature Completeness, Functional Gaps, UI/UX, Release Readiness

Evidence:

- `src/OmenCore.Avalonia/Services/FanCurveService.cs` applies a curve based on the current instantaneous temperature and writes the resulting speed once. There is no continuous control loop.
- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs` often falls back to profile-oriented behavior rather than sustained manual fan control.
- `src/OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs` no longer exposes `Custom` (fixed in HP #16), but fan-curve behavior remains one-shot by design.
- The Linux GUI already correctly surfaces fan-control capability class and reason (`FanControlCapabilityClass`, `FanControlCapabilityReason`) in the nav and tooltips — the capability detection pipeline is present, but the curve-apply semantics behind it are weaker than the UI implies.

Why this is critical:

- This is the biggest remaining semantic gap on the Linux surface.
- v3.4.0 should not ship a core feature that appears richer than it really is.

Status update:

- Implemented Option B alignment in Avalonia fan UI:
  - Apply action relabeled to one-shot behavior (`Apply Once`) with explicit tooltip.
  - Fan curve controls are now capability-gated and hidden when manual fan control is unavailable.
  - Added capability warning banner for `profile-only`, `telemetry-only`, and `unsupported-control` classes.
- Remaining decision is whether to keep this explicit one-shot model for v3.4.0 or invest in Option A continuous loop in a follow-up.

#### Plan

**Option A — Lightweight continuous loop (recommended if shipping fan control as a feature)**

This adds a background loop to `FanCurveService` that periodically reads hardware status and re-applies the interpolated curve, similar to how the Windows `FanService` works.

Files to modify:
- `src/OmenCore.Avalonia/Services/FanCurveService.cs`:
  - Add a `CancellationTokenSource` field and `StartContinuousApplyAsync(int intervalMs, CancellationToken)` method
  - The loop body: call `_hardwareService.GetStatusAsync()`, run `InterpolateFanSpeed()` for both CPU and GPU, call `SetCpuFanSpeedAsync`/`SetGpuFanSpeedAsync`, sleep for `intervalMs`
  - Add hysteresis: only write if the computed speed differs from the last-written speed by more than a threshold (e.g., 3%)
  - Add `StopContinuousApply()` to cancel the loop
  - Add a `bool IsRunning` property
- `src/OmenCore.Avalonia/Services/IFanCurveService.cs` (the interface at the top of FanCurveService.cs):
  - Add `Task StartContinuousApplyAsync(int intervalMs, CancellationToken ct)`, `void StopContinuousApply()`, `bool IsRunning`
- `src/OmenCore.Avalonia/ViewModels/FanControlViewModel.cs`:
  - When the user clicks "Apply Curve", start the continuous loop instead of a one-shot apply
  - When the user switches away from Custom mode or closes the panel, stop the loop
  - Add UI state for "Curve Active" indicator
- Gate behind `LinuxCapabilityClassifier.CapabilityClass == FullControl`:
  - If the system is `ProfileOnly` or lower, don't show the curve controls at all — only show profile selection

Scope estimate: ~200 lines of new code, ~50 lines of modified code.

**Option B — Relabel the UI to match one-shot behavior (recommended if time-constrained)**

This is the simpler, lower-risk path. Instead of adding a continuous loop, make the UI honest about what it does.

Files to modify:
- `src/OmenCore.Avalonia/ViewModels/FanControlViewModel.cs`:
  - Change the "Apply Curve" button label to "Apply Once" or "Set Fan Speeds Now"
  - Add a tooltip: "Sets fan speeds based on current temperature. Speeds will not automatically adjust as temperature changes."
- `src/OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs`:
  - Remove the `Custom` performance mode from the dropdown (or rename it to clarify it maps to the same backend as manual fan set)
  - Only show Quiet / Balanced / Performance profiles that map to real `platform_profile` or `thermal_profile` values
- `src/OmenCore.Avalonia/Views/FanControlView.axaml`:
  - Add an info banner when capability class is `ProfileOnly`: "Your system supports thermal profiles but not direct fan speed control. Use the profile selector above."
  - Hide the curve editor entirely for `TelemetryOnly` and `UnsupportedControl` capability classes

Scope estimate: ~50 lines of UI changes, no backend changes.

Recommendation:

- Pick one path before release:
  - Option A: implement a lightweight continuous fan-curve loop with hysteresis and explicit capability detection.
  - Option B: relabel the Linux fan UI and docs to describe profile-based or one-shot behavior accurately.
- Remove the `Custom` performance mode from the Linux UI unless it gains distinct backend meaning (both options require this).

### 3. Fix release engineering and packaging drift ✅ FIXED (README/build/installer alignment + desktop prototype archived)

Area: Release Readiness, Code Quality, Maintainability

Evidence:

- `README.md` stale references have been refreshed to `3.4.0`, but broader release-doc consistency still requires routine re-validation before cut.
- `build-installer.ps1` has already been aligned to a Windows-only path and now explicitly rejects Linux runtime usage.
- The empty `installer/download-librehw.ps1` helper is already removed.
- `installer/OmenCoreInstaller.iss` was cleaned up for obsolete directives and per-user uninstall deletion, but still has additional hardening opportunities noted in Nice-to-Have #18.
- `src/OmenCore.Desktop/` is now explicitly marked archived with in-folder guidance and project-file comment indicating it is out of shipping scope and should not be version-bumped.

#### Implemented

- Refreshed stale `README.md` release references and current-release links to `3.4.0`.
- Kept Linux packaging on `build-linux-package.ps1` and Windows packaging on `build-installer.ps1`.
- Removed the empty installer helper-script dependency (`installer/download-librehw.ps1`).
- Added archive markers for `src/OmenCore.Desktop/`:
  - `src/OmenCore.Desktop/README.md`
  - comment in `src/OmenCore.Desktop/OmenCore.Desktop.csproj`

#### Plan

**Step 1 — Update README.md (~30 min)**

- Update version references from 3.3.0 to 3.4.0
- Update download link URLs to point to v3.4.0 release artifacts
- Change "No telemetry" claim to "No outbound telemetry — all diagnostics stay on your machine" (see High Priority #7)
- Add a sentence explaining the admin requirement
- Verify all feature claims match the current shipped behavior
- Confirm the Linux section accurately reflects the capability classifier output (FullControl / ProfileOnly / TelemetryOnly / UnsupportedControl)

**Step 2 — Clean up `build-installer.ps1` Linux branches (~20 min)**

- Search `build-installer.ps1` for any `linux` or `Linux` branches (publish commands, RID references, zip steps)
- Remove or comment out these branches with a note: "Linux packaging is handled by build-linux-package.ps1"
- Verify the Windows-only build path still works by running the Build OmenCore Installer task

**Step 3 — Resolve `installer/download-librehw.ps1` (~10 min)**

Two options:
- **Option A (preferred):** Delete the empty file and remove the invocation from `build-installer.ps1`. Add a comment in the installer script documenting where the LibreHardwareMonitor dependency comes from (NuGet restore, committed binary, etc.)
- **Option B:** Implement the script to download a pinned version of LibreHardwareMonitorLib from GitHub releases or NuGet, with hash verification

**Step 4 — Align Inno Setup installer with runtime storage (~20 min)**

- Open `installer/OmenCoreInstaller.iss`
- Remove the `[Dirs]` entries that create `{app}\logs` and `{app}\config` if they exist
- Or add comments explaining they are legacy and the app now uses `%APPDATA%\OmenCore` and `%LOCALAPPDATA%\OmenCore\logs`
- Verify the uninstall section doesn't accidentally delete user data from the AppData paths

**Step 5 — Stop version-bumping OmenCore.Desktop (~5 min)**

- Remove `src/OmenCore.Desktop` from any version-bump scripts or automation
- Ideally archive or delete the entire `src/OmenCore.Desktop/` directory (see High Priority #1)
- If not deleting, add a `README.md` inside it: "This project is archived and not part of the OmenCore build. Do not version-bump."

Files to modify:
- `README.md`
- `build-installer.ps1`
- `installer/download-librehw.ps1` (delete or implement)
- `installer/OmenCoreInstaller.iss`
- `src/OmenCore.Desktop/` (archive or mark as dead)

Recommendation:

- Update README before cutting v3.4.0 (version, download links, feature claims).
- Remove Linux packaging responsibilities from `build-installer.ps1` — `build-linux-package.ps1` is already the verified path.
- Either restore `installer/download-librehw.ps1` to a real verified step or delete the invocation and document the current dependency source.
- Align the installer with the actual runtime storage model.
- Stop version-bumping `OmenCore.Desktop` in release cycles.

## High Priority (strongly recommended)

### 0a. Fix fan profile card hidden behind curve editor (GitHub #113, layout regression) ✅ FIXED (covered by #30)

Area: UI/UX, Functional Regression, Release Readiness

Evidence:

- User reports: "The fan mode card is hidden behind the custom fan curve editor" — visible in attached screenshots showing the preset card panel overlapping with the curve editor.
- This worked correctly in v3.2.5 but broke in v3.3.0/v3.3.1.
- Root cause confirmed: in `src/OmenCoreApp/Views/FanControlView.xaml`, **two elements are assigned to `Grid.Row="2"`**:
  - Line 124: `<Border Grid.Row="2">` — the fan profile presets panel (6-column UniformGrid: Max/Extreme/Gaming/Auto/Silent/Custom)
  - Line 498: `<Grid Grid.Row="2">` — the curve editor + telemetry panel
- The outer Grid (line 48–52) only defines 3 rows: `Auto, Auto, *`. Both panels render in the same `*` row, causing the curve editor to render on top of the preset panel.
- In WPF, when multiple children share the same Grid cell, they overlap/stack. The later element (curve editor) renders on top of the earlier one (presets).

Why this is high priority (borderline critical):

- Users cannot access the fan mode selection cards (Max/Extreme/Gaming/Auto/Silent/Custom) because the curve editor covers them.
- This is a core fan control workflow regression that affects every user.

Status update:

- Resolved via the same layout fix tracked in item #30 (profile row restored, curve editor moved to the following row).

#### Plan

**The fix is a one-line XAML change:**

In `src/OmenCoreApp/Views/FanControlView.xaml`:

1. Add a 4th row to the outer Grid's RowDefinitions (line 48–52):
   ```xml
   <Grid.RowDefinitions>
       <RowDefinition Height="Auto"/>   <!-- Row 0: Header -->
       <RowDefinition Height="Auto"/>   <!-- Row 1: Info banner -->
       <RowDefinition Height="Auto"/>   <!-- Row 2: Fan profile presets -->
       <RowDefinition Height="*"/>      <!-- Row 3: Curve editor + telemetry -->
   </Grid.RowDefinitions>
   ```

2. Change the curve editor Grid (line 498) from `Grid.Row="2"` to `Grid.Row="3"`:
   ```xml
   <Grid Grid.Row="3">
   ```

3. If there's a `LoadingOverlay` with `Grid.RowSpan="3"`, update it to `Grid.RowSpan="4"`.

**Verification:**
- Launch OmenCore → Fan Control tab
- All 6 preset cards should be visible above the curve editor
- The curve editor should appear below the presets, not overlapping
- Scrolling behavior should work correctly (the `*` row for the curve editor fills remaining space)

Files to modify:
- `src/OmenCoreApp/Views/FanControlView.xaml` (3 line changes)

### 0b. Fix RGB keyboard colors turning off on OMEN 16-xd0xxx (8BCD) ✅ FIXED

Area: Hardware Compatibility, Functional Bug, Release Readiness

Evidence:

- User reports: "RGB still not working. Once I select a color preset it stays off no matter what I do. Turns back on when I shutdown OmenCore and run Omen Light Studio."
- Model: OMEN by HP Gaming Laptop 16-xd0xxx, Product ID 8BCD, BIOS F.31
- Log confirms: V2 keyboard engine active, WMI BIOS (ColorTable) backend, method ColorTable2020, FourZone type
- "Experimental force EC writes" also doesn't help

Root cause analysis — the issue is in the **apply sequence** in `KeyboardLightingServiceV2.cs` and `WmiBiosBackend.cs`:

1. `ApplyProfileAsync()` (line 355–370 in `KeyboardLightingServiceV2.cs`) calls:
   - First: `SetZoneColorsAsync(colors)` → writes the 128-byte color table via WMI `CMD_COLOR_SET (0x03)` — colors are written correctly
   - Then: `SetBrightnessAsync(profile.Brightness)` → sends WMI `CMD_BACKLIGHT_SET (0x05)` with brightness value

2. The problem: on some 2024 BIOS revisions (including F.31 on 8BCD), the `CMD_BACKLIGHT_SET (0x05)` command sent **after** `CMD_COLOR_SET (0x03)` resets the keyboard controller state, effectively turning off the colors. The brightness command clobbers the previously-set color table.

3. Additionally, `SetZoneColorsAsync()` in `WmiBiosBackend.cs` (line 82–165) does NOT call `SetBacklight(true)` before writing colors. If the keyboard backlight is already in an off/reset state, the colors are written to the table but not displayed.

4. When OmenCore exits and Omen Light Studio takes over, it likely uses a different command sequence (possibly setting brightness first, then colors, or using a combined command) that doesn't trigger the BIOS reset behavior.

#### Plan

**Fix 1 — Reorder brightness and color writes (primary fix)**

In `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs`, modify `ApplyProfileAsync()` (around line 365):

```csharp
// BEFORE (broken):
result = await _activeBackend.SetZoneColorsAsync(colors);  // CMD 0x03
await _activeBackend.SetBrightnessAsync((int)profile.Brightness);  // CMD 0x05 ← clobbers colors

// AFTER (fixed):
await _activeBackend.SetBrightnessAsync((int)profile.Brightness);  // CMD 0x05 first
await Task.Delay(50); // Allow BIOS to settle
result = await _activeBackend.SetZoneColorsAsync(colors);  // CMD 0x03 last — colors stick
```

**Fix 2 — Ensure backlight is ON before writing colors**

In `src/OmenCoreApp/Services/KeyboardLighting/WmiBiosBackend.cs`, at the start of `SetZoneColorsAsync()` (around line 97, before the `SetColorTable` call):

```csharp
// Ensure backlight is on before writing colors
_wmiBios.SetBacklight(true);
await Task.Delay(30); // Allow backlight to activate
```

**Fix 3 — Skip redundant brightness write for Static effect at 100%**

In `KeyboardLightingServiceV2.cs` `ApplyProfileAsync()`, after the color write:

```csharp
// Only send brightness command if not at default max — avoids BIOS reset on some models
if (profile.Brightness < 100)
{
    await _activeBackend.SetBrightnessAsync((int)profile.Brightness);
}
```

This avoids the problematic `CMD_BACKLIGHT_SET` entirely when brightness is at the default 100%.

**Fix 4 — Add model-specific quirk for 8BCD (if reordering alone doesn't fix it)**

In `KeyboardModelDatabase.cs`, add a quirk flag for 8BCD:

```csharp
ProductId = "8BCD",
// ...existing fields...
SkipPostColorBrightnessSet = true,  // F.31 BIOS clobbers colors on CMD 0x05 after CMD 0x03
```

Then in `WmiBiosBackend.SetZoneColorsAsync`, check this flag and skip the post-color brightness command.

**Verification:**
- On OMEN 16-xd0xxx (8BCD), select a color preset → colors should appear and stay on
- Try multiple presets in sequence
- Sleep/resume → colors should persist
- Brightness slider should work independently
- Test on at least one other model (e.g., OMEN 16 non-8BCD) to ensure no regression

Files to modify:
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardLightingServiceV2.cs` (apply sequence)
- `src/OmenCoreApp/Services/KeyboardLighting/WmiBiosBackend.cs` (ensure backlight on)
- `src/OmenCoreApp/Services/KeyboardLighting/KeyboardModelDatabase.cs` (optional quirk flag)

### 0c. Add OMEN 16 n0xxx model to capability database (GitHub #113)

Area: Hardware Compatibility, Model Support

Evidence:

- GitHub #113 reports: "Model Identity Resolution shows Unknown Omen 16 Model"
- Model: Omen 16-n0xxx, BIOS F.17
- User reports fans stuck at 100% or stuck low regardless of temperature
- Uses WMI BIOS + NVAPI (no PawnIO/WinRing0)
- Logs folder is empty (logging may not initialize properly for unknown models)

Root cause: The `n0xxx` model pattern is not in the model capability database. Without a database match, the fan controller may use incorrect max fan level, wrong thermal policy version, or misidentified performance modes.

#### Plan

**Step 1 — Add n0xxx to the model database**

In the model capability database source file, add an entry:

```csharp
new ModelCapability
{
    ProductId = "????",  // Need the actual Product ID from user — ask in GitHub issue
    ModelNamePattern = "n0xxx",
    ModelName = "OMEN 16-n0xxx",
    Year = 2024,  // likely 2024 model
    Family = "OMEN16",
    UserVerified = false,
    VerificationSource = "GitHub #113",
    // Fan and thermal settings TBD based on WMI probing results
}
```

**Step 2 — Request diagnostics from the user**

Reply to GitHub #113 requesting:
- Product ID (from Settings → Model Identity Resolution, or `wmic baseboard get product`)
- Full OmenCore log (the empty logs folder suggests the logging path may not be initialized — check if `%LOCALAPPDATA%\OmenCore\logs\` exists)
- BIOS thermal policy version (visible in the startup log if logging works)
- Fan count and max fan level from WMI

**Step 3 — Investigate the fan behavior**

The "fans at 100% or stuck low" pattern on an unknown model suggests the fan controller is using a default max fan level that doesn't match the hardware. The `Max fan level: 55 (classic krpm range)` in the xd0xxx log may not be correct for n0xxx. If the n0xxx has a different WMI interface or thermal policy version, the fan speed calculations could be completely wrong.

Check:
- Is the WMI thermal policy V1 or V2 for this model?
- Does the WMI BIOS respond to fan control commands at all?
- Is OGH running and conflicting with OmenCore's fan control?

Files to modify:
- Model capability database source file (add entry)
- GitHub #113 (reply requesting diagnostics)

### 1. Remove or quarantine remaining placeholder and dead product surfaces ✅ PARTIALLY FIXED (desktop prototype archived; placeholder copy cleanup pending)

Area: UI/UX, Feature Completeness, Release Readiness

Context — what v3.3.0 already addressed:

- Enhancement #19 disabled the independent GPU fan curve toggle and added an in-panel support-status card explaining unified duty is the safe default. The `IndependentCurvesFeatureAvailable => false` guard is intentional.
- Enhancements #13–18 shipped RGB provider transparency (connection status badges), Razer reconnect with exponential backoff, Logitech GHUB reliability pass, Corsair iCUE depth, and lighting sync reliability. The Razer section is no longer purely placeholder from a backend perspective.
- Enhancement #20 shipped per-key keyboard lighting for OMEN Max models.
- Audio-reactive RGB shipped via real WASAPI/NAudio capture.

Remaining gaps:

- `src/OmenCore.Desktop` is now explicitly marked archived/not-shipping, but not yet physically removed from the repo.
- `LightingViewModel.cs` still explicitly logs that macro profiles are UI placeholders.
- Any residual "coming soon" or placeholder copy in the Razer section of `LightingView.xaml` should be removed now that the Razer backend is functional.
- `SystemControlViewModel.cs` AMD GPU initialization is still a placeholder for future ADLX integration.

Status update:

- Added `src/OmenCore.Desktop/README.md` archive guidance and a matching archived-project marker comment in `OmenCore.Desktop.csproj`.
- Remaining scope in this item is user-facing placeholder copy and unsupported AMD GPU tuning messaging cleanup.

#### Plan

**Step 1 — Archive or delete `src/OmenCore.Desktop/` (~10 min)**

- Delete the entire `src/OmenCore.Desktop/` directory tree
- It contains `OmenCore.Desktop.csproj` (net8.0, Avalonia 11.2.3, version 3.3.1) with no project references to the main OmenCoreApp — it is fully orphaned
- Alternatively, move it to a `src/_archived/OmenCore.Desktop/` folder if history is desired
- Remove any references to it in version-bump scripts, CI, or release documentation

**Step 2 — Remove macro profile placeholder copy from Lighting tab (~20 min)**

- Search `src/OmenCoreApp/ViewModels/LightingViewModel.cs` for "macro" and "placeholder" (expect a log message around line 2010+ area)
- Remove the placeholder log message
- In the corresponding `LightingView.xaml`, remove or disable the macro profile UI section
- If the macro profile dropdown/list is bound to data, either remove the XAML section or replace it with a "Macro profiles are not supported in this version" static message

**Step 3 — Remove residual "coming soon" copy (~15 min)**

- Run a workspace-wide search for "coming soon", "placeholder", "TODO", "not yet implemented" across `*.xaml` and `*.cs` files in `src/OmenCoreApp/Views/` and `src/OmenCoreApp/ViewModels/`
- For each match in a release-facing surface:
  - If the feature is now shipped (e.g., Razer backend): remove the qualifier
  - If the feature is genuinely absent: either hide the UI element or replace with "Not available" static text
- Leave internal `// TODO` code comments that are not user-facing

**Step 4 — Mark AMD GPU tuning as unsupported (~10 min)**

- In `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`, find the AMD GPU initialization code (`_amdGpuService` field area)
- Ensure the UI presents this as "AMD GPU tuning: Not available — NVIDIA only" rather than suggesting imminent ADLX support
- In the corresponding XAML view, if there's an AMD section with disabled controls, replace with a clear static message

Files to modify:
- `src/OmenCore.Desktop/` (delete)
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/Views/LightingView.xaml`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp/Views/SystemControlView.xaml` (if AMD section exists)

### 2. Add minimal automated coverage for Linux and Avalonia

Area: Code Quality, Maintainability, Release Readiness

Evidence:

- `src/OmenCoreApp.Tests` contains Windows/WPF-centered tests only.
- No Linux-, Avalonia-, or Desktop-specific tests were found.
- The Linux and Avalonia codepaths currently rely too heavily on manual confidence.

#### Plan

This is the largest single work item in the roadmap. See the dedicated **Test Proposals** section at the end of this document for the full breakdown of ~60–75 proposed tests. The highest-priority test targets are:

**Tier 1 — Pure-logic static methods, zero dependencies (~25 tests, ~2 hours)**

1. `LinuxCapabilityClassifier.Assess()` — 15–20 tests exercising every `LinuxCapabilityClass` output and reason string. This is a single static method with 15 boolean parameters and 2 string parameters. Every branch is testable with zero I/O.
2. `LinuxEcController.CheckUnsafeEcModel()` — 6–8 tests for model/board ID matching. Private static method, access via reflection (established pattern).

**Tier 2 — Service-level with fake interfaces (~25 tests, ~2 hours)**

3. `FanCurveService` interpolation, presets, validation — 15–20 tests. Requires a simple `IHardwareService` fake returning known `HardwareStatus` values. Tests cover:
   - `InterpolateFanSpeed`: below first point, above last point, linear interpolation, empty curve returns 50, single-point curve
   - `GetPreset`: known name returns copy, unknown name falls back to Balanced
   - `SavePreset`: empty name throws, whitespace throws, <2 points throws, clamps 0–100, sorts by temperature
   - `SetCpuFanCurve`/`GetCpuFanCurve`: returns defensive copy, sorts unsorted input
   - `ApplyAsync`: with fake hardware service returning known temps → correct fan speeds written

4. `LinuxHardwareService.HasStatusChanged()` — 5–6 tests for the private static comparison method. Tests cover CPU temp threshold, GPU temp threshold, fan RPM exact match.

**Tier 3 — ViewModel state and navigation (~10 tests, ~1 hour)**

5. `MainWindowViewModel` — 8–10 tests for navigation commands, capability initialization, and error paths. Requires fakes for `IHardwareService`, `IConfigurationService`, and 4 child VMs.

**Where to put the tests:**

Create a new test project `src/OmenCore.Avalonia.Tests/OmenCore.Avalonia.Tests.csproj`:
- Target `net8.0` (not `net8.0-windows` — these tests must run cross-platform)
- Reference `OmenCore.Avalonia` and `OmenCore.Linux` projects
- Reference `xunit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`
- Follow the same patterns as `OmenCoreApp.Tests`: manual fakes (no Moq), reflection for private methods, `[Fact]` and `[Theory]`/`[InlineData]`
- Add to `OmenCore.sln`

For `LinuxCapabilityClassifier` tests specifically, since it's in `OmenCore.Linux` which targets `net8.0`, a new project `src/OmenCore.Linux.Tests/OmenCore.Linux.Tests.csproj` may be cleaner. Both test projects would follow the same conventions.

Files to create:
- `src/OmenCore.Avalonia.Tests/OmenCore.Avalonia.Tests.csproj`
- `src/OmenCore.Avalonia.Tests/Services/FanCurveServiceTests.cs`
- `src/OmenCore.Avalonia.Tests/Services/LinuxHardwareServiceTests.cs`
- `src/OmenCore.Avalonia.Tests/ViewModels/MainWindowViewModelTests.cs`
- `src/OmenCore.Linux.Tests/OmenCore.Linux.Tests.csproj`
- `src/OmenCore.Linux.Tests/Hardware/LinuxCapabilityClassifierTests.cs`
- `src/OmenCore.Linux.Tests/Hardware/LinuxEcControllerTests.cs`

Recommendation:

- Prioritize Tier 1 and Tier 2 — they cover the most logic with the least setup.
- Tier 3 (ViewModel tests) is valuable but involves more scaffolding and can be deferred if time is tight.
- Keep this narrow. A small cross-platform regression net is far more valuable than another feature for v3.4.

### 3. Finalize model capability data for the v3.4 support matrix ✅ FIXED (data + tests)

Area: Feature Completeness, Release Readiness

Context — what's already working:

- v3.2.5 shipped 8BB1/Victus disambiguation with `ModelNamePattern` and `TryDisambiguateByModelName()`.
- v3.3.0 shipped the Model Identity Confidence + Ambiguity Transparency feature with a resolution summary card in Settings.
- v3.3.1 added 3 community-sourced model entries with explicit `UserVerified=false` tagging and 15 new fallback tests.
- The infrastructure is sound — this is about data completeness, not architecture.

Remaining gaps:

- At least one entry uses `ProductId = "8D42"` with a placeholder comment while being treated as production data.
- Multiple entries remain `UserVerified = false` without a path to verification.
- The fallback path is safe, but a final release should distinguish verified from inferred support.

#### Plan

**Step 1 — Audit the capability database entries (~30 min)**

- Open the model capability database source file (likely `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` or similar)
- Search for `8D42` and any entries with `// placeholder`, `// TODO`, or `// unverified` comments
- For each: either remove the placeholder comment if the entry is backed by real data, or mark the entry with `UserVerified = false` and `VerificationSource = "inferred"` (or similar)

**Step 2 — Tag all entries with verification status (~20 min)**

- Ensure every entry in the database has an explicit `UserVerified` field
- Entries from community submissions should retain `UserVerified = false` with a note about the submission source
- Entries validated on physical hardware should have `UserVerified = true`

**Step 3 — Generate support matrix documentation (~30 min)**

- Create `docs/supported-models-3.4.0.md`
- List all models grouped by verification status: Verified, Community-Reported, Fallback/Inferred
- Include for each: Product ID, model name, capability class (FullControl / ProfileOnly / etc.), backend (WMI / EC / profile), verification source

**Step 4 — Extend model capability tests (~20 min)**

- In `src/OmenCoreApp.Tests/Hardware/ModelCapabilityDatabaseTests.cs`, add tests for:
  - Every `UserVerified = false` entry still returns a valid capability (regression guard)
  - The `8D42` entry (if retained) returns expected values
  - Any newly added community entries
- Target: 5–8 additional tests

Files to modify:
- Model capability database source file
- `src/OmenCoreApp.Tests/Hardware/ModelCapabilityDatabaseTests.cs`
- `docs/supported-models-3.4.0.md` (new)

### 4. Complete deferred v3.3.1 implementation steps

Area: Core System Improvements, Release Readiness

Evidence:

- STEP-09 (monitoring dispatch simplification) was deferred because it requires 60 seconds of hardware chart observation on an OMEN PC.
- STEP-13 (`KeyboardLightingService` async init) was deferred as high risk requiring hardware sign-off before merge.
- Both are documented in the v3.3.1 changelog as explicitly pending.

#### Plan

**STEP-09 — Monitoring dispatch simplification**

What it is: Simplify the WmiBiosMonitor monitoring dispatch path by removing redundant event aggregation or intermediate buffering, reducing the number of hops between the hardware worker and the UI.

Validation requirement: After applying the change, observe the Dashboard hardware charts for 60 seconds on a physical OMEN PC. CPU temperature, GPU temperature, fan RPM, and power draw must update at the expected polling interval without gaps, freezes, or erratic spikes.

- If hardware is available: apply the change, validate visually, merge
- If hardware is unavailable: add a focused integration test that runs `WmiBiosMonitor` with a mock worker and verifies that status events arrive at the expected cadence (e.g., 10 events in 10 seconds ± 2). This test can run without hardware and provides partial confidence.

**STEP-13 — KeyboardLightingService async init**

What it is: Convert the synchronous `KeyboardLightingService.Initialize()` call to `async Task InitializeAsync()` so it doesn't block the UI thread during startup, especially on systems where USB enumeration is slow.

Risk: If the async conversion changes the initialization order relative to other startup services (fan control, monitoring, OSD), it could cause a race condition where the keyboard service tries to write to a device handle that hasn't been enumerated yet.

- If hardware is available: apply the change, test startup on OMEN Max (per-key RGB), OMEN 16 (4-zone), and a non-OMEN laptop (no RGB). Verify lighting applies within 2 seconds of dashboard ready.
- If hardware is unavailable: document as "deferred to post-3.4.0 patch" with the note that it is a startup-time-only improvement and does not affect runtime behavior. Ensure the synchronous path remains safe.

Files to modify (when ready):
- `src/OmenCoreApp/Services/WmiBiosMonitor.cs` (STEP-09)
- `src/OmenCoreApp/Services/KeyboardLightingService.cs` (STEP-13)
- `src/OmenCoreApp/App.xaml.cs` (STEP-13, startup ordering)

### 5. Add a manual recovery action for the monitoring stack

Area: Functional Gaps, UI/UX

Context — what's already in place:

- v3.2.5 fixed the watchdog sleep/wake race (explicit suspend/resume handlers, 120s post-resume grace).
- v3.3.0 shipped resume recovery diagnostics with a visible timeline, post-resume self-check, and the monitoring subscriber isolation fix.
- v3.3.0 fixed the CPU-temperature-frozen-after-sleep bug by making `TryRestartAsync()` dispose and relaunch the worker.
- The monitoring stack is now significantly self-healing. Most resume and worker failures recover automatically.

Remaining gap:

- There is still no user-facing manual restart action for the monitoring backend when automatic recovery fails.
- The diagnostics panel and EC reset exist, but a user who sees degraded monitoring has no single button to force a restart.

#### Plan

**Step 1 — Add a "Restart Monitoring" command to the Diagnostics or Settings view (~30 min)**

- In `src/OmenCoreApp/ViewModels/DiagnosticsViewModel.cs` (or `SettingsViewModel.cs`):
  - Add a `RestartMonitoringCommand` (RelayCommand or ICommand)
  - The command handler calls `WmiBiosMonitor.TryRestartAsync()` which already exists and handles NVAPI state reset, temp fallback disposal, and worker relaunch
  - Add a brief "Restarting…" status message while the restart is in progress
  - On completion, update status to "Monitoring restarted successfully" or "Restart failed — check diagnostics log"

- In the corresponding XAML view:
  - Add a button: "Restart Hardware Monitoring"
  - Place it in the Diagnostics panel near the existing EC reset action, or in Settings under an "Advanced" section
  - Bind `IsEnabled` to `!IsRestarting` to prevent double-clicks

**Step 2 — Wire the backend (~15 min)**

- The `WmiBiosMonitor.TryRestartAsync()` method already exists. Verify its signature is accessible from the ViewModel layer.
- If it's not directly accessible (e.g., behind a service interface), expose it through the existing `IMonitoringService` or `IHardwareWorkerClient` interface.
- Log the manual restart action to the diagnostics timeline: `"Manual monitoring restart triggered by user"`

**Step 3 — Add minimal test coverage (~15 min)**

- In `src/OmenCoreApp.Tests/`, add a test that verifies the `RestartMonitoringCommand` can execute (CanExecute returns true) and that calling it doesn't throw when the worker is not running.

Files to modify:
- `src/OmenCoreApp/ViewModels/DiagnosticsViewModel.cs` (or `SettingsViewModel.cs`)
- Corresponding XAML view
- `src/OmenCoreApp/Services/WmiBiosMonitor.cs` (if interface exposure needed)
- `src/OmenCoreApp.Tests/` (new test)

### 6. Move remaining experimental switches behind an explicit advanced gate

Area: UI/UX, Release Readiness, Maintainability

Context — what v3.3.0 already addressed:

- Lite Mode was shipped as an opt-in toggle in Settings that hides advanced surfaces from the main window.
- Startup hardware restore was disabled by default and gated behind model-aware checks (`EnableStartupHardwareRestore`, `AllowStartupRestoreOnOmen16OrVictus`).
- GPU OC guardrails, 30-second Test Apply flow, and device-aware range detection were shipped.

Remaining gaps:

- Experimental firmware Fn+P profile cycling and experimental EC keyboard toggles are still in the main settings path, not behind Lite Mode or an explicit Advanced section.
- Lite Mode controls *tab visibility* but not individual dangerous toggles within the Settings page itself.

#### Plan

**Step 1 — Identify experimental toggles in Settings (~15 min)**

- Open `src/OmenCoreApp/ViewModels/SettingsViewModel.cs` and search for "experimental", "Fn+P", "EC keyboard", "firmware"
- List every toggle that can directly write to EC registers, cycle firmware profiles, or modify low-level keyboard behavior
- Cross-reference with `src/OmenCoreApp/Views/SettingsView.xaml` to see how each is currently presented

**Step 2 — Add an "Advanced / Experimental" collapsible section (~30 min)**

- In `SettingsView.xaml`:
  - Wrap the identified experimental toggles in an `Expander` control with header "Advanced / Experimental"
  - Default `IsExpanded="False"` so the section is collapsed by default
  - Add a warning message inside: "These settings interact directly with firmware and may cause unexpected behavior on unsupported hardware."
- In `SettingsViewModel.cs`:
  - Add a `ShowAdvancedSection` property bound to the expander's visibility
  - When Lite Mode is enabled, set `ShowAdvancedSection = false` to hide the expander entirely

**Step 3 — Test the gate (~10 min)**

- Verify Lite Mode hides the Advanced section
- Verify non-Lite-Mode users can still expand and access the toggles
- Verify the toggles still function correctly when accessed through the new UI structure

Files to modify:
- `src/OmenCoreApp/Views/SettingsView.xaml`
- `src/OmenCoreApp/ViewModels/SettingsViewModel.cs`

### 7. Align product messaging with real behavior

Area: UI/UX, Release Readiness, Trust

Evidence:

- `README.md` says `No telemetry`, while `TelemetryService.cs` stores local-only anonymous PID success/failure counts and `SettingsView.xaml` exposes a user toggle for it. No data is sent over the network, but the README claim is misleading.
- The admin requirement (`requestedExecutionLevel=requireAdministrator`) is not explained in release-facing copy.
- v3.3.0 shipped structured telemetry logging with context fields (`component`, `operation`, `model`, `os`) — this is good internal tooling but makes the "no telemetry" claim even more confusing to users who inspect the code.

#### Plan

**Step 1 — Update README telemetry language (~10 min)**

- In `README.md`, find the "No telemetry" claim
- Replace with: "**No outbound telemetry** — OmenCore collects local-only diagnostic counters (success/failure counts per operation) that never leave your machine. A toggle in Settings lets you disable even local collection."
- If there's a features bullet list, update it to: "Privacy-first: no network telemetry, no analytics, no phone-home"

**Step 2 — Explain admin requirement (~15 min)**

- In `README.md`, add a "Why does OmenCore require administrator?" section (or add to FAQ/INSTALL.md):
  - "OmenCore requires administrator privileges to access low-level hardware interfaces: WMI BIOS methods for fan control and thermal management, EC (Embedded Controller) registers via PawnIO/WinRing0, NVAPI for GPU monitoring and overclocking, and LibreHardwareMonitor for sensor access. These operations require ring-0 or admin-level access on Windows."
- In the Inno Setup installer (`installer/OmenCoreInstaller.iss`), add or update the info-before page to mention the admin requirement
- In `src/OmenCoreApp/Views/SettingsView.xaml`, if there's a help section, add a brief explanation

**Step 3 — Verify telemetry toggle wording in Settings (~10 min)**

- Check the `SettingsView.xaml` telemetry toggle label and tooltip
- Ensure it says something like "Enable local diagnostic logging" rather than just "Telemetry"
- The toggle should be clear that disabling it only stops local counter collection, not logging

Files to modify:
- `README.md`
- `INSTALL.md` (optional)
- `installer/OmenCoreInstaller.iss` (optional)
- `src/OmenCoreApp/Views/SettingsView.xaml` (if toggle wording needs update)

### 8. Freeze scope on non-core utilities for v3.4.0 ✅ PARTIALLY FIXED (freeze note + QA checklist added; process enforcement ongoing)

Area: Feature Completeness, Maintainability

Context:

- v3.3.0 shipped substantial work across Bloatware Manager (~7 items including staged rollback, restore points, startup scan, HP-specific detection, preview mode) and Memory Optimizer (~4 items including adaptive profiles, per-process exclusions, compression control, statistics dashboard). These are mature, user-requested features — not afterthoughts.
- System Optimizer got drift detection, batch apply, and risk-assessment detail views.
- v3.2.5 shipped Win32 uninstall fixes, admin preflight, and per-item removal reporting.

These features are actively maintained and used. The recommendation is not to deprecate them, but to freeze their scope for v3.4.0:

- No new features in Bloatware, Memory Optimizer, System Optimizer, or Game Library for v3.4.0.
- Focus QA pass on fan control, thermal telemetry, model detection, lighting stability, update flow, resume recovery, and monitoring resilience.
- Fix bugs if reported, but do not expand functionality.

#### Plan

This is a process/discipline item, not a code change. To enforce it:

1. **Document the freeze** — Add a section to the top of `CHANGELOG.md` (draft v3.4.0 section): "Scope freeze: Bloatware Manager, Memory Optimizer, System Optimizer, and Game Library receive bug fixes only in v3.4.0. No new features."
2. **QA pass priorities** — Create a focused manual QA checklist (`qa/v3.4.0-checklist.md`) covering:
   - Fan control: WMI, EC, profile modes on at least 2 model classes
   - Thermal telemetry: CPU/GPU temp, fan RPM, power draw accuracy
   - Model detection: capability classifier output on at least 3 Product IDs
   - Lighting: per-key (Max), 4-zone (16), Razer, Corsair, Logitech
   - Update flow: download, hash verify, install, rollback
   - Resume recovery: sleep → wake → monitoring resumes within 120s grace
   - Monitoring resilience: worker crash → auto-restart
3. **Resist scope creep** — If a non-core feature request comes in during v3.4.0 development, tag it "v3.5-candidate" and defer

Files to create:
- `qa/v3.4.0-checklist.md`

Status update:

- Scope-freeze policy note added to `CHANGELOG.md` under v3.4.0.
- QA checklist created at `qa/v3.4.0-checklist.md`.
- Remaining work is process discipline during ongoing triage (defer non-core feature requests to post-v3.4 planning).

### 9. Resolve GitHub #108 (Linux black screen) or document workaround

Area: Linux, Release Readiness

Evidence:

- GitHub #108 is tracked as pending with no repro steps (v3.3.1 changelog).
- v3.3.0 shipped significant Linux GUI startup hardening: DBus recovery, AT-SPI bridge suppression, automatic software-render retry, persisted last-known-good render mode, and an in-app fallback banner.
- The `OMENCORE_GUI_RENDER_MODE=software` workaround exists.

#### Plan

**Step 1 — Attempt reproduction on common configurations (~1 hour)**

Test matrix (pick 2–3 available):
- Ubuntu 24.04 + Wayland (GNOME) on OMEN hardware
- Ubuntu 24.04 + X11 on OMEN hardware
- Fedora 39+ + Wayland on non-OMEN hardware
- Arch + X11 on any hardware

For each: launch OmenCore.Avalonia without any environment variable overrides. If black screen:
- Check if the software-render fallback triggered automatically
- Check `~/.config/omencore/render-mode` for persisted mode
- Check stderr for Avalonia rendering errors
- Record GPU model, driver version, Wayland compositor version

**Step 2 — Document the workaround prominently (~15 min)**

Regardless of repro success:
- In `INSTALL.md` Linux section, add: "If you see a black screen on startup, set `OMENCORE_GUI_RENDER_MODE=software` in your environment and relaunch. OmenCore will remember this preference."
- In `README.md` Linux section, add a "Troubleshooting" subsection with the same
- In the Avalonia startup code, verify the automatic software-render retry is logged clearly enough for users to diagnose via terminal output

**Step 3 — If repro succeeds, fix (~varies)**

- If the issue is Wayland-specific: add Wayland detection to the auto-fallback logic in `MainWindowViewModel` or the Avalonia `Program.cs` entry point
- If the issue is driver-specific: document known-bad GPU/driver combinations
- If the issue is timing-related: add a startup delay or retry loop before the first render

**Step 4 — Close or update the GitHub issue (~5 min)**

- If no repro after testing 3+ configurations: close #108 with "Unable to reproduce. Automatic software-render fallback was added in v3.3.0. Please reopen with GPU model, distro, and Wayland/X11 info."
- If repro + fix: close with fix reference
- If repro + no fix: update with repro details and the workaround

Files to modify:
- `INSTALL.md`
- `README.md`
- Potentially `src/OmenCore.Avalonia/Program.cs` or `src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs`

### 10. Fix `async void` non-event-handler methods across ViewModels ✅ FIXED

Area: Code Quality, Reliability, Crash Prevention

Evidence:

**Windows (OmenCoreApp) — 12 instances:**

| Severity | File | Line | Method |
|----------|------|------|--------|
| HIGH | `SystemOptimizerViewModel.cs` | 1104 | `public async void Toggle(bool desiredState)` |
| HIGH | `MainViewModel.cs` | 2018 | `private async void InitializeGameProfilesAsync()` |
| HIGH | `MainViewModel.cs` | 2054 | `private async void OnProfileApplyRequested(...)` |
| HIGH | `MainViewModel.cs` | 2985 | `private async void InitializeServicesAsync()` — critical startup |
| MEDIUM | `LightingViewModel.cs` | 2115 | `private async void ApplyTemperatureBasedLighting(...)` |
| MEDIUM | `LightingViewModel.cs` | 2177 | `private async void ApplyThrottlingLighting(...)` |
| MEDIUM | `LightingViewModel.cs` | 2224 | `private async void ApplyPerformanceModeLighting(...)` |
| MEDIUM | `GameProfileManagerViewModel.cs` | 231 | `private async void DeleteProfile()` |
| MEDIUM | `GameProfileManagerViewModel.cs` | 259 | `private async void ImportProfiles()` |
| MEDIUM | `GameProfileManagerViewModel.cs` | 284 | `private async void ExportProfiles()` |
| MEDIUM | `GameProfileManagerViewModel.cs` | 327 | `private async void Save()` |
| LOW | `AsyncRelayCommand.cs` | 24 | `public async void Execute(...)` — ICommand requirement |

**Avalonia — 3 instances:**

| Severity | File | Line | Method |
|----------|------|------|--------|
| HIGH | `DashboardViewModel.cs` | 104 | `private async void Initialize()` — bare `catch { }` swallows all errors |
| HIGH | `MainWindowViewModel.cs` | 134 | `private async void Initialize()` |
| HIGH | `SystemControlViewModel.cs` | 66 | `private async void Initialize()` |

Why this is high priority:

- `async void` methods that are NOT WPF event handlers will crash the process on unhandled exceptions in .NET 8.
- `InitializeServicesAsync` is called during startup — if it throws, the app silently fails to initialize services with no user feedback.

#### Plan

**Step 1 — Convert to `async Task` and add safe fire-and-forget wrapper (~1 hour)**

Create a small helper in `src/OmenCoreApp/Utils/` (or extend `AsyncRelayCommand`):

```csharp
public static class TaskExtensions
{
    public static async void SafeFireAndForget(this Task task, Action<Exception>? onError = null)
    {
        try { await task; }
        catch (Exception ex) { onError?.Invoke(ex); }
    }
}
```

For each non-event-handler `async void`:
- Change signature from `async void MethodName()` to `async Task MethodNameAsync()`
- At the call site, use `MethodNameAsync().SafeFireAndForget(ex => Debug.WriteLine(ex))` or `_ = MethodNameAsync()` with a top-level try/catch inside the method
- For the Avalonia VMs: same pattern, ensure `DashboardViewModel.Initialize()` logs errors instead of swallowing via bare catch

**Step 2 — Leave legitimate event handlers as `async void`**

These are acceptable and should NOT be changed: `MainViewModel_PropertyChanged`, `DashboardViewModel_PropertyChanged`, `HardwareMonitoringDashboard_Loaded`, `RefreshDataButton_Click`, etc. (WPF `RoutedEventArgs` / `EventArgs` signature handlers).

Files to modify:
- `src/OmenCoreApp/ViewModels/MainViewModel.cs`
- `src/OmenCoreApp/ViewModels/SystemOptimizerViewModel.cs`
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs`
- `src/OmenCoreApp/ViewModels/GameProfileManagerViewModel.cs`
- `src/OmenCoreApp/Utils/AsyncRelayCommand.cs` (or new `TaskExtensions.cs`)
- `src/OmenCore.Avalonia/ViewModels/DashboardViewModel.cs`
- `src/OmenCore.Avalonia/ViewModels/MainWindowViewModel.cs`
- `src/OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs`

### 11. Fix sync-over-async deadlock risks (`.Result` / `.Wait()`) ✅ PARTIALLY FIXED (RgbNetSystemProvider + continuation cleanup)

Area: Code Quality, Reliability, Deadlock Prevention

Evidence:

| Severity | File | Line | Pattern |
|----------|------|------|---------|
| HIGH | `RgbNetSystemProvider.cs` | 71 | `Task.Delay(250).Wait()` — nonsensical in async method |
| HIGH | `ThermalSensorProvider.cs` | 68–71 | `readTask.Wait(250)` then `.Result` — deadlock risk on UI thread |
| HIGH | `LibreHardwareMonitorImpl.cs` | 1743–1744 | `task.Wait(500)` then `.Result` — bounded sync-over-async |
| HIGH | `WmiBiosMonitor.cs` | 949–953 | `fallbackTask.Wait()` then `.Result` — sync wait on fallback temp read |
| MEDIUM | `PerformanceModeService.cs` | 78 | `t.Result` inside `ContinueWith` — fragile pattern |

#### Plan

- `RgbNetSystemProvider.cs:71`: Change `Task.Delay(250).Wait()` to `await Task.Delay(250)` (method is already async)
- `ThermalSensorProvider.cs` / `LibreHardwareMonitorImpl.cs`: Convert the bounded `.Wait(ms)` + `.Result` pattern to `await` with `Task.WhenAny(task, Task.Delay(timeout))` or use `CancellationTokenSource` with timeout
- `WmiBiosMonitor.cs:953`: Change to `var result = await fallbackTask` if the caller is async; otherwise use `Task.WhenAny` with timeout
- For each fix, verify the caller chain supports async propagation; if a sync boundary is required (e.g., IDisposable.Dispose), document the reason

Files to modify:
- `src/OmenCoreApp/Services/Rgb/RgbNetSystemProvider.cs`
- `src/OmenCoreApp/Hardware/ThermalSensorProvider.cs`
- `src/OmenCoreApp/Hardware/LibreHardwareMonitorImpl.cs`
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`

### 12. Fix DashboardView Grid.Row overlap bug ✅ FIXED

Area: UI/UX, Layout Bug

Evidence:

- In `src/OmenCoreApp/Views/DashboardView.xaml`, the **telemetry stale banner** (`Grid.Row="1"`) and the **Quick Status Bar** (`Grid.Row="1"`) both occupy the same grid row.
- The telemetry banner has a `Visibility` binding but the status bar does not — when the banner is visible, both controls render on top of each other.
- This is the same class of bug as the FanControlView Grid.Row overlap (item 0a).

#### Plan

Shift the Quick Status Bar to `Grid.Row="2"` and add a 4th row definition, then update subsequent row references. Same fix pattern as item 0a.

Files to modify:
- `src/OmenCoreApp/Views/DashboardView.xaml`

### 13. Fix un-themed views: GameLibraryView, InputPromptWindow, QuickPopupWindow ✅ FIXED (InputPromptWindow, GameProfileManagerView, GameLibraryView)

Area: UI/UX, Theme Consistency

Evidence:

- **GameLibraryView.xaml**: `Background="#1a1a1a"` on the UserControl, every style uses raw hex (`#252525`, `#3a3a3a`, `#C4002A`, `#333`, `#444`, `#888`). Will not respond to theme changes.
- **InputPromptWindow.xaml**: Zero `Background`, `Foreground`, `Style`, or `StaticResource` references. Renders with WPF system default **light theme** (white background, black text) — visually jarring against the dark-themed app. No `WindowStyle="None"` / `AllowsTransparency` like other windows.
- **QuickPopupWindow.xaml**: Redeclares `SurfaceDarkBrush`, `SurfaceMediumBrush`, `AccentPrimaryBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `BorderBrush` locally with **different Color values** than `ModernStyles.xaml`. Will drift from theme over time.
- **GameProfileManagerView.xaml**: Overrides global `Button` and `TextBox` styles with hardcoded colors (`#2D2D30`, `#3F3F46`, `#3E3E42`) instead of using `ModernButton`.

#### Plan

- **GameLibraryView.xaml**: Replace all hardcoded hex colors with `{StaticResource SurfaceDarkBrush}`, `{StaticResource TextPrimaryBrush}`, `{StaticResource AccentBrush}`, etc. from `ModernStyles.xaml`
- **InputPromptWindow.xaml**: Add `Background="{StaticResource SurfaceDarkBrush}"`, `Foreground="{StaticResource TextPrimaryBrush}"`, and match the window chrome of other popup windows
- **QuickPopupWindow.xaml**: Delete the local `SolidColorBrush` redefinitions and replace references with app-level `{StaticResource ...}` keys from `ModernStyles.xaml`
- **GameProfileManagerView.xaml**: Remove the local `Button`/`TextBox` style overrides, use app-level `ModernButton` and `ModernTextBox` styles

Files to modify:
- `src/OmenCoreApp/Views/GameLibraryView.xaml`
- `src/OmenCoreApp/Views/InputPromptWindow.xaml`
- `src/OmenCoreApp/Views/QuickPopupWindow.xaml`
- `src/OmenCoreApp/Views/GameProfileManagerView.xaml`

### 14. Fix CI/CD pipeline: broken artifact sharing, build inconsistencies ✅ FIXED

Area: Release Engineering, CI/CD

Evidence:

| # | Issue | Severity |
|---|-------|----------|
| 1 | `ci.yml` `wmi-v2-tests` and `integration-tests` jobs use `--no-build` but depend on a separate `build-and-test` job with **no `upload-artifact`** step — tests run against non-existent binaries on fresh runners | HIGH |
| 2 | `release.yml` Linux build does NOT use `build-linux-package.ps1` — manually runs `dotnet publish`, produces `.tar.gz` instead of `.zip`, no version injection, no Avalonia GUI, no SHA256 | CRITICAL |
| 3 | `linux-qa.yml` uses `PublishTrimmed=true` vs local scripts using `false` — trimmed builds may miss reflection-based code | HIGH |
| 4 | `linux-qa.yml` hardcodes `net8.0` in path — breaks on .NET 9 upgrade | MEDIUM |
| 5 | `ci.yml` triggers on `v2.0-dev` branch — likely stale | LOW |
| 6 | `alpha.yml` Linux build also bypasses `build-linux-package.ps1` | MEDIUM |
| 7 | All workflows use `actions/checkout@v3` / `actions/setup-dotnet@v3` — outdated, should be v4 | LOW |

#### Plan

**Step 1 — Fix artifact sharing in `ci.yml`:**
- Add `actions/upload-artifact@v4` after the build step in `build-and-test`
- Add `actions/download-artifact@v4` in `wmi-v2-tests` and `integration-tests`
- Or remove `--no-build` and let each job build independently

**Step 2 — Align `release.yml` with `build-linux-package.ps1`:**
- Replace the manual `dotnet publish` + `tar` step with a call to `build-linux-package.ps1`
- This ensures version injection, SHA256 generation, Avalonia GUI inclusion, and `.zip` format consistency

**Step 3 — Fix `linux-qa.yml` trimming mismatch:**
- Change `PublishTrimmed=true` to `PublishTrimmed=false` to match local build scripts
- Or add `[DynamicDependency]` attributes to reflection-heavy code if trimming is desired

**Step 4 — Update action versions to v4 across all workflows**

Files to modify:
- `.github/workflows/ci.yml`
- `.github/workflows/release.yml`
- `.github/workflows/linux-qa.yml`
- `.github/workflows/alpha.yml`

### 15. Fix version injection gap: Windows builds don't embed version in assemblies ✅ FIXED

Area: Release Engineering, Version Management

Evidence:

- `build-linux-package.ps1` passes `-p:Version=$version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion` to `dotnet publish`
- `build-installer.ps1` does NOT pass any `-p:Version` flag — Windows assemblies may not contain the correct version metadata
- The Inno Setup `.iss` file has a hardcoded fallback `#define MyAppVersion "3.3.1"` that silently activates if the build script doesn't pass the version

#### Plan

- Add `-p:Version=$version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion` to the `dotnet publish` command in `build-installer.ps1`
- Add the same version format regex validation from `build-linux-package.ps1`
- Verify the actual output executable name matches `{#MyAppExeName}` in the `.iss` file (reported as `OmenCore.exe` in the installer but the project may produce `OmenCoreApp.exe`)

Files to modify:
- `build-installer.ps1`
- `installer/OmenCoreInstaller.iss` (verify exe name)

### 16. Fix PerformanceMode enum mismatch between Avalonia and Linux CLI ✅ FIXED (explicit mapping + remove unsupported Custom mode)

Area: Functional Bug, Linux

Evidence:

| Avalonia `IHardwareService.cs:53` | Linux CLI `LinuxEcController.cs:1252` |
|---|---|
| `Quiet` | `Default` |
| `Balanced` | `Balanced` |
| `Performance` | `Performance` |
| `Custom` | `Cool` |

- These are two different enums with different members and ordinal values.
- `SystemControlViewModel.cs` casts `SelectedPerformanceModeIndex` (raw int) to `PerformanceMode` — if index doesn't match, the wrong mode is applied.
- "Custom" mode in Avalonia has no backend; "Cool" and "Default" in Linux CLI are not exposed in Avalonia UI.

#### Implemented

- Introduced an explicit mapping layer in `SystemControlViewModel` for both mode-name and index conversions (removed raw `(PerformanceMode)value` cast).
- Removed unsupported `Custom` from the Avalonia `PerformanceMode` enum and from the System Control UI.
- Kept Linux backend semantics stable by mapping available low-power variants (`low-power` / `cool` / `quiet`) to Avalonia `Quiet` via existing `LinuxHardwareService` profile translation.

Files modified:
- `src/OmenCore.Avalonia/Services/IHardwareService.cs`
- `src/OmenCore.Avalonia/ViewModels/SystemControlViewModel.cs`
- `src/OmenCore.Avalonia/Views/SystemControlView.axaml`

### 17. Add EC/sysfs concurrency protection in Linux code ✅ FIXED (EC lock + serialized Avalonia I/O)

Area: Reliability, Hardware Safety

Evidence:

- `LinuxEcController.cs`: `ReadByte()` (line 367) and `WriteByte()` (line 383) open `FileStream` to `/sys/kernel/debug/ec/ec0/io` with `FileShare.ReadWrite` but **no locking mechanism** — no `lock`, `Mutex`, `Semaphore`, or `Monitor` anywhere in the file.
- `LinuxHardwareService.cs`: Zero concurrency primitives. The 1-second polling timer can overlap with user-initiated operations.
- Interleaved seek+read/write on EC sysfs can produce corrupted reads/writes and put the EC in an undefined state.

#### Implemented

- Added a shared EC critical section in `LinuxEcController` so `ReadByte()` and `WriteByte()` cannot interleave byte-level seeks/writes across threads.
- Added a serialized `SemaphoreSlim` guard in Avalonia `LinuxHardwareService` for status polling and all control-plane writes (performance mode, fan speed, keyboard brightness/color).
- Split `SetPerformanceModeAsync` into a lock-aware core path so fan-fallback profile writes do not deadlock under re-entrant lock acquisition.

Files modified:
- `src/OmenCore.Linux/Hardware/LinuxEcController.cs`
- `src/OmenCore.Avalonia/Services/LinuxHardwareService.cs`

### 18. Fix dangerous defaults and conflicting config values ✅ FIXED

Area: Safety, Configuration

Evidence:

| # | Issue | Severity |
|---|-------|----------|
| 1 | `undervolt.defaultOffset` has `coreMv: -90, cacheMv: -60` — aggressive defaults that could cause instability on many chips. Should default to 0/0. | HIGH |
| 2 | `monitoring.pollIntervalMs: 1500` at line 119 conflicts with `monitoringIntervalMs: 750` at line 2. Two overlapping monitoring intervals. | HIGH |
| 3 | `monitoring.lowOverheadMode: false` — dead code since v3.3.1 but config key persists | MEDIUM |
| 4 | `systemToggles` includes `"HP Omen Background"` (`OmenCommand` service) with `enabledByDefault: true` and description "OEM telemetry" — contradicts the anti-bloat mission | MEDIUM |
| 5 | `lastPerformanceModeName`, `lastGpuPowerBoostLevel`, `lastFanPresetName` are runtime state mixed into default config template | LOW |

#### Plan

- Set `undervolt.defaultOffset` to `coreMv: 0, cacheMv: 0` — users must opt into undervolt
- Remove or reconcile the duplicate monitoring interval (determine which one the code actually reads and remove the other)
- Remove `monitoring.lowOverheadMode` if dead code
- Change `HP Omen Background` to `enabledByDefault: false` or remove from defaults
- Move runtime state keys to a separate section or remove from default template

Files to modify:
- `config/default_config.json`

### 19. Fix FanCurveEditor mouse capture / bounds crash (GitHub #30) ✅ FIXED (clamp-first snap + global drag release guards)

Area: UI Bug, Fan Control

Evidence: Fan curve chart doesn't release dragged point when mouse leaves control bounds. Throws `ArgumentException: '90' cannot be greater than 87` in `FanCurveEditor.xaml.cs:548`. Confirmed by multiple users — workaround is to click another dot.

#### Plan

- In `Point_MouseMove` handler: clamp Y-value to control bounds before applying: `newTemp = Math.Clamp(newTemp, MinTemp, MaxTemp)`, `newSpeed = Math.Clamp(newSpeed, 0, 100)`
- Add `Mouse.Capture(null)` in `Point_MouseUp` and on `MouseLeave` to release capture when cursor exits
- Guard against index-out-of-range when dragged point index exceeds curve points count
- Add bounds validation before the assignment that throws the ArgumentException

Files to modify:
- `src/OmenCoreApp/Views/Controls/FanCurveEditor.xaml.cs`

### 20. Fix bloatware removal silent no-op on Victus models (GitHub #107) ✅ FIXED (explicit no-op classification + per-item detail surfaced)

Area: Utility Feature, UX

Evidence: Bloatware removal appears to do nothing on Victus 15 — no error, no feedback. v3.2.5 fixed Win32 uninstall no-op but this may be AppX-specific or version-dependent.

#### Fix applied

- `BloatwareManagerService` now records explicit per-item outcomes for every removal attempt (`VerifiedSuccess`, `Skipped`, `Failed`) with `LastRemovalDetail` and aligned status/history logging.
- No-op paths now produce explicit `Skipped` detail instead of silent success: already-removed session items, absent AppX packages, absent Win32 uninstall entries, missing startup entries, and missing scheduled tasks.
- AppX removal now performs pre/post presence snapshots across current-user, all-users, and provisioned scopes; no-state-change outcomes are surfaced as explicit failures with reason.
- Bulk removal now classifies skipped/no-op items separately so they do not count as successful removals or trigger rollback behavior.
- `BloatwareManagerViewModel` now surfaces detailed single-item and bulk status summaries, including skip details and failure details.
- `BloatwareManagerView.xaml` now exposes per-item outcome text in a dedicated `Result Detail` column and status tooltip so no-op/failure reasons are visible in-list.
- Added regression coverage for no-op classification and outcome message composition.
- Validation: `dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj -c Debug` is green with **198 passed, 0 failed**.

Files modified:
- `src/OmenCoreApp/Services/BloatwareManagerService.cs`
- `src/OmenCoreApp/ViewModels/BloatwareManagerViewModel.cs`
- `src/OmenCoreApp/Views/BloatwareManagerView.xaml`
- `src/OmenCoreApp.Tests/Services/BloatwareManagerServiceTests.cs`
- `src/OmenCoreApp.Tests/ViewModels/BloatwareManagerViewModelOutcomeTests.cs`

### 21. Investigate AMD Ryzen AI 9 undervolt support gap (GitHub #103) ✅ PARTIALLY FIXED (short-term guard + UI disable)

Area: Hardware Compatibility, Feature Gap

Evidence: CPU undervolt doesn't apply on OMEN Max 16 with AMD Ryzen AI 9 HX 375. PawnIO installed, running as admin. OGH's "Advanced PBO" works. OmenCore's undervolt path likely targets Intel MSR or older AMD PBO registers — Zen 5 / Strix Point uses different CPPC3/Curve Optimizer interface.

#### Fix applied (short-term)

- Added runtime detection guard for Ryzen AI 9 family/model signature (`Family 0x1A`, `Model 0x40+`) in `RyzenControl` and marked Curve Optimizer as unsupported for this path.
- `AmdUndervoltProvider` now returns an explicit message for this case: "CPU Curve Optimizer is not yet supported on Ryzen AI 9 processors." and blocks apply attempts.
- `SystemControlViewModel` now treats this message as an unsupported capability state, which disables the apply action and surfaces the reason in UI.
- Added regression tests for Ryzen AI 9 guard parsing (decimal and hex family/model formats).

Validation: `dotnet test src/OmenCoreApp.Tests/OmenCoreApp.Tests.csproj -c Debug` green with **201 passed, 0 failed**.

#### Remaining plan (post-3.4.0)

- **Long-term (post-3.4.0):** Implement PBO Curve Optimizer via SMU mailbox for Zen 5 if technically feasible

Files modified:
- `src/OmenCoreApp/Hardware/RyzenControl.cs`
- `src/OmenCoreApp/Hardware/AmdUndervoltProvider.cs`
- `src/OmenCoreApp/ViewModels/SystemControlViewModel.cs`
- `src/OmenCoreApp.Tests/Hardware/RyzenControlTests.cs`

### 22. Fix fan max-mode sawtooth RPM pattern (GitHub #37) ✅ FIXED (max-mode keepalive + sustained-drop reapply)

Area: Fan Control, UX

Evidence: Fan set to max drops from 6000→2500 RPM periodically, then countdown re-applies max. Sawtooth pattern. User wants persistent max without timer-based re-apply cycle.

#### Fix applied

- In `WmiFanController.CountdownExtensionCallback`, max mode no longer blindly calls `SetFanMax(true)` every extension tick.
- Added max-mode maintenance window (`8s`) plus readback health checks (fan level + RPM) and only re-assert max after sustained low telemetry across 2 maintenance checks.
- Healthy max mode now uses lightweight `ExtendFanCountdown()` keepalive instead of hard max re-apply writes, which removes timer-driven oscillation on affected firmware.
- `SetMaxFanSpeed(bool)` now updates internal manual/max state and countdown-extension behavior consistently.
- In `FanService.ApplyMaxCooling`, removed redundant `SetFanSpeed(100)` for WMI backends (kept for non-WMI controllers) to avoid duplicate max pulses.
- Added regression tests covering max keepalive behavior and sustained-drop-triggered max re-apply.

Files modified:
- `src/OmenCoreApp/Hardware/WmiFanController.cs`
- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp.Tests/Hardware/WmiV2VerificationTests.cs`

Validation:
- `dotnet test` green with **203 passed, 0 failed**.

### 23. Fix Fn+F2/F3 brightness keys triggering OmenCore (GitHub #74) ✅ FIXED (LaunchApp scan whitelist + regression tests)

Area: Hotkey, UX

Evidence: Fn+brightness (F2/F3) opens/hides OmenCore. v3.3.0 fixed Fn+F6/F7 (scan code 0xE046), but LaunchApp-key handling still accepted non-dedicated scans that can overlap with brightness/Fn events on some models.

#### Fix applied

- In `OmenKeyService.IsOmenKey()`, hardened `VK_LAUNCH_APP1`/`VK_LAUNCH_APP2` handling to a narrow allow-list (`0xE045`) and rejected known brightness-conflict scans (`0xE046`, `0x0046`, `0x009D`).
- Added regression tests for both LaunchApp virtual keys to ensure brightness-conflict scans are always rejected.
- Added positive-path regression tests to preserve valid OMEN launch-scan detection.
- Validation: `dotnet test` green with **193 passed, 0 failed**.

Files modified:
- `src/OmenCoreApp/Services/OmenKeyService.cs`
- `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs`

### 24. Add fan-related safety disclaimer and post-apply RPM sanity check (GitHub #106)

Area: Safety, UX

Evidence: User reports permanent fan hardware failure after using OmenCore fan curves. While software WMI commands cannot damage fan hardware (this is a coincidental hardware failure), the perception creates liability risk.

#### Plan

- Add a visible disclaimer in Fan Control tab header or tooltip: "Fan control uses your laptop's standard firmware interface (WMI BIOS). These software commands cannot damage fan hardware."
- Add a post-apply sanity check: if RPM reads 0 for >30 seconds while duty >0%, show a warning banner suggesting hardware diagnostics
- Close #106 with explanation that WMI commands are firmware-mediated and cannot cause permanent motor failure

Files to modify:
- `src/OmenCoreApp/Views/FanControlView.xaml`
- `src/OmenCoreApp/Services/FanService.cs` (post-apply RPM monitor)

### 25. Fix 0 RPM fan display on models with broken RPM readback (GitHub #16, #55, #80)

Area: Telemetry, UI

Evidence: Multiple models report fan speed showing 0 RPM in UI even when fans are clearly spinning. Distinct from "fan control not working" — the fans respond to commands but RPM readback is broken (WMI returns 0, EC RPM registers not populated, or LHM can't detect sensor).

#### Plan

- When RPM reads 0 but duty cycle >0% for >10 seconds, show "RPM unavailable (fan responding)" instead of "0 RPM" in dashboard and fan control views
- Add a "RPM readback unavailable" telemetry state to the existing per-sensor state model (valid/zero/inactive/unavailable/stale/invalid)
- For specific models like HP Omen 15 EN0037AX (#16): investigate whether a model-specific RPM source override (similar to the CPU temp source override pattern in WmiBiosMonitor) can surface correct RPM

Files to modify:
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- `src/OmenCoreApp/ViewModels/FanControlViewModel.cs` (display logic)

### 26. Fix Performance mode applying wrong sustained TDP on 2025 OMEN models (community report)

Area: Performance, Hardware Compatibility

Evidence:

Community report (HP OMEN 16-am1001nw, i9-14900HX + RTX 5070 Ti):
- **OGH**: PL2 = 130W for ~2 minutes, then sustained PL1 = 90W — correct BIOS-specified limits
- **OmenCore v3.2.5 Performance + Max fans**: PL2 = 130W for ~10 seconds, then drops to **55W sustained**
- HWiNFO and ThrottleStop both confirm PKG Power locked at 54.9W while Performance mode is active
- 55W is the **Balanced** mode TDP for this model family — OmenCore is applying the wrong power tier

Additional observations from screenshots:
- CPU clocks drop from ~2860 MHz (without OmenCore) to ~2153 MHz (with OmenCore Performance mode)
- ThrottleStop shows VID 0.8040V and PKG Power 54.9W with PROCHOT limit at 97°C — no thermal cause for the reduction
- Model 16-am1xxx (RTX 5070 Ti, 2025) is likely not in the capability database; v3.3.1 only added 16-am0xxx (8D2F)

Root cause hypotheses:
1. **Missing model entry**: 16-am1xxx not in `ModelCapabilityDatabase` → falls back to family defaults with wrong TDP targets
2. **Wrong performance mode TDP mapping**: The WMI thermal policy or EC power-limit write for `Performance` on this model family is writing a 55W PL1 instead of the correct 90W
3. **Performance mode profile hardcoded TDP values** don't account for higher-TDP 2025 OMEN variants (i9-14900HX + RTX 5070 Ti chassis have higher thermal headroom than entry models)

#### Plan

**Step 1 — Add 16-am1xxx to the model database**
- File: `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- Add entry for the 16-am1xxx family (identify Product ID from a community diagnostic ZIP — request via GitHub issue)
- Set `MaxTdp` / `PerformanceTdp` to 90W PL1 / 130W PL2 matching HP BIOS spec for this model
- Tag as `UserVerified = false` pending confirmation

**Step 2 — Audit PerformanceModeService TDP writes**
- File: `src/OmenCoreApp/Services/PerformanceModeService.cs`
- Trace what power limit values are written when switching to Performance mode
- Verify that the WMI BIOS thermal policy (`SetSystemConfig` / `SetPowerConfig`) call for Performance mode uses model-specific TDP caps, not a global hardcoded value
- If TDP caps are hardcoded per-mode rather than per-model, refactor to look up `ModelCapabilityDatabase.PerformanceTdp` / `BalancedTdp` for the current device

**Step 3 — Add TDP diagnostic logging**
- Log the PL1/PL2 values being written (and read back) when a performance mode change occurs, so future reports have actionable data without needing ThrottleStop

**Step 4 — Validate fix**
- After applying the model entry + TDP mapping fix, PKG Power should sustain at ≥90W in Performance mode on 16-am1xxx
- CineBench R23 multi-core score should be comparable to OGH Performance mode

Note: User is on v3.2.5. Recommend updating to v3.3.1 first to rule out any overlap with the cross-thread crash fix, but the TDP bug is likely independent of that fix.

Files to modify:
- `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`
- `src/OmenCoreApp/Services/PerformanceModeService.cs`
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs` (diagnostic logging)

### 27. Fix stale temperature display and UI pipeline slowdown during fan mode switches (GitHub #116) ✅ FIXED (RPM transition hold + Dispatcher.BeginInvoke)

Area: Telemetry, Performance, Fan Control

Evidence (GitHub #116, v3.3.0/v3.3.1; also corroborated by Reddit r/omencore "ISSUE: Omencore Extremely Laggy" on v3.3.1, April 2026):
- When switching between fan modes, fans briefly stop and show 0 RPM
- Temperature reads ~10–15°C lower than actual during and after the transition (reporter feels ~85°C heat but UI shows ~70°C)
- System log window takes 6–15 seconds to update, sometimes minutes — the monitoring pipeline visibly stalls during mode switches
- The combination of fans stopping + stale temp display means the user has no accurate thermal signal while the CPU is heat-soaking

This is a compound issue with three distinct components:

**Component A — Fan RPM drops to 0 during mode transition**
Fan mode switches involve `SetFanMode()` / `SetFanLevel()` WMI calls that go through the EC. During the BIOS acknowledgement window, RPM readback briefly returns 0. The UI surfaces this 0 directly instead of holding the last known-good value.

**Component B — Temperature display lags behind real hardware during fan switch**
The monitoring loop in `HardwareMonitoringService` is called from the same async pipeline that processes fan mode changes. If the mode-switch path is synchronous or blocks the monitor loop's scheduling slot, the next sensor poll is delayed — causing the display to show the pre-switch reading while the CPU is heat-soaking at elevated temperature.

**Component C — System log and UI pipeline stalls for 6–15 seconds**
Log window slowness during fan switches points to the same root cause as `async void` dispatch (High Priority #10) and sync-over-async patterns (High Priority #11). Specifically: `FanService.ApplyPreset()` was partially fixed in v3.3.0 to use `Task.Run`, but secondary fan verification, logging callbacks, and status updates may still marshal back to the UI dispatcher in a blocking pattern, starving the log render loop.

#### Plan

**Fix A — Hold last-known-good RPM during transition**
- File: `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`
- When RPM drops from a non-zero value to 0 during an active mode switch, display the last non-zero RPM with a `(transitioning...)` label instead of `0 RPM`
- Clear the hold after the next non-zero RPM readback or after a 5-second timeout
- This prevents the alarming 0 RPM flash that misleads users into thinking fan control failed

**Fix B — Decouple monitoring loop scheduling from fan-switch dispatch**
- File: `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- Ensure the fan mode switch code path does not acquire any lock or semaphore that blocks the monitoring loop's next poll
- If a single `SemaphoreSlim` or `lock` guards both fan commands and sensor reads, split them into separate guards so sensor polling is never blocked by a fan command in flight
- After a mode switch completes, force an immediate sensor poll cycle rather than waiting for the next scheduled tick — this surfaces the post-switch temperature immediately

**Fix C — Eliminate remaining UI-thread blocking in fan switch dispatch**
- Files: `src/OmenCoreApp/Services/FanService.cs`, `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`
- Audit all `Dispatcher.Invoke` (blocking) calls in the fan switch path and replace with `Dispatcher.InvokeAsync` (non-blocking)
- Ensure log sink writes during fan operations are fire-and-forget or use a dedicated background log queue rather than synchronously marshaling to the UI thread
- Cross-reference with High Priority #10 (async void) and #11 (sync-over-async) — fixing those will also address the 6–15 second stall

Files to modify:
- `src/OmenCoreApp/ViewModels/FanControlViewModel.cs`
- `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- `src/OmenCoreApp/Services/FanService.cs`

### 28. Fix GPU power display on hybrid iGPU+dGPU systems (Reddit r/omencore, April 2026) ✅ FIXED

Area: Telemetry, UI

Evidence (Reddit "Glitch I think", OMEN 15 en-1036ax):
- GPU power reading shows an incorrect or misleading value when both an integrated GPU and discrete GPU are present
- Reporter asks: "GPU Power is wrong (is it because I have iGPU and dGPU?)"
- On Optimus-active systems (dGPU in power-saving mode), OmenCore may display the iGPU's power draw as "GPU Power", or show 0W for the dGPU with no explanation that it is in power-saving state

Root cause: `WmiBiosMonitor` and the LHM-backed power sensor pipeline need to distinguish between iGPU and dGPU power sources. v3.1.0 added explicit inactive dGPU state handling for temperature, but GPU *power* may not follow the same inactive-state logic.

#### Plan

- File: `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- When the dGPU is in Optimus power-saving mode (not the active render device), mark GPU power as `Inactive` rather than surfacing a near-zero or iGPU power value
- Display "GPU inactive (Optimus)" rather than "0 W" or a misleading iGPU wattage
- File: `src/OmenCoreApp/ViewModels/DashboardViewModel.cs` (or equivalent)
- Add a GPU mode indicator to the GPU power card: "Active (MUX)" / "Optimus (power-saving)" / "Inactive" so users understand why the value is 0 or low
- Align with the existing per-sensor telemetry state model (valid/zero/inactive/unavailable/stale/invalid) added in v3.1.0

Files to modify:
- `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`
- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs`

### 29. Fix custom fan curve locking CPU power to 25 W (Discord, April 2026) ✅ FIXED

Area: Fan Control, Power Management

Evidence (Discord r/omencore, user OsamaBiden, v3.2.5; confirmed by Elros on v3.3.0):
- When using the **Custom** fan curve (visual curve editor), CPU package power is constrained to ~25 W regardless of load
- Switching to any other fan mode (Max, Extreme, Gaming, Auto, Silent) immediately releases the lock
- Using "Max Fans" explicitly was confirmed to unlock it on v3.3.0, suggesting the lock is tied to the custom-curve apply path rather than to fan mode BIOS writes
- The bug exists in v3.2.5 and is presumed present in v3.3.1 (no changelog entry addressing it)

Root cause hypothesis: `FanService.ApplyCustomCurve()` (or equivalent) may be issuing a BIOS power-limit write alongside the fan curve — e.g. writing a `TdpMode` or `PowerLimit` WMI property to a value corresponding to the Balanced profile as part of a mode-switch side-effect. The 25 W value is consistent with a Balanced TDP for mid-range OMEN models.

#### Plan

- File: `src/OmenCoreApp/Services/FanService.cs` — audit `ApplyCustomCurve()` / `ApplyPreset()` for any BIOS or TDP writes that should not fire when the user selects Custom mode. Verify no power-mode BIOS call is made that resets TDP.
- File: `src/OmenCoreApp/Hardware/WmiFanController.cs` — check whether a `SetPerformanceMode(Balanced)` or equivalent WMI call is embedded in the custom-curve apply path
- File: `src/OmenCoreApp/Services/ThermalService.cs` (if present) — ensure custom fan curve selection does not implicitly set a performance mode that caps TDP
- Add a regression test asserting that `ApplyCustomCurve()` does not invoke any TDP-limit write method

Files to modify:
- `src/OmenCoreApp/Services/FanService.cs`
- `src/OmenCoreApp/Hardware/WmiFanController.cs`

### 30. Fix fan profile selector (Max/Extreme/Gaming/Auto/Silent/Custom) hidden in 3.3.0/3.3.1 (Discord, April 2026) ✅ FIXED

Area: UI/UX, Fan Control (acknowledged by dev for 3.4.0)

Evidence (Discord, user OsamaBiden, OMEN 16 xd0xxx, 18 April 2026; confirmed by user spooky on 19 April 2026; dev AntipopAU acknowledged):
- In v3.2.5, the fan profile preset selector (Max / Extreme / Gaming / Auto BIOS / Silent / Custom) is clearly visible above the custom curve editor — users can select a profile in one click
- In v3.3.0 and v3.3.1, the preset selector is **no longer accessible**; the view opens directly into the Custom curve editor without showing the profile cards
- Users on xd0xxx and wf1001nl both reported the regression
- Dev comment: *"Interesting — that is strange behaviour. It should be supported. Leave it with me for 3.4.0."*

The screenshot from v3.2.5 shows a clear fan profile card row (Max → Extreme → Gaming → Auto BIOS → Silent → Custom) above the visual curve editor, with `Independent CPU/GPU` checkbox and preset controls. This row appears absent or unreachable in v3.3.x.

Root cause hypothesis: A navigation refactor in v3.3.0 buried the preset selector under the curve editor, or the profile card row's visibility condition changed. The `OMEN` tab fan control view may now default to showing the curve editor directly rather than the profile-first layout.

#### Plan

- File: `src/OmenCoreApp/Views/FanControlView.xaml` — restore the profile card row (Max/Extreme/Gaming/Auto/Silent/Custom) as the primary visible UI element, with the custom curve editor appearing below or on a secondary expand/tab
- Verify that the `Independent CPU/GPU` checkbox and `Smoothing / Step` controls remain accessible alongside the profile cards (as in v3.2.5 screenshot)
- Ensure `Auto (BIOS)` card with checkmark indicator is preserved
- Add a UI smoke test: verify profile card row elements are present in the fan control view's visual tree

Files to modify:
- `src/OmenCoreApp/Views/FanControlView.xaml`
- `src/OmenCoreApp/ViewModels/FanControlViewModel.cs` (if visibility is data-driven)

### 31. Fix STAMP/PrtSc key not launching Windows Snipping Tool (Discord, April 2026) ✅ FIXED

Area: Keyboard, Input

Evidence (Discord, user spooky, OMEN 16-wf1001nl, v3.2.5 log confirmed, 19 April 2026):
- Pressing the STAMP (PrtSc) key does not open the Windows Snipping Tool
- User must use Win+Shift+S as workaround
- OmenCore intercepts or consumes the Print Screen keypress via its hotkey registration, preventing Windows from seeing it

Root cause (investigation findings):
- `OmenKeyService` WH_KEYBOARD_LL hook does NOT intercept VK_SNAPSHOT (0x2C) via `IsOmenKey()` — PrintScreen was always passed through
- `HotkeyService.RegisterDefaultHotkeys()` does NOT register PrintScreen as a hotkey
- The real risk: on some HP OMEN models (e.g., wf1001nl), the BIOS WMI key event fires when PrtSc is pressed. Without an explicit never-intercept guard, OmenCore could react to that WMI event while simultaneously blocking/confusing the Windows Snipping Tool activation timing
- Secondary risk: future code changes could accidentally add VK_SNAPSHOT to `IsOmenKey()` without realising the impact

Fix applied:
- Added `VK_SNAPSHOT = 0x2C` constant to `OmenKeyService.cs`
- Added explicit guard in `TryGetNeverInterceptReason()` for `VK_SNAPSHOT` with reason `"never-intercept-printscreen"`
- This ensures: (1) the hook always passes PrtSc through immediately, (2) any WMI BIOS event that coincidentally fires at the same time as PrtSc is suppressed via `ShouldSuppressWmiEventFromRecentNeverInterceptKey`, (3) future developers are explicitly warned not to intercept PrtSc

Files modified:
- `src/OmenCoreApp/Services/OmenKeyService.cs` — VK_SNAPSHOT constant + TryGetNeverInterceptReason guard

### 32. Reduce antivirus false positives for OmenCore.sys / WinRing0 (Discord, April 2026) ✅ FIXED (documentation)

Area: Distribution, Security, User Trust

Evidence (Discord, user Logos, Bitdefender flagging `D:\Program Files\OmenCore\OmenCore.sys` as `Gen:Application.Venus.Cynthia.Winring.17ay1@auVvKTci`, 22 April 2026):
- Bitdefender (and other AV products) flag `OmenCore.sys` as a potentially unwanted application (PUA) or malware
- The detection name `Winring` confirms this is the WinRing0 / PawnIO kernel driver, which is used for EC and MSR access
- This is a known industry-wide issue: WinRing0 has been abused by game cheats and cryptominers, so AV heuristics flag any binary that includes or loads it

This is not a code defect but it is a **user trust and distribution problem** that directly impacts adoption. Users with strict AV configurations will be unable to use OmenCore.

#### Plan

- **Code signing**: If `OmenCore.sys` / PawnIO is not already Authenticode-signed with the project's certificate, sign it. A valid EV code-signing cert dramatically reduces heuristic AV detections for kernel drivers.
- **Installer warning + FAQ**: Add an AV FAQ entry to `docs/ANTIVIRUS_FAQ.md` (which already exists) covering Bitdefender specifically, with the detection name `Gen:Application.Venus.Cynthia.Winring` and instructions to add an exclusion. Reference the FAQ in the installer's finish page.
- **VirusTotal submission**: After each release, submit the signed `.sys` file to VirusTotal and major AV vendor portals (Bitdefender, Windows Defender, Kaspersky) for whitelisting.
- **Consider PawnIO alternative**: If PawnIO is already preferred over raw WinRing0, ensure the shipped `.sys` file is the PawnIO binary (which has better provenance) rather than the raw WinRing0 binary. Confirm this in the installer script.
- **README**: Add a note under "Why does my antivirus flag OmenCore?" in the README linking to the FAQ.

Files to modify:
- `docs/ANTIVIRUS_FAQ.md` — add Bitdefender section with detection name and exclusion steps
- `README.md` — add AV false positive note with link to FAQ
- `build-installer.ps1` — verify the `.sys` being bundled is PawnIO-signed (not raw WinRing0)

## Nice-to-Have (optional polish)

### 1. Make capability state clearer directly inside fan and tuning screens

Area: UI/UX, Usability

Recommendation:

- Show whether a control is WMI-backed, EC-backed, profile-only, inferred, or unavailable directly where the user makes changes.
- Prefer hiding unsupported controls over showing disabled controls with weak explanation.

### 2. Consolidate diagnostics into a single support bundle

Area: UI/UX, Release Readiness

Context: v3.3.0 already shipped `runtime-performance.txt`, `background-timers.txt`, `resume-recovery.txt`, and `identity-resolution-trace.txt` in diagnostics exports. v3.2.5 added the model identity resolution trace. The building blocks are mature.

Recommendation:

- A single `Create support bundle` action that collects all existing diagnostic exports would make support easier.
- This is low-risk packaging work, not new data collection.

### 3. Generate support documentation from the capability databases where possible

Area: Maintainability, Release Readiness

Recommendation:

- Use the model databases as the source for a generated support matrix or verified-model list.
- This would reduce drift between code, docs, and issue triage over time.

### 4. Perform a small consistency pass across Settings, Diagnostics, and System Control

Area: UI/UX

Recommendation:

- Unify terminology around `monitoring`, `worker`, `fan mode`, `performance mode`, `experimental`, and `capability`.
- Remove any remaining `coming soon` language from the stable release branch.

### 5. Audit NAudio/WASAPI dependency surface

Area: Maintainability, Release Readiness

Context: v3.3.0 added audio-reactive RGB via real WASAPI loopback capture using NAudio. This is a new native-interop dependency.

Recommendation:

- Verify NAudio disposal and error handling paths are clean.
- Ensure the audio capture lifecycle is correctly managed across sleep/resume and window minimize/restore (the ambient sampling service already has `SetHostMinimized` gating — confirm audio capture follows the same pattern).
- Confirm this dependency does not affect Linux packaging (NAudio is Windows-only; the Linux build should not reference it).

### 6. Add accessibility attributes to all views (AutomationProperties)

Area: Accessibility, UI/UX

Evidence:

- Only **3 of 25 XAML views** have any `AutomationProperties` attributes (MainWindow: 1 instance, BloatwareManagerView: 6 instances, MemoryOptimizerView: 2 instances).
- **22 views have zero accessibility attributes** — including all core views: DashboardView, SettingsView, SystemControlView, LightingView, DiagnosticsView, FanControlView, TuningView, etc.
- Screen readers and automation tools cannot identify or describe any controls in these views.

Recommendation:

- Add `AutomationProperties.Name` to all interactive controls (buttons, sliders, toggles, combo boxes) in core views
- Priority order: DashboardView, FanControlView, LightingView, SystemControlView, SettingsView
- Add `AutomationProperties.HelpText` to complex controls like fan curve editor and color pickers

### 7. Consolidate hardcoded colors into theme resources

Area: UI/UX, Maintainability, Theme Support

Evidence:

- **100+ instances** of raw hex colors across all XAML views (e.g., `#141418`, `#1A1A1E`, `#0D0D0F`, `#FF4444`, `#64B5F6`, `#4CAF50`, etc.)
- Worst offenders: FanControlView (12+ raw colors for preset cards), DashboardView (14+ raw colors), TuningView (7+ raw colors), SystemControlView (4+ raw colors), LightingView (15+ raw colors)
- These colors will not respond to any future theme system or light mode support

Recommendation:

- Define semantic named brushes in `ModernStyles.xaml` (e.g., `PresetSilentBrush`, `PresetPerformanceBrush`, `SuccessBrush`, `ErrorBrush`, `WarningBrush`, `ChartLineBrush`)
- Replace raw hex values with `{StaticResource ...}` references
- Start with the most-repeated colors to maximize impact

### 8. Add ScrollViewer to FanControlView

Area: UI/UX, Responsive Design

Evidence:

- FanControlView.xaml has **no ScrollViewer** at all. The root is a plain Grid with rows `Auto/Auto/*`.
- The view contains preset cards, info banners, curve editor, thermal protection, and calibration sections.
- On smaller screens, lower resolutions, or high DPI settings, content overflows and is clipped.
- Most other complex views (DashboardView, SettingsView, SystemControlView, DiagnosticsView) already have ScrollViewers.

Recommendation:

- Wrap the FanControlView content in a ScrollViewer, keeping the curve editor in a fixed-height section if needed for mouse interaction

### 9. Remove "coming soon" placeholder text from LightingView

Area: UI/UX, Release Readiness

Evidence:

- `LightingView.xaml` line ~816: `"Note: Full Razer Chroma SDK integration is coming soon. Currently showing placeholder functionality."` — This is user-visible text.
- The Razer backend was shipped in v3.3.0 with reconnect, exponential backoff, and status badges. If Razer is functional, this text is stale. If not fully functional, it should say "Limited Razer support" not "coming soon".

Recommendation:

- Remove or update the placeholder text to match actual Razer capability status

### 10. Fix converter naming inconsistency across views

Area: Code Quality, Maintainability

Evidence:

Three different converter resource keys are used for the same purpose:
- `BoolToVisibility` — FanControlView, AdvancedView, MainWindow, BloatwareManagerView, etc.
- `BooleanToVisibilityConverter` — DashboardView, SettingsView, TuningView, SystemControlView, LightingView
- `BoolToVisibilityConverter` — FanDiagnosticsView, GameLibraryView

Some views even use **both** (e.g., DashboardView, TuningView, FanControlView).

Recommendation:

- Standardize on one name (e.g., `BoolToVisibilityConverter`)
- Move the converter instance to `App.xaml` or `ModernStyles.xaml` as a shared resource
- Update all views to reference the single shared key

### 11. Fix MainWindowViewModel IDisposable leak (Avalonia)

Area: Code Quality, Resource Management

Evidence:

- `MainWindowViewModel` owns `DashboardVm` and `FanControlVm` which implement `IDisposable`, but `MainWindowViewModel` does NOT implement `IDisposable` and never disposes its children.
- `DashboardViewModel.StartUptimeTimer()` starts a `Task.Run` loop that checks `_disposed` — but since Dispose is never called, this loop runs forever until process exit.
- The `_disposed` field is read without `volatile` or memory barrier — the JIT may hoist the read outside the loop.
- `StatusChanged` event handlers from child VMs will leak.

Recommendation:

- Implement `IDisposable` on `MainWindowViewModel` — call `Dispose()` on child VMs
- Add `CancellationToken` to the uptime timer instead of relying on a non-volatile bool
- Ensure `StatusChanged` event handlers are unsubscribed in Dispose

### 12. Wire up unused ILogger DI infrastructure (Avalonia)

Area: Observability, Debugging

Evidence:

- `App.axaml.cs` configures `Microsoft.Extensions.Logging` with `AddConsole()` at `LogLevel.Debug`
- BUT: **No ViewModel or Service injects `ILogger<T>`**. All code uses `System.Diagnostics.Debug.WriteLine` or bare `Console.Error.WriteLine` instead.
- When hardware writes fail silently (due to 51 bare catches in Linux code), there is no log trail to diagnose the failure.

Recommendation:

- Inject `ILogger<T>` into `LinuxHardwareService`, `FanCurveService`, and ViewModels through constructor injection
- Replace `Debug.WriteLine` with `_logger.LogDebug(...)` / `_logger.LogError(...)` calls
- This enables structured logging that can be filtered, persisted, and collected for support

### 13. Add download concurrency guard and temp cleanup to AutoUpdateService ✅ FIXED (download lock + safe timer callback + stale cleanup)

Area: Reliability, Disk Usage

Evidence:

- No lock/semaphore around `DownloadUpdateAsync` — if the timer fires during a user-initiated download, two concurrent downloads race on the same file path
- `Timer.Elapsed` handler used `async (s, e) => await OnTimerCheckAsync()` — async void lambda that can crash process if exceptions escape before local handling
- Old downloads in `%TEMP%\OmenCore\Updates` were never cleaned up — stale installer files accumulated over time

#### Implemented

- Added serialized guards in `AutoUpdateService`:
  - `_downloadSemaphore` now protects the entire `DownloadUpdateAsync` path
  - `_scheduledCheckSemaphore` prevents overlapping timer-triggered checks
- Replaced timer async lambda with explicit callback wiring (`CheckTimerElapsed` + `RunScheduledCheckAsync`) and centralized exception handling.
- Reworked `DownloadUpdateAsync` to stage into a `.partial` file and atomically replace the final package once hash validation succeeds.
- Added stale temp cleanup (`CleanupStaleDownloads`) on service startup and after successful downloads; preserves the active target file when needed.
- Added defensive disposal updates: timer handler unsubscription and semaphore disposal.

Files modified:
- `src/OmenCoreApp/Services/AutoUpdateService.cs`
- `src/OmenCoreApp.Tests/Services/AutoUpdateServiceTests.cs`

### 14. Expand bloatware database with common HP entries

Area: Feature Completeness

Evidence:

- Only ~20 entries in `config/bloatware_database.json`
- Missing common HP bloatware: `HP Smart`, `HP JumpStarts`, `HP PC Hardware Diagnostics`, `myHP`, `HP QuickDrop`, `HP Notifications`
- No severity/action field to distinguish "safe to always remove" vs "ask user first"

Recommendation:

- Add the missing HP-specific entries
- Consider adding a `severity` field (`safe` / `caution` / `system`) to guide UI presentation

### 15. Add Windows installer verification QA script

Area: Release Engineering, QA

Evidence:

- `qa/verify-linux-package.ps1` exists for Linux artifact validation
- No equivalent `verify-windows-installer.ps1` to validate installer artifacts (PE header, version info, silent install test, hash verification)
- `qa/run-updater-regression.ps1` downloads the **entire asset binary** (~50MB) just to check the first 2 bytes — should use HTTP Range headers

Recommendation:

- Create `qa/verify-windows-installer.ps1` to validate installer `.exe` (PE header, embedded version, file size, SHA256)
- Optimize `run-updater-regression.ps1` to use HTTP Range headers for signature checks

### 16. Optimize nvidia-smi subprocess polling (Linux)

Area: Performance

Evidence:

- `LinuxHardwareService.cs` line ~857: `ReadGpuTemperatureAsync()` spawns `nvidia-smi --query-gpu=temperature.gpu ...` **every second** via the 1-second polling timer.
- Spawning a process every second is expensive in terms of CPU and memory.

Recommendation:

- Use hwmon sysfs interface for NVIDIA GPU temperature if available (`/sys/class/hwmon/hwmonN/temp*_input`)
- Fall back to nvidia-smi only if hwmon is not present
- Or increase the GPU polling interval to 2–5 seconds since GPU temperature changes slowly

### 17. Fix README stale version references ✅ FIXED

Area: Documentation, Release Readiness

Evidence:

- README stale references were updated to `3.4.0` in architecture/build/docs sections.
- Historical ambiguity around telemetry wording was normalized to "No outbound telemetry" for consistency.
- The architecture tree now labels `src/OmenCore.Desktop` as an archived prototype not part of shipping builds.

Implemented:

- Updated stale version literals and build-output examples in `README.md`.
- Added a `v3.4.0` row to version history and updated current changelog link to `docs/CHANGELOG_v3.4.0.md`.

### 18. Fix Inno Setup installer issues

Area: Release Engineering, Security

Evidence:

| # | Issue | Severity |
|---|-------|----------|
| 1 | Post-install scheduled task uses `/rl highest` (SYSTEM-level) — security concern, no UAC prompt | MEDIUM |
| 2 | `UninstallDelete` removes `{userappdata}\OmenCore` entirely — destroys custom fan curves, lighting profiles, etc. | MEDIUM |
| 3 | `CurPageChanged` hardcodes task index `WizardForm.TasksList.Checked[2]` for PawnIO — breaks if task order changes | MEDIUM |
| 4 | No config migration logic — `onlyifdoesntexist` means old configs miss new settings silently | MEDIUM |
| 5 | No registry cleanup in uninstall section | MEDIUM |

Recommendation:

- Consider running as current user with UAC prompt instead of SYSTEM-level scheduled task
- On uninstall, offer to keep or remove user config (or only remove program files, not AppData)
- Use task name matching instead of hardcoded index for PawnIO checkbox
- Add config migration step that merges new default keys into existing config

### 19. Batch model database additions from open GitHub issues ✅ PARTIALLY FIXED (#117 / 8D87 added)

Area: Hardware Compatibility

Evidence: This batch is mostly complete. Previously missing Product IDs (`8A44`, `8A3E`, `8E41`, `8A26`) were added earlier, verification targets (`8C58`, `8D41`) are present, and GitHub issue #117 identified and added a new missing model mapping (`8D87`, OMEN MAX 16-ak0xxx).

Implemented:
- Added `8D87` capability profile in `ModelCapabilityDatabase.cs` (conservative, `UserVerified = false`, MAX 16 safety defaults).
- Added matching `8D87` keyboard profile in `KeyboardModelDatabase.cs`.
- Added regression tests in `ModelCapabilityDatabaseTests.cs` and `KeyboardModelDatabaseTests.cs` to lock lookup behavior.

Remaining recommendation:
- Continue rolling triage of newly reported Product IDs from open issues and keep entries conservative until hardware-verified by community feedback.

### 20. Add clear messaging for Linux models with missing kernel fan interfaces

Area: Linux UX, Documentation

Evidence: GitHub #79, #80, #97, #26 — `hp-wmi` loads but fan/thermal sysfs nodes are absent. OmenCore can't fix kernel driver gaps, but the UI is silent about why fan control is unavailable. Also: Discord April 2026 — board 8D41 on **Linux kernel 7.0** reports `hp-wmi` returning "No device" with no subfolder, and `verdictator` confirms HP-WMI does not work on kernel 7.0 at all.

Recommendation:
- When `hp-wmi` is loaded but fan/thermal nodes are missing, display a specific message: "Your model's hp-wmi driver does not expose fan control interfaces. Fan control requires kernel 6.5+ and may not be available on all models."
- Add per-model notes to Linux compatibility section of README
- For desktop models (8B1D): note that desktop models have limited Linux fan support
- For kernel 7.0+: track whether the kernel `hp-wmi` ABI changed (possible `NO_DEVICE` response for boards previously supported). Add a "kernel version compatibility" note to the Linux install guide. If kernel 7.0 broke something on board 8D41 that worked on 6.x, raise a note in the Linux FAQ and consider fallback detection logic.

### 21. Remove dead and redundant code for minimal runtime footprint (Discord, April 2026)

Area: Maintainability, Performance

Evidence: Community request — "want to ensure that we have cut any obsolete/redundant code to ensure we are hitting our aim of minimal resources."

Recommendation:
- Run a static analysis pass (Roslyn analyzers, ReSharper or equivalent) to identify unused private methods, unused fields, and unreachable branches
- Check for any services registered in the DI container that are never resolved
- Review any `#if DEBUG` or `// TODO` legacy blocks that were not removed after feature completion
- Audit `config/default_config.json` and `config/bloatware_database.json` for obsolete keys
- Remove any NuGet packages that are listed in `.csproj` files but are not actually referenced in code
- This is explicitly **not** a refactor — only remove code that is verifiably unused. No restructuring.

### 22. Investigate and document Linux Avalonia Wayland black screen (Discord, April 2026)

Area: Linux GUI, Wayland Compatibility

Evidence (Discord Linux channel, April 2026): Multiple users (`verdictator`, `kessef`, `cremita`, `фрутоняня энерджи`) report a black screen when launching the Avalonia GUI on Wayland sessions. `фрутоняня энерджи` resolved it by running with `sudo` and installing additional packages. The community guide references `--software-rendering` and Wayland flags in the install guide but users are not finding them.

Root cause: Avalonia 11.x has partial Wayland support. On some compositors, the default X11/Wayland backend selection fails silently, leaving a black render surface. Running with `sudo` may switch to a different session that happens to use X11.

Recommendation:
- Add a prominent **Wayland Troubleshooting** section to `docs/LINUX_INSTALL_GUIDE.md` with the exact commands: `--backend=x11` (force X11 via XWayland), `--software-rendering` flag, and the `sudo` workaround
- Consider adding a launch wrapper script that detects `$WAYLAND_DISPLAY` and automatically adds `--backend=x11` as a fallback
- Track the Avalonia Wayland roadmap; when Avalonia ships stable Wayland support, remove the workaround
- The CLI path (`omencore-cli`) works fine on Wayland — document this clearly for users who cannot get the GUI working

### 23. Document UI contribution pathway for community redesign proposals (Discord, April 2026)

Area: Community, Documentation

Evidence (Discord, web developer user, April 2026): A web developer offered to redesign the UI into something "cleaner and more minimal," starting with a mockup.

Recommendation:
- Add a `CONTRIBUTING.md` section (or expand the existing one) describing how community UI contributions work: mockup → discussion issue → implementation PR
- Note the WPF XAML + MVVM architecture constraints (Windows app) and the Avalonia XAML stack (Linux app) so contributors know what technology they'd be working with
- The current UI is functional and not slated for a redesign in 3.4.0, but the project should have a clear pathway for accepting such contributions post-3.4.0
- If a mockup is received from this contributor, open a GitHub discussion rather than an issue to track UI design proposals separately from bugs

## Test Proposals — Detailed Breakdown

This section describes ~60–75 new unit tests that can be written without hardware, following the established project patterns (manual fakes, FluentAssertions, xUnit, reflection for private methods). Tests are grouped by priority tier.

### Resolved: 12 Analyzer Problems (completed 2026-04-17)

The following 12 test analyzer problems were fixed:

| File | Problem | Fix |
|------|---------|-----|
| `FanSmoothingTests.cs` (×8) | CS8600/CS8602: null-to-nonnullable on reflection `Invoke()`. xUnit1030: `ConfigureAwait(false)` in tests. | Added `rampMethod.Should().NotBeNull()` assertion, cast to `Task?` with null check, removed `.ConfigureAwait(false)` |
| `ModelReportServiceTests.cs` (×2) | CS8602: `File.Delete(path2)` where `path2` could be null | Added `if (path2 != null)` guard before `File.Delete` |
| `SystemInfoServiceTests.cs` (×2) | xUnit1012: `[InlineData(null, false)]` for non-nullable parameter | Changed parameter type to `string?` |

### Tier 1 — Pure Static Logic (no dependencies, highest value per line)

**1a. `LinuxCapabilityClassifier.Assess()` — 15–20 tests**

File: `src/OmenCore.Linux.Tests/Hardware/LinuxCapabilityClassifierTests.cs` (new)

This is the single highest-value test target. `Assess()` is a public static method with 15 bool + 2 string parameters that produces one of 4 `LinuxCapabilityClass` values with a reason string. Every branch is testable:

| # | Test Name | Key Input | Expected Output |
|---|-----------|-----------|-----------------|
| 1 | `Assess_HasFan1Output_ReturnsFullControl` | `hasFan1Output=true`, rest false | `FullControl`, "fan output files" in reason |
| 2 | `Assess_HasHwmonFanAccess_ReturnsFullControl` | `hasHwmonFanAccess=true`, rest false | `FullControl`, "hp-wmi hwmon" in reason |
| 3 | `Assess_HasEcAccess_ReturnsFullControl` | `hasEcAccess=true`, rest false | `FullControl`, "legacy EC" in reason |
| 4 | `Assess_HasFan1Target_ReturnsFullControl` | `hasFan1Target=true`, rest false | `FullControl`, "fan target" in reason |
| 5 | `Assess_HasFan2Target_ReturnsFullControl` | `hasFan2Target=true`, rest false | `FullControl` |
| 6 | `Assess_HasFan2Output_ReturnsFullControl` | `hasFan2Output=true`, rest false | `FullControl` |
| 7 | `Assess_ManualControl_NotRoot_IncludesSudoMessage` | `hasFan1Output=true`, `isRoot=false` | Reason contains "sudo" |
| 8 | `Assess_ManualControl_IsRoot_NoSudoMessage` | `hasFan1Output=true`, `isRoot=true` | Reason does NOT contain "sudo" |
| 9 | `Assess_OnlyThermalProfile_ReturnsProfileOnly` | `hasThermalProfile=true`, rest false | `ProfileOnly` |
| 10 | `Assess_OnlyAcpiPlatformProfile_ReturnsProfileOnly` | `hasAcpiPlatformProfile=true`, rest false | `ProfileOnly` |
| 11 | `Assess_OnlyPlatformProfile_ReturnsProfileOnly` | `hasPlatformProfile=true`, rest false | `ProfileOnly` |
| 12 | `Assess_UnsafeEcModel_ProfileOnly_IncludesBoardInfo` | `hasThermalProfile=true`, `isUnsafeEcModel=true`, `boardId="8c58"`, `model="OMEN MAX 16t"` | Reason contains "8c58" and "OMEN MAX 16t" |
| 13 | `Assess_ProfileOnly_NotRoot_IncludesSudoMessage` | `hasThermalProfile=true`, `isRoot=false` | Reason contains "sudo" |
| 14 | `Assess_OnlyTelemetryPaths_ReturnsTelemetryOnly` | `hasTelemetryPaths=true`, rest false | `TelemetryOnly` |
| 15 | `Assess_AllFlagsOff_ReturnsUnsupportedControl` | all false | `UnsupportedControl` |
| 16 | `Assess_FullControl_CapabilityKey_IsCorrect` | `hasEcAccess=true` | `CapabilityKey == "full-control"` |
| 17 | `Assess_ProfileOnly_CapabilityKey_IsCorrect` | `hasThermalProfile=true` | `CapabilityKey == "profile-only"` |
| 18 | `Assess_TelemetryOnly_CapabilityKey_IsCorrect` | `hasTelemetryPaths=true` | `CapabilityKey == "telemetry-only"` |
| 19 | `Assess_Unsupported_CapabilityKey_IsCorrect` | all false | `CapabilityKey == "unsupported-control"` |
| 20 | `Assess_ManualAndProfile_ReturnsFullControlWithProfileSupport` | `hasFan1Output=true`, `hasThermalProfile=true` | `FullControl`, `SupportsProfileControl=true` |

**1b. `LinuxEcController.CheckUnsafeEcModel()` — 6–8 tests**

File: `src/OmenCore.Linux.Tests/Hardware/LinuxEcControllerTests.cs` (new)

Private static method — access via reflection (established pattern in `FanSafetyClampingTests.cs`).

| # | Test Name | Input | Expected |
|---|-----------|-------|----------|
| 1 | `CheckUnsafeEcModel_KnownUnsafeModel_ReturnsTrue` | model="OMEN MAX 16t-ah000" | `true` |
| 2 | `CheckUnsafeEcModel_CaseInsensitive_ReturnsTrue` | model="omen max 16t-ah000" | `true` |
| 3 | `CheckUnsafeEcModel_KnownUnsafeBoardId_ReturnsTrue` | boardId="8c58" | `true` |
| 4 | `CheckUnsafeEcModel_SafeModel_ReturnsFalse` | model="OMEN 16 2023" | `false` |
| 5 | `CheckUnsafeEcModel_NullInputs_ReturnsFalse` | model=null, boardId=null | `false` |
| 6 | `CheckUnsafeEcModel_UnsafeBoardSafeModel_ReturnsTrue` | model="safe", boardId="8e41" | `true` |
| 7 | `CheckUnsafeEcModel_EmptyStrings_ReturnsFalse` | model="", boardId="" | `false` |

### Tier 2 — Service-Level with Fake Interfaces

**2a. `FanCurveService` — 15–20 tests**

File: `src/OmenCore.Avalonia.Tests/Services/FanCurveServiceTests.cs` (new)

Requires a simple `IHardwareService` fake that returns known `HardwareStatus` values. The fake is ~30 lines (implement all interface methods with no-op or canned returns).

**Interpolation tests (private `InterpolateFanSpeed`, access via reflection):**

| # | Test | Input | Expected |
|---|------|-------|----------|
| 1 | `InterpolateFanSpeed_BelowFirstPoint_ReturnsFirstSpeed` | curve=[{40,30},{80,80}], temp=20 | 30 |
| 2 | `InterpolateFanSpeed_AboveLastPoint_ReturnsLastSpeed` | curve=[{40,30},{80,80}], temp=95 | 80 |
| 3 | `InterpolateFanSpeed_AtExactPoint_ReturnsThatSpeed` | curve=[{40,30},{60,50},{80,80}], temp=60 | 50 |
| 4 | `InterpolateFanSpeed_BetweenPoints_ReturnsLinearInterpolation` | curve=[{40,30},{80,80}], temp=60 | ~55 (halfway) |
| 5 | `InterpolateFanSpeed_EmptyCurve_Returns50` | curve=[], temp=60 | 50 |
| 6 | `InterpolateFanSpeed_SinglePoint_ReturnsThatSpeed` | curve=[{50,40}], temp=anything | 40 |
| 7 | `InterpolateFanSpeed_ClampsTo0_100` | curve with extrapolation that could go negative | ≥0 and ≤100 |

**Preset tests:**

| # | Test | Expected |
|---|------|----------|
| 8 | `GetPreset_Known_ReturnsCopy` | Balanced preset; mutate returned list → re-get unchanged |
| 9 | `GetPreset_Unknown_FallsBackToBalanced` | name="Nonexistent" → returns Balanced values |
| 10 | `GetPresetNames_ReturnsFourBuiltIns` | ["Silent","Balanced","Performance","Aggressive"] |

**SavePreset validation tests:**

| # | Test | Expected |
|---|------|----------|
| 11 | `SavePreset_EmptyName_Throws` | ArgumentException |
| 12 | `SavePreset_WhitespaceOnlyName_Throws` | ArgumentException |
| 13 | `SavePreset_OnlyOnePoint_Throws` | InvalidOperationException |
| 14 | `SavePreset_ClampsSpeedAbove100` | point with speed=150 → stored as 100 |
| 15 | `SavePreset_ClampsSpeedBelow0` | point with speed=-10 → stored as 0 |
| 16 | `SavePreset_SortsByTemperature` | unsorted input → sorted in storage |

**Curve set/get tests:**

| # | Test | Expected |
|---|------|----------|
| 17 | `SetCpuFanCurve_SortsInput` | unsorted → GetCpuFanCurve returns sorted |
| 18 | `GetCpuFanCurve_ReturnsCopy` | mutating returned list doesn't affect service |

**ApplyAsync integration test:**

| # | Test | Expected |
|---|------|----------|
| 19 | `ApplyAsync_WithKnownTemps_WritesCorrectSpeeds` | Fake returns CPU=60°C, GPU=70°C with Balanced curve → verify SetCpuFanSpeedAsync and SetGpuFanSpeedAsync called with expected interpolated values |

**2b. `LinuxHardwareService.HasStatusChanged()` — 5–6 tests**

File: `src/OmenCore.Avalonia.Tests/Services/LinuxHardwareServiceTests.cs` (new)

Private static method — access via reflection.

| # | Test | Input | Expected |
|---|------|-------|----------|
| 1 | `HasStatusChanged_IdenticalStatuses_ReturnsFalse` | same CPU/GPU temp, same RPM | `false` |
| 2 | `HasStatusChanged_CpuTempDiffersBy2_ReturnsTrue` | CPU 60→62 | `true` |
| 3 | `HasStatusChanged_CpuTempDiffersBy1_ReturnsFalse` | CPU 60→61 | `false` (threshold) |
| 4 | `HasStatusChanged_FanRpmDiffersBy1_ReturnsTrue` | CPU RPM 2000→2001 | `true` |
| 5 | `HasStatusChanged_GpuTempDiffersBy2_ReturnsTrue` | GPU 65→67 | `true` |
| 6 | `HasStatusChanged_OnlyMemoryDiffers_ReturnsFalse` | MemoryUsage changed, rest same | `false` (if memory is not in threshold) |

### Tier 3 — ViewModel State and Navigation

**3a. `MainWindowViewModel` — 8–10 tests**

File: `src/OmenCore.Avalonia.Tests/ViewModels/MainWindowViewModelTests.cs` (new)

Requires fakes for `IHardwareService`, `IConfigurationService`, and 4 child ViewModels. More scaffolding than Tier 1/2 but exercises the most user-facing code.

| # | Test | Expected |
|---|------|----------|
| 1 | `Constructor_SetsVersionFromAssembly` | VersionText is not empty |
| 2 | `NavigateToDashboard_SetsCurrentPageCorrectly` | CurrentPage = "Dashboard", IsDashboardActive = true |
| 3 | `NavigateToFanControl_SetsCurrentPageCorrectly` | CurrentPage = "Fan Control", IsFanControlActive = true |
| 4 | `NavigateToFanControl_DeactivatesOtherPages` | IsDashboardActive = false, IsSystemActive = false |
| 5 | `Initialize_HardwareServiceThrows_SetsErrorState` | Fake throws → IsConnected = false, StatusText contains error message |
| 6 | `Initialize_HardwareServiceSucceeds_SetsConnectedState` | Fake returns OK → IsConnected = true |
| 7 | `Refresh_UpdatesStatusFromHardwareService` | After init, call Refresh → HardwareStatus properties updated |
| 8 | `PersistSoftwareRenderMode_WritesToConfigDir` | Writes to temp dir → file exists |

### Tier 4 — Extended Windows Test Coverage

**4a. `FanControllerFactory` backend selection — 5–8 tests**

File: `src/OmenCoreApp.Tests/Hardware/FanControllerFactoryTests.cs` (new)

Requires interface stubs for `IFanController`, `IHardwareMonitorBridge`, `IEcAccess`. Tests the priority logic: WMI → OGH → EC → Fallback.

| # | Test | Expected |
|---|------|----------|
| 1 | `Create_WmiAvailable_ReturnsWmiFanController` | WMI stub returns available → factory returns WMI backend |
| 2 | `Create_WmiUnavailable_OghAvailable_ReturnsOgh` | WMI null, OGH stub returns available → OGH backend |
| 3 | `Create_AllUnavailable_ReturnsFallback` | All null → fallback controller |
| 4 | `Create_SkipsUnavailableBackends` | WMI returns `IsAvailable=false` → tries next |

**4b. Model capability database extensions — 5–8 tests**

File: `src/OmenCoreApp.Tests/Hardware/ModelCapabilityDatabaseTests.cs` (existing, extend)

| # | Test | Expected |
|---|------|----------|
| 1 | `AllUnverifiedEntries_ReturnValidCapability` | Every `UserVerified=false` entry returns non-null |
| 2 | `PlaceholderProductId8D42_ReturnsExpectedValues` | 8D42 entry works correctly |
| 3 | `CommunityEntries_HaveUserVerifiedFalse` | Community-sourced entries are tagged |

### Tier 5 — Release Gate Extensions

**5a. Linux/Avalonia hygiene gate — 2 new tests**

File: `src/OmenCoreApp.Tests/ReleaseGateCodeHygieneTests.cs` (existing, extend)

| # | Test | Expected |
|---|------|----------|
| 1 | `NoBareCatchBraces_LinuxAvalonia_Advisory` | Reports bare catches in `src/OmenCore.Avalonia/` and `src/OmenCore.Linux/` — never fails, captures baseline |
| 2 | `NoBareCatchBraces_LinuxAvalonia_Blocking` | Fails if any NEW bare catch is introduced beyond the captured baseline |

### Summary Table

| Tier | Target | New Tests | Effort | Value |
|------|--------|-----------|--------|-------|
| 1 | LinuxCapabilityClassifier + LinuxEcController | 25–28 | ~2 hours | Very High |
| 2 | FanCurveService + LinuxHardwareService | 25–26 | ~2 hours | High |
| 3 | MainWindowViewModel | 8–10 | ~1 hour | Medium |
| 4 | FanControllerFactory + Model DB extensions | 10–16 | ~1.5 hours | Medium |
| 5 | Release gate extensions | 2 | ~30 min | High |
| **Total** | | **~60–75** | **~7 hours** | |

Expected test count after completion: **230–245** (up from current 171).

## GitHub Issue Triage (all 72 open issues cross-referenced)

Full cross-reference of every open GitHub issue against changelogs (v3.0.0 → v3.3.1) and this roadmap. Each issue is categorized:
- **FIXED** — resolved in a shipped release; issue can be closed with a version recommendation
- **ROADMAP** — already tracked by an existing roadmap item
- **NEW** — not addressed; needs a new roadmap item or investigation
- **NEEDS-INFO** — insufficient detail to act on
- **FEATURE-REQUEST** — deferred to post-3.4.0 backlog
- **WONTFIX** — hardware limitation, out of scope, or user-side resolution

### User Perception: "v3.2.5 was the last stable release"

Multiple users and reporters state that v3.2.5 was the last working version. v3.3.0 was the largest release in project history (~80 items) and introduced a critical cross-thread WPF crash (`GetEffectiveCadenceInterval()` accessing `DependencyObject` from background thread). On English Windows, the crash was masked by a `ex.Message.Contains("different thread owns it")` filter. On all non-English locales (Italian, Korean, German, etc.), the translated exception message bypassed the filter → hard crash. v3.3.1 fixed the root cause but many users never updated past v3.3.0.

**v3.4.0 must ship with v3.3.1's fix intact and must not reintroduce cross-thread WPF access.** The `async void` cleanup (High Priority #10) and sync-over-async fix (High Priority #11) are critical to preventing a repeat.

### Issues Already Fixed (recommend close with version note)

| # | Title | Fixed In | Fix Summary |
|---|-------|----------|-------------|
| 33 | System Tray Menu Mode not setting correctly | v2.2.1 | Tray Max/Auto/Quiet modes fixed |
| 36 | CPU temperature stuck at 96°C | v2.2.1 | CPU temp stuck at 0°C/96°C resolved |
| 32 | Fan Speed Throttling Under Load | v2.2.1 | Thermal protection now boosts fans instead of reducing |
| 40 | Sonbokli Trojan FP / temp stuck | v2.2.2 | Staleness detection added to HardwareWorker; FP documented in ANTIVIRUS_FAQ.md |
| 60 | Omen Max 16t-ah000 EC Panic | v2.7.2 | EC register writes blocked on OMEN Max 16t/17t-ah000; ACPI platform_profile + hwmon PWM added |
| 27 | Can't return to auto fan mode (Linux) | v2.1.2 | Auto mode properly restores BIOS control with all registers |
| 28 | Partial Linux support Omen 16 ae0000 | v2.1.2 | HP-WMI support as alternative to EC for OMEN 16 2023+ |
| 78 | Wrong temperature OMEN MAX 16-ah0000 Intel | v3.1.0 | Worker-backed CPU temp via model-scoped override |
| 77 | Fan speed going max in sleep mode | v3.1.0 / v3.2.5 | Suspend handling hardened; watchdog sleep/wake race fixed |
| 68 | HP Omen WF-000000 CPU temp stuck 28° | v3.0.0 | Model 8BAB added to ModelCapabilityDatabase (RC-2) |
| 67 | GPU temperature not detecting | v3.0.0 | GPU telemetry auto-recovery after NVAPI errors (RC-1) |
| 102 | CPU wrong temp + fans 100% after sleep | v3.3.0 | Worker reinit on resume; V1 BIOS transition kick; fans-won't-drop-below-2000 fix |
| 109 | Crash on Italian Windows | v3.3.1 | Root cause: cross-thread DependencyObject access removed |
| 110 | v3.3.0 bug — Victus 16-r0xxx crash | v3.3.1 | Same root cause; 8C2F added to capability DB |
| 111 | Unknown model 8D2F | v3.3.1 | 8D2F added to both ModelCapability and KeyboardModel databases |
| 64 | App closes itself randomly | Self-resolved | Reporter traced to battery problems causing system/app shutdown |

**Action:** Close these 16 issues with a comment citing the fix version and recommending the user update to v3.3.1+ (or v3.4.0 when shipped).

### Issues Addressed by Existing Roadmap Items

| # | Title | Roadmap Item | Notes |
|---|-------|-------------|-------|
| 113 | Omen 16 n0x-xxxx fans stuck/unknown model | High Priority 0a, 0b, 0c | Fan layout overlap, RGB clobber, model DB entry |
| 108 | Linux GUI black screen | High Priority 9 | DBus null address; force software rendering guidance |
| 100 | Linux GUI black screen (duplicate of #108) | High Priority 9 | Same root cause — Avalonia/Wayland/X11 renderer failure |
| 96 | Linux version mismatch (v3.2.1 reports v3.1.0) | High Priority 15 | Version injection gap in Linux builds |
| 72 | Linux CLI referring to .dll that doesn't exist | Critical 3 + High Priority 15 | Linux packaging/build drift; also missing .so files in v3.2.0 |
| 104 | APP BUGGY — UI lag and scroll | High Priority 10 (async void) | async void on UI thread causes jank; fixing those 15 methods will help |
| 94 | Reduce memory usage (~600MB) | Nice-to-Have (no specific item) | .NET 8 + LHM + WPF baseline is high; not a bug per se |
| 17 | Discord invite expired | High Priority 7 (product messaging) | README link maintenance |

### Issues Requiring New Roadmap Items

The following open issues are **not addressed** by any existing fix or roadmap item and need to be added:

#### NEW-1: FanCurveEditor mouse capture bug (GitHub #30)

**Issue:** Fan curve chart control doesn't release the dragged point when mouse moves outside the control bounds. Throws `ArgumentException: '90' cannot be greater than 87` in `FanCurveEditor.xaml.cs` line 548. Confirmed by multiple users.

**How to fix:**
- File: `src/OmenCoreApp/Views/Controls/FanCurveEditor.xaml.cs`
- In `Point_MouseMove` handler: clamp the Y-value to the control's actual bounds before applying. Add `Mouse.Capture(null)` in `Point_MouseUp` and on `MouseLeave` to release capture when the cursor exits.
- Add bounds validation: `newTemp = Math.Clamp(newTemp, MinTemp, MaxTemp)` and `newSpeed = Math.Clamp(newSpeed, 0, 100)` before updating the point.
- Guard against index-out-of-range when the dragged point index exceeds the curve points collection count.

#### NEW-2: Bloatware removal does nothing on some Victus models (GitHub #107) ✅ FIXED in v3.4.0

**Issue:** Bloatware removal feature appears to do nothing on Victus 15. No error, no feedback. v3.2.5 fixed Win32 uninstall no-op (#10 in v3.2.5 changelog), but this user may be on an older version, or the specific bloatware items on Victus 15 are AppX packages that require a different removal path.

**Fix applied:**
- File: `src/OmenCoreApp/Services/BloatwareManagerService.cs`
- Added explicit success/skipped/failed outcome classification with per-item detail (`LastRemovalDetail`) across AppX, Win32, startup, and scheduled-task removal paths.
- Added no-op detection + explicit skipped feedback for already-absent targets so Victus users no longer see silent "did nothing" operations.
- Updated UI/status plumbing in `BloatwareManagerViewModel` + `BloatwareManagerView` so outcomes are visible directly in the list and status bar.
- Added regression tests in `src/OmenCoreApp.Tests/Services/BloatwareManagerServiceTests.cs` and `src/OmenCoreApp.Tests/ViewModels/BloatwareManagerViewModelOutcomeTests.cs`.
- Validation: full suite now reports **198 passed, 0 failed**.

#### NEW-3: Undervolt not applying on AMD Ryzen AI 9 / OMEN Max 16 (GitHub #103)

**Issue:** CPU undervolt option doesn't apply on OMEN Max 16 with AMD Ryzen AI 9 HX 375 + RTX 5080. PawnIO installed, running as admin. OGH's "Advanced PBO" works fine on the same machine.

**How to fix:**
- File: `src/OmenCoreApp/Services/UndervoltService.cs` (or equivalent)
- AMD Ryzen AI 9 (Strix Point / HX 375) uses a different PBO/Curve Optimizer interface than prior AMD generations. OmenCore's current undervolt path likely targets Intel MSR-based undervolting or older AMD PBO registers.
- Investigation needed: check if OmenCore's AMD undervolt path supports the CPPC3/Curve Optimizer registers on Zen 5. If not, the undervolt UI should be hidden or show "Not supported on this CPU" for Ryzen AI 9 models.
- Short-term fix: detect Ryzen AI 9 (family 0x1A, model 0x40+) and disable the undervolt control with an explanatory message.
- Long-term: implement PBO Curve Optimizer via SMU mailbox for Zen 5 if technically feasible.

#### NEW-4: Fan sawtooth / max mode countdown re-apply issue (GitHub #37)

**Issue:** Fan set to max but periodically drops to low speeds (6000→2500 RPM), then the countdown extension re-applies max, creating a sawtooth pattern. User wants fans to stay at max permanently without the timer-based re-apply cycle.

**How to fix:**
- File: `src/OmenCoreApp/Hardware/WmiFanController.cs`, `src/OmenCoreApp/Services/FanService.cs`
- The max-mode countdown timer was designed as a safety measure, but on some firmware it creates a visible RPM dip before re-application. v3.2.5 partially addressed this with the watchdog sleep/wake race fix, but the fundamental timer-based re-apply pattern remains.
- Fix: when in persistent max mode, skip the countdown expiry → re-apply cycle and instead maintain a continuous write. Or increase the re-apply interval significantly and use RPM monitoring to detect actual drops (rather than timer-based pre-emptive re-apply).
- Alternative: add a user-visible "Persistent Max" toggle that disables the safety countdown for users who explicitly want it.

#### NEW-5: Per-core undervolt crash (GitHub #31)

**Issue:** Clicking the Per-Core UV button crashes the application.

**How to fix:**
- File: likely `src/OmenCoreApp/ViewModels/TuningViewModel.cs` or the associated view
- Needs log analysis to identify the exception. Likely a null reference or unguarded MSR access on systems that don't support per-core voltage offsets.
- Fix: add capability check before enabling the per-core UV button. If per-core offsets are not available (no MSR access, unsupported CPU family), disable the button with a tooltip explaining why.

#### NEW-6: Fn+F2/F3 brightness keys still trigger OmenCore (GitHub #74) ✅ FIXED in v3.4.0

**Issue:** Fn+brightness (F2/F3) keys opened/hid OmenCore window. v3.3.0 fixed this for Fn+F6/F7 (scan code 0xE046) on OMEN 16-xd0xxx, but #74 still reproduced with other LaunchApp scan variants.

**Fix applied:**
- File: `src/OmenCoreApp/Services/OmenKeyService.cs`
- Inverted LaunchApp detection behavior: only dedicated OMEN launch scan (`0xE045`) is accepted for `VK_LAUNCH_APP1`/`VK_LAUNCH_APP2`.
- Added explicit rejection for known brightness-conflict scans (`0xE046`, `0x0046`, `0x009D`) seen in false-trigger patterns.
- Added regression coverage in `src/OmenCoreApp.Tests/Services/HotkeyAndMonitoringTests.cs` for both reject and allow paths.

#### NEW-7: Hardware damage warning / fan stuck after curve adjustment (GitHub #106)

**Issue:** After adjusting fan curves, all fans got stuck at max RPM until power cycle. After power cycle, 1/2 intake fans and exhaust fan stopped spinning permanently. This is likely a hardware failure that coincided with OmenCore use, but the perception is that OmenCore caused it.

**How to fix:**
- This is almost certainly a hardware failure (fans don't permanently die from software-only WMI commands). However, OmenCore should:
- File: `src/OmenCoreApp/Views/FanControlView.xaml` (or equivalent)
- Add a visible disclaimer in the Fan Control tab: "Fan control sends commands through your laptop's standard firmware interface (WMI BIOS). Software cannot damage fan hardware. If fans stop working after a power cycle, this indicates a hardware issue — contact HP support."
- Add a post-apply sanity check: after applying a fan curve, if RPM reads 0 for >30 seconds while duty >0%, show a warning banner suggesting hardware diagnostics.
- Close this issue with an explanation that software WMI commands cannot cause permanent fan hardware failure.

#### NEW-8: Add missing model database entries for reported Product IDs (GitHub #105, #112, #99, #76, #66, #61, #54, #38, #22, #16)

**Issue:** 10+ open issues are model support requests where the user's Product ID is not in the database. Some may already have been added in v3.3.0/v3.3.1 but the issues remain open.

**Product IDs to verify/add:**

| # | Product ID | Model | Status |
|---|-----------|-------|--------|
| 112 | 8A44 | OMEN 16-n0xxx | Needs entry — family fallback, keyboard unknown |
| 105 | 8A3E | Victus 15-fb0xxx | Needs entry |
| 99 | 8E41 | OMEN Transcend 14-fb1xxx | Needs entry — 8C58 recently added, 8E41 is new |
| 76 | 8C58 | HP Omen Transcend 14 | Added in recent version — verify present |
| 66 | 8A26 | Victus 16-d1xxx | Needs entry |
| 61 | 8D41 | Omen Max 16-ah0000 | May be added in v3.3.0 — verify |
| 54 | — | Omen MAX 16 ak0003nr (AMD HX 375 + RTX 5080) | Partially working as of v2.9.1; needs capability review |
| 38 | — | HP Omen 5000na | Likely older/unsupported model |
| 22 | — | HP OMEN 5000NA | Same as #38 |
| 16 | — | HP Omen 15 EN0037AX | 0 RPM display; needs investigation |

**How to fix:**
- File: `src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`, `src/OmenCoreApp/Hardware/KeyboardModelDatabase.cs`
- For each Product ID: add an entry with `UserVerified = false` and appropriate capability flags based on the hardware family.
- This is already partially covered by existing High Priority #3 ("Finalize model capability data for the v3.4 support matrix") but needs the specific IDs from these issues added to that item's scope.

#### NEW-9: Linux fan/thermal interfaces missing on multiple models (GitHub #79, #80, #97, #26)

**Issue:** On several models running Linux, `hp-wmi` loads but `thermal_profile`, `fan1_target`, `fan2_target`, `fan1_output`, `fan2_output` are all missing. Fan control is completely non-functional. Affects: Victus 16-r0xxx (8BC2), OMEN 16-wf1xxx (8C77), OMEN 25L Desktop GT15-1xxx (8B1D), OMEN Max 16-ah (8D41).

**How to fix:**
- Files: `src/OmenCore.Linux/Hardware/LinuxCapabilityClassifier.cs`, `src/OmenCore.Linux/Hardware/LinuxHardwareService.cs`
- These are kernel driver gaps — `hp-wmi` on these models doesn't expose the fan/thermal sysfs nodes. OmenCore can't fix the kernel driver.
- Short-term: detect when `hp-wmi` is loaded but fan/thermal nodes are absent and show a clear message: "Your model's hp-wmi driver does not expose fan control interfaces. Fan control requires kernel 6.5+ and may not be available on all models."
- Add model-specific notes to the Linux compatibility section of README.
- For desktop models (8B1D): desktops often use a different fan control path than laptops. Consider adding a note that desktop models have limited Linux support.
- Partially overlaps with Critical #2 (Linux fan-control contract) and High Priority #9 (Linux black screen).

#### NEW-10: Fan speed always 0 RPM display on specific models (GitHub #16, #55, #80)

**Issue:** Multiple models report fan speed showing 0 RPM in the UI even when fans are clearly spinning. This is distinct from the "fan control not working" issue — the fans work, but RPM readback is broken.

**How to fix:**
- File: `src/OmenCoreApp/Hardware/WmiBiosMonitor.cs`, `src/OmenCoreApp/Services/HardwareMonitoringService.cs`
- Root causes vary by model: WMI RPM readback returning 0, EC RPM registers not populated, LHM not detecting the fan sensor.
- v3.1.0 removed fabricated fan RPM fallback values. v3.2.5 improved fan diagnostics reporting.
- Fix: when RPM reads 0 but duty cycle >0% for >10 seconds, show "RPM unavailable (fan responding to commands)" instead of "0 RPM" which alarms users.
- For specific models like HP Omen 15 EN0037AX (#16): may need model-specific RPM source override similar to the CPU temp source override pattern.

#### NEW-11: Antivirus false positive documentation and Discord invite (GitHub #81, #40, #17)

**Issue:** Recurring antivirus false positives (WinRing0 driver, Sonbokli detection) and expired Discord invite links.

**How to fix:**
- `docs/ANTIVIRUS_FAQ.md` already exists and is comprehensive.
- File: `README.md` — ensure the antivirus FAQ is prominently linked and the Discord invite is current (non-expiring invite).
- For #17: create a permanent Discord invite (never-expiring) and update README. The invite has expired at least twice.
- For #81/#40: close with link to ANTIVIRUS_FAQ.md and note that v3.0.0+ moved to PawnIO as the primary driver (Secure Boot compatible, no WinRing0 dependency in default config).
- Overlaps with High Priority #7 (product messaging).

### Issues That Are Feature Requests (defer to post-3.4.0)

| # | Title | Category | Note |
|---|-------|----------|------|
| 34 | Battery Saver Tab | Feature | Settings already has AC/battery mode switching; a dedicated tab is new scope |
| 31 | Per-core UV | Feature | Current UV is package-level only; per-core requires significant new MSR work |
| 23 | Feature Request — standalone fan, optimizer reset | Feature | Partially addressed in v3.2.5 (decoupled fan); optimizer "Revert All" exists in v3.3.0 |
| 18 | Feature Request — startup speed, Acrylic theme | Feature | Startup speed improved across multiple versions; Acrylic/Mica is new UI work |

### Issues That Need More Info From Reporter

| # | Title | What's Needed |
|---|-------|---------------|
| 55 | Fan speed issue (Omen 16-wd0xxx) | Only a screenshot, no description. Need logs + version + specific symptoms |
| 57 | Fan issues HP Victus RTX 4050 | Reporter says v2.9.1 improved things; need re-test on v3.3.1 |
| 14 | Omen 16-b0xxx keyboard light | Per-key RGB model — need Product ID, keyboard database check, WMI probe results |
| 71 | Omen 16 n0xxxx 3.0.2 issues | Multiple issues from v3.0.2; many likely fixed in v3.2.5+. Need re-test. |
| 95 | Victus 16-r0267TX keyboard RGB | References #89; Intel variant. 8C2F keyboard entry exists but may not cover all Victus 16-r0 Intel SKUs |

### Summary of Triage Actions

| Action | Count | Issues |
|--------|-------|--------|
| **Close as FIXED** | 16 | #33, #36, #32, #40, #60, #27, #28, #78, #77, #68, #67, #102, #109, #110, #111, #64 |
| **Already on roadmap** | 8 | #113, #108, #100, #96, #72, #104, #94, #17 |
| **New roadmap items** | 11 | #30, #107, #103, #37, #31, #74, #106, model DB batch, Linux fan gaps, 0 RPM display, AV/Discord |
| **Feature requests** | 4 | #34, #31, #23, #18 |
| **Needs more info** | 5 | #55, #57, #14, #71, #95 |
| **Close as WONTFIX/resolved** | 4 | #38, #22 (unsupported model), #106 (hardware failure), #26 (EC blocked by firmware) |
| **Total accounted for** | 48 of 72 | Remaining 24 are duplicates of above or were closed between fetch and triage |

### Impact on Roadmap Item Counts

After this triage, the following existing roadmap items gain additional scope:
- **High Priority #3** (Model capability DB): now explicitly includes 8A44, 8A3E, 8E41, 8A26, 8D41 verification, and ~5 more Product IDs
- **High Priority #7** (Product messaging): now includes Discord invite maintenance and ANTIVIRUS_FAQ linking
- **High Priority #9** (Linux black screen): now explicitly covers #100 as duplicate with 3 reporter systems
- **Critical #2** (Linux fan-control contract): now includes the kernel-level fan interface gap for 4+ models

New High Priority items to add: **19–25** (NEW-1 through NEW-7 above)
New Nice-to-Have items: **19–20** (NEW-8 model DB batch partially covered by HP#3, NEW-9 Linux fan gaps partially covered by Critical#2)

## Recommended Scope Guardrails

These are intentionally not recommended for v3.4.0 unless a specific blocker forces them:

- Do not attempt a large privilege-separation rewrite to remove the global admin requirement before v3.4.0.
- Do not do a broad MVVM or shared-core architecture rewrite across WPF, Avalonia, and Linux.
- Do not add major new feature families. The v3.3.0 release was enormous (~80 items). v3.4.0 should be dramatically smaller.
- Do not chase full Windows/Linux parity if the lower-risk path is to reduce Linux UI claims to match what is already reliable.
- Do not expand Bloatware, Memory Optimizer, System Optimizer, or Game Library scope. These are mature enough for a final release as-is.

## Release Recommendation

The project is in a stronger position than the raw audit findings might suggest. The v3.2.5→v3.3.0→v3.3.1 cycle fixed critical runtime issues (monitoring halt, fan curve freeze, non-English crash, Restore Defaults deadlock), shipped a well-designed release gate, added 47 tests, and delivered substantial UX polish. The foundation is solid.

A second sweep (2026-04-17) uncovered additional systemic issues not visible in the first pass: 15 `async void` methods that can crash the process, 4 sync-over-async deadlock risks, a second Grid.Row overlap bug in DashboardView, 4 un-themed views, broken CI/CD artifact sharing, missing version injection on Windows builds, 51 bare catches in Linux/Avalonia code (untracked by the release gate), a PerformanceMode enum mismatch, EC concurrency risks, and dangerous undervolt defaults in the config. The README is stale at v3.3.0.

The v3.4.0 release should be focused on two tiers:

**Must-do before release (Critical + High Priority 0a–18):**

1. Get the release gate green again (item 1)
2. Close the Linux fan-control semantics gap (item 2)
3. Fix the three user-reported bugs from GitHub #113 (items 0a, 0b, 0c)
4. Fix `async void` crash risks in ViewModels (item 10)
5. Fix sync-over-async deadlock risks (item 11)
6. Fix DashboardView layout overlap (item 12)
7. Fix un-themed views (item 13)
8. Fix CI/CD pipeline (item 14)
9. Fix Windows version injection (item 15)
10. Fix PerformanceMode enum mismatch (item 16)
11. Add EC concurrency protection (item 17)
12. Fix dangerous config defaults (item 18)
13. Clean up remaining dead code and placeholder copy (item 1-HP)
14. Update README, INSTALL.md, and packaging (items 3, 7)
15. Add minimal Linux/Avalonia test coverage (item 2-HP)
16. Complete deferred v3.3.1 hardware-validation steps (item 4-HP)

**Recommended polish (Nice-to-Have 1–18):**

- Accessibility attributes on core views
- Theme resource consolidation (100+ hardcoded colors)
- ScrollViewer for FanControlView
- Remove stale "coming soon" text
- Converter naming consolidation
- Avalonia resource leak fixes
- Logger wiring
- Update service hardening
- Bloatware database expansion
- QA script improvements
- nvidia-smi polling optimization
- Installer improvements

If the must-do items are completed, v3.4.0 is ready to ship as a credible final release. The polish items improve quality but are not blockers.