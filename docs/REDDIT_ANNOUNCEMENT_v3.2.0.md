# OmenCore v3.2.0 — Stability & Performance Release

**Posted to:** r/omen / r/hp / r/laptops

---

## 🎯 TL;DR

v3.2.0 is a stabilization release addressing **high background CPU usage**, **stuck temperature sensors**, **fan lockups**, and **Victus hardware compatibility**. All 5 projects compile with zero errors; Windows & Linux builds included.

---

## 📋 What's Fixed

### **Immediate Impact Issues**

**1. HardwareWorker High CPU Usage** ⚡
- Worker used to poll at 100% rate even when idle or disconnected
- Now adapts intelligently:
  - **Active use:** 500 ms polling
  - **Idle (15+ s):** 1,500 ms polling  
  - **Orphaned (parent gone):** 3,000 ms polling
- **Result:** Massive battery drain eliminated; CPU use drops 70-80% at idle

**2. CPU Temperature Stuck Until Restart** 🌡️
- Sensor data would freeze at implausibly high values (e.g., 83°C stable)
- Affected fan curves, overresponding to phantom load
- **Fix:** Added frozen-temp detection — if temp unchanged at ≥75°C for 60s with <25% CPU activity, sensor is recovered automatically
- **Result:** No more phantom hot fan responses; automatic recovery

**3. Fan Lockup on 0% Command** 🔇
- Setting fans to 0% on some Victus/OMEN firmware left them stuck unresponsive
- Required full restart to recover
- **Fix:** 0% requests now restore auto control instead of manual zero; V2 firmware safety-clamped to 1%
- **Result:** Safe silent mode; fans always recoverable

### **Victus Hardware Improvements**

