# WinRing0 Driver Setup Guide

## ⚠️ IMPORTANT: Antivirus False Positives

**Windows Defender and other antivirus software will flag WinRing0 as malware.** This is a **false positive**.

### Why This Happens:
- WinRing0 provides direct kernel-level hardware access (CPU MSRs, EC registers, I/O ports)
- This same functionality can be abused by malware to hide processes or modify system behavior
- Antivirus heuristics detect the low-level capabilities and flag it as potentially unwanted

### Is It Safe?
**Yes, when using trusted sources:**
- ✅ **LibreHardwareMonitor's WinRing0**: Signed by LibreHardwareMonitor project, widely used in monitoring tools
- ✅ **Official WinRing0 builds**: From the original OpenLibSys project
- ❌ **Random downloads**: Never download WinRing0 from untrusted sources

### How to Proceed:
1. **Add Exclusion in Windows Defender:**
   - Open Windows Security → Virus & threat protection → Manage settings
   - Scroll to Exclusions → Add an exclusion → Folder
   - Add: `C:\Windows\System32\drivers\WinRing0x64.sys`
   - Add: LibreHardwareMonitor installation folder

2. **Verify File Authenticity:**
   ```powershell
   # Check digital signature (should show LibreHardwareMonitor or Noriyuki MIYAZAKI)
   Get-AuthenticodeSignature "C:\Windows\System32\drivers\WinRing0x64.sys"
   ```

3. **Alternative**: Use test signing mode for custom-built drivers (development only)

### Known Detection Names:
- Windows Defender: `HackTool:Win64/WinRing0`
- Other AVs: May report as `PUA:Win32/WinRing0` or similar

**This is expected behavior. The driver itself is legitimate hardware monitoring software.**

---

## Current Status
- ✅ EC access interface implemented (`WinRing0EcAccess.cs`)
- ✅ Safety allowlist for EC addresses in place
- ✅ Fan controller ready (`FanController.cs`)
- ✅ Automatic driver detection on startup
- ❌ Driver files not bundled
- ❌ Automatic driver installation not implemented

## What You Need

### Option 1: Use Existing WinRing0/LibreHardwareMonitor Driver
LibreHardwareMonitor includes a signed WinRing0 driver that OmenCore can use.

**Steps:**
1. Download [LibreHardwareMonitor v0.9.4+](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases)
2. Extract and locate `WinRing0x64.sys` from the package
3. Copy to `C:\Windows\System32\drivers\`
4. The driver auto-loads when LibreHardwareMonitor runs with admin rights

**Advantages:**
- Already signed
- Actively maintained
- Community tested

**Disadvantages:**
- Requires LibreHardwareMonitor to be installed
- OmenCore doesn't auto-install it

### Option 2: Bundle Custom EC Driver
Create a minimal KMDF driver specifically for OmenCore.

**Required Files:**
```
drivers/
  OmenEcAccess/
    OmenEcAccess.sys     (signed kernel driver)
    OmenEcAccess.inf     (device installation)
    OmenEcAccess.cat     (catalog file)
```

**Implementation Needed:**
1. Build driver from WinRing0 source (strip unnecessary features)
2. Sign with EV certificate (required for Windows 11)
3. Add installer logic to `App.xaml.cs` startup
4. Use `pnputil.exe` to install driver if missing

### Option 3: Development Mode (Test Signing)
For development only - enables unsigned drivers.

**Enable Test Signing:**
```powershell
# Run as Administrator
bcdedit /set testsigning on
# Reboot required
```

**Deploy Driver:**
```powershell
pnputil /add-driver OmenEcAccess.inf /install
```

## Auto-Detection Logic

Add to `App.xaml.cs` startup:

```csharp
private bool CheckWinRing0Driver()
{
    var devicePath = "\\\\.\\WinRing0_1_2";
    var handle = CreateFile(devicePath, 0, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    
    if (handle.IsInvalid)
    {
        _logging.Warn("WinRing0 driver not detected");
        return false;
    }
    
    handle.Dispose();
    _logging.Info("WinRing0 driver detected and accessible");
    return true;
}

private async Task InstallDriverIfMissing()
{
    if (CheckWinRing0Driver())
        return;
        
    var result = MessageBox.Show(
        "Fan control requires WinRing0 driver. Install now?",
        "Driver Required",
        MessageBoxButton.YesNo);
        
    if (result == MessageBoxResult.Yes)
    {
        // Run pnputil to install bundled driver
        await InstallBundledDriver();
    }
}
```

## Safety Considerations

### Current Allowlist (HP Omen 17-ck2xxx)
```csharp
0x44-0x4D  // Fan control registers
0xBA-0xBB  // Keyboard backlight
0xCE-0xCF  // Performance modes
```

### Adding New Addresses
1. **Research**: Check EC datasheets or reverse engineer OEM software
2. **Test on sacrificial hardware**: NEVER test on your main laptop first
3. **Verify with monitoring**: Use RWEverything to confirm register behavior
4. **Document**: Add comments explaining what each address controls

### Hardware Risks
- ❌ **NEVER** write to addresses outside allowlist
- ❌ Writing to VRM registers can **permanently damage** hardware
- ❌ Incorrect fan control can overheat and destroy CPU/GPU
- ✅ Start with read-only testing
- ✅ Validate fan curves on old/backup hardware first

## Next Steps

1. **Short term**: Document manual driver installation for users
2. **Medium term**: Bundle LibreHardwareMonitor's WinRing0 driver
3. **Long term**: Build custom minimal EC-only driver
4. **Production**: Get EV code signing certificate for Windows 11

## Current Workaround

Users must manually install LibreHardwareMonitor or enable WinRing0 driver. OmenCore detects availability at startup and gracefully disables fan control if unavailable.

Log message: `"EC bridge not available; fan writes disabled"`
