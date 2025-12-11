# OmenCore v1.0.0.4 Release Notes

## 🎯 Stability Upgrade + Visual Polish

This release focuses on stability improvements, visual refinements, and architectural foundations for future modularity. All changes are backward-compatible with existing configurations.

---

## ✨ Highlights

### 🖼️ **Live System Tray Temperature Badge**
- **32px CPU temperature overlay** on notification icon updates every 2 seconds
- Gradient background with accent ring for visual depth
- Tooltip displays full telemetry (CPU/GPU temps + loads)
- Falls back to static icon if rendering fails

### 🔒 **Enhanced Hardware Safety**
- **EC write allowlist** prevents accidental writes to dangerous registers
  - Blocks: Battery charger (0xFF), VRM control (0x10-0x20), unknown addresses
  - Allows: Fan control (0x44-0x4D), RGB (0xBA-0xBB), performance (0xCE-0xCF)
  - Throws `UnauthorizedAccessException` with detailed message on blocked write

### 📊 **Chart Visual Upgrades**
- **Gridlines** on thermal and load charts with dashed dividers
- **Temperature labels** on Y-axis for thermal charts (e.g., "75°", "50°", "25°")
- **Gradient backgrounds** with subtle rounded corners
- **Refined color palette** - darker backgrounds (#05060A), vibrant accents (#FF005C red, #8C6CFF purple)

### 🛠️ **Architectural Improvements**
- **Sub-ViewModel pattern** with `FanControlViewModel`, `DashboardViewModel`, `LightingViewModel`, `SystemControlViewModel`
  - Reduces `MainViewModel` complexity from monolithic to modular
  - Future: Replace MainWindow inline XAML with UserControl bindings
- **Async peripheral services** with proper factory pattern (`CorsairDeviceService.CreateAsync`)
- **Change detection optimization** - UI updates only when telemetry changes >0.5° or >0.5%

---

## 🐛 Bug Fixes

### Critical Fixes
- **Logging shutdown flush** - Writer thread now joins with 2-second timeout to ensure tail logs are written before exit
- **Cleanup toggle mapping** - "Remove legacy installers" checkbox now correctly binds to `CleanupRemoveLegacyInstallers` option instead of `CleanupStartupEntries`
- **Auto-update safety** - Missing SHA256 hash now returns null with warning instead of crashing, blocks install button with clear messaging
- **Installer version** - Updated `OmenCoreInstaller.iss` to 1.0.0.4 (was incorrectly hardcoded as 1.0.0.3)

### UI/UX Fixes
- **Garbled glyphs** - Replaced mojibake characters (â¬†, âš ) with ASCII "Update" and "!" in MainWindow update banner
- **TrayIconService disposal** - Properly unsubscribes timer event handler to prevent memory leak
- **FanMode backward compatibility** - Defaults to `Auto` for existing configurations without mode property

---

## 🎨 Visual Improvements

### Typography
- **Font family**: Segoe UI Variable Text with fallback to Segoe UI
- **Text rendering**: `TextFormattingMode=Display` for crisp display on HiDPI
- **Weight adjustments**: SemiBold headers, Medium body text

### Color Palette Refresh
```
Backgrounds:  #05060A (BackgroundDark) → #15192B (SurfaceMedium)
Accents:      #FF005C (Red) #8C6CFF (Purple) #45C7FF (Blue)
Text:         #F5F7FF (Primary) → #8D92AA (Tertiary)
Borders:      #2F3448 (30% lighter than before)
```

### UI Components
- **SurfaceCard style** - 12px rounded corners, consistent 20px padding, drop shadows
- **ModernTabItem** - Pill-style tabs with purple underline accent on selection
- **ModernComboBox** - Chevron dropdown icon, enhanced hover states, gradient selection
- **ModernListBox** - Rounded item borders with purple accent on selection
- **DataGrid** - Alternating row colors, horizontal gridlines, refined header styling

---

## 🔧 Technical Changes

### Services
- **HardwareMonitoringService**:
  - Added `_lastSample` field for change detection
  - `ShouldUpdateUI()` method compares deltas against 0.5° threshold
  - Consecutive error counter (max 5) stops monitoring on persistent failures
  - Adaptive polling interval in low overhead mode (+500ms delay)

- **CorsairDeviceService** / **LogitechDeviceService**:
  - Factory pattern with `CreateAsync()` static method
  - Async `DiscoverAsync()`, `ApplyLightingAsync()`, `ApplyDpiStagesAsync()`
  - Null checks and exception handling throughout
  - Implements `IDisposable` with `Shutdown()` call

- **AutoUpdateService**:
  - `ExtractHashFromBody()` regex: `SHA-?256:\s*([a-fA-F0-9]{64})`
  - Download skips when hash missing, returns `null` instead of throwing
  - File deletion on hash mismatch with detailed error logging

### Hardware
- **WinRing0EcAccess**:
  - `AllowedWriteAddresses` static HashSet with 11 approved registers
  - `WriteByte()` validates address before EC write
  - Exception message includes allowed address list for debugging

### ViewModels
- **MainViewModel**:
  - `InitializeServicesAsync()` creates peripheral services asynchronously
  - `InitializeSubViewModels()` wires up sub-ViewModels after services ready
  - Properties: `FanControl`, `Lighting`, `SystemControl`, `Dashboard`
  - Command refactor: Many changed from `RelayCommand` to `AsyncRelayCommand`

### UI Controls
- **ThermalChart** / **LoadChart**:
  - `DrawGridlines()` method with 4 horizontal × 6 vertical dividers
  - Temperature labels with 10px font size, 4px offset
  - `BorderBrush` cloned with 25% opacity for subtle grid

### Tests
- **Unit test stubs** for hardware, services, and peripheral SDKs
  - `WinRing0EcAccessTests` - EC allowlist validation
  - `AutoUpdateServiceTests` - SHA256 verification (updated for null return)
  - `CorsairDeviceServiceTests` - Device discovery with stub provider
  - FluentAssertions + xUnit + Moq stack

---

## 📦 Installation & Upgrade

### Fresh Install
1. Download `OmenCoreSetup-1.0.0.4.exe` from [Releases](https://github.com/theantipopau/omencore/releases/tag/v1.0.0.4)
2. Run installer as Administrator
3. Select "Install WinRing0 driver" task if not already installed
4. Launch from Start Menu or Desktop

### Upgrade from 1.0.0.3
1. **No uninstall required** - installer will upgrade in place
2. **Config preserved** - `%APPDATA%\OmenCore\config.json` carries over
3. **No migrations** - `FanMode` defaults to `Auto` for old presets
4. **Logs retained** - existing log files remain in `%LOCALAPPDATA%\OmenCore\`

### In-App Auto-Update
- Click "Check for Updates" in Settings tab
- If v1.0.0.4 available, click "Install Update"
- **Requires SHA256 hash in release notes** - if missing, manual download required
- Installer runs in background, app restarts on completion

---

## 🔗 SHA256 Checksums

```
OmenCoreSetup-1.0.0.4.exe:
SHA256: [PLACEHOLDER - Generate after build]

OmenCore-1.0.0.4-win-x64.zip:
SHA256: [PLACEHOLDER - Generate after build]
```

**Verify integrity**:
```powershell
Get-FileHash .\OmenCoreSetup-1.0.0.4.exe -Algorithm SHA256
```

---

## 📋 Known Issues

1. **MainWindow UserControl integration incomplete**
   - Sub-ViewModels created but MainWindow still uses inline XAML
   - Planned for v1.1.0: Replace tabs with `<ContentControl Content="{Binding FanControl}"><FanControlView /></ContentControl>`

2. **Peripheral SDK stubs**
   - Corsair iCUE SDK integration incomplete (hardware detection works, lighting/DPI stubs only)
   - Logitech G HUB SDK integration incomplete (hardware detection works, color/DPI stubs only)
   - Planned for v1.1.0: Full iCUE/G HUB SDK with real device control

3. **Test coverage gaps**
   - TrayIconService, chart gridlines, view models lack unit tests
   - Integration tests needed for EC writes, MSR access
   - Planned for v1.0.5: 80% code coverage target

4. **EC address allowlist hardcoded**
   - Current allowlist works for tested OMEN models (15-dh, 16-b, 17-ck)
   - Other models may need different addresses
   - Planned for v1.1.0: Configurable EC address map with hardware detection

---

## 🔮 What's Next (v1.1.0 Roadmap)

- [ ] Complete MainWindow tab refactor with UserControl integration
- [ ] Create LightingView.xaml and SystemControlView.xaml
- [ ] Full Corsair iCUE SDK integration (replace stub)
- [ ] Full Logitech G HUB SDK integration (replace stub)
- [ ] Per-game profile switching (detect game launch, auto-apply profile)
- [ ] Configurable EC address map with hardware presets
- [ ] Macro recording for peripherals
- [ ] Per-key RGB editor (Lighting Studio style)
- [ ] CSV export for telemetry history

---

## 🙏 Acknowledgments

Thanks to the OMEN community for testing, feedback, and EC register documentation.

**Special thanks**:
- RWEverything community for EC exploration tools
- LibreHardwareMonitor maintainers for sensor framework
- GitHub users who reported issues and tested pre-release builds

---

## 📝 Full Changelog

See [CHANGELOG.md](../CHANGELOG.md) for complete version history.

---

**Download**: [OmenCore v1.0.0.4](https://github.com/theantipopau/omencore/releases/tag/v1.0.0.4)

**Report Issues**: [GitHub Issue Tracker](https://github.com/theantipopau/omencore/issues)

**Discussions**: [GitHub Discussions](https://github.com/theantipopau/omencore/discussions)