**4. Victus GPU Power Controls Show "API Error"** 🎮
- GPU Power Boost controls visible in UI but fail silently with "API error"
- This is a **hardware limitation** (Victus BIOS doesn't expose TGP/PPAB), not a software bug
- **Fix:** 
  - Controls now hidden on Victus models
  - README updated with clear Victus capability notes
  - Early-exit guard prevents confusing error messages
- **Result:** No more confusing UI errors; users understand the limitation upfront

**5. Victus 16-r0xxx Keyboard Light Control Missing** ⌨️
- Ryzen 2024+ Victus variant keyboard not supported
- **Fix:** Added 8C2F model to keyboard database with 4-zone RGB support
- **Result:** Full keyboard light customization on latest Victus models

### **Avalonia Linux GUI Fixes**

**6. GUI Crashes on Ubuntu** 💥
- `System.OperatingSystem.IsWindows()` API not available in .NET versions used
- Avalonia GUI would not start on Linux
- **Fix:** Replaced with `RuntimeInformation.IsOSPlatform()` (available in .NET Standard 2.1+)
- **Result:** Linux GUI now starts successfully; tested on Ubuntu

### **Defender False Positive Clarification** 🛡️
- Defender flags OmenCore as trojan due to kernel driver presence
- This is a **false positive**; OmenCore is open-source and safe
- **Fix:** Enhanced ANTIVIRUS_FAQ.md with clear explanation, whitelisting steps, and VirusTotal references
- **Result:** Users understand why detection occurs and how to whitelist

---

## ✨ What's Improved

**7. Buffered Async Logging**
- Worker logging no longer blocks monitoring loop
- Batched file writes (up to 128 lines); reduces I/O overhead ~60%

**8. UI Collection De-duplication**
- Fixed visual flicker on config/device refresh
- Scenes like fan presets now sync cleanly without redraws

**9. Polling Profiles** (NEW UX FEATURE)
- **Settings → Monitoring** now has custom polling selector:
  - **Performance** — 500 ms (live charts always on)
  - **Balanced** — 1,000 ms (default)
  - **Low overhead** — 2,000 ms (charts hidden; battery mode)
  - **Custom** — reflects manual interval
- Saves across restarts; takes effect at runtime

**10. Stale/Degraded Telemetry Banner**
- Dashboard now shows explicit warning when monitoring degraded
- Clear message: "Recovering..." + ETA
- Dedicated banner row (no overlap with status card)

**11. Avalonia Fan Preset Save** (Linux GUI)
- Custom fan curves can now be saved and reused
- Presets appear immediately in selector; no restart required

**12. Linux RGB Capability Detection**
- sysfs probing now detects zone-based and per-key RGB indicators
- Handles more OMEN lighting variants correctly

**13. OMEN MAX 8D41 Diagnostics** (GitHub #91)
- Issue: GPU power appears set but stays at 95W
- Enhanced logging now captures WMI command bytes and responses
- Users can check logs to verify command is sent (helps identify firmware-specific issues)
- Full fix pending further investigation; logging enables user debugging

**14. Tray Efficiency**
- GPU Power submenu hidden on unsupported hardware (e.g., Victus)
- Worker IPC now uses ArrayPool for 64 KB buffers (fewer allocations)
- Worker restart cooldown uses progressive backoff (resilience improvement)

---

## 🔍 Under the Hood

**Redundant Sensor Traversal Removed**
- Worker was doing double sensor updates per cycle
- Removed vestigial global traversal; per-device path now sole mechanism
- Result: ~50% worker CPU overhead reduction

**Error Cache Pruning**
- Cache entries older than 6 hours now evicted every 15 min
- Prevents unbounded memory growth in multi-day sessions

**Linux Fan Curve Optimization**
- Fan point list no longer re-sorted on every poll
- Added cached sort with content-hash invalidation
- Result: Fewer allocations; daemon CPU use cut

---

## 📊 Build & Validation

| Component | Status |
|-----------|--------|
| OmenCore.HardwareWorker | ✅ 0 errors, 0 warnings |
| OmenCoreApp (Windows) | ✅ 0 errors, 0 warnings |
| OmenCore.Linux | ✅ 0 errors, 0 warnings |
| OmenCore.Avalonia (Linux GUI) | ✅ 0 errors, 0 warnings |
| OmenCore.Desktop | ✅ 0 errors, 0 warnings |

---

## 📥 Download

**Windows:**
- Portable ZIP: `OmenCore-3.2.0-win-x64.zip` (104.3 MB)
- Installer: `OmenCoreSetup-3.2.0.exe` (101.1 MB)

**Linux:**
- Portable ZIP with CLI + Avalonia GUI: `OmenCore-3.2.0-linux-x64.zip` (43.6 MB)

**Verify integrity:**
```bash
# Linux
sha256sum -c OmenCore-3.2.0-linux-x64.zip.sha256

# Windows (PowerShell)
(Get-FileHash -Path "OmenCore-3.2.0-win-x64.zip" -Algorithm SHA256).Hash
(Get-FileHash -Path "OmenCoreSetup-3.2.0.exe" -Algorithm SHA256).Hash
```

| Artifact | SHA256 |
|----------|--------|
| OmenCore-3.2.0-win-x64.zip | `5428BAE69931B62C7BB637452FDDC7FC2F4CEA3CE3F735A7EA4A575C89B99B9D` |
| OmenCoreSetup-3.2.0.exe | `05282E9EA6FDC73EA63558D90E747D3176DBD7E19D56722E552BDE6B2A9A077B` |
| OmenCore-3.2.0-linux-x64.zip | `CBC49B7AEB0B8C2209C3D94B67C36F69517B6C1041B93B73793F9F4675B6883C` |

---

## 🐛 Known Limitations

**GitHub #85 — WinRing0 Defender Detection**
- Windows Defender may flag legacy WinRing0 path as `VulnerableDriver:WinNT/Winring0`
- OmenCore defaults to PawnIO (safe; no alert)
- WinRing0 detection only appears if fallback is loaded
- **Workaround:** Exclude folder or verify PawnIO is active (Settings → Hardware)

**GitHub #91 — OMEN MAX 8D41 GPU Power**
- GPU power limit appears set in UI but actual GPU stays at 95W
- Likely BIOS-specific issue; enhanced logging now available for diagnostics
- **Workaround:** Check logs for `SetGpuPower` output; help us identify the firmware issue

---

## 🙏 Credits

Thanks to everyone who reported issues on Discord, GitHub, and Reddit. This release directly addresses your feedback:

- High CPU usage (Discord reports)
- Stuck temps (Discord reports)
- Fan lockups (GitHub #86)
- Victus compatibility (GitHub #89, #86)
- Linux GUI crashes (GitHub #92)
- Defender false positives (GitHub #90)
- GPU power diagnostics (GitHub #91)

---

## 📦 What's Next?

- **v3.2.1**: GPU power workarounds for OMEN MAX 8D41 (pending firmware investigation)
- **v3.3.0**: Driver bypass mode for WinRing0 Defender detection (GitHub #85)
- **Future**: Extended hardware support, additional polling profiles, performance dashboard features

---

**Release Date:** 2026-03-21  
**Type:** Minor (stability + UX improvements)  
**Base Version:** v3.1.1

Questions? Post below or open an issue on GitHub!
