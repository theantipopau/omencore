# OmenCore v2.4.1 - Release Summary

**Release Date:** January 16, 2026  
**Status:** ✅ Production Ready

## Release Artifacts

### Windows
- **OmenCoreSetup-2.4.1.exe** (100.51 MB)
  - Windows installer with all dependencies
  - Self-contained .NET 8.0 runtime
  - Hardware Worker included for crash isolation
  - SHA256: `B5BFFFBD0BA75B1AA27508CDBF6F12B5C7A4A506877484308ABD20BA181AD36F`

- **OmenCore-2.4.1-win-x64.zip** (103.78 MB)
  - Portable version without installer
  - Self-contained executable
  - SHA256: `F6BB04CAF67E45D984BF8D1F852600808326A13682F5A052E131CBF5A91BDC71`

### Linux x86_64
- **OmenCore-2.4.1-linux-x64.zip** (66.24 MB)
  - CLI daemon and Avalonia GUI
  - Self-contained for glibc-based systems
  - SHA256: `E24EB0A8956F62C731488BEE21037F424789B3550BF56A3481CF9CF9AF135947`

### Linux ARM64
- **OmenCore-2.4.1-linux-arm64.zip** (35.80 MB)
  - ARM64 optimized for Raspberry Pi and similar devices
  - CLI daemon and Avalonia GUI
  - SHA256: `56BA1FB1499BAB9854FD083A66494F1D9E96E5D89E783C27C1080EC19BBD53D9`

## Key Improvements

- All critical fixes and roadmap items for v2.4.1 completed (WinRing0 reliability, diagnostic logging, MSI Afterburner detection, RPM source indicator)
- Linux and Windows artifacts built; checksums to be generated and updated in this document

## Build Information
- **Configuration**: Release
- **Framework**: .NET 8.0
- **Build Errors**: 0
- **Build Warnings**: 0

## Installation Instructions

### Windows
1. Download `OmenCoreSetup-2.4.1.exe`
2. Run the installer
3. Follow the setup wizard

**Portable Alternative:**
1. Extract `OmenCore-2.4.1-win-x64.zip`
2. Run `OmenCore.exe`

### Linux
1. Download `OmenCore-2.4.1-linux-x64.zip` (x86_64) or `OmenCore-2.4.1-linux-arm64.zip` (ARM64)
2. Extract the archive
3. Run `./omencore-gui` for GUI or `./omencore-cli` for CLI daemon

---

**Next Steps:**
- Generate SHA256 checksums for artifacts and update this file
- Publish GitHub Release and attach artifacts
- Post announcements (Discord, Reddit)

---

**Ready for production deployment.**
