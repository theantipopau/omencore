# Antivirus False Positives FAQ

## Why does my antivirus flag OmenCore?

OmenCore uses low-level hardware drivers to control your laptop's fan curves, LED lighting, and advanced CPU/GPU settings. These drivers require kernel-level access, which is similar to how some malware operates - causing antivirus software to flag them as potentially dangerous.

### Affected Components

| Component | Purpose | Why Flagged |
|-----------|---------|-------------|
| **PawnIO** | Secure hardware access for Secure Boot systems | Kernel driver for MSR/EC access |
| **WinRing0 (Legacy/Optional)** | Legacy fallback backend on limited systems | Kernel driver for MSR/SMBus |
| **LibreHardwareMonitorLib** | Temperature/sensor monitoring | Low-level hardware polling |

## Is OmenCore Safe?

**Yes.** OmenCore is open-source software. You can review the complete source code at:
- https://github.com/theantipopau/omencore

The drivers used are:
- **PawnIO**: The primary and recommended backend for advanced EC/MSR access (Secure Boot compatible)
- **WinRing0**: A legacy optional backend that may be present on some systems/configurations

## How to Whitelist OmenCore

### Windows Defender

1. Open **Windows Security** (Win + I → Privacy & Security → Windows Security)
2. Click **Virus & threat protection**
3. Scroll down and click **Manage settings** under "Virus & threat protection settings"
4. Scroll down to **Exclusions** and click **Add or remove exclusions**
5. Click **Add an exclusion** → **Folder**
6. Browse to `C:\Program Files\OmenCore` and select it

### Windows SmartScreen

If you see "Windows protected your PC" when running the installer:

1. Click **More info**
2. Click **Run anyway**

This warning appears because the installer is not code-signed with an Extended Validation (EV) certificate ($500+/year).

### Windows 11 Smart App Control

**Smart App Control** (Windows 11 22H2+) is more restrictive than SmartScreen and may block OmenCore entirely with no "Run anyway" option.

**If Smart App Control is blocking OmenCore:**

1. **Option 1: Temporarily disable Smart App Control**
   - Open **Windows Security** → **App & browser control** → **Smart App Control settings**
   - Set to **Off** (Note: This cannot be re-enabled without reinstalling Windows)
   
2. **Option 2: Use Evaluation Mode**
   - If Smart App Control is in "Evaluation" mode, it may allow OmenCore after learning it's safe
   
3. **Option 3: Download from Microsoft Store** (coming soon)
   - Microsoft Store apps bypass Smart App Control

**Why is this happening?**
Smart App Control uses cloud reputation to block apps without extensive download history. As a niche utility for HP OMEN laptops, OmenCore doesn't yet have the download volume to build reputation. This affects many legitimate open-source tools.

### Other Antivirus Software

Most antivirus programs have similar whitelist/exclusion features:

- **Avast/AVG**: Settings → General → Exceptions → Add Exception
- **Bitdefender**: Protection → Antivirus → Settings → Exclusions
- **Kaspersky**: Settings → Additional → Threats and Exclusions → Exclusions
- **Norton**: Settings → Antivirus → Scans and Risks → Exclusions
- **ESET**: Setup → Advanced Setup → Antivirus → Exclusions

Add the following paths to your exclusions:
```
C:\Program Files\OmenCore\
C:\Program Files\OmenCore\OmenCore.exe
C:\Program Files\OmenCore\drivers\
```

## VirusTotal Analysis

OmenCore binaries are periodically scanned on VirusTotal. Detection rates vary by vendor, but most major antivirus products correctly identify it as safe after analysis.

