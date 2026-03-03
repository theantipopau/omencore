# OmenCore v3.0.0-hotfix2 — Changelog

**Release Date:** 2026-03-03
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
| Build (0 errors / 0 warnings) | ✅ Clean |

---

## 📦 Downloads

| File | Description |
|---|---|
| `OmenCoreSetup-3.0.0.exe` | **Windows installer (recommended)** |
| `OmenCore-3.0.0-win-x64.zip` | Windows portable |
| `OmenCore-3.0.0-linux-x64.zip` | Linux portable (CLI + Avalonia GUI) |

### SHA256 Checksums
```
C3C6DD6F9A4E8001114B7AE0603FFD0B04330297EBAA86176387FF3BE7044BEA  OmenCoreSetup-3.0.0.exe
DFC7A1D3EB12C35492B1BAA56E156D43A22BF37EF53CCDDC0BC9CCCDFBC01E0D  OmenCore-3.0.0-win-x64.zip
605335229F5C403D915E99184CC20C1A047EB709B6F33817464DF88DAA5858D4  OmenCore-3.0.0-linux-x64.zip
```

---

**Full v3.0.0 changelog:** https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.0.md

**GitHub:** https://github.com/theantipopau/omencore

**Discord:** https://discord.gg/9WhJdabGk8
