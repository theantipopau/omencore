# OmenCore v2.5.0 - Advanced RGB Lighting & Hardware Monitoring 🎨📊

**Major Release Highlights:**

## 🎨 Advanced RGB Lighting System
- **Temperature-Responsive Lighting**: Keyboard and RGB devices change colors based on CPU/GPU temps with configurable thresholds
- **Performance Mode Sync**: RGB lighting automatically syncs with Performance/Balanced/Silent modes
- **Throttling Indicators**: Flashing red lighting alerts when thermal throttling is detected
- **6 New Lighting Presets**: Wave Blue/Red, Breathing Green, Reactive Purple, Spectrum Flow, Audio Reactive
- **Multi-Vendor Support**: HP OMEN, Corsair, Logitech, and Razer devices with unified control

## 📊 Hardware Monitoring Enhancements
- **Power Consumption Tracking**: Real-time power monitoring with efficiency metrics and trend analysis
- **Battery Health Monitoring**: Comprehensive battery assessment with wear level and cycle count tracking
- **Live Fan Curve Visualization**: Interactive charts showing temperature vs fan speed relationships
- **Real-time Current Metrics**: Live CPU/GPU temps, power consumption, battery health, and efficiency metrics

## 🛡️ Critical Fixes
- **Fan Auto-Control Restoration**: Fans now properly return to BIOS control when app closes (no more fans staying at high RPM!)
- **OMEN Key Fix**: Fixed app popping up on brightness keys (Fn+F2/F3) - added settings toggle to disable if needed
- **Victus 16 Stability**: Enhanced stuck sensor detection, worker robustness, and fan control retry logic
- **Monitoring Tab Fixes**: Resolved empty tables and data display issues

## 🔧 Technical Improvements
- **Power Limit Verification**: Reads back EC registers to verify power limits applied successfully
- **Enhanced Diagnostics**: Better logging and conflict detection for troubleshooting
- **GPU Power Boost Integration**: Combined WMI BIOS + NVAPI control for accurate power management
- **Fan Control Hardening**: Multi-level retry logic with verification and enhanced logging

## 📦 Downloads
- **Windows Installer**: `OmenCoreSetup-2.5.0.exe`
- **Windows Portable**: `OmenCore-2.5.0-win-x64.zip`
- **Linux Portable**: `OmenCore-2.5.0-linux-x64.zip`

**SHA256 Hashes available in changelog for verification**

---

**Migration**: No breaking changes from v2.4.x - existing settings and profiles are compatible.

**Known Issues**: Windows Defender false positive (use PawnIO for Secure Boot), MSI Afterburner conflicts (detection added).

Full changelog: https://github.com/theantipopau/omencore/blob/main/docs/CHANGELOG_v2.5.0.md</content>
<parameter name="filePath">f:\Omen\discord_post_v2.5.0.md