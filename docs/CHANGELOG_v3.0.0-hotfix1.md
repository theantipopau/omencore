# OmenCore v3.0.0-hotfix1 — Changelog

**Release Date:** 2026-03-03
**Base:** v3.0.0
**Branch:** v3.0.0
**Commit:** fc0df86

---

## 🐛 Bug Fixes

### Startup Error Dialog on First Launch — ConfigurationService Not Registered in DI

**Severity:** High — affects every user launching v3.0.0 for the first time

**Symptom:** An error dialog appeared immediately on opening the app (before the main window loaded). The error was an unhandled `System.InvalidOperationException`.

**Root Cause:**

The Onboarding Wizard (shown on first launch when `FirstRunCompleted = false`) called `_serviceProvider.GetRequiredService<ConfigurationService>()` in `App.OnStartup()`. However, `ConfigurationService` was never registered in `ConfigureServices()` — only `MainViewModel` and `MainWindow` were registered in the DI container.

`App.Configuration` already exists as a live static singleton (`public static ConfigurationService Configuration { get; } = new();`) and is the correct instance to use — the DI call was unnecessary and broken.

A second related issue was found in `InitializeTrayIcon()`: `_serviceProvider?.GetService<ConfigurationService>()` silently returned `null` (no crash due to `GetService` vs `GetRequiredService`), causing `TrayIconService` to be initialized without its config context.

**Fix:**

Both locations now reference `App.Configuration` directly instead of the DI container:

```csharp
// Before (App.xaml.cs ~line 157)
var configService = _serviceProvider.GetRequiredService<ConfigurationService>(); // ← threw
var onboarding = new OnboardingWindow(Configuration.Config, configService);

// After
var onboarding = new OnboardingWindow(Configuration.Config, Configuration);
```

```csharp
// Before (App.xaml.cs ~line 321)
var configService = _serviceProvider?.GetService<ConfigurationService>(); // ← null
_trayIconService = new TrayIconService(_trayIcon, ForceShowMainWindow, () => Shutdown(), configService);

// After
_trayIconService = new TrayIconService(_trayIcon, ForceShowMainWindow, () => Shutdown(), Configuration);
```

**Affected Users:** All users launching v3.0.0 for the first time (i.e., `FirstRunCompleted = false` in their config, which is the case for any fresh install or any user who had not previously run OmenCore).

**Not Affected:** Users who had already launched v3.0.0 at least once and dismissed/completed the Onboarding Wizard — `FirstRunCompleted` is set to `true` after the wizard, bypassing the broken code path on subsequent launches.

---

## ✅ Validation

| Scenario | Result |
|---|---|
| Fresh install, first launch (no existing config) | ✅ No error dialog; Onboarding Wizard opens cleanly |
| Existing user, `FirstRunCompleted = true` | ✅ Unaffected; no code path change |
| TrayIconService config context | ✅ Now receives valid `ConfigurationService` instance |
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
29053D5C60A79C71FB2B892F9835AE066E3CB211316F21C5D0C578B961FF29DB  OmenCoreSetup-3.0.0.exe
5AE6FC781ADB5D0E5DA86C82550179DFBD176191A29EF0DF5C0CCE5134CB5E2B  OmenCore-3.0.0-win-x64.zip
605335229F5C403D915E99184CC20C1A047EB709B6F33817464DF88DAA5858D4  OmenCore-3.0.0-linux-x64.zip
```

---

**Full v3.0.0 changelog:** https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v3.0.0.md

**GitHub:** https://github.com/theantipopau/omencore

**Discord:** https://discord.gg/9WhJdabGk8
