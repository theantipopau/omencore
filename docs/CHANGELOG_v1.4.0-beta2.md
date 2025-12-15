# OmenCore v1.4.0-beta2 Changelog

**Release Date:** June 2025  
**Type:** Beta Release

## 🎉 What's New

### 🎨 Interactive 4-Zone Keyboard Controls
- **Visual Zone Editor**: Click on any of the 4 keyboard zones to select and edit colors individually
- **Hex Color Input**: Enter precise colors using hex codes (#FF0000, #00FF00, etc.)
- **Quick Presets**: Dropdown with popular color presets including:
  - OMEN Red (#C40000)
  - Dragon Purple (#8B00FF)  
  - Cyber Blue (#00D4FF)
  - Gaming Green (#00FF41)
  - Sunset Orange (#FF6B00)
  - Hot Pink (#FF1493)
  - Ice White (#FFFFFF)
  - Stealth Black (#1A1A1A)
- **Apply Buttons**: "Apply to Keyboard" sends colors to hardware, "All Same Color" applies Zone 1 color to all zones
- **Visual Feedback**: Zone boxes show current colors with selection highlighting

### 🚀 Startup Reliability Improvements
- **StartupSequencer Service**: New centralized startup manager ensures boot-time reliability
  - Priority-ordered task execution
  - Configurable retry logic with exponential backoff
  - Progress tracking for startup operations
  - Handles Windows race conditions gracefully

### 🖼️ Splash Screen
- **Branded Loading Experience**: New OMEN diamond logo splash screen during startup
- **Progress Bar**: Visual progress indicator during initialization
- **Status Messages**: Shows current startup operation
- **Smooth Animations**: Fade in/out transitions

### 🔔 Enhanced Notification System  
- **In-App Notification Center**: New `AddInfo()`, `AddSuccess()`, `AddWarning()`, `AddError()` methods
- **Notification Types**: Support for Info, Success, Warning, Error with appropriate icons
- **Unread Count**: Track and display unread notification count
- **Read/Unread State**: Mark notifications as read
- **Timestamp Tracking**: All notifications include creation time

## 🐛 Bug Fixes

### BUG-6: SSD Sensor 0°C Display Fix
- **Issue**: Storage card displayed 0°C when no SSD temperature sensor was available
- **Fix**: Storage widget now automatically hides when `SsdTemperatureC <= 0`
- **Technical**: Added `IsSsdDataAvailable` property to `MonitoringSample` model

### BUG-7: Overlay Hotkey Registration on Minimized Start
- **Issue**: Overlay hotkey (Ctrl+Shift+O) failed to register when app started minimized to tray
- **Fix**: Implemented retry mechanism with 5 attempts at 2-second intervals
- **Technical**: Added `StartHotkeyRetryTimer()` and `RegisterHotkeyWithHandle()` to `OsdService`

## 📁 Files Changed

### New Files
- `src/OmenCoreApp/Services/StartupSequencer.cs` - Centralized startup manager
- `src/OmenCoreApp/Views/SplashWindow.xaml` - Splash screen UI
- `src/OmenCoreApp/Views/SplashWindow.xaml.cs` - Splash screen code-behind

### Modified Files
- `src/OmenCoreApp/Models/MonitoringSample.cs` - Added `IsSsdDataAvailable` property
- `src/OmenCoreApp/ViewModels/DashboardViewModel.cs` - Added `IsSsdDataAvailable` binding
- `src/OmenCoreApp/Views/DashboardView.xaml` - Storage card visibility binding
- `src/OmenCoreApp/Services/OsdService.cs` - Hotkey retry logic
- `src/OmenCoreApp/Services/HotkeyService.cs` - Retry infrastructure
- `src/OmenCoreApp/ViewModels/LightingViewModel.cs` - 4-zone keyboard properties and commands
- `src/OmenCoreApp/Views/LightingView.xaml` - Interactive keyboard zone UI
- `src/OmenCoreApp/Services/NotificationService.cs` - In-app notification center

## 📊 Build Information

- **Build Configuration:** Release
- **Target Framework:** .NET 8.0
- **Platform:** Windows x64
- **Self-Contained:** Yes

## 📥 Download

**Installer:** `OmenCoreSetup-1.4.0-beta2.exe`  
**SHA256:** `398836F3A5EABCE0BE4BDCF456ACFBD51BABCB5382FBADAB0EAD43F148417D8D`

## 🔄 Upgrade Notes

This is a beta release. Please backup your settings before upgrading. Report any issues on GitHub.

### From v1.4.0-beta1
- Direct upgrade supported
- Settings preserved
- New features available immediately

---

**Full Changelog:** [Compare v1.4.0-beta1...v1.4.0-beta2](https://github.com/yourusername/OmenCore/compare/v1.4.0-beta1...v1.4.0-beta2)