Common false positive detections:
- `Generic.Trojan.Malware` - Heuristic detection of kernel driver behavior
- `Riskware.WinRing0` - Legacy WinRing0 artifact detected as potentially unwanted
- `HackTool.Win32` - MSR access flagged as "hacking tool"
- `Trojan.Gen` / `Trojan detected` - Kernel driver / low-level hardware access (GitHub #90)
- `Gen:Application.Venus.Cynthia.Winring` - Bitdefender heuristic for WinRing0-adjacent kernel drivers (see below)

These are **false positives** caused by the legitimate low-level hardware access required for fan/thermal control.

---

### Bitdefender: `Gen:Application.Venus.Cynthia.Winring`

**Detection name**: `Gen:Application.Venus.Cynthia.Winring.17ay1@auVvKTci` (exact hash suffix varies by version)

This is a heuristic rule Bitdefender applies to any kernel driver that resembles WinRing0 / PawnIO. OmenCore's `OmenCore.sys` (PawnIO) is flagged because:
- It uses MSR and I/O port access at ring-0, which is the same pattern used by WinRing0 cheats/cryptominers
- Bitdefender's `Venus.Cynthia.Winring` family targets the **access pattern**, not the binary itself

**This is safe.** PawnIO is an open-source kernel driver with full source at https://github.com/ekknod/EC.

#### Bitdefender Exclusion Steps

1. Open **Bitdefender Security** (system tray icon → Open)
2. Click **Protection** in the left sidebar
3. Click **Antivirus** → **Settings**
4. Scroll to **Exclusions** and click **Manage Exclusions**
5. Click **+ Add an Exclusion** → **File/Folder**
6. Browse to and select: `C:\Program Files\OmenCore\drivers\PawnIO\`
7. Click **Save**

Alternatively, add these individual files to exclusions:
```
C:\Program Files\OmenCore\drivers\PawnIO\PawnIO.sys
C:\Program Files\OmenCore\drivers\PawnIO\PawnIO.exe
C:\Program Files\OmenCore\OmenCore.HardwareWorker.exe
```

**Reporting to Bitdefender**: Submit a false positive report at https://www.bitdefender.com/submit/ — attach the flagged `.sys` file and describe it as a signed kernel driver for laptop hardware access.

---

## Building from Source

If you're concerned about pre-built binaries, you can build OmenCore yourself:

1. Install .NET 8 SDK
2. Clone the repository: `git clone https://github.com/theantipopau/omencore`
3. Build: `dotnet build -c Release`
4. The compiled binaries will be in `src/OmenCoreApp/bin/Release/net8.0-windows10.0.19041.0/win-x64/`

## Reporting False Positives

Help us by reporting false positives to your antivirus vendor:

- **Microsoft Defender**: https://www.microsoft.com/wdsi/filesubmission
- **VirusTotal**: Mark as safe when viewing scan results
- **Avast**: https://www.avast.com/false-positive-file-form.php
- **ESET**: https://support.eset.com/en/kb141-submit-a-malware-sample-to-the-eset-research-lab

When submitting, explain that OmenCore is:
- Open-source laptop control software
- Uses kernel drivers for legitimate hardware access (fan control, LED control)
- Similar to other system utilities like HWiNFO, CPU-Z, ThrottleStop

## Technical Details

### Why Kernel Drivers?

OmenCore requires kernel-level access because:

1. **Fan Control**: HP OMEN laptops use Embedded Controller (EC) registers to control fans. These registers are only accessible from kernel mode.

2. **CPU Undervolt**: Voltage offsets are applied via Model Specific Registers (MSRs), which require ring-0 (kernel) access.

3. **Temperature Monitoring**: Some sensors are only accessible through direct hardware polling.

### PawnIO vs WinRing0

| Feature | PawnIO | WinRing0 |
|---------|--------|----------|
| Secure Boot | ✅ Compatible | ❌ Blocked |
| Signing | Microsoft WHQL | Self-signed |
| AV Detection | Lower | Higher |
| Development | Active | Abandoned |

OmenCore defaults to PawnIO and WMI-based paths. WinRing0 is legacy/optional and not required for core operation.

---

*Last updated: December 2025*
